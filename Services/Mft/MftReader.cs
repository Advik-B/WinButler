using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace WinButler.Services.Mft;

/// <summary>
/// One parsed MFT FILE record, flattened to just what a space analyzer needs. This is the
/// boundary between the binary parsing (<see cref="MftReader"/>) and the tree aggregation
/// (<see cref="MftTreeBuilder"/>) — deliberately a dumb data bag with no tree links.
/// </summary>
public readonly record struct MftEntry(
    uint RecordNo,
    uint ParentRecordNo,
    string Name,
    bool IsDirectory,
    long RealSize,
    long AllocSize,
    long ModifiedTicks,
    bool InUse);

/// <summary>
/// Reads the raw NTFS <c>$MFT</c> and parses every in-use FILE record — the technique that
/// makes WizTree fast (one big sequential read instead of millions of <c>FindNextFile</c>
/// calls). Output is a flat <see cref="MftEntry"/> array indexed by record number; turning
/// that into a tree is <see cref="MftTreeBuilder"/>'s job.
///
/// Behaviour notes / v1 limitations:
///  • Heavily fragmented files whose unnamed <c>$DATA</c> overflows into an extension record
///    (via <c>$ATTRIBUTE_LIST</c>, 0x20) are recovered: the VCN-0 size from the extension is
///    credited back to the base record. The rare unhandled case is a stream so fragmented that
///    even its size-bearing VCN-0 fragment isn't a plain non-resident $DATA.
///  • Hardlinks: a file is counted once (per MFT record), attributed to its first Win32 name —
///    so totals can read lower than WizTree, which counts every hardlink name separately.
/// </summary>
public sealed class MftReader
{
    // NTFS attribute type codes.
    private const uint ATTR_STANDARD_INFORMATION = 0x10;
    private const uint ATTR_ATTRIBUTE_LIST = 0x20;
    private const uint ATTR_FILE_NAME = 0x30;
    private const uint ATTR_DATA = 0x80;
    private const uint ATTR_END = 0xFFFFFFFF;

    private const ushort FLAG_IN_USE = 0x0001;
    private const ushort FLAG_DIRECTORY = 0x0002;

    private const byte NAMESPACE_DOS = 2; // 8.3 short name — skip in favour of the Win32 name.

    // The 48-bit record-number mask of an 8-byte MFT file reference (high 16 bits = sequence).
    private const ulong RecordNumberMask = 0x0000_FFFF_FFFF_FFFF;

