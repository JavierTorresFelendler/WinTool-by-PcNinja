using System;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Logic
{
    /// <summary>WU-61: first delay x multiplier^(n-1), at most 360 minutes.</summary>
    public static class RetryPolicy
    {
        public const int MaximumDelayMinutes = 360;

        public static int DelayMinutes(WindowsUpdateSettings settings, int retryNumber)
        {
            var baseDelay = Math.Max(1, settings.RetryInitialDelayMinutes);
            var multiplier = Math.Max(1, settings.RetryBackoffMultiplier);
            var power = Math.Max(0, retryNumber - 1);
            var delay = baseDelay * Math.Pow(multiplier, power);
            return delay >= MaximumDelayMinutes ? MaximumDelayMinutes : (int)delay;
        }
    }
}
