using WinTool.Core.Json;
using Microsoft.Win32;

namespace WinTool.Upgrade;

public sealed record UpgradeAttempt(DateTimeOffset StartedUtc, string SourceRoot, string ImagePath,
    int ImageIndex, int OriginalBuild, int TargetBuild, string TargetEdition, int? ProcessExitCode,
    string State, string Message)
{
    public SetupFailureEvidence? SetupFailure { get; init; }
}

public static class UpgradeHistory
{
    private static string StatePath => Path.Combine(AppLog.Folder, "Last-Upgrade.json");

    internal static UpgradeAttempt Begin(MountedSource source, InstallationImage image)
    {
        var attempt = new UpgradeAttempt(DateTimeOffset.UtcNow, source.Root, source.ImagePath!, image.Index,
            CurrentWindows.Read().Build, Version.Parse(image.Version).Build, image.Edition, null,
            "SetupStarting", "Setup launch recorded; the final upgrade outcome is not yet known.");
        Save(attempt);
        return attempt;
    }

    internal static void RecordExit(UpgradeAttempt attempt, int exitCode)
    {
        var state = exitCode == 0 ? "PreparationCompleted" : "OutcomeUnconfirmed";
        var message = exitCode == 0
            ? "Setup completed its current stage. The final outcome requires post-restart verification."
            : $"Setup process returned 0x{unchecked((uint)exitCode):X8}. Check Windows Setup logs; no restart was forced.";
        try { Save(attempt with { ProcessExitCode = exitCode, State = state, Message = message }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Error($"Could not record Setup exit status: {ex}"); }
    }

    public static UpgradeAttempt? ReadStatus()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var attempt = JsonData.Deserialize<UpgradeAttempt>(File.ReadAllText(StatePath));
            if (attempt is null) return null;
            using var setup = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup");
            var current = CurrentWindows.Read();
            var inProgress = Convert.ToInt32(setup?.GetValue("SystemSetupInProgress") ?? 0) != 0;
            var failure = !inProgress && current.Build == attempt.OriginalBuild
                ? SetupOutcomeDiagnostics.Read(attempt, Path.GetPathRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows))!) : null;
            return Reconcile(attempt, current, inProgress, failure);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        { AppLog.Error($"Cannot read previous upgrade status: {ex.Message}"); return null; }
    }

    internal static UpgradeAttempt Reconcile(UpgradeAttempt attempt, CurrentWindows current, bool setupInProgress,
        SetupFailureEvidence? failure = null) =>
        !setupInProgress && current.Build == attempt.OriginalBuild && failure is not null
            ? attempt with { State = "RollbackObserved", SetupFailure = failure,
                Message = $"{failure.Summary} {failure.ResultCode} - {failure.ExtendedCode}; phase: {failure.Phase}. See View log." }
            :
        // Windows 10's shared-base image metadata is not a verified feature-release
        // target in this POC. Keep manual outcome verification instead of a false match.
        !setupInProgress && attempt.TargetBuild >= 22000 && current.Build != attempt.OriginalBuild && current.Build == attempt.TargetBuild &&
        current.Edition.Equals(attempt.TargetEdition, StringComparison.OrdinalIgnoreCase)
            ? attempt with { State = "TargetWindowsObserved", Message = "The target Windows build and edition are now running. Verify that your files and applications are intact." }
            : attempt;

    private static void Save(UpgradeAttempt attempt)
    {
        Directory.CreateDirectory(AppLog.Folder);
        WinTool.Core.IO.AtomicFile.WriteAllText(StatePath, JsonData.Serialize(attempt));
        AppLog.Info($"Upgrade state: {attempt.State}. Target build: {attempt.TargetBuild}; image: {attempt.ImageIndex}.");
    }
}
