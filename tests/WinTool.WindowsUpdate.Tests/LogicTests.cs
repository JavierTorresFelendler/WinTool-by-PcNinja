using System;
using System.Collections.Generic;
using System.Linq;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class ClassifierTests
    {
        [Fact]
        public void Kinds_follow_type_then_browse_only()
        {
            Assert.Equal(UpdateKind.Driver, UpdateClassifier.KindOf(U.Driver()));
            Assert.Equal(UpdateKind.Optional, UpdateClassifier.KindOf(U.Optional()));
            Assert.Equal(UpdateKind.Windows, UpdateClassifier.KindOf(U.Windows()));
        }

        [Theory]
        [InlineData("Dell Inc. - Firmware - 1.25.0", null, true)]
        [InlineData("Lenovo BIOS Update", null, true)]
        [InlineData("Surface - UEFI - 3.1", null, true)]
        [InlineData("Intel - System - 10.1", "Firmware", true)]
        [InlineData("Intel - Display - 31.0", "Display", false)]
        [InlineData("Biosphere screensaver", null, false)]
        public void Firmware_is_found_by_driver_class_or_words(string title, string driverClass, bool expected)
        {
            Assert.Equal(expected, UpdateClassifier.IsFirmware(U.Driver(title, driverClass)));
        }

        [Fact]
        public void Default_scope_includes_everything_except_firmware()
        {
            var scope = UpdateScope.Default();
            Assert.True(UpdateClassifier.IsIncluded(U.Windows(), scope));
            Assert.True(UpdateClassifier.IsIncluded(U.Optional(), scope));
            Assert.True(UpdateClassifier.IsIncluded(U.Driver(), scope));
            Assert.False(UpdateClassifier.IsIncluded(U.Driver("BIOS", null), scope));
            Assert.True(UpdateClassifier.IsIncluded(U.Driver("BIOS", null), UpdateScope.Default(includeFirmware: true)));
        }

        [Fact]
        public void Explicit_scope_excludes_kinds()
        {
            var windowsOnly = new UpdateScope(true, false, false, false);
            Assert.False(UpdateClassifier.IsIncluded(U.Optional(), windowsOnly));
            Assert.False(UpdateClassifier.IsIncluded(U.Driver(), windowsOnly));
            Assert.Equal("Windows", windowsOnly.ToString());
        }
    }

    public class MergerAndScanTests
    {
        [Fact]
        public void Merge_keeps_one_entry_per_update_with_all_sources()
        {
            var driver = U.Driver();
            var copy = new UpdateItem(driver.UpdateId, driver.Revision, driver.Title) { IsDriverType = true };
            var merged = UpdateMerger.Merge(new[]
            {
                new KeyValuePair<string, IReadOnlyList<UpdateItem>>("Broad", new[] { driver }),
                new KeyValuePair<string, IReadOnlyList<UpdateItem>>("Driver", new[] { copy })
            });
            var only = Assert.Single(merged);
            Assert.Equal(new[] { "Broad", "Driver" }, only.Sources);
        }

        [Fact]
        public void Scan_survives_one_failing_search()
        {
            var agent = new FakeAgent();
            agent.Available.Add(U.Driver("Only found by the driver search"));
            agent.FailingCriteria.Add(ScanService.BroadCriteria);
            var result = new ScanService(agent, new ListLog()).Scan(UpdateScope.Default());
            Assert.Equal(1, result.Drivers);
        }

        [Fact]
        public void Scan_fails_with_the_windows_error_when_every_search_fails()
        {
            var agent = new FakeAgent { SearchErrorHResult = unchecked((int)0x8024402C) };
            var error = Assert.Throws<UpdateAgentException>(() => new ScanService(agent, new ListLog()).Scan(UpdateScope.Default()));
            Assert.Equal(unchecked((int)0x8024402C), error.HResult);
        }

        [Fact]
        public void Scan_counts_match_the_preview_rules()
        {
            var agent = new FakeAgent();
            agent.Available.AddRange(new[] { U.Windows(), U.Windows(), U.Optional(), U.Driver(), U.Driver("BIOS update", null) });
            var result = new ScanService(agent, new ListLog()).Scan(UpdateScope.Default());
            Assert.Equal(2, result.Important);
            Assert.Equal(1, result.Optional);
            Assert.Equal(1, result.Drivers);
            Assert.Equal(1, result.FirmwareSkipped);
            Assert.Equal(4, result.Total);
        }
    }

    public class PendingRestartTests
    {
        [Fact]
        public void Nothing_pending_on_a_clean_machine()
        {
            Assert.False(PendingRestartEvaluator.Evaluate(new PendingRestartFacts()).Pending);
        }

        [Fact]
        public void Servicing_and_update_keys_mean_pending()
        {
            var state = PendingRestartEvaluator.Evaluate(new PendingRestartFacts { ComponentServicingRebootPending = true, WindowsUpdateRebootRequired = true });
            Assert.Equal(2, state.Reasons.Count);
        }

        [Fact]
        public void Harmless_file_renames_only_warn()
        {
            var state = PendingRestartEvaluator.Evaluate(new PendingRestartFacts
            {
                PendingFileRenameOperations = new[] { @"\??\C:\Program Files\Vendor\old.dll", "", @"\??\C:\Users\me\AppData\Local\Temp\x.tmp", "" }
            });
            Assert.False(state.Pending);
            Assert.Single(state.Warnings);
            Assert.Equal(2, state.FileRenames.Count);
            Assert.True(state.FileRenames[0].IsDelete);
        }

        [Theory]
        [InlineData(@"\??\C:\Windows\WinSxS\Temp\PendingRenames\file.dll")]
        [InlineData(@"\SystemRoot\System32\drivers\new.sys")]
        [InlineData(@"\??\C:\Windows\System32\DriverStore\FileRepository\x.inf")]
        [InlineData(@"*1\??\C:\Windows\servicing\pending.xml")]
        public void Servicing_or_driver_renames_mean_pending(string path)
        {
            var state = PendingRestartEvaluator.Evaluate(new PendingRestartFacts { PendingFileRenameOperations = new[] { path, "" } });
            Assert.True(state.Pending);
            Assert.Contains(PendingRestartEvaluator.RenameReason, state.Reasons);
        }

        [Fact]
        public void Paths_are_normalized()
        {
            Assert.Equal(@"C:\Windows\System32\x.sys", PendingRestartEvaluator.NormalizePath(@"\SystemRoot\System32\x.sys", @"C:\Windows"));
            Assert.Equal(@"C:\a.txt", PendingRestartEvaluator.NormalizePath(@"!\??\C:\a.txt", @"C:\Windows"));
        }

        [Fact]
        public void An_odd_number_of_entries_does_not_fail()
        {
            var renames = PendingRestartEvaluator.ParseRenames(new[] { @"\??\C:\only-source.txt" }, @"C:\Windows");
            Assert.True(Assert.Single(renames).IsDelete);
        }

        [Fact]
        public void A_computer_rename_means_pending()
        {
            var state = PendingRestartEvaluator.Evaluate(new PendingRestartFacts { ComputerName = "NEW-PC", ActiveComputerName = "OLD-PC" });
            Assert.Contains(PendingRestartEvaluator.ComputerRenameReason, state.Reasons);
        }
    }

    public class ActivityTests
    {
        [Fact]
        public void Install_processes_or_trusted_installer_mean_installing()
        {
            Assert.Equal(ActivityStatus.Installing, ActivityEvaluator.Evaluate(new[] { "explorer", "tiworker" }, false).Status);
            Assert.Equal(ActivityStatus.Installing, ActivityEvaluator.Evaluate(new string[0], true).Status);
        }

        [Fact]
        public void Update_orchestrator_means_background_activity()
        {
            var activity = ActivityEvaluator.Evaluate(new[] { "MoUsoCoreWorker", "svchost" }, false);
            Assert.Equal(ActivityStatus.BackgroundActivity, activity.Status);
            Assert.Equal(new[] { "MoUsoCoreWorker" }, activity.BackgroundProcesses);
        }

        [Fact]
        public void Otherwise_idle()
        {
            Assert.Equal(ActivityStatus.Idle, ActivityEvaluator.Evaluate(new[] { "explorer", "svchost" }, false).Status);
        }
    }

    public class RetryAndCodesTests
    {
        [Theory]
        [InlineData(1, 5)]
        [InlineData(2, 10)]
        [InlineData(3, 20)]
        [InlineData(10, 360)]
        public void Delay_doubles_and_is_capped(int retry, int expected)
        {
            Assert.Equal(expected, RetryPolicy.DelayMinutes(new WindowsUpdateSettings(), retry));
        }

        [Fact]
        public void Delay_is_at_least_one_minute()
        {
            Assert.Equal(1, RetryPolicy.DelayMinutes(new WindowsUpdateSettings { RetryInitialDelayMinutes = 0, RetryBackoffMultiplier = 0 }, 1));
        }

        [Theory]
        [InlineData(0x8024402C, true)]
        [InlineData(0x80072EFD, true)]
        [InlineData(0x8024000E, false)]
        [InlineData(0x80070643, false)]
        public void Only_network_errors_are_retryable(uint hresult, bool retryable)
        {
            Assert.Equal(retryable, HResults.IsRetryable(unchecked((int)hresult)));
        }

        [Fact]
        public void Errors_are_described_with_hex_code_and_explanation()
        {
            var text = HResults.Describe("Search failed", unchecked((int)0x8024402C));
            Assert.Contains("0x8024402C", text);
            Assert.Contains("proxy", text);
            Assert.Equal("x (0x80070643)", HResults.Describe("x", unchecked((int)0x80070643)));
        }

        [Fact]
        public void Every_result_has_a_deliberate_exit_code()
        {
            var expected = new Dictionary<RunResult, int>
            {
                { RunResult.Succeeded, 0 }, { RunResult.NoUpdates, 0 }, { RunResult.MaxPassesReached, 0 },
                { RunResult.RebootRequired, 10 }, { RunResult.SucceededWithErrors, 11 },
                { RunResult.RetryScheduled, 20 },
                { RunResult.SkippedInstalling, 21 }, { RunResult.SkippedBackgroundActivity, 21 }, { RunResult.CooldownActive, 21 }, { RunResult.AlreadyRunning, 21 },
                { RunResult.MaxRetriesReached, 22 }, { RunResult.RetryDisabled, 22 },
                { RunResult.Cancelled, 3 },
                { RunResult.DownloadFailed, 1 }, { RunResult.NoDownloadedUpdates, 1 }, { RunResult.Failed, 1 }
            };

            foreach (RunResult result in Enum.GetValues(typeof(RunResult)))
            {
                Assert.True(expected.ContainsKey(result), "No expected exit code for " + result);
                Assert.Equal(expected[result], ExitCodes.For(result));
            }
        }
    }

    public class HealthCheckTests
    {
        [Fact]
        public void A_healthy_machine_is_ok()
        {
            var items = HealthCheck.Evaluate(new HealthFacts
            {
                ServicesCanRun = new Dictionary<string, bool> { { "wuauserv", true } },
                SystemDriveFreeBytes = 100L * 1024 * 1024 * 1024,
                NetworkReady = true,
                MicrosoftUpdateRegistered = true,
                ScheduledTaskMatchesSettings = true
            });
            Assert.Equal(HealthStatus.Ok, HealthCheck.Overall(items));
        }

        [Fact]
        public void Disabled_service_low_disk_or_broken_schedule_are_problems()
        {
            Assert.Equal(HealthStatus.Problem, HealthCheck.Overall(HealthCheck.Evaluate(new HealthFacts { ServicesCanRun = new Dictionary<string, bool> { { "wuauserv", false } } })));
            Assert.Equal(HealthStatus.Problem, HealthCheck.Overall(HealthCheck.Evaluate(new HealthFacts { SystemDriveFreeBytes = 5L * 1024 * 1024 * 1024 })));
            Assert.Equal(HealthStatus.Problem, HealthCheck.Overall(HealthCheck.Evaluate(new HealthFacts { ScheduledTaskMatchesSettings = false })));
        }

        [Fact]
        public void Pending_restart_and_little_free_space_are_warnings()
        {
            Assert.Equal(HealthStatus.Warning, HealthCheck.Overall(HealthCheck.Evaluate(new HealthFacts { PendingRestart = FakeProbe.PendingNow() })));
            Assert.Equal(HealthStatus.Warning, HealthCheck.Overall(HealthCheck.Evaluate(new HealthFacts { SystemDriveFreeBytes = 12L * 1024 * 1024 * 1024 })));
        }

        [Fact]
        public void No_schedule_means_no_schedule_item()
        {
            var items = HealthCheck.Evaluate(new HealthFacts());
            Assert.DoesNotContain(items, i => i.Id == "schedule");
        }
    }
}

namespace WinTool.WindowsUpdate.Tests
{
    public class ActivityMessageTests
    {
        [Fact]
        public void The_message_names_what_windows_is_doing()
        {
            Assert.Contains("(TiWorker)", ActivityEvaluator.Evaluate(new[] { "TiWorker" }, false).Message);
            Assert.Contains("(TrustedInstaller service)", ActivityEvaluator.Evaluate(new string[0], true).Message);
            Assert.Contains("(MoUsoCoreWorker)", ActivityEvaluator.Evaluate(new[] { "MoUsoCoreWorker" }, false).Message);
        }
    }
}
