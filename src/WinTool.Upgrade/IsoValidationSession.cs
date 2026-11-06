namespace WinTool.Upgrade;

internal sealed record IsoMountIdentity(string ImagePath, string DevicePath, string VolumeId, string Root,
    bool MountedNow = false)
{
    internal bool CanReuse => !string.IsNullOrWhiteSpace(DevicePath) && !string.IsNullOrWhiteSpace(VolumeId);

    internal bool Matches(IsoMountIdentity? other) => other is not null &&
        Same(ImagePath, other.ImagePath) && Same(DevicePath, other.DevicePath) &&
        Same(VolumeId, other.VolumeId) && Same(Root, other.Root);

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

// One successful source per engine lifetime. Snapshots contain no live file/mount handles.
internal sealed class IsoValidationSession(
    Func<string, IIsoFileProof?> openProof,
    Func<string, bool, CancellationToken, Task<IsoMountIdentity?>> resolveMount,
    Func<Stream, CancellationToken, IProgress<string>?, Task<string>> hashStream,
    Func<string, CancellationToken, IProgress<string>?, Task<MountedSource>> validateSource,
    Func<string, CancellationToken, Task> checkSafety)
{
    private sealed record Snapshot(string Path, IsoFileStamp Stamp, IsoMountIdentity Mount,
        string Hash, MountedSource Source, long Generation);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;
    private long _generation;

    internal void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _snapshot, null);
    }

    internal async Task<IsoPreparationResult> PrepareAsync(string path, string? expected,
        IProgress<string>? progress, CancellationToken token)
    {
        try { await _gate.WaitAsync(token).ConfigureAwait(false); }
        catch { Invalidate(); throw; }
        try
        {
            path = System.IO.Path.GetFullPath(path);
            expected = expected is null ? null : UpgradeEngine.NormalizeSha256(expected);
            var generation = Volatile.Read(ref _generation);
            var previous = Interlocked.Exchange(ref _snapshot, null);
            using var proof = openProof(path);
            var stamp = proof?.Capture();
            if (stamp is not null && previous is not null && previous.Generation == generation && previous.Stamp == stamp &&
                string.Equals(previous.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                var attached = await resolveMount(path, false, token).ConfigureAwait(false);
                if (previous.Mount.CanReuse && previous.Mount.Matches(attached))
                {
                    CheckExpected(previous.Hash, expected);
                    progress?.Report("Reusing the unchanged ISO; rechecking signature and media safety...");
                    await checkSafety(attached!.Root, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (proof!.Capture() == stamp && attached.Matches(
                        await resolveMount(path, false, token).ConfigureAwait(false)))
                    {
                        token.ThrowIfCancellationRequested();
                        Store(previous, generation);
                        AppLog.Info($"Reused session ISO hash and image metadata after unchanged-file and exact mount proof: {path}.");
                        return new(previous.Source, previous.Hash, ReusedValidation: true);
                    }
                }
            }

            // Unsupported filesystems/journals still work, with the ordinary full checks.
            using var fallback = proof is null ? new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan) : null;
            var stream = (Stream?)proof?.Stream ?? fallback!;
            stream.Position = 0;
            progress?.Report("Checking the ISO checksum...");
            var hash = await hashStream(stream, token, progress).ConfigureAwait(false);
            CheckExpected(hash, expected);
            AppLog.Info($"ISO SHA256: {hash}. Trusted expected hash supplied: {expected is not null}.");
            progress?.Report("Locating the ISO's mounted drive...");
            var mount = await resolveMount(path, true, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected ISO's mounted drive could not be identified.");
            if (!string.Equals(path, mount.ImagePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The mounted ISO does not match the selected file. Refresh and try again.");
            var source = await validateSource(mount.Root, token, progress).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!mount.Matches(await resolveMount(path, false, token).ConfigureAwait(false)))
                throw new InvalidOperationException("Installation media changed during validation. Refresh and validate it again.");
            if (stamp is not null && proof!.Capture() == stamp && mount.CanReuse)
                Store(new(path, stamp, mount, hash, source with
                    { Images = Array.AsReadOnly((source.Images ?? []).ToArray()) }, generation), generation);
            else
                AppLog.Info("ISO validation completed; unchanged-file proof unavailable, so later preparation will run full checks.");
            return new(source, hash);
        }
        catch
        {
            Invalidate();
            throw;
        }
        finally { _gate.Release(); }
    }

    private void Store(Snapshot snapshot, long generation)
    {
        if (Volatile.Read(ref _generation) == generation) Interlocked.Exchange(ref _snapshot, snapshot);
    }

    private static void CheckExpected(string hash, string? expected)
    {
        if (expected is not null && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ISO checksum does not match. Download a fresh copy before upgrading.");
    }
}
