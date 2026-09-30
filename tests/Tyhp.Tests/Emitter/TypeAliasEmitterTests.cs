using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TypeAliasEmitterTests
{
    private static string CompileAndEmit(string tyhp, string? tempRoot = null)
        => CompileAndEmitResult(tyhp, tempRoot).Php;

    private static (string Php, IReadOnlyList<PHPOutputFile> Files, IReadOnlyCollection<string> RequiredPackages)
        CompileAndEmitResult(
            string tyhp,
            string? tempRoot = null,
            string? tyhpdef = null,
            bool skipChecking = false)
    {
        var tempDir = tempRoot ?? Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "aliases.tyhp");
        File.WriteAllText(filePath, tyhp);
        var includePaths = new List<string>();
        if (tyhpdef is not null)
        {
            var tyhpdefPath = Path.Combine(tempDir, "stubs.tyhpdef");
            File.WriteAllText(tyhpdefPath, tyhpdef);
            includePaths.Add(tyhpdefPath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    skipChecking: skipChecking,
                    tyhpdefIncludePaths: includePaths.Count == 0 ? null : includePaths));

            // Filter out infrastructure errors from tyhpdef packages (not our test's concern).
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty($"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                requiresRuntimeGenericTracking: result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return (
                string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)),
                outputFiles,
                context.RequiredPackages);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_TypeAlias_Declaration_EmitsTypeFactoryInFunctionsFile()
    {
        var (php, files, packages) = CompileAndEmitResult(@"
<?tyhp

type UserId = int;

function convertToString(UserId $val): string
{
    return \strval($val);
}
");

        php.Should().NotContain("type UserId");
        php.Should().Contain("function UserId(): \\Tyhp\\Type");
        php.Should().Contain("return \\Tyhp\\Type::int();");
        php.Should().Contain("int $val");
        php.Should().NotContain("UserId $val");
        packages.Should().Contain("tyhp/core");

        var functions = files.Should().ContainSingle(f =>
            (f.OutputFilePath ?? "").Contains("_functions.php", StringComparison.OrdinalIgnoreCase)).Subject;
        functions.GeneratedContent.Should().Contain("function UserId(): \\Tyhp\\Type");
        functions.GeneratedContent.Should().Contain("return \\Tyhp\\Type::int();");
    }

    [Fact]
    public void Emit_GenericOptionalAlias_EmitsFactoryWithDefaultedTypeParameter()
    {
        var php = CompileAndEmitResult(@"
<?tyhp
type Optional<T> = T|null;
").Php;

        php.Should().Contain("function Optional(?\\Tyhp\\Type $T = null): \\Tyhp\\Type");
        php.Should().Contain("$T ??= \\Tyhp\\Type::mixed();");
        php.Should().Contain("return \\Tyhp\\Type::nullable($T);");
    }

    [Fact]
    public void Emit_ClassScopedTypeAlias_EmitsPublicAndPrivateStaticFactories()
    {
        var php = CompileAndEmitResult(@"
<?tyhp
class UserService {
    public type UserIdType = int;
    private type Row<T> = array<string, T>;
}
").Php;

        php.Should().Contain("public static function UserIdType(): \\Tyhp\\Type");
        php.Should().Contain("return \\Tyhp\\Type::int();");
        php.Should().Contain("private static function Row(?\\Tyhp\\Type $T = null): \\Tyhp\\Type");
        php.Should().Contain("$T ??= \\Tyhp\\Type::mixed();");
        php.Should().Contain("\\Tyhp\\Type::generic('array'");
        php.Should().NotContain("type UserIdType");
        php.Should().NotContain("type Row");
    }

    [Fact]
    public void Emit_TypeAlias_UsedInFunctionParameter_ExpandsToUnderlyingPHPTypes()
    {
        var php = CompileAndEmit(@"
<?tyhp

type scalarType = int|float|string|bool|null|array;

function convertToString(scalarType $val): string
{
    return \strval($val);
}
");

        php.Should().Contain("int|float|string|bool|array|null $val");
        php.Should().NotContain("scalarType $val");
        php.Should().Contain("function scalarType(): \\Tyhp\\Type");
    }

    [Fact]
    public void Emit_ClassScopedTypeAlias_UsedInConstructor_ExpandsSelfReference()
    {
        var php = CompileAndEmit(@"
<?tyhp

class MyObject
{
    public type MyObjOrNull = self|null;

    public function __construct(self\MyObjOrNull $buildFrom = null)
    {
        // do stuff
    }
}
");

        php.Should().Contain("public function __construct(?self $buildFrom = null");
        php.Should().NotContain("MyObjOrNull $buildFrom");
        php.Should().Contain("public static function MyObjOrNull(): \\Tyhp\\Type");
        php.Should().Contain("\\Tyhp\\Type::fromClassName(self::class)");
        php.Should().Contain("\\Tyhp\\Type::nullable(");
    }

    [Fact]
    public void Emit_ClassScopedTypeAlias_UsedOutsideClass_ExpandsToSelf()
    {
        var php = CompileAndEmit(@"
<?tyhp

class MyObject
{
    public type MyObjOrNull = self|null;

    public function __construct(self\MyObjOrNull $buildFrom = null)
    {
        // do stuff
    }
}

MyObject\MyObjOrNull $myObj = null;
");

        php.Should().Contain("$myObj = null");
        php.Should().NotContain("MyObjOrNull $myObj");
        php.Should().Contain("public static function MyObjOrNull(): \\Tyhp\\Type");
    }

    [Fact]
    public void Emit_NestedAlias_FactoryBodyCallsInnerFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;
type UserIds = array<UserId>;
");

        php.Should().Contain("function UserId(): \\Tyhp\\Type");
        php.Should().Contain("function UserIds(): \\Tyhp\\Type");
        php.Should().Contain("UserId()");
        var userIdsFn = php[php.IndexOf("function UserIds():", StringComparison.Ordinal)..];
        userIdsFn.Should().Contain("UserId()");
        userIdsFn.Should().NotContain("\\Tyhp\\Type::int()");
    }

    [Fact]
    public void Emit_TypeAliasFactory_IsEmittedEvenWithoutTypeof()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;

function f(): void
{
}
");

        php.Should().Contain("function UserId(): \\Tyhp\\Type");
        php.Should().Contain("return \\Tyhp\\Type::int();");
        php.Should().NotContain("typeof");
    }

    [Fact]
    public void Emit_TypeofSourceAlias_CallsFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;

function f(): void
{
    $t = typeof(UserId);
}
");

        php.Should().Contain("$t = UserId();");
        php.Should().NotContain("typeof(");
        php.Should().NotContain("\\Tyhp\\Type::mixed()");
    }

    [Fact]
    public void Emit_TypeofBoolInsideClassWithBoolMethod_UsesScalarFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
namespace App;
class Holder {
    public static fn bool(): self => new self();
    public static fn string(): self => new self();

    public function t(): void
    {
        $b = typeof(bool);
        $s = typeof(string);
    }
}
");

        php.Should().Contain("$b = \\Tyhp\\Type::bool();");
        php.Should().Contain("$s = \\Tyhp\\Type::string();");
        php.Should().NotContain("self::bool()");
        php.Should().NotContain("self::string()");
        php.Should().NotContain("Holder::bool()");
        php.Should().NotContain("typeof(");
    }

    /// <summary>
    /// Regression for the <c>\Tyhp\Type</c> self-hosting bug: a class with ordinary static
    /// methods named <c>string</c>/<c>int</c>/<c>bool</c> (e.g. <c>Type::bool()</c>) must not
    /// make constructor/property parameters typed <c>bool</c>/<c>?string</c>/
    /// <c>array&lt;int|string, self&gt;</c> resolve to an invented <c>\Tyhp\NamedClass\bool</c>
    /// alias. Those parameter types stay the plain builtins (TYHP4008 / TYHP4010 repro).
    /// </summary>
    [Fact]
    public void Emit_ClassWithScalarNamedStaticMethods_ConstructorParamsStayBuiltinTypes()
    {
        var php = CompileAndEmit(@"
<?tyhp
namespace App;
class TypeLike {
    private static array<string, self> $singletons = [];

    protected function __construct(
        private readonly string $kind,
        private readonly ?string $name = null,
        private readonly bool $isReadOnly = false,
        private readonly array<int|string, self> $structFields = [],
    ): void {}

    public static fn string(): self => self::$singletons['string'] ??= new self('scalar', 'string');

    public static fn int(): self => self::$singletons['int'] ??= new self('scalar', 'int');

    public static fn bool(): self => self::$singletons['bool'] ??= new self('scalar', 'bool');
}
");

        php.Should().Contain("string $kind");
        php.Should().Contain("?string $name");
        php.Should().Contain("bool $isReadOnly");
        php.Should().NotContain("TypeLike\\bool");
        php.Should().NotContain("TypeLike\\string");
        php.Should().NotContain("App\\bool");
        php.Should().NotContain("App\\string");
    }

    [Fact]
    public void Emit_TypeofGenericAlias_PassesRuntimeTypeArguments()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Optional<T = mixed> = T|null;

function f(): void
{
    $t = typeof(Optional<int>);
    $u = typeof(Optional);
}
");

        php.Should().Contain("Optional(\\Tyhp\\Type::int())");
        php.Should().Contain("$u = Optional();");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofMapOfAlias_NestsFactoryCalls()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;
type Map<TKey, TValue> = array<TKey, TValue>;

function f(): void
{
    $t = typeof(Map<string, UserId>);
}
");

        php.Should().Contain("Map(\\Tyhp\\Type::string(), UserId())");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofClassLevelAlias_UsesSelfAndClassCallee()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type UserIdType = int;
    public type NameType = string;

    public function t(): void
    {
        $a = typeof(self\UserIdType);
        $b = typeof(C\NameType);
    }
}
");

        php.Should().Contain("self::UserIdType()");
        php.Should().Contain("C::NameType()");
        php.Should().NotContain("typeof(");
        php.Should().NotContain("\\Tyhp\\Type::mixed()");
    }

    [Fact]
    public void Emit_IsAlias_ReifiesToTypeIs_IsClass_StaysInstanceof()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;
class User {}

function f(mixed $x): void
{
    $a = $x is UserId;
    $b = $x is User;
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, UserId())");
        php.Should().Contain("$x instanceof User");
        php.Should().NotContain("$x is UserId");
        php.Should().NotContain("$x instanceof UserId");
        php.Should().NotContain("\\Tyhp\\Type::is($x, User())");
    }

    [Fact]
    public void Emit_IsAliasOfClass_StillUsesTypeIs()
    {
        var php = CompileAndEmit(@"
<?tyhp
class User {}
type UserAlias = User;

function f(mixed $x): bool
{
    return $x is UserAlias;
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, UserAlias())");
        php.Should().NotContain("$x instanceof UserAlias");
        php.Should().NotContain("$x instanceof User;");
    }

    [Fact]
    public void Emit_DefaultSourceAlias_CallsFactoryDefaultValue()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;

function f(): void
{
    $d = default(UserId);
}
");

        php.Should().Contain("UserId()->defaultValue()");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_TypeofTyhpdefAlias_InlinesBodyWithoutFactoryCall()
    {
        var php = CompileAndEmitResult(
            @"
<?tyhp
function f(): void
{
    $t = typeof(StubId);
}
",
            tyhpdef: """
            <?tyhpdef
            type StubId = int;
            """).Php;

        php.Should().Contain("\\Tyhp\\Type::int()");
        php.Should().NotContain("function StubId");
        php.Should().NotContain("StubId()");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofTyhpdefAlias_WithGenericRuntime_CallsFactory()
    {
        var php = CompileAndEmitResult(
            @"
<?tyhp
function f(mixed $x): void
{
    $t = typeof(StubId);
    $b = $x is StubId;
}
",
            tyhpdef: """
            <?tyhpdef
            #[\Tyhp\GenericRuntime(aliasFactory: "StubId", layout: 1)]
            type StubId = int;
            """).Php;

        php.Should().Contain("StubId()");
        php.Should().NotContain("\\Tyhp\\Type::int()");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofNestedSourceAlias_CallsOuterFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
type UserId = int;
type UserIds = array<UserId>;

function f(): void
{
    $t = typeof(UserIds);
}
");

        php.Should().Contain("$t = UserIds();");
        var userIdsFn = php[php.IndexOf("function UserIds():", StringComparison.Ordinal)..];
        userIdsFn.Should().Contain("UserId()");
        userIdsFn.Should().NotContain("\\Tyhp\\Type::int()");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofFullyQualifiedAlias_EmitsQualifiedCall()
    {
        var php = CompileAndEmit(@"
<?tyhp
namespace App\Types;
type UserId = int;

function f(): void
{
    $t = typeof(\App\Types\UserId);
}
");

        php.Should().Contain("\\App\\Types\\UserId()");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TyhpdefAlias_DoesNotEmitFactoryFunction()
    {
        var php = CompileAndEmitResult(
            @"
<?tyhp
function f(StubId $id): void
{
}
",
            tyhpdef: """
            <?tyhpdef
            type StubId = int;
            """).Php;

        php.Should().NotContain("function StubId");
        php.Should().Contain("int $id");
        php.Should().NotContain("StubId $id");
    }

    [Fact]
    public void Emit_TypeofAndDefaultClassLevelAlias_FromOutsideClass_UseClassCallee()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type NameType = string;
}

function f(): void
{
    $a = typeof(C\NameType);
    $b = default(C\NameType);
    $c = false;
}
");

        php.Should().Contain("$a = C::NameType();");
        php.Should().Contain("$b = C::NameType()->defaultValue();");
        php.Should().NotContain("typeof(");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_DefaultClassLevelSelfAlias_CallsSelfFactoryDefaultValue()
    {
        // Regression test: `self\Alias` type expressions used inside a method body previously
        // failed checker resolution (CheckerNonConstantExpression) because the checker's
        // name-resolution scope for a method skipped past the enclosing class scope entirely
        // (TypeInferrer.GetResolutionScope used EnclosingFunction, which is never set for
        // methods, instead of EnclosingCallable).
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type NameType = string;

    public function t(): void
    {
        $d = default(self\NameType);
    }
}
");

        php.Should().Contain("$d = self::NameType()->defaultValue();");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_IsClassLevelSelfAlias_ReifiesToTypeIsWithSelfFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type NameType = string;

    public function t(mixed $x): bool
    {
        return $x is self\NameType;
    }
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, self::NameType());");
        php.Should().NotContain("$x is self\\NameType");
        php.Should().NotContain("$x instanceof");
    }

    [Fact]
    public void Emit_InstanceofClassLevelSelfAlias_ReifiesToTypeIsWithSelfFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type NameType = string;

    public function t(mixed $x): bool
    {
        return $x instanceof self\NameType;
    }
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, self::NameType());");
        php.Should().NotContain("$x instanceof self\\NameType");
        php.Should().NotContain("$x instanceof");
    }

    [Fact]
    public void Emit_IsClassLevelAliasFromOutside_UsesClassFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type NameType = string;
}

