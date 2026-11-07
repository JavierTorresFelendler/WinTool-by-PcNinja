using System.Text;
using WinTool.Core.Diagnostics;
using WinTool.Core.Operations;

namespace WinTool.Office;

public enum OfficeStage
{
    FindingTool,
    DownloadingTool,
    CheckingTool,
    ExtractingTool,
    Installing,
    DownloadingOffice,
    Removing,
    WritingPackage,
    CleaningUp,
    Done
}

/// <summary>Progress: a percentage, a stage the window translates, and an English line for the log.</summary>
public sealed class OfficeProgress
{
    public OfficeProgress(int percent, OfficeStage stage, string message, long? downloadedBytes = null)
    {
        Percent = percent;
        Stage = stage;
        Message = message;
        DownloadedBytes = downloadedBytes;
    }

    public int Percent { get; }
    public OfficeStage Stage { get; }
    public string Message { get; }
    public long? DownloadedBytes { get; }
}

public enum OfficeFailure
{
    /// <summary>The ODT could not be downloaded or extracted.</summary>
    ToolUnavailable,

    /// <summary>The ODT or its setup.exe is not signed by Microsoft.</summary>
    ToolNotTrusted,

    /// <summary>The ODT's setup.exe returned a failure code.</summary>
    SetupFailed
}

public sealed class OfficeDeploymentException : Exception
{
    public OfficeDeploymentException(OfficeFailure failure, string message, int? setupExitCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        SetupExitCode = setupExitCode;
    }

    public OfficeFailure Failure { get; }
    public int? SetupExitCode { get; }
}

public sealed class OdtCheck
{
    public OdtCheck(OdtLink link, bool signedByMicrosoft, string signer, long bytes, IReadOnlyList<string> warnings)
    {
        Url = link.Url;
        Latest = link.Latest;
        SignedByMicrosoft = signedByMicrosoft;
        Signer = signer;
        Bytes = bytes;
        Warnings = warnings;
    }

