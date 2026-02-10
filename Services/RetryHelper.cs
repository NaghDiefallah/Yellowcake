using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

internal static class RetryHelper
{
    public static async Task<TResult> RetryAsync<TResult>(
        Func<Task<TResult>> operation,
        int maxAttempts = 4,
        TimeSpan? initialDelay = null,
        CancellationToken ct = default)
    {
        initialDelay ??= TimeSpan.FromMilliseconds(200);
        var delay = initialDelay.Value;
        var rand = new Random();

        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                int jitter = rand.Next(0, 100);
                await Task.Delay(delay + TimeSpan.FromMilliseconds(jitter), ct).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(10).Ticks));
            }
        }
    }
}