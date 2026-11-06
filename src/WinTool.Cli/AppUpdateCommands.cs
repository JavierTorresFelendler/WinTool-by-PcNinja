using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using WinTool.Core;
using WinTool.Core.Json;
using WinTool.Core.Updates;

namespace WinTool.Cli
{
    /// <summary>
    /// "app-update check|install": WinTool's own update from the command line (D-023).
    /// A portable copy replaces itself (D-018); an installed copy gets the verified MSI
    /// downloaded and the command to run it, because the running EXE is the file it replaces.
    /// </summary>
    public sealed class AppUpdateCommands
    {
        private readonly Func<AppUpdater> createUpdater;
        private readonly string exePath;
        private readonly TextWriter output;
        private readonly TextWriter error;

        public AppUpdateCommands(Func<AppUpdater> createUpdater, string exePath, TextWriter output, TextWriter error)
        {
            this.createUpdater = createUpdater;
            this.exePath = exePath;
            this.output = output;
            this.error = error;
        }

        public bool IsInstalled => exePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

        public int Run(string command, string[] rest)
        {
            switch (command)
            {
                case "check": return Check(Parse(rest, new[] { "test-versions", "json" }));
                case "install": return Install(Parse(rest, new[] { "test-versions", "yes", "json" }));
                default: throw new UsageException("Unknown command: app-update " + command + ". Use app-update check or app-update install --yes.");
            }
        }

        private static Arguments Parse(string[] rest, string[] switches)
        {
            var a = Arguments.Parse(rest, switches, null);
            a.NoPositional();
            return a;
        }

        private int Check(Arguments a)
        {
            ReleaseInfo release;
            var current = AppVersion.Current;
            try { release = createUpdater().CheckAsync(current, a.Has("test-versions"), CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return Fail(a, "The update check failed: " + ex.Message); }

            if (a.Has("json"))
            {
                Json(new
                {
                    Result = release == null ? "UpToDate" : "Available",
                    ExitCode = CliExit.Ok,
                    Current = current.ToString(),
                    Available = release?.Version.ToString(),
                    Page = release?.PageUrl,
                    Installed = IsInstalled
                });
            }
            else if (release == null) output.WriteLine(ProductInfo.Name + " " + current + " is up to date.");
            else output.WriteLine(ProductInfo.Name + " " + release.Version + " is available (you have " + current + "). " + release.PageUrl);
            return CliExit.Ok;
        }

        private int Install(Arguments a)
        {
            if (!a.Has("yes")) throw new UsageException("app-update install replaces this copy of WinTool. Add --yes to go ahead. Nothing was changed.");
            var updater = createUpdater();
            var current = AppVersion.Current;
            ReleaseInfo release;
            try { release = updater.CheckAsync(current, a.Has("test-versions"), CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return Fail(a, "The update check failed: " + ex.Message); }

            if (release == null)
            {
                if (a.Has("json")) Json(new { Result = "UpToDate", ExitCode = CliExit.Ok, Current = current.ToString() });
                else output.WriteLine(ProductInfo.Name + " " + current + " is up to date.");
                return CliExit.Ok;
            }

            try
            {
                if (IsInstalled)
                {
                    var msi = updater.DownloadVerifiedAsync(release, ReleaseFiles.Installer, null, CancellationToken.None).GetAwaiter().GetResult();
                    var command = "msiexec /i \"" + msi + "\" /qn /norestart";
                    if (a.Has("json")) Json(new { Result = "InstallerReady", ExitCode = CliExit.Ok, Version = release.Version.ToString(), Installer = msi, Command = command });
                    else output.WriteLine("Downloaded and verified " + msi + "\nRun it when WinTool is not running:\n  " + command);
                    return CliExit.Ok;
                }

                var exe = updater.DownloadVerifiedAsync(release, ReleaseFiles.Executable, null, CancellationToken.None).GetAwaiter().GetResult();
                var archive = PortableSwap.Apply(exePath, exe, current.FileVersion);
                if (a.Has("json")) Json(new { Result = "Updated", ExitCode = CliExit.Ok, From = current.ToString(), To = release.Version.ToString(), PreviousCopy = archive });
                else output.WriteLine("Updated to " + release.Version + ". The previous copy is " + archive + ".");
                return CliExit.Ok;
            }
            catch (UpdateVerificationException ex) { return Fail(a, ex.Message); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Net.Http.HttpRequestException)
            {
                return Fail(a, "The update did not complete; WinTool was not changed. " + ex.Message);
            }
        }

        private int Fail(Arguments a, string message)
        {
            if (a.Has("json")) Json(new { Result = "Failed", ExitCode = CliExit.Failed, Message = message });
            else error.WriteLine(message);
            return CliExit.Failed;
        }

        private void Json(object value) => output.WriteLine(JsonText.Serialize(value));
    }
}
