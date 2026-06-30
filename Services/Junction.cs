using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinButler.Services;

/// <summary>
/// Creates, detects and removes NTFS directory junctions (mount points). Junctions work
/// cross-volume and need no elevation/Developer Mode, which is why we use them rather than
/// symlinks (<see cref="Directory.CreateSymbolicLink"/> would create a symlink — wrong type).
/// </summary>
public static class Junction
{
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_SHARE_READ_WRITE_DELETE = 0x00000007;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    /// <summary>True if <paramref name="path"/> is a reparse point (junction or symlink).</summary>
    public static bool IsJunction(string path)
    {
        try
        {
            return Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The target a junction/symlink resolves to, or null if not a link.</summary>
    public static string? GetTarget(string path)
    {
        try { return Directory.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName; }
        catch { return null; }
    }

    /// <summary>
    /// Creates a junction at <paramref name="source"/> pointing to <paramref name="target"/>.
    /// The source must not already exist; the target must exist.
    /// </summary>
    public static void Create(string source, string target)
    {
        target = Path.GetFullPath(target);
        if (!Directory.Exists(target))
            throw new DirectoryNotFoundException($"Junction target does not exist: {target}");
        if (Directory.Exists(source) || File.Exists(source))
            throw new IOException($"Junction source already exists: {source}");

        // A junction is an empty directory tagged with a reparse point.
        Directory.CreateDirectory(source);

        try
        {
            using var handle = CreateFile(
                source, GENERIC_WRITE, FILE_SHARE_READ_WRITE_DELETE, IntPtr.Zero,
                OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);

            if (handle.IsInvalid)
                throw new IOException($"Could not open junction source (error {Marshal.GetLastWin32Error()}).");

            var buffer = BuildReparseBuffer(target);
            if (!DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, buffer, buffer.Length,
                    IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                throw new IOException($"Failed to set reparse point (error {Marshal.GetLastWin32Error()}).");
            }
        }
        catch
        {
            // Roll back the empty directory we created if tagging failed.
            try { Directory.Delete(source, recursive: false); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Removes a junction WITHOUT following it into the target. Throws if the path is not a junction
    /// (guards against ever recursively deleting real data).
    /// </summary>
    public static void Remove(string source)
    {
        if (!IsJunction(source))
            throw new IOException($"Refusing to remove '{source}': not a junction.");

        // Deleting a reparse-point directory removes the link itself, not its target.
        Directory.Delete(source, recursive: false);
    }

    private static byte[] BuildReparseBuffer(string target)
    {
        // Substitute name is the NT path form; print name is the friendly form.
        string substituteName = @"\??\" + target;
        string printName = target;

        byte[] subBytes = Encoding.Unicode.GetBytes(substituteName);
        byte[] printBytes = Encoding.Unicode.GetBytes(printName);

        // PathBuffer = substitute + null + print + null  (UTF-16)
        int pathBufferLength = subBytes.Length + 2 + printBytes.Length + 2;

        // MountPoint payload header = 4 USHORTs (offsets/lengths) = 8 bytes.
        ushort reparseDataLength = (ushort)(8 + pathBufferLength);

        // Full buffer = common header (ReparseTag 4 + ReparseDataLength 2 + Reserved 2 = 8) + payload.
        byte[] buffer = new byte[8 + reparseDataLength];
        int pos = 0;

        void WriteUInt(uint v) { BitConverter.GetBytes(v).CopyTo(buffer, pos); pos += 4; }
        void WriteUShort(ushort v) { BitConverter.GetBytes(v).CopyTo(buffer, pos); pos += 2; }

        WriteUInt(IO_REPARSE_TAG_MOUNT_POINT);
        WriteUShort(reparseDataLength);
        WriteUShort(0);                                   // Reserved

        WriteUShort(0);                                   // SubstituteNameOffset
        WriteUShort((ushort)subBytes.Length);             // SubstituteNameLength (no null)
        WriteUShort((ushort)(subBytes.Length + 2));       // PrintNameOffset (after substitute + null)
        WriteUShort((ushort)printBytes.Length);           // PrintNameLength (no null)

        // PathBuffer
        subBytes.CopyTo(buffer, pos); pos += subBytes.Length;
        pos += 2;                                          // null terminator for substitute
        printBytes.CopyTo(buffer, pos); pos += printBytes.Length;
        // trailing null left as zero

        return buffer;
    }
}
