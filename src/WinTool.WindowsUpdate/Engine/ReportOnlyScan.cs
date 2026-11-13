using System;

namespace WinTool.WindowsUpdate.Engine
{
    public static class ReportOnlyScan
    {
        /// <summary>Advisory zero-wait probe only. A later operation may overlap the search.</summary>
        public static ScanResult TryRun(Func<IDisposable> probe, Func<ScanResult> search)
        {
            using (var lease = probe())
                if (lease == null) return null;
            return search();
        }
    }
}
