using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
public class BinderTestHelper
{
    private static CompilationOptions CreateOptions()
        => new()
        {
            EnableAstCache = false,
            PhpVersion = "8.2",
            ProjectPath = TestFileManager.GetRepoRoot(),
            TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            SkipChecking = true,
        };

    public static (GlobalScope? Global, DiagnosticBag Diagnostics) BindFile(string filePath)
    {
        using var compilationService = new CompilationService();
        var result = compilationService.ParseFiles(new[] { filePath }, CreateOptions());
        return (result.GlobalScope, result.Diagnostics);
    }

    public static (GlobalScope? Global, DiagnosticBag Diagnostics) BindFiles(params string[] filePaths)
    {
        using var compilationService = new CompilationService();
        var result = compilationService.ParseFiles(filePaths, CreateOptions());
        return (result.GlobalScope, result.Diagnostics);
    }

    public static (GlobalScope? Global, DiagnosticBag Diagnostics) BindContent(string content, string fileName = "test.tyhp")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);
        try
        {
            return BindFile(filePath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}

[Trait("Category", "Binder")]
public class ScopeTreeTests
{
    [Fact]
    public void Bind_SimpleClassInNamespace_BuildsNamespaceHierarchy()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/simple_class_in_namespace.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();
        global!.FileScopeCount.Should().BeGreaterThanOrEqualTo(1);
        global.NamespaceScopeCount.Should().BeGreaterThan(0);
        ((IBaseScope)global!).GetAllChildScopes().OfType<NamespaceScope>().Should().NotBeEmpty();
    }

    [Fact]
    public void Bind_MultipleNamespaces_CreatesMultipleNamespaceScopes()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/multiple_namespaces.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        ((IBaseScope)global!).GetAllChildScopes().OfType<NamespaceScope>().Count().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Bind_NestedScopes_CreatesFunctionAndBlockScopes()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/nested_scopes.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        global!.TotalScopeCount.Should().BeGreaterThan(3);
    }
}

[Trait("Category", "Binder")]
public class SymbolRegistrationTests
{
    [Fact]
    public void Bind_ClassWithMembers_RegistersSymbols()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/class_with_members.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        global!.TotalSymbolCount.Should().BeGreaterThan(0);
    }
}

[Trait("Category", "Binder")]
public class NameResolutionTests
{
    // `CLI_DiagnosticDidYouMean` resolves through Message's embedded-resource fallback, so these
    // tests must not install a stub localizer: Message.SetLocalizer writes global state that no
    // test can undo, and a stub that echoes unknown keys leaves every later test in the run
    // reading "ERROR_TYHP####" instead of the real diagnostic text.

