using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 5 — <c>bind</c> / <c>bindTo</c> / <c>call</c> overload pick, leftover
/// scope, non-rebindable producers, and <c>call(null)</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ClosureBindTests
{
    [Fact]
    public void BindTo_OmittedAndStatic_KeepsTScopeAndDoesNotStoreStatic()
    {
        var (checker, file, errors) = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    $kept = $fn->bindTo($this);
                    $keptStatic = $fn->bindTo($this, 'static');
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerMissingArgument
                || d.Code == MessageCode.CheckerTooManyArguments,
            "bindTo($x) must pick the defaulted 'static' overload: " + Describe(errors));
        errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerClosureBindIncompatible
            || d.Code == MessageCode.CheckerClosureNonRebindable
            || d.Code == MessageCode.CheckerClosureStaticScopeStored,
            Describe(errors));

        var omitted = InferredBindToReturn(checker, file, 0);
        var withStatic = InferredBindToReturn(checker, file, 1);
        ScopeSlot(omitted).Should().Contain("Host", omitted.DisplayName);
        ScopeSlot(omitted).Should().NotContain("static", omitted.DisplayName);
        ScopeSlot(withStatic).Should().Contain("Host", withStatic.DisplayName);
        ScopeSlot(withStatic).Should().NotContain("static", withStatic.DisplayName);
        ThisSlot(omitted).Should().Contain("Host", omitted.DisplayName);
        ThisSlot(withStatic).Should().Contain("Host", withStatic.DisplayName);
    }

    [Fact]
    public void BindTo_ParentClassAndParentInstance_AllowedWhenTThisExtendsParent()
    {
        var errors = Compile("""
            <?tyhp
            class ParentHost {}
            class Host extends ParentHost {
                public function demo(): void {
                    $fn = function (): void {};
                    $fn->bindTo($this, ParentHost::class);
                    $fn->bindTo($this, new ParentHost());
                }
            }
            """).Errors;

        errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerClosureBindIncompatible,
            "Parent::class / parent instance must be a legal scope when TThis <: Parent: "
            + Describe(errors));
    }

    [Fact]
    public void BindTo_UnrelatedClass_ErrorsWhenTThisIsKnown()
    {
        var errors = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    $fn->bindTo(new Other());
                }
            }
            class Other {}
            """).Errors;

        errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureBindIncompatible,
            "unrelated $newThis must error when TThis is a known class: " + Describe(errors));
    }

    [Fact]
    public void BindTo_UnrelatedClass_AllowedWhenGradual()
    {
        var errors = Compile("""
            <?tyhp
            class Other {}
            function demo(\Closure $c, Other $o): void {
                $c->bindTo($o);
            }
            """).Errors;

        errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerClosureBindIncompatible
            || d.Code == MessageCode.CheckerClosureNonRebindable,
            "gradual Closure must allow bindTo: " + Describe(errors));
    }

    [Fact]
    public void BindTo_StdClassNewThis_Allowed()
    {
        // PHP only bans an internal class as `newScope` ("Cannot bind closure to scope of
        // internal class …", PHP 7.0+) — `newThis` may legally be an instance of an internal
        // class such as `stdClass`.
        var errors = Compile("""
            <?tyhp
            function demo(): void {
                $fn = function (): void {};
                $fn->bindTo(new \stdClass());
            }
            """).Errors;

        errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerClosureNonRebindable
            || d.Code == MessageCode.CheckerClosureBindIncompatible,
            "binding $this to an internal-class instance is legal PHP: " + Describe(errors));
    }

    [Fact]
    public void BindTo_This_NoSpuriousGenericConstraintDiagnostics()
    {
        // `bindTo`'s return type instantiates `\Closure<TCallableShape, TNewThis, TNewScope>`;
        // `TNewScope`'s own constraint (`__ClosureScope<TNewThis>`) must resolve `TNewThis`
        // from the method's generic scope, not silently collapse to a 3003/4035 pair
        // attributed to the Ext.Core.tyhpdef overlay instead of the call site.
        var errors = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    $kept = $fn->bindTo($this);
                }
            }
            """).Errors;

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void BindTo_Arrow_ErrorsNonRebindable()
    {
        var errors = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = fn (): int => 0;
                    $fn->bindTo($this);
                }
            }
            """).Errors;

        errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureNonRebindable,
            "arrow bindTo must error: " + Describe(errors));
    }

    [Fact]
    public void Call_Null_Errors()
    {
        var errors = Compile("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    $fn->call(null);
                }
            }
            """).Errors;

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerClosureBindIncompatible
            || d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            "call(null) must error (TNewThis extends object): " + Describe(errors));
    }

    [Fact]
    public void BindTo_StaticSentinelAnnotation_ErrorsWhenStoredAsTScope()
    {
        var errors = Compile("""
            <?tyhp
            function demo(\Closure<callable(): void, object, 'static'> $c): void {}
            """).Errors;

        errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureStaticScopeStored,
            "'static' stored as TScope must error: " + Describe(errors));
    }

    [Fact]
    public void BindTo_Fcc_ErrorsNonRebindable()
    {
        var errors = Compile("""
            <?tyhp
            class Host {
                public function m(): void {}
                public function demo(): void {
                    $c = $this->m(...);
                    $c->bindTo($this);
                }
            }
            """).Errors;

        errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureNonRebindable,
            "FCC bindTo must error: " + Describe(errors));
    }

    [Fact]
    public void BindTo_ChildClassName_ErrorsWhenTThisIsParent()
    {
        var errors = Compile("""
            <?tyhp
            class ParentHost {}
            class Host extends ParentHost {
                public function demo(ParentHost $p): void {
                    $fn = function (): void {};
                    $rebound = $fn->bindTo($this, ParentHost::class);
                    $rebound->bindTo($p, Host::class);
                }
            }
            """).Errors;

        errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureBindIncompatible,
            "descendant class-name is __CompatibleTypeName, not __SuperTypeName: "
            + Describe(errors));
    }

    [Fact]
    public void Bind_ConcreteClosure_AcceptsSameCallAsBindTo()
    {
        // The `$closure` parameter is `\Closure<TCallableShape, TOldThis, TOldScope>`. While the
        // callee generics are unbound, its invariant this/scope slots must still accept a
        // closure with a known this/scope (TYHP4010 otherwise).
        var errors = CompileOverlay("""
            <?tyhp
            function demo(object $object, \Closure<callable(object): void, ?object, null> $call): void {
                \Closure::bind($call, null, $object);
                $call->bindTo(null, $object);
            }
            """);

        errors.Should().BeEmpty("Closure::bind must accept the closure bindTo accepts: " + Describe(errors));
    }

    [Fact]
    public void Bind_StaticClosure_AcceptsObjectScope()
    {
        var errors = CompileOverlay("""
            <?tyhp
            class Host {
                public function demo(object $object): void {
                    $call = static function (object $target): void {};
                    \Closure::bind($call, null, $object);
                    $call->bindTo(null, $object);
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void Bind_OmittedScopeAndObjectScope_AcceptInstanceClosure()
    {
        var errors = CompileOverlay("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    \Closure::bind($fn, $this);
                    \Closure::bind($fn, $this, $this);
                    $fn->bindTo($this);
                    $fn->bindTo($this, $this);
                }
            }
            """);

        errors.Should().BeEmpty("Closure::bind must accept an instance closure: " + Describe(errors));
    }

    [Fact]
    public void Bind_StaticSentinelLiteral_ReportsLikeBindTo()
    {
        // The literal 'static' is widened to `string` before the `TNewScope extends 'static'`
        // check, for bind and bindTo alike. Both calls must report the same diagnostics.
        var errors = CompileOverlay("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    \Closure::bind($fn, $this, 'static');
                    $fn->bindTo($this, 'static');
                }
            }
            """);

        var bind = errors.Where(d => d.Line == 5).Select(d => d.Code).Order().ToList();
        var bindTo = errors.Where(d => d.Line == 6).Select(d => d.Code).Order().ToList();
        bind.Should().Equal(bindTo, Describe(errors));
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(errors));
    }

    [Fact]
    public void Bind_UnrelatedClass_ErrorsWhenTThisIsKnown()
    {
        var errors = CompileOverlay("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = function (): void {};
                    \Closure::bind($fn, new Other());
                }
            }
            class Other {}
            """);

        errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerClosureBindIncompatible,
            "unrelated $newThis must error for static bind: " + Describe(errors));
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(errors));
    }

    [Fact]
    public void Bind_TypedClosure_ChecksThisAgainstDeclaredThis()
    {
        // A closure whose `$this` is `Host` is not widened to accept an unrelated `$newThis`,
        // while a subclass instance is allowed. bindTo reports the same.
        var errors = CompileOverlay("""
            <?tyhp
            class Host {}
            class Sub extends Host {}
            class Other {}
            function demo(\Closure<callable(): void, Host, Host> $c, Sub $sub, Other $other): void {
                \Closure::bind($c, $sub);
                $c->bindTo($sub);
                \Closure::bind($c, $other);
                $c->bindTo($other);
            }
            """);

        errors.Should().OnlyContain(d => d.Code == MessageCode.CheckerClosureBindIncompatible,
            Describe(errors));
        errors.Select(d => d.Line).Order().Should().Equal([8, 9], Describe(errors));
    }

    [Fact]
    public void Bind_StaticClosure_RejectsInstanceThis_LikeBindTo()
    {
        // A closure without `$this` has scope `null`; an omitted scope keeps it, so a non-null
        // `$newThis` has no scope to run in.
        var errors = CompileOverlay("""
            <?tyhp
            class Host {
                public function demo(object $object): void {
                    $call = static function (): void {};
                    \Closure::bind($call, $object);
                    $call->bindTo($object);
                }
            }
            """);

        errors.Should().OnlyContain(d => d.Code == MessageCode.CheckerClosureBindIncompatible,
            Describe(errors));
        errors.Select(d => d.Line).Order().Should().Equal([5, 6], Describe(errors));
    }

    [Fact]
    public void Bind_NonClosureArgument_StillReportsIncompatibleArgument()
    {
        // The wide this/scope placeholders apply to the this/scope slots only; a value that is
        // not a Closure is still rejected as the first argument.
        var errors = CompileOverlay("""
            <?tyhp
            function demo(object $object): void {
                \Closure::bind($object, $object);
                \Closure::bind(1, null);
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType && d.Line == 3,
            Describe(errors));
        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType && d.Line == 4,
            Describe(errors));
    }

    [Fact]
    public void Bind_Arrow_ErrorsNonRebindable()
    {
        var errors = CompileOverlay("""
            <?tyhp
            class Host {
                public function demo(): void {
                    $fn = fn (): int => 0;
                    \Closure::bind($fn, $this);
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureNonRebindable,
            "arrow Closure::bind must error: " + Describe(errors));
    }

    private static ICheckedType UnwrapClosure(ICheckedType type)
    {
        while (type is NullableCheckedType nullable)
        {
            type = nullable.InnerType;
        }

        return type;
    }

    private static string ThisSlot(ICheckedType type) =>
        UnwrapClosure(type) is GenericCheckedType { TypeArguments.Count: > 1 } g
            ? g.TypeArguments[1].DisplayName
            : "";

    private static string ScopeSlot(ICheckedType type) =>
        UnwrapClosure(type) is GenericCheckedType { TypeArguments.Count: > 2 } g
            ? g.TypeArguments[2].DisplayName
            : "";

    private static ICheckedType InferredBindToReturn(TyhpChecker checker, SrcFileAst file, int index)
    {
        var matches = new List<ICheckedType>();
        foreach (var deref in FindAllAst<PhpDereferenceableAst>(file))
        {
            if (deref.Suffix is not PhpCallAst call
                || CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments)
                || deref.Base is not PhpDereferenceableAst inner
                || inner.Suffix is not PhpInstanceMemberAccessAst member)
            {
                continue;
            }

            if (!string.Equals(ExprText(member.MemberName), "bindTo", StringComparison.Ordinal))
            {
                continue;
            }

            if (checker.ExpressionTypes.TryGetValue(deref, out var type) && type is not null)
            {
                matches.Add(type);
            }
        }

        matches.Should().HaveCountGreaterThan(index, "expected bindTo call return types");
        return matches[index];
    }

    private static IReadOnlyList<IDiagnostic> CompileOverlay(string content)
    {
        using var builder = IsolatedCompilation.CreateOverlayProject(
            phpVersion: "8.5",
            includeTyhpdef: SyntheticPhpStubs.ClosureFiberHarvest,
            overlayTyhpdef: SyntheticPhpStubs.ClosureFiberOverlay,
            userTyhp: content);
        var result = IsolatedCompilation.BindProject(builder, skipChecking: true);
        result.GlobalScope.Should().NotBeNull(
            "bind should succeed: " + Describe(result.Diagnostics.Errors));
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return result.Diagnostics.Errors;
    }

    private static string? ExprText(IExpression? expression) => expression switch
    {
        PhpNameAst name => name.ValueString,
        TokenValueAst token => token.ValueString,
        _ => null,
    };

    private static IEnumerable<T> FindAllAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var found in FindAllAst<T>(child))
            {
                yield return found;
            }
        }
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static (TyhpChecker Checker, SrcFileAst File, IReadOnlyList<IDiagnostic> Errors) Compile(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull(
                "bind should succeed: " + Describe(result.Diagnostics.Errors));
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (checker, result.ParsedFiles![0], result.Diagnostics.Errors);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
