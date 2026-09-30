using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.CLI;
using Tyhp.CLI.Support;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class SymbolTreeActionTests
{
    [Fact]
    public void Dump_IncludesProjectFunctionSignature()
    {
        using var builder = OverlayFixture();
        var project = builder.BuildProject();
        var outPath = Path.Combine(project.GetProjectPath(), "symbols.json");

        var result = new SymbolTreeAction(project, outPath, "app_entry")
            .Start(CancellationToken.None);

        result.Should().NotBeNull();
        result!.GlobalScope.Should().NotBeNull();
        File.Exists(outPath).Should().BeTrue();

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        root.GetProperty("command").GetString().Should().Be("symbol_tree");
        root.GetProperty("phpVersion").GetString().Should().Be("8.2");

        var fn = FindSymbol(root, "app_entry");
        fn.Should().NotBeNull();
        fn!.Value.GetProperty("kind").GetString().Should().Be("function");
        fn.Value.GetProperty("signature").GetString().Should().Contain("app_entry");
        fn.Value.GetProperty("signature").GetString().Should().Contain(": void");
    }

    [Fact]
    public void Dump_KeepAndAlias_ExposesBothTyhpNamesWithPhpOriginal()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/alias.tyhpdef", """
            <?tyhpdef
            partial function \tyhp_symbol_tree_fn;
            partial function \tyhp_symbol_tree_fn as tyhp_symbol_tree_fn_alias;
            """);
        var project = builder.BuildProject();
        var outPath = Path.Combine(project.GetProjectPath(), "symbols.json");

        new SymbolTreeAction(project, outPath, "tyhp_symbol_tree_fn")
            .Start(CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var original = FindSymbol(doc.RootElement, "tyhp_symbol_tree_fn");
        var alias = FindSymbol(doc.RootElement, "tyhp_symbol_tree_fn_alias");

        original.Should().NotBeNull();
        alias.Should().NotBeNull();
        alias!.Value.GetProperty("phpName").GetString().Should().Be("tyhp_symbol_tree_fn");
        alias.Value.GetProperty("aliased").GetBoolean().Should().BeTrue();
        alias.Value.GetProperty("signature").GetString().Should().Contain("tyhp_symbol_tree_fn_alias");
    }

    [Fact]
    public void Dump_AliasOnly_ReplacesTyhpName()
    {
        using var builder = OverlayFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/rename.tyhpdef", """
            <?tyhpdef
            partial function \tyhp_symbol_tree_fn as tyhp_symbol_tree_fn_renamed;
            """);
        var project = builder.BuildProject();
        var outPath = Path.Combine(project.GetProjectPath(), "symbols.json");

        new SymbolTreeAction(project, outPath, "tyhp_symbol_tree_fn")
            .Start(CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        FindSymbol(doc.RootElement, "tyhp_symbol_tree_fn").Should().BeNull();
        var renamed = FindSymbol(doc.RootElement, "tyhp_symbol_tree_fn_renamed");
        renamed.Should().NotBeNull();
        renamed!.Value.GetProperty("phpName").GetString().Should().Be("tyhp_symbol_tree_fn");
        renamed.Value.GetProperty("aliased").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Dump_NameFilter_ExcludesUnrelatedSymbols()
    {
        using var builder = OverlayFixture();
        var project = builder.BuildProject();
        var outPath = Path.Combine(project.GetProjectPath(), "symbols.json");

        new SymbolTreeAction(project, outPath, "app_entry")
            .Start(CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        FindSymbol(doc.RootElement, "app_entry").Should().NotBeNull();
        FindSymbol(doc.RootElement, "call_user_func").Should().BeNull();
        doc.RootElement.GetProperty("counts").GetProperty("symbols").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Dump_IncludesTypeMembers()
    {
        using var builder = OverlayFixture();
        var project = builder.BuildProject();
        var outPath = Path.Combine(project.GetProjectPath(), "symbols.json");

        new SymbolTreeAction(project, outPath, "TyhpSymbolTreeType")
            .Start(CancellationToken.None);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var type = FindSymbol(doc.RootElement, "TyhpSymbolTreeType");
        type.Should().NotBeNull();
        type!.Value.GetProperty("kind").GetString().Should().Be("class");
        type.Value.TryGetProperty("members", out var members).Should().BeTrue();
        members.EnumerateArray().Select(m => m.GetProperty("name").GetString())
            .Should().Contain("getMessage");
    }

    [Fact]
    public void Dumper_NullScope_StillEmitsDocument()
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.AddError(
            MessageCode.BinderUnknownError,
            "<input>",
            0,
            0,
            "No source files provided for binding.");

        var json = SymbolTreeDumper.Dump(null, diagnostics, null, "8.2", 0);
        json["command"]!.GetValue<string>().Should().Be("symbol_tree");
        json["namespaces"]!.AsArray().Should().BeEmpty();
        json["diagnostics"]!.AsArray().Should().NotBeEmpty();
    }

    private static JsonElement? FindSymbol(JsonElement root, string name)
    {
        foreach (var ns in root.GetProperty("namespaces").EnumerateArray())
        {
            foreach (var symbol in ns.GetProperty("symbols").EnumerateArray())
            {
                if (string.Equals(symbol.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return symbol;
                }
            }
        }

        return null;
    }

    private static TestProjectBuilder OverlayFixture()
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithConfigValue("no-cache", "true");
        builder.WithConfigValue("quiet", "true");
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpFile("pkg/composer.json", """
            {
                "name": "acme/pkg",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./_tyhpdef/*.tyhpdef"],
                            "overlay": [
                                "./_tyhpdef/overlays/stubs/*.tyhpdef",
                                "./_tyhpdef/overlays/*.tyhpdef"
                            ]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", """
            <?tyhpdef
            function \call_user_func(callable $callback): mixed;
            function \array_map(callable $callback, array $array): array;
            function \tyhp_symbol_tree_fn(): void;
            class \TyhpSymbolTreeType {
                public function getMessage(): string;
                public function getPrevious(): mixed;
            }
            """);
        return builder;
    }
}
