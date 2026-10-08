using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Models;

namespace Slate.Lib.Mcp.Services;

public sealed class ToolExecutor(LibraryContext library, ILogger<ToolExecutor> logger)
{
    public async Task<McpResult<T>> Run<T>(string operation, Func<Guid, Task<McpResult<T>>> action, CancellationToken cancellationToken)
    {
        Guid? libraryId = null;
        try
        {
            libraryId = (await library.Status(cancellationToken)).LibraryId;
            return await action(libraryId.Value);
        }
        catch (SlateUpstreamException exception)
        {
            logger.LogWarning("Slate MCP operation {Operation} failed with {Code} and status {StatusCode}", operation, exception.Code, exception.StatusCode);
            return McpResult<T>.Fail(libraryId, operation, exception.Code, exception.Message, exception.Retryable);
        }
        catch (UpstreamResponseTooLargeException)
        {
            logger.LogWarning("Slate MCP operation {Operation} exceeded the upstream response bound", operation);
            return McpResult<T>.Fail(libraryId, operation, "response_too_large", "The upstream response exceeded the MCP safety limit.");
        }
        catch (ArgumentException exception)
        {
            return McpResult<T>.Fail(libraryId, operation, "invalid_request", exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return McpResult<T>.Fail(libraryId, operation, "cancelled", "The operation was cancelled.", true);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Slate MCP upstream request failed for {Operation}", operation);
            return McpResult<T>.Fail(libraryId, operation, "upstream_unavailable", "A required Slate service is unavailable.", true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slate MCP operation {Operation} failed", operation);
            return McpResult<T>.Fail(libraryId, operation, "internal_error", "The MCP service could not complete the operation.", true);
        }
    }
}
