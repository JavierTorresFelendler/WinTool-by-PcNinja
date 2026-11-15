using System;
using WinTool.Core.Updates;
using Xunit;

namespace WinTool.Core.Tests
{
    public class UniversalLaunchPolicyTests
    {
        private static UniversalLaunchFacts Facts(string installed = null, bool trusted = true, bool portable = false) =>
            new UniversalLaunchFacts
            {
                HasInstaller = true, CurrentVersion = AppVersion.Parse("1.0.4-rc.1").FileVersion,
                InstalledVersion = installed == null ? null : AppVersion.Parse(installed).FileVersion,
                InstalledProtected = trusted, PortableIntent = portable
            };

        [Theory]
        [InlineData(null, true, false, UniversalLaunch.AskInstall)]
        [InlineData(null, false, true, UniversalLaunch.RunCurrent)]
        [InlineData("1.0.3", true, false, UniversalLaunch.AskUpdate)]
        [InlineData("1.0.4-rc.1", true, false, UniversalLaunch.OpenInstalled)]
        [InlineData("1.0.4", true, false, UniversalLaunch.OpenInstalled)]
        [InlineData("1.0.3", false, false, UniversalLaunch.AskUnprotected)]
        [InlineData("1.0.4-rc.1", false, false, UniversalLaunch.AskUnprotected)]
        [InlineData("1.0.4", false, false, UniversalLaunch.BlockOlder)]
        [InlineData("1.0.4", true, true, UniversalLaunch.BlockOlder)]
        [InlineData("1.0.4", false, true, UniversalLaunch.BlockOlder)]
        [InlineData("1.0.3", true, true, UniversalLaunch.RunCurrent)]
        [InlineData("1.0.4-rc.1", false, true, UniversalLaunch.RunCurrent)]
        public void Launch_table(string installed, bool trusted, bool portable, UniversalLaunch expected) =>
            Assert.Equal(expected, UniversalLaunchPolicy.Decide(Facts(installed, trusted, portable)));

        [Theory]
        [InlineData("1.0.4", true)]
        [InlineData("1.0.4", false)]
        [InlineData(null, false)]
        public void Plain_builds_ignore_universal_policy(string installed, bool trusted)
        {
            var facts = Facts(installed, trusted, true);
            facts.HasInstaller = false;
            Assert.Equal(UniversalLaunch.RunCurrent, UniversalLaunchPolicy.Decide(facts));
        }

        [Fact]
        public void Installed_self_runs_without_asking_even_in_a_custom_folder()
        {
            var facts = Facts("1.0.4-rc.1", false);
            facts.IsInstalledSelf = true;
            Assert.Equal(UniversalLaunch.RunCurrent, UniversalLaunchPolicy.Decide(facts));
        }

        [Fact]
        public void Managed_worker_is_not_a_version_fence_bypass()
        {
            var facts = Facts("1.0.4", false);
            facts.IsProtectedWorker = true;
            Assert.Equal(UniversalLaunch.BlockOlder, UniversalLaunchPolicy.Decide(facts));
            facts.InstalledVersion = null;
            Assert.Equal(UniversalLaunch.RunCurrent, UniversalLaunchPolicy.Decide(facts));
        }

        [Theory]
        [InlineData("--portable")]
        [InlineData("--portable-chosen")]
        public void Both_markers_are_intent_only_and_require_fresh_elevated_facts(string flag)
        {
            Assert.Equal(StartupArguments.PortableGui, UniversalLaunchPolicy.Classify(new[] { flag }, true));
            var beforeUac = Facts("1.0.3", true, true);
            Assert.Equal(UniversalLaunch.RunCurrent, UniversalLaunchPolicy.Decide(beforeUac));
            var afterUac = Facts("1.0.4", true, true);
            Assert.Equal(UniversalLaunch.BlockOlder, UniversalLaunchPolicy.Decide(afterUac));
        }

        [Theory]
        [InlineData("--portable", "--json")]
        [InlineData("status", "--portable")]
        [InlineData("--portable-chosen", "status")]
        [InlineData("--portable", "--portable-chosen")]
        public void Mixed_gui_flags_are_usage_errors(string first, string second) =>
            Assert.Equal(StartupArguments.UsageError, UniversalLaunchPolicy.Classify(new[] { first, second }, true));

        [Theory]
        [InlineData("status", "--json")]
        [InlineData("schedule", "show")]
        [InlineData("version", "--json")]
        [InlineData("--unknown", "--json")]
        public void Existing_cli_and_unknown_arguments_stay_with_cli(string first, string second) =>
            Assert.Equal(StartupArguments.CommandLine, UniversalLaunchPolicy.Classify(new[] { first, second }, true));

