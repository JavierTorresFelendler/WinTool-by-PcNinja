using System;
using System.IO;
using System.Reflection;

namespace WinTool.App.Infrastructure
{
    /// <summary>
    /// Loads referenced assemblies that the build embedded into WinTool-by-PcNinja.exe.
    /// The portable EXE is one file; nothing is extracted to disk (lesson from
    /// WinUpdate Tool V2.2.5: no dropped executables).
    /// This class must not reference WinTool.Core: it runs before Core can load.
    /// </summary>
    internal static class EmbeddedAssemblies
    {
        private const string Prefix = "WinTool.Embedded.";

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (!string.IsNullOrEmpty(requested.CultureName) && requested.CultureName != "neutral") return null;

            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase)) return loaded;
            }

            var resourceName = Prefix + requested.Name + ".dll";
            using (var stream = typeof(EmbeddedAssemblies).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return Assembly.Load(buffer.ToArray());
                }
            }
        }
    }
}
