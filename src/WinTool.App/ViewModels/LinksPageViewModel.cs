using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinTool.Core;
using WinTool.Core.Diagnostics;

namespace WinTool.App.ViewModels
{
    /// <summary>Useful Links (formerly Drivers &amp; Links in the mockup): help, PcNinja's free tools and manufacturer pages. Opens links only.</summary>
    public sealed class LinksPageViewModel : PageViewModel
    {
        /// <summary>The password of PcNinja's shared download files. Public on purpose (owner decision).</summary>
        public const string FilePassword = "JavierTorres";

        private bool copied;

        public LinksPageViewModel()
            : base("Nav_Links", "Page_Links_Subtitle")
        {
            OpenCommand = new RelayCommand<string>(Open);
            CopyPasswordCommand = new RelayCommand(CopyPassword);
        }

        public ICommand OpenCommand { get; }
        public ICommand CopyPasswordCommand { get; }

        public string Password => FilePassword;
        public string HelpUrl => ProductInfo.HelpUrl;
        public string WebsiteUrl => ProductInfo.WebsiteUrl;
        public string RepositoryUrl => ProductInfo.RepositoryUrl;
        public string DriverUpdaterUrl => "https://driver.pcninja.pro";
        public string ActivationUrl => "https://active.pcninja.pro";
        public string DellUrl => "https://www.dell.com/support/home/en-us?app=drivers";
        public string HpUrl => "https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HPIA.html";
        public string LenovoUrl => "https://support.lenovo.com/us/en/solutions/ht003029-lenovo-system-update-update-drivers-bios-and-applications";
        public string AsusUrl => "https://www.asus.com/support/download-center/";

        public string CopyLabel => copied ? T["LK_Copied"] : T["LK_Copy"];

        private void CopyPassword()
        {
            try
            {
                Clipboard.SetText(FilePassword);
                copied = true;
                OnPropertyChanged(nameof(CopyLabel));
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                AppLog.Current.Warn("UI", "Clipboard busy: " + ex.Message);
            }
        }

        private static void Open(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { AppLog.Current.Warn("UI", "Opening " + url + " failed: " + ex.Message); }
        }
    }
}
