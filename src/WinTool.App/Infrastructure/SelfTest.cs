using System;
using System.IO;
using System.Linq;
using WinTool.App.Localization;
using WinTool.Core;
using WinTool.Core.Diagnostics;

namespace WinTool.App.Infrastructure
{
    /// <summary>
    /// "--self-test": proves that a copied WinTool-by-PcNinja.exe is complete. CI runs it
    /// on the portable EXE alone in an empty folder.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(TextWriter output)
        {
            var failures = 0;
            failures += Check(output, "Core library loads", () => !string.IsNullOrEmpty(ProductInfo.Version));
            failures += Check(output, "Windows Update module loads", () => typeof(WinTool.WindowsUpdate.Engine.UpdateRunner).Assembly != null
                && typeof(WinTool.Cli.CommandRunner).Assembly != null);
            failures += Check(output, "Windows Upgrade module loads", () => WinTool.Upgrade.DownloadSourcePolicy.Sources.Count > 0);
            failures += Check(output, "Office module loads", () => WinTool.Office.OfficeCatalog.Languages.Count > 0);
            failures += Check(output, "English text is present", () => Loc.Instance.KeysFor("en").Count > 0);

            failures += Check(output, "Spanish has every English key", () =>
            {
                var missing = Loc.Instance.KeysFor("en").Except(Loc.Instance.KeysFor("es")).ToList();
                foreach (var key in missing) output.WriteLine("    missing in Spanish: " + key);
                return missing.Count == 0;
            });
            failures += Check(output, "Hebrew has every English key", () =>
            {
                var english = Loc.Instance.KeysFor("en");
                var hebrew = Loc.Instance.KeysFor("he");
                var missing = english.Except(hebrew).ToList();
                foreach (var key in missing) output.WriteLine("    missing in Hebrew: " + key);
                return missing.Count == 0;
            });

            failures += Check(output, "Text still resolves after the checks", () =>
                Loc.Instance["Nav_Dashboard"] == "Dashboard");

            failures += Check(output, "Theme files are embedded", () =>
            {
                var compiled = Theming.ThemeManager.CompiledResourceKeys();
                var missing = Theming.ThemeManager.ThemeResourceNames().Where(name => !compiled.Contains(name)).ToList();
                foreach (var name in missing) output.WriteLine("    missing theme: " + name);
                return missing.Count == 0;
            });

            output.WriteLine(failures == 0 ? "Self-test passed." : "Self-test failed: " + failures + " check(s).");
            AppLog.Current.Info("SelfTest", failures == 0 ? "Passed" : "Failed: " + failures);
            return failures == 0 ? Program.ExitOk : 1;
        }

        private static int Check(TextWriter output, string name, Func<bool> check)
        {
            bool ok;
            try
            {
                ok = check();
            }
            catch (Exception ex)
            {
                output.WriteLine("    " + ex.GetType().Name + ": " + ex.Message);
                ok = false;
            }

            output.WriteLine((ok ? "  OK    " : "  FAIL  ") + name);
            return ok ? 0 : 1;
        }
    }
}