    /// <summary>
    /// Reads and parses the entire MFT of the given drive. <paramref name="progress"/> is
    /// invoked occasionally with (recordsParsed, totalRecords). Throws if the volume can't be
    /// opened or isn't NTFS — callers fall back to a recursive walk in that case.
    /// </summary>
    public MftEntry[] Read(char driveLetter, Action<long, long>? progress = null, CancellationToken ct = default)
    {
        using var volume = NtfsNative.OpenVolume(driveLetter);
        var vd = NtfsNative.GetVolumeData(volume);

        int frs = (int)vd.BytesPerFileRecordSegment;     // file record size, typically 1024
        int cluster = (int)vd.BytesPerCluster;
        if (frs <= 0 || cluster <= 0)
            throw new IOException("Unexpected NTFS geometry (zero record/cluster size).");

        long mftByteOffset = vd.MftStartLcn * cluster;

        // 1) Read FILE record 0 (the $MFT itself) and decode its own $DATA runs — these tell us
        //    where every other MFT record physically lives (the MFT is usually fragmented).
        int firstChunk = RoundUpToCluster(Math.Max(cluster, frs), cluster);
        var rec0 = new byte[firstChunk];
        NtfsNative.ReadAt(volume, mftByteOffset, rec0, firstChunk);
        var runs = ParseMftSelfDataRuns(rec0.AsSpan(0, frs));

        long recordCount = vd.MftValidDataLength / frs;
        if (recordCount <= 0)
            throw new IOException("MFT reports zero valid records.");
        var entries = new MftEntry[recordCount];

        // 2) Stream the MFT extents in large, cluster-aligned chunks and parse each FILE record.
        int chunkBytes = RoundUpToCluster(4 * 1024 * 1024, cluster);
        var buffer = new byte[chunkBytes];

        // Sizes recovered from extension records (heavily fragmented files whose whole $DATA was
        // evicted from the base record), keyed by base record number — applied after the sweep.
        var extensionSizes = new Dictionary<uint, (long real, long alloc)>();

        long recordIndex = 0;
        foreach (var (lcn, clusterCount) in runs)
        {
            if (recordIndex >= recordCount) break;
            if (lcn < 0) continue; // sparse run — never happens for $MFT, but be safe.

            long extentBytes = clusterCount * (long)cluster;
            long extentOffset = lcn * (long)cluster;

            for (long pos = 0; pos < extentBytes && recordIndex < recordCount; )
            {
                ct.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(chunkBytes, extentBytes - pos);
                int got = NtfsNative.ReadAt(volume, extentOffset + pos, buffer, toRead);
                if (got <= 0) break;

                for (int off = 0; off + frs <= got && recordIndex < recordCount; off += frs)
                {
                    ParseRecord(buffer.AsSpan(off, frs), (uint)recordIndex, vd, ref entries[recordIndex], extensionSizes);
                    recordIndex++;

                    if ((recordIndex & 0xFFFF) == 0)
                        progress?.Invoke(recordIndex, recordCount);
                }

                pos += got;
            }
        }

        // Credit recovered extension-record sizes to their base records (only where the base
        // itself reported no size, i.e. its $DATA had fully overflowed into the extension).
        foreach (var (baseRecord, size) in extensionSizes)
        {
            if (baseRecord < entries.Length &&
                entries[baseRecord].InUse && !entries[baseRecord].IsDirectory &&
                entries[baseRecord].RealSize == 0)
            {
                entries[baseRecord] = entries[baseRecord] with { RealSize = size.real, AllocSize = size.alloc };
            }
        }

        progress?.Invoke(recordCount, recordCount);
        return entries;
    }

    // ---- FILE record parsing -------------------------------------------------------------

