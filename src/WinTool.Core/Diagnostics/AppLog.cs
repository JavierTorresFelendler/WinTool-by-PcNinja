using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WinTool.Core.Diagnostics
{
    /// <summary>
    /// Daily UTF-8 log files. Logging never throws: a failure to write a log
    /// line must not stop the operation that is being logged.
    /// </summary>
    public sealed class AppLog
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static AppLog current;

        private readonly object gate = new object();
        private readonly Func<DateTime> clock;

        public AppLog(string directory, Func<DateTime> clock = null)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            this.clock = clock ?? (() => DateTime.Now);
        }

        public string Directory { get; }

        /// <summary>The process-wide log, under %ProgramData%\PcNinja-WinTool\Logs by default.</summary>
        public static AppLog Current
        {
            get { return current ?? (current = CreateDefault()); }
            set { current = value; }
        }

        private static AppLog CreateDefault()
        {
            return CreateDefault(DefaultDirectory, Path.Combine(WinToolPaths.UserData, "Logs"), () =>
            {
                if (WinToolPaths.DataRootOverride == null)
                {
                    using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                            throw new UnauthorizedAccessException("Machine logs are writable only by administrators and SYSTEM.");
                }
                WinToolPaths.EnsureMachineDataDirectory(DefaultDirectory);
            });
        }

        internal static AppLog CreateDefault(string machineDirectory, string userDirectory, Action prepareMachine)
        {
            try
            {
                prepareMachine();
                return new AppLog(machineDirectory);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is Operations.OperationBusyException)
            {
                var fallback = new AppLog(userDirectory);
                fallback.Warn("Storage", "Machine logging is unavailable; using the per-user log. " + ex.GetType().Name);
                return fallback;
            }
        }

        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(WinToolPaths.MachineData, "Logs");
            }
        }

        public string CurrentFilePath
        {
            get { return Path.Combine(Directory, "WinTool-" + clock().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"); }
        }

        public void Info(string area, string message) { Write("INFO", area, message); }

        public void Warn(string area, string message) { Write("WARN", area, message); }

        public void Error(string area, string message, Exception exception = null)
        {
            Write("ERROR", area, exception == null ? message : message + " | " + exception.GetType().Name + ": " + exception.Message);
        }

        internal static string FormatLine(DateTime time, string level, string area, string message)
        {
            var oneLine = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}  {1,-5}  {2,-14} {3}",
                time, level, area, oneLine);
        }

        private void Write(string level, string area, string message)
        {
            try
            {
                var line = FormatLine(clock(), level, area, message) + Environment.NewLine;
                lock (gate)
                {
                    WinToolPaths.EnsureMachineDataDirectory(Directory);
                    if (Security.ProtectedMachineData.IsMachinePath(CurrentFilePath))
                    {
                        using (var stream = Security.ProtectedMachineData.OpenWriter(CurrentFilePath, true))
                        { var bytes = Utf8NoBom.GetBytes(line); stream.Write(bytes, 0, bytes.Length); }
                    }
                    else File.AppendAllText(CurrentFilePath, line, Utf8NoBom);
                }
            }
            catch (Exception)
            {
                // Logging is best effort by design.
            }
        }
    }
}
