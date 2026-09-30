using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefExtGateTests
{
    [Fact]
    public void ParseFallbackConst()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            fallback const int GRAPHEME_EXTR_COUNT ?? 0;
            """);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        TyhpdefImportConstAst? constant = null;
        foreach (var child in result.Ast.AstChildren)
        {
            if (child is TyhpdefImportConstAst direct)
            {
                constant = direct;
            }

            if (child == null)
            {
                continue;
            }

            foreach (var nested in child.AstChildren)
            {
                if (nested is TyhpdefImportConstAst found)
                {
                    constant = found;
                }
            }
        }

        constant.Should().NotBeNull();
        constant!.IsFallback.Should().BeTrue();
        constant.Identifier.Should().Be("GRAPHEME_EXTR_COUNT");
    }

    [Fact]
    public void FallbackConst_IsVisibleWithoutAutoloadOrder()
    {
        using var builder = Project();
        WritePackage(builder, "pkg-a", "acme/pkg-a", null, """
            <?tyhpdef
            fallback const int GRAPHEME_EXTR_COUNT ?? 0;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().BeEmpty(Join(diagnostics));
        FindConstant(global, "GRAPHEME_EXTR_COUNT").Should().NotBeNull();
    }

    [Fact]
    public void TwoFallbackConsts_WithoutAutoloadOrder_Error()
    {
        using var builder = Project();
        WritePackage(builder, "pkg-a", "acme/pkg-a", null, """
            <?tyhpdef
            fallback const int GRAPHEME_EXTR_COUNT ?? 0;
            """);
        WritePackage(builder, "pkg-b", "acme/pkg-b", null, """
            <?tyhpdef
            fallback const int GRAPHEME_EXTR_COUNT ?? 0;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefFallbackConstOrderUnknown);
        FindConstant(global, "GRAPHEME_EXTR_COUNT").Should().BeNull();
    }

    [Fact]
    public void ExtPackage_HidesNegativeExtGate()
    {
        using var builder = Project();
        WritePackage(builder, "intl", "acme/intl-stubs", "intl", """
            <?tyhpdef
            const int GRAPHEME_EXTR_COUNT ?? 0;
            """);
        WritePackage(builder, "polyfill", "acme/polyfill", null, """
            <?tyhpdef
            declare(ext="!intl") {
                fallback const int GRAPHEME_EXTR_COUNT ?? 1;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().BeEmpty(Join(diagnostics));
        var constant = FindConstant(global, "GRAPHEME_EXTR_COUNT");
        constant.Should().NotBeNull();
        constant!.IsFallback.Should().BeFalse();
        constant.SourceFile.Should().Contain("intl");
    }

    [Fact]
    public void TyhpSource_NegativeExtGate_StillCompiles_WhenExtensionPackageIsLoaded()
    {
        // In `.tyhp` the negative arm is the runtime fallback for a PHP without the extension, so
        // it must compile even though an intl tyhpdef is loaded. The adjacent pair joins as
        // if/else, so `$value` is definitely assigned.
        using var builder = Project();
        WritePackage(builder, "intl", "acme/intl-stubs", "intl", """
            <?tyhpdef
            function grapheme_strlen(string $string): int|false;
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function pick(): string {
                ?string $value = null;
                declare(ext="intl") {
                    $value = 'intl';
                }
                declare(ext="!intl") {
                    $value = 'plain';
                }
                return $value;
            }
            """);

        var result = IsolatedCompilation.BindProject(builder, skipChecking: false);
        result.Diagnostics.Errors.Should().BeEmpty(Join(result.Diagnostics));
    }

    [Fact]
    public void TyhpSource_ComplementaryExtBlocks_EmitOneIfElse()
    {
        using var builder = Project();
        WritePackage(builder, "intl", "acme/intl-stubs", "intl", """
            <?tyhpdef
            function grapheme_strlen(string $string): int|false;
            """);
        WritePackage(builder, "polyfill", "acme/iconv-stubs", "iconv", """
            <?tyhpdef
            function iconv_strlen(string $string): int|false;
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function pick(): string {
                string $value = 'none';
                declare(ext="intl") {
                    $value = 'intl';
                }
                declare(ext="!intl") {
                    declare(ext="!iconv") {
                        $value = 'plain';
                    }
                    declare(ext="iconv") {
                        $value = 'iconv';
                    }
                }
                return $value;
            }
            """);

        var result = IsolatedCompilation.BindProject(builder);
        var context = Tyhp.TyhpLang.Emitter.EmitContext.Create(
            result.GlobalScope, result.Diagnostics, builder.BuildProject());
        var php = string.Join(
            '\n',
            new Tyhp.TyhpLang.Emitter.TyhpEmitter(context).Emit(result.ParsedFiles!).Select(f => f.GeneratedContent));

        php.Should().Contain("if (\\extension_loaded('intl')) {");
        php.Should().Contain("} else {");
        php.Should().NotContain("if (!\\extension_loaded('intl'))");
        // The pair is written negative-first in source, and keeps that order.
        php.Should().Contain("if (!\\extension_loaded('iconv')) {");
        php.Should().NotContain("if (\\extension_loaded('iconv'))");
        php.Should().Contain("$value = 'iconv';");
        php.Split("} else {").Length.Should().Be(3, "each pair shares one else");
    }

    [Fact]
    public void TyhpSource_SameFunctionUnderComplementaryExtGates_IsNotADuplicate()
    {
        using var builder = Project();
        WritePackage(builder, "intl", "acme/intl-stubs", "intl", """
            <?tyhpdef
            function grapheme_strlen(string $string): int|false;
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            declare(ext="intl") {
                function len(string $s): int { return 1; }
            }
            declare(ext="!intl") {
                function len(string $s): int { return 2; }
            }
            """);

        var result = IsolatedCompilation.BindProject(builder, skipChecking: false);
        result.Diagnostics.Errors.Should().BeEmpty(Join(result.Diagnostics));
    }

    [Fact]
    public void NegativeExtGate_KeptWhenExtensionPackageIsAbsent()
    {
        using var builder = Project();
        WritePackage(builder, "polyfill", "acme/polyfill", null, """
            <?tyhpdef
            declare(ext="!intl") {
                fallback const int GRAPHEME_EXTR_COUNT ?? 0;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().BeEmpty(Join(diagnostics));
        FindConstant(global, "GRAPHEME_EXTR_COUNT").Should().NotBeNull();
    }

    [Fact]
    public void ExtRequire_BeatsFallbackFunction_WithoutPhpExtPackageName()
    {
        using var builder = Project();
        WritePackage(builder, "intl", "acme/intl-stubs", "intl", """
            <?tyhpdef
            function grapheme_strlen(string $string): int;
            """);
        WritePackage(builder, "polyfill", "acme/polyfill", null, """
            <?tyhpdef
            fallback function grapheme_strlen(string $string): int|false;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefFallbackRedeclare);
        var function = FindFunction(global, "grapheme_strlen");
        function.Should().NotBeNull();
        function!.IsFallback.Should().BeFalse();
        function.SourceFile.Should().Contain("intl");
    }

    [Fact]
    public void Harvest_DefinedGate_AndExtensionReturn_AndVersionRequire()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-gates-out-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-gates-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "bootstrap.php"), """
                <?php
                if (PHP_VERSION_ID >= 80300) {
                    return require __DIR__ . '/bootstrap80.php';
                }
                if (!function_exists('demo_old')) {
                    function demo_old(): void {}
                }
                if (extension_loaded('intl')) {
                    return;
                }
                if (!defined('DEMO_COUNT')) {
                    define('DEMO_COUNT', 0);
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "bootstrap80.php"), """
                <?php
                if (!function_exists('demo_new')) {
                    function demo_new(): void {}
                }
                """);
            var options = new TyhpdefGenerationOptions
            {
                OutputDirectory = outputDir,
                IncludeDocComments = false,
                IncludeDeprecated = true,
                Overwrite = true,
                Split = "file",
                FetchStubCache = false,
                StubCacheDirectory = Path.Combine(outputDir, "missing-stubs"),
                CatalogRoots = [],
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = new TyhpdefGenerationResult();
            new NativeTyhpdefGenerator().Generate(options, result, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = string.Join("\n", result.GeneratedFiles.Select(File.ReadAllText));
            text.Should().Contain("fallback function demo_old");
            text.Should().Contain("fallback const int DEMO_COUNT");
            text.Should().Contain("declare(ext=\"!intl\")");
            text.Should().Contain("declare(php=\">=8.3\")");
            text.Should().Contain("fallback function demo_new");
            text.Should().Contain("declare(php=\"<8.3\")");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    private static TestProjectBuilder Project()
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");
        builder.WithConfigValue("tyhpdefInclude:2", "./intl/composer.json");
        builder.WithConfigValue("tyhpdefInclude:3", "./polyfill/composer.json");
        return builder;
    }

    private static void WritePackage(
        TestProjectBuilder builder,
        string directory,
        string packageName,
        string? extension,
        string tyhpdef)
    {
        var require = extension is null
            ? ""
            : "\"require\": { \"ext-" + extension + "\": \"*\" },\n";
        var json = """
            {
                "name": "PACKAGE",
                REQUIRE
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./_tyhpdef/*.tyhpdef"]
                        }
                    }
                }
            }
            """.Replace("PACKAGE", packageName, StringComparison.Ordinal)
            .Replace("REQUIRE", require, StringComparison.Ordinal);
        builder.WithTyhpFile($"{directory}/composer.json", json);
        builder.WithTyhpFile($"{directory}/_tyhpdef/types.tyhpdef", tyhpdef);
    }

    private static (GlobalScope Global, Tyhp.Domain.Diagnostics.DiagnosticBag Diagnostics) Bind(TestProjectBuilder builder)
    {
        var result = IsolatedCompilation.BindProject(builder);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static ConstantSymbol? FindConstant(GlobalScope global, string name)
    {
        ConstantSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ConstantSymbol constant
                && string.Equals(constant.Name, name, StringComparison.Ordinal))
            {
                found = constant;
            }
        });
        return found;
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol func
                && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = func;
            }
        });
        return found;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }

    private static string Join(Tyhp.Domain.Diagnostics.DiagnosticBag diagnostics)
        => string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
