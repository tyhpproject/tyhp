using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Story20.5")]
public class PhpVersionAttributeBinderTests
{
    [Fact]
    public void GatedFunction_Unsatisfied_OmitsSymbol()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            function only_on_84(): void {}
            function always_present(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument);
        FindFunction(global, "only_on_84").Should().BeNull();
        FindFunction(global, "always_present").Should().NotBeNull();
    }

    [Fact]
    public void GatedFunction_Satisfied_RegistersSymbol()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            function only_on_84(): void {}
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "only_on_84").Should().NotBeNull();
    }

    [Fact]
    public void NamedVersionArgument_Satisfied_RegistersSymbol()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(version: ">=8.4")]
            function named_gate(): void {}
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "named_gate").Should().NotBeNull();
    }

    [Fact]
    public void GatedClassAndMethod_AppearOnlyWhenVersionMatches()
    {
        var (global, _) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            class GatedClass {
                #[\Tyhp\Php(">=8.5")]
                public function only_85(): void {}
                public function always_on_class(): void {}
            }
            """, phpVersion: "8.4");

        var obj = FindObject(global, "GatedClass");
        obj.Should().NotBeNull();
        FindMethod(obj!, "always_on_class").Should().NotBeNull();
        FindMethod(obj!, "only_85").Should().BeNull();
    }

    [Fact]
    public void DisjointSameNameFunctions_BindMatchingVariant()
    {
        var source = """
            <?tyhp
            #[\Tyhp\Php(version: ">=8.2 <8.4")]
            function example(string $v): mixed { return $v; }
            #[\Tyhp\Php(version: ">=8.4")]
            function example(string $v, bool $strict = false): string { return $v; }
            """;

        var (global82, diagnostics82) = Bind(source, phpVersion: "8.2");
        diagnostics82.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        var fn82 = FindFunction(global82, "example");
        fn82.Should().NotBeNull();
        fn82!.Parameters.Should().HaveCount(1);

        var (global84, diagnostics84) = Bind(source, phpVersion: "8.4");
        diagnostics84.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        var fn84 = FindFunction(global84, "example");
        fn84.Should().NotBeNull();
        fn84!.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void DisjointSameNameMethods_BindMatchingVariant()
    {
        var source = """
            <?tyhp
            class Host {
                #[\Tyhp\Php(">=8.2 <8.4")]
                public function example(): int { return 1; }
                #[\Tyhp\Php(">=8.4")]
                public function example(): string { return "x"; }
            }
            """;

        var obj82 = FindObject(Bind(source, phpVersion: "8.2").Global, "Host");
        obj82.Should().NotBeNull();
        var method82 = FindMethod(obj82!, "example");
        method82.Should().NotBeNull();
        method82!.ReturnType.Should().NotBeNull();

        var obj84 = FindObject(Bind(source, phpVersion: "8.4").Global, "Host");
        obj84.Should().NotBeNull();
        FindMethod(obj84!, "example").Should().NotBeNull();
    }

    [Fact]
    public void OverlappingSameNameFunctions_Reports4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.3")]
            function foo(): void {}
            #[\Tyhp\Php(">=8.4")]
            function foo(): void {}
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        FindFunction(global, "foo").Should().NotBeNull();
    }

    [Fact]
    public void OverlappingSameName_UnsatisfiedTarget_StillReports4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.3")]
            function foo(): void {}
            #[\Tyhp\Php(">=8.4")]
            function foo(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        FindFunction(global, "foo").Should().BeNull();
    }

    [Fact]
    public void OverlappingStaticAndInstanceMethods_DistinctShapes_Reports4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            class Foo {
                #[\Tyhp\Php(">=8.0")]
                public function bar(int $a): void {}
                #[\Tyhp\Php(">=8.0")]
                public static function bar(string $a, int $b): void {}
            }
            """, phpVersion: "8.3");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        var foo = FindObject(global, "Foo");
        foo.Should().NotBeNull();
        FindMethod(foo!, "bar").Should().NotBeNull();
    }

    [Fact]
    public void OverlappingStaticAndInstanceMethods_OnlyOneGateActive_StillReports4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            class Foo {
                #[\Tyhp\Php(">=8.3")]
                public function bar(int $a): void {}
                #[\Tyhp\Php(">=8.4")]
                public static function bar(string $a, int $b): void {}
            }
            """, phpVersion: "8.3");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        var foo = FindObject(global, "Foo");
        foo.Should().NotBeNull();
        var bar = FindMethod(foo!, "bar");
        bar.Should().NotBeNull();
        bar!.IsStatic.Should().BeFalse();
    }

    [Fact]
    public void OverlappingStaticAndInstanceMethods_TyhpdefDistinctShapes_Reports4303()
    {
        var (_, diagnostics) = Bind("""
            <?tyhpdef
            class Foo {
                #[\Tyhp\Php(">=8.0")]
                public function bar(int $a): void;
                #[\Tyhp\Php(">=8.0")]
                public static function bar(string $a, int $b): void;
            }
            """, phpVersion: "8.3", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    [Fact]
    public void OverlappingStaticMethods_DistinctShapes_DoesNotReport4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            class Host {
                #[\Tyhp\Php(">=8.3")]
                public static function example(int $a): void;
                #[\Tyhp\Php(">=8.3")]
                public static function example(string $a, int $b): void;
            }
            """, phpVersion: "8.3", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var host = FindObject(global, "Host");
        host.Should().NotBeNull();
        var example = FindMethod(host!, "example");
        example.Should().NotBeNull();
        example!.IsStatic.Should().BeTrue();
        example.Overloads.Should().HaveCount(1);
    }

    [Fact]
    public void OverlappingInstanceMethods_DistinctShapes_DoesNotReport4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            class Host {
                #[\Tyhp\Php(">=8.3")]
                public function example(int $a): void;
                #[\Tyhp\Php(">=8.3")]
                public function example(string $a, int $b): void;
            }
            """, phpVersion: "8.3", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var host = FindObject(global, "Host");
        host.Should().NotBeNull();
        var example = FindMethod(host!, "example");
        example.Should().NotBeNull();
        example!.IsStatic.Should().BeFalse();
        example.Overloads.Should().HaveCount(1);
    }

    [Fact]
    public void DisjointStaticAndInstanceMethods_DoesNotReport4303()
    {
        var source = """
            <?tyhp
            class Host {
                #[\Tyhp\Php(">=8.2 <8.4")]
                public function example(int $a): void {}
                #[\Tyhp\Php(">=8.4")]
                public static function example(string $a, int $b): void {}
            }
            """;

        var (global82, diagnostics82) = Bind(source, phpVersion: "8.2");
        diagnostics82.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        var method82 = FindMethod(FindObject(global82, "Host")!, "example");
        method82.Should().NotBeNull();
        method82!.IsStatic.Should().BeFalse();

        var (global84, diagnostics84) = Bind(source, phpVersion: "8.4");
        diagnostics84.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        var method84 = FindMethod(FindObject(global84, "Host")!, "example");
        method84.Should().NotBeNull();
        method84!.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void DistinctSignaturesSameGate_DoesNotReport4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            #[\Tyhp\Php(">=8.3")]
            function foo(int $a): int;
            #[\Tyhp\Php(">=8.3")]
            function foo(string $a): string;
            """, phpVersion: "8.3", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var foo = FindFunction(global, "foo");
        foo.Should().NotBeNull();
        foo!.Overloads.Should().HaveCount(1);
        foo.Parameters.Should().HaveCount(1);
        foo.Overloads[0].Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void DeprecatedBoolArgOverload_SameDeclareGate_RegistersSecondSignature()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            declare(php=">=8.5") {
                function listed(): array;
                deprecated function listed(bool $exclude_disabled): array;
            }
            """, phpVersion: "8.5", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var fn = FindFunction(global, "listed");
        fn.Should().NotBeNull();
        fn!.IsDeprecated.Should().BeFalse();
        fn.Parameters.Should().BeEmpty();
        fn.Overloads.Should().HaveCount(1);
        fn.Overloads[0].IsDeprecated.Should().BeTrue();
        fn.Overloads[0].Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void GatedInterfaceEnumTrait_AppearOnlyWhenVersionMatches()
    {
        var source = """
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            interface GatedIface {}
            #[\Tyhp\Php(">=8.4")]
            enum GatedEnum { case A; }
            #[\Tyhp\Php(">=8.4")]
            trait GatedTrait {
                public function t(): void {}
            }
            interface AlwaysIface {}
            """;

        var (global82, diagnostics82) = Bind(source, phpVersion: "8.2");
        diagnostics82.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics82.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindObject(global82, "GatedIface").Should().BeNull();
        FindObject(global82, "GatedEnum").Should().BeNull();
        FindObject(global82, "GatedTrait").Should().BeNull();
        FindObject(global82, "AlwaysIface").Should().NotBeNull();

        var (global84, diagnostics84) = Bind(source, phpVersion: "8.4");
        diagnostics84.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics84.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindObject(global84, "GatedIface")!.ObjectKind.Should().Be(PhpTypeDeclType.Interface);
        FindObject(global84, "GatedEnum")!.ObjectKind.Should().Be(PhpTypeDeclType.Enum);
        FindObject(global84, "GatedTrait")!.ObjectKind.Should().Be(PhpTypeDeclType.Trait);
        FindObject(global84, "AlwaysIface").Should().NotBeNull();
    }

    [Fact]
    public void AttributeOnStruct_Reports4304_StillBinds()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            type Point = struct { int $x = 0; };
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget);
        var point = FindObject(global, "Point");
        point.Should().NotBeNull();
        point!.IsStruct.Should().BeTrue();
    }

    [Fact]
    public void AttributeOnTyhpExtension_Reports4304_StillBinds()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            extension StringOps extends string {
                function length(): int {
                    return \strlen($this);
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget);
        var ext = FindObject(global, "StringOps");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();
    }

    [Fact]
    public void AttributeOnTyhpdefExtension_Reports4304_StillBinds()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            extension UriStringExtensions extends string {
                fn parse(): string => \parse_url($this, \PHP_URL_PATH) ?? '';
            }
            """, phpVersion: "8.2", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget
            && (d.FileName ?? "").EndsWith("test.tyhpdef", StringComparison.Ordinal));
        var ext = FindObject(global, "UriStringExtensions");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();
    }

    [Fact]
    public void AndWithEnclosingDeclare_RequiresBothConstraints()
    {
        var source = """
            <?tyhp
            declare(php=">=8.3") {
                #[\Tyhp\Php(">=8.4")]
                function inner_need_84(): void {}
                function outer_ok(): void {}
            }
            """;

        var (global83, diagnostics83) = Bind(source, phpVersion: "8.3");
        diagnostics83.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics83.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global83, "outer_ok").Should().NotBeNull();
        FindFunction(global83, "inner_need_84").Should().BeNull();

        var (global84, _) = Bind(source, phpVersion: "8.4");
        FindFunction(global84, "outer_ok").Should().NotBeNull();
        FindFunction(global84, "inner_need_84").Should().NotBeNull();
    }

    [Fact]
    public void InactiveOuterDeclare_OmitsAttributeGatedInner_DoesNotDoubleBind()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.3") {
                #[\Tyhp\Php(">=8.4")]
                function inner_need_84(): void {}
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "inner_need_84").Should().BeNull();
    }

    [Fact]
    public void MissingVersionArgument_Reports4305_DoesNotOmit()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php]
            function still_bound(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument);
        FindFunction(global, "still_bound").Should().NotBeNull();
    }

    [Fact]
    public void NonStringVersionArgument_Reports4305_DoesNotOmit()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(84)]
            function still_bound(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument);
        FindFunction(global, "still_bound").Should().NotBeNull();
    }

    [Fact]
    public void UnqualifiedAttributeViaUseImport_Resolves()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            use Tyhp\Php;

            #[Php(">=8.4")]
            function only_on_84(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionAttributeInvalidArgument);
        FindFunction(global, "only_on_84").Should().BeNull();
    }

    [Fact]
    public void PatchLevelOverlappingConstraints_Reports4303()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            #[\Tyhp\Php(">=8.3.5 <8.3.10")]
            function foo(): void {}
            #[\Tyhp\Php(">=8.3.7 <8.3.12")]
            function foo(): void {}
            """, phpVersion: "8.3.8");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        FindFunction(global, "foo").Should().NotBeNull();
    }

    [Fact]
    public void PatchLevelDisjointConstraints_DoesNotReport4303()
    {
        var source = """
            <?tyhp
            #[\Tyhp\Php(">=8.3.0 <8.3.5")]
            function foo(): int { return 1; }
            #[\Tyhp\Php(">=8.3.5 <8.3.10")]
            function foo(): string { return "x"; }
            """;

        var (global1, diagnostics1) = Bind(source, phpVersion: "8.3.2");
        diagnostics1.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        FindFunction(global1, "foo").Should().NotBeNull();

        var (global2, diagnostics2) = Bind(source, phpVersion: "8.3.7");
        diagnostics2.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        FindFunction(global2, "foo").Should().NotBeNull();
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(
        string content,
        string? phpVersion,
        string fileName = "test.tyhp")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: phpVersion ?? "",
                skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull();
            return (result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

        FunctionDeclarationSymbol? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null)
            {
                return;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is FunctionDeclarationSymbol func
                    && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = func;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        return found;
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null)
            {
                return;
            }

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
        return found;
    }

    private static ObjectMethodSymbol? FindMethod(ObjectDeclarationSymbol obj, string name)
    {
        if (obj.Members.TryGetValue(name, out var member) && member is ObjectMethodSymbol method)
        {
            return method;
        }

        if (obj.ContainingScope is not IBaseScope scope)
        {
            return null;
        }

        foreach (var childScope in scope.GetAllChildScopes())
        {
            if (childScope.DeclarationSymbol is ObjectDeclarationSymbol same
                && ReferenceEquals(same, obj))
            {
                foreach (var symbol in childScope.GetAllChildSymbols())
                {
                    if (symbol is ObjectMethodSymbol found
                        && string.Equals(found.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }
}
