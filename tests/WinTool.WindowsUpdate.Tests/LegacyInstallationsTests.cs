using WinTool.WindowsUpdate.Migration;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class LegacyInstallationsTests
    {
        private const string ProgramFiles = @"C:\Program Files";
        private const string System32 = @"C:\Windows\system32";
        private const string Written =
            "\"C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Program Files\\PcNinja\\WinUpdateTool\\Uninstall-WinUpdateTool.ps1\"";

        [Fact]
        public void The_exact_string_Install_WinUpdateTool_writes_is_accepted_and_rebuilt()
        {
            var item = LegacyInstallPolicy.ForScript(Written, ProgramFiles, System32);
            Assert.NotNull(item);
            Assert.Equal(LegacyInstallKind.Script, item.Kind);
            Assert.Equal(@"C:\Windows\system32\WindowsPowerShell\v1.0\powershell.exe", item.File);
            Assert.Contains(@"-File ""C:\Program Files\PcNinja\WinUpdateTool\Uninstall-WinUpdateTool.ps1""", item.Arguments);
        }

        [Theory]
        [InlineData("\"C:\\Temp\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Program Files\\PcNinja\\WinUpdateTool\\Uninstall-WinUpdateTool.ps1\"")]
        [InlineData("\"C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Users\\x\\evil.ps1\"")]
        [InlineData("\"C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"C:\\Program Files\\PcNinja\\WinUpdateTool\\Uninstall-WinUpdateTool.ps1\" & del C:\\*")]
        [InlineData("\"C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\powershell.exe\" -Command \"Remove-Item C:\\ -Recurse\"")]
        [InlineData("cmd.exe /c Uninstall.cmd")]
        [InlineData("")]
        public void Anything_else_is_never_run(string quiet)
        {
            Assert.Null(LegacyInstallPolicy.ForScript(quiet, ProgramFiles, System32));
        }

        [Fact]
        public void An_msi_installation_is_removed_quietly_by_product_code()
        {
            var item = LegacyInstallPolicy.ForMsi("{11111111-2222-3333-4444-555555555555}");
            Assert.Equal("msiexec.exe", item.File);
            Assert.Equal("/x {11111111-2222-3333-4444-555555555555} /qn /norestart", item.Arguments);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(3010, true)]
        [InlineData(1641, true)]
        [InlineData(1605, true)]
        [InlineData(1603, false)]
        [InlineData(1618, false)]
        [InlineData(1, false)]
        public void Exit_codes(int code, bool removed)
        {
            Assert.Equal(removed, LegacyInstallPolicy.Succeeded(code));
        }
    }
}