    private static void ParseRecord(
        Span<byte> rec, uint recordNo, NtfsNative.VolumeData vd, ref MftEntry entry,
        Dictionary<uint, (long real, long alloc)> extensionSizes)
    {
        // Records that aren't "FILE" (zeroed, or "BAAD") leave the default entry (InUse = false).
        if (rec[0] != (byte)'F' || rec[1] != (byte)'I' || rec[2] != (byte)'L' || rec[3] != (byte)'E')
            return;

        ApplyUsaFixup(rec);

        ushort flags = U16(rec, 22);
        if ((flags & FLAG_IN_USE) == 0)
            return; // deleted record — skip.

        // Extension records (BaseFileRecordSegment != 0) hold overflow attributes of a fragmented
        // base file. We don't count them as files (that would double-count), but if one carries the
        // file's entire unnamed $DATA (VCN 0) we stash its size to credit back to the base record.
        uint baseRecord = (uint)(U64(rec, 0x20) & RecordNumberMask);
        if (baseRecord != 0)
        {
            CollectExtensionDataSize(rec, baseRecord, extensionSizes);
            return;
        }

        bool isDir = (flags & FLAG_DIRECTORY) != 0;
        int attrOff = U16(rec, 20);

        string? winName = null; uint winParent = 0; bool haveWin = false;
        string? anyName = null; uint anyParent = 0; bool haveAny = false;
        long realSize = 0, allocSize = 0, modified = 0;

        int pos = attrOff;
        while (pos + 8 <= rec.Length)
        {
            uint type = U32(rec, pos);
            if (type == ATTR_END)
                break;

            int attrLen = (int)U32(rec, pos + 4);
            if (attrLen <= 0 || pos + attrLen > rec.Length)
                break;

            byte nonResident = rec[pos + 8];
            byte nameLen = rec[pos + 9];

            switch (type)
            {
                case ATTR_STANDARD_INFORMATION:
                    if (nonResident == 0)
                    {
                        int v = pos + U16(rec, pos + 0x14);
                        if (v + 0x10 <= rec.Length)
                            modified = I64(rec, v + 0x08); // last data-change FILETIME
                    }
                    break;

                case ATTR_FILE_NAME:
                    if (nonResident == 0)
                    {
                        int v = pos + U16(rec, pos + 0x14);
                        if (v + 0x42 <= rec.Length)
                        {
                            uint parent = (uint)(U64(rec, v + 0x00) & RecordNumberMask);
                            byte fnLen = rec[v + 0x40];
                            byte ns = rec[v + 0x41];
                            int nameStart = v + 0x42;
                            if (nameStart + fnLen * 2 <= rec.Length)
                            {
                                string nm = Encoding.Unicode.GetString(rec.Slice(nameStart, fnLen * 2));
                                if (ns != NAMESPACE_DOS && !haveWin)
                                {
                                    winName = nm; winParent = parent; haveWin = true;
                                }
                                if (!haveAny)
                                {
                                    anyName = nm; anyParent = parent; haveAny = true;
                                }
                            }
                        }
                    }
                    break;

                case ATTR_DATA:
                    if (nameLen == 0 && !isDir) // unnamed default stream of a file
                    {
                        if (nonResident == 0)
                        {
                            // Resident data lives inside the MFT record — no extra clusters on disk.
                            realSize = U32(rec, pos + 0x10);
                            allocSize = realSize;
                        }
                        else
                        {
                            allocSize = I64(rec, pos + 0x28); // AllocatedSize (cluster-rounded)
                            realSize = I64(rec, pos + 0x30);  // RealSize (actual bytes)
                        }
                    }
                    break;

                // ATTR_ATTRIBUTE_LIST: $DATA may live in an extension record we don't follow (v1
                // limitation). We leave size 0 rather than guess — see class remarks.
            }

            pos += attrLen;
        }

        string name = haveWin ? winName! : (haveAny ? anyName! : string.Empty);
        uint parentNo = haveWin ? winParent : anyParent;

        entry = new MftEntry(
            RecordNo: recordNo,
            ParentRecordNo: parentNo,
            Name: name,
            IsDirectory: isDir,
            RealSize: isDir ? 0 : realSize,
            AllocSize: isDir ? 0 : allocSize,
            ModifiedTicks: modified,
            InUse: true);
    }

    /// <summary>
    /// From an already-fixed-up extension record, captures the size of an unnamed non-resident
    /// $DATA that begins at VCN 0 (the whole stream lives here), keyed by the base record so the
    /// caller can restore the size the base record couldn't hold.
    /// </summary>
    private static void CollectExtensionDataSize(Span<byte> rec, uint baseRecord, Dictionary<uint, (long real, long alloc)> sizes)
    {
        int pos = U16(rec, 20);
        while (pos + 8 <= rec.Length)
        {
            uint type = U32(rec, pos);
            if (type == ATTR_END)
                break;

            int attrLen = (int)U32(rec, pos + 4);
            if (attrLen <= 0 || pos + attrLen > rec.Length)
                break;

            if (type == ATTR_DATA && rec[pos + 9] == 0 && rec[pos + 8] == 1) // unnamed, non-resident
            {
                long startVcn = I64(rec, pos + 0x10);
                if (startVcn == 0)
                {
                    long real = I64(rec, pos + 0x30);
                    if (real > 0 && !sizes.ContainsKey(baseRecord))
                        sizes[baseRecord] = (real, I64(rec, pos + 0x28));
                }
            }

            pos += attrLen;
        }
    }

