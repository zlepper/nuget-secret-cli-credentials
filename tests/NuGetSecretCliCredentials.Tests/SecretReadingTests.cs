using System.Diagnostics;
using System.Text.Json.Nodes;
using NuGetSecretCliCredentials.Config;
using NuGetSecretCliCredentials.Secrets;
using NuGetSecretCliCredentials.Tests.Support;

namespace NuGetSecretCliCredentials.Tests;

public sealed class SecretReadingTests
{
    private const string SecretValue = "s3cr3t-token-value";

    private TempDirectory _directory = null!;
    private RecordingLog _log = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = new TempDirectory();
        _log = new RecordingLog();
    }

    [TearDown]
    public void TearDown()
    {
        Assert.That(_log.AllText, Does.Not.Contain(SecretValue), "a secret reached the log");
        _directory.Dispose();
    }

    [Test]
    public async Task Op_read_returns_the_value_and_passes_no_newline_and_account()
    {
        var op = FakeTool.Create(_directory, "op", FakeBehavior.Prints(SecretValue));
        var cli = new OpCli(Config(opPath: op.Path, account: "team.1password.com"), _log, pathVariable: null);

        var result = await cli.ReadAsync("op://Private/GitHub Packages/token", CancellationToken.None);

        Assert.That(result.Value?.Reveal(), Is.EqualTo(SecretValue));
        Assert.That(op.Invocations.Single(),
            Is.EqualTo(new[] { "read", "--no-newline", "--account", "team.1password.com", "op://Private/GitHub Packages/token" }));
    }

    [Test]
    public async Task Op_read_without_account_passes_no_account_flag()
    {
        var op = FakeTool.Create(_directory, "op", FakeBehavior.Prints(SecretValue));
        var cli = new OpCli(Config(opPath: op.Path), _log, pathVariable: null);

        await cli.ReadAsync("op://v/i/f", CancellationToken.None);

        Assert.That(op.Invocations.Single(), Is.EqualTo(new[] { "read", "--no-newline", "op://v/i/f" }));
    }

    [TestCase("[ERROR] 2026/11/25 10:00:00 \"Nope\" isn't an item in the \"Private\" vault.", "does not exist")]
    [TestCase("[ERROR] 2026/11/25 10:00:00 account is not signed in", "not signed in")]
    [TestCase("[ERROR] 2026/11/25 10:00:00 authorization prompt dismissed, please try again", "declined")]
    public async Task Op_failure_is_reported_with_a_reason_and_logged_with_stderr(string standardError, string expectedReason)
    {
        var op = FakeTool.Create(_directory, "op", FakeBehavior.Fails(standardError, exitCode: 1));
        var cli = new OpCli(Config(opPath: op.Path), _log, pathVariable: null);

        var result = await cli.ReadAsync("op://v/i/f", CancellationToken.None);

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Failure, Does.Contain(expectedReason).And.Contain("op://v/i/f"));
        Assert.That(_log.AllText, Does.Contain("exit code 1").And.Contain(standardError));
    }

    [Test]
    public async Task Op_that_outlives_the_timeout_is_killed()
    {
        var op = FakeTool.Create(_directory, "op", FakeBehavior.Prints(SecretValue) with { SleepSeconds = 30 });
        var cli = new OpCli(Config(opPath: op.Path, timeoutSeconds: 1), _log, pathVariable: null);

        var stopwatch = Stopwatch.StartNew();
        var result = await cli.ReadAsync("op://v/i/f", CancellationToken.None);

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Failure, Does.Contain("timed out"));
    }

    [Test]
    public async Task Stderr_lines_containing_the_value_never_reach_the_log()
    {
        var op = FakeTool.Create(_directory, "op", new FakeBehavior
        {
            Output = SecretValue,
            StandardError = $"debug: resolved to {SecretValue}\nsomething else went wrong",
            ExitCode = 2,
        });
        var cli = new OpCli(Config(opPath: op.Path), _log, pathVariable: null);

        var result = await cli.ReadAsync("op://v/i/f", CancellationToken.None);

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Failure, Does.Not.Contain(SecretValue));
        Assert.That(_log.AllText, Does.Contain("something else went wrong"));
        // TearDown asserts that SecretValue is absent from the log.
    }

    [Test]
    public async Task Missing_op_binary_fails_cleanly()
    {
        var cli = new OpCli(Config(opPath: _directory.Combine("no-such-op")), _log, pathVariable: null);

        var result = await cli.ReadAsync("op://v/i/f", CancellationToken.None);

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Failure, Does.Contain("Could not start"));
    }

    [Test]
    public async Task Op_whoami_exit_code_decides_authorization()
    {
        var authorized = FakeTool.Create(_directory, "op", new FakeBehavior { WhoAmIExitCode = 0 });
        Assert.That(await new OpCli(Config(opPath: authorized.Path, account: "acct"), _log, null).IsAuthorizedAsync(CancellationToken.None), Is.True);
        Assert.That(authorized.Invocations.Single(), Is.EqualTo(new[] { "whoami", "--account", "acct" }));

        using var other = new TempDirectory();
        var locked = FakeTool.Create(other, "op", new FakeBehavior { WhoAmIExitCode = 1 });
        Assert.That(await new OpCli(Config(opPath: locked.Path), _log, null).IsAuthorizedAsync(CancellationToken.None), Is.False);
    }

    [Test]
    public async Task Gh_token_is_trimmed_and_the_host_comes_from_the_reference()
    {
        var gh = FakeTool.Create(_directory, "gh", FakeBehavior.Prints(SecretValue + "\n"));
        var cli = new GhCli(Config(ghPath: gh.Path), _log, pathVariable: null);

        var github = await cli.TokenAsync("gh://github.com", CancellationToken.None);
        var enterprise = await cli.TokenAsync("gh://ghe.example.com/", CancellationToken.None);
        var defaulted = await cli.TokenAsync("gh://", CancellationToken.None);

        Assert.That(github.Value?.Reveal(), Is.EqualTo(SecretValue));
        Assert.That(enterprise.Value?.Reveal(), Is.EqualTo(SecretValue));
        Assert.That(defaulted.Value?.Reveal(), Is.EqualTo(SecretValue));
        Assert.That(gh.Invocations, Is.EqualTo(new[]
        {
            new[] { "auth", "token", "--hostname", "github.com" },
            new[] { "auth", "token", "--hostname", "ghe.example.com" },
            new[] { "auth", "token", "--hostname", "github.com" },
        }));
    }

    [Test]
    public async Task Gh_failure_says_how_to_log_in()
    {
        var gh = FakeTool.Create(_directory, "gh", FakeBehavior.Fails("no oauth token found for github.com"));
        var cli = new GhCli(Config(ghPath: gh.Path), _log, pathVariable: null);

        var result = await cli.TokenAsync("gh://github.com", CancellationToken.None);

        Assert.That(result.Value, Is.Null);
        Assert.That(result.Failure, Does.Contain("gh auth login"));
        Assert.That(_log.AllText, Does.Contain("no oauth token found"));
    }

    [Test]
    public void Tool_lookup_prefers_config_then_PATH_then_fallbacks_and_skips_non_executables()
    {
        var onPath = FakeTool.Create(_directory, "op", FakeBehavior.Prints("x"));
        var pathDirectory = Path.GetDirectoryName(onPath.Path)!;
        var notExecutableDirectory = _directory.Combine("plain");
        Directory.CreateDirectory(notExecutableDirectory);
        File.WriteAllText(Path.Combine(notExecutableDirectory, "op"), "not executable");
        var pathVariable = string.Join(Path.PathSeparator, notExecutableDirectory, pathDirectory);

        Assert.That(ToolLocator.Locate("/configured/op", "op", [], pathVariable), Is.EqualTo("/configured/op"));
        Assert.That(ToolLocator.Locate(null, "op", [], pathVariable), Is.EqualTo(onPath.Path));
        Assert.That(ToolLocator.Locate(null, "op", [onPath.Path], notExecutableDirectory), Is.EqualTo(onPath.Path));
        Assert.That(ToolLocator.Locate(null, "op", ["/no/such/op"], null), Is.Null);
    }

    [Test]
    public void Secret_never_prints_its_value()
    {
        var secret = new Secret(SecretValue);

        Assert.That(secret.ToString(), Is.EqualTo("***"));
        Assert.That($"{secret}", Is.EqualTo("***"));
        Assert.That(new Credentials.Credential(secret, secret).ToString(), Does.Not.Contain(SecretValue));
    }

    private PluginConfig Config(string? opPath = null, string? ghPath = null, string? account = null, int timeoutSeconds = 20)
    {
        var json = new JsonObject
        {
            ["opPath"] = opPath,
            ["ghPath"] = ghPath,
            ["account"] = account,
            ["timeoutSeconds"] = timeoutSeconds,
            ["sources"] = new JsonArray(),
        };
        return TestConfig.Load(_directory, json);
    }
}
