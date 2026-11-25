using System.Text.Json.Nodes;
using NuGetOpCredentialProvider.Config;
using NuGetOpCredentialProvider.Tests.Support;

namespace NuGetOpCredentialProvider.Tests;

public sealed class ConfigTests
{
    private TempDirectory _directory = null!;

    [SetUp]
    public void SetUp() => _directory = new TempDirectory();

    [TearDown]
    public void TearDown() => _directory.Dispose();

    [Test]
    public void Loads_a_complete_config()
    {
        var result = ConfigLoader.Load(TestConfig.Write(_directory.Path, new JsonObject
        {
            ["opPath"] = "/opt/op",
            ["ghPath"] = "/opt/gh",
            ["account"] = "my.1password.com",
            ["nonInteractive"] = "skip",
            ["timeoutSeconds"] = 30,
            ["sources"] = new JsonArray(
                TestConfig.Source("https://nuget.pkg.github.com/acme/", "me", "op://Private/GitHub Packages/token"),
                TestConfig.Source("https://nuget.pkg.github.com/other/", "me", "gh://github.com")),
        }));

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Warnings, Is.Empty);
        var config = result.Config!;
        Assert.That(config.OpPath, Is.EqualTo("/opt/op"));
        Assert.That(config.GhPath, Is.EqualTo("/opt/gh"));
        Assert.That(config.Account, Is.EqualTo("my.1password.com"));
        Assert.That(config.NonInteractive, Is.EqualTo(NonInteractiveMode.Skip));
        Assert.That(config.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(config.Sources.Select(s => s.UriPrefix.ToString()),
            Is.EqualTo(new[] { "https://nuget.pkg.github.com/acme/", "https://nuget.pkg.github.com/other/" }));
    }

    [Test]
    public void Defaults_apply_when_optional_keys_are_missing()
    {
        var result = ConfigLoader.Load(TestConfig.Write(_directory.Path, new JsonObject
        {
            ["sources"] = new JsonArray(TestConfig.Source("https://feed.example/", "me", "op://v/i/f")),
        }));

        var config = result.Config!;
        Assert.That(config.OpPath, Is.Null);
        Assert.That(config.GhPath, Is.Null);
        Assert.That(config.Account, Is.Null);
        Assert.That(config.NonInteractive, Is.EqualTo(NonInteractiveMode.Prompt));
        Assert.That(config.Timeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
    }

    [Test]
    public void Default_path_follows_XDG_CONFIG_HOME_and_falls_back_to_dot_config()
    {
        Assert.That(ConfigLoader.DefaultPath(_ => "/xdg"), Is.EqualTo("/xdg/nuget-op-credential-provider/config.json"));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallback = Path.Combine(home, ".config", "nuget-op-credential-provider", "config.json");
        Assert.That(ConfigLoader.DefaultPath(_ => null), Is.EqualTo(fallback));
        Assert.That(ConfigLoader.DefaultPath(_ => "relative/path"), Is.EqualTo(fallback), "XDG requires an absolute path");
    }

    [Test]
    public void Http_sources_are_refused()
    {
        var result = ConfigLoader.Load(TestConfig.Write(_directory.Path, new JsonObject
        {
            ["sources"] = new JsonArray(TestConfig.Source("http://feed.example/", "me", "op://v/i/f")),
        }));

        Assert.That(result.Config, Is.Null);
        Assert.That(result.Error, Does.Contain("https://"));
    }

    [Test]
    public void Http_sources_are_allowed_only_behind_the_test_flag_and_then_warn()
    {
        var result = ConfigLoader.Load(TestConfig.Write(_directory.Path, new JsonObject
        {
            ["allowInsecureHttpForTesting"] = true,
            ["sources"] = new JsonArray(TestConfig.Source("http://127.0.0.1:1234/", "me", "op://v/i/f")),
        }));

        Assert.That(result.Config, Is.Not.Null);
        Assert.That(result.Warnings, Has.Some.Contains("allowInsecureHttpForTesting"));
    }

    [Test]
    public void Literal_password_loads_with_a_warning_that_does_not_contain_it()
    {
        var result = ConfigLoader.Load(TestConfig.Write(_directory.Path, new JsonObject
        {
            ["sources"] = new JsonArray(TestConfig.Source("https://feed.example/", "me", "hunter2")),
        }));

        Assert.That(result.Config, Is.Not.Null);
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings[0], Does.Contain("literal").And.Not.Contain("hunter2"));
    }

