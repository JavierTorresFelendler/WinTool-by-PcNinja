using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WinTool.WindowsUpdate.Logic
{
    public sealed class FileRenameOperation
    {
        public FileRenameOperation(string source, string destination, bool blocksUpdates)
        {
            Source = source;
            Destination = destination;
            BlocksUpdates = blocksUpdates;
        }

        public string Source { get; }
        public string Destination { get; }
        public bool IsDelete => string.IsNullOrEmpty(Destination);
        public bool BlocksUpdates { get; }
    }

    public sealed class PendingRestartState
    {
        public PendingRestartState(IReadOnlyList<string> reasons, IReadOnlyList<string> warnings, IReadOnlyList<FileRenameOperation> renames)
        {
            Reasons = reasons;
            Warnings = warnings;
            FileRenames = renames;
        }

        public bool Pending => Reasons.Count > 0;
        public IReadOnlyList<string> Reasons { get; }
        public IReadOnlyList<string> Warnings { get; }
        public IReadOnlyList<FileRenameOperation> FileRenames { get; }

        public static PendingRestartState None { get; } = new PendingRestartState(new string[0], new string[0], new FileRenameOperation[0]);
    }

    /// <summary>The registry facts the pending-restart rules read.</summary>
    public sealed class PendingRestartFacts
    {
        public bool ComponentServicingRebootPending { get; set; }
        public bool WindowsUpdateRebootRequired { get; set; }
        public string[] PendingFileRenameOperations { get; set; }
        public string ComputerName { get; set; }
        public string ActiveComputerName { get; set; }
        public string SystemRoot { get; set; } = @"C:\Windows";
    }

    /// <summary>WU-40, WU-41, WU-42.</summary>
    public static class PendingRestartEvaluator
    {
        public const string CbsReason = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
        public const string WuReason = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
        public const string RenameReason = "Pending file rename operations that affect Windows servicing or drivers";
        public const string ComputerRenameReason = "Pending computer rename";

        // Only renames that touch servicing or drivers block an update run. Many programs leave harmless entries.
        private static readonly string[] BlockingPatterns =
        {
            @"\\windows\\winsxs\\", @"\\windows\\servicing\\", @"\\windows\\softwaredistribution\\",
            @"\\windows\\system32\\catroot", @"\\windows\\system32\\drivers\\", @"\\windows\\system32\\driverstore\\",
            @"\\windows\\inf\\", @"pending\.xml", @"\\windows\\system32\\wu", @"\\windows\\system32\\uso", @"\\windows\\system32\\mus"
        };

        public static PendingRestartState Evaluate(PendingRestartFacts facts)
        {
            var reasons = new List<string>();
            var warnings = new List<string>();
            if (facts.ComponentServicingRebootPending) reasons.Add(CbsReason);
            if (facts.WindowsUpdateRebootRequired) reasons.Add(WuReason);

            var renames = ParseRenames(facts.PendingFileRenameOperations, facts.SystemRoot);
            if (renames.Any(r => r.BlocksUpdates)) reasons.Add(RenameReason);
            else if (renames.Count > 0) warnings.Add("Other pending file rename operations exist, but none affect Windows Update, servicing or drivers.");

            if (!string.IsNullOrEmpty(facts.ComputerName) && !string.IsNullOrEmpty(facts.ActiveComputerName)
                && !string.Equals(facts.ComputerName, facts.ActiveComputerName, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(ComputerRenameReason);
            }

            return new PendingRestartState(reasons, warnings, renames);
        }

        /// <summary>The registry value holds pairs: source, destination (empty destination = delete).</summary>
        public static List<FileRenameOperation> ParseRenames(string[] entries, string systemRoot)
        {
            var result = new List<FileRenameOperation>();
            if (entries == null) return result;
            for (var i = 0; i < entries.Length; i += 2)
            {
                var source = NormalizePath(entries[i], systemRoot);
                var destination = i + 1 < entries.Length ? NormalizePath(entries[i + 1], systemRoot) : string.Empty;
                if (source.Length == 0 && destination.Length == 0) continue;
                result.Add(new FileRenameOperation(source, destination, IsBlocking(source, destination)));
            }

            return result;
        }

        internal static string NormalizePath(string value, string systemRoot)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var text = Regex.Replace(value, @"^[!*]", string.Empty);
            text = Regex.Replace(text, @"^\d+(?=\\\?\?\\)", string.Empty);
            text = Regex.Replace(text, @"^\\\?\?\\", string.Empty);
            if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                text = (systemRoot ?? @"C:\Windows").TrimEnd('\\') + "\\" + text.Substring(@"\SystemRoot\".Length);
            }

            return text;
        }

        internal static bool IsBlocking(string source, string destination)
        {
            var combined = (source + "\n" + destination).ToLowerInvariant();
            return BlockingPatterns.Any(pattern => Regex.IsMatch(combined, pattern));
        }
    }
}
