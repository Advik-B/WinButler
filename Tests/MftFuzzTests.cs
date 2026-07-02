using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinButler.Services.Mft;
using Xunit;

namespace WinButler.Tests;

/// <summary>
/// Fault-injection tests for the MFT binary parser — every on-disk structure is untrusted
/// input. The invariants: <c>ParseRecord</c> NEVER throws (malformed records leave the default
/// InUse=false entry); <c>ApplyUsaFixup</c> rejects inconsistent USA headers instead of indexing
/// out of bounds; the record-0 parser throws <see cref="IOException"/> (the degrade-to-walk
/// signal), never an out-of-range crash; and the data-run decoder stops at corruption.
/// </summary>
public sealed class MftFuzzTests
{
    private const int RecLen = 1024;
    private const int AttrStart = 0x38;

    // ---- synthetic FILE record builder -------------------------------------------------

    /// <summary>A minimal well-formed in-use FILE record shell (valid USA, attributes at 0x38).</summary>
    private static byte[] RecordShell(ushort flags = 0x0001)
    {
        var rec = new byte[RecLen];
        rec[0] = (byte)'F'; rec[1] = (byte)'I'; rec[2] = (byte)'L'; rec[3] = (byte)'E';
        W16(rec, 4, 0x30);          // USA offset
        W16(rec, 6, 3);             // USA count: 1 USN + one word per 512-byte sector
        W16(rec, 20, AttrStart);    // first attribute offset
        W16(rec, 22, flags);        // 0x01 in-use, 0x02 directory
        // Base-record reference at 0x20 stays 0 (this is a base record).
        W16(rec, 0x30, 0xAAAA);     // USN
        W16(rec, 0x32, 0x1111);     // true tail of sector 1
        W16(rec, 0x34, 0x2222);     // true tail of sector 2
        W16(rec, 510, 0xAAAA);      // sector tails carry the USN on disk
        W16(rec, 1022, 0xAAAA);
        return rec;
    }

    private static int AddFileName(byte[] rec, int pos, string name, ulong parent = 5, byte ns = 1)
    {
        int valueLen = 0x42 + name.Length * 2;
        int attrLen = Align8(0x18 + valueLen);
        W32(rec, pos, 0x30);                 // $FILE_NAME
        W32(rec, pos + 4, (uint)attrLen);
        rec[pos + 8] = 0;                    // resident
        W16(rec, pos + 0x14, 0x18);          // value offset
        int v = pos + 0x18;
        W64(rec, v, parent);                 // parent reference (low 48 bits = record no)
        rec[v + 0x40] = (byte)name.Length;
        rec[v + 0x41] = ns;                  // 1 = Win32 namespace
        Encoding.Unicode.GetBytes(name).CopyTo(rec.AsSpan(v + 0x42));
        return pos + attrLen;
    }

    private static int AddResidentData(byte[] rec, int pos, uint size)
    {
        W32(rec, pos, 0x80);                 // $DATA
        W32(rec, pos + 4, 0x18);
        rec[pos + 8] = 0;                    // resident
        W32(rec, pos + 0x10, size);          // value length == file size
        return pos + 0x18;
    }

    private static int AddNonResidentData(byte[] rec, int pos, long real, long alloc, ushort runsOffset = 0x40)
    {
        const int attrLen = 0x48;            // 0x40 header + 8 bytes of (empty) runs
        W32(rec, pos, 0x80);                 // $DATA
        W32(rec, pos + 4, attrLen);
        rec[pos + 8] = 1;                    // non-resident
        W16(rec, pos + 0x20, runsOffset);
        W64(rec, pos + 0x28, (ulong)alloc);
        W64(rec, pos + 0x30, (ulong)real);
        return pos + attrLen;
    }

    private static void AddEnd(byte[] rec, int pos) => W32(rec, pos, 0xFFFFFFFF);

