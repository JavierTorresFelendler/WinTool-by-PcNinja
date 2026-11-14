using System;
using System.Threading;
using WinTool.Core.Diagnostics;
using WinTool.Core.Updates;

namespace WinTool.Core
{
    /// <summary>Test configuration belongs to Debug and explicitly versioned test builds only.</summary>
    public static class TestEnvironment
    {
        private static int warned;
#if DEBUG
        public const bool IsDebugBuild = true;
#else
        public const bool IsDebugBuild = false;
#endif
        public static bool Allowed => IsDebugBuild || AppVersion.Current.IsTest;

        public static string Get(string variable)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            var result = Resolve(value, IsDebugBuild, AppVersion.Current);
            if (result == null && !string.IsNullOrWhiteSpace(value) && Interlocked.Exchange(ref warned, 1) == 0)
                AppLog.Current.Warn("Configuration", "Test environment overrides are ignored by this stable build.");
            return result;
        }

        internal static string Resolve(string value, bool debugBuild, AppVersion version) =>
            debugBuild || version.IsTest ? value : null;
    }
}
