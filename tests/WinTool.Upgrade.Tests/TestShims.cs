using System.Security.Cryptography;

namespace WinTool.Upgrade.Tests;

/// <summary>.NET 8 helpers the ported checks use, available on .NET Framework 4.8 too.</summary>
internal static class TestShims
{
    public static byte[] Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    /// <summary>array[start..(start + length)] without the .NET Core RuntimeHelpers.GetSubArray.</summary>
    public static byte[] Slice(byte[] array, int start, int length)
    {
        var result = new byte[length];
        Array.Copy(array, start, result, 0, length);
        return result;
    }

#if NETFRAMEWORK
    public static async Task WaitAsync(this Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task) throw new TimeoutException();
        await task.ConfigureAwait(false);
    }

    public static async Task WaitAsync(this Task task, CancellationToken token)
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (token.Register(() => cancelled.TrySetCanceled(token)))
            await (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false)).ConfigureAwait(false);
    }
#endif
}