    private static byte[] ValidFileRecord(out int dataAttrPos)
    {
        var rec = RecordShell();
        int pos = AddFileName(rec, AttrStart, "hello.txt");
        dataAttrPos = pos;
        pos = AddResidentData(rec, pos, 1234);
        AddEnd(rec, pos);
        return rec;
    }

    private static MftEntry Parse(byte[] rec)
    {
        MftEntry entry = default;
        MftReader.ParseRecord(rec, 42, ref entry, new Dictionary<uint, (long real, long alloc)>());
        return entry;
    }

    // ---- builder sanity ------------------------------------------------------------------

    [Fact]
    public void Valid_synthetic_record_parses_name_parent_and_size()
    {
        var entry = Parse(ValidFileRecord(out _));

        Assert.True(entry.InUse);
        Assert.False(entry.IsDirectory);
        Assert.Equal("hello.txt", entry.Name);
        Assert.Equal(5u, entry.ParentRecordNo);
        Assert.Equal(1234, entry.RealSize);
    }

    [Fact]
    public void Nonresident_data_sizes_are_read_from_the_header()
    {
        var rec = RecordShell();
        int pos = AddFileName(rec, AttrStart, "big.bin");
        pos = AddNonResidentData(rec, pos, real: 5_000_000_000, alloc: 5_000_002_048);
        AddEnd(rec, pos);

        var entry = Parse(rec);

        Assert.Equal(5_000_000_000, entry.RealSize);
        Assert.Equal(5_000_002_048, entry.AllocSize);
    }

    // ---- malformed records must be skipped, never thrown on -------------------------------

    [Fact]
    public void Zeroed_and_BAAD_records_leave_the_default_entry()
    {
        Assert.False(Parse(new byte[RecLen]).InUse);

        var baad = ValidFileRecord(out _);
        baad[0] = (byte)'B'; baad[1] = (byte)'A'; baad[2] = (byte)'A'; baad[3] = (byte)'D';
        Assert.False(Parse(baad).InUse);
    }

    [Theory]
    [InlineData(514)]    // sectors=513 → stride 1024/513 = 1 → would index rec[-1]
    [InlineData(600)]    // 1024 % 599 != 0 → inconsistent geometry
    [InlineData(65535)]  // sectors=65534 → stride 0 → would index rec[-2]
    public void Usa_fixup_rejects_inconsistent_sector_counts(int usaCount)
    {
        var rec = ValidFileRecord(out _);
        W16(rec, 6, (ushort)usaCount);

        Assert.False(MftReader.ApplyUsaFixup(rec)); // must reject, not crash
        Assert.False(Parse(rec).InUse);             // and the record is skipped
    }

    [Fact]
    public void Usa_fixup_rejects_an_array_that_overruns_the_record()
    {
        var rec = ValidFileRecord(out _);
        W16(rec, 4, 1020); // USA of 3 words starting at 1020 runs past 1024

        Assert.False(MftReader.ApplyUsaFixup(rec));
    }

    [Fact]
    public void Usa_fixup_still_restores_valid_records()
    {
        var rec = ValidFileRecord(out _);

        Assert.True(MftReader.ApplyUsaFixup(rec));
        Assert.Equal(0x1111, BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(510)));
        Assert.Equal(0x2222, BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(1022)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(0x17)]        // below the minimum well-formed attribute size
    [InlineData(0x7FFFFFFF)]  // huge positive
    [InlineData(-16)]         // negative when read as int
    public void Corrupt_attribute_length_never_throws(int badLen)
    {
        var rec = ValidFileRecord(out int dataAttrPos);
        W32(rec, dataAttrPos + 4, unchecked((uint)badLen));

        Parse(rec); // walking the chain must stop cleanly, never index out of bounds
    }

