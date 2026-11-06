using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinTool.Core.Platform;

namespace WinTool.Core.Machine
{
    /// <summary>
    /// Computer facts for the Dashboard (the same set WinUpdate V2 showed).
    /// Everything comes from the registry or .NET; each value fails on its
    /// own and shows as empty rather than hiding the rest.
    /// </summary>
    public sealed class MachineInfo
    {
        public string ComputerName { get; set; }
        public string UserName { get; set; }
        public OsInfo Os { get; set; }
        public string LanAddress { get; set; }
        public string Processor { get; set; }
        public int LogicalProcessors { get; set; }
        public long? MemoryBytes { get; set; }
        public long? SystemDriveTotalBytes { get; set; }
        public long? SystemDriveFreeBytes { get; set; }
        public string Graphics { get; set; }
        public string Motherboard { get; set; }
        public string Bios { get; set; }
        public bool? SecureBoot { get; set; }
        public TimeSpan? Uptime { get; set; }

        /// <summary>Windows 10 support ended on 2025-10-14 (builds below 22000 are Windows 10).</summary>
        public static bool IsWindows10(int build) => build > 0 && build < 22000;

        public static MachineInfo ReadCurrent()
        {
            var info = new MachineInfo
            {
                ComputerName = Environment.MachineName,
                UserName = Environment.UserDomainName + "\\" + Environment.UserName,
                LogicalProcessors = Environment.ProcessorCount
            };

            info.Os = Try(OsInfo.ReadCurrent);
            info.LanAddress = Try(ReadLanAddress);
            info.Processor = Try(() => Clean(Value(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")));
            info.MemoryBytes = TryStruct(ReadMemory);
            info.Graphics = Try(ReadGraphics);
            info.Motherboard = Try(() => Join(Value(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer"), Value(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct")));
            info.Bios = Try(() => Clean(Value(@"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion")));
            info.SecureBoot = TryStruct(() =>
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                {
                    var value = key?.GetValue("UEFISecureBootEnabled");
                    return value is int ? (int)value == 1 : (bool?)null;
                }
            });
            info.Uptime = TryStruct(() => (TimeSpan?)TimeSpan.FromMilliseconds(GetTickCount64()));

            var drive = Try(() => new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))));
            if (drive != null)
            {
                info.SystemDriveTotalBytes = TryStruct(() => (long?)drive.TotalSize);
                info.SystemDriveFreeBytes = TryStruct(() => (long?)drive.AvailableFreeSpace);
            }

            return info;
        }

        /// <summary>The public address, from api.ipify.org (as in WinUpdate V2). Null when offline.</summary>
        public static string ReadWanAddress(int timeoutMilliseconds = 4000)
        {
            try
            {
#pragma warning disable SYSLIB0014
                var request = (HttpWebRequest)WebRequest.Create("https://api.ipify.org");
#pragma warning restore SYSLIB0014
                request.Timeout = timeoutMilliseconds;
                request.UserAgent = "WinTool by PcNinja";
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var text = reader.ReadToEnd().Trim();
                    IPAddress parsed;
                    return IPAddress.TryParse(text, out parsed) ? text : null;
                }
            }
            catch (Exception ex) when (ex is WebException || ex is IOException || ex is NotSupportedException)
            {
                return null;
            }
        }

        public static string FormatBytes(long? bytes)
        {
            if (!bytes.HasValue) return string.Empty;
            var gb = bytes.Value / (1024.0 * 1024 * 1024);
            return (gb >= 100 ? gb.ToString("0", CultureInfo.InvariantCulture) : gb.ToString("0.#", CultureInfo.InvariantCulture)) + " GB";
        }

        private static string ReadLanAddress()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderBy(n => n.GetIPProperties().GatewayAddresses.Count == 0 ? 1 : 0))
            {
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        return address.Address.ToString();
                    }
                }
            }

            return null;
        }

        private static string ReadGraphics()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (key == null) return null;
                var names = key.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit))
                    .Select(n => { using (var sub = key.OpenSubKey(n)) return sub?.GetValue("DriverDesc") as string; })
                    .Where(n => !string.IsNullOrWhiteSpace(n) && n.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Remote Display", StringComparison.OrdinalIgnoreCase) < 0)
                    .Distinct().ToList();
                return names.Count == 0 ? null : string.Join(", ", names);
            }
        }

        private static long? ReadMemory()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
            return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhysical : (long?)null;
        }

        private static string Value(string path, string name)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(path)) return key?.GetValue(name) as string;
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

        private static string Join(string a, string b) => string.Join(" ", new[] { Clean(a), Clean(b) }.Where(s => s != null));

        private static T Try<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
        }

        private static T? TryStruct<T>(Func<T?> read) where T : struct
        {
            try { return read(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
