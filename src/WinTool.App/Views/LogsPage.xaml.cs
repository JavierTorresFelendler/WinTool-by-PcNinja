using System;
using System.Windows;
using System.Windows.Controls;
using WinTool.App.ViewModels;

namespace WinTool.App.Views
{
    public partial class LogsPage : UserControl
    {
        public LogsPage()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is LogsPageViewModel old) old.FollowedToEnd -= ScrollToEnd;
            if (e.NewValue is LogsPageViewModel model) model.FollowedToEnd += ScrollToEnd;
        }

        private void ScrollToEnd(object sender, EventArgs e)
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }
    }
}
