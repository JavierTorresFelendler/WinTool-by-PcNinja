using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using WinTool.Cli;
using WinTool.Core;
using WinTool.Core.Diagnostics;
using WinTool.WindowsUpdate.Platform;

namespace WinTool.App.Infrastructure
{
    /// <summary>
    /// Command-line entry (decision D-016: one EXE for GUI and CLI). Phase 1
    /// has only version, help and self-test; feature commands come with
    /// their modules.
    /// </summary>
    internal static class CommandLine
    {
        private const int AttachParentProcess = -1;

        public static int Run(string[] args)
        {
            AttachParentConsole();
            var command = args[0].Trim().ToLowerInvariant();
            if (CommandRunner.Handles(command))
            {
                AppLog.Current.Info("CLI", "Command: " + string.Join(" ", args));
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                var exit = new CommandRunner(new WindowsUpdateEnvironment(exePath), Console.Out, Console.Error).Run(args);
                AppLog.Current.Info("CLI", "Exit code " + exit + ".");
                return exit;
            }

            switch (command)
            {
                case "--version":
                case "version":
                    Console.WriteLine(ProductInfo.Name + " " + ProductInfo.InformationalVersion);
                    return Program.ExitOk;
                case "--self-test":
                    return SelfTest.Run(Console.Out);
                case "--help":
                case "-h":
                case "/?":
                case "help":
                    WriteUsage(Console.Out);
                    return Program.ExitOk;
                default:
                    Console.Error.WriteLine("Unknown command: " + args[0]);
                    WriteUsage(Console.Error);
                    AppLog.Current.Warn("CLI", "Unknown command: " + args[0]);
                    return Program.ExitUsage;
            }
        }

        private static void WriteUsage(TextWriter writer)
        {
            writer.WriteLine(ProductInfo.Name + " " + ProductInfo.Version);
            writer.WriteLine();
            writer.WriteLine("Usage: WinTool-by-PcNinja.exe [command] [options]");
            writer.WriteLine("With no command, the application window opens. Add --json for machine-readable output.");
            writer.WriteLine();
            writer.WriteLine("Windows Update");
            writer.WriteLine("  update scan      [--windows] [--optional] [--drivers] [--firmware]");
            writer.WriteLine("  update run       [--scheduled|--retry|--startup|--wake] [--snooze] [scope options]   (admin)");
            writer.WriteLine("  update reset     --confirm                                                         (admin)");
            writer.WriteLine("  update health    [--no-network]");
            writer.WriteLine("  update start-as-system                                                             (admin)");
            writer.WriteLine();
            writer.WriteLine("Schedule (admin, except show)");
            writer.WriteLine("  schedule show");
            writer.WriteLine("  schedule set     [--enable|--disable] [--frequency Daily|Weekly|Monthly|Startup] [--time HH:mm]");
            writer.WriteLine("                   [--day Sunday..Saturday] [--month-day 1-28] [--at-startup|--no-at-startup]");
            writer.WriteLine("                   [--startup-delay MIN] [--run-if-missed|--no-run-if-missed] [--wake|--no-wake]");
            writer.WriteLine("                   [--reboot-prompt|--no-reboot-prompt] [--firmware|--no-firmware]");
            writer.WriteLine("                   [--auto-retry|--no-auto-retry] [--retry-delay MIN] [--retry-attempts N]");
            writer.WriteLine("                   [--retry-backoff N] [--cooldown MIN]");
            writer.WriteLine("  schedule remove");
            writer.WriteLine("  schedule import  <settings.json>");
            writer.WriteLine("  schedule import-v2   Take over the WinUpdate Tool V1/V2 schedule");
            writer.WriteLine();
            writer.WriteLine("Other");
            writer.WriteLine("  status           Version, last update run, schedule, pending restart");
            writer.WriteLine("  logs collect     [--output folder-or-file.zip]");
            writer.WriteLine("  version | help | --self-test");
            writer.WriteLine();
            writer.WriteLine("Exit codes: docs/CLI.md. In cmd.exe use \"start /wait\" to wait for the result.");
        }

        /// <summary>
        /// A WinExe has no console. Attach to the caller's console when there is
        /// one; when output is redirected the handles already work. Encoding is
        /// changed only when a console exists (WinUpdate V2.2.8 incident).
        /// </summary>
        private static void AttachParentConsole()
        {
            if (!AttachConsole(AttachParentProcess)) return;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.WriteLine();
            }
            catch (IOException)
            {
                // Output stays where the runtime put it.
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);
    }
}
