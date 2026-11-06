using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WinTool.Core;
using WinTool.Core.Diagnostics;

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
            writer.WriteLine("Usage: WinTool.exe [command]");
            writer.WriteLine();
            writer.WriteLine("  (no command)   Open the application window");
            writer.WriteLine("  version        Show the version");
            writer.WriteLine("  --self-test    Check that the application files are complete (exit code 0 = OK)");
            writer.WriteLine("  help           Show this help");
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
