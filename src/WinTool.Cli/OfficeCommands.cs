using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WinTool.Core.Diagnostics;
using WinTool.Core.Json;
using WinTool.Core.Machine;
using WinTool.Core.Operations;
using WinTool.Office;

namespace WinTool.Cli
{
    /// <summary>
    /// "office status|config|install|package|remove" (D-022, OFFICE-BEHAVIOR):
    /// the same library as the Office screen.
    /// </summary>
    public sealed class OfficeCommands
    {
        private static readonly string[] RequestSwitches = { "visio", "project", "x86", "no-activate", "remove-existing", "json" };
        private static readonly string[] RequestValues = { "family", "channel", "apps", "languages" };

        private readonly Func<OfficeDeployment> createDeployment;
        private readonly Func<bool> isAdministrator;
        private readonly TextWriter output;
        private readonly TextWriter error;

        public OfficeCommands(Func<OfficeDeployment> createDeployment, Func<bool> isAdministrator, TextWriter output, TextWriter error)
        {
            this.createDeployment = createDeployment;
            this.isAdministrator = isAdministrator;
            this.output = output;
            this.error = error;
        }

        public int Run(string command, string[] rest)
        {
            switch (command)
            {
                case "status": return Status(Parse(rest, new[] { "json" }, null));
                case "config": return Config(Parse(rest, RequestSwitches.Concat(new[] { "quiet" }), RequestValues));
                case "install": return Install(Parse(rest, RequestSwitches.Concat(new[] { "quiet", "confirm" }), RequestValues));
                case "package": return Package(Parse(rest, RequestSwitches, RequestValues.Concat(new[] { "output" })));
                case "remove": return Remove(Parse(rest, new[] { "confirm", "quiet", "json" }, null));
                case "tool-check": return ToolCheck(Parse(rest, new[] { "json" }, null));
                default:
                    throw new UsageException("Unknown command: office " + command + ". Run \"WinTool-by-PcNinja.exe help\".");
            }
        }

        private static Arguments Parse(string[] rest, IEnumerable<string> switches, IEnumerable<string> values)
        {
            var a = Arguments.Parse(rest, switches, values);
            a.NoPositional();
            return a;
        }

        // ---------------------------------------------------------------- read-only

