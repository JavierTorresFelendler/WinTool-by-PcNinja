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
            var text = WinTool.Core.Security.ProtectedMachineData.IsMachinePath(StatePath)
                ? WinTool.Core.Security.ProtectedMachineData.ReadProtectedFile(StatePath) : File.ReadAllText(StatePath);
            var attempt = JsonData.Deserialize<UpgradeAttempt>(text);
            if (attempt is null) return null;
            if (attempt.State == "LegacyStateUnconfirmed") return attempt;
            using var setup = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup");
            var current = CurrentWindows.Read();
            var inProgress = Convert.ToInt32(setup?.GetValue("SystemSetupInProgress") ?? 0) != 0;
            var failure = !inProgress && current.Build == attempt.OriginalBuild
                ? SetupOutcomeDiagnostics.Read(attempt, Path.GetPathRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows))!) : null;
            return Reconcile(attempt, current, inProgress, failure);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or WinTool.Core.Operations.OperationBusyException)
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

    public static string ValidateLegacyState(string json)
    {
        var value = JsonData.Deserialize<UpgradeAttempt>(json);
        if (value is null || value.ImageIndex < 1 || value.ImageIndex > 65535 || value.OriginalBuild < 1 || value.TargetBuild < 1 ||
            value.StartedUtc.Year < 2000 || value.StartedUtc > DateTimeOffset.UtcNow.AddDays(1)) throw new FormatException("Invalid legacy upgrade state.");
        foreach (var text in new[] { value.SourceRoot, value.ImagePath, value.TargetEdition })
            if (text is null || text.Length > 1024 || text.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new FormatException("Invalid legacy upgrade metadata.");
        return JsonData.Serialize(value with { State = "LegacyStateUnconfirmed", ProcessExitCode = null, SetupFailure = null,
            Message = "Legacy history imported; Setup and final upgrade outcomes require independent verification." });
    }
}
