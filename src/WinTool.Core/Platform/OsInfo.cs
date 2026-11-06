using System;
using System.Globalization;
using Microsoft.Win32;

namespace WinTool.Core.Platform
{
    /// <summary>Windows version facts and the supported-OS rule (decision D-002).</summary>
    public sealed class OsInfo
    {
        /// <summary>Windows 10 version 1903.</summary>
        public const int MinimumSupportedBuild = 18362;

        public OsInfo(string productName, string displayVersion, int build, int revision, bool is64BitOperatingSystem)
        {
            ProductName = productName ?? string.Empty;
            DisplayVersion = displayVersion ?? string.Empty;
            Build = build;
            Revision = revision;
            Is64BitOperatingSystem = is64BitOperatingSystem;
        }

        public string ProductName { get; }
        public string DisplayVersion { get; }
        public int Build { get; }
        public int Revision { get; }
        public bool Is64BitOperatingSystem { get; }

        public bool IsSupported
        {
            get { return IsSupportedBuild(Build) && Is64BitOperatingSystem; }
        }

        /// <summary>Windows 11 builds report "Windows 10" in ProductName; fix it from the build number.</summary>
        public string FriendlyName
        {
            get
            {
                var name = ProductName;
                if (Build >= 22000 && name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
                {
                    name = "Windows 11" + name.Substring("Windows 10".Length);
                }

                return string.IsNullOrEmpty(DisplayVersion) ? name : name + " " + DisplayVersion;
            }
        }

        public static bool IsSupportedBuild(int build)
        {
            return build >= MinimumSupportedBuild;
        }

        public static OsInfo ReadCurrent()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                var product = key?.GetValue("ProductName") as string;
                var display = (key?.GetValue("DisplayVersion") as string) ?? (key?.GetValue("ReleaseId") as string);
                int build;
                int.TryParse(key?.GetValue("CurrentBuildNumber") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out build);
                var revisionValue = key?.GetValue("UBR");
                var revision = revisionValue is int ? (int)revisionValue : 0;
                return new OsInfo(product, display, build, revision, Environment.Is64BitOperatingSystem);
            }
        }
    }
}
