using System;
using System.Linq;
using System.Text.RegularExpressions;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Logic
{
    public static class UpdateClassifier
    {
        private static readonly Regex FirmwareWords = new Regex(@"\bfirmware\b|\bbios\b|\buefi\b|system firmware",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>WU-05: Driver when type is driver, Optional when BrowseOnly, otherwise Windows.</summary>
        public static UpdateKind KindOf(UpdateItem update)
        {
            if (update.IsDriverType) return UpdateKind.Driver;
            return update.BrowseOnly ? UpdateKind.Optional : UpdateKind.Windows;
        }

        /// <summary>WU-06: the driver class first, then words in the title and categories.</summary>
        public static bool IsFirmware(UpdateItem update)
        {
            if (string.Equals(update.DriverClass, "Firmware", StringComparison.OrdinalIgnoreCase)) return true;
            var text = update.Title + " " + string.Join(" ", update.Categories);
            return FirmwareWords.IsMatch(text);
        }

        /// <summary>WU-07: firmware follows the firmware switch; everything else follows its kind.</summary>
        public static bool IsIncluded(UpdateItem update, UpdateScope scope)
        {
            if (IsFirmware(update)) return scope.Firmware;
            switch (KindOf(update))
            {
                case UpdateKind.Driver: return scope.Drivers;
                case UpdateKind.Optional: return scope.Optional;
                default: return scope.Windows;
            }
        }

        public static string Describe(UpdateItem update)
        {
            return update.Title
                + " | " + (update.KbArticles.Count == 0 ? "No KB listed" : string.Join(", ", update.KbArticles.Select(k => k.StartsWith("KB", StringComparison.OrdinalIgnoreCase) ? k : "KB" + k)))
                + " | Type: " + (update.IsDriverType ? "Driver" : "Software")
                + " | Categories: " + (update.Categories.Count == 0 ? "No category listed" : string.Join(", ", update.Categories))
                + " | Flags: BrowseOnly=" + update.BrowseOnly + ", IsMandatory=" + update.IsMandatory
                + " | Firmware: " + (IsFirmware(update) ? "Yes" : "No")
                + (update.Sources.Count > 0 ? " | Sources: " + string.Join(", ", update.Sources) : string.Empty);
        }
    }
}
