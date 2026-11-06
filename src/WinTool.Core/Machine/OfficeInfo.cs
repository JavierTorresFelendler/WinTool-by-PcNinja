using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WinTool.Core.Machine
{
    /// <summary>The installed Click-to-Run Office, read from the registry (Dashboard).</summary>
    public sealed class OfficeInfo
    {
        public string ProductIds { get; set; }
        public string ProductName { get; set; }
        public string Channel { get; set; }
        public string Platform { get; set; }
        public string Version { get; set; }
        public bool IsVolumeLicense { get; set; }
        public IReadOnlyList<string> Apps { get; set; } = new string[0];

        private static readonly Dictionary<string, string> Channels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "492350f6-3a01-4f97-b9c0-c7c6ddf67d60", "Current Channel" },
            { "64256afe-f5d9-4f86-8936-8840a6a4f5be", "Current Channel (Preview)" },
            { "5440fd1f-7ecb-4221-8110-145efaa6372f", "Beta Channel" },
            { "55336b82-a18d-4dd6-b5f6-9e5095c314a6", "Monthly Enterprise Channel" },
            { "7ffbc6bf-bc32-4f92-8982-f9dd17fd3114", "Semi-Annual Enterprise Channel" },
            { "b8f9b850-328d-4355-9145-c59439a0c4cf", "Semi-Annual Enterprise Channel (Preview)" },
            { "f2e724c1-748f-4b47-8fb8-8e0d210e9208", "Office 2019 Perpetual Enterprise" },
            { "5030841d-c919-4594-8d2d-84ae4f96e58e", "Office LTSC 2021 Perpetual Enterprise" },
            { "7983bac0-e531-40cf-be00-fd24fe66619c", "Office LTSC 2024 Perpetual Enterprise" }
        };

        private static readonly Dictionary<string, string> Products = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "O365ProPlusRetail", "Microsoft 365 Apps for Enterprise" },
            { "O365BusinessRetail", "Microsoft 365 Apps for Business" },
            { "O365HomePremRetail", "Microsoft 365 Family / Personal" },
            { "ProPlus2024Volume", "Office LTSC Professional Plus 2024" },
            { "ProPlus2021Volume", "Office LTSC Professional Plus 2021" },
            { "ProPlus2019Volume", "Office 2019 Professional Plus" },
            { "Standard2024Volume", "Office LTSC Standard 2024" },
            { "Standard2021Volume", "Office LTSC Standard 2021" },
            { "Standard2019Volume", "Office 2019 Standard" },
            { "ProPlus2019Retail", "Office 2019 Professional Plus" },
            { "ProPlus2021Retail", "Office 2021 Professional Plus" },
            { "HomeBusiness2019Retail", "Office Home & Business 2019" },
            { "HomeBusiness2021Retail", "Office Home & Business 2021" },
            { "HomeBusiness2024Retail", "Office Home & Business 2024" },
            { "HomeStudent2019Retail", "Office Home & Student 2019" },
            { "HomeStudent2021Retail", "Office Home & Student 2021" }
        };

        private static readonly KeyValuePair<string, string>[] AppExecutables =
        {
            new KeyValuePair<string, string>("WINWORD.EXE", "Word"),
            new KeyValuePair<string, string>("EXCEL.EXE", "Excel"),
            new KeyValuePair<string, string>("POWERPNT.EXE", "PowerPoint"),
            new KeyValuePair<string, string>("OUTLOOK.EXE", "Outlook"),
            new KeyValuePair<string, string>("ONENOTE.EXE", "OneNote"),
            new KeyValuePair<string, string>("MSACCESS.EXE", "Access"),
            new KeyValuePair<string, string>("MSPUB.EXE", "Publisher"),
            new KeyValuePair<string, string>("VISIO.EXE", "Visio"),
            new KeyValuePair<string, string>("WINPROJ.EXE", "Project")
        };

        public static string ChannelName(string cdnBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(cdnBaseUrl)) return null;
            var id = cdnBaseUrl.TrimEnd('/').Split('/').Last();
            string name;
            return Channels.TryGetValue(id, out name) ? name : null;
        }

        /// <summary>The first known product of a comma-separated ProductReleaseIds list.</summary>
        public static string ProductNameFor(string productIds)
        {
            if (string.IsNullOrWhiteSpace(productIds)) return null;
            var ids = productIds.Split(',').Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
            foreach (var id in ids)
            {
                string name;
                if (Products.TryGetValue(id, out name)) return name;
            }

            return ids.FirstOrDefault();
        }

        /// <summary>Support for the 2019 editions ended on 2025-10-14.</summary>
        public static bool IsOutOfSupport(string productIds) =>
            !string.IsNullOrEmpty(productIds) && productIds.IndexOf("2019", StringComparison.Ordinal) >= 0
            || !string.IsNullOrEmpty(productIds) && productIds.IndexOf("2016", StringComparison.Ordinal) >= 0;

        public bool OutOfSupport => IsOutOfSupport(ProductIds);

        /// <summary>Null when no Click-to-Run Office is installed.</summary>
        public static OfficeInfo ReadCurrent()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
            {
                if (key == null) return null;
                var ids = key.GetValue("ProductReleaseIds") as string;
                if (string.IsNullOrWhiteSpace(ids)) return null;

                var info = new OfficeInfo
                {
                    ProductIds = ids,
                    ProductName = ProductNameFor(ids),
                    Channel = ChannelName((key.GetValue("UpdateChannel") as string) ?? (key.GetValue("CDNBaseUrl") as string)),
                    Platform = (key.GetValue("Platform") as string) == "x86" ? "32-bit" : "64-bit",
                    Version = key.GetValue("VersionToReport") as string,
                    IsVolumeLicense = ids.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0
                };

                var installPath = key.GetValue("InstallationPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    var root = Path.Combine(installPath, "root", "Office16");
                    info.Apps = AppExecutables.Where(app => File.Exists(Path.Combine(root, app.Key))).Select(app => app.Value).ToList();
                }

                return info;
            }
        }
    }
}
