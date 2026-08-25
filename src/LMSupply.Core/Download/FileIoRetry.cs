namespace LMSupply.Core.Download;

/// <summary>
/// Bounded retry-with-backoff for file operations that can fail with a transient
/// <see cref="IOException"/> because another actor briefly holds an exclusive handle on the
/// same path — a second process racing to write the same ".part" file, a real-time antivirus
/// scanner opening a file immediately after it was renamed into place, or a reader opening a
/// model file in the narrow window right after a download's completion rename.
/// </summary>
public static class FileIoRetry
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying on <see cref="IOException"/> with exponential
    /// backoff. The exception from the final attempt propagates unchanged if every attempt fails.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<T> operation, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(
                    InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc cref="ExecuteAsync{T}(Func{T}, CancellationToken)"/>
    public static Task ExecuteAsync(Action operation, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () =>
            {
                operation();
                return true;
            },
            cancellationToken);

    /// <inheritdoc cref="ExecuteAsync{T}(Func{T}, CancellationToken)"/>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(
                    InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc cref="ExecuteAsync{T}(Func{T}, CancellationToken)"/>
    public static async Task ExecuteAsync(
        Func<Task> operation, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation().ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(
                    InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
