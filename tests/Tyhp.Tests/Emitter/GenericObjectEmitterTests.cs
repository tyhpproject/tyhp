using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class GenericObjectEmitterTests
{
    [Fact]
    public void Emit_GenericClassWithoutTrackingTriggers_SkipsGenericObject()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public function identity(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().NotContain(@"use \Tyhp\Concerns\HasGenerics");
        php.Should().NotContain("__generic_");
        php.Should().NotContain("tyhpGenericObjectInit");
    }

    [Fact]
    public void Emit_GenericClassWithTypeof_InjectsTraitAndInit()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain(@"use \Tyhp\Concerns\HasGenerics;");
        php.Should().NotContain(@"use \Tyhp\Concerns\BootsTraits;");

        // Mechanism C: type arguments arrive through the init hook rather than through hidden
        // constructor parameters, and bindings are keyed by the class that declared them.
        php.Should().Contain(
            "protected function __initGenerics__tyhpGeneric(?\\Tyhp\\Type ...$generics): void");
        php.Should().Contain("$t = $generics[0] ?? \\Tyhp\\Type::mixed();");
        php.Should().Contain(
            "$this->__tyhpGeneric->init(static::class, \\Box::class, new \\Tyhp\\NamedType('T', $t));");
        php.Should().Contain("$this->__tyhpGeneric->markBound();");
        php.Should().Contain("__tyhpGeneric->resolvedType(\\Box::class, 'T')");

        // The synthesized constructor carries nothing but the gate.
        php.Should().Contain("public function __construct()");
        php.Should().Contain("$this->tyhpBootTraits();");
        php.Should().Contain("if ($this->__tyhpGeneric->needsInit()) {");
        php.Should().NotContain("$this->__tyhpGeneric ??= new \\Tyhp\\GenericObject();");
        php.Should().Contain("self::__initGenerics__tyhpGeneric(null);");
        php.Should().NotContain("$__generic_T = null");
    }

    [Fact]
    public void Emit_ConstructorReturnTypeVoid_IsErasedInPhp()
    {
        // Tyhp requires `: void` on constructors; PHP forbids return types on __construct.
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public function __construct(int $n): void {}
                public function __destruct(): void {}
            }
            """);

        php.Should().Contain("function __construct(int $n)");
        php.Should().Contain("function __destruct()");
        php.Should().NotContain("__construct(int $n): void");
        php.Should().NotContain("__destruct(): void");
    }

    [Fact]
    public void Emit_GenericTypedAndClosureProperties_RegisterSetPropertyType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder<TValue> {
                public ?\Closure<bool> $isset = null;
                public TValue $value;

                public function typeOfValue(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }
            """);

        php.Should().Contain(@"use \Tyhp\Concerns\HasGenerics;");
        php.Should().Contain("__tyhpGeneric->setPropertyType('isset'");
        php.Should().Contain("__tyhpGeneric->setPropertyType('value'");
        php.Should().Contain(@"\Tyhp\Type::generic(\Closure::class");
    }

    [Fact]
    public void Emit_GenericTypedProperty_SameNamespaceUnqualifiedName_EmitsFqcnInTypeGeneric()
    {
        // FOUND #1e: `Deferred` in `namespace Tyhp` must become `\Tyhp\Deferred::class`, not `\Deferred::class`.
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Cell<TValue> {
                public function __construct(public TValue $v): void {}
            }

            class Holder<TValue> {
                public function __construct(private Cell<TValue> $cell): void {}
                public function typeOfCell(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }
            """);

        php.Should().Contain("__tyhpGeneric->setPropertyType('cell'");
        php.Should().Contain(@"\Tyhp\Type::generic(\Probe\Cell::class");
        php.Should().NotContain(@"\Tyhp\Type::generic(\Cell::class");
    }

    [Fact]
    public void Emit_CallSiteNewWithTypeArgs_RoutesThroughTheGeneratedFactory()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            function make(): Box {
                return new Box<string>();
            }
            """);

        // Type arguments have to be bound before the constructor runs, which a plain `new` cannot do;
        // the factory binds them onto an unconstructed instance first.
        php.Should().Contain("return \\Box::new_Box__tyhpGeneric(\\Tyhp\\Type::string());");
        php.Should().NotContain("new Box(");
    }

    [Fact]
    public void Emit_CallSiteNewWithTypeArgs_OnUntrackedClass_StaysAPlainNew()
    {
        // Pair<A, B> has generic params but nothing that requires runtime tracking (no typeof,
        // no `new T`, no generic-typed properties) and no generic ancestor. It therefore gets no
        // generic plumbing at all, so the call site must stay a plain `new` — routing to a factory
        // that was never emitted would be a PHP "Call to undefined method" fatal error.
        var php = CompileAndEmit("""
            <?tyhp
            class Pair<A, B> {
                public function __construct(A $a, B $b): void {}
            }
            function make(): Pair {
                return new Pair<int, string>(1, 'x');
            }
            """);

        php.Should().Contain("return new Pair(1, 'x');");
        php.Should().NotContain("__tyhpGeneric");
        php.Should().NotContain(@"use \Tyhp\Concerns\HasGenerics");
    }

    [Fact]
    public void Emit_NewTypeParameter_UsesRuntimeClassNameLookup()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Factory<T> {
                public function create(): mixed {
                    return new T();
                }

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain(
            "new ($this->__tyhpGeneric->genericType(\\Factory::class, 'T')->getUnderlyingType()->getName())");
    }

    [Fact]
    public void Emit_RuntimeGenericChecks_EmitsTypeCheckForGenericParam()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public function take(T $value): void {
                    $x = typeof(T);
                }
            }
            """, runtimeGenericChecks: true);

        php.Should().Contain(@"\Tyhp\Type::check($value");
    }

    [Fact]
    public void Emit_RuntimeGenericChecks_EmitsTypeCheckForGenericReturn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Container<TValue> {
                private ?TValue $item = null;

                public function set(TValue $value): void {
                    $this->item = $value;
                }

                public function get(): ?TValue {
                    return $this->item;
                }
            }
            """, runtimeGenericChecks: true);

        php.Should().Contain(@"\Tyhp\Type::check($value");
        php.Should().MatchRegex(
            @"\$__tyhp_ret_\d+ = \$this->item;\s*"
            + @"\\Tyhp\\Type::check\(\$__tyhp_ret_\d+, \\Tyhp\\Type::nullable\(\$this->__tyhpGeneric->resolvedType\(\\Container::class, 'TValue'\)\)\);\s*"
            + @"return \$__tyhp_ret_\d+;");
        php.Should().NotContain("return $this->item;");
    }

    [Fact]
    public void Emit_RuntimeGenericChecks_Off_DoesNotCheckGenericReturn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Container<TValue> {
                private ?TValue $item = null;
                public function get(): ?TValue {
                    return $this->item;
                }
            }
            """, runtimeGenericChecks: false);

        php.Should().Contain("return $this->item;");
        php.Should().NotContain("__tyhp_ret_");
    }

    [Fact]
    public void Emit_RuntimeGenericChecks_ConstructorParam_UsesTraitLookupNotInitHookLocal()
    {
        // The boundary check lives in the constructor while the resolved `$t` local lives in the init
        // hook, so the check has to read the binding back through the trait.
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public function __construct(T $value): void {}
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """, runtimeGenericChecks: true);

        php.Should().Contain(
            "\\Tyhp\\Type::check($value, $this->__tyhpGeneric->resolvedType(\\Box::class, 'T'))");
        php.Should().NotContain("__construct(mixed $value): void");
        php.Should().Contain("function __construct(mixed $value");
    }

    [Fact]
    public void Emit_TypeofStillUsesGenericObjectLookup()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Sample<TValue> {
                public function t(): \Tyhp\Type {
                    return typeof(TValue);
                }
            }
            """);

        php.Should().Contain("($this->__tyhpGeneric->resolvedType(\\Sample::class, 'TValue'))");
    }

    [Fact]
    public void Checker_TypeofGenericParam_MarksRequiresRuntimeGenericTracking()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "mark.tyhp");
        File.WriteAllText(filePath, """
            <?tyhp
            class Box<T> {
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            result.RequiresRuntimeGenericTracking.Should().NotBeNull();
            result.RequiresRuntimeGenericTracking!.Should().Contain(s =>
                string.Equals(s.Name, "Box", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_TypeofClassGenericInStaticMethod_FallsBackToMixedInsteadOfThis()
    {
        // The runtime lookup reads the type argument off the instance, so emitting it in a static
        // method produces `Error: Using $this when not in object context` on first call. The checker
        // rejects this shape (TYHP4148), so the emitter guard is only reachable when something
        // bypasses the checker — but it must still emit valid PHP.
        var php = CompileAndEmit(
            """
            <?tyhp
            class Holder<T> {
                public static function staticTypeof(): \Tyhp\Type {
                    return typeof(T);
                }

                public function instanceTypeof(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """,
            allowedErrorCodes: [MessageCode.CheckerGenericTypeofInStaticContext]);

        php.Should().MatchRegex(
            @"static function staticTypeof\(\)[^{]*\{\s*return \\Tyhp\\Type::mixed\(\);");
        php.Should().Contain("__tyhpGeneric->resolvedType(\\Holder::class, 'T')");
    }

    /// <summary>
    /// <c>default(T)</c> on a class generic reads the type argument recorded on the instance and asks
    /// it for its zero value, rather than folding to the literal <c>null</c> it used to emit.
    /// </summary>
    [Fact]
    public void Emit_DefaultClassGeneric_UsesGenericObjectLookup()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Sample<TValue> {
                public function zero(): mixed {
                    return default(TValue);
                }
            }
            """);

        php.Should().Contain(
            "return $this->__tyhpGeneric->defaultValue(\\Sample::class, 'TValue');");
        php.Should().Contain("use \\Tyhp\\Concerns\\HasGenerics;");
    }

    [Fact]
    public void Emit_DefaultClassGenericInStaticMethod_FallsBackToNullInsteadOfThis()
    {
        // Same reasoning as the typeof guard above: the lookup needs the instance, so the checker
        // rejects the shape (TYHP4152) and the emitter guard only has to keep the PHP valid.
        var php = CompileAndEmit(
            """
            <?tyhp
            class Holder<T> {
                public static function staticZero(): mixed {
                    return default(T);
                }
            }
            """,
            allowedErrorCodes: [MessageCode.CheckerGenericDefaultInStaticContext]);

        php.Should().MatchRegex(@"static function staticZero\(\)[^{]*\{\s*return null;");
    }

    /// <summary>
    /// The lookup is a fatal error on a class that never received the trait, so <c>default(T)</c> has
    /// to be a tracking trigger — including in a position the rule walker does not visit.
    /// </summary>
    [Theory]
    [InlineData("return default(T);")]
    [InlineData("default(T);")]
    [InlineData("return match (true) { default => default(T) };")]
    public void Checker_DefaultGenericParam_MarksRequiresRuntimeGenericTracking(string body)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "mark.tyhp");
        File.WriteAllText(filePath, $$"""
            <?tyhp
            class Box<T> {
                public function zero(): mixed {
                    {{body}}
                    return null;
                }
            }
            """);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            result.RequiresRuntimeGenericTracking.Should().NotBeNull();
            result.RequiresRuntimeGenericTracking!.Should().Contain(s =>
                string.Equals(s.Name, "Box", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Trait("Category", "PHP")]
    [Fact]
    public void Emit_DefaultClassGeneric_ResolvesBoundTypeWhenExecuted()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var output = CompileAndRun("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                public function zero(): mixed {
                    return default(T);
                }
            }

            function run(): void {
                var_dump((new Box<int>())->zero());
                var_dump((new Box<string>())->zero());
                var_dump((new Box<bool>())->zero());
                var_dump((new Box<float>())->zero());
            }
            """);

        output.Should().Be(
            """
            int(0)
            string(0) ""
            bool(false)
            float(0)

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Emit_FreeGenericProperty_GetsSyntheticSetHook_Php84()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public T $value;

                public function __construct(): void {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain("public mixed $value {");
        php.Should().Contain("set(mixed $value) {");
        php.Should().Contain("$this->__tyhpGeneric->checkProperty('value', $value);");
        php.Should().Contain("$this->value = $value;");
        php.Should().Contain("$this->__tyhpGeneric->enablePropertyChecks();");
        php.Should().Contain("($this->__tyhpGeneric->resolvedType(\\Box::class, 'T'))");
    }

    [Fact]
    public void Emit_FreeGenericProperty_GetsRegisterAccessor_Php82()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public T $value;

                public function __construct(): void {}

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain(@"use \Tyhp\Concerns\UsesPropertyAccessors;");
        php.Should().Contain("__tyhpGeneric->checkProperty('value', $value)");
        php.Should().Contain("__tyhpPropertyHook->setBacking('value', $value, self::class)");
        php.Should().Contain("\\Tyhp\\Type::mixed()");
        php.Should().Contain("$this->__tyhpGeneric->enablePropertyChecks();");
        php.Should().NotContain("set(mixed $value) { $this->__tyhpGeneric->checkProperty('value'");
    }

    [Fact]
    public void Emit_ParameterizedGenericProperty_DoesNotGetSyntheticSetHook()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Cell<T> {}
            class Bag<T> {}

            class Holder<T> {
                public Cell<T> $cell;
                public Bag<T> $bag;
                public array<T> $items;

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().NotContain("__tyhpGeneric->checkProperty('cell'");
        php.Should().NotContain("__tyhpGeneric->checkProperty('bag'");
        php.Should().NotContain("__tyhpGeneric->checkProperty('items'");
        php.Should().NotContain("__tyhpGeneric->enablePropertyChecks()");
        php.Should().Contain("__tyhpGeneric->setPropertyType('cell'");
        php.Should().Contain("__tyhpGeneric->setPropertyType('bag'");
        php.Should().Contain("__tyhpGeneric->setPropertyType('items'");
    }

    [Fact]
    public void Emit_ReadonlyFreeGenericProperty_SkipsSyntheticSetHook()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public readonly T $value;

                public function __construct(T $value): void {
                    $this->value = $value;
                }

                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().NotContain("__tyhpGeneric->checkProperty('value'");
        php.Should().NotContain("__tyhpGeneric->enablePropertyChecks()");
    }

    private static string CompileAndRun(string tyhp) =>
        EmittedPhpRunner.Run(CompileToFiles(tyhp), "\\Probe\\run();");

    private static string CompileAndEmit(
        string tyhp,
        bool runtimeGenericChecks = false,
        MessageCode[]? allowedErrorCodes = null,
        string phpVersion = "8.4") =>
        string.Join(
            '\n',
            CompileToFiles(tyhp, runtimeGenericChecks, allowedErrorCodes, phpVersion)
                .Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(
        string tyhp,
        bool runtimeGenericChecks = false,
        MessageCode[]? allowedErrorCodes = null,
        string phpVersion = "8.4")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "generic-object.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject(runtimeGenericChecks, phpVersion);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = phpVersion,
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => allowedErrorCodes is null || !allowedErrorCodes.Contains(d.Code))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject(bool runtimeGenericChecks, string phpVersion = "8.4")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = phpVersion,
                ["build:runtimeGenericChecks"] = runtimeGenericChecks ? "true" : "false",
            })
            .Build();
        return new Project(configuration);
    }
}
