using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.Tests.Checker;

/// <summary>
/// The wrapper plan of a first-class callable: the parameters that accept a type converting to
/// the declared one through <c>operator convert</c> (<see cref="CallableWrapperFacts"/>).
/// </summary>
[Trait("Category", "Checker")]
public class ConvertibleSourceTests
{
    private const string Library = """
        <?tyhp
        namespace Probe;

        class Wallet {
            public int $amount = 0;
            operator convert(int $value): self {
                Wallet $w = new Wallet();
                $w->amount = $value;
                return $w;
            }
            operator convert(string $value): self {
                return new Wallet();
            }
        }

        class Money {
            public int $cents = 0;
            operator convert(self $value): int => $value->cents;
            operator convert(self $value): Wallet {
                return new Wallet();
            }
        }

        function takeWallet(Wallet $w): int { return $w->amount; }
        function takeMaybeWallet(?Wallet $w): int { return 0; }
        function takeInt(int $n): int { return $n; }
        function takeString(string $s): string { return $s; }
        function takeIntOrWallet(int|Wallet $v): int { return 0; }
        function takeRef(int &$n): void {}
        function takeMany(int ...$n): int { return 0; }
        function takeBoth(Wallet $w, int $n, string $s): int { return 0; }

        class Host {
            public function eat(Wallet $w): int { return $w->amount; }
            public static function make(Wallet $w): int { return $w->amount; }
        }
        """;

