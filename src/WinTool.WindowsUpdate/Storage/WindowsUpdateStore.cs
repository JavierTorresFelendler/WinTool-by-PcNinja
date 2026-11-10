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

        public WindowsUpdateSettings LoadSettings() { Prepare(); return settings.LoadOrNull() ?? new WindowsUpdateSettings(); }
        public void SaveSettings(WindowsUpdateSettings value) { Prepare(); settings.Save(value); }
        public WindowsUpdateState LoadState() { Prepare(); return state.LoadOrNull() ?? new WindowsUpdateState(); }
        public void SaveState(WindowsUpdateState value) { Prepare(); state.Save(value); }

        private void Prepare() => WinTool.Core.WinToolPaths.EnsureMachineDataDirectory(Folder);
    }
}
