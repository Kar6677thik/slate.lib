namespace Slate.Lib.Mcp.Clients;

internal static class BoundedHttpContent
{
    public static async Task<byte[]> ReadAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes) throw new UpstreamResponseTooLargeException();
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new UpstreamResponseTooLargeException();
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.ToArray();
    }
}

public sealed class UpstreamResponseTooLargeException : Exception;

public sealed class SlateUpstreamException(int statusCode, string code, string message, bool retryable = false) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
