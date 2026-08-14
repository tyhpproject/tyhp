using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regression tests for constructor-promoted property resolution, member visibility, and the
/// diagnostic argument fixes uncovered alongside them.
///
/// Promoted properties were previously registered only in the object scope and never added to the
/// object's member table, so <c>$this-&gt;prop</c> failed to resolve. When a same-named accessor
/// method existed the access resolved to the method (a <c>callable</c>) instead of the property.
/// Fixing that exposed (a) a member-visibility check that compared the wrong scopes and flagged
/// every same-class private access, and (b) two diagnostics raised with fewer arguments than their
/// message templates required.
/// </summary>
[Trait("Category", "Checker")]
public class PromotedPropertyResolutionTests
{
    [Fact]
    public void PromotedProperty_WithSameNamedAccessorMethod_ResolvesToPropertyType()
    {
        // `$this->item` must resolve to the promoted property, not the same-named accessor method
        // (whose value would be a `callable(): Item`), so the arrow-function return type matches.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Item {}

            class Box {
                public function __construct(
                    private readonly Item $item,
                ): void {}

                fn item(): Item => $this->item;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void PromotedPrivateProperty_ReadWithinDeclaringClass_NoMemberNotAccessible()
    {
        // The arrow-function body reads a promoted private property through `$this`; same-class
        // access must not be flagged as inaccessible.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Item {}

            class Box {
                public function __construct(
                    private readonly Item $item,
                ): void {}

                fn read(): Item => $this->item;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void PrivateProperty_ReadWithinDeclaringClass_NoMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Item {}

            class Box {
                private Item $item;

                fn read(): Item => $this->item;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void GenericArityMismatch_CarriesTypeNameAndBothCounts()
    {
        // A user generic type used with the wrong number of arguments must report the arity
        // mismatch with three format arguments (type name, expected, provided) so the localized
        // template `Generic type '{0}' expects {1}... but {2}...` formats without crashing.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box<T> {}

            function demo(): void {
                Box<Box, Box> $b;
            }
            """);

        var diagnostic = diagnostics.Errors
            .FirstOrDefault(d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch);

        diagnostic.Should().NotBeNull("a user generic used with too many arguments must report an arity mismatch");
        diagnostic!.FormatParams.Should().HaveCount(3);
        diagnostic.FormatParams[0].Should().Be("Box");
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
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
