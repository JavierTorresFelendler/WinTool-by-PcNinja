using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinTool.Cli;
using WinTool.Core.Json;
using WinTool.Office;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    /// <summary>"office" argument rules. The deployment factory throws: nothing may reach it here.</summary>
    public class OfficeCliTests
    {
        private static (int Exit, string Out, string Err) Run(bool admin, params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var commands = new OfficeCommands(() => throw new InvalidOperationException("The deployment must not run."), () => admin, output, error);
            int exit;
            try { exit = commands.Run(args[0], args.Skip(1).ToArray()); }
            catch (UsageException ex) { error.WriteLine(ex.Message); exit = CliExit.Usage; }
            return (exit, output.ToString(), error.ToString());
        }

        [Fact]
        public void Config_prints_the_same_file_the_installer_would_use()
        {
            var result = Run(false, "config", "--family", "ltsc", "--apps", "Word,Excel", "--languages", "he-il,en-us", "--visio", "--quiet");
            Assert.Equal(CliExit.Ok, result.Exit);
            var expected = OfficeConfiguration.Build(new OfficeRequest
            {
                Family = OfficeFamily.Ltsc2024,
                Apps = new List<string> { "Word", "Excel" },
                Languages = new List<string> { "he-il", "en-us" },
                Visio = true,
                Silent = true
            });
            Assert.Equal(expected, result.Out);
        }

        [Fact]
        public void Config_json_carries_the_file()
        {
            var result = Run(false, "config", "--json");
            var json = (Dictionary<string, object>)JsonData.Parse(result.Out);
            Assert.Equal(OfficeConfiguration.Build(new OfficeRequest()), json["configuration"]);
        }

        [Theory]
        [InlineData("remove")]
        [InlineData("remove", "--quiet")]
        [InlineData("install", "--remove-existing")]
        [InlineData("package")]
        [InlineData("package", "--output", "D:\\x", "--remove-existing")]
        [InlineData("install", "--family", "visio")]
        [InlineData("install", "--family", "ltsc", "--channel", "Current")]
        [InlineData("install", "--no-activate")]
        [InlineData("install", "--channel", "Beta")]
        [InlineData("install", "--apps", "Word,Paint")]
        [InlineData("install", "--languages", "xx-yy")]
        [InlineData("install", "--apps", "")]
        [InlineData("config", "--confirm")]
        [InlineData("status", "--visio")]
        [InlineData("repair")]
        public void Refusals_happen_before_anything_runs(params string[] args)
        {
            Assert.Equal(CliExit.Usage, Run(true, args).Exit);
        }

        [Theory]
        [InlineData("install")]
        [InlineData("package", "--output", "D:\\x")]
        [InlineData("remove", "--confirm")]
        public void Changing_commands_need_an_administrator(params string[] args)
        {
            Assert.Equal(CliExit.AdministratorRequired, Run(false, args).Exit);
        }

        [Fact]
        public void Remove_without_confirm_says_nothing_changed()
        {
            Assert.Contains("Nothing was changed", Run(true, "remove").Err);
        }
    }
}
