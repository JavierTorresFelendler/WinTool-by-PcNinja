using System.Windows;
using WinTool.App.Localization;
using WinTool.Core;

namespace WinTool.App.ViewModels
{
    /// <summary>Message boxes in the current language and direction.</summary>
    public static class Dialogs
    {
        public static bool Confirm(string text) =>
            Show(text, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        public static void Info(string text) => Show(text, MessageBoxButton.OK, MessageBoxImage.Information);

        public static void Error(string text) => Show(text, MessageBoxButton.OK, MessageBoxImage.Error);

        private static MessageBoxResult Show(string text, MessageBoxButton buttons, MessageBoxImage image)
        {
            var options = Loc.Instance.FlowDirection == FlowDirection.RightToLeft
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                : MessageBoxOptions.None;
            var owner = Application.Current?.MainWindow;
            return owner != null
                ? MessageBox.Show(owner, text, ProductInfo.Name, buttons, image, MessageBoxResult.None, options)
                : MessageBox.Show(text, ProductInfo.Name, buttons, image, MessageBoxResult.None, options);
        }
    }
}
