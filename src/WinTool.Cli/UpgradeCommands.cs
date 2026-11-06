using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WinTool.Core;
using WinTool.Core.Json;
using WinTool.Core.Operations;
using WinTool.Upgrade;

namespace WinTool.Cli
{
    /// <summary>
    /// "upgrade status|downloads|scan|validate|run": the Windows Upgrade Tool's
    /// command line on the same Core operations as the window (WUP spec).
    /// The JSON result contract (schemaVersion 1, resultCode, readyForRestart, ...)
    /// is the original tool's; the exit codes are WinTool's (decision D-021).
    /// The command line never asks a question: "run" needs --yes or --dry-run.
    /// </summary>
    public sealed class UpgradeCommands
    {
        public const string UpgradePreparedRestartRequired = "UPGRADE_PREPARED_RESTART_REQUIRED";

        private static readonly string[] ReadOnlySwitches = { "json" };
        private static readonly string[] ValidateValues = { "iso", "mounted", "sha256", "image-index" };
        private static readonly string[] RunSwitches = { "download", "no-restart", "quiet", "dry-run", "yes", "json" };
        private static readonly string[] RunValues = { "iso", "mounted", "destination", "sha256", "image-index" };

        private static volatile bool setupRunning;

        private readonly Func<UpgradeEngine> createEngine;
        private readonly Func<bool> isAdministrator;
        private readonly TextWriter output;
        private readonly TextWriter error;

        public UpgradeCommands(Func<UpgradeEngine> createEngine, Func<bool> isAdministrator, TextWriter output, TextWriter error)
        {
            this.createEngine = createEngine;
            this.isAdministrator = isAdministrator;
            this.output = output;
            this.error = error;
        }

        public int Run(string command, string[] rest)
        {
            var json = rest.Any(token => token.Equals("--json", StringComparison.OrdinalIgnoreCase));
            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler onCancel = (sender, e) =>
                {
                    // Ctrl+C stops download, checksum and preparation work, never a running Setup.
                    e.Cancel = true;
                    if (!setupRunning) cancellation.Cancel();
                };
                Console.CancelKeyPress += onCancel;
                try
                {
                    return RunAsync(command, rest, cancellation.Token).GetAwaiter().GetResult();
                }
                catch (UsageException ex)
                {
                    return Fail(CliExit.Usage, ex.Message, json);
                }
                catch (OperationCanceledException)
                {
                    return Fail(CliExit.Cancelled, "Operation cancelled. Windows Setup was not started.", json);
                }
                catch (OperationBusyException ex)
                {
                    return Fail(CliExit.Busy, ex.Message, json);
                }
                catch (Exception ex)
                {
                    AppLog.Error("CLI failure: " + ex);
                    return Fail(CliExit.Failed, ex.Message, json);
                }
                finally
                {
                    Console.CancelKeyPress -= onCancel;
                    setupRunning = false;
                }
            }
        }

        private Task<int> RunAsync(string command, string[] rest, CancellationToken token)
        {
            switch (command)
            {
                case "status": return Task.FromResult(Status(ReadOnlyArguments(rest)));
                case "downloads": return Downloads(ReadOnlyArguments(rest), token);
                case "scan": return Task.FromResult(Scan(ReadOnlyArguments(rest)));
                case "validate":
                    return ValidateOrRun(Options.From(Parse(rest, ReadOnlySwitches, ValidateValues), false), false, token);
                case "run":
                    return ValidateOrRun(Options.From(Parse(rest, RunSwitches, RunValues), true), true, token);
                default:
                    throw new UsageException("Unknown command: upgrade " + command + ". Run \"WinTool-by-PcNinja.exe help\".");
            }
        }

        private static Arguments ReadOnlyArguments(string[] rest) => Parse(rest, ReadOnlySwitches, null);

        private static Arguments Parse(string[] rest, string[] switches, string[] values)
        {
            var a = Arguments.Parse(rest, switches, values);
            a.NoPositional();
            return a;
        }

        // ---------------------------------------------------------------- read-only

