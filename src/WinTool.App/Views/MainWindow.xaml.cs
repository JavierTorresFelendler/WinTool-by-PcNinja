using System;
using System.Windows;
using WinTool.App.Localization;

namespace WinTool.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            StateChanged += OnStateChanged;
            Loc.Instance.LanguageChanged += (sender, e) => UpdateMaximizeButton();
        }

        private void OnMinimize(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void OnMaximizeRestore(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeButton();
        }

        private void UpdateMaximizeButton()
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = maximized ? "" : "";
            System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, Loc.Instance[maximized ? "Caption_Restore" : "Caption_Maximize"]);
        }
    }
}
