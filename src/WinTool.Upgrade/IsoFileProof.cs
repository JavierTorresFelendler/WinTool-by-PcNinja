using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinTool.Upgrade;

internal sealed record IsoFileStamp(uint VolumeSerial, ulong FileId, ulong JournalId, long Usn, long Length);

internal interface IIsoFileProof : IDisposable
{
    FileStream Stream { get; }
    IsoFileStamp? Capture();
}

/// <summary>
/// Operation-scoped protection and conservative NTFS change evidence, not a content hash.
/// Callers must dispose this before returning to idle and must never reuse a null stamp.
/// </summary>
internal sealed class IsoFileProof : IIsoFileProof
{
    // winioctl.h: FILE_DEVICE_FILE_SYSTEM, functions 46/58/61, FILE_ANY_ACCESS.
    private const uint ReadUsnJournal = 0x000900bb;
    private const uint ReadFileUsnData = 0x000900eb;
    private const uint QueryUsnJournal = 0x000900f4;
    private const uint UsnReasonClose = 0x80000000;
    private const uint ReparseOrDirectory = (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory);
    private readonly SafeFileHandle _fileHandle;
    private readonly string _volumePath;
    private bool _disposed;

    public FileStream Stream { get; }

    private IsoFileProof(FileStream stream, string volumePath)
    {
        Stream = stream;
        _fileHandle = stream.SafeFileHandle;
        _volumePath = volumePath;
    }