        private int Status(Arguments a)
        {
            var engine = createEngine();
            var storage = engine.CheckSystemDriveSpace();
            var profile = DownloadSystem.Read();
            var automaticSource = DownloadSourcePolicy.GetAutomaticSource(profile);
            string supportError = null;
            try { engine.ValidateSupportedComputer(); }
            catch (Exception ex) { supportError = ex.Message; }
            var previous = UpgradeHistory.ReadStatus();
            var unavailable = AutomaticDownloadPolicy.GetUnavailableReason(profile);
            var value = new
            {
                operatingSystem = engine.GetOperatingSystemDescription(),
                upgradeSupported = supportError == null,
                supportError,
                systemUiLanguage = profile.Language,
                automaticDownloadAvailable = AutomaticDownloadPolicy.IsAvailable(profile),
                automaticDownloadUnavailableReason = unavailable,
                downloadProfile = new
                {
                    edition = profile.Edition, language = profile.Language, build = profile.Build,
                    revision = profile.Revision, architecture = profile.Architecture, installationType = profile.InstallationType,
                    isLtsc = profile.IsLtsc, isServer = profile.IsServer, isSupportedClient = profile.IsSupportedClient
                },
                automaticDownloadSourceId = automaticSource?.Id,
                automaticDownloadSourceName = automaticSource?.Name,
                pcNinjaDownloadPage = DownloadSourcePolicy.GetPcNinjaBrowserPage(profile),
                isAdministrator = isAdministrator(),
                systemDrive = storage.RootPath,
                systemDriveFreeBytes = storage.AvailableBytes,
                systemDriveReady = storage.IsValid,
                previousUpgrade = previous
            };
            Write(value, a.Has("json"),
                "OS: " + value.operatingSystem +
                "\nUpgrade supported: " + YesNo(value.upgradeSupported) + (supportError == null ? "" : " (" + supportError + ")") +
                "\nSystem UI language: " + profile.Language +
                "\nAutomatic download available: " + YesNo(value.automaticDownloadAvailable) +
                (unavailable == null ? "" : "\n" + unavailable) +
                "\nDownload profile: " + profile.Edition + ", " + profile.Architecture + ", " + profile.InstallationType +
                ", build " + profile.Build + "." + profile.Revision +
                "\nPcNinja download page: " + value.pcNinjaDownloadPage +
                "\nAdministrator: " + YesNo(value.isAdministrator) +
                "\nSystem storage: " + storage.Message +
                (previous == null ? "" : "\nPrevious upgrade: " + previous.State + ". " + previous.Message),
                "STATUS_REPORTED");
            return CliExit.Ok;
        }

        private async Task<int> Downloads(Arguments a, CancellationToken token)
        {
            var json = a.Has("json");
            DownloadRecommendation recommendation;
            try
            {
                recommendation = await createEngine().GetDownloadRecommendationAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Error("CLI download recommendation failed: " + ex);
                return Fail(CliExit.DownloadFailed, "Could not retrieve a matching download recommendation. " + ex.Message, json);
            }

            Write(new { recommendation, automaticTransferStarted = false, setupStarted = false }, json,
                recommendation.Message + "\nSource: " + recommendation.Source + "\nDownload page: " + recommendation.PageUrl +
                (recommendation.FileName == null ? "" : "\nFile: " + recommendation.FileName) +
                (recommendation.TargetRelease == null ? "" : "\nTarget release: " + recommendation.TargetRelease) +
                (recommendation.TargetVersion == null ? "" : "\nTarget version: " + recommendation.TargetVersion) +
                "\nThis command does not download an ISO or open a browser. Download the matching ISO, " +
                "then run: upgrade validate --iso <path>",
                "DOWNLOAD_RECOMMENDATION");
            return CliExit.Ok;
        }

        private int Scan(Arguments a)
        {
            var engine = createEngine();
            var local = engine.FindLocalIsos();
            var mounted = engine.FindMountedSources();
            var drives = engine.FindDownloadLocations();
            Write(new { localIsos = local, mountedSources = mounted, downloadLocations = drives }, a.Has("json"),
                "Local ISO files:" + List(local) +
                "\nMounted Windows sources:" + List(mounted.Select(source => source.Root)) +
                "\nDownload locations:" + List(drives.Select(drive => drive.ToString())),
                "SOURCES_SCANNED");
            return CliExit.Ok;
        }

        // ---------------------------------------------------------------- validate / run

