using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinTool.Upgrade;

internal static class DownloadVolume
{
    // Partition metadata is queried without spawning a console or changing disk state.
    internal static bool IsReserved(string root)
    {
        if (root.Length < 2 || root[1] != ':') return false;
        using var volume = CreateFile(@"\\.\" + root[..2], 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (volume.IsInvalid) return false;
        var info = new byte[144];
        if (!DeviceIoControl(volume, 0x00070048, IntPtr.Zero, 0, info, info.Length, out _, IntPtr.Zero)) return false;
        var style = BitConverter.ToInt32(info, 0);
        if (style == 0) // MBR hidden/recovery/EFI partitions.
            return info[32] is 0x12 or 0x17 or 0x1B or 0x1C or 0x27 or 0xEF;
        if (style != 1) return false;
        var type = Fx.GuidAt(info, 32);
        var attributes = BitConverter.ToUInt64(info, 64);
        // NO_DRIVE_LETTER can also be set on a perfectly usable Windows C:
        // volume. Only hidden/read-only flags and reserved partition TYPES block it.
        return (attributes & 0x5000000000000000UL) != 0 ||
            type == new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B") ||
            type == new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE") ||
            type == new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, IntPtr input, int inputSize,
        [Out] byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
