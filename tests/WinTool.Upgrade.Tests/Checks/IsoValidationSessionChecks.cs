// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class IsoValidationSessionChecks
{
    internal static async Task RunAsync()
    {
        using (var test = new Fixture())
        {
            var first = await test.Prepare();
            // Mutation of a caller-owned list cannot change the stored snapshot.
            ((List<InstallationImage>)first.Source.Images!).Clear();
            var second = await test.Prepare();
            Assert(second.ReusedValidation && second.Source.Images!.Count == 1 && test.Hashes == 1 &&
                test.Validations == 1 && test.SafetyChecks == 1 && test.ActualMounts == 0,
                "Exact unchanged ISO did not reuse checksum/images with fresh safety and no mount.");
            test.AssertReleased();

            foreach (var changed in new[]
            {
                test.Stamp with { FileId = 2 }, test.Stamp with { VolumeSerial = 2 },
                test.Stamp with { JournalId = 2 }, test.Stamp with { Usn = 2 }, test.Stamp with { Length = 9 }
            })
            {
                test.Stamp = changed;
                Assert(!(await test.Prepare()).ReusedValidation, "A changed file identity reused validation.");
            }
            test.Mount = test.Mount with { DevicePath = "different-device" };
            Assert(!(await test.Prepare()).ReusedValidation, "Different mount device at the same root reused validation.");
            test.Mount = test.Mount with { VolumeId = "different-volume" };
            Assert(!(await test.Prepare()).ReusedValidation, "Different volume at the same root reused validation.");
            test.Attached = false;
            Assert(!(await test.Prepare()).ReusedValidation && test.ActualMounts == 1,
                "An ejected ISO was not fully revalidated after attaching.");

            var hashes = test.Hashes;
            await MustFail<InvalidDataException>(() => test.Prepare(new string('0', 64)));
            Assert(test.Hashes == hashes, "Changed expected SHA was not compared to the proven actual hash.");
            Assert(!(await test.Prepare()).ReusedValidation, "Failed validation left a reusable snapshot.");
            test.Session.Invalidate();
            Assert(!(await test.Prepare()).ReusedValidation, "Explicit invalidation reused metadata.");
        }

        using (var test = new Fixture { ProofAvailable = false })
        {
            await test.Prepare();
            Assert(!(await test.Prepare()).ReusedValidation && test.Hashes == 2 && test.Validations == 2,
                "Unavailable native proof did not fall back to ordinary checks.");
            test.AssertReleased();
        }
        using (var test = new Fixture { Capture = _ => null })
        {
            await test.Prepare();
            Assert(!(await test.Prepare()).ReusedValidation && test.Validations == 2,
                "A missing journal stamp supplied reusable validation.");
        }
        using (var test = new Fixture())
        {
            test.Capture = call => call == 1 ? test.Stamp : null;
            await test.Prepare();
            test.Capture = null;
            Assert(!(await test.Prepare()).ReusedValidation, "Missing ending proof cached the completed preparation.");
            test.FailSafety = true;
            await MustFail<UnsafeInstallationMediaException>(() => test.Prepare());
            test.AssertReleased();
            test.FailSafety = false;
            Assert(!(await test.Prepare()).ReusedValidation, "A late unsafe-media failure retained cached validation.");
        }
        using (var test = new Fixture())
        {
            await test.Prepare();
            test.FailSafety = true;
            // A path change cannot borrow the first file's mounted-source metadata.
            File.Copy(test.Path, test.OtherPath);
            Assert(!(await test.Session.PrepareAsync(test.OtherPath, null, null, CancellationToken.None)).ReusedValidation,
                "A different exact path borrowed the first path's proof.");
            test.FailSafety = false;
            test.ChangeMountDuringValidation = true;
            test.Session.Invalidate();
            await MustFail<InvalidOperationException>(() => test.Prepare());
            test.AssertReleased();
        }

        using (var test = new Fixture())
        using (var cancellation = new CancellationTokenSource())
        {
            await test.Prepare();
            test.BlockSafety = true;
            var cancelled = test.Prepare(token: cancellation.Token);
            await test.SafetyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await MustFail<OperationCanceledException>(() => cancelled);
            test.AssertReleased();
            test.BlockSafety = false;
            Assert(!(await test.Prepare()).ReusedValidation, "Cancelled reuse retained the previous snapshot.");
        }
        using (var test = new Fixture())
        {
            test.BlockHash = true;
            var first = test.Prepare();
            await test.HashEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = test.Prepare();
            test.ContinueHash.TrySetResult(true);
            var results = await Task.WhenAll(first, second);
            Assert(!results[0].ReusedValidation && results[1].ReusedValidation &&
                test.Hashes == 1 && test.Validations == 1 && test.MaxActiveProofs == 1,
                "Concurrent preparations duplicated hashing/metadata or overlapped proof handles.");
            test.AssertReleased();
        }
        using (var test = new Fixture())
        {
            test.BlockHash = true;
            var preparing = test.Prepare();
            await test.HashEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            test.Session.Invalidate();
            test.ContinueHash.TrySetResult(true);
            await preparing;
            Assert(!(await test.Prepare()).ReusedValidation, "Invalidation during preparation resurrected a snapshot.");
        }
        using (var test = new Fixture())
        {
            test.Mount = test.Mount with { VolumeId = "" };
            await test.Prepare();
            Assert(!(await test.Prepare()).ReusedValidation, "Unavailable mount identity was considered reusable.");
        }
        Console.WriteLine("PASS: ISO session exact identity/mount reuse, fresh safety, immutable metadata, expected SHA, invalidation, cancellation, fallback and serialization. Fake media only; no Setup/mount.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static async Task MustFail<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name} was not reported.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PcNinja-IsoSession-" + Guid.NewGuid().ToString("N"));
        internal string Path { get; }
        internal string OtherPath => System.IO.Path.Combine(_directory, "other.iso");
        internal IsoValidationSession Session { get; }
        internal IsoFileStamp Stamp = new(1, 1, 1, 1, 4);
        internal IsoMountIdentity Mount;
        internal bool ProofAvailable = true, Attached = true, FailSafety, BlockHash, BlockSafety, ChangeMountDuringValidation;
        internal Func<int, IsoFileStamp?>? Capture;
        internal int Hashes, Validations, SafetyChecks, ActualMounts, MaxActiveProofs, ActiveProofs;
        internal TaskCompletionSource<bool> HashEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> ContinueHash = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> SafetyEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Fixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "exact.iso");
            File.WriteAllBytes(Path, [1, 2, 3, 4]);
            Mount = new(Path, "device", "volume", _directory);
            Session = new(path => ProofAvailable ? new Proof(this, path) : null,
                (path, attach, _) =>
                {
                    if (!Attached && !attach) return Task.FromResult<IsoMountIdentity?>(null);
                    if (!Attached) { Attached = true; ActualMounts++; }
                    return Task.FromResult<IsoMountIdentity?>(Mount with { ImagePath = path });
                }, async (stream, token, progress) =>
                {
                    Hashes++;
                    if (BlockHash)
                    {
                        HashEntered.TrySetResult(true);
                        await ContinueHash.Task.WaitAsync(token);
                    }
                    return await UpgradeEngine.HashIsoStreamAsync(stream, token, progress);
                }, (root, _, _) =>
                {
                    Validations++;
                    if (ChangeMountDuringValidation) Mount = Mount with { VolumeId = "changed" };
                    return Task.FromResult(new MountedSource(root, System.IO.Path.Combine(root, "setup.exe"),
                        System.IO.Path.Combine(root, "sources", "install.wim"), new List<InstallationImage>
                        { new(1, "Pro", "Professional", "x64", "10.0.26300.1", "Client", "en-US") }));
                }, async (_, token) =>
                {
                    SafetyChecks++;
                    if (FailSafety) throw new UnsafeInstallationMediaException("late secondary answer file fixture");
                    if (BlockSafety)
                    {
                        SafetyEntered.TrySetResult(true);
                        await Task.Delay(Timeout.Infinite, token);
                    }
                });
        }

        internal Task<IsoPreparationResult> Prepare(string? expected = null, CancellationToken token = default) =>
            Session.PrepareAsync(Path, expected, null, token);

        internal void AssertReleased()
        {
            Assert(ActiveProofs == 0, "An ISO proof handle survived the operation.");
            using var exclusive = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private sealed class Proof : IIsoFileProof
        {
            private readonly Fixture _fixture;
            private int _captures;
            public FileStream Stream { get; }
            internal Proof(Fixture fixture, string path)
            {
                _fixture = fixture;
                Stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fixture.ActiveProofs++;
                fixture.MaxActiveProofs = Math.Max(fixture.MaxActiveProofs, fixture.ActiveProofs);
            }
            public IsoFileStamp? Capture() => _fixture.Capture is { } capture ? capture(++_captures) : _fixture.Stamp;
            public void Dispose() { Stream.Dispose(); _fixture.ActiveProofs--; }
        }
    }
}
