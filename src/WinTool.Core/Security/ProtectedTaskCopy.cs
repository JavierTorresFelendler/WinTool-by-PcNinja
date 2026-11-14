using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WinTool.Core.Operations;
using WinTool.Core.Updates;

namespace WinTool.Core.Security
{
    /// <summary>D-027: a protected local, content-addressed task target, independent of MSI registration.</summary>
    public static class ProtectedTaskCopy
    {
        public static string Folder => Path.Combine(ProtectedMachineData.Root, "Scheduler");
        public static bool IsManagedPath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path); var generation = Path.GetDirectoryName(full); var name = Path.GetFileName(generation);
                return string.Equals(Path.GetDirectoryName(generation), Folder, StringComparison.OrdinalIgnoreCase) && name.Length == 64 && name.All(Uri.IsHexDigit) &&
                    string.Equals(Path.GetFileName(full), ReleaseFiles.Executable, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return false; }
        }

        public static string Resolve(string source)
        {
            using (OperationGate.EnterWindows())
            {
                if (ProtectedMachineData.IsProtectedExecutable(source)) return Path.GetFullPath(source);
                return Provision(source, Folder, OpenRunningImage, path => ProtectedMachineData.EnsureDirectory(path),
                    path => ProtectedMachineData.OpenWriter(path, false), ProtectedMachineData.IsProtectedExecutable);
            }
        }

        internal static string Provision(string source, string folder, Func<string, Stream> openSource, Action<string> prepare,
            Func<string, Stream> create, Func<string, bool> protectedFile)
        {
            prepare(folder);
            using (var input = openSource(source))
            using (var sha = SHA256.Create())
            {
                var hash = Compat.ToHexString(sha.ComputeHash(input));
                input.Position = 0;
                var generation = Path.Combine(folder, hash);
                prepare(generation);
                var target = Path.Combine(generation, ReleaseFiles.Executable);
                if (File.Exists(target))
                {
                    if (!protectedFile(target) || !string.Equals(AppUpdater.Sha256(target), hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Existing scheduler copy failed validation.");
                    return target;
                }
                var staged = Path.Combine(generation, Guid.NewGuid().ToString("N") + ".partial");
                try
                {
                    using (var output = create(staged)) input.CopyTo(output);
                    if (!protectedFile(staged) || !string.Equals(AppUpdater.Sha256(staged), hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Scheduler copy validation failed.");
                    File.Move(staged, target);
                    return target;
                }
                finally { if (File.Exists(staged)) File.Delete(staged); }
            }
        }

        public static void Retire(IEnumerable<string> referenced)
        {
            if (referenced == null || !Directory.Exists(Folder)) return;
            using (OperationGate.EnterWindows()) Retire(Folder, referenced, ProtectedMachineData.IsProtectedExecutable);
        }

        internal static void Retire(string folder, IEnumerable<string> referenced, Func<string, bool> protectedFile)
        {
            var keep = new HashSet<string>(referenced.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.GetDirectories(folder))
            {
                var name = Path.GetFileName(directory);
                if (name.Length != 64 || !name.All(Uri.IsHexDigit)) continue;
                var exe = Path.Combine(directory, ReleaseFiles.Executable);
                if (keep.Contains(Path.GetFullPath(exe))) continue;
                if (!File.Exists(exe)) { try { if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { } continue; }
                if (!protectedFile(exe)) continue;
                try
                {
                    // Win32 DeleteFile refuses an in-use executable image. Never kill a worker or use POSIX deletion.
                    File.Delete(exe);
                    var pending = Path.Combine(directory, "retirement.pending");
                    if (File.Exists(pending)) File.Delete(pending);
                }
                catch (IOException) { Pending(directory); }
                catch (UnauthorizedAccessException) { Pending(directory); }
                // Empty generation directories may remain pinned until process exit; cleanup is retried on later task changes.
            }
        }

        private static void Pending(string directory)
        {
            var path = Path.Combine(directory, "retirement.pending");
            try
            {
                if (ProtectedMachineData.IsMachinePath(path))
                { using (var stream = ProtectedMachineData.OpenWriter(path, true)) { var bytes = Encoding.UTF8.GetBytes("Cleanup deferred until the image is no longer in use.\n"); stream.Write(bytes, 0, bytes.Length); } }
                else File.WriteAllText(path, "cleanup deferred");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static Stream OpenRunningImage(string source)
        {
            var handle = CreateFileW(source, 0x80020080, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("The running image cannot be opened for scheduler provisioning."); }
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var mapped = new StringBuilder(32768); var opened = new StringBuilder(32768);
                    if (GetMappedFileNameW(process.Handle, process.MainModule.BaseAddress, mapped, mapped.Capacity) == 0 ||
                        GetFinalPathNameByHandleW(handle, opened, opened.Capacity, 2) == 0 ||
                        !string.Equals(mapped.ToString(), opened.ToString(), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Scheduler source does not identify the running executable image.");
                    var identity = ExeIdentity.Read(source);
                    if (identity.ProductName != ProductInfo.Name || !Version.TryParse(identity.FileVersion, out _))
                        throw new IOException("Scheduler source product identity is invalid.");
                    return new FileStream(handle, FileAccess.Read);
                }
            }
            catch { handle.Dispose(); throw; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("psapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint GetMappedFileNameW(IntPtr process, IntPtr address, StringBuilder name, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, int length, uint flags);
    }
}
