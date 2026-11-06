using System;
using System.Threading;
using WinTool.Core.Operations;
using Xunit;

namespace WinTool.Core.Tests
{
    public class OperationLockTests
    {
        private static string UniqueName() => @"Local\WinTool.Tests." + Guid.NewGuid().ToString("N");

        /// <summary>Mutexes belong to a thread, so each competitor runs on its own dedicated thread.</summary>
        private static T OnNewThread<T>(Func<T> work)
        {
            var result = default(T);
            var thread = new Thread(() => result = work());
            thread.Start();
            thread.Join();
            return result;
        }

        [Fact]
        public void Second_holder_is_refused_while_the_first_holds_the_lock()
        {
            var name = UniqueName();
            using (var first = OperationLock.TryAcquire("install updates", TimeSpan.Zero, name))
            {
                Assert.NotNull(first);
                var refused = OnNewThread(() =>
                {
                    var second = OperationLock.TryAcquire("upgrade", TimeSpan.FromMilliseconds(100), name);
                    second?.Dispose();
                    return second == null;
                });
                Assert.True(refused);
            }
        }

        [Fact]
        public void Lock_is_available_again_after_release()
        {
            var name = UniqueName();
            OperationLock.TryAcquire("first", TimeSpan.Zero, name).Dispose();
            var acquired = OnNewThread(() =>
            {
                using (var again = OperationLock.TryAcquire("second", TimeSpan.Zero, name))
                {
                    return again != null;
                }
            });
            Assert.True(acquired);
        }

        [Fact]
        public void Abandoned_lock_is_taken_over()
        {
            var name = UniqueName();
            // A holder thread that ends without releasing simulates a crashed process.
            OnNewThread(() => { OperationLock.TryAcquire("crashed", TimeSpan.Zero, name); return true; });
            using (var taken = OperationLock.TryAcquire("next", TimeSpan.FromSeconds(1), name))
            {
                Assert.NotNull(taken);
            }
        }

        [Fact]
        public void Dispose_twice_is_safe()
        {
            var held = OperationLock.TryAcquire("x", TimeSpan.Zero, UniqueName());
            held.Dispose();
            held.Dispose();
        }
    }
}