    /// <summary>
    /// Applies the NTFS Update Sequence Array fix-up: the last two bytes of every sector in the
    /// record were swapped out for the update-sequence number when written, and must be restored
    /// from the fix-up array before the record is parseable. Skipping this corrupts any field that
    /// straddles a sector boundary.
    /// </summary>
    internal static void ApplyUsaFixup(Span<byte> rec)
    {
        int usaOffset = U16(rec, 4);
        int usaCount = U16(rec, 6); // 1 update-sequence number + one fix-up word per sector
        int sectors = usaCount - 1;
        if (sectors <= 0)
            return;

        int stride = rec.Length / sectors; // self-consistent regardless of physical sector size
        for (int k = 1; k <= sectors; k++)
        {
            int sectorEnd = k * stride - 2;
            int usaPos = usaOffset + k * 2;
            if (sectorEnd + 2 > rec.Length || usaPos + 2 > rec.Length)
                break;
            rec[sectorEnd] = rec[usaPos];
            rec[sectorEnd + 1] = rec[usaPos + 1];
        }
    }

    /// <summary>Finds record 0's non-resident unnamed $DATA and decodes its data runs.</summary>
    private static List<(long lcn, long count)> ParseMftSelfDataRuns(Span<byte> rec0)
    {
        if (rec0[0] != (byte)'F' || rec0[1] != (byte)'I' || rec0[2] != (byte)'L' || rec0[3] != (byte)'E')
            throw new IOException("$MFT record 0 has no FILE signature.");

        ApplyUsaFixup(rec0);

        int pos = U16(rec0, 20);
        while (pos + 8 <= rec0.Length)
        {
            uint type = U32(rec0, pos);
            if (type == ATTR_END)
                break;

            int attrLen = (int)U32(rec0, pos + 4);
            if (attrLen <= 0 || pos + attrLen > rec0.Length)
                break;

            byte nonResident = rec0[pos + 8];
            byte nameLen = rec0[pos + 9];

            if (type == ATTR_DATA && nameLen == 0 && nonResident == 1)
            {
                int runsOffset = U16(rec0, pos + 0x20);
                return DecodeDataRuns(rec0.Slice(pos + runsOffset, attrLen - runsOffset));
            }

            pos += attrLen;
        }

        throw new IOException("Could not locate the $MFT data runs in record 0.");
    }

    /// <summary>
    /// Decodes an NTFS data-run list into (LCN, clusterCount) extents. Each run is a header byte
    /// (low nibble = #length bytes, high nibble = #offset bytes) followed by a little-endian
    /// unsigned length and a little-endian <em>signed</em> LCN delta relative to the previous run.
    /// </summary>
    internal static List<(long lcn, long count)> DecodeDataRuns(ReadOnlySpan<byte> runs)
    {
        var result = new List<(long, long)>();
        long lcn = 0;
        int i = 0;
        while (i < runs.Length)
        {
            byte header = runs[i++];
            if (header == 0)
                break;

            int lenBytes = header & 0x0F;
            int offBytes = (header >> 4) & 0x0F;
            if (lenBytes == 0 || i + lenBytes + offBytes > runs.Length)
                break;

            long length = ReadVarUnsigned(runs.Slice(i, lenBytes));
            i += lenBytes;

            if (offBytes == 0)
            {
                // Sparse run (no LCN) — not expected for $MFT; record it as a hole.
                result.Add((-1, length));
                continue;
            }

            lcn += ReadVarSigned(runs.Slice(i, offBytes));
            i += offBytes;
            result.Add((lcn, length));
        }
        return result;
    }

    private static long ReadVarUnsigned(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        for (int i = 0; i < bytes.Length; i++)
            value |= (long)bytes[i] << (8 * i);
        return value;
    }

    private static long ReadVarSigned(ReadOnlySpan<byte> bytes)
    {
        long value = ReadVarUnsigned(bytes);
        int bits = bytes.Length * 8;
        // Sign-extend from the top bit of the most-significant byte.
        if (bits < 64 && (value & (1L << (bits - 1))) != 0)
            value |= -1L << bits;
        return value;
    }

    private static int RoundUpToCluster(long value, long cluster)
        => (int)(((value + cluster - 1) / cluster) * cluster);

    private static ushort U16(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(off));
    private static uint U32(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(off));
    private static long I64(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadInt64LittleEndian(s.Slice(off));
    private static ulong U64(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(off));
}
