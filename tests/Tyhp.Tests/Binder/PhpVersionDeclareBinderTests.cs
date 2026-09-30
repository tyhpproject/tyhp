using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Story20.5")]
public class PhpVersionDeclareBinderTests
{
    [Fact]
    public void FileLevel_Unsatisfied_OmitsFileSymbols()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4");
            function gated_file_fn(): void {}
            class GatedFileClass {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDeclareNotAlone);
        FindFunction(global, "gated_file_fn").Should().BeNull();
        FindObject(global, "GatedFileClass").Should().BeNull();
        RequireUserFile(global).IsPhpVersionGateInactive.Should().BeTrue();
    }

    [Fact]
    public void FileLevel_Satisfied_RegistersFileSymbols()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4");
            function gated_file_fn(): void {}
            class GatedFileClass {}
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "gated_file_fn").Should().NotBeNull();
        FindObject(global, "GatedFileClass").Should().NotBeNull();
        RequireUserFile(global).IsPhpVersionGateInactive.Should().BeFalse();
    }

    [Fact]
    public void Block_Unsatisfied_OmitsInnerSymbols_KeepsSiblings()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            function always_present(): void {}
            declare(php=">=8.4") {
                function only_on_84(): void {}
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "always_present").Should().NotBeNull();
        FindFunction(global, "only_on_84").Should().BeNull();
        FindDeclareBlocks(global).Should().ContainSingle(d => d.IsPhpVersionGateInactive);
    }

    [Fact]
    public void Block_Satisfied_RegistersInnerSymbols()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            function always_present(): void {}
            declare(php=">=8.4") {
                function only_on_84(): void {}
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "always_present").Should().NotBeNull();
        FindFunction(global, "only_on_84").Should().NotBeNull();
        FindDeclareBlocks(global).Should().ContainSingle(d => !d.IsPhpVersionGateInactive);
    }

    [Fact]
    public void NestedBlocks_And_OuterOnly_WhenInnerUnsatisfied()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.3") {
                function outer_ok(): void {}
                declare(php=">=8.4") {
                    function inner_need_84(): void {}
                }
            }
            """, phpVersion: "8.3");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "outer_ok").Should().NotBeNull();
        FindFunction(global, "inner_need_84").Should().BeNull();
    }

    [Fact]
    public void NestedBlocks_And_BothPresent_WhenTargetSatisfiesAll()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.3") {
                function outer_ok(): void {}
                declare(php=">=8.4") {
                    function inner_need_84(): void {}
                }
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "outer_ok").Should().NotBeNull();
        FindFunction(global, "inner_need_84").Should().NotBeNull();
    }

    [Fact]
    public void NestedBlocks_And_BothAbsent_WhenOuterUnsatisfied()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.3") {
                function outer_ok(): void {}
                declare(php=">=8.4") {
                    function inner_need_84(): void {}
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "outer_ok").Should().BeNull();
        FindFunction(global, "inner_need_84").Should().BeNull();
    }

    [Fact]
    public void FileLevelAndBlock_AndTogether()
    {
        var (global, _) = Bind("""
            <?tyhp
            declare(php=">=8.3");
            function file_ok(): void {}
            declare(php=">=8.4") {
                function block_need_84(): void {}
            }
            """, phpVersion: "8.3");

        FindFunction(global, "file_ok").Should().NotBeNull();
        FindFunction(global, "block_need_84").Should().BeNull();
    }

    [Fact]
    public void SequentialStrictTypesThenPhp_GatesFileWithoutMixing()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(strict_types=1);
            declare(php=">=8.4");
            function gated(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDeclareNotAlone);
        FindFunction(global, "gated").Should().BeNull();
        RequireUserFile(global).HasFileDeclareDirective("strict_types").Should().BeTrue();
    }

    [Fact]
    public void MixedPhpWithOtherDirective_Reports4301_DoesNotApplyGate()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4", strict_types=1);
            function still_bound(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpVersionDeclareNotAlone);
        FindFunction(global, "still_bound").Should().NotBeNull();
        RequireUserFile(global).IsPhpVersionGateInactive.Should().BeFalse();
    }

    [Fact]
    public void InvalidConstraint_DoesNotThrow_OmitsSymbols_DoesNotReport4300()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php="not-a-constraint!!");
            function should_be_absent(): void {}
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
        FindFunction(global, "should_be_absent").Should().BeNull();
        RequireUserFile(global).IsPhpVersionGateInactive.Should().BeTrue();
        RequireUserFile(global).IsPhpVersionConstraintValid.Should().BeFalse();
    }

    [Fact]
    public void AlternateEnddeclare_Unsatisfied_OmitsInnerSymbols()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            function always_present(): void {}
            declare(php=">=8.4"):
                function only_on_84(): void {}
            enddeclare;
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "always_present").Should().NotBeNull();
        FindFunction(global, "only_on_84").Should().BeNull();
    }

    [Fact]
    public void NamespaceScopedFileLevelDeclare_Unsatisfied_OmitsNamespaceSymbols()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            namespace App {
                declare(php=">=8.4");
                function ns_gated_fn(): void {}
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "ns_gated_fn").Should().BeNull();
    }

    [Fact]
    public void NamespaceScopedFileLevelDeclare_Satisfied_RegistersNamespaceSymbols()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            namespace App {
                declare(php=">=8.4");
                function ns_gated_fn(): void {}
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "ns_gated_fn").Should().NotBeNull();
    }

    [Fact]
    public void Block_InvalidConstraint_DoesNotThrow_OmitsInnerSymbols_KeepsSiblings()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            function always_present(): void {}
            declare(php="not-a-constraint!!") {
                function bad_constraint_fn(): void {}
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
        FindFunction(global, "always_present").Should().NotBeNull();
        FindFunction(global, "bad_constraint_fn").Should().BeNull();
        FindDeclareBlocks(global).Should().ContainSingle(d =>
            d.IsPhpVersionGateInactive && !d.IsPhpVersionConstraintValid);
    }

    [Fact]
    public void FileLevel_Extension_Unsatisfied_OmitsExtension()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4");
            extension GatedOps extends string {
                function length(): int {
                    return \strlen($this);
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindObject(global, "GatedOps").Should().BeNull();
        RequireUserFile(global).IsPhpVersionGateInactive.Should().BeTrue();
    }

    [Fact]
    public void FileLevel_Extension_Satisfied_RegistersExtension()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4");
            extension GatedOps extends string {
                function length(): int {
                    return \strlen($this);
                }
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var ext = FindObject(global, "GatedOps");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();
        RequireUserFile(global).IsPhpVersionGateInactive.Should().BeFalse();
    }

    [Fact]
    public void Block_Struct_Unsatisfied_OmitsStruct()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            function always_present(): void {}
            declare(php=">=8.4") {
                type Point = struct {
                    int $x;
                };
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "always_present").Should().NotBeNull();
        FindObject(global, "Point").Should().BeNull();
    }

    [Fact]
    public void Block_Struct_Satisfied_RegistersStruct()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4") {
                type Point = struct {
                    int $x;
                };
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var point = FindObject(global, "Point");
        point.Should().NotBeNull();
        point!.IsStruct.Should().BeTrue();
    }

    [Fact]
    public void Block_Extension_Unsatisfied_OmitsExtension()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4") {
                extension GatedOps extends string {
                    function length(): int {
                        return \strlen($this);
                    }
                }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindObject(global, "GatedOps").Should().BeNull();
    }

    [Fact]
    public void Block_Extension_Satisfied_RegistersExtension()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4") {
                extension GatedOps extends string {
                    function length(): int {
                        return \strlen($this);
                    }
                }
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var ext = FindObject(global, "GatedOps");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();
    }

    [Fact]
    public void Tyhpdef_FileLevelDeclare_OmitsAtUnsatisfiedVersion()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            declare(php=">=8.4");
            function gated_tyhpdef_file(): void;
            """, phpVersion: "8.2", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "gated_tyhpdef_file").Should().BeNull();
    }

    [Fact]
    public void Tyhpdef_FileLevelDeclare_BindsAtSatisfiedVersion()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            declare(php=">=8.4");
            function gated_tyhpdef_file(): void;
            """, phpVersion: "8.4", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "gated_tyhpdef_file").Should().NotBeNull();
    }

    [Fact]
    public void Tyhpdef_BlockDeclare_OmitsInnerAtUnsatisfiedVersion()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            function tyhpdef_always(): void;
            declare(php=">=8.4") {
                function tyhpdef_only_84(): void;
            }
            """, phpVersion: "8.2", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "tyhpdef_always").Should().NotBeNull();
        FindFunction(global, "tyhpdef_only_84").Should().BeNull();
    }

    [Fact]
    public void Tyhpdef_BlockDeclare_BindsInnerAtSatisfiedVersion()
    {
        var (global, diagnostics) = Bind("""
            <?tyhpdef
            function tyhpdef_always(): void;
            declare(php=">=8.4") {
                function tyhpdef_only_84(): void;
            }
            """, phpVersion: "8.4", fileName: "test.tyhpdef");

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global, "tyhpdef_always").Should().NotBeNull();
        FindFunction(global, "tyhpdef_only_84").Should().NotBeNull();
    }

    [Fact]
    public void CompoundAndRange_StoresFullConstraintAndOmitsOutsideBand()
    {
        var source = """
            <?tyhp
            declare(php=">=8.4 <8.5") {
                function only_on_84(): void {}
            }
            """;

        var (global82, diagnostics82) = Bind(source, phpVersion: "8.2");
        diagnostics82.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionInvalidConstraint);
        FindFunction(global82, "only_on_84").Should().BeNull();
        FindDeclareBlocks(global82).Should().ContainSingle(d =>
            d.PhpVersionConstraint == ">=8.4 <8.5" && d.IsPhpVersionGateInactive);

        var (global84, diagnostics84) = Bind(source, phpVersion: "8.4");
        diagnostics84.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics84.Errors.Select(e => $"{e.Code}: {e.Message}")));
        FindFunction(global84, "only_on_84").Should().NotBeNull();
        FindDeclareBlocks(global84).Should().ContainSingle(d =>
            d.PhpVersionConstraint == ">=8.4 <8.5" && !d.IsPhpVersionGateInactive);

        var (global85, _) = Bind(source, phpVersion: "8.5");
        FindFunction(global85, "only_on_84").Should().BeNull();
        FindDeclareBlocks(global85).Should().ContainSingle(d =>
            d.PhpVersionConstraint == ">=8.4 <8.5" && d.IsPhpVersionGateInactive);
    }

    [Fact]
    public void DisjointDeclareBlocks_BindMatchingVariant()
    {
        var source = """
            <?tyhp
            declare(php=">=8.2 <8.4") {
                function example(string $v): mixed { return $v; }
            }
            declare(php=">=8.4") {
                function example(string $v, bool $strict = false): string { return $v; }
            }
            """;

        var (global82, diagnostics82) = Bind(source, phpVersion: "8.2");
        diagnostics82.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        diagnostics82.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        var fn82 = FindFunction(global82, "example");
        fn82.Should().NotBeNull();
        fn82!.Parameters.Should().HaveCount(1);

        var (global84, diagnostics84) = Bind(source, phpVersion: "8.4");
        diagnostics84.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);
        diagnostics84.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        var fn84 = FindFunction(global84, "example");
        fn84.Should().NotBeNull();
        fn84!.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void MissingPhpVersionOption_DefaultsTo82Locally()
    {
        var (global, diagnostics) = Bind("""
            <?tyhp
            declare(php=">=8.4");
            function gated(): void {}
            """, phpVersion: null);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDefaulted);
        FindFunction(global, "gated").Should().BeNull();
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
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = phpVersion ?? "",
                ProjectPath = tempDir,
                SuppressedWarnings = IsolatedCompilation.MissingPackageWarnings,
                SkipChecking = true,
            };
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull();
            return (result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static FileSymbol RequireUserFile(GlobalScope global)
    {
        var file = ((IBaseScope)global).GetAllChildScopes().OfType<FileScope>()
            .Should().ContainSingle(f => f.FileName.EndsWith("test.tyhp", StringComparison.Ordinal)).Subject;
        file.DeclarationSymbol.Should().NotBeNull();
        return file.DeclarationSymbol!;
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

        // Fall back to a full-tree scan so namespaced functions (unresolvable via a bare
        // relative name from the global scope) are still found by simple name for assertions.
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

    private static List<DeclareBlockSymbol> FindDeclareBlocks(GlobalScope global)
    {
        var found = new List<DeclareBlockSymbol>();
        void Walk(IBaseScope scope)
        {
            if (scope.DeclarationSymbol is DeclareBlockSymbol declare)
            {
                found.Add(declare);
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        return found;
    }
}
