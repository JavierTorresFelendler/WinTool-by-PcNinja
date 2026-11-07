using System;
using System.Runtime.InteropServices;
using WinTool.App.Localization;
using WinTool.Core.Diagnostics;
using WinTool.Core.Localization;

namespace WinTool.App.Infrastructure
{
    /// <summary>
    /// WinTool's MSI is English with Hebrew and Spanish embedded as language transforms
    /// (packaging/msi/Build-Msi.ps1). When WinTool starts the installer, it opens in WinTool's
    /// language. An MSI without that transform (an older release) opens in English rather than failing.
    /// </summary>
    internal static class InstallerLanguage
    {
        /// <summary>msiexec arguments for the current language, with a leading space, or empty.</summary>
        public static string Arguments(string msiPath)
        {
            var lcid = LcidFor(Loc.Instance.Language);
            if (lcid == null) return string.Empty;
            // Windows Installer shows text through the system ANSI code page: Hebrew on a system whose
            // code page is not 1255 turns into '?'. Then the installer stays English (CI, 2026-11-07).
            var needed = CodepageFor(lcid);
            if (GetACP() != needed)
            {
                AppLog.Current.Info("Update", "The installer opens in English: Windows' code page is " + GetACP() + ", the " + lcid + " texts need " + needed + ".");
                return string.Empty;
            }

            try
            {
                if (HasStorage(msiPath, lcid)) return " TRANSFORMS=:" + lcid;
                AppLog.Current.Info("Update", "The installer has no " + lcid + " language; it opens in English.");
            }
            catch (Exception ex) when (ex is ExternalException || ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                AppLog.Current.Warn("Update", "Could not read the installer's languages: " + ex.Message);
            }

            return string.Empty;
        }

        internal static uint CodepageFor(string lcid) => lcid == "1037" ? 1255u : 1252u;

        internal static string LcidFor(string language)
        {
            if (language == LanguageResolver.Hebrew) return "1037";
            if (language == LanguageResolver.Spanish) return "3082";
            return null;
        }

        private static bool HasStorage(string msiPath, string name)
        {
            IntPtr database = IntPtr.Zero, view = IntPtr.Zero, record = IntPtr.Zero;
            try
            {
                Check(MsiOpenDatabase(msiPath, IntPtr.Zero, out database));
                Check(MsiDatabaseOpenView(database, "SELECT `Name` FROM `_Storages` WHERE `Name`='" + name + "'", out view));
                Check(MsiViewExecute(view, IntPtr.Zero));
                var fetched = MsiViewFetch(view, out record);
                if (fetched == NoMoreItems) return false;
                Check(fetched);
                return true;
            }
            finally
            {
                if (record != IntPtr.Zero) MsiCloseHandle(record);
                if (view != IntPtr.Zero) MsiCloseHandle(view);
                if (database != IntPtr.Zero) MsiCloseHandle(database);
            }
        }

        private const uint NoMoreItems = 259;

        private static void Check(uint code)
        {
            if (code != 0) throw new ExternalException("Windows Installer error " + code, (int)code);
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetACP();

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiOpenDatabase(string databasePath, IntPtr persist, out IntPtr database);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);

        [DllImport("msi.dll")]
        private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

        [DllImport("msi.dll")]
        private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

        [DllImport("msi.dll")]
        private static extern uint MsiCloseHandle(IntPtr handle);
    }
}
