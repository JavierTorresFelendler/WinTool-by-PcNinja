using System;
using WinTool.WindowsUpdate.Logic;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class RestartGuardTests
    {
        private sealed class Lease : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        private static readonly WindowsUpdateActivity Installing =
            new WindowsUpdateActivity(ActivityStatus.Installing, new[] { "TiWorker" }, new string[0]);

        [Fact]
        public void A_scheduled_run_holding_the_lock_blocks_the_restart()
        {
            Assert.Equal(RestartDecision.OperationRunning,
                RestartGuard.Evaluate(() => null, () => false, () => WindowsUpdateActivity.Idle, out var lease));
            Assert.Null(lease);
        }

        [Fact]
        public void A_busy_page_blocks_the_restart_and_the_lock_is_released()
        {
            var held = new Lease();
            Assert.Equal(RestartDecision.OperationRunning,
                RestartGuard.Evaluate(() => held, () => true, () => WindowsUpdateActivity.Idle, out var lease));
            Assert.Null(lease);
            Assert.True(held.Disposed);
        }

        [Fact]
        public void Windows_servicing_asks_first_and_otherwise_the_restart_keeps_the_lock()
        {
            var held = new Lease();
            Assert.Equal(RestartDecision.WindowsInstalling, RestartGuard.Evaluate(() => held, () => false, () => Installing, out var lease));
            Assert.Same(held, lease);
            Assert.Equal(RestartDecision.Allowed, RestartGuard.Evaluate(() => new Lease(), () => false, () => WindowsUpdateActivity.Idle, out var other));
            Assert.NotNull(other);
        }
    }
}
