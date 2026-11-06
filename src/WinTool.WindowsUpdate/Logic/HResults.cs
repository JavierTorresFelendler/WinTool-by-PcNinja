using System.Collections.Generic;
using System.Globalization;

namespace WinTool.WindowsUpdate.Logic
{
    /// <summary>Windows Update error codes WinTool explains or retries (WU-60).</summary>
    public static class HResults
    {
        private static readonly Dictionary<uint, string> Friendly = new Dictionary<uint, string>
        {
            { 0x8024402C, "Network, DNS, proxy or firewall problem while contacting Windows Update." },
            { 0x8024401C, "The Windows Update service request timed out." },
            { 0x80244022, "Windows Update is temporarily unavailable or overloaded." },
            { 0x8024001E, "The Windows Update service stopped while the operation was running." },
            { 0x8024000E, "Windows Update data store error." },
            { 0x80072EE2, "Network timeout while contacting an update service." },
            { 0x80072EFD, "Could not connect to the update service." },
            { 0x80072EFE, "The connection to the update service was interrupted." }
        };

        private static readonly HashSet<uint> Retryable = new HashSet<uint>
        {
            0x8024402C, 0x8024401C, 0x80244022, 0x80072EE2, 0x80072EFD, 0x80072EFE
        };

        public static string ToHex(int hresult) => "0x" + ((uint)hresult).ToString("X8", CultureInfo.InvariantCulture);

        public static bool IsRetryable(int hresult) => Retryable.Contains((uint)hresult);

        public static string Explain(int hresult)
        {
            string text;
            return Friendly.TryGetValue((uint)hresult, out text) ? text : null;
        }

        public static string Describe(string message, int hresult)
        {
            if (hresult == 0) return message;
            var explanation = Explain(hresult);
            return explanation == null
                ? message + " (" + ToHex(hresult) + ")"
                : message + " (" + ToHex(hresult) + ") - " + explanation;
        }
    }
}
