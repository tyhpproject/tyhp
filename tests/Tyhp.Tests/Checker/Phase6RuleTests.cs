using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase6RuleTests
{
    [Fact]
    public void Check_AwaitOutsideAsync_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                await loadAsync();
            }

            async function loadAsync(): int { return 1; }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInReturnOutsideAsync_ReportsError()
    {
        // ControlFlowRule suppresses child traversal on return statements; await inside
        // `return await …` must still be validated via the expression-tree walk.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            function fetchDataSync(int $id): string {
                return await fetchRawAsync($id);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInAsyncReturn_DoesNotReportOutsideAsync()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function fetchRawAsync(int $id): string {
                return "id-" . $id;
            }

            async function fetchDataAsync(int $id): string {
                return await fetchRawAsync($id);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInAsyncMethodReturn_DoesNotReportOutsideAsync()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Loader {
                async function fetchRawAsync(int $id): string {
                    return "id-" . $id;
                }

                async function fetchDataAsync(int $id): string {
                    return await $this->fetchRawAsync($id);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_AwaitInSyncMethodReturn_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Loader {
                async function fetchRawAsync(int $id): string {
                    return "id-" . $id;
                }

                function fetchDataSync(int $id): string {
                    return await $this->fetchRawAsync($id);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAwaitOutsideAsync);
    }

    [Fact]
    public void Check_VariableVariable_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $name = 'value';
                mixed $x = $$name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariableVariableProhibited);
    }

    [Fact]
    public void Check_CompactCall_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $a = 'hello';
                array $data = compact('a');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCompactProhibited);
    }

    [Fact]
    public void Check_AssignmentInCondition_ReportsWarning()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): bool {
                if ($x = true) {
                    return true;
                }
                return false;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerAssignmentInCondition);
    }

    [Fact]
    public void Check_WithInvalidProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }

            function demo(): void {
                Config $cfg = new Config();
                Config $copy = clone $cfg with [missing => true];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty);
    }

    [Fact]
    public void Check_WithInheritedProperty_IsAllowed()
    {
        // Inherited members are not flattened into Members; the rule must walk the base chain.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct SerializedExpression {
                string $nodeType = '';
            }

            struct SerializedParameterExpression extends SerializedExpression {
                string $name = '';
            }

            function demo(): SerializedParameterExpression {
                return new SerializedParameterExpression() with [
                    nodeType => 'parameter',
                    name => 'id',
                ];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty);
    }

    [Fact]
    public void Check_NewStructWith_MissingRequiredProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x;
                int $y = 0;
            }

            function demo(): Point {
                return new Point() with [y => 1];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_NewStructWith_RequiredPropertyProvided_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x;
                int $y = 0;
            }

            function demo(): Point {
                return new Point() with [x => 1];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_BareNewStruct_MissingRequiredProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x;
            }

            function demo(): Point {
                return new Point();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_StructPropertyType_UseImportInDeclaringFile_ResolvesAtAccessSite()
    {
        // Regression: PathNode-style structs declare property types via `use` in their own file.
        // Reading `$node->body` from another namespace must resolve that annotation against the
        // declaring file's imports — not the access site's namespace (which would yield
        // `\App\Node` instead of `\Lib\Node` and a TYHP4008 mismatch).
        var diagnostics = CompileAndCheckFiles(
            ("Node.tyhp", """
                <?tyhp
                namespace Lib;

                class Node {}
                """),
            ("Holder.tyhp", """
                <?tyhp
                namespace Lib\Structs;

                use Lib\Node;

                struct Holder {
                    Node $body;
                }
                """),
            ("Consumer.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Node;
                use Lib\Structs\Holder;

                function read(Holder $holder): Node {
                    Node $body = $holder->body;
                    return $body;
                }

                function write(): Holder {
                    return new Holder() with [
                        body => new Node(),
                    ];
                }
                """));

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_WithQuotedPropertyKey_ResolvesName()
    {
        // Property names colliding with Tyhp keywords / builtin type names are written quoted.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Node {
                string $type = '';
            }

            function demo(): Node {
                return new Node() with ['type' => 'binary'];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty);
    }

    [Fact]
    public void Check_WithQuotedUnknownPropertyKey_ReportsErrorWithName()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Node {
                string $type = '';
            }

            function demo(): Node {
                return new Node() with ['missing' => 'x'];
            }
            """);

        var error = diagnostics.Errors
            .Should().ContainSingle(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty).Subject;
        error.FormatParams.Should().Contain("missing");
    }

    [Fact]
    public void Check_EvalUsage_ReportsInfo()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                eval('echo 1;');
            }
            """);

        diagnostics.All.Should().Contain(d => d.Code == MessageCode.CheckerEvalUsage);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TraitProvidedProperty_DoesNotReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasName {
                public string $name = '';
            }

            class Person {
                use HasName;
            }

            function demo(): void {
                Person $p = new Person();
                $p->name = 'hello';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TraitUser_StillFlagsMissingProperty()
    {
        // CHECKER_GAPS P0 #8: using any trait must not blanket-suppress TYHP4134.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasName {
                public string $name = '';
            }

            class Person {
                use HasName;
            }

            function demo(): void {
                Person $p = new Person();
                $p->missing = 5;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_TransitiveTraitProvidedProperty_DoesNotReport()
    {
        // Trait A uses trait B; B declares the property. Person only writes `use A;`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasNameCore {
                public string $name = '';
            }

            trait HasName {
                use HasNameCore;
            }

            class Person {
                use HasName;
            }

            function demo(): void {
                Person $p = new Person();
                $p->name = 'hello';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_InheritedTraitProvidedProperty_DoesNotReport()
    {
        // The base class uses the trait; the property must be visible through inheritance too.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasName {
                public string $name = '';
            }

            class Base {
                use HasName;
            }

            class Person extends Base {
            }

            function demo(): void {
                Person $p = new Person();
                $p->name = 'hello';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DynamicPropertyAssignment_MissingProperty_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {
                public int $x = 0;
            }

            function demo(): void {
                Plain $p = new Plain();
                $p->missing = 5;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDynamicPropertyProhibited);
    }

    [Fact]
    public void Check_DuplicateImport_ReportsWarning()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App\Model;

            class User {}

            use App\Model\User;
            use App\Model\User;
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_SingleUnusedImport_ReportsUnusedButNotDuplicate()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;

            use App\Missing\Thing;

            class Demo {}
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_SingleUsedImport_ReportsNeitherUnusedNorDuplicate()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Model {
                class User {}
            }

            namespace App {
                use App\Model\User;

                function demo(): void {
                    User $u = new User();
                }
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInParameterType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(Widget $w): void {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInReturnType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): Widget {
                    return new Widget();
                }
                """));

        // Even if `new Widget()` also marks it, return-only probes should work — use abstract:
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInReturnTypeAnnotation_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): ?Widget {
                    return null;
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInStaticAccess_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {
                    public static function make(): void {}
                }
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): void {
                    Widget::make();
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInGenericTypeArg_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(array<string, Widget> $items): void {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInClosureParameterType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): void {
                    $f = function (Widget $w): void {};
                    $f(new Widget());
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInClosureReturnType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                function demo(): void {
                    $f = function (): Widget {
                        return new Widget();
                    };
                    $f();
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInCatchClauseType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("MyException.tyhp", """
                <?tyhp
                namespace Lib;
                class MyException extends \Exception {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\MyException;
                function demo(): void {
                    try {
                        echo 1;
                    } catch (MyException $e) {
                        echo 2;
                    }
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInStructPropertyType_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Widget;
                struct Holder {
                    Widget $widget;
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyInOperatorOverloadSignature_NotUnused()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Amount.tyhp", """
                <?tyhp
                namespace Lib;
                class Amount {}
                """),
            ("Uses.tyhp", """
                <?tyhp
                namespace App;
                use Lib\Amount;
                final class Money {
                    public int $cents = 0;
                    operator convert(self $v): Amount {
                        return new Amount();
                    }
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_RenamedImport_UsedThroughAlias_ReportsNothing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Model {
                class User {}
            }

            namespace App {
                use App\Model\User as Account;

                function demo(): void {
                    Account $u = new Account();
                }
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);
    }

    [Fact]
    public void Check_ImportedNamespace_UsedAsQualifiedPrefix_ReportsNothing()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Deep.tyhp", """
                <?tyhp
                namespace Lib\Inner;

                class Deep {}
                """),
            ("UsesPrefix.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Inner;

                function make(): void {
                    Inner\Deep $d = new Inner\Deep();
                }
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_FullyQualifiedReference_DoesNotCountAsImportUsage()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;

                class Widget {}
                """),
            ("UsesFqn.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Widget;

                function make(): void {
                    \Lib\Widget $w = new \Lib\Widget();
                }
                """));

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_Imports_AreAttributedPerFile()
    {
        var diagnostics = CompileAndCheckFiles(
            ("Widget.tyhp", """
                <?tyhp
                namespace Lib;

                class Widget {}
                """),
            ("UsesImport.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Widget;

                function make(): void {
                    Widget $w = new Widget();
                }
                """),
            ("UnusedImport.tyhp", """
                <?tyhp
                namespace App;

                use Lib\Widget;

                class Other {}
                """),
            ("NoImports.tyhp", """
                <?tyhp
                namespace App;

                class Plain {}
                """));

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerDuplicateImport);

        var unused = diagnostics.Warnings
            .Where(d => d.Code == MessageCode.CheckerUnusedImport)
            .ToList();
        unused.Should().ContainSingle();
        unused[0].FileName.Should().Contain("UnusedImport.tyhp");
        unused[0].Message.Should().Contain("Lib\\Widget");

        diagnostics.Warnings.Should().NotContain(d =>
            d.Code == MessageCode.CheckerUnusedImport
            && d.FileName.Contains("UsesImport.tyhp", StringComparison.Ordinal));
        diagnostics.Warnings.Should().NotContain(d =>
            d.Code == MessageCode.CheckerUnusedImport
            && d.FileName.Contains("NoImports.tyhp", StringComparison.Ordinal));
    }

    private static DiagnosticBag CompileAndCheck(string content) =>
        CompileAndCheck(content, requireNoBindErrors: true);

    private static DiagnosticBag CompileAndCheckAllowBindWarnings(string content) =>
        CompileAndCheck(content, requireNoBindErrors: false);

    private static DiagnosticBag CompileAndCheck(
        string content,
        bool requireNoBindErrors,
        string phpVersion = "8.2",
        bool experimentalReadonlyCloneWith = false)
    {
        return CompileAndCheckFiles(
            requireNoBindErrors,
            phpVersion,
            experimentalReadonlyCloneWith,
            ("test.tyhp", content));
    }

    private static DiagnosticBag CompileAndCheckFiles(params (string FileName, string Content)[] files) =>
        CompileAndCheckFiles(
            requireNoBindErrors: true,
            phpVersion: "8.2",
            experimentalReadonlyCloneWith: false,
            files);

    private static DiagnosticBag CompileAndCheckFiles(
        bool requireNoBindErrors,
        string phpVersion,
        bool experimentalReadonlyCloneWith,
        params (string FileName, string Content)[] files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePaths = new List<string>();
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = phpVersion,
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
                Checker = new CheckerOptions
                {
                    PhpVersion = phpVersion,
                    ExperimentalReadonlyCloneWith = experimentalReadonlyCloneWith,
                },
            };
            var result = compilationService.ParseFiles(filePaths, options);
            if (requireNoBindErrors)
            {
                var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
                bindErrors.Should().BeEmpty(
                    $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");
            }

            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics,
                symbolTree,
                result.GlobalScope!,
                options.Checker);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_WithReadonlyInPlace_Reports4141()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(): void {
                Color $c = new Color();
                $c with [alpha => 128];
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerWithReadonlyInPlace);
    }

    [Fact]
    public void Check_WithReadonlyClone_Php84_WithoutExperimental_Reports4139()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
    }

    [Fact]
    public void Check_WithReadonlyClone_Php85_Allows()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.5");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyInPlace);
    }

    [Fact]
    public void Check_WithReadonlyFinalClass_Php84_Reports4140()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
    }

    [Fact]
    public void Check_WithReadonlyClone_Php84_WithExperimental_AllowsNonFinal()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4", experimentalReadonlyCloneWith: true);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
    }

    [Fact]
    public void Check_WithReadonlyNew_Php84_AllowsNonFinal()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }

            function demo(): Color {
                return new Color() with [alpha => 128];
            }
            """, requireNoBindErrors: true, phpVersion: "8.4");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneWithReadonlyRequiresConfig);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyFinalClass);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerWithReadonlyInPlace);
    }

    [Fact]
    public void Check_CloneWith_AfterInferredNewWithAssignment_DoesNotReportUnknown()
    {
        // Regression: `$cfg = new Config() with [...]` must infer `$cfg` as Config on first
        // assignment so `clone $cfg with [...]` does not see `unknown` (TYHP4073).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Config {
                public string $name = "";
                public int $value = 0;
            }

            function demo_with(): void {
                $cfg = new Config() with [name => "test", value => 42];
                $clone = clone $cfg with [name => "updated"];
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCloneNonObject);
    }

    [Fact]
    public void Check_ExistenceGate_UnqualifiedName_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_WrongNamespace_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_EmptyName_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_OtherFunctionName_ReportsError()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('asdfasdfasdfasdf')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_FullyQualifiedName_Accepted()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists('\\App\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }

    [Fact]
    public void Check_ExistenceGate_NamespaceConcat_Accepted()
    {
        var diagnostics = CompileAndCheckAllowBindWarnings("""
            <?tyhp
            namespace App;
            if (!\function_exists(__NAMESPACE__ . '\\demo')) {
                function demo(): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExistenceGateInvalidName);
    }
}
