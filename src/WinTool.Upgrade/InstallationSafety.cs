using Microsoft.Win32;

namespace WinTool.Upgrade;

public sealed class UnsafeInstallationMediaException(string message) : InvalidOperationException(message);

public static class InstallationSafety
{
    private static readonly string[] AnswerNames = ["Autounattend.xml", "Unattend.xml"];

    public static void EnsureSafe(string selectedRoot, bool removeSelectedAnswerFiles = true) =>
        Check(selectedRoot, GetAttachedRoots(), removeSelectedAnswerFiles, true);

    internal static void Check(string selectedRoot, IEnumerable<string> attachedRoots,
        bool removeSelectedAnswerFiles, bool inspectSystemLocations = false)
    {
        selectedRoot = Fx.TrimEndingDirectorySeparator(Path.GetFullPath(selectedRoot));
        var selectedCandidates = AnswerNames.SelectMany(name => new[]
        {
            Path.Combine(selectedRoot, name), Path.Combine(selectedRoot, "sources", name)
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new HashSet<string>(selectedCandidates, StringComparer.OrdinalIgnoreCase);
        foreach (var root in attachedRoots.Append(Path.GetPathRoot(selectedRoot)!))
        {
            foreach (var name in AnswerNames) candidates.Add(Path.Combine(root, name));
            if (File.Exists(Path.Combine(root, "setup.exe")))
                foreach (var name in AnswerNames) candidates.Add(Path.Combine(root, "sources", name));
        }
        if (inspectSystemLocations)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var name in AnswerNames)
                candidates.Add(Path.Combine(windows, "Panther", "Unattend", name));
            using var setup = Registry.LocalMachine.OpenSubKey(@"SYSTEM\Setup");
            if (setup?.GetValue("UnattendFile") is string configured && !string.IsNullOrWhiteSpace(configured))
                candidates.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim('"'))));
            // Windows owns the annotated Panther cache; never delete/reuse it.
        }

        foreach (var path in candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw Blocked(path, "The file could not be checked.", ex); }

            if (removeSelectedAnswerFiles && selectedCandidates.Contains(path) &&
                (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 &&
                !HasReparseParent(path) && IsInstallationFolder(selectedRoot))
            {
                try
                {
                    File.SetAttributes(path, attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
                    File.Delete(path);
                    try { File.GetAttributes(path); }
                    catch (FileNotFoundException)
                    {
                        AppLog.Info($"Removed selected-source answer file: {path}");
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { AppLog.Info($"Cannot remove selected-source answer file {path}: {ex.Message}"); }
            }
            throw Blocked(path, "Customized installation media can trigger a clean install and data loss.");
        }
    }

    private static bool IsInstallationFolder(string root) => File.Exists(Path.Combine(root, "setup.exe")) &&
        new[] { "install.wim", "install.esd", "install.swm" }.Any(name => File.Exists(Path.Combine(root, "sources", name))) &&
        !root.Equals(Fx.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase);

    private static bool HasReparseParent(string path)
    {
        for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static IEnumerable<string> GetAttachedRoots()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            // Empty card readers / detached disks cannot supply an answer file.
            if (!drive.IsReady) continue;
            yield return drive.RootDirectory.FullName;
        }
    }

    private static UnsafeInstallationMediaException Blocked(string path, string detail, Exception? inner = null)
    {
        if (inner is not null) AppLog.Error($"Answer-file safety check failed: {path}. {inner}");
        return new($"Upgrade blocked: {path}\n{detail}\nRemove the interfering ISO/USB or answer file, then Refresh and try again. Windows Setup was not started.");
    }
}
