using System.Diagnostics;
using System.Text.Json.Nodes;
using NuGetSecretCliCredentials.Tests.Support;

namespace NuGetSecretCliCredentials.Tests;

/// <summary>
/// Runs a real <c>dotnet restore</c> against a feed that requires basic auth, with a NuGet.Config that has no
/// credentials. The restore only succeeds if NuGet gets the credentials from the plugin.
/// </summary>
[NonParallelizable]
public sealed class EndToEndTests
{
    private const string Username = "octocat";
    private const string Token = "e2e-feed-token-1f9c";

    private TempDirectory _directory = null!;

    [SetUp]
    public void SetUp() => _directory = new TempDirectory();

    [TearDown]
    public void TearDown() => _directory.Dispose();

    [TestCase("op", "op://Private/Test Feed/token")]
    [TestCase("gh", "gh://github.com")]
    public async Task Restore_gets_credentials_from_the_plugin(string toolName, string passwordReference)
    {
        using var feed = new TestFeed(Username, Token, _directory);
        var tool = FakeTool.Create(_directory, toolName, FakeBehavior.Prints(Token));

        var configHome = _directory.Combine("config");
        TestConfig.Write(configHome, new JsonObject
        {
            [$"{toolName}Path"] = tool.Path,
            ["allowInsecureHttpForTesting"] = true,
            ["sources"] = new JsonArray(TestConfig.Source(feed.BaseUri, Username, passwordReference)),
        });

        var projectDirectory = WriteProject(feed);

        var (exitCode, output) = await RunRestoreAsync(projectDirectory, configHome);

        Assert.That(exitCode, Is.Zero, output);
        Assert.That(Directory.Exists(Path.Combine(_directory.Combine("packages"), TestFeed.PackageId.ToLowerInvariant(), TestFeed.PackageVersion)),
            "the package landed in the global packages folder");
        Assert.That(feed.Requests.Any(request => request.Authorized), "the feed saw the plugin's credentials");
        Assert.That(tool.Invocations, Is.Not.Empty, $"the plugin ran {toolName}");
        Assert.That(output, Does.Not.Contain(Token), "the token leaked into the restore output");
    }

    [Test]
    public async Task Restore_fails_when_the_plugin_has_no_config()
    {
        using var feed = new TestFeed(Username, Token, _directory);
        var projectDirectory = WriteProject(feed);

        var (exitCode, output) = await RunRestoreAsync(projectDirectory, _directory.Combine("empty-config"));

        Assert.That(exitCode, Is.Not.Zero, output);
        Assert.That(feed.Requests.Any(request => request.Authorized), Is.False);
    }

    private string WriteProject(TestFeed feed)
    {
        _directory.WriteFile("project/NuGet.Config", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="test-feed" value="{feed.ServiceIndex}" allowInsecureConnections="true" />
              </packageSources>
            </configuration>
            """);
        _directory.WriteFile("project/Consumer.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{TestFeed.PackageId}" Version="{TestFeed.PackageVersion}" />
              </ItemGroup>
            </Project>
            """);
        return _directory.Combine("project");
    }

    private async Task<(int ExitCode, string Output)> RunRestoreAsync(string projectDirectory, string configHome)
    {
        var startInfo = new ProcessStartInfo(PluginLocation.DotnetHost)
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--disable-build-servers", "-v", "detailed" },
            Environment =
            {
                ["NUGET_NETCORE_PLUGIN_PATHS"] = PluginLocation.DllPath,
                ["NUGET_PLUGINS_CACHE_PATH"] = _directory.Combine("plugins-cache"),
                ["NUGET_PACKAGES"] = _directory.Combine("packages"),
                ["NUGET_HTTP_CACHE_PATH"] = _directory.Combine("http-cache"),
                ["XDG_CONFIG_HOME"] = configHome,
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
            },
        };

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, await stdout + await stderr);
    }
}
