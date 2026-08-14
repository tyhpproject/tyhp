using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TraitRequirementEmitterTests
{
    // A trait requirement names the base the using class must already extend, so `Entity` has to
    // exist or the declaration reports an unresolved extends target (3017). `Serializable` and
    // `JsonSerializable` need no declaration — they are PHP builtins from the extension tyhpdefs.
    private const string EntityDeclaration = @"
class Entity
{
    public function getId(): string
    {
        return '';
    }
}
";

    private static string CompileAndEmit(string tyhp, string? tempRoot = null)
    {
        var tempDir = tempRoot ?? Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "traits.tyhp");
        File.WriteAllText(filePath, tyhp);

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

            // Filter out infrastructure errors from tyhpdef packages (not our test's concern).
            // Also ignore BinderUnresolvedParameterType for `self\Alias` / `Class\Alias` refs —
            // binder support for class-scoped alias qualification is incomplete; emission still
            // expands via TypeAliasMap when the alias declaration was collected.
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty($"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_Trait_WithExtends_Clause_StripsExtends()
    {
        // GIVEN: a trait with an extends clause
        // WHEN: compiled and emitted
        // THEN: the extends clause is stripped; trait emits as plain PHP trait
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

trait TimestampedEntity extends Entity
{
    protected \DateTime $createdAt;
    protected \DateTime $updatedAt;

    public function touch(): void
    {
        $this->updatedAt = new \DateTime();
    }

    public function getEntityLabel(): string
    {
        return static::class . ':' . $this->getId();
    }
}
");

        // The extends clause should be stripped
        php.Should().NotContain("extends Entity");
        php.Should().Contain("trait TimestampedEntity");
        php.Should().Contain("protected \\DateTime $createdAt;");
        php.Should().Contain("public function touch(): void");
    }

    [Fact]
    public void Emit_Trait_WithImplements_Clause_StripsImplements()
    {
        // GIVEN: a trait with an implements clause
        // WHEN: compiled and emitted
        // THEN: the implements clause is stripped; trait emits as plain PHP trait
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }

    public function toCacheValue(): string
    {
        return $this->serialize();
    }
}
");

        // The implements clause should be stripped
        php.Should().NotContain("implements Serializable");
        php.Should().NotContain("extends Entity");
        php.Should().Contain("trait Cacheable");
        php.Should().Contain("public function getCacheKey(): string");
    }

    [Fact]
    public void Emit_Trait_WithBothExtendsAndImplements_Clauses_StripsBoth()
    {
        // GIVEN: a trait with both extends and implements clauses
        // WHEN: compiled and emitted
        // THEN: both clauses are stripped; trait emits as plain PHP trait
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

trait FullyManaged extends Entity implements Serializable, JsonSerializable
{
    public function toManagedArray(): array
    {
        return [
            'id' => $this->getId(),
            'serialized' => $this->serialize(),
            'json' => $this->jsonSerialize(),
        ];
    }
}
");

        // Both clauses should be stripped
        php.Should().NotContain("extends Entity");
        php.Should().NotContain("implements Serializable");
        php.Should().NotContain("JsonSerializable");
        php.Should().Contain("trait FullyManaged");
        php.Should().Contain("public function toManagedArray(): array");
    }

    [Fact]
    public void Emit_Trait_WithNoRequirements_EmitsNormally()
    {
        // GIVEN: a plain trait with no extends/implements clauses
        // WHEN: compiled and emitted
        // THEN: the trait emits as a normal PHP trait (unchanged)
        var php = CompileAndEmit(@"
<?tyhp

trait Timestamped
{
    public function getTimestamp(): string
    {
        return 'now';
    }
}
");

        // The trait should emit normally without any special handling
        php.Should().Contain("trait Timestamped");
        php.Should().NotContain("extends");
        php.Should().NotContain("implements");
        php.Should().Contain("public function getTimestamp(): string");
        php.Should().Contain("return 'now';");
    }

    [Fact]
    public void Emit_Trait_WithRequirements_BodyIsPreserved()
    {
        // GIVEN: a trait with extends/implements and a body containing methods
        // WHEN: compiled and emitted
        // THEN: the extends/implements clauses are stripped but the body is preserved unchanged
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }

    public function toCacheValue(): string
    {
        return $this->serialize();
    }

    public function fromCacheValue(string $data): void
    {
        $this->deserialize($data);
    }
}
");

        // Body should be preserved with all methods intact (concat may be parenthesized by emitter)
        php.Should().Contain("public function getCacheKey(): string");
        php.Should().Contain("static::class");
        php.Should().Contain("$this->getId()");
        php.Should().Contain("public function toCacheValue(): string");
        php.Should().Contain("return $this->serialize();");
        php.Should().Contain("public function fromCacheValue(string $data): void");
        php.Should().Contain("$this->deserialize($data);");
    }

    [Fact]
    public void Emit_Class_WithExtends_Clause_Preserved()
    {
        // GIVEN: a class with an extends clause (not a trait)
        // WHEN: compiled and emitted
        // THEN: the extends clause is preserved (this transformer only affects traits)
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

class User extends Entity
{
    public string $name;
    public int $age;

    public function __construct(string $name, int $age)
    {
        $this->name = $name;
        $this->age = $age;
    }
}
");

        // Class extends clause should be preserved
        php.Should().Contain("class User extends Entity");
    }

    [Fact]
    public void Emit_Interface_WithExtends_Clause_Preserved()
    {
        // GIVEN: an interface with extends clause
        // WHEN: compiled and emitted
        // THEN: the extends clause is preserved (this transformer only affects traits)
        var php = CompileAndEmit(@"
<?tyhp

interface MySerializable
{
    public function serialize(): string;
    public function unserialize(string $data): void;
}

interface MyJsonSerializable extends MySerializable
{
    public function jsonSerialize(): string;
    public function jsonDeserialize(string $data): void;
}
");

        // Interface extends clause should be preserved
        php.Should().Contain("interface MySerializable");
        php.Should().Contain("interface MyJsonSerializable extends MySerializable");
    }

    [Fact]
    public void Emit_MultipleTraits_WithRequirements_AllStripped()
    {
        // GIVEN: multiple traits, some with requirements, some without
        // WHEN: compiled and emitted
        // THEN: all extends/implements clauses are stripped from traits
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

trait TimestampedEntity extends Entity
{
    protected \DateTime $createdAt;
    protected \DateTime $updatedAt;

    public function touch(): void
    {
        $this->updatedAt = new \DateTime();
    }
}

trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }

    public function toCacheValue(): string
    {
        return $this->serialize();
    }
}

