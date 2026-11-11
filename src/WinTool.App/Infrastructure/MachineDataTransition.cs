using System;
using System.IO;
using WinTool.Core;
using WinTool.Core.IO;
using WinTool.Core.Security;
using WinTool.Core.Operations;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.App.Infrastructure
{
    internal static class MachineDataTransition
    {
        internal static bool IsReadOnly(string[] args)
        {
            var area = args[0].ToLowerInvariant(); var command = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            return area == "status" || area == "logs" || (area == "schedule" && command == "show") ||
                (area == "update" && (command == "scan" || command == "health")) ||
                (area == "upgrade" && (command == "status" || command == "scan" || command == "downloads")) ||
                (area == "office" && (command == "status" || command == "config" || command == "tool-check")) || (area == "app-update" && command == "check");
        }
        internal static void Initialize()
        {
            if (!Program.IsAdministrator() || WinToolPaths.DataRootOverride != null) return;
            MachineDataMigration.Run(WinToolPaths.MachineData, WinToolPaths.LegacyMachineData, new[]
            {
                new MigrationRule(Path.Combine("WindowsUpdate", "settings.json"), LegacyDataValidation.Settings),
                new MigrationRule(Path.Combine("WindowsUpdate", "state.json"), LegacyDataValidation.State),
                new MigrationRule(Path.Combine("Upgrade", "Last-Upgrade.json"), WinTool.Upgrade.UpgradeHistory.ValidateLegacyState)
            }, () => OperationGate.EnterWindows(), path => WinToolPaths.EnsureMachineDataDirectory(path),
                ProtectedMachineData.ReadProtectedFile, AtomicFile.WriteAllText);
        }
    }
}
