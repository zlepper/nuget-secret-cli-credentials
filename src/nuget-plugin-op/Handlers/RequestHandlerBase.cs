using NuGet.Common;
using NuGet.Protocol.Plugins;
using NuGetOpCredentialProvider.Logging;

namespace NuGetOpCredentialProvider.Handlers;

/// <summary>
/// Deserializes a request, runs <see cref="HandleRequestAsync"/> and sends the response. Payloads are never logged,
/// because <c>SetCredentials</c> carries passwords.
/// </summary>
internal abstract class RequestHandlerBase<TRequest, TResponse>(IPluginLog log) : IRequestHandler
    where TRequest : class
    where TResponse : class
{
    public CancellationToken CancellationToken => CancellationToken.None;

    public async Task HandleResponseAsync(
        IConnection connection,
        Message message,
        IResponseHandler responseHandler,
        CancellationToken cancellationToken)
    {
        var request = MessageUtilities.DeserializePayload<TRequest>(message)
                      ?? throw new ProtocolException($"{message.Method} request {message.RequestId} has no payload.");

        TResponse response;
        using (CreateProgressReporter(connection, message, cancellationToken))
        {
            response = await HandleRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }

        log.Log(LogLevel.Debug, $"Responding to {message.Method} request {message.RequestId}.");
        await responseHandler.SendResponseAsync(message, response, cancellationToken).ConfigureAwait(false);
    }

    public abstract Task<TResponse> HandleRequestAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>Override to keep long-running requests alive with progress messages.</summary>
    protected virtual IDisposable? CreateProgressReporter(IConnection connection, Message message, CancellationToken cancellationToken) => null;
}
