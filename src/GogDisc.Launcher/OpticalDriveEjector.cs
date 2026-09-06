using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GogDisc.Launcher;

internal static class OpticalDriveEjector
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint EjectMedia = 0x002D4808;

    public static bool IsOpticalDrive(string? path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path ?? ""));
            return root is not null && new DriveInfo(root).DriveType == DriveType.CDRom;
        }
        catch
        {
            return false;
        }
    }

    public static void Eject(string? path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path ?? ""));
        if (root is null || new DriveInfo(root).DriveType != DriveType.CDRom)
            throw new InvalidOperationException("The active package is not on an optical disc drive.");

        var volume = $@"\\.\{root[..2]}";
        using var handle = CreateFile(volume, GenericRead | GenericWrite, ShareRead | ShareWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not open the optical drive.");
        if (!DeviceIoControl(handle, EjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not eject the disc. Close any files still in use and try again.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode,
        IntPtr inputBuffer, uint inputBufferSize, IntPtr outputBuffer, uint outputBufferSize,
        out uint bytesReturned, IntPtr overlapped);
}
