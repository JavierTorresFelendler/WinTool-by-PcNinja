using WinTool.Core.Diagnostics;

namespace WinTool.Upgrade;

/// <summary>
/// The ported Windows Upgrade code logs through this class (it was AppLog in the
/// standalone tool). Lines go to the WinTool log with the "Upgrade" area; upgrade
/// state files live in %ProgramData%\PcNinja\WinTool\Upgrade.
/// </summary>
public static class AppLog
{
    public static string Folder => WinTool.Core.WinToolPaths.EnsureMachineDataDirectory(System.IO.Path.Combine(WinTool.Core.WinToolPaths.MachineData, "Upgrade"));

    public static string Path => WinTool.Core.Diagnostics.AppLog.Current.CurrentFilePath;

    public static void Info(string message) => WinTool.Core.Diagnostics.AppLog.Current.Info("Upgrade", message);

    public static void Error(string message) => WinTool.Core.Diagnostics.AppLog.Current.Error("Upgrade", message);
}

/// <summary>Replaces the standalone tool's AppUpdateIdentity for the HTTP user agent.</summary>
internal static class AppUpdateIdentity
{
    public static string Version => WinTool.Core.ProductInfo.Version;
}

/// <summary>Windows preparation and Setup take WinTool's machine-wide gate (it was AppOperationGate).</summary>
internal static class AppOperationGate
{
    public static WinTool.Core.Operations.OperationLease EnterWindows() => WinTool.Core.Operations.OperationGate.EnterWindows();
}
