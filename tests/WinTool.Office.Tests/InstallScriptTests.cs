using System.Diagnostics;
using System.Globalization;
using System.Text;
using Xunit;

namespace WinTool.Office.Tests;

public sealed class InstallScriptTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task The_offline_launcher_reports_and_returns_the_setup_result(int setupExitCode)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;

        // Spaces exercise the package-path quoting as well as the result handling.
        var folder = Path.Combine(Path.GetTempPath(), "WinToolOfficeTests", "offline launcher " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var script = OfficeDeployment.InstallScript;
            Assert.All(script, character => Assert.InRange((int)character, 0, 127));
            File.WriteAllText(Path.Combine(folder, "Install-Office.bat"), script, Encoding.ASCII);
            File.WriteAllText(Path.Combine(folder, "Office_Config.xml"), "<Configuration />", Encoding.ASCII);
            await CompileStubAsync(folder);

            // Only the installation outcome is under test: a local net.exe stub
            // accepts the administrator probe without needing elevation or calling net session.
            File.Copy(Path.Combine(folder, "setup.exe"), Path.Combine(folder, "net.exe"));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                "/d /c Install-Office.bat") { WorkingDirectory = folder };
            start.EnvironmentVariables["PATH"] = folder;
            start.EnvironmentVariables["WINTOOL_TEST_SETUP_EXIT_CODE"] = setupExitCode.ToString(CultureInfo.InvariantCulture);
            // Prove the script replaces an inherited value with the real setup result.
            start.EnvironmentVariables["setupExitCode"] = "99";

            var result = await RunAsync(start);
            Assert.Contains("TEST-STUB setup exit " + setupExitCode, result.Output);
            Assert.Equal(setupExitCode, result.ExitCode);
            if (setupExitCode == 0)
            {
                Assert.Contains("Office installation succeeded.", result.Output);
                Assert.DoesNotContain("Office installation failed", result.Output);
            }
            else
            {
                Assert.Contains("Office installation failed with exit code 42.", result.Output);
                Assert.DoesNotContain("Office installation succeeded", result.Output);
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static async Task CompileStubAsync(string folder)
    {
        // The Windows .NET Framework compiler builds a tiny standalone stub
        // without package downloads; it runs in both test target frameworks.
        var frameworkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET");
        var compiler = Path.Combine(frameworkRoot, "Framework64", "v4.0.30319", "csc.exe");
        if (!File.Exists(compiler)) compiler = Path.Combine(frameworkRoot, "Framework", "v4.0.30319", "csc.exe");
        Assert.True(File.Exists(compiler), "The Windows .NET Framework C# compiler is required for the launcher test.");

        var source = Path.Combine(folder, "Stub.cs");
        File.WriteAllText(source, @"
using System;
using System.IO;
public static class Stub
{
    public static int Main(string[] args)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]), ""net"", StringComparison.OrdinalIgnoreCase))
            return args.Length == 1 && args[0] == ""session"" ? 0 : 98;
        if (args.Length != 2 || args[0] != ""/configure"" || args[1] != ""Office_Config.xml"" || !File.Exists(args[1]))
            return 97;
        var code = int.Parse(Environment.GetEnvironmentVariable(""WINTOOL_TEST_SETUP_EXIT_CODE""));
        Console.WriteLine(""TEST-STUB setup exit "" + code);
        return code;
    }
}
", Encoding.ASCII);
        var start = new ProcessStartInfo(compiler, "/nologo /target:exe /out:\"" + Path.Combine(folder, "setup.exe") + "\" \"" + source + "\"")
        {
            WorkingDirectory = folder
        };
        var result = await RunAsync(start);
        Assert.True(result.ExitCode == 0, "Stub compilation failed: " + result.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (var process = Process.Start(start)!)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            // EOF lets pause return without a keypress on either result path.
            process.StandardInput.Close();
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                process.WaitForExit();
                throw new TimeoutException("The offline launcher test process did not exit within 30 seconds.");
            }

            return (process.ExitCode, await output + await error);
        }
    }
}
