using System.Diagnostics;
using System.Text.Json.Nodes;
using NuGet.Protocol.Plugins;
using NuGetSecretCliCredentials.Tests.Support;

namespace NuGetSecretCliCredentials.Tests;

/// <summary>Runs the built plugin with <c>-Plugin</c> and talks to it over stdin/stdout like NuGet does.</summary>
public sealed class ProtocolOutOfProcessTests
{
    private const string Feed = "https://nuget.pkg.github.com/acme/";
    private const string Token = "out-of-process-token";

    private TempDirectory _directory = null!;

    [SetUp]
    public void SetUp() => _directory = new TempDirectory();

    [TearDown]
    public void TearDown() => _directory.Dispose();

    [Test]
    public async Task Handshake_claims_credentials_and_close()
    {
        var op = FakeTool.Create(_directory, "op", FakeBehavior.Prints(Token));
        await using var client = await StartAsync(op, TimeSpan.FromSeconds(5));
        await client.InitializeAsync();

        var claims = await client.SendAsync<GetOperationClaimsRequest, GetOperationClaimsResponse>(
            MessageMethod.GetOperationClaims, new GetOperationClaimsRequest(Feed + "index.json", serviceIndexJson: null));
        var noClaims = await client.SendAsync<GetOperationClaimsRequest, GetOperationClaimsResponse>(
            MessageMethod.GetOperationClaims, new GetOperationClaimsRequest("https://api.nuget.org/v3/index.json", serviceIndexJson: null));
        var credentials = await client.SendAsync<GetAuthenticationCredentialsRequest, GetAuthenticationCredentialsResponse>(
            MessageMethod.GetAuthenticationCredentials, new GetAuthenticationCredentialsRequest(new Uri(Feed + "index.json"), false, true, false));
        var notFound = await client.SendAsync<GetAuthenticationCredentialsRequest, GetAuthenticationCredentialsResponse>(
            MessageMethod.GetAuthenticationCredentials, new GetAuthenticationCredentialsRequest(new Uri("https://api.nuget.org/v3/index.json"), false, true, false));

        Assert.That(claims.Claims, Is.EqualTo(new[] { OperationClaim.Authentication }));
        Assert.That(noClaims.Claims, Is.Empty);
        Assert.That(credentials.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(credentials.Username, Is.EqualTo("octocat"));
        Assert.That(credentials.Password, Is.EqualTo(Token));
        Assert.That(notFound.ResponseCode, Is.EqualTo(MessageResponseCode.NotFound));

        var stopwatch = Stopwatch.StartNew();
        Assert.That(await client.CloseAsync(TimeSpan.FromSeconds(10)), Is.True, "the plugin exits after Close");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(client.Process.ExitCode, Is.Zero);

        Assert.That(client.LogMessages, Has.Some.Contains("Read credentials for " + Feed), "logs travel over the protocol");
        AssertTokenAbsent(client);
    }

    [Test]
    public async Task A_slow_op_survives_the_request_timeout_through_progress_messages()
    {
        var op = FakeTool.Create(_directory, "op", FakeBehavior.Prints(Token) with { SleepSeconds = 7 });
        await using var client = await StartAsync(op, requestTimeout: TimeSpan.FromSeconds(5));
        await client.InitializeAsync();

        var stopwatch = Stopwatch.StartNew();
        var credentials = await client.SendAsync<GetAuthenticationCredentialsRequest, GetAuthenticationCredentialsResponse>(
            MessageMethod.GetAuthenticationCredentials, new GetAuthenticationCredentialsRequest(new Uri(Feed), false, false, true));

        Assert.That(stopwatch.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(5)), "the request outlived NuGet's timeout");
        Assert.That(credentials.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(credentials.Password, Is.EqualTo(Token));
        AssertTokenAbsent(client);
    }

    [Test]
    public async Task A_missing_config_claims_nothing_and_logs_why()
    {
        await using var client = await PluginClient.StartAsync(
            new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = _directory.Path },
            TimeSpan.FromSeconds(5));
        await client.InitializeAsync();

        var claims = await client.SendAsync<GetOperationClaimsRequest, GetOperationClaimsResponse>(
            MessageMethod.GetOperationClaims, new GetOperationClaimsRequest(null, serviceIndexJson: null));

        Assert.That(claims.Claims, Is.Empty);
        Assert.That(await client.CloseAsync(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(client.LogMessages, Has.Some.Contains("does not exist"));
    }

    [Test]
    public async Task Without_the_plugin_flag_it_prints_usage_and_exits()
    {
        var startInfo = new ProcessStartInfo(PluginLocation.DotnetHost)
        {
            ArgumentList = { PluginLocation.DllPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.That(process.ExitCode, Is.EqualTo(1));
        Assert.That(stdout, Is.Empty);
        Assert.That(stderr, Does.Contain("-Plugin"));
    }

    private Task<PluginClient> StartAsync(FakeTool op, TimeSpan requestTimeout)
    {
        TestConfig.Write(_directory.Path, new JsonObject
        {
            ["opPath"] = op.Path,
            ["sources"] = new JsonArray(TestConfig.Source(Feed, "octocat", "op://Private/GitHub Packages/token")),
        });
        return PluginClient.StartAsync(new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = _directory.Path }, requestTimeout);
    }

    private static void AssertTokenAbsent(PluginClient client)
    {
        Assert.That(string.Join('\n', client.LogMessages), Does.Not.Contain(Token));
        Assert.That(client.StandardError, Does.Not.Contain(Token));
    }
}
