using System;
using System.Collections.Generic;
using System.IO;
using WinTool.Core.Security;

namespace WinTool.Core.IO
{
    public sealed class MigrationRule
    {
        public MigrationRule(string relativePath, Func<string, string> validate) { RelativePath = relativePath; Validate = validate; }
        public string RelativePath { get; }
        public Func<string, string> Validate { get; }
    }

    /// <summary>One bounded, restartable migration; caller supplies the machine operation lock and typed validators.</summary>
    public static class MachineDataMigration
    {
        public static void Run(string root, string legacy, IEnumerable<MigrationRule> rules, Func<IDisposable> acquire,
            Action<string> prepare, Func<string, string> read, Action<string, string> write)
        {
            var marker = Path.Combine(root, "migration-v1.complete.json");
            using (var operation = acquire())
            {
                if (operation == null) throw new IOException("Machine data migration is busy.");
                prepare(root);
                if (File.Exists(marker))
                {
                    if (read(marker) != "{\"schemaVersion\":1,\"complete\":true}") throw new IOException("Migration completion is unconfirmed.");
                    return;
                }
                var report = new List<string>();
                foreach (var rule in rules)
                {
                    var relative = rule.RelativePath;
                    if (Path.IsPathRooted(relative) || relative.Contains("..") || relative.IndexOf(':') >= 0) throw new ArgumentException("Migration rule must be relative.");
                    var source = Path.Combine(legacy, relative);
                    var target = Path.Combine(root, relative);
                    if (File.Exists(target)) { read(target); report.Add(relative + ": existing protected file retained"); continue; }
                    if (!File.Exists(source)) continue;
                    string parsed;
                    try
                    {
                        parsed = rule.Validate(ProtectedMachineData.ReadUntrustedFile(source));
                        if (parsed == null) throw new FormatException("Input was not validated.");
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException ||
                        ex is ArgumentException || ex is System.Runtime.Serialization.SerializationException || ex is System.Security.Cryptography.CryptographicException)
                    { report.Add(relative + ": not imported (" + ex.GetType().Name + ")"); continue; }
                    // Destination failures must leave completion unpublished so a later run can retry.
                    prepare(Path.GetDirectoryName(target));
                    write(target, parsed);
                    report.Add(relative + ": validated and reserialized; legacy outcomes unconfirmed");
                }
                write(Path.Combine(root, "migration-v1.report.txt"), string.Join(Environment.NewLine, report));
                // Written last: a crash never publishes a completion marker ahead of its data/report.
                write(marker, "{\"schemaVersion\":1,\"complete\":true}");
            }
        }
    }
}