trait PlainTrait
{
    public function doSomething(): void
    {
        echo 'plain';
    }
}

trait FullyManaged extends Entity implements Serializable, JsonSerializable
{
    public function toManagedArray(): array
    {
        return [
            'id' => $this->getId(),
            'serialized' => $this->serialize(),
            'json' => $this->jsonSerialize(),
        ];
    }
}
");

        // All extends/implements clauses should be stripped from all traits
        php.Should().NotContain("extends Entity");
        php.Should().NotContain("implements Serializable");
        php.Should().NotContain("JsonSerializable");
        php.Should().Contain("trait TimestampedEntity");
        php.Should().Contain("trait Cacheable");
        php.Should().Contain("trait PlainTrait");
        php.Should().Contain("trait FullyManaged");
    }

    [Fact]
    public void Emit_Trait_WithRequirements_NoExtendsOrImplementsInOutput()
    {
        // GIVEN: a trait with both extends and implements clauses
        // WHEN: compiled and emitted
        // THEN: neither extends nor implements appears in the trait declaration
        var php = CompileAndEmit(@"
<?tyhp

" + EntityDeclaration + @"

trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }
}
");

        // Verify the trait declaration line has no extends/implements
        var lines = php.Split('\n');
        var traitLine = lines.FirstOrDefault(l => l.Contains("trait Cacheable"));
        traitLine.Should().NotBeNull();
        traitLine.Should().Be("trait Cacheable");
        php.Should().Contain("trait Cacheable");
        traitLine.Should().NotContain("extends");
        traitLine.Should().NotContain("implements");
    }
}
