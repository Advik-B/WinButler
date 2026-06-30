using System;
using System.IO;
using System.Runtime.InteropServices;

namespace WinButler.Services;

/// <summary>
/// Sends files/folders to the Windows Recycle Bin via the shell's SHFileOperation,
/// so recycled items can be restored by the user.
/// </summary>
internal static class RecycleBin
{
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;       // route to Recycle Bin instead of permanent
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>Sends a single path to the Recycle Bin. Throws on failure.</summary>
    public static void Send(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            // pFrom must be double-null terminated.
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
            throw new IOException($"Recycle Bin delete failed (SHFileOperation code 0x{result:X}).");
        if (op.fAnyOperationsAborted != 0)
            throw new IOException("Recycle Bin delete was aborted.");
    }
}