function f(mixed $x): bool
{
    return $x is C\NameType;
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, C::NameType());");
        php.Should().NotContain("$x is C\\NameType");
        php.Should().NotContain("$x instanceof");
    }

    [Fact]
    public void Emit_IsParentClassLevelAlias_ReifiesToTypeIsWithParentFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
class Base {
    public type IdType = int;
}

class Child extends Base {
    public function t(mixed $x): bool
    {
        return $x is parent\IdType;
    }
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, parent::IdType());");
        php.Should().NotContain("$x is parent\\IdType");
        php.Should().NotContain("$x instanceof");
    }

    [Fact]
    public void Emit_IsStaticClassLevelAlias_ReifiesToTypeIsWithStaticFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type NameType = string;

    public function t(mixed $x): bool
    {
        return $x is static\NameType;
    }
}
");

        php.Should().Contain("\\Tyhp\\Type::is($x, static::NameType());");
        php.Should().NotContain("$x is static\\NameType");
        php.Should().NotContain("$x instanceof");
    }

    [Fact]
    public void Emit_TypeofNullableStructAlias_WrapsStructInNullable()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Point = struct { int $x; int $y; };
type P = ?Point;
");

        php.Should().Contain(
            "return \\Tyhp\\Type::nullable(\\Tyhp\\Type::struct('Point', "
            + "['x' => \\Tyhp\\Type::int(), 'y' => \\Tyhp\\Type::int()], ['x', 'y']));");
    }

    [Fact]
    public void Emit_TypeofUnionContainingStructAlias_WrapsStructInUnion()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Point = struct { int $x; int $y; };
