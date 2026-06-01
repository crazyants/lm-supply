namespace LMSupply.Embedder.Inference;

/// <summary>
/// Runs synchronous, potentially non-cancellable native inference work on the thread pool while
/// guaranteeing that control returns to the caller as soon as the supplied token is cancelled.
/// </summary>
/// <remarks>
/// ONNX Runtime inference is a blocking native call. <c>Task.Run(work, token)</c> only checks the
/// token before scheduling, so once the native call starts the token is ignored. Wrapping the
/// resulting task in <c>WaitAsync(token)</c> re-projects the same token onto the await, so the
/// caller is unblocked the moment cancellation is requested — even when the underlying thread
/// remains blocked in native code. Best-effort native termination (freeing that thread) is handled
/// separately via ONNX <c>RunOptions.Terminate</c>.
/// </remarks>
internal static class CancellableInference
{
    /// <summary>
    /// Executes <paramref name="work"/> on the thread pool and returns when it completes or when
    /// <paramref name="cancellationToken"/> is cancelled, whichever comes first.
    /// </summary>
    /// <typeparam name="T">Result type produced by the inference delegate.</typeparam>
    /// <param name="work">The synchronous inference delegate to run.</param>
    /// <param name="cancellationToken">Token whose cancellation returns control to the caller.</param>
    /// <returns>The inference result.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the token is cancelled before completion.</exception>
    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        return Task.Run(work, cancellationToken).WaitAsync(cancellationToken);
    }
}