    [Fact]
    public void Bind_UnresolvedReference_ProducesBinderSymbolNotFound()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function test(UnknownType $missing): void {
            }
            """);

        global.Should().NotBeNull();
        // An unresolved *parameter* type is reported with the dedicated
        // BinderUnresolvedParameterType (3020) code, not the generic BinderSymbolNotFound (3003).
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
    }

    [Fact]
    public void Bind_AllLiteralParameterAndReturnTypes_DoNotReport3019Or3020()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function local(): void { 'red'|'green' $color = 'red'; }
            class Props { public 'red'|'green' $prop = 'red'; }
            function a(false|'red'|'green' $c): void {}
            function b(int|'red' $c): void {}
            function c('red'|'green' $c): void {}
            function d('a' $c): 'a' { return $c; }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.BinderUnresolvedParameterType
                || d.Code == MessageCode.BinderUnresolvedReturnType,
            "all-literal param/return types must resolve (FOUND_BUGS #35)");
        diagnostics.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Bind_UnresolvedTypeNearExisting_AttachesDidYouMeanSuggestion()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class User {}
            function test(Useer $missing): void {
            }
            """);

        global.Should().NotBeNull();
        var error = diagnostics.Errors.Should()
            .ContainSingle(d => d.Code == MessageCode.BinderUnresolvedParameterType)
            .Subject;

        error.Suggestion.Should().NotBeNull();
        error.Suggestion!.Value.Replacement.Should().Be("User");
        error.Help.Should().Be("did you mean `User`?");
    }

    [Fact]
    public void Bind_UnresolvedQualifiedType_SuggestionSpansOnlyTheFinalSegment()
    {
        const string source = """
            <?tyhp
            namespace App;
            class User {}
            function test(\App\Useer $missing): void {
            }
            """;

        var (_, diagnostics) = BinderTestHelper.BindContent(source);

        var error = diagnostics.Errors.Should()
            .ContainSingle(d => d.Code == MessageCode.BinderUnresolvedParameterType)
            .Subject;

        error.Suggestion.Should().NotBeNull();
        var suggestion = error.Suggestion!.Value;
        var line = source.Split('\n')[suggestion.Span.Line - 1];
        line[suggestion.Span.Column..suggestion.Span.EndColumn!.Value].Should().Be("Useer");
        suggestion.Replacement.Should().Be("User");
    }

    [Fact]
    public void Bind_UnresolvedUnionType_DoesNotSuggestAcrossTheWholeUnion()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class UserName {}
            function test(Usr|Name $missing): void {
            }
            """);

        var error = diagnostics.Errors.Should()
            .ContainSingle(d => d.Code == MessageCode.BinderUnresolvedParameterType)
            .Subject;

        error.Suggestion.Should().BeNull();
        error.Help.Should().BeNull();
    }

    // FOUND_BUGS #33: anonymous classes inside method/function bodies live in
    // CodeBlockScope._additionalChildScopes; resolution must visit GetAllChildScopes().
    [Fact]
    public void Bind_AnonymousClassInsideMethod_UnresolvedParameterType_Reports3020()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class C {
                public function go(): void {
                    $obj = new class {
                        public function inner(NopeC $x): void {}
                    };
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
    }

    [Fact]
    public void Bind_AnonymousClassInsideMethod_UnresolvedReturnType_Reports3019()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class C {
                public function go(): void {
                    $obj = new class {
                        public function inner(): NopeReturn {}
                    };
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Bind_AnonymousClassInsideFreeFunction_UnresolvedParameterType_Reports3020()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function make(): void {
                $obj = new class {
                    public function inner(NopeInFunc $x): void {}
                };
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
    }

    [Fact]
    public void Bind_FileScopeAnonymousClass_StillReportsUnresolvedParameterType()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            $top = new class {
                public function inner(NopeD $x): void {}
            };
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
    }

    // FOUND_BUGS #36: named functions inside method/function bodies must be bound and resolved.
    [Fact]
    public void Bind_NestedNamedFunctionInsideMethod_UnresolvedParameterType_Reports3020()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function control(NopeA $x): void {}
            class C {
                public function go(): void {
                    function nested(NopeB $x): void {}
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.BinderUnresolvedParameterType
            && d.Message.Contains("NopeA", StringComparison.Ordinal));
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.BinderUnresolvedParameterType
            && d.Message.Contains("NopeB", StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_NestedNamedFunctionInsideMethod_UnresolvedReturnType_Reports3019()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class C {
                public function go(): void {
                    function nested(): NopeRet {}
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Bind_NestedNamedFunctionInsideFreeFunction_UnresolvedParameterType_Reports3020()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function outer(): void {
                function nested(NopeInFunc $x): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
    }

    [Fact]
    public void Bind_ValidNestedNamedFunction_BindsWithoutBinderErrors()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class C {
                public function go(): void {
                    function nested(int $x): int {
                        return $x;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedParameterType
            || d.Code == MessageCode.BinderUnresolvedReturnType
            || d.Code == MessageCode.BinderUnknownError);
    }
}

[Trait("Category", "Binder")]
public class DuplicateDeclarationTests
{
    [Fact]
    public void Bind_SameNamespaceUseAlias_DoesNotCollideWithClassInOtherFile()
    {
        // use aliases are file-local but share the class-like name index. A sibling file's
        // `use Tyhp\Type` must not TYHP3002 the real `class Type` declaration (core GenericObject).
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var aliasPath = Path.Combine(tempDir, "alias.tyhp");
        var classPath = Path.Combine(tempDir, "class.tyhp");
        File.WriteAllText(aliasPath, """
            <?tyhp
            namespace App;
            use App\Type;
            class Consumer {
                public function go(): Type {
                    return new Type();
                }
            }
            """);
        File.WriteAllText(classPath, """
            <?tyhp
            namespace App;
            class Type {}
            """);

        try
        {
            var (_, diagnostics) = BinderTestHelper.BindFiles(aliasPath, classPath);
            diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Bind_DuplicateClassInSameNamespace_ProducesDuplicateDiagnostic()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/duplicate_class.tyhp");
        var (_, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void Bind_DuplicateFunctionAcrossFilesSameNamespace_ProducesDuplicateDiagnostic()
    {
        // Each file gets its own NamespaceBlockScope; uniqueness must still be enforced across them.
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var onePath = Path.Combine(tempDir, "one.tyhp");
        var twoPath = Path.Combine(tempDir, "two.tyhp");
        File.WriteAllText(onePath, """
            <?tyhp
            namespace App;
            function demo(): void {}
            """);
        File.WriteAllText(twoPath, """
            <?tyhp
            namespace App;
            function demo(): void {}
            """);

        try
        {
            var (_, diagnostics) = BinderTestHelper.BindFiles(onePath, twoPath);
            diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Bind_DebugProject_HasNoBinderDuplicateDeclarations()
    {
        // DebugProject exercises multi-resource using blocks; those must not false-report TYHP3002.
        var projectPath = Path.Combine(TestFileManager.GetRepoRoot(), "DebugProject");
        var projectFile = Path.Combine(projectPath, "tyhp.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(projectFile, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = projectFile,
            })
            .Build();
        var project = new Project(configuration);
        var files = project.GetProjectSourceFiles().Select(Path.GetFullPath).ToArray();

        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project);
        options.EnableAstCache = false;
        options.SkipChecking = true;
        var result = compilationService.ParseFiles(files, options);

        var dups = result.Diagnostics.Errors
            .Where(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
            .Select(d => $"{Path.GetFileName(d.FileName)}:{string.Join(",", d.FormatParams ?? [])}")
            .ToList();

        dups.Should().BeEmpty($"unexpected binder duplicates: {string.Join("; ", dups)}");
    }

    [Fact]
    public void Bind_SameFunctionNameDifferentNamespaces_IsAllowed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var onePath = Path.Combine(tempDir, "one.tyhp");
        var twoPath = Path.Combine(tempDir, "two.tyhp");
        File.WriteAllText(onePath, """
            <?tyhp
            namespace App\One;
            function demo(): void {}
            """);
        File.WriteAllText(twoPath, """
            <?tyhp
            namespace App\Two;
            function demo(): void {}
            """);

        try
        {
            var (_, diagnostics) = BinderTestHelper.BindFiles(onePath, twoPath);
            diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Bind_ClassAndFunctionSameNameSameNamespace_IsAllowed()
    {
        // PHP keeps functions and classes in separate symbol namespaces.
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            class demo {}
            function demo(): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void Bind_MultiResourceUsingBlock_DoesNotReportFalseDuplicates()
    {
        // Resource vars are registered on the using CodeBlockScope; Variable must be an
        // allowed child type or AddChildSymbol fails and is misreported as TYHP3002.
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function demo(): void {
                using ($db1 = new stdClass(), $db2 = new stdClass()) {
                    $test = $db1;
                }
                using await ($a = new stdClass(), $b = new stdClass()) {
                    $test2 = $a;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void Bind_DuplicateResourceNameInUsingBlock_ProducesDuplicateDiagnostic()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            function demo(): void {
                using ($db = new stdClass(), $db = new stdClass()) {
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void Bind_ConstAndSameNamedMethod_AreAllowed()
    {
        // PHP: class constants and methods are separate namespaces (Prop-init #25).
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            final class Widget {
                public const TAG = 'w';
                public function tag() { return Widget::TAG; }
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration,
            $"const TAG + tag() must bind: {string.Join("; ", diagnostics.Errors.Select(e => e.Message))}");

        var widget = FindObject(global!, "Widget");
        widget.TryGetConstant("TAG", out var constant).Should().BeTrue();
        constant.Should().BeOfType<ObjectConstantSymbol>();
        widget.Members.Should().ContainKey("tag");
        widget.Members["tag"].Should().BeOfType<ObjectMethodSymbol>();
        // Case-sensitive constant namespace: wrong-case lookup must not hit TAG.
        widget.TryGetConstant("tag", out _).Should().BeFalse();
    }

    [Fact]
    public void Bind_ConstPropertyAndMethodSameSpelling_AreAllAllowed()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            class Widget {
                public const TAG = 'w';
                public string $tag = 'p';
                public function tag(): string { return $this->tag; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);

        var widget = FindObject(global!, "Widget");
        widget.TryGetConstant("TAG", out _).Should().BeTrue();
        widget.Members.Should().ContainKey("$tag");
        widget.Members.Should().ContainKey("tag");
        widget.Members["tag"].Should().BeOfType<ObjectMethodSymbol>();
    }

    [Fact]
    public void Bind_DuplicateMethodsDifferingOnlyByCase_ReportsDuplicate()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            class Widget {
                public function tag(): string { return 'a'; }
                public function TAG(): string { return 'b'; }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void Bind_DuplicateConstantsSameExactName_ReportsDuplicate()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            class Widget {
                public const TAG = 'a';
                public const TAG = 'b';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void Bind_ConstantsDifferingOnlyByCase_AreAllowed()
    {
        // PHP class-constant names are case-sensitive.
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            class Widget {
                public const tag = 'a';
                public const TAG = 'b';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        var widget = FindObject(global!, "Widget");
        widget.TryGetConstant("tag", out _).Should().BeTrue();
        widget.TryGetConstant("TAG", out _).Should().BeTrue();
    }

    [Fact]
    public void Bind_EnumCaseAndSameNamedMethod_AreAllowed()
    {
        // Enum cases share the case-sensitive constant namespace, so `case Red` and a method
        // `red()` (different case) — or even identical-looking names on `->`/`::` — must coexist.
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            enum Color {
                case Red;
                function red(): string { return 'r'; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);

        var colorEnum = FindObject(global!, "Color");
        colorEnum.TryGetConstant("Red", out var enumCase).Should().BeTrue();
        enumCase.Should().BeOfType<ObjectConstantSymbol>();
        ((ObjectConstantSymbol)enumCase).IsEnumCase.Should().BeTrue();
        colorEnum.Members.Should().ContainKey("red");
        colorEnum.Members["red"].Should().BeOfType<ObjectMethodSymbol>();
    }

    [Fact]
    public void Bind_InheritedConstantAndSubclassSameNamedMethod_BothResolve()
    {
        // A constant declared on a base class must not shadow (or be shadowed by) a same-named
        // method declared on a derived class — the two live in independent namespaces at every
        // level of the hierarchy, not just within a single class body.
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            class Base {
                public const TAG = 'base';
            }
            class Derived extends Base {
                public function tag(): string { return 'derived'; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);

        var symbolTree = new SymbolTree(global!);
        var derived = FindObject(global!, "Derived");

        var resolvedConstant = symbolTree.ResolveConstant("TAG", derived, new DiagnosticBag());
        resolvedConstant.Should().BeOfType<ObjectConstantSymbol>();

        var resolvedMember = symbolTree.ResolveMember("tag", derived, new DiagnosticBag());
        resolvedMember.Should().BeOfType<ObjectMethodSymbol>();
    }

    private static ObjectDeclarationSymbol FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null) return;
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol obj
                    && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = obj;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        found.Should().NotBeNull($"expected to find class '{name}'");
        return found!;
    }
}

[Trait("Category", "Binder")]
public class NamespaceMergingTests
{
    [Fact]
    public void Bind_TwoFilesSameNamespace_MergesIntoOneNamespaceScope()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var onePath = Path.Combine(tempDir, "one.tyhp");
        var twoPath = Path.Combine(tempDir, "two.tyhp");
        File.WriteAllText(onePath, "<?tyhp\nnamespace App;\nclass One {}\n");
        File.WriteAllText(twoPath, "<?tyhp\nnamespace App;\nclass Two {}\n");

        try
        {
            var (global, diagnostics) = BinderTestHelper.BindFiles(onePath, twoPath);
            diagnostics.HasErrors.Should().BeFalse();
            global!.FindNamespaceScope("App").Should().NotBeNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}

[Trait("Category", "Binder")]
public class TraitBindingTests
{
    [Fact]
    public void Bind_TraitUsage_BindsWithoutErrors()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/trait_usage.tyhp");
        var (_, diagnostics) = BinderTestHelper.BindFile(path);
        diagnostics.HasErrors.Should().BeFalse();
    }
}

[Trait("Category", "Binder")]
public class GenericBindingTests
{
    [Fact]
    public void Bind_GenericClass_BindsWithoutErrors()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/generic_class.tyhp");
        var (_, diagnostics) = BinderTestHelper.BindFile(path);
        diagnostics.HasErrors.Should().BeFalse();
    }
}

[Trait("Category", "Binder")]
public class TyhpdefLoadingTests
{
    [Fact]
    public void Bind_MinimalTyhpdefClass_RegistersSymbols()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "MinimalTyhpdef/simple_class.tyhpdef");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);
        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();
        global!.TotalSymbolCount.Should().BeGreaterThan(0);
    }
}
