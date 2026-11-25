using NuGet.Common;
using NuGetSecretCliCredentials.Logging;

namespace NuGetSecretCliCredentials.Tests;

public sealed class PluginLogTests
{
    [Test]
    public void Before_SetLogLevel_only_warnings_and_errors_reach_stderr()
    {
        // NuGet doesn't redirect the plugin's stderr, so this is what the user sees in the terminal.
        var standardError = new StringWriter();
        var log = new PluginLog(standardError);

        log.Log(LogLevel.Debug, "debug message");
        log.Log(LogLevel.Verbose, "verbose message");
        log.Log(LogLevel.Information, "information message");
        log.Log(LogLevel.Warning, "warning message");
        log.Log(LogLevel.Error, "error message");

        var written = standardError.ToString();
        Assert.That(written, Does.Not.Contain("debug message").And.Not.Contain("verbose message").And.Not.Contain("information message"));
        Assert.That(written, Does.Contain("warning message").And.Contain("error message"));
    }
}
