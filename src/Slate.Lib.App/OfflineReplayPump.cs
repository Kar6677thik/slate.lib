using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class OfflineReplayPump
{
    private static CancellationTokenSource? lifetime;
    internal static void Start(ClientStateStore state, LibraryApiClient api)
    {
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = new();
        var token = lifetime.Token;
        _ = Task.Run(() => RunAsync(state, api, token), token);
    }
    private static async Task RunAsync(ClientStateStore state, LibraryApiClient api, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    if ((await state.Offline.Queue.ReadAsync()).Any(x => x.State is "pending" or "failed" or "sending")) await state.ReconcileOfflineAsync(api, token);
                }
                catch (Exception e) when (e is not OutOfMemoryException && !token.IsCancellationRequested) { /* Durable queue retains retry/conflict details; connection will be checked on the next bounded interval. */ }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
