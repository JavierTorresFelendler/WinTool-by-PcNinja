using Microsoft.Win32;

namespace WinTool.Core.Platform
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public enum ThemePreference
    {
        System,
        Light,
        Dark
    }

    /// <summary>Chooses the app theme from the user preference and the Windows app mode (decision D-012).</summary>
    public static class ThemeResolver
    {
        public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public const string AppsUseLightThemeValue = "AppsUseLightTheme";

        public static AppTheme Resolve(ThemePreference preference, object appsUseLightTheme)
        {
            switch (preference)
            {
                case ThemePreference.Light:
                    return AppTheme.Light;
                case ThemePreference.Dark:
                    return AppTheme.Dark;
                default:
                    return SystemTheme(appsUseLightTheme);
            }
        }

        /// <summary>Windows stores 1 for light and 0 for dark. A missing value means light (the Windows default).</summary>
        public static AppTheme SystemTheme(object appsUseLightTheme)
        {
            if (appsUseLightTheme is int)
            {
                return (int)appsUseLightTheme == 0 ? AppTheme.Dark : AppTheme.Light;
            }

            return AppTheme.Light;
        }

        public static object ReadAppsUseLightTheme()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
            {
                return key?.GetValue(AppsUseLightThemeValue);
            }
        }
    }
}
