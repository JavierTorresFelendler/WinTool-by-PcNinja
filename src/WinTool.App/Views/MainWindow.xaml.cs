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
            Loaded += (sender, e) => ApplyStateMargin();
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Codex review F1: never drop an operation that is still running (its lock, result and cleanup).
            if (DataContext is ViewModels.MainViewModel model && model.AnyOperationRunning)
            {
                e.Cancel = true;
                if (!model.ClosingWhenIdle && ViewModels.Dialogs.Confirm(Loc.Instance.Format("Close_OperationRunning", model.RunningOperationNames)))
                {
                    model.CancelAllThenClose(Close);
                }
            }

            base.OnClosing(e);
        }

        /// <summary>A maximized custom-chrome window overhangs the screen by the resize border.</summary>
        private void ApplyStateMargin()
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            ApplyStateMargin();
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