type P = Point|int;
");

        php.Should().Contain(
            "return \\Tyhp\\Type::union(\\Tyhp\\Type::struct('Point', "
            + "['x' => \\Tyhp\\Type::int(), 'y' => \\Tyhp\\Type::int()], ['x', 'y']), \\Tyhp\\Type::int());");
    }

    [Fact]
    public void Emit_TypeofArrayOfStructAlias_WrapsStructInGenericArray()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Point = struct { int $x; int $y; };
type Points = array<Point>;
");

        php.Should().Contain(
            "return \\Tyhp\\Type::generic('array', new \\Tyhp\\NamedType('TValue', "
            + "\\Tyhp\\Type::struct('Point', ['x' => \\Tyhp\\Type::int(), 'y' => \\Tyhp\\Type::int()], ['x', 'y'])));");
    }

    [Fact]
    public void Emit_TypeofIterableOfStructAlias_LabelsSoleArgumentTValue()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Point = struct { int $x; int $y; };
type Points = iterable<Point>;
");

        php.Should().Contain(
            "return \\Tyhp\\Type::generic('iterable', new \\Tyhp\\NamedType('TValue', "
            + "\\Tyhp\\Type::struct('Point', ['x' => \\Tyhp\\Type::int(), 'y' => \\Tyhp\\Type::int()], ['x', 'y'])));");
    }

    [Fact]
    public void Emit_TypeofTwoArgArrayAlias_LabelsTKeyAndTValue()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Point = struct { int $x; int $y; };
