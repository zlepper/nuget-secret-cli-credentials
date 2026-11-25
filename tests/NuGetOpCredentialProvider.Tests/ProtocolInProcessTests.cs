using System.Text.Json.Nodes;
using NuGet.Protocol.Plugins;
using NuGetOpCredentialProvider.Config;
using NuGetOpCredentialProvider.Credentials;
using NuGetOpCredentialProvider.Handlers;
using NuGetOpCredentialProvider.Tests.Support;

namespace NuGetOpCredentialProvider.Tests;

/// <summary>Drives the request handlers with NuGet.Protocol's own request types, against fake <c>op</c> and <c>gh</c>.</summary>
public sealed class ProtocolInProcessTests
{
    private const string Feed = "https://nuget.pkg.github.com/acme/";
    private const string GhFeed = "https://nuget.pkg.github.com/other/";
    private const string OpToken = "op-token-value";
    private const string GhToken = "gh-token-value";

    private TempDirectory _directory = null!;
    private RecordingLog _log = null!;
    private FakeTool _op = null!;
    private FakeTool _gh = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = new TempDirectory();
        _log = new RecordingLog();
        _op = FakeTool.Create(_directory, "op", FakeBehavior.Prints(OpToken));
        _gh = FakeTool.Create(_directory, "gh", FakeBehavior.Prints(GhToken + "\n"));
    }

    [TearDown]
    public void TearDown()
    {
        Assert.That(_log.AllText, Does.Not.Contain(OpToken).And.Not.Contain(GhToken));
        _directory.Dispose();
    }

    [Test]
    public async Task Claims_authentication_for_a_configured_source_only()
    {
        var handler = new GetOperationClaimsRequestHandler(Context());

        var matching = await handler.HandleRequestAsync(new GetOperationClaimsRequest(Feed + "index.json", serviceIndexJson: null), default);
        var unrelated = await handler.HandleRequestAsync(new GetOperationClaimsRequest("https://api.nuget.org/v3/index.json", serviceIndexJson: null), default);

        Assert.That(matching!.Claims, Is.EqualTo(new[] { OperationClaim.Authentication }));
        Assert.That(unrelated!.Claims, Is.Empty);
    }

    [Test]
    public async Task Claims_authentication_for_a_source_agnostic_query()
    {
        var handler = new GetOperationClaimsRequestHandler(Context());

        var response = await handler.HandleRequestAsync(new GetOperationClaimsRequest(packageSourceRepository: null, serviceIndexJson: null), default);

        Assert.That(response.Claims, Is.EqualTo(new[] { OperationClaim.Authentication }));
    }

    [Test]
    public async Task Claims_nothing_without_a_usable_config_and_logs_why_once()
    {
        var context = PluginContext.Create(ConfigLoader.Load(_directory.Combine("missing.json")), _log, pathVariable: null);
        var handler = new GetOperationClaimsRequestHandler(context);

        var agnostic = await handler.HandleRequestAsync(new GetOperationClaimsRequest(null, serviceIndexJson: null), default);
        var specific = await handler.HandleRequestAsync(new GetOperationClaimsRequest(Feed, serviceIndexJson: null), default);

        Assert.That(agnostic.Claims, Is.Empty);
        Assert.That(specific.Claims, Is.Empty);
        Assert.That(_log.Entries.Count(entry => entry.Message.Contains("does not exist")), Is.EqualTo(1));
    }

    [Test]
    public async Task Returns_op_credentials_for_a_matching_uri()
    {
        var handler = CredentialsHandler(Context());

        var response = await handler.HandleRequestAsync(Request(Feed + "download/x/index.json"), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(response.Username, Is.EqualTo("octocat"));
        Assert.That(response.Password, Is.EqualTo(OpToken));
        Assert.That(response.AuthenticationTypes, Is.EqualTo(new[] { "Basic" }));
        Assert.That(response.Message, Does.Not.Contain(OpToken));
    }

    [Test]
    public async Task Returns_gh_credentials_for_a_gh_source()
    {
        var handler = CredentialsHandler(Context());

        var response = await handler.HandleRequestAsync(Request(GhFeed + "index.json"), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(response.Password, Is.EqualTo(GhToken));
        Assert.That(_op.Invocations, Is.Empty);
    }

    [Test]
    public async Task Resolves_a_username_reference_too()
    {
        var handler = CredentialsHandler(Context(source: TestConfig.Source(Feed, "op://v/user", "op://v/token")));

        var response = await handler.HandleRequestAsync(Request(Feed), default);

        Assert.That(response.Username, Is.EqualTo(OpToken));
        Assert.That(_op.Invocations.Select(args => args.Last()), Is.EqualTo(new[] { "op://v/user", "op://v/token" }));
    }

    [Test]
    public async Task Answers_not_found_for_an_unrelated_uri_without_running_op()
    {
        var handler = CredentialsHandler(Context());

        var response = await handler.HandleRequestAsync(Request("https://api.nuget.org/v3/index.json"), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.NotFound));
        Assert.That(response.Password, Is.Null);
        Assert.That(_op.Invocations, Is.Empty);
    }

    [Test]
    public async Task Caches_within_the_process_and_rereads_on_retry()
    {
        var handler = CredentialsHandler(Context());

        await handler.HandleRequestAsync(Request(Feed + "a"), default);
        await handler.HandleRequestAsync(Request(Feed + "b"), default);
        Assert.That(_op.Invocations, Has.Count.EqualTo(1), "the second request is served from the cache");

        var retried = await handler.HandleRequestAsync(Request(Feed + "a", isRetry: true), default);
        Assert.That(_op.Invocations, Has.Count.EqualTo(2), "a retry drops the cached value");
        Assert.That(retried.Password, Is.EqualTo(OpToken));
    }

    [Test]
    public async Task Concurrent_requests_for_one_source_share_a_single_op_read()
    {
        using var slow = new TempDirectory();
        var slowOp = FakeTool.Create(slow, "op", FakeBehavior.Prints(OpToken) with { SleepSeconds = 1 });
        var handler = CredentialsHandler(Context(opPath: slowOp.Path));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => handler.HandleRequestAsync(Request(Feed + i), default)));

        Assert.That(responses.Select(r => r.Password), Is.All.EqualTo(OpToken));
        Assert.That(slowOp.Invocations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Op_failure_answers_not_found_with_the_reason()
    {
        using var failing = new TempDirectory();
        var failingOp = FakeTool.Create(failing, "op", FakeBehavior.Fails("[ERROR] account is not signed in"));
        var handler = CredentialsHandler(Context(opPath: failingOp.Path));

        var response = await handler.HandleRequestAsync(Request(Feed), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.NotFound));
        Assert.That(response.Message, Does.Contain("not signed in"));
    }

    [Test]
    public async Task Prompt_mode_reads_even_when_non_interactive()
    {
        var handler = CredentialsHandler(Context(nonInteractive: "prompt"));

        var response = await handler.HandleRequestAsync(Request(Feed, isNonInteractive: true), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(_op.Invocations.Select(args => args[0]), Is.EqualTo(new[] { "read" }), "no whoami check in prompt mode");
    }

    [Test]
    public async Task Skip_mode_answers_not_found_without_reading_when_op_is_locked()
    {
        using var lockedDirectory = new TempDirectory();
        var locked = FakeTool.Create(lockedDirectory, "op", FakeBehavior.Prints(OpToken) with { WhoAmIExitCode = 1 });
        var handler = CredentialsHandler(Context(opPath: locked.Path, nonInteractive: "skip"));

        var response = await handler.HandleRequestAsync(Request(Feed, isNonInteractive: true), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.NotFound));
        Assert.That(response.Message, Is.EqualTo(CredentialService.LockedMessage));
        Assert.That(locked.Invocations.Select(args => args[0]), Is.EqualTo(new[] { "whoami" }));
        Assert.That(_log.AllText, Does.Contain("1Password is locked"));
    }

    [Test]
    public async Task Skip_mode_reads_when_op_is_already_authorized()
    {
        var handler = CredentialsHandler(Context(nonInteractive: "skip"));

        var response = await handler.HandleRequestAsync(Request(Feed, isNonInteractive: true), default);

        Assert.That(response.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(_op.Invocations.Select(args => args[0]), Is.EqualTo(new[] { "whoami", "read" }));
    }

    [Test]
    public async Task Skip_mode_does_not_apply_to_interactive_requests_or_gh_sources()
    {
        using var lockedDirectory = new TempDirectory();
        var locked = FakeTool.Create(lockedDirectory, "op", FakeBehavior.Prints(OpToken) with { WhoAmIExitCode = 1 });
        var handler = CredentialsHandler(Context(opPath: locked.Path, nonInteractive: "skip"));

        var interactive = await handler.HandleRequestAsync(Request(Feed, isNonInteractive: false), default);
        var gh = await handler.HandleRequestAsync(Request(GhFeed, isNonInteractive: true), default);

        Assert.That(interactive.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(gh.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
        Assert.That(locked.Invocations.Select(args => args[0]), Is.EqualTo(new[] { "read" }));
    }

    private PluginContext Context(string? opPath = null, string nonInteractive = "prompt", JsonObject? source = null)
    {
        var config = new JsonObject
        {
            ["opPath"] = opPath ?? _op.Path,
            ["ghPath"] = _gh.Path,
            ["nonInteractive"] = nonInteractive,
            ["sources"] = new JsonArray(
                source ?? TestConfig.Source(Feed, "octocat", "op://Private/GitHub Packages/token"),
                TestConfig.Source(GhFeed, "octocat", "gh://github.com")),
        };
        return PluginContext.Create(ConfigLoader.Load(TestConfig.Write(_directory.Path, config)), _log, pathVariable: null);
    }

    private static GetAuthenticationCredentialsRequestHandler CredentialsHandler(PluginContext context) =>
        new(context, GetAuthenticationCredentialsRequestHandler.DefaultProgressInterval);

    private static GetAuthenticationCredentialsRequest Request(string uri, bool isRetry = false, bool isNonInteractive = false) =>
        new(new Uri(uri), isRetry, isNonInteractive, canShowDialog: false);
}
