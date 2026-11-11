using System;
using System.Diagnostics;
using System.Threading;
using WinTool.Core.Operations;

namespace WinTool.App.Infrastructure
{
    internal static class TaskMaintenance
    {
        internal static IDisposable WaitForOperation(TimeSpan timeout)
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try { return OperationGate.EnterWindows(); }
                catch (OperationBusyException) when (timer.Elapsed < timeout) { Thread.Sleep(100); }
            }
        }
    }
}
