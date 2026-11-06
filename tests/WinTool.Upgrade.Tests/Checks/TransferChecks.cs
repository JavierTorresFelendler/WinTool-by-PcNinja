// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using WinTool.Core.Json;
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class TransferChecks
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PcNinja-Transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var data = new byte[8192];
            new Random().NextBytes(data);
            var hash = Compat.ToHexString(TestShims.Sha256(data));
            var handler = new FakeDownloadHandler(data);
            using var http = new HttpClient(handler);
            var endpoint = new Uri("https://download.invalid/iso");
            var storageCalls = new List<long?>();
            try
            {
                await IsoTransfer.DownloadAsync(http, endpoint, directory, storageCalls.Add, null, CancellationToken.None, hash, 1);
                throw new Exception("Interrupted transfer unexpectedly completed.");
            }
            catch (DownloadTransferException) { }
            var exact = Path.Combine(directory, "Original.iso");
            if (!File.Exists(exact + ".partial")) throw new Exception("Interrupted partial missing.");
            var result = await IsoTransfer.DownloadAsync(http, endpoint, directory, storageCalls.Add, null, CancellationToken.None, hash, 1);
            if (!result.Resumed || !handler.SawRange || result.Sha256 != hash ||
                !File.ReadAllBytes(exact).SequenceEqual(data) || !storageCalls.Contains(4096L))
                throw new Exception("Exact resume failed byte identity, checksum or remaining-space checks.");
            handler.Interrupt = false;
            var validations = 0;
            var reused = await IsoTransfer.DownloadAsync(http, endpoint, directory, _ => throw new Exception("Reuse reserved download space."),
                null, CancellationToken.None, hash, 1, (_, _) => { validations++; return Task.CompletedTask; });
            if (!reused.Reused || validations != 1 || reused.IsoPath != exact)
                throw new Exception("The completed exact file was not reused and revalidated.");
            using var offlineHttp = new HttpClient(new OfflineHandler());
            var offlineReuse = await IsoTransfer.DownloadAsync(offlineHttp, endpoint, directory,
                _ => throw new Exception("Offline reuse reserved download space."), null, CancellationToken.None, hash, 1);
            if (!offlineReuse.Reused || offlineReuse.IsoPath != exact)
                throw new Exception("Recorded exact ISO could not be reused while the provider was unavailable.");
            var damaged = data.ToArray();
            damaged[0] ^= 0xFF;
            File.WriteAllBytes(exact, damaged);
            var repaired = await IsoTransfer.DownloadAsync(http, endpoint, directory, _ => { }, null, CancellationToken.None, hash, 1);
            if (repaired.Reused || repaired.IsoPath != exact || !File.ReadAllBytes(exact).SequenceEqual(data))
                throw new Exception("A proven-corrupt owned download was not replaced at the original name.");

            try
            {
                await IsoTransfer.DownloadAsync(http, endpoint, directory, _ => { }, null, CancellationToken.None, hash, 1,
                    (_, _) => throw new UnsafeInstallationMediaException("interfering secondary media"));
                throw new Exception("Unsafe media validation unexpectedly passed.");
            }
            catch (UnsafeInstallationMediaException) { }
            if (!File.ReadAllBytes(exact).SequenceEqual(data)) throw new Exception("Environmental media failure deleted a valid ISO.");
            try
            {
                await IsoTransfer.DownloadAsync(http, endpoint, directory, _ => { }, null, CancellationToken.None, hash, 1,
                    (_, _) => throw new IOException("simulated unavailable drive"));
                throw new Exception("I/O validation unexpectedly passed.");
            }
            catch (IOException) { }
            if (!File.Exists(exact)) throw new Exception("An I/O error deleted a completed ISO.");

            var unownedDirectory = Path.Combine(directory, "unowned");
            Directory.CreateDirectory(unownedDirectory);
            var unowned = Path.Combine(unownedDirectory, "Original.iso");
            File.WriteAllBytes(unowned, damaged);
            try
            {
                await IsoTransfer.DownloadAsync(http, endpoint, unownedDirectory, _ => { }, null, CancellationToken.None, hash, 1);
                throw new Exception("An unowned corrupt file was overwritten.");
            }
            catch (InvalidOperationException) { }
            if (!File.ReadAllBytes(unowned).SequenceEqual(damaged)) throw new Exception("An unowned ISO was deleted.");

            var wrongHashDirectory = Path.Combine(directory, "wrong-hash");
            try
            {
                await IsoTransfer.DownloadAsync(http, endpoint, wrongHashDirectory, _ => { }, null, CancellationToken.None, new string('0', 64), 1);
                throw new Exception("An incorrect trusted checksum was accepted.");
            }
            catch (CorruptDownloadException) { }
            if (File.Exists(Path.Combine(wrongHashDirectory, "Original.iso")) ||
                File.Exists(Path.Combine(wrongHashDirectory, "Original.iso.partial")))
                throw new Exception("Corrupt new bytes were kept/promoted.");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await IsoTransfer.DownloadAsync(http, endpoint, directory, _ => { }, null, cancelled.Token, hash, 1);
                throw new Exception("Cancellation was ignored.");
            }
            catch (OperationCanceledException) { }
            if (!File.Exists(exact)) throw new Exception("Cancellation removed a valid ISO.");

            var restartDirectory = Path.Combine(directory, "range-ignored");
            using var restartHttp = new HttpClient(new FakeDownloadHandler(data));
            try { await IsoTransfer.DownloadAsync(restartHttp, endpoint, restartDirectory, _ => { }, null, CancellationToken.None, hash, 1); }
            catch (DownloadTransferException) { }
            using var fullHttp = new HttpClient(new FakeDownloadHandler(data) { Interrupt = false, IgnoreRange = true });
            var restarted = await IsoTransfer.DownloadAsync(fullHttp, endpoint, restartDirectory, _ => { }, null, CancellationToken.None, hash, 1);
            if (restarted.Resumed || restarted.IsoPath != Path.Combine(restartDirectory, "Original.iso") ||
                !File.ReadAllBytes(restarted.IsoPath).SequenceEqual(data) || File.Exists(restarted.IsoPath + ".partial"))
                throw new Exception("Ignored Range did not safely restart at the original name.");

            using var midCancel = new CancellationTokenSource();
            using var cancelHttp = new HttpClient(new FakeDownloadHandler(data) { CancelOnSecondRead = midCancel });
            var cancelDirectory = Path.Combine(directory, "cancel-active");
            try
            {
                await IsoTransfer.DownloadAsync(cancelHttp, endpoint, cancelDirectory, _ => { }, null, midCancel.Token, hash, 1);
                throw new Exception("Mid-transfer cancellation was ignored.");
            }
            catch (OperationCanceledException) { }
            if (new FileInfo(Path.Combine(cancelDirectory, "Original.iso.partial")).Length != 4096)
                throw new Exception("Cancelled transfer lost its written bytes.");
            File.WriteAllBytes(Path.Combine(cancelDirectory, "Original.iso.partial"), []);
            var zeroRetry = await IsoTransfer.DownloadAsync(http, endpoint, cancelDirectory, _ => { }, null, CancellationToken.None, hash, 1);
            if (!File.ReadAllBytes(zeroRetry.IsoPath).SequenceEqual(data)) throw new Exception("Zero-byte owned partial could not restart.");

            var routes = new List<Uri>();
            await UpgradeEngine.DownloadWithTransportFallbackAsync(uri =>
            {
                routes.Add(uri);
                return routes.Count == 1
                    ? Task.FromException<DownloadResult>(new DownloadTransferException("TLS failure", new HttpRequestException("ProtocolVersion")))
                    : Task.FromResult(result);
            });
            if (routes.Count != 2 || routes[0].Host == routes[1].Host || routes[1].Scheme != "https")
                throw new Exception("Secure direct-provider fallback was not attempted.");
            await RunExistingValidatorChecksAsync(directory, data, hash, endpoint);
            if (Directory.EnumerateFiles(directory, "*Copy*", SearchOption.AllDirectories).Any())
                throw new Exception("Obsolete numbered-copy logic remains.");
            Console.WriteLine("PASS: Original-name resume/restart, exact existing-ISO validation hook/offline reuse, checksum sidecars, owned corruption replacement, unowned/environmental/cancel preservation, secure transport fallback; no Copy N allocation.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task RunExistingValidatorChecksAsync(string parent, byte[] data, string hash, Uri endpoint)
    {
        var directory = Path.Combine(parent, "existing-validator");
        var exact = Path.Combine(directory, "Original.iso");
        var freshMediaCalls = 0;
        var existingCalls = 0;
        var freshHandler = new FakeDownloadHandler(data) { Interrupt = false };
        using (var freshHttp = new HttpClient(freshHandler))
        {
            var fresh = await IsoTransfer.DownloadAsync(freshHttp, endpoint, directory, _ => { }, null,
                CancellationToken.None, hash, 1,
                validateMedia: (path, _) =>
                {
                    if (path != exact) throw new Exception("Fresh media validation received a different ISO.");
                    freshMediaCalls++;
                    return Task.CompletedTask;
                },
                validateExistingIso: (_, _, _) =>
                {
                    existingCalls++;
                    throw new Exception("Fresh bytes incorrectly used the existing-ISO validation hook.");
                });
            if (fresh.Reused || fresh.Sha256 != hash || freshMediaCalls != 1 || existingCalls != 0 || freshHandler.RequestCount != 1)
                throw new Exception("Fresh download duplicated or bypassed its existing media-validation handoff.");
        }
        AssertCompletedRecords(directory, exact, endpoint, data.Length, hash);

        var offlineHandler = new OfflineHandler();
        using var offlineHttp = new HttpClient(offlineHandler);
        var reused = await IsoTransfer.DownloadAsync(offlineHttp, endpoint, directory,
            _ => throw new Exception("Existing hook reuse reserved download space."), null, CancellationToken.None, null, 1,
            validateMedia: (_, _) => throw new Exception("Existing ISO ran duplicate media validation."),
            validateExistingIso: (path, expected, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (path != exact || expected != hash) throw new Exception("Offline hook lost the exact ISO or recorded checksum.");
                existingCalls++;
                return Task.FromResult(hash.ToLowerInvariant());
            });
        if (!reused.Reused || reused.IsoPath != exact || reused.Sha256 != hash || existingCalls != 1 || offlineHandler.RequestCount != 0)
            throw new Exception("Recorded ISO hook failed to reuse its actual checksum without HTTP or duplicate validation.");
        AssertCompletedRecords(directory, exact, endpoint, data.Length, hash);

        foreach (var failure in new Exception[]
        {
            new UnsafeInstallationMediaException("simulated secondary answer file"),
            new IOException("simulated unavailable source")
        })
        {
            var calls = 0;
            Exception? observed = null;
            try
            {
                await IsoTransfer.DownloadAsync(offlineHttp, endpoint, directory,
                    _ => throw new Exception("Failed reuse reserved download space."), null, CancellationToken.None, hash, 1,
                    validateMedia: (_, _) => throw new Exception("Failed hook ran duplicate media validation."),
                    validateExistingIso: (_, _, _) => { calls++; throw failure; });
            }
            catch (Exception ex) { observed = ex; }
            if (!ReferenceEquals(observed, failure) || calls != 1 || offlineHandler.RequestCount != 0 ||
                !File.ReadAllBytes(exact).SequenceEqual(data))
                throw new Exception("An environmental existing-hook failure was swallowed, retried or deleted the recorded ISO.");
            AssertCompletedRecords(directory, exact, endpoint, data.Length, hash);
        }

        using (var cancel = new CancellationTokenSource())
        {
            var calls = 0;
            try
            {
                await IsoTransfer.DownloadAsync(offlineHttp, endpoint, directory,
                    _ => throw new Exception("Cancelled reuse reserved download space."), null, cancel.Token, hash, 1,
                    validateMedia: (_, _) => throw new Exception("Cancelled hook ran duplicate media validation."),
                    validateExistingIso: (_, _, token) =>
                    {
                        calls++;
                        cancel.Cancel();
                        token.ThrowIfCancellationRequested();
                        return Task.FromResult(hash);
                    });
                throw new Exception("Existing-hook cancellation was ignored.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (calls != 1 || offlineHandler.RequestCount != 0 || !File.ReadAllBytes(exact).SequenceEqual(data))
                throw new Exception("Existing-hook cancellation retried or removed the recorded ISO.");
            AssertCompletedRecords(directory, exact, endpoint, data.Length, hash);
        }

        var unownedDirectory = Path.Combine(parent, "existing-validator-unowned");
        Directory.CreateDirectory(unownedDirectory);
        var unowned = Path.Combine(unownedDirectory, "Original.iso");
        File.WriteAllBytes(unowned, data);
        var unownedHandler = new FakeDownloadHandler(data) { Interrupt = false };
        using (var unownedHttp = new HttpClient(unownedHandler))
        {
            var calls = 0;
            var result = await IsoTransfer.DownloadAsync(unownedHttp, endpoint, unownedDirectory,
                _ => throw new Exception("Unowned exact-name reuse reserved download space."), null, CancellationToken.None, null, 1,
                validateMedia: (_, _) => throw new Exception("Unowned hook ran duplicate media validation."),
                validateExistingIso: (path, expected, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (path != unowned || expected is not null) throw new Exception("Unowned hook received a guessed path/checksum.");
                    calls++;
                    return Task.FromResult(hash.ToLowerInvariant());
                });
            if (!result.Reused || result.IsoPath != unowned || result.Sha256 != hash || calls != 1 || unownedHandler.RequestCount != 1 ||
                !File.ReadAllBytes(unowned).SequenceEqual(data))
                throw new Exception("Exact-name unowned reuse did not preserve the file and single-hook handoff.");
        }
        AssertCompletedRecords(unownedDirectory, unowned, endpoint, data.Length, hash);

        var damaged = data.ToArray();
        damaged[0] ^= 0xFF;
        File.WriteAllBytes(exact, damaged);
        var damagedHash = Compat.ToHexString(TestShims.Sha256(damaged));
        var repairHandler = new FakeDownloadHandler(data) { Interrupt = false };
        using (var repairHttp = new HttpClient(repairHandler))
        {
            var calls = 0;
            var newMediaCalls = 0;
            var repaired = await IsoTransfer.DownloadAsync(repairHttp, endpoint, directory, _ => { }, null,
                CancellationToken.None, hash, 1,
                validateMedia: (path, _) =>
                {
                    if (path != exact) throw new Exception("Repair validated a different destination.");
                    newMediaCalls++;
                    return Task.CompletedTask;
                },
                validateExistingIso: (path, expected, _) =>
                {
                    if (path != exact || expected != hash) throw new Exception("Repair hook lost exact owned-file identity.");
                    calls++;
                    return Task.FromResult(damagedHash);
                });
            if (repaired.Reused || repaired.IsoPath != exact || repaired.Sha256 != hash || calls != 1 || newMediaCalls != 1 ||
                repairHandler.RequestCount != 1 || !File.ReadAllBytes(exact).SequenceEqual(data))
                throw new Exception("Existing-hook checksum mismatch did not repair only the recorded owned download.");
        }
        AssertCompletedRecords(directory, exact, endpoint, data.Length, hash);

        var corruptDirectory = Path.Combine(parent, "existing-validator-corrupt-unowned");
        Directory.CreateDirectory(corruptDirectory);
        var corrupt = Path.Combine(corruptDirectory, "Original.iso");
        File.WriteAllBytes(corrupt, damaged);
        var corruptHandler = new FakeDownloadHandler(data) { Interrupt = false };
        using (var corruptHttp = new HttpClient(corruptHandler))
        {
            var calls = 0;
            try
            {
                await IsoTransfer.DownloadAsync(corruptHttp, endpoint, corruptDirectory,
                    _ => throw new Exception("Unowned corrupt reuse reserved download space."), null, CancellationToken.None, hash, 1,
                    validateMedia: (_, _) => throw new Exception("Corrupt hook ran duplicate media validation."),
                    validateExistingIso: (_, _, _) => { calls++; throw new InvalidDataException("simulated failed integrity check"); });
                throw new Exception("Unowned corrupt-hook input was accepted.");
            }
            catch (InvalidOperationException) { }
            if (calls != 1 || corruptHandler.RequestCount != 1 || !File.ReadAllBytes(corrupt).SequenceEqual(damaged) ||
                File.Exists(corrupt + ".pcninja.json") || File.Exists(Path.Combine(corruptDirectory, ".pcninja-download.json")))
                throw new Exception("Corrupt existing-hook input deleted or adopted an unowned file.");
        }
    }

    private static void AssertCompletedRecords(string directory, string exact, Uri endpoint, long bytes, string hash)
    {
        var expected = new CompletedDownload(endpoint.AbsoluteUri, Path.GetFileName(exact), bytes, hash);
        var record = JsonData.Deserialize<CompletedDownload>(File.ReadAllText(exact + ".pcninja.json"));
        var pointer = JsonData.Deserialize<CompletedDownload>(File.ReadAllText(Path.Combine(directory, ".pcninja-download.json")));
        if (record != expected || pointer != expected)
            throw new Exception("Existing-ISO validation did not preserve the exact actual-checksum ownership records.");
    }

    private sealed class FakeDownloadHandler(byte[] data) : HttpMessageHandler
    {
        public bool Interrupt { get; set; } = true;
        public bool IgnoreRange { get; set; }
        public CancellationTokenSource? CancelOnSecondRead { get; set; }
        public bool SawRange { get; private set; }
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            var offset = request.Headers.Range?.Ranges.First().From;
            SawRange |= offset.HasValue;
            if (IgnoreRange) offset = null;
            HttpContent content = offset.HasValue
                ? new ByteArrayContent(data, (int)offset.Value, data.Length - (int)offset.Value)
                : Interrupt ? new StreamContent(new InterruptedStream(data, CancelOnSecondRead)) : new ByteArrayContent(data);
            content.Headers.ContentLength = data.Length - (offset ?? 0);
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "Original.iso" };
            if (offset.HasValue)
                content.Headers.ContentRange = new ContentRangeHeaderValue(offset.Value, data.Length - 1, data.Length);
            var response = new HttpResponseMessage(offset.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = content, RequestMessage = request
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"test-v1\"");
            return Task.FromResult(response);
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new HttpRequestException("A valid recorded cache must not contact the unavailable provider.");
        }
    }

    private sealed class InterruptedStream(byte[] data, CancellationTokenSource? cancellation = null) : MemoryStream(data)
    {
#if NETFRAMEWORK
        // .NET Framework has no Memory<byte> overload; the port reads through this one.
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (Position >= 4096)
            {
                cancellation?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("Simulated connection drop.");
            }
            return base.ReadAsync(buffer, offset, Math.Min(count, 4096), cancellationToken);
        }
#else
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= 4096)
            {
                cancellation?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("Simulated connection drop.");
            }
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], cancellationToken);
        }
#endif
    }
}
