#if NETFRAMEWORK
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WinTool.Upgrade;

/// <summary>.NET Framework 4.8 lacks the token overloads used by the ported code.</summary>
internal static class HttpPolyfills
{
    public static async Task<Stream> ReadAsStreamAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await content.ReadAsStreamAsync().ConfigureAwait(false);
    }

    public static async Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await content.ReadAsStringAsync().ConfigureAwait(false);
    }
}
#endif
