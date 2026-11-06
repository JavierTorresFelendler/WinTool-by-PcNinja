using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Input;
using System.Linq;
using WinTool.Core;
using WinTool.WindowsUpdate.Engine;

namespace WinTool.App.ViewModels
{
    public sealed class MainViewModel : LocalizedObject
    {
        // 24x24 stroke icons, the same shapes as in the approved mockup.
        private const string IconDashboard = "M3.5,16 A8.5,8.5 0 1 1 20.5,16 M12,16 L16,11 M3.5,19.5 H20.5";
        private const string IconUpdate = "M20,12 A8,8 0 1 1 17.7,6.4 M20,4 V8.5 H15.5 M12,8.5 V12.5 L14.5,14";
        private const string IconUpgrade = "M12,3 A9,9 0 1 1 11.99,3 Z M12,16.5 V8 M8.5,11.5 L12,8 L15.5,11.5";
        private const string IconOffice = "M5.5,4 H9.5 A1.5,1.5 0 0 1 11,5.5 V9.5 A1.5,1.5 0 0 1 9.5,11 H5.5 A1.5,1.5 0 0 1 4,9.5 V5.5 A1.5,1.5 0 0 1 5.5,4 Z M14.5,4 H18.5 A1.5,1.5 0 0 1 20,5.5 V9.5 A1.5,1.5 0 0 1 18.5,11 H14.5 A1.5,1.5 0 0 1 13,9.5 V5.5 A1.5,1.5 0 0 1 14.5,4 Z M5.5,13 H9.5 A1.5,1.5 0 0 1 11,14.5 V18.5 A1.5,1.5 0 0 1 9.5,20 H5.5 A1.5,1.5 0 0 1 4,18.5 V14.5 A1.5,1.5 0 0 1 5.5,13 Z M14.5,13 H18.5 A1.5,1.5 0 0 1 20,14.5 V18.5 A1.5,1.5 0 0 1 18.5,20 H14.5 A1.5,1.5 0 0 1 13,18.5 V14.5 A1.5,1.5 0 0 1 14.5,13 Z";
        private const string IconLogs = "M9,6 H20 M9,12 H20 M9,18 H20 M4.5,6 H4.6 M4.5,12 H4.6 M4.5,18 H4.6";
        private const string IconSettings = "M12,9 A3,3 0 1 1 11.99,9 Z M12,2.5 V5.5 M12,18.5 V21.5 M2.5,12 H5.5 M18.5,12 H21.5 M5.3,5.3 L7.4,7.4 M16.6,16.6 L18.7,18.7 M5.3,18.7 L7.4,16.6 M16.6,7.4 L18.7,5.3";
        private const string IconLinks = "M10,14 A4.5,4.5 0 0 0 16.4,14 L19.4,11 A4.5,4.5 0 0 0 13,4.6 L12,5.6 M14,10 A4.5,4.5 0 0 0 7.6,10 L4.6,13 A4.5,4.5 0 0 0 11,19.4 L12,18.4";

        private PageViewModel currentPage;
        private bool isCompact;

        public MainViewModel(SettingsPageViewModel settingsPage, IWindowsUpdateEnvironment windowsUpdate)
        {
            var windowsUpdatePage = new WindowsUpdatePageViewModel(windowsUpdate);
            var upgradePage = new WindowsUpgradePageViewModel();
            var officePage = new OfficePageViewModel();
            var dashboardPage = new DashboardPageViewModel(windowsUpdate, windowsUpdatePage, upgradePage, officePage, Navigate);
            NavItems = new List<NavItemViewModel>
            {
                Item("dashboard", "Nav_Dashboard", IconDashboard, false, dashboardPage),
                Item("windows-update", "Nav_WindowsUpdate", IconUpdate, true, windowsUpdatePage),
                Item("windows-upgrade", "Nav_WindowsUpgrade", IconUpgrade, false, upgradePage),
                Item("office", "Nav_Office", IconOffice, false, officePage),
                Item("logs", "Nav_Logs", IconLogs, true, new LogsPageViewModel(windowsUpdate)),
                Item("settings", "Nav_Settings", IconSettings, false, settingsPage),
                Item("links", "Nav_Links", IconLinks, false, new LinksPageViewModel())
            };

            OpenHelpCommand = new RelayCommand(() => OpenUrl(ProductInfo.HelpUrl));
            CheckToolUpdateCommand = new RelayCommand(() => { }, () => false);
            NavItems[0].IsSelected = true;
        }

        public IReadOnlyList<NavItemViewModel> NavItems { get; }

        public PageViewModel CurrentPage
        {
            get => currentPage;
            private set
            {
                var previous = currentPage;
                if (!Set(ref currentPage, value)) return;
                previous?.OnDeactivated();
                value?.OnActivated();
            }
        }

        /// <summary>True on narrow windows: the sidebar shows icons only.</summary>
        public bool IsCompact
        {
            get => isCompact;
            set
            {
                if (Set(ref isCompact, value)) OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public bool IsExpanded => !isCompact;

        public string VersionText => T.Format("Footer_Installed", ProductInfo.Version);

        public ICommand OpenHelpCommand { get; }

        /// <summary>Disabled until the self-update flow (roadmap phase 6).</summary>
        public ICommand CheckToolUpdateCommand { get; }

        public void Navigate(string key)
        {
            var item = NavItems.FirstOrDefault(i => i.Key == key);
            if (item != null) item.IsSelected = true;
        }

        internal static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private NavItemViewModel Item(string key, string titleKey, string icon, bool separatorBefore, PageViewModel page)
        {
            return new NavItemViewModel(key, titleKey, icon, separatorBefore, page, selected =>
            {
                foreach (var item in NavItems)
                {
                    if (!ReferenceEquals(item, selected)) item.IsSelected = false;
                }

                CurrentPage = selected.Page;
            });
        }
    }
}
