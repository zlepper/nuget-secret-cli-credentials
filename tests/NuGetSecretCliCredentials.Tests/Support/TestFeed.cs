using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NuGet.Packaging;
using NuGet.Versioning;

namespace NuGetSecretCliCredentials.Tests.Support;

/// <summary>
/// A minimal NuGet v3 feed (service index plus flat container) serving one package, which answers 401 to every
/// request without the expected basic-auth credentials.
/// </summary>
internal sealed class TestFeed : IDisposable
{
    public const string PackageId = "OpPlugin.TestPackage";
    public const string PackageVersion = "1.0.0";

    private readonly HttpListener _listener = new();
    private readonly string _expectedAuthorization;
    private readonly byte[] _package;
    private readonly Task _serveTask;

    public TestFeed(string username, string password, TempDirectory workDirectory)
    {
        _expectedAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _package = BuildPackage(workDirectory);

        BaseUri = $"http://127.0.0.1:{FreePort()}/";
        _listener.Prefixes.Add(BaseUri);
        _listener.Start();
        _serveTask = Task.Run(ServeAsync);
    }

    public string BaseUri { get; }

    public string ServiceIndex => BaseUri + "v3/index.json";

    public ConcurrentQueue<(string Path, bool Authorized)> Requests { get; } = new();

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        try
        {
            _serveTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The listener throws when stopped mid-accept.
        }
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            using var response = context.Response;
            var path = context.Request.Url!.AbsolutePath;
            var authorized = context.Request.Headers["Authorization"] == _expectedAuthorization;
            Requests.Enqueue((path, authorized));

            if (!authorized)
            {
                response.StatusCode = 401;
                response.AddHeader("WWW-Authenticate", "Basic realm=\"test feed\"");
                continue;
            }

            var lowerId = PackageId.ToLowerInvariant();
            var (status, contentType, body) = path switch
            {
                "/v3/index.json" => (200, "application/json", Encoding.UTF8.GetBytes(ServiceIndexJson())),
                _ when path == $"/v3/flat/{lowerId}/index.json" => (200, "application/json", Encoding.UTF8.GetBytes($$"""{"versions":["{{PackageVersion}}"]}""")),
                _ when path == $"/v3/flat/{lowerId}/{PackageVersion}/{lowerId}.{PackageVersion}.nupkg" => (200, "application/octet-stream", _package),
                _ => (404, "text/plain", Encoding.UTF8.GetBytes("not found")),
            };

            response.StatusCode = status;
            response.ContentType = contentType;
            await response.OutputStream.WriteAsync(body);
        }
    }

    private string ServiceIndexJson() => $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "{{BaseUri}}v3/flat/", "@type": "PackageBaseAddress/3.0.0" }
          ]
        }
        """;

    private static byte[] BuildPackage(TempDirectory workDirectory)
    {
        var placeholder = workDirectory.WriteFile("package-content/_._", string.Empty);
        var builder = new PackageBuilder
        {
            Id = PackageId,
            Version = NuGetVersion.Parse(PackageVersion),
            Description = "Test package for nuget-plugin-secret-cli.",
        };
        builder.Authors.Add("nuget-plugin-secret-cli tests");
        builder.Files.Add(new PhysicalPackageFile { SourcePath = placeholder, TargetPath = "lib/netstandard2.0/_._" });

        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