    public string Url { get; }
    public bool Latest { get; }
    public bool SignedByMicrosoft { get; }
    public string Signer { get; }
    public long Bytes { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed class OfficeOutcome
{
    public OfficeOutcome(string message, string? packageFolder = null, string? toolUrl = null)
    {
        Message = message;
        PackageFolder = packageFolder;
        ToolUrl = toolUrl;
    }

    public string Message { get; }
    public string? PackageFolder { get; }
    public string? ToolUrl { get; }
}

/// <summary>
/// Install, offline package and remove with the Office Deployment Tool
/// (OFFICE-BEHAVIOR OF-20..OF-45). Runs under WinTool's operation gate; the
/// Office installer itself is never interrupted once it has started.
/// </summary>
public sealed class OfficeDeployment
{
    public const string PackageFolderName = "OFFICE-OFFLINE";
    private const string LogArea = "Office";

    private readonly IOfficeTools tools;
    private readonly Func<IDisposable?> acquireLock;
    private readonly string workRoot;

    public OfficeDeployment()
        : this(new OfficeTools(), () => OperationLock.TryAcquire("Office", TimeSpan.Zero), Path.GetTempPath())
    {
    }

    internal OfficeDeployment(IOfficeTools tools, Func<IDisposable?> acquireLock, string workRoot)
    {
        this.tools = tools;
        this.acquireLock = acquireLock;
        this.workRoot = workRoot;
    }

    public Task<OfficeOutcome> InstallAsync(OfficeRequest request, IProgress<OfficeProgress>? progress, CancellationToken token) =>
        RunAsync(progress, token, async (work, report) =>
        {
            var config = Path.Combine(work.Folder, "configuration.xml");
            WriteText(config, OfficeConfiguration.Build(request));
            report(52, OfficeStage.Installing, "Installing Office" + (request.Silent ? " silently (no Office window)" : "") + ". This can take 10-30 minutes.");
            // Once the Office installer runs it is never stopped: cancelling it can leave Office half installed.
            var run = tools.RunAsync(work.Setup, "/configure \"" + config + "\"", work.Folder, false, CancellationToken.None);
            await MonitorInstallAsync(run, report).ConfigureAwait(false);
            var exit = await run.ConfigureAwait(false);
            if (exit != 0) throw SetupFailed("Office setup", exit);
            return new OfficeOutcome("Office was installed.", null, work.ToolUrl);
        });

    public Task<OfficeOutcome> RemoveAllAsync(bool silent, IProgress<OfficeProgress>? progress, CancellationToken token) =>
        RunAsync(progress, token, async (work, report) =>
        {
            var config = Path.Combine(work.Folder, "remove.xml");
            WriteText(config, OfficeConfiguration.RemoveAll(silent));
            report(50, OfficeStage.Removing, "Removing Office. This can take several minutes.");
            var exit = await tools.RunAsync(work.Setup, "/configure \"" + config + "\"", work.Folder, false, CancellationToken.None).ConfigureAwait(false);
            if (exit != 0) throw SetupFailed("Office removal", exit);
            return new OfficeOutcome("Office was removed.", null, work.ToolUrl);
        });

    /// <summary>
    /// Downloads Office into <paramref name="folder"/>\OFFICE-OFFLINE (or the folder itself when it is
    /// already named so), with setup.exe, Office_Config.xml and Install-Office.bat beside the Office folder.
    /// </summary>
    public Task<OfficeOutcome> PackageAsync(OfficeRequest request, string folder, IProgress<OfficeProgress>? progress, CancellationToken token) =>
        RunAsync(progress, token, async (work, report) =>
        {
            var package = ResolvePackageFolder(Path.GetFullPath(folder));
            Directory.CreateDirectory(package);
            var download = Path.Combine(work.Folder, "download.xml");
            WriteText(download, OfficeConfiguration.Build(request, package));
            report(52, OfficeStage.DownloadingOffice, "Downloading Office. This can take 20-40 minutes.");
            var office = Path.Combine(package, "Office");
            var run = tools.RunAsync(work.Setup, "/download \"" + download + "\"", package, true, token);
            await MonitorDownloadAsync(run, office, report).ConfigureAwait(false);
            var exit = await run.ConfigureAwait(false);
            if (exit != 0) throw SetupFailed("The Office download", exit);
            if (!Directory.Exists(Path.Combine(office, "Data")))
                throw new OfficeDeploymentException(OfficeFailure.SetupFailed, "The Office download finished but created no Office\\Data folder.");

            report(94, OfficeStage.WritingPackage, "Writing setup.exe, Office_Config.xml and Install-Office.bat.");
            File.Copy(work.Setup, Path.Combine(package, "setup.exe"), true);
            // No SourcePath: setup.exe installs from the Office folder beside it, wherever the package is copied.
            WriteText(Path.Combine(package, "Office_Config.xml"), OfficeConfiguration.Build(request));
            File.WriteAllText(Path.Combine(package, "Install-Office.bat"), InstallScript, Encoding.ASCII);
            return new OfficeOutcome("Copy the whole \"" + package + "\" folder to the target computer and run Install-Office.bat as administrator.",
                package, work.ToolUrl);
        });

    /// <summary>
    /// Finds the ODT, downloads it and checks the Microsoft signature, then deletes it.
    /// Runs nothing and changes nothing (diagnostics, CI).
    /// </summary>
    public async Task<OdtCheck> CheckToolAsync(CancellationToken token)
    {
        var folder = Path.Combine(workRoot, "WinTool-Office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var warnings = new List<string>();
            var link = await OdtSource.ResolveAsync(tools, warnings.Add, token).ConfigureAwait(false);
            var file = Path.Combine(folder, "officedeploymenttool.exe");
            await tools.DownloadFileAsync(link.Url, file, token).ConfigureAwait(false);
            var signed = tools.IsMicrosoftSigned(file, out var signer);
            return new OdtCheck(link, signed, signer, new FileInfo(file).Length, warnings);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    internal static string ResolvePackageFolder(string selected)
    {
        var trimmed = selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(trimmed), PackageFolderName, StringComparison.OrdinalIgnoreCase)
            ? selected
            : Path.Combine(selected, PackageFolderName);
    }

    internal const string InstallScript =
        "@echo off\r\n" +
        "setlocal\r\n" +
        "net session >nul 2>&1\r\n" +
        "if %errorLevel% neq 0 (\r\n" +
        "    echo.\r\n" +
        "    echo  ERROR: Must be run as Administrator.\r\n" +
        "    echo  Right-click Install-Office.bat and choose Run as administrator.\r\n" +
        "    echo.\r\n" +
        "    pause\r\n" +
        "    exit /b 1\r\n" +
        ")\r\n" +
        "echo  WinTool by PcNinja - Office offline installation\r\n" +
        "echo.\r\n" +
        "cd /d \"%~dp0\"\r\n" +
        "echo  Starting installation, please wait...\r\n" +
        "setup.exe /configure Office_Config.xml\r\n" +
        "set \"setupExitCode=%errorlevel%\"\r\n" +
        "echo.\r\n" +
        "if \"%setupExitCode%\"==\"0\" (\r\n" +
        "    echo  Office installation succeeded.\r\n" +
        ") else (\r\n" +
        "    echo  ERROR: Office installation failed with exit code %setupExitCode%.\r\n" +
        ")\r\n" +
        "echo  Press any key to exit.\r\n" +
        "pause > nul\r\n" +
        "exit /b %setupExitCode%\r\n";

    // ---------------------------------------------------------------- shared flow

    /// <summary>A progress line; bytes only for the running Office download size (not written to the log each second).</summary>
    private delegate void Reporter(int percent, OfficeStage stage, string message, long? bytes = null);

    private sealed class Work
    {
        public Work(string folder) { Folder = folder; }
        public string Folder { get; }
        public string Setup => Path.Combine(Folder, "odt", "setup.exe");
        public string ToolUrl { get; set; } = string.Empty;
    }

    private async Task<OfficeOutcome> RunAsync(IProgress<OfficeProgress>? progress, CancellationToken token,
        Func<Work, Reporter, Task<OfficeOutcome>> body)
    {
        void Report(int percent, OfficeStage stage, string message, long? bytes = null)
        {
            if (bytes == null) AppLog.Current.Info(LogArea, message);
            progress?.Report(new OfficeProgress(percent, stage, message, bytes));
        }

        using (var gate = acquireLock())
        {
            if (gate == null) throw new OperationBusyException();
            var work = new Work(Path.Combine(workRoot, "WinTool-Office-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(work.Folder);
            try
            {
                await PrepareToolAsync(work, Report, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var outcome = await body(work, Report).ConfigureAwait(false);
                Report(100, OfficeStage.Done, outcome.Message);
                return outcome;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                AppLog.Current.Error(LogArea, "Office operation failed.", ex);
                throw;
            }
            finally
            {
                // Always removed, also after a failure (the original kept it then). ODT's own logs stay in %TEMP%.
                TryDeleteFolder(work.Folder);
            }
        }
    }

    private async Task PrepareToolAsync(Work work, Reporter report, CancellationToken token)
    {
        report(3, OfficeStage.FindingTool, "Finding the latest Office Deployment Tool.");
        var link = await OdtSource.ResolveAsync(tools, message => AppLog.Current.Warn(LogArea, message), token).ConfigureAwait(false);
        work.ToolUrl = link.Url;
        var package = Path.Combine(work.Folder, "officedeploymenttool.exe");
        report(8, OfficeStage.DownloadingTool, "Downloading the Office Deployment Tool: " + link.Url);
        try
        {
            await tools.DownloadFileAsync(link.Url, package, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            throw new OfficeDeploymentException(OfficeFailure.ToolUnavailable, "The Office Deployment Tool could not be downloaded: " + ex.Message, null, ex);
        }

        report(25, OfficeStage.CheckingTool, "Checking the Microsoft signature.");
        RequireMicrosoft(package);
        report(30, OfficeStage.ExtractingTool, "Extracting the Office Deployment Tool.");
        var extract = Path.Combine(work.Folder, "odt");
        Directory.CreateDirectory(extract);
        var exit = await tools.RunAsync(package, "/quiet /extract:\"" + extract + "\"", work.Folder, true, token).ConfigureAwait(false);
        if (exit != 0 || !File.Exists(work.Setup))
            throw new OfficeDeploymentException(OfficeFailure.ToolUnavailable, "The Office Deployment Tool could not be extracted (exit code " + exit + ").");
        RequireMicrosoft(work.Setup);
    }

    private void RequireMicrosoft(string path)
    {
        if (!tools.IsMicrosoftSigned(path, out var signer))
            throw new OfficeDeploymentException(OfficeFailure.ToolNotTrusted,
                Path.GetFileName(path) + " is not signed by Microsoft (signer: " + signer + "). Nothing was run.");
        AppLog.Current.Info(LogArea, Path.GetFileName(path) + " signed by " + signer);
    }

    /// <summary>The original tool's download meter: folder size every second, a report at least every 10 seconds.</summary>
    /// <summary>How often a running Office installation writes a progress line (tests shorten it).</summary>
    internal TimeSpan InstallHeartbeat { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Bytes in the Microsoft Office folders, which Click-to-Run fills while it installs (tests replace it).</summary>
    internal Func<long> OfficeFolderBytes { get; set; } = () =>
        FolderSize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Office")) +
        (Environment.Is64BitOperatingSystem
            ? FolderSize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Office"))
            : 0);

    /// <summary>
    /// Office setup gives no progress of its own (none at all when silent), so while it runs a line
    /// goes to the window and the WinTool log every <see cref="InstallHeartbeat"/>: elapsed time and
    /// how much the Microsoft Office folders grew since the start.
    /// </summary>
    private async Task MonitorInstallAsync(Task run, Reporter report)
    {
        var started = DateTime.UtcNow;
        var startBytes = OfficeFolderBytes();
        var percent = 52;
        while (await Task.WhenAny(run, Task.Delay(InstallHeartbeat)).ConfigureAwait(false) != run)
        {
            var elapsed = DateTime.UtcNow - started;
            var bytes = OfficeFolderBytes();
            percent = Math.Min(88, Math.Max(percent, 52 + (int)Math.Floor(elapsed.TotalMinutes * 1.5)));
            report(percent, OfficeStage.Installing, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Office setup is running ({0}:{1:00}); Microsoft Office folder {2:N0} MB ({3:+#,0;-#,0;0} MB since the start).",
                (int)elapsed.TotalMinutes, elapsed.Seconds, bytes / 1048576.0, (bytes - startBytes) / 1048576.0));
        }

        report(90, OfficeStage.Installing, string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "Office setup finished after {0:N0} minute(s).", (DateTime.UtcNow - started).TotalMinutes));
    }

    private static async Task MonitorDownloadAsync(Task run, string officeFolder, Reporter report)
    {
        var started = DateTime.UtcNow;
        var lastReport = DateTime.MinValue;
        long lastBytes = 0;
        var percent = 52;
        while (await Task.WhenAny(run, Task.Delay(1000)).ConfigureAwait(false) != run)
        {
            var bytes = FolderSize(officeFolder);
            var now = DateTime.UtcNow;
            if (bytes <= lastBytes && (now - lastReport).TotalSeconds < 10) continue;
            var minutes = Math.Max(0.1, (now - started).TotalMinutes);
            var byTime = 52 + Math.Min(36, (int)Math.Floor(minutes * 1.5));
            if (bytes > lastBytes) byTime = Math.Max(byTime, percent + 1);
            percent = Math.Min(88, Math.Max(percent, byTime));
            report(percent, OfficeStage.DownloadingOffice, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Downloading Office... {0:N0} MB", bytes / 1048576.0), bytes);
            lastReport = now;
            lastBytes = bytes;
        }
    }

    private static long FolderSize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            return total;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static OfficeDeploymentException SetupFailed(string what, int exit) =>
        new OfficeDeploymentException(OfficeFailure.SetupFailed,
            what + " failed with exit code " + exit + " (0x" + unchecked((uint)exit).ToString("X8") + "). See the Office logs in %TEMP%.", exit);

    private static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException ex) { AppLog.Current.Warn(LogArea, "Could not remove " + path + ": " + ex.Message); }
        catch (UnauthorizedAccessException ex) { AppLog.Current.Warn(LogArea, "Could not remove " + path + ": " + ex.Message); }
    }
}
