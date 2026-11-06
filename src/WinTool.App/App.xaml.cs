using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinTool.App.Localization;
using WinTool.App.Theming;
using WinTool.App.ViewModels;
using WinTool.App.Views;
using WinTool.Core.Diagnostics;
using WinTool.Core.Platform;
using WinTool.Core.Settings;

namespace WinTool.App
{
    public partial class App : Application
    {
        private SettingsStore store;
        private AppSettings settings;

        internal int RunMainWindow(AppSettings loadedSettings)
        {
            settings = loadedSettings ?? new AppSettings();
            store = new SettingsStore(SettingsStore.DefaultPath);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            ApplyTheme();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

            var settingsPage = new SettingsPageViewModel(settings, SaveSettings, ApplyTheme);
            var window = new MainWindow { DataContext = new MainViewModel(settingsPage) };
            MainWindow = window;
            AppLog.Current.Info("App", "Window opened; theme " + ThemeManager.Current + ", language " + Loc.Instance.Language + ".");
            return Run(window);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // SystemEvents holds static references; always unsubscribe.
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            AppLog.Current.Info("App", "Exit code " + e.ApplicationExitCode + ".");
            base.OnExit(e);
        }

        private void ApplyTheme()
        {
            object appsUseLightTheme = null;
            try
            {
                appsUseLightTheme = ThemeResolver.ReadAppsUseLightTheme();
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                AppLog.Current.Warn("Theme", "Windows app mode could not be read: " + ex.Message);
            }

            ThemeManager.Apply(ThemeManager.Choose(settings.ThemePreference, appsUseLightTheme, SystemParameters.HighContrast));
        }

        private void SaveSettings()
        {
            try
            {
                store.Save(settings);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                AppLog.Current.Error("Settings", "Settings could not be saved.", ex);
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.VisualStyle)
            {
                Dispatcher.BeginInvoke(new Action(ApplyTheme));
            }
        }

        private void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
            {
                Dispatcher.BeginInvoke(new Action(ApplyTheme));
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLog.Current.Error("App", "Unhandled UI error.", e.Exception);
            MessageBox.Show(Loc.Instance.Format("Error_Unexpected", AppLog.Current.CurrentFilePath), WinTool.Core.ProductInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            AppLog.Current.Error("App", "Unhandled error; the application stops.", e.ExceptionObject as Exception);
        }
    }
}
