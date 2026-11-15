using System;
using System.Linq;

namespace WinTool.Core.Updates
{
    public enum StartupArguments { DefaultGui, PortableGui, CommandLine, UsageError }
    public enum UniversalLaunch { RunCurrent, AskInstall, OpenInstalled, AskUpdate, AskUnprotected, BlockOlder }
    public enum LaunchAnswer { Cancel, Yes, No }
    public enum LaunchAction { Stop, RunCurrent, OpenInstalled, Install }

    /// <summary>Read-only observations, never authority carried across elevation (D-039).</summary>
    public sealed class UniversalLaunchFacts
    {
        public bool HasInstaller { get; set; }
        public bool IsInstalledSelf { get; set; }
        public bool IsProtectedWorker { get; set; }
        public bool PortableIntent { get; set; }
        public Version CurrentVersion { get; set; }
        public Version InstalledVersion { get; set; }
        public bool InstalledProtected { get; set; }
    }

    /// <summary>Pure GUI policy. No registry, filesystem, process, UI or installation effects.</summary>
    public static class UniversalLaunchPolicy
    {
        public const string PortableArgument = "--portable";
        public const string InternalPortableArgument = "--portable-chosen";

        public static StartupArguments Classify(string[] args, bool hasInstaller)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length == 0) return StartupArguments.DefaultGui;
            // Plain builds keep their existing CLI and internal-marker behavior.
            if (args.Length == 1 && (args[0] == InternalPortableArgument ||
                (hasInstaller && args[0] == PortableArgument))) return StartupArguments.PortableGui;
            if (hasInstaller && args.Any(a => a == PortableArgument || a == InternalPortableArgument))
                return StartupArguments.UsageError;
            return StartupArguments.CommandLine;
        }

        public static UniversalLaunch Decide(UniversalLaunchFacts facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (!facts.HasInstaller || facts.IsInstalledSelf) return UniversalLaunch.RunCurrent;
            var known = facts.InstalledVersion != null;
            var older = known && (facts.CurrentVersion == null || facts.InstalledVersion > facts.CurrentVersion);
            // Applies even to internal markers, restart intent and protected managed workers.
            if (older && (facts.PortableIntent || !facts.InstalledProtected)) return UniversalLaunch.BlockOlder;
            if (facts.PortableIntent) return UniversalLaunch.RunCurrent;
            if (known)
            {
                if (!facts.InstalledProtected) return UniversalLaunch.AskUnprotected;
                if (facts.CurrentVersion == null || facts.InstalledVersion >= facts.CurrentVersion)
                    return UniversalLaunch.OpenInstalled;
                return UniversalLaunch.AskUpdate;
            }
            return facts.IsProtectedWorker ? UniversalLaunch.RunCurrent : UniversalLaunch.AskInstall;
        }

        public static LaunchAction Resolve(UniversalLaunchFacts facts, LaunchAnswer answer)
        {
            var decision = Decide(facts);
            switch (decision)
            {
                case UniversalLaunch.RunCurrent: return LaunchAction.RunCurrent;
                case UniversalLaunch.OpenInstalled: return LaunchAction.OpenInstalled;
                case UniversalLaunch.AskInstall:
                case UniversalLaunch.AskUnprotected:
                    return answer == LaunchAnswer.Yes ? (CanInstall(facts) ? LaunchAction.Install : LaunchAction.Stop) :
                        answer == LaunchAnswer.No ? LaunchAction.RunCurrent : LaunchAction.Stop;
                case UniversalLaunch.AskUpdate:
                    return answer == LaunchAnswer.Yes ? (CanInstall(facts) ? LaunchAction.Install : LaunchAction.Stop) :
                        answer == LaunchAnswer.No ? LaunchAction.OpenInstalled : LaunchAction.Stop;
                default: return LaunchAction.Stop;
            }
        }

        public static bool CanInstall(UniversalLaunchFacts facts) => facts.HasInstaller && facts.CurrentVersion != null &&
            (facts.InstalledVersion == null || facts.CurrentVersion > facts.InstalledVersion);
    }
}
