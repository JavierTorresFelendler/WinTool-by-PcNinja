using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WinTool.Core.Operations;
using WinTool.Core.Security;
using WinTool.WindowsUpdate.Schedule;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class MaintenancePolicyTests
    {
        [Theory]
        [InlineData("broken XML")]
        [InlineData("<Task/>")]
        [InlineData("<Task><Command>a</Command><Command>b</Command></Task>")]
        public void Odd_definition_is_skipped_while_legacy_retry_is_refreshed(string oddXml)
        {
            var tasks = new FakeTasks();
            tasks.Register(TaskNames.Main, oddXml);
            tasks.Register(TaskNames.Retry, TaskXmlBuilder.BuildRetry(@"C:\old.exe", DateTime.Now.AddHours(1), "fixture", DateTime.Now));
            var service = new ScheduleService(tasks, new ListLog(), @"C:\new.exe", () => DateTime.Now,
                path => path, installedSource: () => true);
            service.RefreshExisting();
            Assert.Equal(oddXml, tasks.Get(TaskNames.Main).Xml);
            Assert.Contains(@"C:\new.exe", tasks.Get(TaskNames.Retry).Xml);
        }

        [Theory]
        [InlineData("status", "--json", false)]
        [InlineData("schedule", "show", false)]
        [InlineData("update", "scan", false)]
        [InlineData("office", "status", false)]
        [InlineData("app-update", "check", false)]
        [InlineData("schedule", "set", true)]
        public void Read_only_commands_do_not_trigger_refresh(string area, string command, bool expected) =>
            Assert.Equal(expected, MaintenancePolicy.RefreshOnCommand(new[] { area, command }));

        [Theory]
        [InlineData(false, false, true, false)]
        [InlineData(true, false, true, true)]
        [InlineData(false, false, false, true)]
        [InlineData(false, true, true, true)]
        public void Portable_refresh_respects_protected_installed_targets(bool installed, bool managed, bool protectedFile, bool changes)
        {
            var old = managed ? Path.Combine(ProtectedTaskCopy.Folder, new string('A', 64), "WinTool-by-PcNinja.exe") : @"C:\installed\WinTool.exe";
            var tasks = new FakeTasks();
            tasks.Register(TaskNames.Retry, TaskXmlBuilder.BuildRetry(old, DateTime.Now.AddHours(1), "fixture", DateTime.Now));
            var resolved = 0;
            var service = new ScheduleService(tasks, new ListLog(), @"C:\portable\WinTool.exe", () => DateTime.Now,
                _ => { resolved++; return @"C:\new-worker.exe"; }, installedSource: () => installed, protectedTarget: _ => protectedFile);
            service.RefreshExisting();
            Assert.Equal(changes ? 1 : 0, resolved);
            var command = (string)XDocument.Parse(tasks.Get(TaskNames.Retry).Xml).Descendants().Single(element => element.Name.LocalName == "Command");
            Assert.Equal(changes ? @"C:\new-worker.exe" : old, command);
        }

        [Theory]
        [InlineData(false, false, 1)]
        [InlineData(false, true, 1)]
        [InlineData(true, false, 0)]
        [InlineData(true, true, 21)]
        public void Maintenance_failure_is_logged_and_only_busy_blocks_removal(bool removal, bool busy, int expected)
        {
            var warnings = 0;
            Assert.Equal(expected, MaintenancePolicy.Execute(() =>
                { if (busy) throw new OperationBusyException(); throw new InvalidOperationException("odd task"); }, removal, _ => warnings++));
            Assert.Equal(1, warnings);
        }
    }
}
