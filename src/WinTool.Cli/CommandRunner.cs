using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WinTool.Core;
using WinTool.Core.IO;
using WinTool.Core.Json;
using WinTool.Core.Updates;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Migration;
using WinTool.WindowsUpdate.Model;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.Cli
{
    /// <summary>
    /// The command line (decision D-016, spec WU-86 to WU-91). Output is
    /// English text, or JSON with --json, so scripts work in every UI language.
    /// </summary>
    public sealed class CommandRunner
    {
        private static readonly string[] ScopeSwitches = { "windows", "optional", "drivers", "firmware" };

        private readonly IWindowsUpdateEnvironment env;
        private readonly TextWriter output;
        private readonly TextWriter error;
        private readonly Func<string> installedFolder;

        public CommandRunner(IWindowsUpdateEnvironment env, TextWriter output, TextWriter error, Func<string> installedFolder = null)
        {
            this.env = env;
            this.output = output;
            this.error = error;
            this.installedFolder = installedFolder ?? InstalledCopy.ReadInstallFolder;
        }

        /// <summary>Areas this runner handles; the app handles version, help and self-test.</summary>
        public static bool Handles(string area)
        {
            switch ((area ?? string.Empty).ToLowerInvariant())
            {
                case "update":
                case "schedule":
                case "status":
                case "logs":
                case "upgrade":
                case "office":
                case "app-update":
                    return true;
                default:
                    return false;
            }
        }

        public int Run(string[] args)
        {
            try
            {
                var area = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
                var command = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
                var rest = args.Skip(area == "status" ? 1 : 2).ToArray();

                switch (area)
                {
                    case "status": return Status(rest);
                    case "update":
                        switch (command)
                        {
                            case "scan": return UpdateScan(rest);
                            case "run": return UpdateRun(rest);
                            case "reset": return UpdateReset(rest);
                            case "health": return UpdateHealth(rest);
                            case "start-as-system": return UpdateStartAsSystem(rest);
                        }

                        break;
                    case "schedule":
                        switch (command)
                        {
                            case "show": return ScheduleShow(rest);
                            case "set": return ScheduleSet(rest);
                            case "remove": return ScheduleRemove(rest);
                            case "import": return ScheduleImport(rest);
                            case "import-v2": return ScheduleImportV2(rest);
                        }

                        break;
                    case "logs":
                        if (command == "collect") return LogsCollect(rest);
                        break;
                    case "app-update":
                        return new AppUpdateCommands(() => new WinTool.Core.Updates.AppUpdater(), env.ExecutablePath, output, error).Run(command, rest);
                    case "office":
                        return new OfficeCommands(() => new WinTool.Office.OfficeDeployment(), () => env.IsAdministrator, output, error)
                            .Run(command, rest);
                    case "upgrade":
                        return new UpgradeCommands(() => new WinTool.Upgrade.UpgradeEngine(), () => env.IsAdministrator, output, error)
                            .Run(command, rest);
                }

                throw new UsageException("Unknown command: " + string.Join(" ", args.Take(2)) + ". Run \"WinTool-by-PcNinja.exe help\".");
            }
            catch (InstalledCopyRequiredException ex)
            {
                if (args.Any(a => a == "--json")) Json(new { Result = "InstalledCopyRequired", ExitCode = CliExit.InstalledCopyRequired, Message = ex.Message });
                else error.WriteLine(ex.Message);
                return CliExit.InstalledCopyRequired;
            }
            catch (UsageException ex)
            {
                error.WriteLine(ex.Message);
                return CliExit.Usage;
            }
        }

        // ---------------------------------------------------------------- update

        private int UpdateScan(string[] rest)
        {
            var a = Arguments.Parse(rest, ScopeSwitches.Concat(new[] { "json" }), null);
            a.NoPositional();
            var settings = env.Store.LoadSettings();
            var scope = ScopeFrom(a, settings);
            ScanResult scan;
            try
            {
                scan = new ScanService(env.CreateAgent(), env.Log).Scan(scope, null, "Scan");
            }
            catch (UpdateAgentException ex)
            {
                return Fail(a, ex.Describe());
            }

            var updates = scan.Updates.Select(u => new
            {
                u.Update.Key,
                u.Update.Title,
                Kb = u.Update.KbText,
                Kind = u.Kind,
                Firmware = u.IsFirmware,
                u.Included,
                Excluded = u.Excluded == ExclusionReason.None ? null : u.Excluded.ToString(),
                Sources = u.Update.Sources,
                DownloadMegabytes = Math.Round(u.Update.MaxDownloadSize / 1048576.0, 1)
            }).ToList();

            if (a.Has("json"))
            {
                Json(new
                {
                    Result = "Succeeded",
                    Scope = scope.ToString(),
                    scan.Important,
                    scan.Optional,
                    scan.Drivers,
                    scan.FirmwareSkipped,
                    scan.ScopeSkipped,
                    scan.Total,
                    Updates = updates
                });
                return CliExit.Ok;
            }

            output.WriteLine("Scope: " + scope);
            output.WriteLine("Important: " + scan.Important + "   Optional: " + scan.Optional + "   Drivers: " + scan.Drivers
                + "   Firmware skipped: " + scan.FirmwareSkipped + "   Outside scope: " + scan.ScopeSkipped);
            if (scan.Updates.Count == 0)
            {
                output.WriteLine("No updates are available.");
                return CliExit.Ok;
            }

            output.WriteLine();
            foreach (var u in scan.Updates)
            {
                var label = u.IsFirmware ? "Firmware" : u.Kind == UpdateKind.Windows ? "Important" : u.Kind.ToString();
                output.WriteLine("  [" + label + "] " + u.Update.Title
                    + (u.Update.KbText.Length > 0 && u.Update.Title.IndexOf(u.Update.KbText, StringComparison.OrdinalIgnoreCase) < 0 ? " (" + u.Update.KbText + ")" : string.Empty)
                    + (u.Included ? string.Empty : "  - skipped: " + u.Excluded.ToString().ToLowerInvariant()));
            }

            return CliExit.Ok;
        }

        private int UpdateRun(string[] rest)
        {
            var a = Arguments.Parse(rest, ScopeSwitches.Concat(new[] { "scheduled", "retry", "startup", "wake", "as-system-task", "snooze", "json" }), null);
            a.NoPositional();
            if (!env.IsAdministrator) return AdminRequired(a);

            var types = new[] { "scheduled", "retry", "startup", "wake" }.Where(a.Has).ToList();
            if (types.Count > 1) throw new UsageException("Use only one of --scheduled, --retry, --startup and --wake.");
            var type = types.Count == 0 ? RunType.Manual : (RunType)Enum.Parse(typeof(RunType), types[0], true);
            var explicitScope = ScopeSwitches.Any(a.Has);
            var snooze = a.Has("snooze");
            if (snooze && type != RunType.Manual) throw new UsageException("--snooze is only allowed for manual runs.");
            // Refuse before scanning/installing when this run can create an automatic retry task.
            if (env.Store.LoadSettings().AutoRetryEnabled) Schedule().RequireInstalled();

            var request = new RunRequest
            {
                Type = type,
                Scope = explicitScope ? ScopeFrom(a, env.Store.LoadSettings()) : null,
                OnWindowsBusy = snooze ? new Func<WindowsUpdateActivity, BusyDecision>(_ => BusyDecision.Snooze) : null
            };

            var runner = new UpdateRunner(env.CreateAgent(), env.Probe, env.Store, Retries(), env.Log, env.Clock, env.TryAcquireOperationLock);
            var progress = a.Has("json") ? null : new ConsoleProgress(output);
            var outcome = runner.Run(request, progress, default(System.Threading.CancellationToken), interactive: false);
            var exit = ExitCodes.For(outcome.Result);

            if (a.Has("json"))
            {
                Json(new
                {
                    Result = outcome.Result,
                    ExitCode = exit,
                    outcome.Message,
                    outcome.UpdatesFound,
                    outcome.PassesCompleted,
                    outcome.RebootRequired,
                    outcome.RetryAt,
                    Updates = outcome.Items.Select(i => new { i.Update.Title, Kb = i.Update.KbText, Result = i.Result, HResult = i.HResult == 0 ? null : HResults.ToHex(i.HResult) })
                });
                return exit;
            }

            output.WriteLine();
            output.WriteLine("Result: " + outcome.Result + " (exit code " + exit + ")");
            output.WriteLine(outcome.Message);
            if (outcome.UpdatesFound > 0) output.WriteLine("Updates found: " + outcome.UpdatesFound + ", passes completed: " + outcome.PassesCompleted);
            foreach (var failed in outcome.Items.Where(i => !i.Succeeded))
            {
                output.WriteLine("  Failed: " + failed.Update.Title + " " + HResults.Describe(failed.Result.ToString(), failed.HResult));
            }

            if (outcome.RebootRequired) output.WriteLine("Restart the computer to finish installing updates.");
            return exit;
        }

        private int UpdateReset(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "confirm", "json" }, null);
            a.NoPositional();
            if (!a.Has("confirm"))
            {
                throw new UsageException("Resetting Windows Update stops the update services and rebuilds SoftwareDistribution and catroot2. Add --confirm to continue.");
            }

            if (!env.IsAdministrator) return AdminRequired(a);
            using (var held = env.TryAcquireOperationLock())
            {
                if (held == null) return Busy(a);
                var result = env.CreateReset().Run();
                var exit = result.Succeeded ? CliExit.Ok : CliExit.Warning;
                if (a.Has("json"))
                {
                    Json(new { Result = result.Succeeded ? "Succeeded" : "SucceededWithWarnings", ExitCode = exit, result.Message, result.Catroot2Backup, result.Warnings });
                }
                else
                {
                    output.WriteLine(result.Message);
                    if (result.Catroot2Backup != null) output.WriteLine("Previous catroot2 kept as: " + result.Catroot2Backup);
                }

                return exit;
            }
        }

        private int UpdateHealth(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "no-network", "json" }, null);
            a.NoPositional();
            var facts = env.CollectHealthFacts(!a.Has("no-network"));
            var settings = env.Store.LoadSettings();
            if (settings.ScheduleEnabled) facts.ScheduledTaskMatchesSettings = Schedule().GetStatus(settings).MatchesSettings;

            var items = HealthCheck.Evaluate(facts);
            var overall = HealthCheck.Overall(items);
            var exit = overall == HealthStatus.Ok ? CliExit.Ok : overall == HealthStatus.Warning ? CliExit.Warning : CliExit.Failed;
            if (a.Has("json"))
            {
                Json(new { Overall = overall, ExitCode = exit, Items = items });
                return exit;
            }

            output.WriteLine("Health: " + overall);
            foreach (var item in items) output.WriteLine("  " + item.Status.ToString().PadRight(8) + item.Detail);
            return exit;
        }

        private int UpdateStartAsSystem(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json" }, null);
            a.NoPositional();
            if (!env.IsAdministrator) return AdminRequired(a);
            Schedule().StartRunOnce();
            if (a.Has("json")) Json(new { Result = "Started", Task = TaskNames.Folder + "\\" + TaskNames.RunOnce });
            else output.WriteLine("A Windows Update run started as SYSTEM. Follow it with \"status\" or in the log.");
            return CliExit.Ok;
        }

        // -------------------------------------------------------------- schedule

        private static readonly string[] ScheduleSetSwitches =
        {
            "enable", "disable", "at-startup", "no-at-startup", "run-if-missed", "no-run-if-missed", "wake", "no-wake",
            "reboot-prompt", "no-reboot-prompt", "firmware", "no-firmware", "auto-retry", "no-auto-retry", "json"
        };

        private static readonly string[] ScheduleSetValues =
        {
            "frequency", "time", "day", "month-day", "startup-delay", "retry-delay", "retry-attempts", "retry-backoff", "cooldown"
        };

        private int ScheduleShow(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json" }, null);
            a.NoPositional();
            var settings = env.Store.LoadSettings();
            var status = Schedule().GetStatus(settings);
            if (a.Has("json"))
            {
                Json(new { Settings = settings, Description = ScheduleService.Describe(settings), status.MatchesSettings, status.Differences, Task = status.Main, RetryTask = status.Retry });
                return CliExit.Ok;
            }

            output.WriteLine("Schedule: " + (settings.ScheduleEnabled ? "on, " + ScheduleService.Describe(settings) : "off"));
            if (settings.ScheduleEnabled)
            {
                output.WriteLine("Task: " + (status.Main == null ? "missing" : status.Main.State + ", next run " + Format(status.Main.NextRunTime) + ", last run " + Format(status.Main.LastRunTime)));
                if (status.MatchesSettings == false)
                {
                    output.WriteLine("Warning: the scheduled task does not match the settings. Run \"schedule set\" to repair it.");
                    foreach (var difference in status.Differences) output.WriteLine("  " + difference);
                }
            }

            output.WriteLine("Run if missed: " + YesNo(settings.RunIfMissed) + "   Wake computer: " + YesNo(settings.WakeToRun) + "   Firmware/BIOS: " + YesNo(settings.InstallFirmwareUpdates));
            output.WriteLine("Automatic retry: " + (settings.AutoRetryEnabled
                ? settings.RetryMaxAttempts + " attempts, first after " + settings.RetryInitialDelayMinutes + " min, x" + settings.RetryBackoffMultiplier
                : "off") + "   Cooldown: " + settings.MinimumCooldownMinutes + " min");
            if (status.Retry != null) output.WriteLine("Retry pending: " + Format(status.Retry.NextRunTime));
            return CliExit.Ok;
        }

        private int ScheduleSet(string[] rest)
        {
            var a = Arguments.Parse(rest, ScheduleSetSwitches, ScheduleSetValues);
            a.NoPositional();
            if (a.Has("enable") && a.Has("disable")) throw new UsageException("Use either --enable or --disable, not both.");
            if (!env.IsAdministrator) return AdminRequired(a);

            var settings = env.Store.LoadSettings().Clone();
            if (a.Has("enable")) settings.ScheduleEnabled = true;
            if (a.Has("disable")) settings.ScheduleEnabled = false;
            if (a.Value("frequency") != null) settings.Frequency = Capitalize(a.Value("frequency"));
            if (a.Value("time") != null) settings.Time = a.Value("time");
            if (a.Value("day") != null) settings.DayOfWeek = Capitalize(a.Value("day"));
            settings.MonthlyDay = a.Int("month-day") ?? settings.MonthlyDay;
            settings.RunAtStartup = a.Toggle("at-startup") ?? settings.RunAtStartup;
            settings.StartupDelayMinutes = a.Int("startup-delay") ?? settings.StartupDelayMinutes;
            settings.RunIfMissed = a.Toggle("run-if-missed") ?? settings.RunIfMissed;
            settings.WakeToRun = a.Toggle("wake") ?? settings.WakeToRun;
            settings.ShowRebootPrompt = a.Toggle("reboot-prompt") ?? settings.ShowRebootPrompt;
            settings.InstallFirmwareUpdates = a.Toggle("firmware") ?? settings.InstallFirmwareUpdates;
            settings.AutoRetryEnabled = a.Toggle("auto-retry") ?? settings.AutoRetryEnabled;
            settings.RetryInitialDelayMinutes = a.Int("retry-delay") ?? settings.RetryInitialDelayMinutes;
            settings.RetryMaxAttempts = a.Int("retry-attempts") ?? settings.RetryMaxAttempts;
            settings.RetryBackoffMultiplier = a.Int("retry-backoff") ?? settings.RetryBackoffMultiplier;
            settings.MinimumCooldownMinutes = a.Int("cooldown") ?? settings.MinimumCooldownMinutes;
            return SaveAndApply(a, settings);
        }

        private int ScheduleRemove(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json" }, null);
            a.NoPositional();
            if (!env.IsAdministrator) return AdminRequired(a);
            var settings = env.Store.LoadSettings().Clone();
            settings.ScheduleEnabled = false;
            return SaveAndApply(a, settings);
        }

        private int ScheduleImport(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json" }, null);
            if (a.Positional.Count != 1) throw new UsageException("Usage: schedule import <settings.json>");
            a.NoPositional(1);
            if (!env.IsAdministrator) return AdminRequired(a);

            string text;
            try { text = File.ReadAllText(a.Positional[0]); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Fail(a, "Cannot read " + a.Positional[0] + ": " + ex.Message); }

            WindowsUpdateSettings settings;
            if (text.IndexOf("\"Frequency\"", StringComparison.Ordinal) >= 0)
            {
                settings = LegacyWinUpdateConfig.Import(text);
            }
            else
            {
                try { settings = JsonFileStore<WindowsUpdateSettings>.Deserialize(text); }
                catch (Exception ex) when (ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException) { return Fail(a, "Not a valid settings file: " + ex.Message); }
            }

            return SaveAndApply(a, settings);
        }

        /// <summary>D-015: take over the WinUpdate Tool V1/V2 schedule.</summary>
        /// <summary>
        /// Takes over from WinUpdate Tool V1/V2 (D-015, D-025): imports its settings, removes its
        /// scheduled tasks, applies WinTool's schedule, then uninstalls it. A step that fails is
        /// reported as a failure, never as a completed migration.
        /// </summary>
        private int ScheduleImportV2(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json", "wait-for-installer", "if-present", "keep-schedule" }, null);
            a.NoPositional();
            if (!env.IsAdministrator) return AdminRequired(a);
            // WinTool's MSI starts this after its own installation: one MSI cannot remove another while it runs.
            if (a.Has("wait-for-installer")) WaitForWindowsInstaller(TimeSpan.FromMinutes(10));

            var legacy = env.ReadLegacyConfig();
            var tasks = env.Tasks.Find(TaskNames.IsLegacyWinUpdate);
            var installs = env.LegacyInstallations.Find();
            // The MSI starts this command without a console, so the log is the only durable record of the result.
            void Note(EventLevel level, string text) => env.Log.Write(EventIds.LegacyTakeover, level, "WinUpdate Tool takeover: " + text);
            if (legacy == null && tasks.Count == 0 && installs.Count == 0)
            {
                Note(EventLevel.Information, "nothing to migrate (no settings file, scheduled task or installation found).");
                if (a.Has("if-present"))
                {
                    if (a.Has("json")) Json(new { Result = "NothingToMigrate", ExitCode = CliExit.Ok });
                    return CliExit.Ok;
                }

                return Fail(a, "No WinUpdate Tool was found on this computer.");
            }

            // --keep-schedule: the WinTool installer was given its own schedule, which wins over the old tool's.
            var settings = legacy == null || a.Has("keep-schedule") ? null : LegacyWinUpdateConfig.Import(legacy);
            if (settings != null && settings.ScheduleEnabled) Schedule().RequireInstalled();
            Note(EventLevel.Information, "found " + (legacy != null ? "its settings file" : "no settings file") + ", " +
                tasks.Count + " scheduled task(s) and " + installs.Count + " installation(s)" +
                (installs.Count > 0 ? " (" + string.Join("; ", installs.Select(i => i.ToString())) + ")" : "") + ".");
            Note(EventLevel.Information, settings != null
                ? "importing its schedule: " + (settings.ScheduleEnabled ? ScheduleService.Describe(settings) : "off") + "."
                : a.Has("keep-schedule") ? "keeping the schedule chosen in the WinTool installer; the old tool's schedule is not imported."
                : "no settings to import; WinTool's schedule is unchanged.");
            var removed = new List<string>();
            foreach (var task in tasks)
            {
                try
                {
                    env.Tasks.Delete(task.Folder, task.Name);
                    removed.Add(task.FullPath);
                    Note(EventLevel.Information, "removed scheduled task " + task.FullPath + ".");
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    Note(EventLevel.Error, "incomplete: task " + task.FullPath + " could not be removed: " + ex.Message);
                    return Fail(a, "The WinUpdate Tool task " + task.FullPath + " could not be removed: " + ex.Message);
                }
            }

            if (settings != null)
            {
                var exit = SaveAndApply(a, settings, quiet: true);
                if (exit != CliExit.Ok)
                {
                    Note(EventLevel.Error, "incomplete: the imported schedule could not be applied (exit code " + exit + ").");
                    return exit;
                }
            }

            var uninstalled = new List<string>();
            var failures = new List<string>();
            foreach (var install in installs)
            {
                try
                {
                    var code = env.LegacyInstallations.Uninstall(install);
                    if (LegacyInstallPolicy.Succeeded(code))
                    {
                        uninstalled.Add(install.ToString());
                        Note(EventLevel.Information, "uninstalled " + install + " (exit code " + code + ").");
                    }
                    else failures.Add(install + " could not be uninstalled (exit code " + code + ").");
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    failures.Add(install + " could not be uninstalled: " + ex.Message);
                }
            }

            foreach (var line in failures) Note(EventLevel.Warning, line);
            Note(failures.Count == 0 ? EventLevel.Information : EventLevel.Error, failures.Count == 0
                ? "completed: " + removed.Count + " task(s) removed, " + uninstalled.Count + " installation(s) uninstalled."
                : "incomplete: " + failures.Count + " installation(s) could not be uninstalled.");
            var result = failures.Count == 0 ? CliExit.Ok : CliExit.Failed;
            if (failures.Count == 0) RememberTakeoverNotice(settings != null ? TakeoverNotices.Imported : a.Has("keep-schedule") ? TakeoverNotices.Kept : TakeoverNotices.Removed);
            if (a.Has("json"))
            {
                Json(new
                {
                    Result = failures.Count == 0 ? "Migrated" : "Incomplete",
                    ExitCode = result,
                    SettingsImported = settings != null,
                    Settings = settings,
                    RemovedTasks = removed,
                    Uninstalled = uninstalled,
                    Failures = failures
                });
            }
            else
            {
                foreach (var path in removed) output.WriteLine("Removed WinUpdate Tool task: " + path);
                if (settings != null) output.WriteLine("WinUpdate Tool settings imported.");
                foreach (var item in uninstalled) output.WriteLine("Uninstalled " + item + ".");
                foreach (var line in failures) error.WriteLine(line);
                output.WriteLine(failures.Count == 0
                    ? "WinTool took over from WinUpdate Tool."
                    : "The takeover is incomplete: uninstall WinUpdate Tool from Settings > Apps, then run this command again.");
            }

            return result;
        }

        /// <summary>The window tells the user once, at its next start, what the takeover did (owner request).</summary>
        private void RememberTakeoverNotice(string kind)
        {
            try
            {
                var state = env.Store.LoadState();
                state.PendingTakeoverNotice = kind;
                env.Store.SaveState(state);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                env.Log.Write(EventIds.LegacyTakeover, EventLevel.Warning, "WinUpdate Tool takeover: the notice for the window could not be saved: " + ex.Message);
            }
        }

        /// <summary>Waits while another Windows Installer transaction runs (its global mutex exists).</summary>
        private static void WaitForWindowsInstaller(TimeSpan limit)
        {
            var until = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < until)
            {
                try
                {
                    using (System.Threading.Mutex.OpenExisting(@"Global\_MSIExecute")) { }
                }
                catch (System.Threading.WaitHandleCannotBeOpenedException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    // It exists but cannot be opened: still busy.
                }

                System.Threading.Thread.Sleep(2000);
            }
        }

        private int SaveAndApply(Arguments a, WindowsUpdateSettings settings, bool quiet = false)
        {
            var errors = settings.Validate();
            if (errors.Count > 0)
            {
                if (a.Has("json")) Json(new { Result = "Invalid", ExitCode = CliExit.Usage, Errors = errors });
                else foreach (var e in errors) error.WriteLine(e);
                return CliExit.Usage;
            }

            try
            {
                if (settings.ScheduleEnabled) Schedule().RequireInstalled();
                Schedule().Apply(settings);
            }
            catch (InstalledCopyRequiredException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException)
            {
                return Fail(a, "The schedule was not saved: " + ex.Message);
            }

            env.Store.SaveSettings(settings);
            if (quiet) return CliExit.Ok;
            if (a.Has("json")) Json(new { Result = "Saved", Settings = settings, Description = settings.ScheduleEnabled ? ScheduleService.Describe(settings) : "off" });
            else output.WriteLine("Schedule " + (settings.ScheduleEnabled ? "saved: " + ScheduleService.Describe(settings) + "." : "turned off."));
            return CliExit.Ok;
        }

        // ---------------------------------------------------------- status, logs

        private object StatusObject()
        {
            var settings = env.Store.LoadSettings();
            ScheduleStatus schedule = null;
            string scheduleError = null;
            try { schedule = Schedule().GetStatus(settings); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { scheduleError = ex.Message; }

            return new
            {
                Product = ProductInfo.Name,
                Version = ProductInfo.InformationalVersion,
                Timestamp = env.Clock(),
                ComputerName = Environment.MachineName,
                UserName = Environment.UserDomainName + "\\" + Environment.UserName,
                IsAdministrator = env.IsAdministrator,
                Windows = env.WindowsVersion,
                WindowsUpdate = new
                {
                    Settings = settings,
                    State = env.Store.LoadState(),
                    Schedule = schedule,
                    ScheduleError = scheduleError,
                    PendingRestart = Safe(() => env.Probe.GetPendingRestart()),
                    Activity = Safe(() => env.Probe.GetActivity())
                }
            };
        }

        private int Status(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json" }, null);
            a.NoPositional();
            if (a.Has("json"))
            {
                Json(StatusObject());
                return CliExit.Ok;
            }

            var state = env.Store.LoadState();
            var settings = env.Store.LoadSettings();
            output.WriteLine(ProductInfo.Name + " " + ProductInfo.InformationalVersion);
            output.WriteLine("Computer: " + Environment.MachineName + "   Windows: " + env.WindowsVersion + "   Administrator: " + YesNo(env.IsAdministrator));
            output.WriteLine("Last update run: " + (state.LastRunFinished ?? "never") + (state.LastResult != null ? " (" + state.LastRunType + ", " + state.LastResult + ")" : string.Empty));
            if (!string.IsNullOrEmpty(state.LastMessage)) output.WriteLine("  " + state.LastMessage);
            output.WriteLine("Last successful run: " + (state.LastSuccessfulRun ?? "never"));
            output.WriteLine("Schedule: " + (settings.ScheduleEnabled ? ScheduleService.Describe(settings) : "off"));
            if (state.LastRetryScheduled != null) output.WriteLine("Retry " + state.RetryCount + " scheduled for " + state.LastRetryScheduled + ": " + state.LastRetryReason);
            var pending = Safe(() => env.Probe.GetPendingRestart());
            if (pending != null) output.WriteLine("Restart pending: " + (pending.Pending ? "yes - " + string.Join("; ", pending.Reasons) : "no"));
            return CliExit.Ok;
        }

        private int LogsCollect(string[] rest)
        {
            var a = Arguments.Parse(rest, new[] { "json" }, new[] { "output" });
            a.NoPositional();
            var stamp = env.Clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = a.Value("output");
            string zipPath;
            if (string.IsNullOrEmpty(target)) zipPath = Path.Combine(env.DataFolder, "LogPackages", "WinTool-Logs-" + stamp + ".zip");
            else if (string.Equals(Path.GetExtension(target), ".zip", StringComparison.OrdinalIgnoreCase)) zipPath = Path.GetFullPath(target);
            else zipPath = Path.Combine(Path.GetFullPath(target), "WinTool-Logs-" + stamp + ".zip");

            var staging = Path.Combine(Path.GetTempPath(), "WinTool-Logs-" + stamp + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var source in env.LogSources())
                {
                    if (Directory.Exists(source)) CopyFolder(source, Path.Combine(staging, new DirectoryInfo(source).Name));
                    else if (File.Exists(source)) File.Copy(source, Path.Combine(staging, Path.GetFileName(source)), true);
                }

                var tasks = Path.Combine(staging, "Tasks");
                Directory.CreateDirectory(tasks);
                foreach (var name in new[] { TaskNames.Main, TaskNames.Retry, TaskNames.RunOnce })
                {
                    var task = Safe(() => env.Tasks.Get(name));
                    if (task?.Xml != null) File.WriteAllText(Path.Combine(tasks, name + ".xml"), task.Xml);
                }

                File.WriteAllText(Path.Combine(staging, "status.json"), JsonText.Serialize(StatusObject()));
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(staging, zipPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Fail(a, "The log package could not be created: " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            if (a.Has("json")) Json(new { Result = "Collected", ZipPath = zipPath });
            else output.WriteLine("Log package: " + zipPath);
            return CliExit.Ok;
        }

        // --------------------------------------------------------------- helpers

        private ScheduleService Schedule() => new ScheduleService(env.Tasks, env.Log, env.ExecutablePath, env.Clock, installedFolder);

        private IRetryScheduler Retries() => Schedule();

        private static UpdateScope ScopeFrom(Arguments a, WindowsUpdateSettings settings)
        {
            return ScopeSwitches.Any(a.Has)
                ? new UpdateScope(a.Has("windows"), a.Has("optional"), a.Has("drivers"), a.Has("firmware"))
                : UpdateScope.Default(settings.InstallFirmwareUpdates);
        }

        private int AdminRequired(Arguments a)
        {
            const string message = "This command needs administrator rights. Run it from an elevated command prompt.";
            if (a.Has("json")) Json(new { Result = "AdministratorRequired", ExitCode = CliExit.AdministratorRequired, Message = message });
            else error.WriteLine(message);
            return CliExit.AdministratorRequired;
        }

        private int Busy(Arguments a)
        {
            const string message = "Another WinTool operation is running. Try again when it has finished.";
            if (a.Has("json")) Json(new { Result = "AlreadyRunning", ExitCode = CliExit.Busy, Message = message });
            else error.WriteLine(message);
            return CliExit.Busy;
        }

        private int Fail(Arguments a, string message)
        {
            if (a.Has("json")) Json(new { Result = "Failed", ExitCode = CliExit.Failed, Message = message });
            else error.WriteLine(message);
            return CliExit.Failed;
        }

        private void Json(object value) => output.WriteLine(JsonText.Serialize(value));

        private static T Safe<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
        private static string Format(DateTime? time) => time.HasValue ? time.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "none";

        private static string Capitalize(string value) =>
            string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();

        private static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var folder in Directory.GetDirectories(source)) CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
        }

        private sealed class ConsoleProgress : IProgress<RunProgress>
        {
            private readonly TextWriter output;

            public ConsoleProgress(TextWriter output)
            {
                this.output = output;
            }

            public void Report(RunProgress value)
            {
                switch (value.Stage)
                {
                    case RunStage.Searching: output.WriteLine("Searching for updates (pass " + value.Pass + ")..."); break;
                    case RunStage.Downloading: output.WriteLine("Downloading " + value.Current + "/" + value.Total + ": " + value.Title); break;
                    case RunStage.Installing: output.WriteLine("Installing " + value.Total + " update(s)..."); break;
                    case RunStage.Verifying: output.WriteLine("Checking what remains..."); break;
                }
            }
        }
    }
}
