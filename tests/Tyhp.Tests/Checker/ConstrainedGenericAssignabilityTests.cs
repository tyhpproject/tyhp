using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Constrained type parameters must be treated as subtypes of their upper bounds
/// (<c>T extends object</c> assignable to <c>object</c>), and foreach over
/// <c>T extends struct</c> must expose string property-name keys. See FOUND_BUGS #16.
/// </summary>
[Trait("Category", "Checker")]
public class ConstrainedGenericAssignabilityTests
{
    [Fact]
    public void ConstrainedTypeParam_ExtendsObject_IsAssignableToObject()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class C {
                public static function nameOf<T extends object>(T $object): string {
                    return \get_class($object);
                }
            }
            """);

        errors.Should().BeEmpty(
            "T extends object must be assignable to get_class(object): " + Describe(errors));
    }

    [Fact]
    public void ConstrainedTypeParam_ExtendsObjectIntersection_IsAssignableToObject()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class ObjectHelper {
                public static function with<TProperties extends struct, T extends object&TProperties>(
                    T $object,
                    TProperties $properties
                ): T {
                    return $object;
                }

                public static function className<TProperties extends struct, T extends object&TProperties>(
                    T $object
                ): string {
                    return \get_class($object);
                }
            }
            """);

        errors.Should().BeEmpty(
            "T extends object&S must be assignable to object: " + Describe(errors));
    }

    [Fact]
    public void ForeachOverStructConstrainedTypeParam_KeysAreStrings()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class ObjectHelper {
                public static function with<TProperties extends struct, T extends object&TProperties>(
                    T $object,
                    TProperties $properties
                ): T {
                    foreach ($properties as $property => $value) {
                        if (\property_exists($object, $property)) {
                            $object->{$property} = $value;
                        } else {
                            throw new \Exception($property);
                        }
                    }
                    return $object;
                }
            }
            """);

        errors.Should().BeEmpty(
            "foreach over TProperties extends struct must use string keys: " + Describe(errors));
    }

    [Fact]
    public void GenericReceiver_MethodParamSubstitutesTypeArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<TValue> {
                public function set(TValue $value): void {}
            }
            class Holder {
                private array<string, Box<mixed>> $boxes = [];
                public function put(string $name, mixed $value): void {
                    if (isset($this->boxes[$name])) {
                        $this->boxes[$name]->set($value);
                    }
                }
            }
            """);

        errors.Should().BeEmpty(
            "Box<mixed>::set must accept mixed after receiver substitution: " + Describe(errors));
    }

    [Fact]
    public void GenericReceiver_MethodParamRejectsIncompatibleArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<TValue> {
                public function set(TValue $value): void {}
            }
            class Holder {
                public function put(Box<string> $box): void {
                    $box->set(42);
                }
            }
            """);

        errors.Should().Contain(
            e => e.Message.Contains("string", StringComparison.Ordinal)
                 && (e.Message.Contains("int", StringComparison.Ordinal)
                     || e.Message.Contains("42", StringComparison.Ordinal)),
            "Box<string>::set must reject int: " + Describe(errors));
    }

    [Fact]
    public void GenericClass_SameDeclaration_IncompatibleTypeArgs_Rejected()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<T> {}
            function takeIntBox(Box<int> $box): void {}
            function pass(Box<string> $box): void {
                takeIntBox($box);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType,
            "Box<string> must not be assignable to Box<int>: " + Describe(errors));
    }

    [Fact]
    public void GenericClass_SameDeclaration_MatchingTypeArgs_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<T> {}
            function takeIntBox(Box<int> $box): void {}
            function pass(Box<int> $box): void {
                takeIntBox($box);
            }
            """);

        errors.Should().BeEmpty(
            "Box<int> must be assignable to Box<int>: " + Describe(errors));
    }

    [Fact]
    public void GenericClass_SameDeclaration_ToMixed_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Foo<T> {}
            function takeMixed(Foo<mixed> $foo): void {}
            function pass(Foo<string> $foo): void {
                takeMixed($foo);
            }
            """);

        errors.Should().BeEmpty(
            "Foo<string> must be assignable to Foo<mixed>: " + Describe(errors));
    }

    [Fact]
    public void GenericClass_SameDeclaration_TypeParamToMixed_Accepted()
    {
        // Mirrors PropertyAccessorObject::register<TValue>(PropertyAccessor<TValue>).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Foo<T> {}
            class Bag {
                private array<string, Foo<mixed>> $items = [];
                protected function register<TValue>(string $name, Foo<TValue> $item): void {
                    $this->items[$name] = $item;
                }
            }
            """);

        errors.Should().BeEmpty(
            "Foo<TValue> must be assignable to Foo<mixed>: " + Describe(errors));
    }

    [Fact]
    public void TypedArray_CovariantValueArgs_StillAccepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function take(array<int, int|string> $xs): void {}
            function pass(array<int, int> $xs): void {
                take($xs);
            }
            """);

        errors.Should().BeEmpty(
            "array value args remain covariant: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

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
