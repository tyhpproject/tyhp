using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Call-site substitution of a generic block target's type arguments from the receiver.
/// </summary>
[Trait("Category", "Checker")]
public class ExtensionBlockTargetGenericSubstitutionTests
{
    [Fact]
    public void TypeParameterReturn_ChainAndAssign_IsReceiverArgument()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension MyClassOps<T> extends MyClass<T> {
                function id(): T {
                    throw new \Exception("unused");
                }
            }
            extension StrOps extends string {
                function shout(): string {
                    return $this;
                }
            }
            function chained(MyClass<string> $x): string {
                return $x->id()->shout();
            }
            function stored(MyClass<string> $x): string {
                $copy = $x->id();
                return $copy;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypes(result, "id").Should().Equal("string", "string");
    }

    [Fact]
    public void SelfReturn_ChainAndAssign_IsSubstitutedTarget()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Ops<T> extends MyClass<T> {
                function label(): self {
                    return $this;
                }
            }
            function chained(MyClass<string> $x): string {
                return $x->label()->value;
            }
            function stored(MyClass<string> $x): MyClass<string> {
                $copy = $x->label();
                return $copy;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypes(result, "label").Should().Equal("\\App\\MyClass<string>", "\\App\\MyClass<string>");
    }

    [Fact]
    public void ExplicitTargetReturn_Assign_IsSubstitutedTarget()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Ops<T> extends MyClass<T> {
                function labelExplicit(): MyClass<T> {
                    return $this;
                }
            }
            function demo(MyClass<string> $x): MyClass<string> {
                return $x->labelExplicit();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "labelExplicit").Should().Be("\\App\\MyClass<string>");
    }

    [Fact]
    public void GroupTypeParameterReturn_IsReceiverArgument()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Helpers {
                extends<T> MyClass<T> {
                    function id(): T {
                        return $this->value;
                    }
                }
            }
            function stored(MyClass<string> $x): string {
                $copy = $x->id();
                return $copy;
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "id").Should().Be("string");
    }

    [Fact]
    public void UnconstrainedAndWrongReceiver_DoesNotInventSubstitution()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            class Pair<TLeft, TRight> {
                public function __construct(public TLeft $left, public TRight $right) {}
            }
            extension MyClassOps<T> extends MyClass<T> {
                function id(): T {
                    throw new \Exception("unused");
                }
            }
            extension PairOps {
                extends<TRight> Pair<string, TRight> {
                    function right(): TRight {
                        return $this->right;
                    }
                }
            }
            class Holder<T> {
                public function __construct(public T $value) {}
            }
            function raw(MyClass $x): void {
                $y = $x->id();
            }
            function mismatched(Pair<int, int> $p): void {
                $y = $p->right();
            }
            function wrong(Holder<string> $h): void {
                $y = $h->id();
            }
            """);

        CallType(result, "id", occurrence: 0).Should().Be("T");
        CallType(result, "right").Should().Be("TRight");
        CallType(result, "id", occurrence: 1).Should().Be("unresolved");
    }

    [Fact]
    public void ScratchProbe_TwoGenericParamsExtensionBindsAtAll()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Pair<TLeft, TRight> {
                public function __construct(public TLeft $left, public TRight $right) {}
            }
            extension PairOps<TLeft, TRight> extends Pair<TLeft, TRight> {
                function describe(): string {
                    return "pair";
                }
            }
            function demo(Pair<string, int> $p): string {
                return $p->describe();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
    }

    [Fact]
    public void ScratchProbe_SwappedGenericOrderReturn_Simple()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Pair<TLeft, TRight> {
                public function __construct(public TLeft $left, public TRight $right) {}
            }
            extension PairOps<TLeft, TRight> extends Pair<TLeft, TRight> {
                function right(): TRight {
                    return $this->right;
                }
            }
            function demo(Pair<string, int> $p): int {
                return $p->right();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "right").Should().Be("int");
    }

    [Fact]
    public void ScratchProbe_MethodLevelGenericIsNotWipedByBlockSubstitution()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Ops<T> extends MyClass<T> {
                function mapTo<R>(callable(T): R $fn): R {
                    return $fn($this->value);
                }
            }
            function demo(MyClass<string> $x): int {
                return $x->mapTo(fn(string $s): int => \strlen($s));
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "mapTo").Should().Be("int");
    }

    [Fact]
    public void ScratchProbe_TwoTypeParametersBothBind()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class Pair<TLeft, TRight> {
                public function __construct(public TLeft $left, public TRight $right) {}
            }
            extension PairOps<TLeft, TRight> extends Pair<TLeft, TRight> {
                function swap(): Pair<TRight, TLeft> {
                    return new Pair($this->right, $this->left);
                }
            }
            function demo(Pair<string, int> $p): Pair<int, string> {
                return $p->swap();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallType(result, "swap").Should().Be("\\App\\Pair<int, string>");
    }

    [Fact]
    public void ScratchProbe_NamespacedSameMethodName_DoesNotCrossBind()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace One;

            class Box<T> {
                public function __construct(public T $value) {}
            }
            extension BoxOps<T> extends Box<T> {
                function id(): T {
                    return $this->value;
                }
            }

            namespace Two;

            class Crate<T> {
                public function __construct(public T $value) {}
            }
            extension CrateOps<T> extends Crate<T> {
                function id(): T {
                    return $this->value;
                }
            }

            namespace Demo;

            function demo(\One\Box<string> $b, \Two\Crate<int> $c): array {
                return [$b->id(), $c->id()];
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
        CallTypes(result, "id").Should().Equal("string", "int");
    }

    [Fact]
    public void HeaderGeneric_ThisPropertyAccess_DoesNotReportUnresolvedReceiver()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Ops<T> extends MyClass<T> {
                function get(): T {
                    return $this->value;
                }
            }
            function demo(MyClass<string> $x): string {
                return $x->get();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
    }

    [Fact]
    public void NestedGroupGeneric_ThisPropertyAccess_DoesNotReportUnresolvedReceiver()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Helpers {
                extends<T> MyClass<T> {
                    function get(): T {
                        return $this->value;
                    }
                }
            }
            function demo(MyClass<string> $x): string {
                return $x->get();
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().BeEmpty(Dump(errors) + " || " + DumpCallTypes(result));
    }

    /// <summary>
    /// <c>InlineSpliceRule.IsExtensionMember</c> used to resolve a nested <c>extends&lt;T&gt;</c>
    /// group member's owner via <c>CallSiteSpliceEngine.OwnerOf</c>, which stops at the nearest
    /// enclosing <see cref="Tyhp.TyhpLang.Binder.Symbols.ObjectDeclarationSymbol"/> — the
    /// compiler-generated group itself (<c>IsExtensionTargetGroup</c>), not the real extension
    /// header one level up. That made every nested-group member invisible to
    /// <c>InlineSpliceRule</c> entirely, so <c>#[\Tyhp\Optimize\Inline]</c> on a group member
    /// silently passed instead of reporting TYHP4176 the way a header member already does.
    /// </summary>
    [Fact]
    public void NestedGroupExtensionMember_InlineAttribute_Reports4176()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            class MyClass<T> {
                public function __construct(public T $value) {}
            }
            extension Helpers {
                extends<T> MyClass<T> {
                    #[\Tyhp\Optimize\Inline]
                    function get(): T {
                        return $this->value;
                    }
                }
            }
            """);

        var errors = SnippetErrorsOf(result);
        errors.Should().Contain(
            error => error.Code == MessageCode.CheckerInlineAttributeOnExtensionMember,
            Dump(errors));
    }

    private static List<IDiagnostic> SnippetErrorsOf(CompilationResult result)
    {
        return result.Diagnostics.Errors
            .Where(error => error.FileName.EndsWith("snippet.tyhp", StringComparison.Ordinal))
            .ToList();
    }

    private static string DumpCallTypes(CompilationResult result)
    {
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return "(no expression types)";
        }

        var calls = new List<string>();
        foreach (var file in result.ParsedFiles)
        {
            foreach (var deref in FindDerefs(file))
            {
                if (deref.Suffix is not PhpCallAst)
                {
                    continue;
                }

                calls.Add($"{CallMethodName(deref) ?? "?"}={TypeName(result, deref)}");
            }
        }

        return calls.Count == 0 ? "(no call types)" : string.Join("; ", calls);
    }

    private static IReadOnlyList<string> CallTypes(CompilationResult result, string methodName)
    {
        var types = new List<string>();
        if (result.ExpressionTypes is null || result.ParsedFiles is null)
        {
            return types;
        }

        foreach (var file in result.ParsedFiles)
        {
            foreach (var deref in FindDerefs(file))
            {
                if (deref.Suffix is not PhpCallAst)
                {
                    continue;
                }

                if (!string.Equals(CallMethodName(deref), methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                types.Add(TypeName(result, deref));
            }
        }

        return types;
    }

    private static string CallType(CompilationResult result, string methodName, int occurrence = 0)
    {
        var types = CallTypes(result, methodName);
        return occurrence < types.Count ? types[occurrence] : "<missing>";
    }

    private static string TypeName(CompilationResult result, PhpDereferenceableAst deref) =>
        result.ExpressionTypes!.TryGetValue(deref, out var cached)
            ? cached.DisplayName
            : "<uncached>";

    private static string? CallMethodName(PhpDereferenceableAst callDeref)
    {
        if (callDeref.Base is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst member })
        {
            return null;
        }

        return member.MemberName switch
        {
            PhpNameAst name => name.ValueString ?? name.Identifier,
            TokenValueAst token => token.ValueString,
            IExpression expression => expression.Identifier,
            _ => member.MemberName?.Identifier,
        };
    }

    private static IEnumerable<PhpDereferenceableAst> FindDerefs(IBase2Ast root)
    {
        if (root is PhpDereferenceableAst deref)
        {
            yield return deref;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var nested in FindDerefs(child))
            {
                yield return nested;
            }
        }
    }

    private static string Dump(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(error => $"{(int)error.Code} {error.Message}"));
}
