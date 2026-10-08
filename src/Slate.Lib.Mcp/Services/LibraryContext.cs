using Microsoft.Extensions.Options;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Configuration;

namespace Slate.Lib.Mcp.Services;

public sealed class LibraryContext(CanonicalSlateClient canonical, IOptions<SlateMcpOptions> options)
{
    private readonly SlateMcpOptions settings = options.Value;

    public async Task<LibraryStatus> Status(CancellationToken cancellationToken)
    {
        var status = await canonical.Status(cancellationToken);
        if (settings.ExpectedLibraryId is { } expected && status.LibraryId != expected)
            throw new SlateUpstreamException(502, "library_identity_mismatch", "The canonical API returned a different Slate library identity.");
        return status;
    }
}
