using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nerdbank.Streams;
using StreamJsonRpc;
using Tyhp.LanguageServer;
using Tyhp.LanguageServer.Configuration;

namespace Tyhp.Tests.LanguageServer;

/// <summary>
/// In-memory JSON-RPC pair for language-server tests. Does not force-exit the process.
/// </summary>
internal sealed class LspTestSession : IAsyncDisposable
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly Stream _serverStream;
    private readonly Stream _clientStream;

    private LspTestSession(
        JsonRpc client,
        TyhpLanguageServer server,
        Stream serverStream,
        Stream clientStream,
        Task serverTask,
        LspNotificationCollector notifications)
    {
        this.Client = client;
        this.Server = server;
        this._serverStream = serverStream;
        this._clientStream = clientStream;
        this.ServerTask = serverTask;
        this.Notifications = notifications;
    }

    public JsonRpc Client { get; }

    public TyhpLanguageServer Server { get; }

    public Task ServerTask { get; }

    public LspNotificationCollector Notifications { get; }

    public static async Task<LspTestSession> StartAsync(
        CancellationToken cancellationToken = default,
        ServerConfiguration? configuration = null)
    {
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TyhpLanguageServer? created = null;
        var serverTask = TyhpLanguageServer.RunAsync(
            serverStream,
            serverStream,
            configuration ?? CreateDefaultConfiguration(),
            cancellationToken,
            onListening: () => listening.TrySetResult(),
            onServerCreated: server => created = server);

        await listening.Task.WaitAsync(DefaultTimeout);

        var collector = new LspNotificationCollector();
        var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, new JsonMessageFormatter()));
        client.AddLocalRpcTarget(collector);
        client.StartListening();
        return new LspTestSession(
            client,
            created ?? throw new InvalidOperationException("Language server was not created."),
            serverStream,
            clientStream,
            serverTask,
            collector);
    }

    private static ServerConfiguration CreateDefaultConfiguration()
    {
        return new ServerConfiguration
        {
            DebounceDelay = 40,
            CompilationOptions = new Tyhp.Domain.Services.CompilationOptions
            {
                EnableAstCache = false,
                ProjectPath = Path.GetTempPath(),
            },
        };
    }

    public Task<InitializeResult> InitializeAsync(Uri? rootUri = null)
    {
        return this.Client.InvokeWithParameterObjectAsync<InitializeResult>(
            Methods.InitializeName,
            new InitializeParams
            {
                ProcessId = null,
                RootUri = rootUri ?? new Uri("file:///tmp/tyhp-test"),
                Capabilities = new ClientCapabilities(),
            });
    }

    public Task NotifyInitializedAsync()
        => this.Client.NotifyWithParameterObjectAsync(Methods.InitializedName, new InitializedParams());

    public Task WaitForDiagnosticsAsync(Uri uri, Func<PublishDiagnosticParams, bool>? predicate = null, TimeSpan? timeout = null)
    {
        return this.WaitForAsync(
            () => this.Notifications.LastFor(uri) is { } latest
                && (predicate is null || predicate(latest)),
            timeout);
    }

    public async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? DefaultTimeout;
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > limit)
            {
                throw new TimeoutException("Timed out waiting for language-server condition.");
            }

            await Task.Delay(10);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            this.Client.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            this._serverStream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            this._clientStream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await this.ServerTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (
            ex is TimeoutException
            or OperationCanceledException
            or ConnectionLostException
            or ObjectDisposedException
            or InvalidOperationException)
        {
        }
    }
}

/// <summary>
/// Collects server-to-client notifications for assertions.
/// </summary>
internal sealed class LspNotificationCollector
{
    private readonly List<PublishDiagnosticParams> _diagnostics = [];
    private readonly object _lock = new();

    [JsonRpcMethod(Methods.TextDocumentPublishDiagnosticsName, UseSingleObjectParameterDeserialization = true)]
    public void OnPublishDiagnostics(PublishDiagnosticParams arg)
    {
        if (arg is null)
        {
            return;
        }

        lock (this._lock)
        {
            this._diagnostics.Add(arg);
        }
    }

    public IReadOnlyList<PublishDiagnosticParams> Diagnostics
    {
        get
        {
            lock (this._lock)
            {
                return [.. this._diagnostics];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (this._lock)
            {
                return this._diagnostics.Count;
            }
        }
    }

    public PublishDiagnosticParams? LastFor(Uri uri)
    {
        string key = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
        lock (this._lock)
        {
            for (int i = this._diagnostics.Count - 1; i >= 0; i--)
            {
                PublishDiagnosticParams item = this._diagnostics[i];
                if (item.Uri is not null
                    && string.Equals(
                        item.Uri.IsAbsoluteUri ? item.Uri.AbsoluteUri : item.Uri.ToString(),
                        key,
                        StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }

        return null;
    }
}
