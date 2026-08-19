using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using Tyhp.Domain.Enums;
using Tyhp.LanguageServer.Configuration;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class TyhpLanguageServerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Initialize_ReturnsIncrementalTextDocumentSyncAndFeatureCapabilities()
    {
        await using var session = await LspTestSession.StartAsync();

        var result = await session.InitializeAsync();

        result.Capabilities.Should().NotBeNull();
        result.Capabilities.TextDocumentSync.Should().NotBeNull();
        AssertIncrementalSync(result.Capabilities.TextDocumentSync!);
        result.Capabilities.CompletionProvider.Should().NotBeNull();
        result.Capabilities.CompletionProvider!.ResolveProvider.Should().BeTrue();
        result.Capabilities.CompletionProvider.TriggerCharacters.Should().Contain("$");
        result.Capabilities.CompletionProvider.TriggerCharacters.Should().Contain(">");
        result.Capabilities.HoverProvider.Should().NotBeNull();
        result.Capabilities.DefinitionProvider.Should().NotBeNull();
        result.Capabilities.ReferencesProvider.Should().NotBeNull();
        result.Capabilities.DocumentHighlightProvider.Should().NotBeNull();
        result.Capabilities.RenameProvider.Should().NotBeNull();
        AssertRenamePrepare(result.Capabilities.RenameProvider);
        result.Capabilities.DocumentSymbolProvider.Should().NotBeNull();
        result.Capabilities.SignatureHelpProvider.Should().NotBeNull();
        result.Capabilities.SignatureHelpProvider!.TriggerCharacters.Should().Contain("(");
        result.Capabilities.SignatureHelpProvider.TriggerCharacters.Should().Contain(",");
        result.Capabilities.FoldingRangeProvider.Should().NotBeNull();
        result.Capabilities.CodeActionProvider.Should().NotBeNull();
        result.Capabilities.DocumentFormattingProvider.Should().NotBeNull();
        result.Capabilities.DocumentRangeFormattingProvider.Should().NotBeNull();
        result.Capabilities.SemanticTokensOptions.Should().NotBeNull();
        result.Capabilities.SemanticTokensOptions!.Legend.Should().NotBeNull();
        result.Capabilities.SemanticTokensOptions.Legend.TokenTypes.Should().Contain(SemanticTokenTypes.Parameter);
        result.Capabilities.SemanticTokensOptions.Legend.TokenTypes.Should().Contain(SemanticTokenTypes.Variable);
        result.Capabilities.WorkspaceSymbolProvider.Should().NotBeNull();
    }

    [Fact]
    public async Task Initialize_WireResponse_ActuallySerializesSelectionRangeProviderCamelCase()
    {
        // TyhpServerCapabilities.SelectionRangeProvider is a plain property on a type the
        // Microsoft.VisualStudio.LanguageServer.Protocol package marks [DataContract]. Without
        // matching [DataMember]/[JsonProperty("selectionRangeProvider")] attributes, Newtonsoft's
        // data-contract resolver drops the property from the payload entirely (not merely with the
        // wrong casing) — so real LSP clients never learn Shift+Alt+Right (selectionRange) is
        // supported, even though the in-process C# object reports the flag as true. Deserializing
        // straight into InitializeResult/ServerCapabilities would hide this: the client-side type
        // does not have the property either, so the test must inspect the raw wire JSON.
        await using var session = await LspTestSession.StartAsync();

        JObject result = await session.Client.InvokeWithParameterObjectAsync<JObject>(
            Methods.InitializeName,
            new InitializeParams
            {
                ProcessId = null,
                RootUri = new Uri("file:///tmp/tyhp-test"),
                Capabilities = new ClientCapabilities(),
            });

        JToken? flag = result["capabilities"]?["selectionRangeProvider"];
        flag.Should().NotBeNull("selectionRangeProvider must be serialized on the wire, not just set on the in-process object");
        flag!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_WireResponse_SerializesSemanticTokensAndWorkspaceSymbolProvider()
    {
        await using var session = await LspTestSession.StartAsync();

        JObject result = await session.Client.InvokeWithParameterObjectAsync<JObject>(
            Methods.InitializeName,
            new InitializeParams
            {
                ProcessId = null,
                RootUri = new Uri("file:///tmp/tyhp-test"),
                Capabilities = new ClientCapabilities(),
            });

        JToken? tokens = result["capabilities"]?["semanticTokensProvider"];
        tokens.Should().NotBeNull("semanticTokensProvider must be serialized on the wire");
        tokens!["legend"]?["tokenTypes"].Should().NotBeNull();
        tokens["full"]?["delta"]?.Value<bool>().Should().BeTrue();

        JToken? workspaceSymbols = result["capabilities"]?["workspaceSymbolProvider"];
        workspaceSymbols.Should().NotBeNull();
        workspaceSymbols!.Type.Should().Be(JTokenType.Boolean);
        workspaceSymbols.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_FallsBackToRootPath_WhenRootUriIsAbsent()
    {
        await using var session = await LspTestSession.StartAsync();

#pragma warning disable CS0618 // Verifying the deprecated-RootPath fallback intentionally.
        await session.Client.InvokeWithParameterObjectAsync<InitializeResult>(
            Methods.InitializeName,
            new InitializeParams
            {
                ProcessId = null,
                RootPath = "/tmp/tyhp-rootpath-test",
                Capabilities = new ClientCapabilities(),
            });
#pragma warning restore CS0618

        session.Server.Workspace.WorkspaceRoot.Should().Be("/tmp/tyhp-rootpath-test");
    }

    [Fact]
    public async Task ShutdownThenExit_CompletesServerAndSetsSuccessExitCode()
    {
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            await using var session = await LspTestSession.StartAsync();
            await session.InitializeAsync();
            await session.NotifyInitializedAsync();

            var shutdown = await session.Client.InvokeAsync<object?>(Methods.ShutdownName);
            shutdown.Should().BeNull();

            await session.Client.NotifyAsync(Methods.ExitName);
            await session.ServerTask.WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.Success);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task ExitWithoutShutdown_SetsGenericErrorExitCode()
    {
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            await using var session = await LspTestSession.StartAsync();
            await session.InitializeAsync();

            await session.Client.NotifyAsync(Methods.ExitName);
            await session.ServerTask.WaitAsync(TestTimeout);

            Environment.ExitCode.Should().Be((int)ExitCode.GenericError);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    [Fact]
    public async Task Cancel_StopsListeningWithoutClientExit()
    {
        using var cts = new CancellationTokenSource();
        await using var session = await LspTestSession.StartAsync(cts.Token);
        await session.InitializeAsync();

        cts.Cancel();
        await session.ServerTask.WaitAsync(TestTimeout);
    }

    [Fact]
    public void ServerConfiguration_DefaultsMatchPhase1Spec()
    {
        var config = new ServerConfiguration();
        config.DebounceDelay.Should().Be(300);
        config.MaxConcurrentAnalysis.Should().Be(4);
        config.EnableDiagnostics.Should().BeTrue();
        config.TyhpProjectPath.Should().BeNull();
    }

    [Fact]
    public void CapabilityRegistration_Create_DeclaresIncrementalSync()
    {
        var capabilities = CapabilityRegistration.Create();
        capabilities.TextDocumentSync.Should().NotBeNull();
        AssertIncrementalSync(capabilities.TextDocumentSync!);
        capabilities.CompletionProvider.Should().NotBeNull();
        capabilities.CompletionProvider!.ResolveProvider.Should().BeTrue();
        capabilities.CompletionProvider.TriggerCharacters.Should().BeEquivalentTo(["$", ">", ":", "\\", "<", "("]);
        capabilities.HoverProvider.Should().NotBeNull();
        capabilities.DefinitionProvider.Should().NotBeNull();
        capabilities.ReferencesProvider.Should().NotBeNull();
        capabilities.DocumentHighlightProvider.Should().NotBeNull();
        capabilities.RenameProvider.Should().NotBeNull();
        AssertRenamePrepare(capabilities.RenameProvider);
        capabilities.DocumentSymbolProvider.Should().NotBeNull();
        capabilities.SignatureHelpProvider.Should().NotBeNull();
        capabilities.SignatureHelpProvider!.TriggerCharacters.Should().BeEquivalentTo(["(", ","]);
        capabilities.FoldingRangeProvider.Should().NotBeNull();
        capabilities.CodeActionProvider.Should().NotBeNull();
        capabilities.DocumentFormattingProvider.Should().NotBeNull();
        capabilities.DocumentRangeFormattingProvider.Should().NotBeNull();
        AssertSelectionRange(capabilities);
        capabilities.SemanticTokensOptions.Should().NotBeNull();
        capabilities.SemanticTokensOptions!.Legend.Should().NotBeNull();
        capabilities.SemanticTokensOptions.Legend.TokenTypes.Should().Contain(SemanticTokenTypes.TypeParameter);
        capabilities.WorkspaceSymbolProvider.Should().NotBeNull();
    }

    private static void AssertIncrementalSync(SumType<TextDocumentSyncKind, TextDocumentSyncOptions> sync)
    {
        if (sync.Value is TextDocumentSyncOptions options)
        {
            options.Change.Should().Be(TextDocumentSyncKind.Incremental);
            options.OpenClose.Should().BeTrue();
            options.Save.Should().NotBeNull();
            return;
        }

        if (sync.Value is TextDocumentSyncKind kind)
        {
            kind.Should().Be(TextDocumentSyncKind.Incremental);
            return;
        }

        true.Should().BeFalse("textDocumentSync should be Incremental options or kind");
    }

    private static void AssertRenamePrepare(SumType<bool, RenameOptions>? rename)
    {
        rename.Should().NotBeNull();
        SumType<bool, RenameOptions> value = rename!.Value;
        if (value.Value is RenameOptions options)
        {
            options.PrepareProvider.Should().BeTrue();
            return;
        }

        if (value.Value is bool enabled)
        {
            enabled.Should().BeTrue();
            return;
        }

        true.Should().BeFalse("renameProvider should be RenameOptions with prepareProvider or true");
    }

    private static void AssertSelectionRange(ServerCapabilities capabilities)
    {
        capabilities.Should().BeOfType<TyhpServerCapabilities>();
        ((TyhpServerCapabilities)capabilities).SelectionRangeProvider.Should().BeTrue();
    }
}
