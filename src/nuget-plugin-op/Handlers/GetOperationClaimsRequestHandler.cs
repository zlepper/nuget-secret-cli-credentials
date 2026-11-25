using NuGet.Protocol.Plugins;
using NuGetOpCredentialProvider.Config;

namespace NuGetOpCredentialProvider.Handlers;

internal sealed class GetOperationClaimsRequestHandler(PluginContext context)
    : RequestHandlerBase<GetOperationClaimsRequest, GetOperationClaimsResponse>(context.Log)
{
    private static readonly GetOperationClaimsResponse Authentication = new([OperationClaim.Authentication]);
    private static readonly GetOperationClaimsResponse Nothing = new([]);

    public override Task<GetOperationClaimsResponse> HandleRequestAsync(GetOperationClaimsRequest request, CancellationToken cancellationToken)
    {
        context.LogConfigIssuesOnce();

        if (context.Credentials is not { } credentials || credentials.Config.Sources.Count == 0)
        {
            return Task.FromResult(Nothing);
        }

        // A source-agnostic query (no source, no service index) asks whether the plugin authenticates at all.
        if (request.PackageSourceRepository is null && request.ServiceIndexJson is null)
        {
            return Task.FromResult(Authentication);
        }

        var claimed = SourceMatcher.Match(credentials.Config.Sources, request.PackageSourceRepository) is not null;
        return Task.FromResult(claimed ? Authentication : Nothing);
    }
}
