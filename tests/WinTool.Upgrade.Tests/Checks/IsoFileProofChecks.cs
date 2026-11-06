// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using System.Buffers.Binary;
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class IsoFileProofChecks
{
    public static void Run()
    {
        CheckNativeParsers();
        var directory = Path.Combine(Path.GetTempPath(), "PcNinja-IsoProof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ordinary-test-file.bin");
            var replacement = Path.Combine(directory, "replacement.bin");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            if (IsoFileProof.TryOpen(Path.Combine(directory, "missing.bin")) is not null)
                throw new Exception("A missing file supplied an ISO identity proof.");

            IsoFileStamp? original;
            using (var proof = IsoFileProof.TryOpen(path))
            {
                if (proof is null)
                {
                    Console.WriteLine("SKIP: Native ISO proof unavailable on this temp volume; no fast-path proof claimed.");
                    return;
                }
                if (proof.Stream.IsAsync || proof.Stream.CanWrite)
                    throw new Exception("ISO proof did not use a synchronous read-only stream.");
                proof.Stream.Position = 2;
                original = proof.Capture();
                if (proof.Stream.Position != 2) throw new Exception("Capturing an ISO proof changed the stream position.");
                MustDeny(() => { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
                MustDeny(() => File.Delete(path));
                if (original is not null && proof.Capture() != original)
                    throw new Exception("An unchanged protected file did not keep the same native proof.");
            }
            // No handles survive an operation, even when the journal is unavailable.
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)) { }
            if (original is null)
            {
                Console.WriteLine("SKIP: NTFS journal/retained CLOSE proof unavailable; operation lock/release checked, fast path not proven.");
                return;
            }

            using (var proof = IsoFileProof.TryOpen(path))
                if (proof?.Capture() != original) throw new Exception("An unchanged file was not reusable across proof lifetimes.");
            var modified = File.GetLastWriteTimeUtc(path);
            File.WriteAllBytes(path, [4, 3, 2, 1]); // Same length; restoring time must not hide the write.
            File.SetLastWriteTimeUtc(path, modified);
            using (var proof = IsoFileProof.TryOpen(path))
                if (proof?.Capture() == original) throw new Exception("Same-length changed bytes with restored mtime reused an old native proof.");

            IsoFileStamp? beforeReplacement;
            using (var proof = IsoFileProof.TryOpen(path)) beforeReplacement = proof?.Capture();
            File.WriteAllBytes(replacement, [4, 3, 2, 1]);
            File.SetLastWriteTimeUtc(replacement, modified);
            File.Delete(path);
            File.Move(replacement, path);
            using (var proof = IsoFileProof.TryOpen(path))
                if (beforeReplacement is not null && proof?.Capture() == beforeReplacement)
                    throw new Exception("Replacing the selected file reused its old native identity.");
            if (beforeReplacement is null)
                Console.WriteLine("SKIP: Replacement identity comparison had no reusable baseline; ordinary validation remains required.");

            // A reader left open can keep change reasons coalesced. Such an open record must
            // not mint reusable evidence, even if a second same-kind write keeps its last USN.
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                File.WriteAllBytes(path, [5, 6, 7, 8]);
                IsoFileStamp? first;
                using (var proof = IsoFileProof.TryOpen(path)) first = proof?.Capture();
                File.WriteAllBytes(path, [8, 7, 6, 5]);
                using var second = IsoFileProof.TryOpen(path);
                if (first is not null && second?.Capture() == first)
                    throw new Exception("Coalesced writes under an external reader reused a native proof.");
            }
            File.Delete(path); // Disposed proof must not leave a delete/eject-style lock.
            Console.WriteLine("PASS: Native NTFS proof, unchanged reuse, restored-time writes, coalescing and operation-only locks; replacement comparison when available. No ISO or Setup used.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CheckNativeParsers()
    {
        const ulong fileId = 42;
        const long usn = 512;
        var record = Record(fileId, usn, 0x80000001);
        Require(IsoFileProof.TryReadV2(record, out var actualId, out var actualUsn, out var reason) &&
            actualId == fileId && actualUsn == usn && reason == 0x80000001, "Known V2 record was rejected.");
        RejectRecord(TestShims.Slice(record, 0, 59), "Truncated V2 header was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes, 72), "Out-of-buffer V2 length was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes, 60), "Unaligned V2 length was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 3), "Unsupported major version was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), 1), "Unknown minor layout was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), 58), "Filename inside V2 header was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), 61), "Unaligned filename offset was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 3), "Odd UTF-16 filename length was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 6), "Out-of-record filename was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 0), "Unknown file ID was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), 0), "USN zero was accepted.");
        ChangeRecord(record, bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), -1), "Negative USN was accepted.");

        var response = JournalResponse(record);
        Require(IsoFileProof.HasExactCloseRecord(response, fileId, usn), "Exact V2 CLOSE record was rejected.");
        Require(!IsoFileProof.HasExactCloseRecord(TestShims.Slice(response, 0, 67), fileId, usn), "Truncated journal record was accepted.");
        Require(!IsoFileProof.HasExactCloseRecord(response, fileId + 1, usn), "A different file's CLOSE was accepted.");
        Require(!IsoFileProof.HasExactCloseRecord(response, fileId, usn + 8), "A different USN's CLOSE was accepted.");
        Require(!IsoFileProof.HasExactCloseRecord(JournalResponse(Record(fileId, usn, 1)), fileId, usn),
            "Open/coalesced record without CLOSE supplied reusable proof.");
        var laterClose = new byte[response.Length + record.Length];
        JournalResponse(Record(fileId, usn, 1)).CopyTo(laterClose, 0);
        record.CopyTo(laterClose, response.Length);
        Require(!IsoFileProof.HasExactCloseRecord(laterClose, fileId, usn), "Lookup searched ahead for a convenient CLOSE.");

        foreach (var size in new[] { 56, 64, 80 })
        {
            var journal = IsoFileProof.ParseJournal(JournalData(size));
            Require(journal is not null && journal.Id == 9 && journal.Contains(usn), "Known journal layout was rejected.");
        }
        foreach (var size in new[] { 0, 55, 57, 60, 79, 81 })
            Require(IsoFileProof.ParseJournal(new byte[size]) is null, "Unknown/truncated journal layout was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteUInt64LittleEndian(bytes, 0), "Journal ID zero was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), -1), "Negative FirstUsn was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), 0), "Invalid NextUsn was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), -1), "Negative LowestValidUsn was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), 1024), "Exhausted FirstUsn bound was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), 1024), "Exhausted lowest bound was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), 1023), "Maximum below NextUsn was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 3), "Journal without V2 support was accepted.");
        ChangeJournal(bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), 1), "Journal whose maximum version predates V2 was accepted.");
        var before = IsoFileProof.ParseJournal(JournalData(80))!;
        Require(!before.Contains(0) && !before.Contains(127) && before.Contains(128) && !before.Contains(1024),
            "USN retention bounds were not inclusive-low/exclusive-high.");
        Require(IsoFileProof.IsStableJournal(before, before, usn), "Unchanged journal was rejected.");
        Require(IsoFileProof.IsStableJournal(before, before with { First = 256, Lowest = 256, Next = 2048 }, usn),
            "Ordinary forward journal growth was rejected.");
        Require(!IsoFileProof.IsStableJournal(before, null, usn), "Unavailable second query was accepted.");
        Require(!IsoFileProof.IsStableJournal(before, before with { Id = 10 }, usn), "Journal reset was accepted.");
        Require(!IsoFileProof.IsStableJournal(before, before with { First = 513 }, usn), "Wrapped-away file USN was accepted.");
        Require(!IsoFileProof.IsStableJournal(before, before with { Lowest = 513 }, usn), "Journal discontinuity was accepted.");
        Require(!IsoFileProof.IsStableJournal(before, before with { First = 64 }, usn), "Backwards FirstUsn was accepted.");
        Require(!IsoFileProof.IsStableJournal(before, before with { Lowest = 64 }, usn), "Backwards lowest bound was accepted.");
        Require(!IsoFileProof.IsStableJournal(before, before with { Next = 900 }, usn), "Backwards journal end was accepted.");
        Console.WriteLine("PASS: Synthetic native V2 bounds/version/identity/CLOSE and journal reset/wrap/unavailable evidence checks (no administrator required).");
    }

    private static byte[] Record(ulong fileId, long usn, uint reason)
    {
        var bytes = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 64);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), fileId);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), usn);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), reason);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), 60);
        bytes[60] = (byte)'x';
        return bytes;
    }

    private static byte[] JournalResponse(byte[] record)
    {
        var response = new byte[record.Length + 8];
        BinaryPrimitives.WriteInt64LittleEndian(response, 1024);
        record.CopyTo(response, 8);
        return response;
    }

    private static byte[] JournalData(int size)
    {
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, 9);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), 128);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), 1024);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), 128);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), long.MaxValue);
        if (size >= 64)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(58), 4);
        }
        return bytes;
    }

    private static void ChangeRecord(byte[] original, Action<byte[]> change, string message)
    {
        var bytes = original.ToArray();
        change(bytes);
        RejectRecord(bytes, message);
    }

    private static void RejectRecord(byte[] bytes, string message) =>
        Require(!IsoFileProof.TryReadV2(bytes, out _, out _, out _), message);

    private static void ChangeJournal(Action<byte[]> change, string message)
    {
        var bytes = JournalData(80);
        change(bytes);
        Require(IsoFileProof.ParseJournal(bytes) is null, message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void MustDeny(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        throw new Exception("An active ISO proof allowed a concurrent write/delete.");
    }
}