        private async Task<int> ValidateOrRun(Options options, bool startSetup, CancellationToken token)
        {
            var engine = createEngine();
            try { engine.ValidateSupportedComputer(); }
            catch (Exception ex) { return Fail(CliExit.UpgradeBlocked, ex.Message, options.Json); }

            if (!isAdministrator())
                return Fail(CliExit.AdministratorRequired, "Run this command from an elevated Command Prompt or PowerShell window.", options.Json);

            if (options.Download && AutomaticDownloadPolicy.GetUnavailableReason(DownloadSystem.Read()) is string reason)
                return Fail(CliExit.UpgradeBlocked, reason + " Run \"upgrade downloads\" for a matching manual download, " +
                    "then use --iso <path>. No automatic transfer or Setup was started.", options.Json);

            var storage = engine.CheckSystemDriveSpace();
            if (!storage.IsValid) return Fail(CliExit.UpgradeBlocked, storage.Message, options.Json);

            MountedSource source;
            string exactIsoPath = null;
            try
            {
                if (options.Download)
                {
                    var destination = options.Destination ?? DefaultDestination(engine);
                    var progress = options.Json || options.Quiet ? null : new Progress<DownloadProgress>(item =>
                    {
                        var text = item.Stage ?? (item.Percentage.HasValue
                            ? item.Percentage.Value.ToString(CultureInfo.InvariantCulture) + "%"
                            : UpgradeEngine.FormatBytes(item.BytesReceived));
                        error.Write("\rDownloading " + Path.GetFileName(item.DestinationPath) + ": " + text + "     ");
                    });
                    DownloadResult download;
                    try
                    {
                        download = await engine.DownloadIsoAsync(destination, progress, token, options.Sha256).ConfigureAwait(false);
                        if (progress != null) error.WriteLine();
                    }
                    catch (DownloadTransferException ex)
                    {
                        if (progress != null) error.WriteLine();
                        // The command line never opens a browser; it names the page instead.
                        return Fail(CliExit.DownloadFailed, ex.Message + " Automatic download did not complete; download the ISO from " +
                            DownloadSourcePolicy.GetPcNinjaBrowserPage(DownloadSystem.Read()) + " and use --iso <path>.", options.Json);
                    }

                    exactIsoPath = download.IsoPath;
                    source = download.ValidatedSource
                        ?? throw new InvalidOperationException("The downloaded ISO did not complete media validation.");
                }
                else if (options.IsoPath != null)
                {
                    exactIsoPath = Path.GetFullPath(options.IsoPath.Trim('"'));
                    source = (await engine.PrepareIsoAsync(exactIsoPath, options.Sha256 ?? UpgradeEngine.ConfiguredIsoSha256, token)
                        .ConfigureAwait(false)).Source;
                }
                else
                {
                    source = await engine.ValidateMountedSourceAsync(options.MountedRoot, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (OperationBusyException) { throw; }
            catch (Exception ex)
            {
                return Fail(CliExit.UpgradeBlocked, ex.Message, options.Json);
            }

            var current = CurrentWindows.Read();
            InstallationImage selectedImage = null;
            try
            {
                if (options.ImageIndex.HasValue || (startSetup && !options.DryRun))
                    selectedImage = InstallationImages.Select(source, options.ImageIndex, current);
            }
            catch (InvalidOperationException ex) { return Fail(CliExit.UpgradeBlocked, ex.Message, options.Json); }

            var matching = InstallationImages.Compatible(source, current);
            var preview = selectedImage ?? (matching.Count == 1 ? matching[0] : null);
            var previewProfile = preview == null ? null : InstallationImages.GetSetupProfile(preview);
            if (!startSetup || options.DryRun)
            {
                Write(new
                    {
                        valid = true, isoPath = exactIsoPath, mountedRoot = source.Root, setupPath = source.SetupPath,
                        imagePath = source.ImagePath, images = source.Images,
                        compatibleImageIndexes = matching.Select(image => image.Index).ToList(),
                        selectedImageIndex = preview?.Index, setupProfile = previewProfile?.ToString(),
                        quiet = options.Quiet, dryRun = options.DryRun,
                        buildOrderChecked = previewProfile == null ? (bool?)null : previewProfile != WindowsSetupProfile.Windows10,
                        policyNotice = previewProfile == WindowsSetupProfile.Windows10 ? InstallationImages.Windows10Notice : null
                    },
                    options.Json,
                    "Validation passed.\nSource: " + source.Root + "\nImage file: " + source.ImagePath + "\n" +
                    string.Join("\n", (source.Images ?? new InstallationImage[0]).Select(image => image.ToString())) +
                    "\nCompatible indexes: " + string.Join(", ", matching.Select(image => image.Index)) +
                    (previewProfile == WindowsSetupProfile.Windows10 ? "\n" + InstallationImages.Windows10Notice : ""),
                    startSetup ? "DRY_RUN_COMPLETED" : "VALIDATION_PASSED");
                return CliExit.Ok;
            }

            token.ThrowIfCancellationRequested();
            UpgradeResult result;
            try
            {
                result = await engine.StartUpgradeAsync(source, selectedImage.Index, !options.NoRestart, token,
                    () => setupRunning = true, quiet: options.Quiet).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (OperationBusyException) { throw; }
            catch (InvalidOperationException ex) { return Fail(CliExit.UpgradeBlocked, ex.Message, options.Json); }

            return ReportSetupResult(result, options, source, selectedImage, exactIsoPath);
        }

        private int ReportSetupResult(UpgradeResult result, Options options, MountedSource source, InstallationImage image, string isoPath)
        {
            if (result.ExitCode == 0x40010004)
                return Fail(CliExit.SetupOutcomeUnconfirmed, "Setup exited during the transition. The upgrade outcome is not confirmed. " +
                    "After restarting, run \"upgrade status --json\" and check your files and apps.", options.Json, result.ExitCode);
            if (result.ExitCode != 0)
                return Fail(CliExit.SetupFailed, "Windows Setup stopped with exit code " + result.ExitCode +
                    ". No restart was forced.", options.Json, result.ExitCode);

            var manualRestart = result.NeedsManualRestartNotice(!options.NoRestart);
            var profile = InstallationImages.GetSetupProfile(image);
            Write(new
                {
                    started = true, setupExitCode = result.ExitCode, automaticRestart = !options.NoRestart,
                    setupExitCodeHex = Hex(result.ExitCode), quiet = options.Quiet,
                    manualRestartRequired = manualRestart,
                    setupProfile = profile?.ToString(),
                    buildOrderChecked = profile != WindowsSetupProfile.Windows10,
                    isoPath, mountedRoot = source.Root, imageIndex = image.Index,
                    outcome = "PreparationCompleted"
                },
                options.Json,
                manualRestart ? UpgradeResult.ManualRestartMessage : "Windows Setup completed its current stage. Check the result after restarting.",
                options.NoRestart ? UpgradePreparedRestartRequired : "UPGRADE_PREPARED_AUTOMATIC_RESTART",
                manualRestart);
            return CliExit.Ok;
        }

        private static string DefaultDestination(UpgradeEngine engine)
        {
            var location = engine.FindDownloadLocations().FirstOrDefault(item => item.DriveType == DriveType.Fixed)
                ?? throw new InvalidOperationException("No suitable download drive was found. Use --destination <folder>.");
            return Path.Combine(location.RootPath, "PcNinja Downloads");
        }

        // ---------------------------------------------------------------- results

        private static readonly Dictionary<int, string> FailureCodes = new Dictionary<int, string>
        {
            { CliExit.Usage, "INVALID_ARGUMENTS" },
            { CliExit.Cancelled, "CANCELLED" },
            { CliExit.AdministratorRequired, "ADMINISTRATOR_REQUIRED" },
            { CliExit.Busy, "OPERATION_RUNNING" },
            { CliExit.UpgradeBlocked, "VALIDATION_FAILED" },
            { CliExit.DownloadFailed, "DOWNLOAD_FAILED" },
            { CliExit.SetupFailed, "SETUP_FAILED" },
            { CliExit.SetupOutcomeUnconfirmed, "SETUP_OUTCOME_UNCONFIRMED" },
        };

        private int Fail(int exitCode, string message, bool json, int? setupExitCode = null)
        {
            string resultCode;
            if (!FailureCodes.TryGetValue(exitCode, out resultCode)) resultCode = "UNEXPECTED_ERROR";
            var hex = setupExitCode.HasValue ? Hex(setupExitCode.Value) : null;
            var payload = Result(new { error = message, setupExitCode, setupExitCodeHex = hex }, resultCode, exitCode, message, false);
            AppLog.Info("CLI result: " + resultCode + "; exit: " + exitCode + "; Setup: " + (hex ?? "not reported") + "; " + message);
            if (json) output.WriteLine(JsonText.Serialize(payload));
            else error.WriteLine("ERROR: " + message + "\nRESULT_CODE=" + resultCode + "; EXIT_CODE=" + exitCode +
                (hex == null ? "" : "; SETUP_EXIT_CODE=" + hex));
            return exitCode;
        }

        private void Write(object value, bool json, string text, string resultCode, bool readyForRestart = false)
        {
            var payload = Result(value, resultCode, CliExit.Ok, text, readyForRestart);
            AppLog.Info("CLI result: " + resultCode + "; exit: 0; readyForRestart: " + readyForRestart + ".");
            output.WriteLine(json ? JsonText.Serialize(payload)
                : text + "\nRESULT_CODE=" + resultCode + "; EXIT_CODE=0; READY_FOR_RESTART=" + (payload["readyForRestart"].Equals(true) ? "true" : "false"));
        }

        /// <summary>
        /// The original tool's automation result (schemaVersion 1). Only a real,
        /// successful preparation with a manual restart can set readyForRestart.
        /// </summary>
        internal static Dictionary<string, object> Result(object details, string resultCode, int exitCode, string message, bool readyForRestart)
        {
            var payload = (Dictionary<string, object>)AsWritten(details, 0);
            payload["schemaVersion"] = 1;
            payload["success"] = exitCode == 0;
            payload["exitCode"] = exitCode;
            payload["resultCode"] = resultCode;
            payload["message"] = message;
            payload["readyForRestart"] = exitCode == 0 && resultCode == UpgradePreparedRestartRequired && readyForRestart;
            payload["logPath"] = AppLog.Path;
            payload["version"] = ProductInfo.Version;
            payload["build"] = ProductInfo.InformationalVersion;
            return payload;
        }

        /// <summary>
        /// Objects as the original tool's System.Text.Json output named them:
        /// property names unchanged and enums as numbers, so existing scripts keep working.
        /// </summary>
        internal static object AsWritten(object value, int depth)
        {
            if (value == null || depth > 16) return null;
            if (value is string || value is bool || value is Guid) return value;
            if (value is Enum) return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (value is DateTime time) return time.ToString("o", CultureInfo.InvariantCulture);
            if (value is DateTimeOffset offset) return offset.ToString("o", CultureInfo.InvariantCulture);
            if (value.GetType().IsPrimitive || value is decimal) return value;
            if (value is IEnumerable sequence) return sequence.Cast<object>().Select(item => AsWritten(item, depth + 1)).ToList();
            var map = new Dictionary<string, object>();
            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                // Records expose a compiler-generated EqualityContract only to derived types; skip anything non-public.
                map[property.Name] = AsWritten(property.GetValue(value, null), depth + 1);
            }

            return map;
        }

        private static string Hex(int code) => "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture);
        private static string YesNo(bool value) => value ? "yes" : "no";

        private static string List(IEnumerable<string> items)
        {
            var values = items.ToList();
            return values.Count == 0 ? "\n  (none)" : string.Concat(values.Select(v => "\n  " + v));
        }

        private sealed class Options
        {
            public string IsoPath, MountedRoot, Destination, Sha256;
            public int? ImageIndex;
            public bool Download, NoRestart, Quiet, DryRun, Json;

            public static Options From(Arguments a, bool run)
            {
                var options = new Options
                {
                    IsoPath = a.Value("iso"),
                    MountedRoot = a.Value("mounted"),
                    Destination = a.Value("destination"),
                    ImageIndex = a.Int("image-index"),
                    Download = a.Has("download"),
                    NoRestart = a.Has("no-restart"),
                    Quiet = a.Has("quiet"),
                    DryRun = a.Has("dry-run"),
                    Json = a.Has("json")
                };
                var sha = a.Value("sha256");
                if (sha != null)
                {
                    try { options.Sha256 = UpgradeEngine.NormalizeSha256(sha); }
                    catch (ArgumentException ex) { throw new UsageException(ex.Message); }
                }

                if (options.ImageIndex.HasValue && options.ImageIndex.Value < 1)
                    throw new UsageException("--image-index must be a positive whole number.");
                var sources = (options.IsoPath == null ? 0 : 1) + (options.MountedRoot == null ? 0 : 1) + (options.Download ? 1 : 0);
                if (sources != 1)
                    throw new UsageException(run
                        ? "Choose exactly one source: --iso <path>, --mounted <drive or folder>, or --download."
                        : "Choose exactly one source: --iso <path> or --mounted <drive or folder>.");
                if (options.Destination != null && !options.Download) throw new UsageException("--destination requires --download.");
                if (options.Sha256 != null && options.MountedRoot != null) throw new UsageException("--sha256 requires --iso or --download.");
                if (run && !a.Has("yes") && !options.DryRun)
                    throw new UsageException("\"upgrade run\" starts Windows Setup. Add --yes to confirm, or --dry-run to check only. Nothing was started.");
                return options;
            }
        }
    }
}
