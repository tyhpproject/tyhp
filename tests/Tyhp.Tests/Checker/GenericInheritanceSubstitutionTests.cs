using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Type arguments of a <em>generic ancestor</em> must reach members inherited from it. A member
/// declared on <c>Base&lt;T&gt;</c> is typed in terms of Base's <c>T</c>, which the receiver's own
/// type arguments say nothing about, so the substitution map has to walk the base chain and
/// re-substitute each level's <c>extends</c> arguments — keyed by parameter symbol, since two levels
/// may spell their parameters identically while binding them to different types.
///
/// See FOUND_BUGS.md item 11.
/// </summary>
[Trait("Category", "Checker")]
public class GenericInheritanceSubstitutionTests
{
    /// <summary>
    /// The prerequisite for everything else here: an inherited member has to resolve at all. Member
    /// resolution used to consult only <c>ObjectDeclarationSymbol.ExtendsType</c>, which a Tyhp
    /// <c>extends</c> clause leaves null, so every inherited member silently typed as unresolved and
    /// no diagnostic involving one could fire.
    /// </summary>
    [Fact]
    public void InheritedMember_ResolvesThroughAPlainBaseClass()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Base {
                protected ?string $item = null;
                public function get(): ?string {
                    return $this->item;
                }
            }
            class Derived extends Base {}
            class Reader {
                public static function read(Derived $box): ?int {
                    return $box->get();
                }
            }
            """);

        errors.Should().NotBeEmpty(
            "the inherited get() is ?string, so returning it as ?int must be reported");
    }

    [Fact]
    public void InheritedMember_IsTypedWithTheAncestorsArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Base<T> {
                protected ?T $item = null;
                public function get(): ?T {
                    return $this->item;
                }
            }
            class Derived<TValue> extends Base<TValue> {}
            class Reader {
                public static function read(Derived<int> $box): ?int {
                    return $box->get();
                }
            }
            """);

        errors.Should().BeEmpty(
            "Derived<int> passes int to Base's T, so the inherited get() returns ?int: "
            + Describe(errors));
    }

    [Fact]
    public void AncestorParameter_BoundToAConcreteType_IgnoresTheDerivedArgument()
    {
        // Both levels spell their parameter `T`, and they are bound to different types. Substituting
        // by bare name would answer the derived level's `int` for a member that is really `?string`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Base<T> {
                protected ?T $item = null;
                public function get(): ?T {
                    return $this->item;
                }
            }
            class Derived<T> extends Base<string> {
                public function own(): ?T {
                    return null;
                }
            }
            class Reader {
                public static function read(Derived<int> $box): ?string {
                    return $box->get();
                }
            }
            """);

        errors.Should().BeEmpty(
            "Base<string> binds Base's own T to string regardless of Derived's T: "
            + Describe(errors));
    }

    [Fact]
    public void AncestorParameter_DoesNotTakeTheDerivedArgumentWhenTheNamesCollide()
    {
        // The inverse of the previous case: reading the inherited member as the derived level's
        // argument must be rejected. Together the two pin the binding to the right level rather than
        // merely proving the type did not erase to `mixed`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Base<T> {
                protected ?T $item = null;
                public function get(): ?T {
                    return $this->item;
                }
            }
            class Derived<T> extends Base<string> {
                public function own(): ?T {
                    return null;
                }
            }
            class Reader {
                public static function read(Derived<int> $box): ?int {
                    return $box->get();
                }
            }
            """);

        errors.Should().NotBeEmpty(
            "the inherited get() is ?string, so returning it as ?int is a mismatch");
    }

    [Fact]
    public void ConcreteAncestorBinding_ReachesThroughANonGenericDerivedClass()
    {
        // The receiver carries no type arguments of its own, so the only source for Base's `T` is the
        // `extends` clause. The walk has to start even when the receiver level is non-generic.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Base<T> {
                protected ?T $item = null;
                public function get(): ?T {
                    return $this->item;
                }
            }
            class Derived extends Base<string> {}
            class Reader {
                public static function read(Derived $box): ?string {
                    return $box->get();
                }
            }
            """);

        errors.Should().BeEmpty(
            "Derived's extends clause is the only binding for Base's T: " + Describe(errors));
    }

    [Fact]
    public void ThreeLevelChain_NarrowingTwoParametersToOne_ResolvesTheTopLevel()
    {
        // The shape the tyhp/lambda package actually has: the middle level forwards both parameters
        // and the bottom level narrows one of them to a concrete type.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Expression<TSource, TReturn> {
                protected ?TReturn $compiled = null;
                public function compiled(): ?TReturn {
                    return $this->compiled;
                }
            }
            class PropertyPath<TSource, TReturn> extends Expression<TSource, TReturn> {}
            class PropertyPathBuilder<T> extends PropertyPath<T, string> {}
            class Reader {
                public static function read(PropertyPathBuilder<int> $path): ?string {
                    return $path->compiled();
                }
            }
            """);

        errors.Should().BeEmpty(
            "PropertyPathBuilder<int> pins Expression's TReturn to string through two levels: "
            + Describe(errors));
    }

    /// <summary>
    /// Type-guard narrowing maps the guard's own type parameters onto the arguments at the call site
    /// <em>by name</em>. When a caller happens to spell a parameter the same way the guard declares
    /// one, that map sends the parameter to itself, and substitution followed the binding forever —
    /// a stack overflow, which kills the process rather than reporting anything.
    ///
    /// The collision is what makes it fire, so the names here are deliberately identical: renaming
    /// either <c>TItem</c> makes this pass whether or not the guard is in place.
    /// </summary>
    [Fact]
    public void TypeGuardParameterSharingTheCallersName_DoesNotRecurForever()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Guard {
                public static function check<TItem>(mixed $value): $value is TItem {
                    return true;
                }
            }
            class Holder<TItem> {
                public function unwrap(mixed $value): TItem {
                    if (Guard::check<TItem>($value)) {
                        return $value;
                    }
                    throw new \LogicException('not a TItem');
                }
            }
            """);

        errors.Should().BeEmpty(
            "the guard narrows $value to TItem, which is what unwrap returns: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    /// <summary>
    /// Compiles and checks a self-contained snippet, returning only the diagnostics that originate
    /// from the snippet file. Compiling against the repo root pulls in the runtime packages, whose
    /// unrelated pre-existing diagnostics are filtered out.
    /// </summary>
    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
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

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
