using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinTool.WindowsUpdate.Migration;

namespace WinTool.WindowsUpdate.Platform
{
    /// <summary>Installed WinUpdate Tool V1/V2 on this computer: MSI products and the script install (D-025).</summary>
    public sealed class WindowsLegacyInstallations : ILegacyInstallations
    {
        private const int NoMoreItems = 259;
        private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(10);

        public IReadOnlyList<LegacyInstallation> Find()
        {
            var found = new List<LegacyInstallation>();
            var code = new StringBuilder(39);
            for (var i = 0; ; i++)
            {
                code.Clear();
                var result = MsiEnumRelatedProducts(LegacyInstallPolicy.UpgradeCode, 0, i, code);
                if (result != 0) break; // NoMoreItems or an error: stop either way.
                found.Add(LegacyInstallPolicy.ForMsi(code.ToString()));
            }

            // The script install leaves an Uninstall key; an MSI install removes that key itself.
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + LegacyInstallPolicy.UninstallKeyName))
            {
                var quiet = key?.GetValue("QuietUninstallString") as string;
                if (quiet != null && found.Count == 0)
                {
                    var script = LegacyInstallPolicy.ForScript(quiet,
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Environment.GetFolderPath(Environment.SpecialFolder.System));
                    if (script != null) found.Add(script);
                }
            }

            return found;
        }

        public int Uninstall(LegacyInstallation installation)
        {
            using (var process = Process.Start(new ProcessStartInfo(installation.File, installation.Arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (!process.WaitForExit((int)UninstallTimeout.TotalMilliseconds))
                    throw new TimeoutException(installation + " did not finish uninstalling within " + UninstallTimeout.TotalMinutes + " minutes.");
                return process.ExitCode;
            }
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, StringBuilder productCode);
    }
}
