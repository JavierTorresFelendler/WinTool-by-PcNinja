using System;
using System.Threading;

namespace WinTool.Core.Operations
{
    /// <summary>
    /// Machine-wide lock for operations that change the computer (install
    /// updates, upgrade, Office install or removal, reset). The GUI, the CLI
    /// and scheduled runs all take it, so two such operations never overlap.
    /// </summary>
    public sealed class OperationLock : IDisposable
    {
        public const string DefaultName = @"Global\PcNinja.WinTool.Operation.v1";

        private Mutex mutex;

        private OperationLock(Mutex mutex, string operation)
        {
            this.mutex = mutex;
            Operation = operation;
        }

        public string Operation { get; }

        /// <summary>Returns the held lock, or null when another operation holds it after <paramref name="timeout"/>.</summary>
        public static OperationLock TryAcquire(string operation, TimeSpan timeout, string name = DefaultName)
        {
            var candidate = new Mutex(false, name);
            bool acquired;
            try
            {
                acquired = candidate.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // The previous holder crashed. The lock is ours now.
                acquired = true;
            }

            if (!acquired)
            {
                candidate.Dispose();
                return null;
            }

            return new OperationLock(candidate, operation);
        }

        public void Dispose()
        {
            var held = Interlocked.Exchange(ref mutex, null);
            if (held == null) return;
            try
            {
                held.ReleaseMutex();
            }
            finally
            {
                held.Dispose();
            }
        }
    }
}
