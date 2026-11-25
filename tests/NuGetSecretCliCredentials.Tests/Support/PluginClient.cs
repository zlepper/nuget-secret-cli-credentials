using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NuGet.Common;
using NuGet.Protocol.Plugins;
using NuGet.Versioning;

namespace NuGetSecretCliCredentials.Tests.Support;

/// <summary>
/// Plays NuGet's side of the plugin protocol against the real plugin process, using NuGet.Protocol's
/// <see cref="Connection"/>. Collects the plugin's <c>Log</c> messages and stderr.
/// </summary>
internal sealed class PluginClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _standardError = new();
    private readonly LogCollector _logCollector;

    private PluginClient(Process process, Connection connection, LogCollector logCollector)
    {
        _process = process;
        Connection = connection;
        _logCollector = logCollector;
    }

    public Connection Connection { get; }

    public Process Process => _process;

    public IReadOnlyList<string> LogMessages => _logCollector.Messages;

    public string StandardError
    {
        get
        {
            lock (_standardError)
            {
                return _standardError.ToString();
            }
        }
    }

    /// <param name="requestTimeout">NuGet's per-request timeout; a request outliving it needs progress messages.</param>
    public static async Task<PluginClient> StartAsync(IReadOnlyDictionary<string, string> environment, TimeSpan requestTimeout)
    {
        var startInfo = new ProcessStartInfo(PluginLocation.DotnetHost)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        startInfo.ArgumentList.Add(PluginLocation.DllPath);
        startInfo.ArgumentList.Add("-Plugin");
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the plugin.");

        var logCollector = new LogCollector();
        var handlers = new RequestHandlers();
        handlers.TryAdd(MessageMethod.Log, logCollector);

        var options = new ConnectionOptions(
            ProtocolConstants.CurrentVersion,
            ProtocolConstants.CurrentVersion,
            handshakeTimeout: TimeSpan.FromSeconds(10),
            requestTimeout);
        var connection = new Connection(
            new MessageDispatcher(handlers, new RequestIdGenerator()),
            new Sender(process.StandardInput),
            new StandardInputReceiver(process.StandardOutput),
            options);

        var client = new PluginClient(process, connection, logCollector);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (client._standardError)
                {
                    client._standardError.AppendLine(e.Data);
                }
            }
        };
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await connection.ConnectAsync(timeout.Token);
        return client;
    }

    /// <summary>The requests NuGet sends right after the handshake.</summary>
    public async Task InitializeAsync(LogLevel logLevel = LogLevel.Debug)
    {
        var initialize = await SendAsync<InitializeRequest, InitializeResponse>(
            MessageMethod.Initialize, new InitializeRequest("7.0.0", "en-US", TimeSpan.FromSeconds(5)));
        Assert.That(initialize.ResponseCode, Is.EqualTo(MessageResponseCode.Success));

        var setLogLevel = await SendAsync<SetLogLevelRequest, SetLogLevelResponse>(
            MessageMethod.SetLogLevel, new SetLogLevelRequest(logLevel));
        Assert.That(setLogLevel.ResponseCode, Is.EqualTo(MessageResponseCode.Success));
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(MessageMethod method, TRequest request)
        where TRequest : class
        where TResponse : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var response = await Connection.SendRequestAndReceiveResponseAsync<TRequest, TResponse>(method, request, timeout.Token);
        return response ?? throw new InvalidOperationException($"{method} returned no payload.");
    }

    /// <summary>Sends <c>Close</c> the way NuGet does and waits for the process to exit.</summary>
    public async Task<bool> CloseAsync(TimeSpan exitTimeout)
    {
        var close = Connection.MessageDispatcher.CreateMessage(MessageType.Request, MessageMethod.Close);
        await Connection.SendAsync(close, CancellationToken.None);

        using var timeout = new CancellationTokenSource(exitTimeout);
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop the process first: disposing the connection while its receive thread is blocked reading the
        // plugin's stdout can hang.
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        Connection.Dispose();
        _process.Dispose();
    }

    private sealed class LogCollector : IRequestHandler
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public CancellationToken CancellationToken => CancellationToken.None;

        public IReadOnlyList<string> Messages => [.. _messages];

        public async Task HandleResponseAsync(IConnection connection, Message message, IResponseHandler responseHandler, CancellationToken cancellationToken)
        {
            var request = MessageUtilities.DeserializePayload<LogRequest>(message)!;
            _messages.Enqueue($"{request.LogLevel}: {request.Message}");
            await responseHandler.SendResponseAsync(message, new LogResponse(MessageResponseCode.Success), cancellationToken);
        }
    }
}
