using System.Reflection;

namespace WinTool.Core
{
    /// <summary>Product identity shared by the GUI, the CLI and the logs.</summary>
    public static class ProductInfo
    {
        public const string Name = "WinTool by PcNinja";

        /// <summary>Single-instance id. Never change or derive it from a version.</summary>
        public const string SingleInstanceAppId = "WinTool";

        public const string HelpUrl = "https://help.pcninja.pro";
        public const string WebsiteUrl = "https://www.pcninja.pro";
        public const string RepositoryUrl = "https://github.com/JavierTorresFelendler/WinTool-by-PcNinja";

        public static string Version
        {
            get
            {
                var version = typeof(ProductInfo).Assembly.GetName().Version;
                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }

        public static string InformationalVersion
        {
            get
            {
                var attribute = typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return attribute != null ? attribute.InformationalVersion : Version;
            }
        }
    }
}
