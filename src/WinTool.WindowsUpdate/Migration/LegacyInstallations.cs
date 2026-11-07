using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WinTool.WindowsUpdate.Migration
{
    public enum LegacyInstallKind
    {
        /// <summary>Installed with the WinUpdate Tool MSI (V1 and V2 share its UpgradeCode).</summary>
        Msi,

        /// <summary>Installed with Install-WinUpdateTool.ps1 (an Uninstall registry entry, no MSI).</summary>
        Script
    }

    public sealed class LegacyInstallation
    {
        public LegacyInstallation(LegacyInstallKind kind, string id, string file, string arguments)
        {
            Kind = kind;
            Id = id;
            File = file;
            Arguments = arguments;
        }

        public LegacyInstallKind Kind { get; }

        /// <summary>The MSI ProductCode, or the Uninstall registry key name.</summary>
        public string Id { get; }

        /// <summary>The program WinTool runs to uninstall it, built by WinTool, never copied from the registry.</summary>
        public string File { get; }

        public string Arguments { get; }

        public override string ToString() => "WinUpdate Tool (" + Kind + ", " + Id + ")";
    }

    /// <summary>Finds and removes installed WinUpdate Tool V1/V2 (decision D-025).</summary>
    public interface ILegacyInstallations
    {
        IReadOnlyList<LegacyInstallation> Find();

        /// <summary>Runs the uninstall and returns its exit code.</summary>
        int Uninstall(LegacyInstallation installation);
    }

    /// <summary>The rules D-025 sets for recognizing and removing the old tool.</summary>
    public static class LegacyInstallPolicy
    {
        /// <summary>UpgradeCode of PcNinja WinUpdate Tool V1 and V2 (PcNinja.WinUpdateTool.wxs).</summary>
        public const string UpgradeCode = "{7BD3183C-C7D9-4F0C-BAE9-FF143A450F53}";

        /// <summary>The Uninstall key Install-WinUpdateTool.ps1 writes.</summary>
        public const string UninstallKeyName = "PcNinja WinUpdate Tool";

        private static readonly Regex QuietScript = new Regex(
            "^\"(?<exe>[^\"]+)\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"(?<file>[^\"]+)\"$",
            RegexOptions.CultureInvariant);

        public static LegacyInstallation ForMsi(string productCode) =>
            new LegacyInstallation(LegacyInstallKind.Msi, productCode, "msiexec.exe", "/x " + productCode + " /qn /norestart");

        /// <summary>
        /// The script installation's uninstall, only when its QuietUninstallString is exactly what
        /// Install-WinUpdateTool.ps1 writes: System32 Windows PowerShell running
        /// &lt;Program Files&gt;\PcNinja\WinUpdateTool\Uninstall-WinUpdateTool.ps1. Anything else is not run.
        /// </summary>
        public static LegacyInstallation ForScript(string quietUninstallString, string programFiles, string systemFolder)
        {
            var match = QuietScript.Match((quietUninstallString ?? string.Empty).Trim());
            if (!match.Success) return null;
            // Windows paths, built explicitly (the product only runs on Windows; tests also run elsewhere).
            var expectedExe = systemFolder.TrimEnd('\\') + @"\WindowsPowerShell\v1.0\powershell.exe";
            var expectedScript = programFiles.TrimEnd('\\') + @"\PcNinja\WinUpdateTool\Uninstall-WinUpdateTool.ps1";
            if (!string.Equals(match.Groups["exe"].Value, expectedExe, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(match.Groups["file"].Value, expectedScript, StringComparison.OrdinalIgnoreCase)) return null;
            return new LegacyInstallation(LegacyInstallKind.Script, UninstallKeyName, expectedExe,
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + expectedScript + "\"");
        }

        /// <summary>0, a restart needed (3010, 1641) or already gone (1605) all mean it is uninstalled.</summary>
        public static bool Succeeded(int exitCode) => exitCode == 0 || exitCode == 3010 || exitCode == 1641 || exitCode == 1605;
    }
}
