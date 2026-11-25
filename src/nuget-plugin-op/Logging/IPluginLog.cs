using NuGet.Common;

namespace NuGetOpCredentialProvider.Logging;

/// <summary>Diagnostics sink. Never pass secret values to it.</summary>
internal interface IPluginLog
{
    void Log(LogLevel level, string message);
}
