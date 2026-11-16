// VM fixture only. Never ships as a product or runs during kit creation/tests.
using System.IO;
using System.Reflection;
[assembly: AssemblyTitle("Issue29 TEST FIXTURE - NOT WINTOOL")]
[assembly: AssemblyProduct("Issue29 TEST FIXTURE - NOT WINTOOL")]
[assembly: AssemblyVersion("1.0.4.0")]
#if OLDER
[assembly: AssemblyFileVersion("1.0.3.9000")]
[assembly: AssemblyInformationalVersion("1.0.3")]
#elif NEWER
[assembly: AssemblyFileVersion("1.0.5.9000")]
[assembly: AssemblyInformationalVersion("1.0.5")]
#else
[assembly: AssemblyFileVersion("1.0.4.3001")]
[assembly: AssemblyInformationalVersion("1.0.4-rc.1")]
#endif
internal static class InstalledStub
{
    private static int Main()
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "executed.marker"),
            "Issue29 fixture executed; no WinTool operation performed.");
        return 0;
    }
}
