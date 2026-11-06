// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using System.Text;
using WinTool.Core.Json;
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class SetupOutcomeChecks
{
    internal static void Run()
    {
        var start = new DateTimeOffset(2026, 10, 19, 4, 56, 17, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 19)));
        var attempt = new UpgradeAttempt(start, @"C:\Media", @"C:\Media\sources\install.esd", 1,
            19043, 19041, "Professional", 0x40010004, "OutcomeUnconfirmed", "pending");
        const string header = """
2026-10-19 04:56:20, Info MOUPG ******** SetupHost Logging Begin ********
2026-10-19 04:56:20, Info MOUPG SetupHost::Initialize: MediaPath = [C:\Media]
2026-10-19 04:56:20, Info MOUPG SetupHost::Initialize: CmdLine = [/Install /InstallFile "C:\Media\sources\install.esd" "/imageindex" "1" ]
""";
        const string panther = """
2026-10-19 05:16:56, Warning SP SPMoveFileWithShortName: Failed to move C:\$WINDOWS.~BT\NewOS\Recovery\ReAgentOld.xml to C:\Recovery\ReAgentOld.xml, error: 0x000000B7
2026-10-19 05:16:56, Info SP Execution phase [Safe OS] exiting with HRESULT [0x800700B7]
""";
        const string rollback = """
2026-10-19 05:19:15, Info SP Rollback completed successfully.
2026-10-19 05:19:41, Info MOUPG SetupHost: Reporting post reboot event -> [0x800700B7, 0x2000A]
""";
        var evidence = SetupOutcomeDiagnostics.Parse(attempt, header, panther, rollback, "fixture.log")
            ?? throw new Exception("Correlated rollback was not detected.");
        Require(evidence.ResultCode == "0x800700B7" && evidence.ExtendedCode == "0x2000A" &&
            evidence.Summary.Contains("ReAgentOld.xml"), "Specific failure evidence was lost.");
        var current = new CurrentWindows("Professional", "en-US", 19043);
        var observed = UpgradeHistory.Reconcile(attempt, current, false, evidence);
        Require(observed.State == "RollbackObserved" && observed.ProcessExitCode == 0x40010004 &&
            observed.SetupFailure == evidence, "Setup failure replaced/misrepresented the process exit.");
        Require(UpgradeHistory.Reconcile(attempt, current, true, evidence) == attempt,
            "Running Setup was classified as a completed rollback.");
        Require(UpgradeHistory.Reconcile(attempt, current with { Build = 19045 }, false, evidence) == attempt,
            "Changed Windows 10 build was classified using old rollback evidence.");
        Require(JsonData.Deserialize<UpgradeAttempt>(JsonData.Serialize(attempt)) == attempt,
            "Existing history JSON compatibility was broken.");

        void Reject(UpgradeAttempt record, string h, string p, string r) => Require(
            SetupOutcomeDiagnostics.Parse(record, h, p, r, "fixture.log") is null,
            "Unrelated, incomplete or stale rollback evidence was accepted.");
        Reject(attempt with { StartedUtc = start.AddDays(1) }, header, panther, rollback);
        Reject(attempt with { SourceRoot = @"C:\Other" }, header, panther, rollback);
        Reject(attempt with { ImagePath = @"C:\Other\install.esd" }, header, panther, rollback);
        Reject(attempt with { ImageIndex = 2 }, header, panther, rollback);
        Reject(attempt, header, panther, "");
        Reject(attempt, header, panther, rollback.Replace("Rollback completed successfully.", "Still rolling back."));
        Reject(attempt, header, panther, rollback.Replace("800700B7", "80070002"));
        Reject(attempt, header, panther, rollback.Replace("2026-10-19", "2026-10-18"));
        Reject(attempt, header, panther.Replace("05:16:56", "04:50:00"), rollback);
        Reject(attempt, header, panther + "\n2026-10-19 06:00:00, Info MOUPG SetupHost Logging Begin", rollback);
        Require(!SetupOutcomeDiagnostics.Parse(attempt, header,
            panther.Replace("ReAgentOld.xml", "Other.xml"), rollback, "fixture.log")!.Summary.Contains("Recovery"),
            "Generic duplicate-file error was misdiagnosed as Recovery collision.");

        var temporary = Path.Combine(Path.GetTempPath(), "PcNinja-Outcome-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sources = Path.Combine(temporary, "$WINDOWS.~BT", "Sources");
            Directory.CreateDirectory(Path.Combine(sources, "Panther"));
            Directory.CreateDirectory(Path.Combine(sources, "Rollback"));
            foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode })
            {
                File.WriteAllText(Path.Combine(sources, "Panther", "setupact.log"),
                    header + "\n" + new string('x', 600000) + "\n" + panther, encoding);
                File.WriteAllText(Path.Combine(sources, "Rollback", "setupact.log"), rollback, encoding);
                Require(SetupOutcomeDiagnostics.Read(attempt, temporary)?.ResultCode == evidence.ResultCode,
                    "Bounded UTF-8/UTF-16 log reading failed.");
            }
            Require(SetupOutcomeDiagnostics.Read(attempt, Path.Combine(temporary, "missing")) is null,
                "Missing logs were not treated as unknown.");
        }
        finally { Directory.Delete(temporary, recursive: true); }
        Console.WriteLine("PASS: Correlated bounded rollback diagnostics, stale/session/source/index rejection, active Setup guard, JSON compatibility; no Setup or Recovery mutations.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