    /// <summary>Compiles the library plus <paramref name="body"/> and returns the plan of every `(...)` call.</summary>
    private static List<CallableWrapperPlan?> PlansOf(string body, IReadOnlyDictionary<string, string>? extraFiles = null)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = Library,
            ["b.tyhp"] = "<?tyhp\nnamespace Probe;\n" + body,
        };
        if (extraFiles is not null)
        {
            foreach (var pair in extraFiles)
            {
                files[pair.Key] = pair.Value;
            }
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                IsolatedCompilation.CreateOptions(tempDir, "8.2"));

            result.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", result.Diagnostics.Errors.Select(d => $"{(int)d.Code}: {d.Message}")));

            var plans = new List<CallableWrapperPlan?>();
            foreach (var file in result.ParsedFiles!)
            {
                Collect(file, plans);
            }

            return plans;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void Collect(IBase2Ast? node, List<CallableWrapperPlan?> plans)
    {
        if (node is null)
        {
            return;
        }

        if (node is PhpCallAst call && CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
        {
            plans.Add(CallableWrapperFacts.Get(call));
        }

        foreach (var child in node.AstChildren)
        {
            Collect(child, plans);
        }
    }

    private static CallableWrapperPlan Single(string body, IReadOnlyDictionary<string, string>? extra = null)
    {
        var plans = PlansOf(body, extra);
        plans.Should().ContainSingle();
        plans[0].Should().NotBeNull("the callable has a parameter that accepts a convertible source");
        return plans[0]!;
    }

    private static string Kinds(ConvertibleParameterPlan parameter) =>
        string.Join(",", parameter.Sources.Select(s => $"{s.Kind}:{s.SourceType.DisplayName}"));

    [Fact]
    public void ConvertFrom_ClassBody_ListsEveryConvertFromOperand()
    {
        var plan = Single("function f(): void { $c = takeWallet(...); }");

        plan.Parameters.Should().ContainSingle();
        var parameter = plan.Parameters[0];
        parameter.Index.Should().Be(0);
        Kinds(parameter).Should().Contain("ConvertFrom:int").And.Contain("ConvertFrom:string");
        parameter.WidenedType.DisplayName.Should().Contain("Wallet").And.Contain("int").And.Contain("string");
    }

    [Fact]
    public void NullableTarget_KeepsNullableWidenedType()
    {
        var plan = Single("function f(): void { $c = takeMaybeWallet(...); }");

        plan.Parameters[0].WidenedType.Should().BeOfType<NullableCheckedType>();
        Kinds(plan.Parameters[0]).Should().Contain("ConvertFrom:int");
    }

    [Fact]
    public void ConvertTo_ScalarTarget_ListsTheClassesThatConvertToIt()
    {
        var plan = Single("function f(): void { $c = takeInt(...); }");

        Kinds(plan.Parameters[0]).Should().Be("ConvertTo:\\Probe\\Money");
        plan.Parameters[0].WidenedType.DisplayName.Should().Contain("int").And.Contain("Money");
    }

    [Fact]
    public void ConvertTo_ClassTarget_ListsTheClassesThatConvertToIt()
    {
        var plan = Single("function f(): void { $c = takeWallet(...); }");

        Kinds(plan.Parameters[0]).Should().Contain("ConvertTo:\\Probe\\Money");
    }

    [Fact]
    public void NoConvertibleSource_HasNoPlan()
    {
        var plans = PlansOf("function f(): void { $c = takeString(...); $d = \\strlen(...); }");

        plans.Should().HaveCount(2);
        plans.Should().OnlyContain(p => p == null);
    }

    [Fact]
    public void UnionTarget_IsNotAConvertTarget()
    {
        PlansOf("function f(): void { $c = takeIntOrWallet(...); }").Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public void ByReferenceAndVariadicParameters_AreNotWidened()
    {
        PlansOf("function f(): void { $a = takeRef(...); $b = takeMany(...); }")
            .Should().HaveCount(2).And.OnlyContain(p => p == null);
    }

    [Fact]
    public void OnlyTheConvertibleParametersArePlanned()
    {
        var plan = Single("function f(): void { $c = takeBoth(...); }");

        plan.Parameters.Select(p => p.Index).Should().Equal(0, 1);
        plan.ForIndex(2).Should().BeNull();
    }

    [Fact]
    public void InstanceAndStaticMethods_ArePlanned()
    {
        var plans = PlansOf(
            "function f(Host $h): void { $a = $h->eat(...); $b = Host::make(...); }");

        plans.Should().HaveCount(2);
        plans.Should().OnlyContain(p => p != null);
        Kinds(plans[0]!.Parameters[0]).Should().Contain("ConvertFrom:int");
        Kinds(plans[1]!.Parameters[0]).Should().Contain("ConvertFrom:int");
    }

    private const string ExtensionLibrary = """
        <?tyhp
        namespace Ext;

        class Coin { public int $value = 0; }

        extension CoinConv extends Coin {
            operator convert(int $value): self {
                Coin $c = new Coin();
                $c->value = $value;
                return $c;
            }
        }

        function takeCoin(Coin $c): int { return $c->value; }

        function sameFile(): void { $f = takeCoin(...); }
        """;

    [Fact]
    public void ExtensionConvert_IsPlannedInTheDeclaringFile()
    {
        var plans = PlansOf(
            "function g(): void {}",
            new Dictionary<string, string> { ["ext.tyhp"] = ExtensionLibrary });

        plans.Should().ContainSingle().Which.Should().NotBeNull();
        Kinds(plans[0]!.Parameters[0]).Should().Be("ConvertFrom:int");
    }

    [Fact]
    public void ExtensionConvert_InAnotherFileNeedsUseExtension()
    {
        var without = PlansOf(
            "function g(): void { $f = \\Ext\\takeCoin(...); }",
            new Dictionary<string, string> { ["ext.tyhp"] = ExtensionLibrary });
        // The declaring file's own callable is planned; the other file's is not.
        without.Should().HaveCount(2);
        without.Count(p => p != null).Should().Be(1);

        var with = PlansOf(
            "use extension \\Ext\\CoinConv;\nfunction g(): void { $f = \\Ext\\takeCoin(...); }",
            new Dictionary<string, string> { ["ext.tyhp"] = ExtensionLibrary });
        with.Should().HaveCount(2);
        with.Should().OnlyContain(p => p != null);
    }
}
