using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinTool.Cli;
using WinTool.Core.Json;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    /// <summary>
    /// "upgrade" argument rules and the automation result contract. The engine
    /// factory throws, so every case here proves nothing was read, mounted or
    /// started before the arguments were accepted.
    /// </summary>
    public class UpgradeCliTests
    {
        private static (int Exit, string Out, string Err) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var commands = new UpgradeCommands(
                () => throw new InvalidOperationException("The engine must not be used."), () => true, output, error);
            var exit = commands.Run(args[0], args.Skip(1).ToArray());
            return (exit, output.ToString(), error.ToString());
        }

        [Theory]
        [InlineData("run", "--iso", @"D:\Win11.iso")]
        [InlineData("run", "--mounted", @"E:\", "--quiet", "--no-restart")]
        public void Run_without_yes_or_dry_run_is_refused_before_anything_happens(params string[] args)
        {
            var result = Run(args);
            Assert.Equal(CliExit.Usage, result.Exit);
            Assert.Contains("--yes", result.Err);
            Assert.Contains("Nothing was started", result.Err);
        }

        [Theory]
        [InlineData("validate")]
        [InlineData("validate", "--iso", "a.iso", "--mounted", @"E:\")]
        [InlineData("run", "--iso", "a.iso", "--download", "--yes")]
        [InlineData("validate", "--download")]
        [InlineData("validate", "--mounted", @"E:\", "--sha256", "0000000000000000000000000000000000000000000000000000000000000000")]
        [InlineData("run", "--iso", "a.iso", "--destination", @"F:\x", "--yes")]
        [InlineData("validate", "--iso", "a.iso", "--image-index", "0")]
        [InlineData("validate", "--iso", "a.iso", "--image-index", "two")]
        [InlineData("validate", "--iso", "a.iso", "--sha256", "not-a-hash")]
        [InlineData("validate", "--iso", "a.iso", "--iso", "b.iso")]
        [InlineData("validate", "--iso", "a.iso", "--yes")]
        [InlineData("status", "--iso", "a.iso")]
        [InlineData("scan", "extra")]
        [InlineData("upgrade-everything")]
        public void Invalid_arguments_are_usage_errors(params string[] args)
        {
            Assert.Equal(CliExit.Usage, Run(args).Exit);
        }

        [Fact]
        public void An_unknown_option_lists_the_valid_ones()
        {
            var result = Run("run", "--Drive", "E:\\", "--dry-run");
            Assert.Equal(CliExit.Usage, result.Exit);
            Assert.Contains("Unknown option: --Drive", result.Err);
            Assert.Contains("--mounted <value>", result.Err);
            Assert.Contains("--dry-run", result.Err);
        }

        [Fact]
        public void A_usage_error_with_json_uses_the_original_result_contract()
        {
            var result = Run("run", "--iso", "a.iso", "--json");
            Assert.Equal(CliExit.Usage, result.Exit);
            var json = (Dictionary<string, object>)JsonData.Parse(result.Out);
            Assert.Equal("INVALID_ARGUMENTS", json["resultCode"]);
            Assert.Equal(2L, Convert.ToInt64(json["exitCode"]));
            Assert.Equal(false, json["readyForRestart"]);
            Assert.Equal(false, json["success"]);
            Assert.Equal(1L, Convert.ToInt64(json["schemaVersion"]));
        }

        [Theory]
        [InlineData(UpgradeCommands.UpgradePreparedRestartRequired, 0, true, true)]
        [InlineData(UpgradeCommands.UpgradePreparedRestartRequired, 0, false, false)]
        [InlineData("UPGRADE_PREPARED_AUTOMATIC_RESTART", 0, true, false)]
        [InlineData("DRY_RUN_COMPLETED", 0, true, false)]
        [InlineData("VALIDATION_PASSED", 0, true, false)]
        [InlineData(UpgradeCommands.UpgradePreparedRestartRequired, CliExit.SetupFailed, true, false)]
        public void Only_a_prepared_upgrade_that_needs_a_manual_restart_is_ready_for_restart(string code, int exit, bool asked, bool expected)
        {
            var payload = UpgradeCommands.Result(new { started = true }, code, exit, "m", asked);
            Assert.Equal(expected, payload["readyForRestart"]);
            Assert.Equal(exit == 0, payload["success"]);
        }

        private enum Kind { Zero, One, Two }

        private sealed class Shape
        {
            public string PageUrl { get; set; } = "https://x";
            public Kind Kind { get; set; } = Kind.Two;
            public int[] Numbers { get; set; } = { 1, 2 };
        }

        [Fact]
        public void Nested_objects_keep_the_original_property_names_and_enum_numbers()
        {
            var json = JsonText.Serialize(UpgradeCommands.Result(new { recommendation = new Shape() }, "X", 0, "m", false));
            Assert.Contains("\"recommendation\"", json);
            Assert.Contains("\"PageUrl\": \"https://x\"", json);
            Assert.Contains("\"Kind\": 2", json);
        }
    }
}
