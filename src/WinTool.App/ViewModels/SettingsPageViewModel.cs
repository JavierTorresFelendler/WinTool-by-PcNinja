using System;
using System.Windows.Input;
using WinTool.App.Localization;
using WinTool.Core;
using WinTool.Core.Localization;
using WinTool.Core.Platform;
using WinTool.Core.Settings;

namespace WinTool.App.ViewModels
{
    public sealed class SettingsPageViewModel : PageViewModel
    {
        private readonly AppSettings settings;
        private readonly Action save;
        private readonly Action applyTheme;

        public SettingsPageViewModel(AppSettings settings, Action save, Action applyTheme, AppUpdateViewModel updates)
            : base("Nav_Settings", null)
        {
            Updates = updates;
            CheckNowCommand = new RelayCommand(() => _ = updates.CheckAsync(true), () => !updates.IsBusy);
            this.settings = settings;
            this.save = save;
            this.applyTheme = applyTheme;
            OpenWebsiteCommand = new RelayCommand(() => MainViewModel.OpenUrl(ProductInfo.WebsiteUrl));
            OpenRepositoryCommand = new RelayCommand(() => MainViewModel.OpenUrl(ProductInfo.RepositoryUrl));
        }

        public bool IsThemeSystem { get => Is(ThemePreference.System); set => SetTheme(value, ThemePreference.System); }
        public bool IsThemeLight { get => Is(ThemePreference.Light); set => SetTheme(value, ThemePreference.Light); }
        public bool IsThemeDark { get => Is(ThemePreference.Dark); set => SetTheme(value, ThemePreference.Dark); }

        public bool IsEnglish { get => Loc.Instance.Language == LanguageResolver.English; set => SetLanguage(value, LanguageResolver.English); }
        public bool IsHebrew { get => Loc.Instance.Language == LanguageResolver.Hebrew; set => SetLanguage(value, LanguageResolver.Hebrew); }
        public bool IsSpanish { get => Loc.Instance.Language == LanguageResolver.Spanish; set => SetLanguage(value, LanguageResolver.Spanish); }
        public bool IsPseudo { get => Loc.Instance.Language == LanguageResolver.Pseudo; set => SetLanguage(value, LanguageResolver.Pseudo); }
        public bool ShowPseudo => Loc.PseudoAllowed;

        public string Version => ProductInfo.Name + " " + ProductInfo.InformationalVersion;
        public string WebsiteText => ProductInfo.WebsiteUrl.Replace("https://", string.Empty);

        public ICommand OpenWebsiteCommand { get; }
        public ICommand CheckNowCommand { get; }
        public AppUpdateViewModel Updates { get; }

        public bool CheckAtStartup
        {
            get => settings.CheckForAppUpdatesAtStartup;
            set { settings.CheckForAppUpdatesAtStartup = value; save(); OnPropertyChanged(); }
        }

        public bool IncludeTestVersions
        {
            get => settings.IncludeTestVersions;
            set { settings.IncludeTestVersions = value; save(); OnPropertyChanged(); }
        }
        public ICommand OpenRepositoryCommand { get; }

        private bool Is(ThemePreference preference) => settings.ThemePreference == preference;

        private void SetTheme(bool selected, ThemePreference preference)
        {
            if (!selected || settings.ThemePreference == preference) return;
            settings.ThemePreference = preference;
            save();
            applyTheme();
            OnPropertyChanged(nameof(IsThemeSystem));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeDark));
        }

        private void SetLanguage(bool selected, string language)
        {
            if (!selected || Loc.Instance.Language == language) return;
            settings.Language = language;
            save();
            Loc.Instance.SetLanguage(language);
        }
    }
}