    [Test]
    public void Missing_file_fails_without_throwing()
    {
        var result = ConfigLoader.Load(_directory.Combine("nope", "config.json"));

        Assert.That(result.Config, Is.Null);
        Assert.That(result.Error, Does.Contain("does not exist"));
    }

    [TestCase("{ not json")]
    [TestCase("""{ "sources": [ { "uriPrefix": "https://feed.example/", "username": "me", "passwrod": "op://v/i/f" } ] }""")]
    [TestCase("""{ "nonInteractive": "sometimes" }""")]
    [TestCase("""{ "timeoutSeconds": 0 }""")]
    [TestCase("""{ "sources": [ { "uriPrefix": "not a uri", "username": "me", "password": "op://v/i/f" } ] }""")]
    [TestCase("""{ "sources": [ { "uriPrefix": "https://feed.example/", "password": "op://v/i/f" } ] }""")]
    [TestCase("""{ "sources": [ { "uriPrefix": "https://feed.example/", "username": "me" } ] }""")]
    [TestCase("null")]
    public void Broken_file_fails_without_throwing(string contents)
    {
        var path = _directory.WriteFile("config.json", contents);

        var result = ConfigLoader.Load(path);

        Assert.That(result.Config, Is.Null);
        Assert.That(result.Error, Does.Contain(path));
    }

    [Test]
    public void Accepts_comments_and_trailing_commas()
    {
        var path = _directory.WriteFile("config.json", """
            {
              // the work feed
              "sources": [
                { "uriPrefix": "https://feed.example/", "username": "me", "password": "op://v/i/f", },
              ],
            }
            """);

        Assert.That(ConfigLoader.Load(path).Config, Is.Not.Null);
    }

    [Test]
    public void Longest_matching_prefix_wins()
    {
        var sources = Sources("https://feed.example/", "https://feed.example/org/", "https://feed.example/org/team/");

        Assert.That(SourceMatcher.Match(sources, new Uri("https://feed.example/org/team/index.json"))?.UriPrefix.AbsolutePath, Is.EqualTo("/org/team/"));
        Assert.That(SourceMatcher.Match(sources, new Uri("https://feed.example/org/other/index.json"))?.UriPrefix.AbsolutePath, Is.EqualTo("/org/"));
        Assert.That(SourceMatcher.Match(sources, new Uri("https://feed.example/x"))?.UriPrefix.AbsolutePath, Is.EqualTo("/"));
    }

    [Test]
    public void Scheme_and_host_match_case_insensitively_but_the_path_matches_exactly()
    {
        var sources = Sources("https://nuget.pkg.github.com/my-org/");

        Assert.That(SourceMatcher.Match(sources, "HTTPS://NuGet.Pkg.GitHub.com/my-org/index.json"), Is.Not.Null);
        Assert.That(SourceMatcher.Match(sources, "https://nuget.pkg.github.com/My-Org/index.json"), Is.Null);
    }

    [Test]
    public void Different_scheme_port_or_host_does_not_match()
    {
        var sources = Sources("https://feed.example/");

        Assert.That(SourceMatcher.Match(sources, "http://feed.example/index.json"), Is.Null);
        Assert.That(SourceMatcher.Match(sources, "https://feed.example:8443/index.json"), Is.Null);
        Assert.That(SourceMatcher.Match(sources, "https://feed.example.evil/index.json"), Is.Null);
        Assert.That(SourceMatcher.Match(sources, "not a uri"), Is.Null);
        Assert.That(SourceMatcher.Match(sources, (string?)null), Is.Null);
    }

    private static List<SourceConfig> Sources(params string[] prefixes) =>
        [.. prefixes.Select(prefix => new SourceConfig { UriPrefix = new Uri(prefix), Username = "u", Password = "op://v/i/f" })];
}