    public static IsoFileProof? TryOpen(string path)
    {
        if (!Fx.IsWindows) return null;
        FileStream? stream = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            // No UNC, device paths, alternate streams, reparse files or reparse parents.
            if (fullPath.Length < 3 || !Fx.IsAsciiLetter(fullPath[0]) || fullPath[1] != ':' ||
                fullPath[2] != '\\' || fullPath.IndexOf(':', 2) >= 0 || HasReparseComponent(fullPath))
                return null;
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            var handle = stream.SafeFileHandle;
            if (!GetFileInformationByHandle(handle, out var information) ||
                (information.Attributes & ReparseOrDirectory) != 0 ||
                !string.Equals(FinalPath(handle, 0), @"\\?\" + fullPath, StringComparison.OrdinalIgnoreCase))
                return null;

            // Resolve the volume from the open file, not a drive letter which could be rebound.
            var finalPath = FinalPath(handle, 1); // VOLUME_NAME_GUID | FILE_NAME_NORMALIZED
            const string prefix = @"\\?\Volume";
            if (finalPath is null || !finalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            var end = finalPath.IndexOf("}\\", StringComparison.Ordinal);
            if (end < prefix.Length || !Guid.TryParse(finalPath.Substring(prefix.Length, end - prefix.Length + 1), out _))
                return null;
            var volumeRoot = finalPath[..(end + 2)];
            var fileSystem = new StringBuilder(32);
            if (GetDriveTypeW(volumeRoot) != 3 || // DRIVE_FIXED only; no removable/network media.
                !GetVolumeInformationByHandleW(handle, null, 0, out var serial, out _, out _, fileSystem, 32) ||
                serial != information.VolumeSerial || !fileSystem.ToString().Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                return null;

            var proof = new IsoFileProof(stream, volumeRoot.TrimEnd('\\'));
            stream = null;
            return proof;
        }
        catch (Exception ex) when (Unavailable(ex)) { return null; }
        finally { stream?.Dispose(); }
    }

    public IsoFileStamp? Capture()
    {
        if (_disposed || _fileHandle.IsClosed || _fileHandle.IsInvalid) return null;
        try
        {
            if (!GetFileInformationByHandle(_fileHandle, out var information) ||
                (information.Attributes & ReparseOrDirectory) != 0) return null;
            var fileId = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            var length = ((ulong)information.SizeHigh << 32) | information.SizeLow;
            if (fileId == 0 || length > long.MaxValue) return null;

            // Every volume handle lives only for this capture. This never creates/modifies a journal.
            using var volume = CreateFileW(_volumePath, 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
            var fileSystem = new StringBuilder(32);
            if (volume.IsInvalid || !GetVolumeInformationByHandleW(volume, null, 0, out var serial,
                out _, out _, fileSystem, 32) || serial != information.VolumeSerial ||
                !fileSystem.ToString().Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return null;
            var before = ReadJournal(volume);
            if (before is null) return null;

            // READ_FILE_USN_DATA requests V2 explicitly. Its Reason field is NOT valid.
            var input = new byte[] { 2, 0, 2, 0 };
            var output = new byte[4096];
            if (!DeviceIoControl(_fileHandle, ReadFileUsnData, input, (uint)input.Length,
                output, (uint)output.Length, out var returned, IntPtr.Zero) ||
                returned > output.Length ||
                !TryReadV2(output.AsSpan(0, (int)returned), out var actualId, out var usn, out _) ||
                actualId != fileId || !before.Contains(usn)) return null;

            // Repeated same-kind changes may coalesce until final close. Only a retained CLOSE
            // record can establish a baseline whose next ordinary mutation must get a new USN.
            // One bounded non-waiting lookup, not a journal scan or WRITE_USN_CLOSE_RECORD.
            if (!HasCloseRecord(volume, before.Id, fileId, usn)) return null;
            var after = ReadJournal(volume);
            if (!IsStableJournal(before, after, usn)) return null;
            return new(information.VolumeSerial, fileId, before.Id, usn, (long)length);
        }
        catch (Exception ex) when (Unavailable(ex)) { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stream.Dispose();
    }

    private static bool HasReparseComponent(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return true;
        for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static string? FinalPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, flags);
        return length == 0 || length >= buffer.Capacity ? null : buffer.ToString();
    }

    private static Journal? ReadJournal(SafeFileHandle volume)
    {
        var output = new byte[80]; // Known USN_JOURNAL_DATA_V0/V1/V2 layouts.
        if (!DeviceIoControl(volume, QueryUsnJournal, null, 0, output, (uint)output.Length,
            out var returned, IntPtr.Zero) || returned > output.Length) return null;
        return ParseJournal(output.AsSpan(0, (int)returned));
    }

    // Pure native-output validation also runs in non-administrator smoke fixtures.
    internal static Journal? ParseJournal(ReadOnlySpan<byte> data)
    {
        if (data.Length is not (56 or 64 or 80)) return null;
        var journal = new Journal(BinaryPrimitives.ReadUInt64LittleEndian(data),
            BinaryPrimitives.ReadInt64LittleEndian(data[8..]), BinaryPrimitives.ReadInt64LittleEndian(data[16..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[24..]));
        var maximum = BinaryPrimitives.ReadInt64LittleEndian(data[32..]);
        if (journal.Id == 0 || journal.First < 0 || journal.Lowest < 0 || journal.Next <= 0 ||
            journal.First >= journal.Next || journal.Lowest >= journal.Next || maximum < journal.Next)
            return null;
        if (data.Length >= 64 && (BinaryPrimitives.ReadUInt16LittleEndian(data[56..]) > 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[58..]) < 2)) return null;
        return journal;
    }

    private static bool HasCloseRecord(SafeFileHandle volume, ulong journalId, ulong fileId, long usn)
    {
        // READ_USN_JOURNAL_DATA_V1; zero Timeout/BytesToWaitFor means no waiting.
        var input = new byte[48];
        BinaryPrimitives.WriteInt64LittleEndian(input, usn);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8), uint.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(32), journalId);
        BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(40), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(42), 2);
        var output = new byte[4096];
        if (!DeviceIoControl(volume, ReadUsnJournal, input, (uint)input.Length,
            output, (uint)output.Length, out var returned, IntPtr.Zero) || returned < 68 || returned > output.Length)
            return false;
        return HasExactCloseRecord(output.AsSpan(0, (int)returned), fileId, usn);
    }

    internal static bool HasExactCloseRecord(ReadOnlySpan<byte> output, ulong fileId, long usn)
    {
        if (output.Length < 68) return false;
        // The first eight bytes are the next USN, not a record. Only the exact first record
        // requested is acceptable; never search forward for a convenient older/newer CLOSE.
        return TryReadV2(output[8..], out var actualId, out var actualUsn, out var reason) &&
            actualId == fileId && actualUsn == usn && (reason & UsnReasonClose) != 0;
    }

    internal static bool TryReadV2(ReadOnlySpan<byte> data, out ulong fileId, out long usn, out uint reason)
    {
        fileId = 0;
        usn = 0;
        reason = 0;
        if (data.Length < 60 || BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != 0) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[56..]);
        var nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[58..]);
        if (length < 60 || length > data.Length || (length & 7) != 0 || nameOffset < 60 ||
            (nameOffset & 1) != 0 || (nameLength & 1) != 0 || (uint)nameOffset + nameLength > length)
            return false;
        fileId = BinaryPrimitives.ReadUInt64LittleEndian(data[8..]);
        usn = BinaryPrimitives.ReadInt64LittleEndian(data[24..]);
        reason = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);
        return fileId != 0 && usn > 0;
    }

    internal static bool IsStableJournal(Journal before, Journal? after, long usn) =>
        after is not null && after.Id == before.Id && after.Contains(usn) &&
        after.First >= before.First && after.Lowest >= before.Lowest && after.Next >= before.Next;

    internal sealed record Journal(ulong Id, long First, long Next, long Lowest)
    {
        public bool Contains(long usn) => usn > 0 && usn >= First && usn >= Lowest && usn < Next;
    }

    private static bool Unavailable(Exception ex) => ex is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or ObjectDisposedException or System.Security.SecurityException or
        DllNotFoundException or EntryPointNotFoundException;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME AccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME WriteTime;
        public uint VolumeSerial;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Links;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string root);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle file, StringBuilder? volumeName,
        uint volumeNameSize, out uint serial, out uint maximumComponentLength, out uint flags,
        StringBuilder fileSystem, uint fileSystemSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, [In] byte[]? input, uint inputLength,
        [Out] byte[] output, uint outputLength, out uint returned, IntPtr overlapped);
}
