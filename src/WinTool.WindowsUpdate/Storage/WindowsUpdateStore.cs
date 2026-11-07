using System;
using System.IO;
using WinTool.Core.IO;

namespace WinTool.WindowsUpdate.Storage
{
    public interface IWindowsUpdateStore
    {
        WindowsUpdateSettings LoadSettings();
        void SaveSettings(WindowsUpdateSettings settings);
        WindowsUpdateState LoadState();
        void SaveState(WindowsUpdateState state);
    }

    /// <summary>%ProgramData%\PcNinja\WinTool\WindowsUpdate\settings.json and state.json (WU-80).</summary>
    public sealed class WindowsUpdateStore : IWindowsUpdateStore
    {
        private readonly JsonFileStore<WindowsUpdateSettings> settings;
        private readonly JsonFileStore<WindowsUpdateState> state;

        public WindowsUpdateStore(string folder)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
            settings = new JsonFileStore<WindowsUpdateSettings>(Path.Combine(folder, "settings.json"));
            state = new JsonFileStore<WindowsUpdateState>(Path.Combine(folder, "state.json"));
        }

        public string Folder { get; }

        public static string DefaultFolder =>
            Path.Combine(WinTool.Core.WinToolPaths.MachineData, "WindowsUpdate");

        public WindowsUpdateSettings LoadSettings() => settings.LoadOrNull() ?? new WindowsUpdateSettings();
        public void SaveSettings(WindowsUpdateSettings value) => settings.Save(value);
        public WindowsUpdateState LoadState() => state.LoadOrNull() ?? new WindowsUpdateState();
        public void SaveState(WindowsUpdateState value) => state.Save(value);
    }
}
