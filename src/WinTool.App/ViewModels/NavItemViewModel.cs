using System;
using System.Windows.Media;

namespace WinTool.App.ViewModels
{
    public sealed class NavItemViewModel : LocalizedObject
    {
        private readonly Action<NavItemViewModel> onSelected;
        private bool isSelected;

        public NavItemViewModel(string key, string titleKey, string iconPath, bool separatorBefore, PageViewModel page, Action<NavItemViewModel> onSelected)
        {
            Key = key;
            TitleKey = titleKey;
            Icon = Geometry.Parse(iconPath);
            Icon.Freeze();
            SeparatorBefore = separatorBefore;
            Page = page;
            this.onSelected = onSelected;
        }

        public string Key { get; }
        public string TitleKey { get; }
        public string Title => T[TitleKey];
        public Geometry Icon { get; }
        public bool SeparatorBefore { get; }
        public PageViewModel Page { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (Set(ref isSelected, value) && value) onSelected(this);
            }
        }
    }
}
