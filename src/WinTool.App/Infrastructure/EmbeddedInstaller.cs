using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using WinTool.Core.Diagnostics;

namespace WinTool.App.Infrastructure
{
    /// <summary>
    /// D-026 prototype: the universal EXE carries WinTool's MSI as a resource, so a downloaded copy
    /// can install itself offline. Normal builds carry nothing and <see cref="IsPresent"/> is false.
    /// </summary>
    internal static class EmbeddedInstaller
    {
        private const string MsiResource = "WinTool.Installer.msi";
        private const string HashResource = "WinTool.Installer.msi.sha256";

        public static bool IsPresent => typeof(EmbeddedInstaller).Assembly.GetManifestResourceInfo(MsiResource) != null;

        /// <summary>Copies the MSI to a new temporary folder and checks it against the hash taken at build time.</summary>
        public static string Extract()
        {
            var assembly = typeof(EmbeddedInstaller).Assembly;
            string expected;
            using (var hash = assembly.GetManifestResourceStream(HashResource))
            using (var reader = new StreamReader(hash ?? throw new InvalidOperationException("The installer hash is missing.")))
            {
                expected = reader.ReadToEnd().Trim();
            }

            var folder = Path.Combine(Path.GetTempPath(), "WinTool-Install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "WinTool-by-PcNinja.msi");
            using (var source = assembly.GetManifestResourceStream(MsiResource))
            using (var target = File.Create(path))
            {
                if (source == null) throw new InvalidOperationException("The installer is missing.");
                source.CopyTo(target);
            }

            string actual;
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path))
            {
                actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty);
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The embedded installer does not match its hash.");
            return path;
        }

        /// <summary>Starts the regular installation wizard from the embedded MSI.</summary>
        public static void StartInstall()
        {
            var msi = Extract();
            AppLog.Current.Info("App", "Installing from the embedded installer: " + msi);
            Process.Start(new ProcessStartInfo("msiexec.exe", "/i \"" + msi + "\"" + InstallerLanguage.Arguments(msi)) { UseShellExecute = true });
        }
    }
}