    [Fact]
    public void Truncated_nonresident_data_reports_zero_instead_of_reading_past_it()
    {
        // A non-resident $DATA whose attrLen (0x20) is too short to contain the 0x28/0x30
        // size fields — the parser must skip the size read, not run past the attribute.
        var rec = RecordShell();
        int pos = AddFileName(rec, AttrStart, "trunc.bin");
        W32(rec, pos, 0x80);
        W32(rec, pos + 4, 0x20);
        rec[pos + 8] = 1;
        AddEnd(rec, pos + 0x20);

        var entry = Parse(rec);

        Assert.True(entry.InUse);
        Assert.Equal(0, entry.RealSize);
    }

    [Fact]
    public void Filename_value_offset_pointing_past_the_record_is_ignored()
    {
        var rec = ValidFileRecord(out _);
        W16(rec, AttrStart + 0x14, 0x3C0); // value would start at ~byte 1016 — no room for 0x42

        var entry = Parse(rec);

        Assert.True(entry.InUse);
        Assert.Equal(string.Empty, entry.Name); // no usable name, but no crash
    }

    [Fact]
    public void Byte_flip_fuzz_never_throws()
    {
        // Deterministic fuzz: random byte-flips over every region of a valid record, including
        // the USA header, attribute chain, and base-record reference (extension-record path).
        var rng = new Random(20260702);
        var pristine = ValidFileRecord(out _);

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            var rec = (byte[])pristine.Clone();
            int flips = 1 + rng.Next(16);
            for (int i = 0; i < flips; i++)
                rec[rng.Next(RecLen)] = (byte)rng.Next(256);

            Parse(rec); // any exception fails the test
        }
    }

    // ---- record 0 (the $MFT's own map): malformed input throws IOException, nothing else --

    [Fact]
    public void Record0_without_FILE_signature_throws_IOException()
    {
        Assert.Throws<IOException>(() => MftReader.ParseMftSelfDataRuns(new byte[RecLen]));
    }

    [Theory]
    [InlineData(0x10)]   // before the non-resident header ends
    [InlineData(0x48)]   // at/after the end of the attribute
    [InlineData(0xFFF)]  // way outside
    public void Record0_with_bad_runs_offset_throws_IOException(int runsOffset)
    {
        var rec = RecordShell();
        int pos = AddNonResidentData(rec, AttrStart, real: 0, alloc: 0, runsOffset: (ushort)runsOffset);
        AddEnd(rec, pos);

        Assert.Throws<IOException>(() => MftReader.ParseMftSelfDataRuns(rec));
    }

    [Fact]
    public void Record0_with_no_data_attribute_throws_IOException()
    {
        var rec = RecordShell();
        int pos = AddFileName(rec, AttrStart, "$MFT");
        AddEnd(rec, pos);

        Assert.Throws<IOException>(() => MftReader.ParseMftSelfDataRuns(rec));
    }

    // ---- data-run decoder: corruption terminates the list, never crashes ------------------

    [Fact]
    public void Data_runs_with_oversized_count_fields_stop_cleanly()
    {
        // Header 0x9F claims a 15-byte length field — impossible; decoding must stop.
        byte[] runs = { 0x9F, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        Assert.Empty(MftReader.DecodeDataRuns(runs));
    }

    [Fact]
    public void Data_runs_truncated_mid_run_stop_cleanly()
    {
        byte[] runs = { 0x21, 0x18 }; // header claims 1+2 more bytes; only 1 present
        Assert.Empty(MftReader.DecodeDataRuns(runs));
    }

    [Fact]
    public void Data_runs_with_negative_cluster_count_stop_cleanly()
    {
        // 8-byte length with the top bit set reads negative — corrupt, not a real extent.
        byte[] runs = { 0x18, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x00 };
        Assert.Empty(MftReader.DecodeDataRuns(runs));
    }

    // ---- little-endian writers -------------------------------------------------------------

    private static int Align8(int v) => (v + 7) & ~7;
    private static void W16(byte[] b, int off, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(off), v);
    private static void W32(byte[] b, int off, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(off), v);
    private static void W64(byte[] b, int off, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(off), v);
}
