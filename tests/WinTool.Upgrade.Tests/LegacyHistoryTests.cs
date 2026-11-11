using WinTool.Core.Json;
using WinTool.Upgrade;
using Xunit;

namespace WinTool.Upgrade.Tests;

public sealed class LegacyHistoryTests
{
    [Fact]
    public void Imported_history_does_not_claim_a_verified_upgrade_outcome()
    {
        var old = new UpgradeAttempt(DateTimeOffset.UtcNow.AddDays(-1), @"E:\", @"E:\sources\install.wim",
            1, 19045, 26200, "Professional", 0, "TargetWindowsObserved", "old success claim");
        var imported = JsonData.Deserialize<UpgradeAttempt>(UpgradeHistory.ValidateLegacyState(JsonData.Serialize(old)));
        Assert.NotNull(imported);
        Assert.Equal("LegacyStateUnconfirmed", imported!.State);
        Assert.Null(imported.ProcessExitCode);
        Assert.Null(imported.SetupFailure);
        Assert.Throws<FormatException>(() => UpgradeHistory.ValidateLegacyState(JsonData.Serialize(old with { ImageIndex = 0 })));
    }
}
