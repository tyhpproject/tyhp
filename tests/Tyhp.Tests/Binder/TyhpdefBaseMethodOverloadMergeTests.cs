using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// FOUND_BUGS #11: base (non-overlay) tyhpdef class bodies must merge same-name methods with
/// distinct parameter shapes into <see cref="ObjectMethodSymbol.Overloads"/> — mirrors
/// <c>DatePeriod::__construct</c>'s three real PHP overloads
/// (docs/content/tyhpdef_overloadedFunctions.md) — while an identical-shape repeat still hard
/// -errors <see cref="MessageCode.TyhpdefDuplicateDeclaration"/> (TYHP8002). Covers
/// <c>TyhpBinder.TryAddTyhpdefMethodOverload</c>, exercised via <c>BindMethodDecl</c>
/// (Tyhp.TyhpLang.Binder.TyhpBinder.ObjectBody.cs).
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefBaseMethodOverloadMergeTests
{
    [Fact]
    public void DatePeriodShapedConstructors_MergeAsOverloads_NoDuplicateError()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            class \DatePeriodProbe {
                public function __construct(\DateTimeInterface $start, \DateInterval $interval, int $recurrences, int $options = 0): void;
                public function __construct(\DateTimeInterface $start, \DateInterval $interval, \DateTimeInterface $end, int $options = 0): void;
                deprecated public function __construct(string $isostr, int $options = 0): void;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);

        var probe = FindObject(global, "DatePeriodProbe");
        probe.Should().NotBeNull();
        var ctor = probe!.Members["__construct"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        ctor.Overloads.Should().HaveCount(2, "3 distinct-shape constructors: 1 primary + 2 merged overloads");
    }

    [Fact]
    public void SingleClassBody_DistinctShapeSameName_MergesAsOverload()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            class \Lib\Widget {
                public function make(int $a): void;
                public function make(string $a, int $b): void;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);

        var widget = FindObject(global, "Widget");
        widget.Should().NotBeNull();
        var make = widget!.Members["make"].Should().BeAssignableTo<ObjectMethodSymbol>().Subject;
        make.Overloads.Should().HaveCount(1);
    }

    [Fact]
    public void SingleClassBody_IdenticalShapeSameName_StillReports8002()
    {
        // Regression for the non-partial path: PartialDuplicateMember_Reports8002
        // (Story20Phase6BinderCheckerTests) covers the include-layer partial-merge fragment;
        // this covers the plain single-class-body declaration.
        var (_, diagnostics) = Bind("""
            <?tyhpdef
            class \Lib\Gadget {
                public function make(int $a): void;
                public function make(int $a): void;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    [Fact]
    public void SingleClassBody_IdenticalShapeDifferingOnlyByReturnType_StillReports8002()
    {
        // The overload-shape digest is parameter-only (TYHP4303's existing digest, reused here).
        // A same-name repeat that differs only by return type is still a real duplicate — it
        // must not silently merge as if it were a legitimate overload.
        var (_, diagnostics) = Bind("""
            <?tyhpdef
            class \Lib\Gizmo {
                public function make(int $a): void;
                public function make(int $a): string;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    [Fact]
    public void SingleClassBody_StaticInstanceMismatchSameName_StillReports8002()
    {
        // A static and an instance method cannot share a name in real PHP even when their
        // parameter shapes differ — this must not be treated as a legitimate overload merge.
        var (_, diagnostics) = Bind("""
            <?tyhpdef
            class \Lib\Sprocket {
                public function make(int $a): void;
                public static function make(string $a, int $b): void;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(string tyhpdef)
    {
        var result = IsolatedCompilation.ParseSnippet(
            tyhp: "<?tyhp\nfunction tyhpdef_base_overload_probe(): void {}\n",
            tyhpdefs: [SyntheticPhpStubs.DateTimeOperators, tyhpdef],
            skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && !obj.IsExtension
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
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
}
