using System.Net;
using System.Net.Http.Headers;
using WinTool.Core.Json;

namespace WinTool.Upgrade;

public sealed class DownloadTransferException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class CorruptDownloadException(string message) : IOException(message);

internal sealed record PartialDownload(string Endpoint, string FileName, string? ETag, long? TotalBytes);
internal sealed record CompletedDownload(string Endpoint, string FileName, long Bytes, string Sha256);

internal static class IsoTransfer
{
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(90);

    internal static async Task<DownloadResult> DownloadAsync(HttpClient http, Uri endpoint, string directory,
        Action<long?> checkStorage, IProgress<DownloadProgress>? progress, CancellationToken token,
        string? expectedSha256, long minimumBytes = UpgradeEngine.MinimumIsoBytes,
        Func<string, CancellationToken, Task>? validateMedia = null,
        Func<string, string?, CancellationToken, Task<string>>? validateExistingIso = null)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var pointerPath = Path.Combine(directory, ".pcninja-download.json");
        RejectLink(pointerPath);
        var cached = await ReadMetadataAsync<CompletedDownload>(pointerPath, token);
        if (cached is not null && cached.FileName is { Length: > 0 } && cached.FileName == Path.GetFileName(cached.FileName) &&
            cached.FileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) &&
            cached.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            cached.Sha256 is { Length: 64 } && cached.Sha256.All(Uri.IsHexDigit))
        {
            var cachedPath = Path.Combine(directory, cached.FileName);
            var cachedRecord = cachedPath + ".pcninja.json";
            RejectLink(cachedPath);
            RejectLink(cachedRecord);
            if (File.Exists(cachedPath) && await ReadMetadataAsync<CompletedDownload>(cachedRecord, token) == cached)
            {
                RejectLink(cachedPath + ".pcninja.lock");
                using var cacheLock = new FileStream(cachedPath + ".pcninja.lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                try
                {
                    progress?.Report(new(cachedPath, 0, cached.Bytes, null, "Validating the existing ISO..."));
                    if (new FileInfo(cachedPath).Length != cached.Bytes || cached.Bytes < minimumBytes)
                        throw new CorruptDownloadException("The recorded ISO size has changed.");
                    var hash = await ValidateExistingAsync(cachedPath, expectedSha256 ?? cached.Sha256, token,
                        validateExistingIso, validateMedia);
                    AppLog.Info($"Reusing exact recorded download without contacting the provider: {cachedPath}. SHA256: {hash}.");
                    return new(cachedPath, cached.Bytes, cached.Bytes, hash, Reused: true);
                }
                catch (CorruptDownloadException)
                {
                    DeleteOwned(cachedPath);
                    DeleteOwned(cachedRecord);
                    DeleteOwned(pointerPath);
                    AppLog.Info($"Removed corrupt recorded ISO before a fresh transfer: {cachedPath}");
                }
            }
        }
        using var initial = await SendAsync(http, new HttpRequestMessage(HttpMethod.Get, endpoint), token);
        var mediaType = initial.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.Contains("text/", StringComparison.OrdinalIgnoreCase))
            throw new DownloadTransferException("The download service returned a web page. Try downloading in your browser.");
        var total = initial.Content.Headers.ContentLength;
        if (total is > 0 && total < minimumBytes)
            throw new DownloadTransferException("The download service did not return a complete Windows ISO.");

        var name = UpgradeEngine.GetRemoteFileName(initial);
        var destination = Path.Combine(directory, name);
        var partial = destination + ".partial";
        var metadataPath = partial + ".json";
        var completedPath = destination + ".pcninja.json";
        foreach (var path in new[] { destination, partial, metadataPath, completedPath, destination + ".pcninja.lock" })
            RejectLink(path);
        // Exact-name lock: concurrent instances cannot replace/reuse a file during a transfer.
        using var downloadLock = new FileStream(destination + ".pcninja.lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var etag = initial.Headers.ETag is { IsWeak: false } strong ? strong.ToString() : null;
        var metadata = new PartialDownload(endpoint.AbsoluteUri, name, etag, total);
        var completed = await ReadMetadataAsync<CompletedDownload>(completedPath, token);
        var ownedFinal = completed is not null && completed.Endpoint == endpoint.AbsoluteUri &&
            completed.FileName == name && completed.Sha256 is { Length: 64 } && completed.Sha256.All(Uri.IsHexDigit);

        if (File.Exists(destination))
        {
            try
            {
                progress?.Report(new(destination, 0, total, null, "Validating the existing ISO..."));
                var length = new FileInfo(destination).Length;
                if (length < minimumBytes || (total.HasValue && length != total))
                    throw new CorruptDownloadException("The existing ISO size does not match the download.");
                var hash = await ValidateExistingAsync(destination, expectedSha256 ?? (ownedFinal ? completed!.Sha256 : null),
                    token, validateExistingIso, validateMedia);
                await SaveCompletedAsync(completedPath, new(endpoint.AbsoluteUri, name, length, hash), token);
                await SaveCompletedAsync(pointerPath, new(endpoint.AbsoluteUri, name, length, hash), token);
                AppLog.Info($"Reusing exact validated download: {destination}. SHA256: {hash}.");
                return new(destination, length, total, hash, Reused: true);
            }
            catch (CorruptDownloadException)
            {
                if (!ownedFinal)
                    throw new InvalidOperationException("The existing ISO failed integrity checks but is not a recorded PcNinja download. Move or remove that exact file, then retry.");
                DeleteOwned(destination);
                DeleteOwned(completedPath);
                AppLog.Info($"Removed corrupt owned download before retry: {destination}");
            }
        }

        var previous = await ReadMetadataAsync<PartialDownload>(metadataPath, token);
        var ownedPartial = previous is not null && previous.Endpoint == endpoint.AbsoluteUri && previous.FileName == name;
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (File.Exists(partial) && !ownedPartial)
            throw new InvalidOperationException("An unrecognized partial file already uses this download name. Move or remove it before retrying.");
        var canResume = ownedPartial && previous == metadata && etag is not null && total is > 0 && offset <= total;
        if (File.Exists(partial) && !canResume)
        {
            DeleteOwned(partial);
            DeleteOwned(metadataPath);
            offset = 0;
        }
        if (offset == 0 && File.Exists(partial)) DeleteOwned(partial);
        checkStorage(total.HasValue ? total.Value - offset : null);
        HttpResponseMessage? resumedResponse = null;
        try
        {
            var response = initial;
            if (offset > 0 && offset != total)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Range = new RangeHeaderValue(offset, null);
                request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(etag!));
                resumedResponse = await SendAsync(http, request, token);
                var range = resumedResponse.Content.Headers.ContentRange;
                if (resumedResponse.StatusCode != HttpStatusCode.PartialContent || range?.From != offset ||
                    range.Length != total || range.To != total - 1 || resumedResponse.Headers.ETag?.ToString() != etag)
                {
                    // Restart the exact owned partial, never append unverified bytes or create Copy N files.
                    resumedResponse.Dispose();
                    resumedResponse = null;
                    DeleteOwned(partial);
                    offset = 0;
                    checkStorage(total);
                }
                else response = resumedResponse;
            }

            AppLog.Info($"Download target: {destination}. Partial: {partial}. Resume offset: {offset}.");
            await Compat.WriteAllTextAsync(metadataPath, JsonData.Serialize(metadata), token);
            var received = offset;
            progress?.Report(new(destination, received, total, total is > 0 ? (int)(received * 100 / total.Value) : null));
            if (offset != total || offset == 0)
            {
                using var input = await response.Content.ReadAsStreamAsync(token);
                using var output = new FileStream(partial, offset > 0 ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
                output.Position = offset;
                var buffer = new byte[1024 * 1024];
                var lastReport = Fx.TickCount64;
                while (true)
                {
                    int count;
                    using (var stall = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        stall.CancelAfter(StallTimeout);
                        try { count = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                        { throw new DownloadTransferException("The download stopped responding. Try again to resume it.", ex); }
                        catch (IOException ex)
                        { throw new DownloadTransferException("The connection was interrupted. Try again to resume the download.", ex); }
                    }
                    if (count == 0) break;
                    await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                    received += count;
                    if (total.HasValue && received > total)
                        throw new CorruptDownloadException("The download exceeded its expected size.");
                    if (Fx.TickCount64 - lastReport >= 200)
                    {
                        progress?.Report(new(destination, received, total,
                            total is > 0 ? (int)Compat.Clamp(received * 100 / total.Value, 0, 100) : null));
                        lastReport = Fx.TickCount64;
                    }
                }
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if ((total.HasValue && received != total) || received < minimumBytes)
                throw new DownloadTransferException("The ISO download is incomplete. Try again to resume it.");
            progress?.Report(new(destination, received, total, null, "Checking the ISO checksum..."));
            var finalHash = await CheckedHashAsync(partial, expectedSha256, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination); // Streams are closed before promotion.
            await SaveCompletedAsync(completedPath, new(endpoint.AbsoluteUri, name, received, finalHash), token);
            await SaveCompletedAsync(pointerPath, new(endpoint.AbsoluteUri, name, received, finalHash), token);
            DeleteOwned(metadataPath);
            if (validateMedia is not null) await validateMedia(destination, token);
            progress?.Report(new(destination, received, total, 100));
            AppLog.Info($"Download complete: {destination}. SHA256: {finalHash}.");
            return new(destination, received, total, finalHash, offset > 0);
        }
        catch (CorruptDownloadException)
        {
            // Only this invocation's exact owned partial is discarded. Never delete on cancellation,
            // I/O/access failures, absent media, signature-chain failures or answer-file interference.
            DeleteOwned(partial);
            DeleteOwned(metadataPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLog.Info($"Download cancelled. Exact files retained: {destination}; {partial}");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Download stopped. Exact partial path: {partial}. {ex}");
            throw;
        }
        finally { resumedResponse?.Dispose(); }
    }

    private static async Task<string> ValidateExistingAsync(string path, string? expected, CancellationToken token,
        Func<string, string?, CancellationToken, Task<string>>? validateExistingIso,
        Func<string, CancellationToken, Task>? validateMedia)
    {
        if (validateExistingIso is null)
        {
            var hash = await CheckedHashAsync(path, expected, token);
            if (validateMedia is not null) await validateMedia(path, token);
            return hash;
        }
        try
        {
            var hash = UpgradeEngine.NormalizeSha256(await validateExistingIso(path, expected, token));
            if (expected is not null && !hash.Equals(UpgradeEngine.NormalizeSha256(expected), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The ISO checksum does not match. Download a fresh copy before upgrading.");
            return hash;
        }
        catch (InvalidDataException ex) { throw new CorruptDownloadException(ex.Message); }
    }

    private static async Task<string> CheckedHashAsync(string path, string? expected, CancellationToken token)
    {
        try { return await UpgradeEngine.VerifyIsoHashAsync(path, expected, token); }
        catch (InvalidDataException ex) { throw new CorruptDownloadException(ex.Message); }
    }

    private static async Task<T?> ReadMetadataAsync<T>(string path, CancellationToken token) where T : class
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) return null;
        try { return JsonData.Deserialize<T>(await Compat.ReadAllTextAsync(path, token)); }
        catch (FormatException) { return null; }
    }

    private static Task SaveCompletedAsync(string path, CompletedDownload value, CancellationToken token) =>
        Compat.WriteAllTextAsync(path, JsonData.Serialize(value), token);

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new InvalidOperationException("A download path is a link or directory. Choose a different folder.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void DeleteOwned(string path)
    {
        RejectLink(path);
        File.Delete(path);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken token)
    {
        using (request)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var code = response.StatusCode;
                    response.Dispose();
                    throw new DownloadTransferException($"Download service returned HTTP {(int)code}. Try again or use your browser.");
                }
                return response;
            }
            catch (HttpRequestException ex)
            { throw new DownloadTransferException("Could not connect to the download service. Try again or use your browser.", ex); }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            { throw new DownloadTransferException("The download service did not respond in time. Try again or use your browser.", ex); }
        }
    }
}
