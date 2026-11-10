using System;
using System.Security.Principal;
using System.Threading;

namespace WinTool.Core.Operations
{
    /// <summary>
    /// Machine-wide lock for operations that change the computer (install
    /// updates, upgrade, Office install or removal, reset). The GUI, the CLI
    /// and scheduled runs all take it, so two such operations never overlap.
    /// Built on <see cref="OperationGate"/>: a protected, non-thread-affine
    /// semaphore, so the lease may be released after an await on another thread.
    /// Nested acquisition in the same logical flow is allowed.
    /// </summary>
    public sealed class OperationLock : IDisposable
    {
        private OperationLease lease;

        private OperationLock(OperationLease lease, string operation)
        {
            this.lease = lease;
            Operation = operation;
        }

        public string Operation { get; }

        /// <summary>Returns the held lock, or null when another operation still holds it after <paramref name="timeout"/>.</summary>
        public static OperationLock TryAcquire(string operation, TimeSpan timeout) =>
            TryAcquire(operation, timeout, () => OperationGate.EnterWindows());

        /// <summary>Tests only: a unique Local gate with a current-user ACL; the production gate is never touched.</summary>
        internal static OperationLock TryAcquireForTest(string operation, TimeSpan timeout, string testGateName)
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var sid = identity.User.Value;
                return TryAcquire(operation, timeout, () => new OperationGate.TestEndpoint(testGateName, sid).EnterWindows());
            }
        }

        private static OperationLock TryAcquire(string operation, TimeSpan timeout, Func<OperationLease> enter)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                try
                {
                    return new OperationLock(enter(), operation);
                }
                catch (OperationBusyException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(100);
                }
                catch (OperationBusyException)
                {
                    return null;
                }
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref lease, null)?.Dispose();
        }
    }
}
