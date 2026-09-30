using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.1 / 21.10: <c>.tyhp</c> use of a tyhpdef <c>extern</c> name (type, function, or const) is TYHP4307.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Tyhpdef")]
public class ExternTypeUseRuleTests
{
    private const string ExternTyhpdef = """
        <?tyhpdef
        // @provided-by: tyhpdef/acme-search
        extern class \ExternNs\T;
        extern class \ExternNs\Client;
        extern class \ExternNs\Document;
        class \Consumer\Handler {
            public function __construct(\ExternNs\Client $client);
            public function getDocument(): \ExternNs\Document;
        }
        """;

    [Fact]
    public void Check_TyhpTypeHintOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(\ExternNs\T $value): void {}
            """);

        var error = diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerExternTypeUsed).Subject;
        Convert.ToString(error.FormatParams[0]).Should().Contain("T");
        error.Labels.Should().NotBeEmpty();
        error.Labels[0].Message.Should().Be(Message.Localize("CLI_DiagnosticLabelDeclaredHere"));
        error.Help.Should().Be("tyhpdef/acme-search");
    }

    [Fact]
    public void Check_TyhpReturnTypeOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(): \ExternNs\T {
                throw new \Exception('no');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_TyhpPropertyTypeOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            class Holder {
                public \ExternNs\T $value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_NewOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(): void {
                new \ExternNs\T();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_CallReturningExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(\Consumer\Handler $handler): void {
                $handler->getDocument();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_MentionRealClassWhoseCtorTakesExtern_IsOkUntilCall()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function mention(\Consumer\Handler $handler): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_ConstructRealClassPassingExternParam_Reports4307AtArgument()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(mixed $client): void {
                new \Consumer\Handler($client);
            }
            """);

        var error = diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed).Which;
        Convert.ToString(error.FormatParams[0]).Should().Contain("Client");
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_UnionWithOneExternArm_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(\ExternNs\T|\Consumer\Handler $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_InstanceofExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(mixed $value): void {
                if ($value instanceof \ExternNs\T) {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_CatchExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(): void {
                try {
                } catch (\ExternNs\T $e) {
                    mixed $unused = $e;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_UseImportOfExternFqcn_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            use ExternNs\T;
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_UseGroupImportOfExternFqcn_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            use ExternNs\{ T };
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_TyhpdefSignaturesNamingExtern_DoNotReport4307()
    {
        var diagnostics = CompileTyhpdefAsSource("""
            <?tyhpdef
            extern class \ExternNs\T;
            class \Consumer\C {
                public function f(\ExternNs\T $x): \ExternNs\T;
                public \ExternNs\T $prop;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
        var fromLib = diagnostics.Errors.Where(d => (d.FileName ?? "").EndsWith("lib.tyhpdef", StringComparison.Ordinal)).ToList();
        fromLib.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        fromLib.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Check_ExtendsExtern_Reports3026Not4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            class Sub extends \ExternNs\T {
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderExternExtendsType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_AfterRealWins_UseIsReal()
    {
        var diagnostics = CompileAndCheckWithTyhpdefs(
            """
            <?tyhp
            function demo(\MergeNs\T $value): void {}
            """,
            """
            <?tyhpdef
            extern class \MergeNs\T;
            """,
            """
            <?tyhpdef
            class \MergeNs\T {
                public function realMethod(): void;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_GenericArgumentNamingExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            class Box<TValue> {}
            function demo(Box<\ExternNs\T> $box): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_IntersectionWithExternArm_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            extern interface \ExternNs\Base {
                public function base(): void;
            }
            extern interface \ExternNs\Sub extends \ExternNs\Base {
                public function sub(): void;
            }
            """, """
            <?tyhp
            function demo(\ExternNs\Sub&\ExternNs\Base $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_NullableExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(ExternTyhpdef, """
            <?tyhp
            function demo(?\ExternNs\T $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_StaticCallReturningExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            extern class \ExternNs\Document;
            class \Consumer\Factory {
                public static function make(): \ExternNs\Document;
            }
            """, """
            <?tyhp
            function demo(): void {
                \Consumer\Factory::make();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_TyhpCallOfExternFunction_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-math
            extern function \widget_add;
            """, """
            <?tyhp
            function demo(): void {
                \widget_add();
            }
            """);

        var error = diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerExternTypeUsed).Subject;
        Convert.ToString(error.FormatParams[0]).Should().Contain("widget_add");
        error.Labels.Should().NotBeEmpty();
        error.Labels[0].Message.Should().Be(Message.Localize("CLI_DiagnosticLabelDeclaredHere"));
        error.Help.Should().Be("tyhpdef/acme-math");
    }

    [Fact]
    public void Check_TyhpConstFetchOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-int
            extern const \WIDGET_ROUND_PLUSINF;
            """, """
            <?tyhp
            function demo(): void {
                mixed $mode = \WIDGET_ROUND_PLUSINF;
            }
            """);

        var error = diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerExternTypeUsed).Subject;
        Convert.ToString(error.FormatParams[0]).Should().Contain("WIDGET_ROUND_PLUSINF");
        error.Help.Should().Be("tyhpdef/acme-int");
    }

    [Fact]
    public void Check_UseFunctionImportOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-math
            extern function \widget_add;
            """, """
            <?tyhp
            use function widget_add;
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_UseConstImportOfExtern_Reports4307()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            // @provided-by: tyhpdef/acme-int
            extern const \WIDGET_ROUND_PLUSINF;
            """, """
            <?tyhp
            use const WIDGET_ROUND_PLUSINF;
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_AfterRealWins_FunctionCallIsReal()
    {
        var diagnostics = CompileAndCheckWithTyhpdefs(
            """
            <?tyhp
            function demo(): void {
                \widget_add("1", "2");
            }
            """,
            """
            <?tyhpdef
            extern function \widget_add;
            """,
            """
            <?tyhpdef
            function \widget_add(string $a, string $b): string;
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_AfterRealWins_ConstFetchIsReal()
    {
        var diagnostics = CompileAndCheckWithTyhpdefs(
            """
            <?tyhp
            function demo(): void {
                mixed $mode = \WIDGET_ROUND_PLUSINF;
            }
            """,
            """
            <?tyhpdef
            extern const \WIDGET_ROUND_PLUSINF;
            """,
            """
            <?tyhpdef
            const int \WIDGET_ROUND_PLUSINF ?? 1;
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    [Fact]
    public void Check_TyhpdefSignaturesAndMappingsNamingExternFunctionOrConst_DoNotReport4307()
    {
        var diagnostics = CompileTyhpdefAsSource("""
            <?tyhpdef
            extern function \widget_add;
            extern const \WIDGET_ROUND_PLUSINF;
            function \round_mode(int $mode = \WIDGET_ROUND_PLUSINF): int;
            extension Bc {
                fn add(string $a, string $b): string => \widget_add($a, $b);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExternTypeUsed);
    }

    private static DiagnosticBag CompileAndCheckWithTyhpdef(string tyhpdef, string tyhp)
        => CompileAndCheckWithTyhpdefs(tyhp, tyhpdef);

    private static DiagnosticBag CompileAndCheckWithTyhpdefs(string tyhp, params string[] tyhpdefs)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpPath = Path.Combine(tempDir, "app.tyhp");
        File.WriteAllText(tyhpPath, tyhp);
        var includes = new List<string>();
        for (var i = 0; i < tyhpdefs.Length; i++)
        {
            var tyhpdefPath = Path.Combine(tempDir, $"types{i}.tyhpdef");
            File.WriteAllText(tyhpdefPath, tyhpdefs[i]);
            includes.Add(tyhpdefPath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.2",
                skipChecking: true,
                tyhpdefIncludePaths: includes);
            var result = compilationService.ParseFiles([tyhpPath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static DiagnosticBag CompileTyhpdefAsSource(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "lib.tyhpdef");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.2",
                skipChecking: true,
                configure: o => o.Checker = new CheckerOptions { PhpVersion = "8.2" });
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
