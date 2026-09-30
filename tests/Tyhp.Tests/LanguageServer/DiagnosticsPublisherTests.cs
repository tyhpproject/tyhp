using System.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nerdbank.Streams;
using Newtonsoft.Json;
using StreamJsonRpc;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.LanguageServer;
using Tyhp.LanguageServer.Configuration;
using Tyhp.LanguageServer.Handlers;
using Tyhp.Tests.Binder;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
using TyhpDiagnostic = Tyhp.Domain.Diagnostics.Diagnostic;
using TyhpSeverity = Tyhp.Domain.Diagnostics.DiagnosticSeverity;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class DiagnosticsPublisherTests
{
    [Fact]
    public void ToLspDiagnostic_MapsSeverityCodeMessageAndSource()
    {
        var diagnostic = TyhpDiagnostic.Error(
            MessageCode.ParserUnknownError,
            "src/app.tyhp",
            4,
            2,
            ["oops"],
            endLine: 4,
            endColumn: 8);

        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(diagnostic);

        lsp.Source.Should().Be(DiagnosticsPublisher.DiagnosticSource);
        lsp.Severity.Should().Be(LspDiagnosticSeverity.Error);
        lsp.Message.Should().Be(diagnostic.Message);
        lsp.Range.Start.Line.Should().Be(3);
        lsp.Range.Start.Character.Should().Be(2);
        lsp.Range.End.Line.Should().Be(3);
        lsp.Range.End.Character.Should().Be(8);
        AssertCodeEquals(lsp, (int)MessageCode.ParserUnknownError);
    }

    [Fact]
    public void ToLspDiagnostic_WarningInfoHint_MapToLspSeverities()
    {
        DiagnosticsPublisher.ToLspDiagnostic(
                TyhpDiagnostic.Warning(MessageCode.CheckerUnusedImport, "a.tyhp", 1, 0, ["U"]))
            .Severity.Should().Be(LspDiagnosticSeverity.Warning);
        DiagnosticsPublisher.ToLspDiagnostic(
                TyhpDiagnostic.Info(MessageCode.CheckerEvalUsage, "a.tyhp", 1, 0, ["e"]))
            .Severity.Should().Be(LspDiagnosticSeverity.Information);
        DiagnosticsPublisher.ToLspDiagnostic(
                new TyhpDiagnostic(TyhpSeverity.Hint, MessageCode.CheckerEvalUsage, "a.tyhp", 1, 0, ["h"]))
            .Severity.Should().Be(LspDiagnosticSeverity.Hint);
    }

    [Fact]
    public void ToLspDiagnostic_UnusedImport_HasUnnecessaryTag()
    {
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(
            TyhpDiagnostic.Warning(MessageCode.CheckerUnusedImport, "a.tyhp", 2, 0, ["Foo"]));
        lsp.Tags.Should().NotBeNull();
        lsp.Tags.Should().Contain(DiagnosticTag.Unnecessary);
    }

    [Fact]
    public void ToLspDiagnostic_DeprecatedUsage_HasDeprecatedTag()
    {
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(
            TyhpDiagnostic.Warning(MessageCode.CheckerDeprecatedUsage, "a.tyhp", 2, 0, ["old", ""]));
        lsp.Tags.Should().NotBeNull();
        lsp.Tags.Should().Contain(DiagnosticTag.Deprecated);
    }

    [Fact]
    public void ToLspDiagnostic_MapsLabelsToRelatedInformation()
    {
        var diagnostic = TyhpDiagnostic.Error(
                MessageCode.BinderDuplicateSymbolDeclaration,
                "src/app.tyhp",
                line: 4,
                column: 0,
                ["User"],
                endLine: 4,
                endColumn: 10)
            .WithLabels(new DiagnosticLabel(
                new DiagnosticSpan("src/app.tyhp", 2, 0, 2, 10),
                "declared here"));

        var documentUri = new Uri("file:///workspace/src/app.tyhp");
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(diagnostic, documentUri);
        var mapped = lsp.Should().BeOfType<TyhpLspDiagnostic>().Subject;
        mapped.RelatedInformation.Should().NotBeNull();
        mapped.RelatedInformation.Should().ContainSingle();

        DiagnosticRelatedInformation related = mapped.RelatedInformation![0];
        related.Message.Should().Be("declared here");
        related.Location.Uri.Should().Be(documentUri);
        related.Location.Range.Start.Line.Should().Be(1);
        related.Location.Range.Start.Character.Should().Be(0);
        related.Location.Range.Start.Line.Should().BeLessThan(lsp.Range.Start.Line);
    }

    [Fact]
    public void ToLspDiagnostic_WithoutLabels_OmitsRelatedInformation()
    {
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(
            TyhpDiagnostic.Error(MessageCode.ParserUnknownError, "a.tyhp", 1, 0, ["x"]));
        var mapped = lsp.Should().BeOfType<TyhpLspDiagnostic>().Subject;
        mapped.RelatedInformation.Should().BeNull();
    }

    [Fact]
    public void ToLspDiagnostic_DuplicateClass3002_RelatedInformationIsNonEmpty()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class User {}
            class User {}
            """);

        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
            .Subject;
        error.Labels.Should().NotBeEmpty();

        Uri documentUri = Path.IsPathRooted(error.FileName)
            ? new Uri(Path.GetFullPath(error.FileName))
            : new Uri("file:///tmp/" + Path.GetFileName(error.FileName));
        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(error, documentUri);
        var mapped = lsp.Should().BeOfType<TyhpLspDiagnostic>().Subject;
        mapped.RelatedInformation.Should().NotBeNull();
        mapped.RelatedInformation.Should().NotBeEmpty();
        mapped.RelatedInformation![0].Message.Should().Be(Message.Localize("CLI_DiagnosticLabelDeclaredHere"));
        mapped.RelatedInformation[0].Location.Uri.Should().NotBeNull();
        mapped.RelatedInformation[0].Location.Range.Start.Line.Should().Be(error.Labels[0].Span.Line - 1);
        mapped.RelatedInformation[0].Location.Range.Start.Line.Should().BeLessThan(lsp.Range.Start.Line);
    }

    [Fact]
    public void ToLspDiagnostic_RelatedInformation_SerializesCamelCaseOnTheWire()
    {
        var diagnostic = TyhpDiagnostic.Error(
                MessageCode.BinderDuplicateSymbolDeclaration,
                "src/app.tyhp",
                4,
                0,
                ["User"])
            .WithLabels(new DiagnosticLabel(
                new DiagnosticSpan("src/app.tyhp", 2, 0, 2, 10),
                "declared here"));

        LspDiagnostic lsp = DiagnosticsPublisher.ToLspDiagnostic(
            diagnostic,
            new Uri("file:///workspace/src/app.tyhp"));

        string json = JsonConvert.SerializeObject(lsp);
        json.Should().Contain("relatedInformation");
        json.Should().Contain("declared here");
        json.Should().NotContain("RelatedInformation");
    }

    /// <summary>
    /// End-to-end proof that <c>relatedInformation</c> reaches the client on the actual
    /// JSON-RPC wire bytes (not just via <see cref="JsonConvert.SerializeObject"/> in
    /// isolation), by running the real server through <see cref="TyhpLanguageServer.RunAsync"/>
    /// and capturing every raw byte the server writes to the transport.
    /// </summary>
    [Fact]
    public async Task PublishDiagnostics_DuplicateClass3002_RelatedInformationPresentOnWire()
    {
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        var capture = new WireCapturingStream(serverStream);
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TyhpLanguageServer? created = null;
        using var cts = new CancellationTokenSource();

        var serverTask = TyhpLanguageServer.RunAsync(
            capture,
            capture,
            new ServerConfiguration
            {
                DebounceDelay = 40,
                CompilationOptions = new Tyhp.Domain.Services.CompilationOptions
                {
                    EnableAstCache = false,
                    ProjectPath = Path.GetTempPath(),
                },
            },
            cts.Token,
            onListening: () => listening.TrySetResult(),
            onServerCreated: server => created = server);

        await listening.Task.WaitAsync(LspTestSession.DefaultTimeout);
        created.Should().NotBeNull();

        var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, new JsonMessageFormatter()));
        client.StartListening();

        try
        {
            await client.InvokeWithParameterObjectAsync<InitializeResult>(
                Methods.InitializeName,
                new InitializeParams
                {
                    ProcessId = null,
                    RootUri = new Uri("file:///tmp/tyhp-test"),
                    Capabilities = new ClientCapabilities(),
                });
            await client.NotifyWithParameterObjectAsync(Methods.InitializedName, new InitializedParams());

            var uri = new Uri("file:///tmp/tyhp-test/dup_" + Guid.NewGuid().ToString("N") + ".tyhp");
            await client.NotifyWithParameterObjectAsync(
                Methods.TextDocumentDidOpenName,
                new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = uri,
                        LanguageId = "tyhp",
                        Version = 1,
                        Text = "<?tyhp\nclass User {}\nclass User {}\n",
                    },
                });

            var start = DateTime.UtcNow;
            while (!capture.CapturedText.Contains("publishDiagnostics", StringComparison.Ordinal)
                || !capture.CapturedText.Contains("\"code\":3002", StringComparison.Ordinal))
            {
                if (DateTime.UtcNow - start > LspTestSession.DefaultTimeout)
                {
                    throw new TimeoutException(
                        "Timed out waiting for a TYHP3002 diagnostic notification on the wire.");
                }

                await Task.Delay(20);
            }

            string wireText = capture.CapturedText;
            wireText.Should().Contain("relatedInformation");
            wireText.Should().Contain(Message.Localize("CLI_DiagnosticLabelDeclaredHere"));
        }
        finally
        {
            client.Dispose();
            serverStream.Dispose();
            clientStream.Dispose();
            cts.Cancel();
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
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
    /// Write-through stream decorator that records every byte written to it (i.e. the raw
    /// JSON-RPC bytes the server sends), so a test can assert on the literal wire payload.
    /// Overrides both the classic <c>byte[]</c> and the <c>Span</c>/<c>Memory</c> write
    /// overloads since the pipe adapter used by <c>HeaderDelimitedMessageHandler</c> may call
    /// either depending on the runtime.
    /// </summary>
    private sealed class WireCapturingStream(Stream inner) : Stream
    {
        private readonly MemoryStream _captured = new();

        public string CapturedText
        {
            get
            {
                lock (this._captured)
                {
                    return Encoding.UTF8.GetString(this._captured.ToArray());
                }
            }
        }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.Capture(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.Capture(buffer);
            inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.Capture(buffer.AsSpan(offset, count));
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.Capture(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Capture(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
            {
                return;
            }

            lock (this._captured)
            {
                this._captured.Write(data);
            }
        }
    }

    private static void AssertCodeEquals(LspDiagnostic diagnostic, int expected)
    {
        object? code = diagnostic.Code;
        if (code is int intCode)
        {
            intCode.Should().Be(expected);
            return;
        }

        if (code is SumType<int, string> sum)
        {
            if (sum.Value is int sumInt)
            {
                sumInt.Should().Be(expected);
                return;
            }
        }

        code.Should().NotBeNull("diagnostic code should be populated");
        Convert.ToInt32(code is SumType<int, string> inner ? inner.Value : code).Should().Be(expected);
    }
}
