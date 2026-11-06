using System;
using System.Windows;
using WinTool.App.Localization;
using WinTool.App.ViewModels;
using WinTool.Core.Platform;

namespace WinTool.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ApplyOpeningSize();
            StateChanged += OnStateChanged;
            SizeChanged += (sender, e) => UpdateCompactLayout();
            DataContextChanged += (sender, e) => UpdateCompactLayout();
            Loc.Instance.LanguageChanged += (sender, e) => UpdateMaximizeButton();
        }

        /// <summary>Fits the window to small screens such as 1024x768 (decision D-019).</summary>
        private void ApplyOpeningSize()
        {
            var workArea = SystemParameters.WorkArea;
            var placement = WindowSizing.Choose(workArea.Width, workArea.Height);
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximize) WindowState = WindowState.Maximized;
        }

        private void UpdateCompactLayout()
        {
            if (DataContext is MainViewModel model)
            {
                var width = ActualWidth > 0 ? ActualWidth : Width;
                model.IsCompact = WindowSizing.UseCompactSidebar(width);
            }
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
