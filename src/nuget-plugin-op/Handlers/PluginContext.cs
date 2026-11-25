using NuGet.Common;
using NuGetOpCredentialProvider.Config;
using NuGetOpCredentialProvider.Credentials;
using NuGetOpCredentialProvider.Logging;

namespace NuGetOpCredentialProvider.Handlers;

/// <summary>State shared by the request handlers.</summary>
internal sealed class PluginContext(ConfigLoadResult configLoad, CredentialService? credentials, IPluginLog log)
{
    private int _configIssuesLogged;

    /// <summary>Null when the config could not be loaded; the plugin then claims nothing.</summary>
    public CredentialService? Credentials => credentials;

    public IPluginLog Log => log;

    /// <summary>Logs config errors and warnings once per process, after NuGet's log channel is available.</summary>
    public void LogConfigIssuesOnce()
    {
        if (Interlocked.Exchange(ref _configIssuesLogged, 1) == 1)
        {
            return;
        }

        if (configLoad.Error is { } error)
        {
            log.Log(LogLevel.Warning, error);
        }

        foreach (var warning in configLoad.Warnings)
        {
            log.Log(LogLevel.Warning, warning);
        }
    }

    public static PluginContext Create(ConfigLoadResult configLoad, IPluginLog log, string? pathVariable)
    {
        CredentialService? credentials = null;
        if (configLoad.Config is { } config)
        {
            var op = new Secrets.OpCli(config, log, pathVariable);
            var gh = new Secrets.GhCli(config, log, pathVariable);
            credentials = new CredentialService(config, new Secrets.SecretResolver(op, gh), op, log);
        }

        return new PluginContext(configLoad, credentials, log);
    }
}