        [Fact]
        public void Plain_build_argument_classification_is_unchanged()
        {
            Assert.Equal(StartupArguments.DefaultGui, UniversalLaunchPolicy.Classify(Array.Empty<string>(), false));
            Assert.Equal(StartupArguments.PortableGui, UniversalLaunchPolicy.Classify(new[] { "--portable-chosen" }, false));
            Assert.Equal(StartupArguments.CommandLine, UniversalLaunchPolicy.Classify(new[] { "--portable" }, false));
            Assert.Equal(StartupArguments.CommandLine, UniversalLaunchPolicy.Classify(new[] { "--portable-chosen", "--json" }, false));
        }

        [Theory]
        [InlineData(null, true, LaunchAnswer.Yes, LaunchAction.Install)]
        [InlineData(null, true, LaunchAnswer.No, LaunchAction.RunCurrent)]
        [InlineData(null, true, LaunchAnswer.Cancel, LaunchAction.Stop)]
        [InlineData("1.0.3", true, LaunchAnswer.Yes, LaunchAction.Install)]
        [InlineData("1.0.3", true, LaunchAnswer.No, LaunchAction.OpenInstalled)]
        [InlineData("1.0.3", true, LaunchAnswer.Cancel, LaunchAction.Stop)]
        [InlineData("1.0.3", false, LaunchAnswer.Yes, LaunchAction.Install)]
        [InlineData("1.0.3", false, LaunchAnswer.No, LaunchAction.RunCurrent)]
        [InlineData("1.0.4-rc.1", false, LaunchAnswer.Yes, LaunchAction.Stop)]
        [InlineData("1.0.4-rc.1", false, LaunchAnswer.No, LaunchAction.RunCurrent)]
        [InlineData("1.0.4", false, LaunchAnswer.Yes, LaunchAction.Stop)]
        [InlineData("1.0.4", false, LaunchAnswer.No, LaunchAction.Stop)]
        public void Answer_table_never_opens_unprotected_or_installs_without_valid_consent(
            string installed, bool trusted, LaunchAnswer answer, LaunchAction expected) =>
            Assert.Equal(expected, UniversalLaunchPolicy.Resolve(Facts(installed, trusted), answer));

        [Fact]
        public void Replaced_install_cannot_silently_downgrade()
        {
            var facts = Facts("1.0.3");
            Assert.True(UniversalLaunchPolicy.CanInstall(facts));
            facts.InstalledVersion = AppVersion.Parse("1.0.4").FileVersion;
            Assert.False(UniversalLaunchPolicy.CanInstall(facts));
        }

        [Theory]
        [InlineData(LaunchAnswer.Yes)]
        [InlineData(LaunchAnswer.No)]
        [InlineData(LaunchAnswer.Cancel)]
        public void Automatic_redirection_never_resolves_to_an_installer(LaunchAnswer answer)
        {
            Assert.Equal(LaunchAction.OpenInstalled, UniversalLaunchPolicy.Resolve(Facts("1.0.4-rc.1"), answer));
            Assert.Equal(LaunchAction.OpenInstalled, UniversalLaunchPolicy.Resolve(Facts("1.0.4"), answer));
        }

        [Fact]
        public void Unknown_current_version_does_not_run_against_known_newer_data()
        {
            var facts = Facts("1.0.3", true, true);
            facts.CurrentVersion = null;
            Assert.Equal(UniversalLaunch.BlockOlder, UniversalLaunchPolicy.Decide(facts));
            Assert.False(UniversalLaunchPolicy.CanInstall(facts));
        }

        [Theory]
        [InlineData("1.0.3+commit", "1.0.3.9000", "1.0.3")]
        [InlineData("1.0.4-rc.1+commit", "1.0.4.3001", "1.0.4-rc.1")]
        [InlineData(null, "1.0.3.9000", null)]
        [InlineData("garbage", "1.0.3.9000", null)]
        [InlineData("1.0.3", "garbage", null)]
        [InlineData("1.0.3", "1.0.4.9000", null)]
        [InlineData("999999999999999999999999.0.3", "1.0.3.9000", null)]
        public void Discovery_metadata_is_consistent_and_product_labels_exclude_commit_and_file_stage(
            string product, string file, string label)
        {
            Assert.Equal(label != null, InstalledCopy.TryReadVersion(product, file, out var version));
            Assert.Equal(label, version?.ToString());
        }
    }
}