type PointsByName = array<string, Point>;
");

        php.Should().Contain(
            "return \\Tyhp\\Type::generic('array', new \\Tyhp\\NamedType('TKey', \\Tyhp\\Type::string()), "
            + "new \\Tyhp\\NamedType('TValue', "
            + "\\Tyhp\\Type::struct('Point', ['x' => \\Tyhp\\Type::int(), 'y' => \\Tyhp\\Type::int()], ['x', 'y'])));");
    }

    [Fact]
    public void Emit_ClassLevelAliasReferencingSiblingAliasBareName_CallsSiblingFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
class C {
    public type A = int;
    public type B = self\A;

    public function t(): void
    {
        $x = typeof(self\B);
    }
}
");

        php.Should().Contain("public static function B(): \\Tyhp\\Type");
        php.Should().Contain("return self::A();");
        php.Should().Contain("$x = self::B();");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_DefaultGenericAlias_PassesRuntimeTypeArgumentToFactoryDefaultValue()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Optional<T = mixed> = T|null;

function f(): void
{
    $d = default(Optional<int>);
}
");

        php.Should().Contain("$d = Optional(\\Tyhp\\Type::int())->defaultValue();");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_SourceAliasOfTyhpdefAlias_InlinesTyhpdefBodyInsideFactoryBody()
    {
        var php = CompileAndEmitResult(
            @"
<?tyhp
type UserId = StubId;
",
            tyhpdef: """
            <?tyhpdef
            type StubId = int;
            """).Php;

        php.Should().Contain("function UserId(): \\Tyhp\\Type");
        php.Should().Contain("return \\Tyhp\\Type::int();");
        php.Should().NotContain("StubId");
    }

    [Fact]
    public void Emit_TypeofGenericTyhpdefAlias_InlinesBodyWithSubstitutedTypeArgument()
    {
        var php = CompileAndEmitResult(
            @"
<?tyhp
function f(): void
{
    $t = typeof(StubOptional<int>);
}
",
            tyhpdef: """
            <?tyhpdef
            type StubOptional<T = mixed> = T|null;
            """).Php;

        php.Should().Contain("$t = \\Tyhp\\Type::union(\\Tyhp\\Type::int(), \\Tyhp\\Type::null());");
        php.Should().NotContain("StubOptional");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofUnionContainingAlias_CallsFactoryInsideUnion()
    {
        var php = CompileAndEmit(@"
<?tyhp
type A = int;

function f(): void
{
    $t = typeof(A|string);
}
");

        php.Should().Contain("$t = \\Tyhp\\Type::union(A(), \\Tyhp\\Type::string());");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofNullableAlias_WrapsFactoryCallInNullable()
    {
        var php = CompileAndEmit(@"
<?tyhp
type A = int;

function f(): void
{
    $t = typeof(?A);
}
");

        php.Should().Contain("$t = \\Tyhp\\Type::nullable(A());");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_DefaultNullableAlias_IsNullLiteral()
    {
        var php = CompileAndEmit(@"
<?tyhp
type A = int;

function f(): void
{
    $t = default(?A);
}
");

        php.Should().Contain("$t = null;");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_TypeofStructAlias_CallsFactoryReturningStructType()
    {
        var php = CompileAndEmit(@"
<?tyhp
type Point = struct { int $x; int $y; };
type P = Point;

function f(): void
{
    $t = typeof(P);
}
");

        php.Should().Contain(
            "function P(): \\Tyhp\\Type\n{\n    return \\Tyhp\\Type::struct('Point', "
            + "['x' => \\Tyhp\\Type::int(), 'y' => \\Tyhp\\Type::int()], ['x', 'y']);\n}");
        php.Should().Contain("$t = P();");
        php.Should().NotContain("\\Tyhp\\Type::array()");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofAliasOfGenericClass_CallsFactoryWithNoFurtherTypeArguments()
    {
        var php = CompileAndEmit(@"
<?tyhp
class Box<T> { public T $value; public function __construct(T $value) { $this->value = $value; } }
type IntBox = Box<int>;

function f(): void
{
    $t = typeof(IntBox);
}
");

        php.Should().Contain("function IntBox(): \\Tyhp\\Type");
        php.Should().Contain("\\Tyhp\\Type::generic(\\Box::class,");
        php.Should().Contain("$t = IntBox();");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofDuplicateAliasNameDifferentNamespace_EachCallsItsOwnFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
namespace App\A;
type Dup = int;

namespace App\B;
type Dup = string;

namespace App\C;
function f(): void
{
    $t = typeof(\App\A\Dup);
    $u = typeof(\App\B\Dup);
}
");

        php.Should().Contain("$t = \\App\\A\\Dup();");
        php.Should().Contain("$u = \\App\\B\\Dup();");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofFullyQualifiedAliasFromDifferentNamespaceInSameFile_CallsQualifiedFactory()
    {
        var php = CompileAndEmit(@"
<?tyhp
namespace App\Types;
type UserId = int;

namespace App\Other;
function f(): void
{
    $t = typeof(\App\Types\UserId);
}
");

        php.Should().Contain("$t = \\App\\Types\\UserId();");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_NoTypeAlias_ReturnsUnchangedCode()
    {
        var php = CompileAndEmit(@"
<?tyhp

function hello(): void
{
    echo 'world';
}
");

        php.Should().Contain("function hello(): void");
        php.Should().Contain("echo 'world';");
    }

    [Fact]
    public void Emit_NamespaceOnlyStructShapeAlias_LeavesBlankLineAfterNamespace()
    {
        var (_, files, _) = CompileAndEmitResult(@"
<?tyhp
namespace App\Shapes;
type Point = struct { int $x; int $y; };
");

        var functions = files.Should().ContainSingle(f =>
            (f.OutputFilePath ?? "").Contains("_functions.php", StringComparison.OrdinalIgnoreCase)).Subject;
        var php = functions.GeneratedContent ?? "";
        php.Should().Contain("namespace App\\Shapes;\n\n");
        php.Should().NotContain("function Point");
        php.Should().NotContain("class ");
    }
}