        private int Status(Arguments a)
        {
            OfficeInfo office;
            try { office = OfficeInfo.ReadCurrent(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { office = null; AppLog.Current.Warn("Office", "Reading installed Office failed: " + ex.Message); }

            if (a.Has("json"))
            {
                Json(new
                {
                    Result = office == null ? "NotInstalled" : "Installed",
                    ExitCode = CliExit.Ok,
                    Installed = office == null ? null : new
                    {
                        office.ProductName, office.ProductIds, office.Version, office.Platform, office.Channel,
                        office.IsVolumeLicense, office.OutOfSupport, office.Apps
                    }
                });
            }
            else if (office == null)
            {
                output.WriteLine("No Click-to-Run Office is installed.");
            }
            else
            {
                output.WriteLine("Office: " + office.ProductName + "   Version: " + office.Version + "   " + office.Platform);
                output.WriteLine("Channel: " + (office.Channel ?? (office.IsVolumeLicense ? "volume license" : "unknown")) + (office.OutOfSupport ? "   (out of support)" : string.Empty));
                output.WriteLine("Apps: " + string.Join(", ", office.Apps));
            }

            return CliExit.Ok;
        }

        private int Config(Arguments a)
        {
            var request = Request(a);
            request.Silent = a.Has("quiet");
            var xml = OfficeConfiguration.Build(request);
            if (a.Has("json")) Json(new { Result = "Configuration", ExitCode = CliExit.Ok, Configuration = xml });
            else output.Write(xml);
            return CliExit.Ok;
        }

        /// <summary>Downloads the ODT and checks its signature; runs nothing. 0 = signed and found on Microsoft's page.</summary>
        private int ToolCheck(Arguments a)
        {
            OdtCheck check;
            try
            {
                check = createDeployment().CheckToolAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Failure(a.Has("json"), "ToolUnavailable", CliExit.OfficeToolUnavailable, "The Office Deployment Tool could not be downloaded: " + ex.Message, null);
            }

            var exit = !check.SignedByMicrosoft ? CliExit.OfficeToolNotTrusted : check.Latest ? CliExit.Ok : CliExit.Warning;
            if (a.Has("json"))
            {
                Json(new { Result = exit == CliExit.Ok ? "Verified" : exit == CliExit.Warning ? "VerifiedFallback" : "ToolNotTrusted",
                    ExitCode = exit, check.Url, check.Latest, check.SignedByMicrosoft, check.Signer, check.Bytes, check.Warnings });
            }
            else
            {
                output.WriteLine("Office Deployment Tool: " + check.Url);
                output.WriteLine(check.Latest ? "Found on Microsoft's download page." : "Microsoft's download page gave no link; this is WinTool's fixed address.");
                foreach (var warning in check.Warnings) output.WriteLine("  " + warning);
                output.WriteLine("Size: " + check.Bytes + " bytes   Signer: " + check.Signer + "   Signed by Microsoft: " + (check.SignedByMicrosoft ? "yes" : "NO"));
            }

            return exit;
        }

        // ---------------------------------------------------------------- changing the computer

        private int Install(Arguments a)
        {
            var request = Request(a);
            request.Silent = a.Has("quiet");
            if (request.RemoveExisting && !a.Has("confirm"))
                throw new UsageException("--remove-existing removes every installed Office first. Add --confirm to go ahead. Nothing was changed.");
            return Execute(a, (deployment, progress, token) => deployment.InstallAsync(request, progress, token));
        }

        private int Package(Arguments a)
        {
            var folder = a.Value("output");
            if (string.IsNullOrWhiteSpace(folder)) throw new UsageException("office package needs --output <folder>. The package goes into <folder>\\OFFICE-OFFLINE.");
            var request = Request(a);
            if (request.RemoveExisting)
                throw new UsageException("--remove-existing belongs to installation, not to a package. Nothing was changed.");
            return Execute(a, (deployment, progress, token) => deployment.PackageAsync(request, folder, progress, token));
        }

        private int Remove(Arguments a)
        {
            if (!a.Has("confirm"))
                throw new UsageException("office remove removes every installed Click-to-Run Office product. Add --confirm to go ahead. Nothing was changed.");
            return Execute(a, (deployment, progress, token) => deployment.RemoveAllAsync(a.Has("quiet"), progress, token));
        }

        private int Execute(Arguments a, Func<OfficeDeployment, IProgress<OfficeProgress>, CancellationToken, System.Threading.Tasks.Task<OfficeOutcome>> work)
        {
            var json = a.Has("json");
            if (!isAdministrator())
            {
                const string message = "Run this command from an elevated Command Prompt or PowerShell window.";
                if (json) Json(new { Result = "AdministratorRequired", ExitCode = CliExit.AdministratorRequired, Message = message });
                else error.WriteLine(message);
                return CliExit.AdministratorRequired;
            }

            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler onCancel = (sender, e) =>
                {
                    e.Cancel = true;
                    cancellation.Cancel();
                };
                Console.CancelKeyPress += onCancel;
                try
                {
                    var progress = json ? null : new ConsoleProgress(error);
                    var outcome = work(createDeployment(), progress, cancellation.Token).GetAwaiter().GetResult();
                    if (json) Json(new { Result = "Succeeded", ExitCode = CliExit.Ok, outcome.Message, outcome.PackageFolder, outcome.ToolUrl });
                    else output.WriteLine(outcome.Message);
                    return CliExit.Ok;
                }
                catch (OperationBusyException ex)
                {
                    return Failure(json, "AlreadyRunning", CliExit.Busy, ex.Message, null);
                }
                catch (OperationCanceledException)
                {
                    return Failure(json, "Cancelled", CliExit.Cancelled, "Cancelled. Office setup was not started or was not interrupted.", null);
                }
                catch (OfficeDeploymentException ex)
                {
                    switch (ex.Failure)
                    {
                        case OfficeFailure.ToolNotTrusted:
                            return Failure(json, "ToolNotTrusted", CliExit.OfficeToolNotTrusted, ex.Message, null);
                        case OfficeFailure.SetupFailed:
                            return Failure(json, "SetupFailed", CliExit.OfficeSetupFailed, ex.Message, ex.SetupExitCode);
                        default:
                            return Failure(json, "ToolUnavailable", CliExit.OfficeToolUnavailable, ex.Message, null);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    return Failure(json, "Failed", CliExit.Failed, ex.Message, null);
                }
                finally
                {
                    Console.CancelKeyPress -= onCancel;
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The request from --family, --channel, --apps, --languages and the switches. Invalid values are usage errors.</summary>
        internal static OfficeRequest Request(Arguments a)
        {
            var request = new OfficeRequest();
            var family = a.Value("family");
            if (family != null)
            {
                switch (family.ToLowerInvariant())
                {
                    case "m365":
                    case "microsoft365":
                        request.Family = OfficeFamily.Microsoft365;
                        break;
                    case "ltsc":
                    case "ltsc2024":
                        request.Family = OfficeFamily.Ltsc2024;
                        break;
                    default:
                        throw new UsageException("--family must be m365 or ltsc.");
                }
            }

            if (a.Value("channel") != null)
            {
                if (request.Family == OfficeFamily.Ltsc2024) throw new UsageException("--channel applies to Microsoft 365 only; Office LTSC 2024 has its own channel.");
                request.Channel = a.Value("channel");
            }

            if (a.Value("apps") != null) request.Apps = List(a.Value("apps"));
            if (a.Value("languages") != null) request.Languages = List(a.Value("languages"));
            request.Visio = a.Has("visio");
            request.Project = a.Has("project");
            request.Use32Bit = a.Has("x86");
            if (a.Has("no-activate"))
            {
                if (request.Family != OfficeFamily.Ltsc2024) throw new UsageException("--no-activate applies to Office LTSC 2024 only.");
                request.AutoActivate = false;
            }

            request.RemoveExisting = a.Has("remove-existing");
            var errors = request.Validate();
            if (errors.Count > 0) throw new UsageException(string.Join(" ", errors));
            return request;
        }

        private static List<string> List(string text) =>
            text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).ToList();

        private int Failure(bool json, string result, int exit, string message, int? setupExitCode)
        {
            if (json) Json(new { Result = result, ExitCode = exit, Message = message, SetupExitCode = setupExitCode });
            else error.WriteLine(message);
            return exit;
        }

        private void Json(object value) => output.WriteLine(JsonText.Serialize(value));

        private sealed class ConsoleProgress : IProgress<OfficeProgress>
        {
            private readonly TextWriter writer;
            private OfficeStage? last;

            public ConsoleProgress(TextWriter writer)
            {
                this.writer = writer;
            }

            public void Report(OfficeProgress value)
            {
                // One line per stage, plus the running size while Office downloads.
                if (value.Stage == last && value.Stage != OfficeStage.DownloadingOffice) return;
                last = value.Stage;
                writer.WriteLine("[" + value.Percent.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3) + "%] " + value.Message);
            }
        }
    }
}
