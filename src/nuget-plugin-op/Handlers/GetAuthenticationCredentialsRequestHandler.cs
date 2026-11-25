using NuGet.Protocol.Plugins;

namespace NuGetOpCredentialProvider.Handlers;

internal sealed class GetAuthenticationCredentialsRequestHandler(PluginContext context, TimeSpan progressInterval)
    : RequestHandlerBase<GetAuthenticationCredentialsRequest, GetAuthenticationCredentialsResponse>(context.Log)
{
    public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(2);

    public override async Task<GetAuthenticationCredentialsResponse> HandleRequestAsync(
        GetAuthenticationCredentialsRequest request,
        CancellationToken cancellationToken)
    {
        context.LogConfigIssuesOnce();

        if (context.Credentials is not { } credentials)
        {
            return NotFound("nuget-plugin-op has no usable configuration; see the earlier warning.");
        }

        var result = await credentials
            .GetAsync(request.Uri, request.IsRetry, request.IsNonInteractive, cancellationToken)
            .ConfigureAwait(false);

        if (result.Credential is not { } credential)
        {
            return NotFound(result.Message);
        }

        return new GetAuthenticationCredentialsResponse(
            credential.Username.Reveal(),
            credential.Password.Reveal(),
            result.Message,
            ["Basic"],
            MessageResponseCode.Success);
    }

    protected override IDisposable CreateProgressReporter(IConnection connection, Message message, CancellationToken cancellationToken) =>
        AutomaticProgressReporter.Create(connection, message, progressInterval, cancellationToken);

    private static GetAuthenticationCredentialsResponse NotFound(string message) =>
        new(username: null, password: null, message, authenticationTypes: null, MessageResponseCode.NotFound);
}
