using System;
using System.Threading;
using System.Threading.Tasks;
using WinTool.Core.Operations;
using Xunit;

namespace WinTool.Core.Tests
{
    /// <summary>The gate is a Windows kernel object; these tests run on Windows (CI) and pass trivially elsewhere.</summary>
    public class OperationLockTests
    {
        private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        private static string UniqueName() => @"Local\PcNinja.WinTool.OperationGate.Tests." + Guid.NewGuid().ToString("N");

        [Fact]
        public void Illegal_nesting_surfaces_as_an_exception_instead_of_a_busy_result()
        {
            if (!OnWindows) return;
            var name = UniqueName();
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            using (var update = new OperationGate.TestEndpoint(name, identity.User.Value).EnterUpdate())
            {
                Assert.Throws<InvalidOperationException>(() => OperationLock.TryAcquireForTest("nested Windows operation", TimeSpan.FromMilliseconds(200), name));
            }
        }

        [Fact]
        public async Task A_second_holder_is_refused_while_the_first_holds_the_lock()
        {
            if (!OnWindows) return;
            var name = UniqueName();
            using (var first = OperationLock.TryAcquireForTest("install updates", TimeSpan.Zero, name))
            {
                Assert.NotNull(first);
                // A separate flow (no shared async context) must be refused.
                var second = await Task.Run(() =>
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        return Task.Run(() => OperationLock.TryAcquireForTest("upgrade", TimeSpan.FromMilliseconds(200), name));
                    }
                });
                Assert.Null(second);
            }
        }

        [Fact]
        public void The_lock_is_free_again_after_release()
        {
            if (!OnWindows) return;
            var name = UniqueName();
            OperationLock.TryAcquireForTest("first", TimeSpan.Zero, name).Dispose();
            using (var again = OperationLock.TryAcquireForTest("second", TimeSpan.Zero, name))
            {
                Assert.NotNull(again);
            }
        }

        [Fact]
        public async Task The_lock_may_be_released_on_another_thread_after_await()
        {
            if (!OnWindows) return;
            var name = UniqueName();
            var held = OperationLock.TryAcquireForTest("async", TimeSpan.Zero, name);
            Assert.NotNull(held);
            await Task.Delay(10);
            await Task.Run(() => held.Dispose());
            using (var next = OperationLock.TryAcquireForTest("next", TimeSpan.Zero, name))
            {
                Assert.NotNull(next);
            }
        }

        [Fact]
        public void Dispose_twice_is_safe()
        {
            if (!OnWindows) return;
            var held = OperationLock.TryAcquireForTest("x", TimeSpan.Zero, UniqueName());
            held.Dispose();
            held.Dispose();
        }
    }
}
