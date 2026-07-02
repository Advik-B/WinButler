using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinButler.Services.Mft;

/// <summary>
/// Win32 P/Invoke for raw NTFS volume access — opening <c>\\.\C:</c>, querying the
/// NTFS volume layout, and reading arbitrary byte ranges off the raw device. Mirrors
/// the <c>[DllImport]</c> + <see cref="SafeFileHandle"/> + <see cref="Marshal.GetLastWin32Error"/>
/// conventions used in <see cref="WinButler.Services.Junction"/>. Requires elevation
/// (the app manifest already requests it).
/// </summary>
internal static class NtfsNative
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    // FSCTL_GET_NTFS_VOLUME_DATA — returns the NTFS_VOLUME_DATA_BUFFER for the volume.
    private const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, int nInBufferSize,
        byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    /// <summary>The slice of NTFS_VOLUME_DATA_BUFFER we actually need.</summary>
    internal readonly record struct VolumeData(
        uint BytesPerSector,
        uint BytesPerCluster,
        uint BytesPerFileRecordSegment,
        long TotalClusters,
        long MftValidDataLength,
        long MftStartLcn);

    /// <summary>
    /// Opens a raw read handle to a volume by drive letter (e.g. <c>'C'</c>). The caller
    /// owns the returned handle. Throws if the open fails (usually access denied without
    /// elevation, or the drive isn't a fixed volume).
    /// </summary>
    public static SafeFileHandle OpenVolume(char driveLetter)
    {
        // \\.\C:  — note: no trailing backslash, that's what selects the raw volume.
        string path = $@"\\.\{char.ToUpperInvariant(driveLetter)}:";
        var handle = CreateFile(
            path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"Could not open volume {path} (error {Marshal.GetLastWin32Error()}).");

        return handle;
    }

    /// <summary>Queries the NTFS layout (sector/cluster sizes, MFT location).</summary>
    public static VolumeData GetVolumeData(SafeFileHandle volume)
    {
        // NTFS_VOLUME_DATA_BUFFER plus the extended NTFS_EXTENDED_VOLUME_DATA fits well under 256 bytes.
        var buffer = new byte[256];
        if (!DeviceIoControl(volume, FSCTL_GET_NTFS_VOLUME_DATA, IntPtr.Zero, 0,
                buffer, buffer.Length, out _, IntPtr.Zero))
        {
            throw new IOException(
                $"FSCTL_GET_NTFS_VOLUME_DATA failed (error {Marshal.GetLastWin32Error()}). " +
                "The volume is likely not NTFS.");
        }

        // NTFS_VOLUME_DATA_BUFFER field offsets (LARGE_INTEGER = 8 bytes, DWORD = 4):
        //   0  VolumeSerialNumber   8  NumberSectors   16 TotalClusters   24 FreeClusters
        //   32 TotalReserved        40 BytesPerSector  44 BytesPerCluster 48 BytesPerFileRecordSegment
        //   52 ClustersPerFileRecordSegment            56 MftValidDataLength   64 MftStartLcn
        var span = buffer.AsSpan();
        long totalClusters = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(span.Slice(16));
        uint bytesPerSector = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(40));
        uint bytesPerCluster = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(44));
        uint bytesPerFrs = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(48));
        long mftValidDataLength = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(span.Slice(56));
        long mftStartLcn = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(span.Slice(64));

        return new VolumeData(bytesPerSector, bytesPerCluster, bytesPerFrs, totalClusters, mftValidDataLength, mftStartLcn);
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes from absolute byte <paramref name="offset"/> into
    /// <paramref name="buffer"/>. On a raw volume handle the offset and length must be
    /// sector-aligned; callers read whole clusters, which satisfies that. Returns bytes read.
    /// </summary>
    public static int ReadAt(SafeFileHandle volume, long offset, byte[] buffer, int count)
    {
        const uint FILE_BEGIN = 0;
        if (!SetFilePointerEx(volume, offset, out _, FILE_BEGIN))
            throw new IOException($"Seek to {offset} failed (error {Marshal.GetLastWin32Error()}).");

        if (!ReadFile(volume, buffer, count, out int read, IntPtr.Zero))
            throw new IOException($"Read of {count} bytes at {offset} failed (error {Marshal.GetLastWin32Error()}).");

        return read;
    }
}
