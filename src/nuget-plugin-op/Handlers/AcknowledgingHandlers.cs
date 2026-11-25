using NuGet.Protocol.Plugins;
using NuGetOpCredentialProvider.Logging;

namespace NuGetOpCredentialProvider.Handlers;

internal sealed class InitializeRequestHandler(IPluginLog log) : RequestHandlerBase<InitializeRequest, InitializeResponse>(log)
{
    public override Task<InitializeResponse> HandleRequestAsync(InitializeRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new InitializeResponse(MessageResponseCode.Success));
}

internal sealed class SetLogLevelRequestHandler(PluginLog pluginLog) : RequestHandlerBase<SetLogLevelRequest, SetLogLevelResponse>(pluginLog)
{
    public override Task<SetLogLevelResponse> HandleRequestAsync(SetLogLevelRequest request, CancellationToken cancellationToken)
    {
        pluginLog.SetLevel(request.LogLevel);
        return Task.FromResult(new SetLogLevelResponse(MessageResponseCode.Success));
    }
}

/// <summary>NuGet passes proxy and source credentials it already has. The plugin has no use for them.</summary>
internal sealed class SetCredentialsRequestHandler(IPluginLog log) : RequestHandlerBase<SetCredentialsRequest, SetCredentialsResponse>(log)
{
    public override Task<SetCredentialsResponse> HandleRequestAsync(SetCredentialsRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new SetCredentialsResponse(MessageResponseCode.Success));
}
