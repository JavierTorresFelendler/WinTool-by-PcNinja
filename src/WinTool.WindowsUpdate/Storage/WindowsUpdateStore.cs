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

    /// <summary>%ProgramData%\PcNinja-WinTool\WindowsUpdate\settings.json and state.json (WU-80, D-028).</summary>
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

        public WindowsUpdateSettings LoadSettings() { return Read(settings) ?? new WindowsUpdateSettings(); }
        public void SaveSettings(WindowsUpdateSettings value) { Prepare(); settings.Save(value); }
        public WindowsUpdateState LoadState() { return Read(state) ?? new WindowsUpdateState(); }
        public void SaveState(WindowsUpdateState value) { Prepare(); state.Save(value); }

        private void Prepare() => WinTool.Core.WinToolPaths.EnsureMachineDataDirectory(Folder);

        private T Read<T>(JsonFileStore<T> store) where T : class
        {
            try
            {
                Prepare();
                if (WinTool.Core.Security.ProtectedMachineData.IsMachinePath(store.Path) && File.Exists(store.Path))
                    return JsonFileStore<T>.Deserialize(WinTool.Core.Security.ProtectedMachineData.ReadProtectedFile(store.Path));
                return store.LoadOrNull();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is WinTool.Core.Operations.OperationBusyException || ex is System.Runtime.Serialization.SerializationException || ex is FormatException)
            { WinTool.Core.Diagnostics.AppLog.Current.Warn("Storage", "Protected data unavailable; read-only defaults used. " + ex.GetType().Name); return null; }
        }
    }
}
