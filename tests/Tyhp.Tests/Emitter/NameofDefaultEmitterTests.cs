using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class NameofDefaultEmitterTests
{
    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "emit.tyhp");
        File.WriteAllText(filePath, content);

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
    public void Emit_Nameof_Variable_ReturnsSimpleName()
    {
        var php = CompileAndEmit(@"<?tyhp function f(mixed $x): void { $n = nameof($x); }");
        php.Should().Contain("'x'");
        php.Should().NotContain("nameof(");
    }

    [Fact]
    public void Emit_Nameof_SimpleClass_ReturnsShortName()
    {
        var php = CompileAndEmit(@"<?tyhp class Sample {} function f(): void { $n = nameof(Sample); }");
        php.Should().Contain("'Sample'");
        php.Should().NotContain("nameof('Sample'");
        php.Should().NotContain("nameof('\\Sample'");
    }

    [Fact]
    public void Emit_Nameof_NamespacedClass_ReturnsShortName()
    {
        var php = CompileAndEmit(@"<?tyhp namespace App\Models; class User {} function f(): void { $n = nameof(User); }");
        php.Should().Contain("'User'");
        php.Should().NotContain("nameof('App\\Models\\User'");
        php.Should().NotContain("nameof('\\App\\Models\\User'");
    }

    [Fact]
    public void Emit_Nameof_InstanceMember_ReturnsMemberName()
    {
        var php = CompileAndEmit(@"<?tyhp class Box { public string $name = ''; } function f(Box $o): void { $n = nameof($o->name); }");
        php.Should().Contain("'name'");
        php.Should().NotContain("nameof('Box'");
    }

    [Fact]
    public void Emit_Nameof_StaticConstant_ReturnsConstantName()
    {
        var php = CompileAndEmit(@"<?tyhp class C { const A = 1; } function f(C $o): void { $n = nameof(C::A); }");
        php.Should().Contain("'A'");
        php.Should().NotContain("nameof('C'");
        php.Should().NotContain("nameof('\\C'");
    }

    [Fact]
    public void Emit_Nameof_MethodGeneric_ReturnsParameterSpelling()
    {
        var php = CompileAndEmit(
            @"<?tyhp function pick<TBatchReturn>(TBatchReturn $v): string { return nameof(TBatchReturn); }");
        php.Should().Contain("'TBatchReturn'");
        php.Should().NotContain("nameof(");
    }

    [Fact]
    public void Emit_Nameof_ClassGeneric_ReturnsParameterSpelling()
    {
        var php = CompileAndEmit(
            @"<?tyhp class Box<T> { public function label(): string { return nameof(T); } }");
        php.Should().Contain("'T'");
        php.Should().NotContain("nameof(");
    }

    [Fact]
    public void Emit_Nameof_PropertyPathFn_ReturnsLastSegment()
    {
        var php = CompileAndEmit(
            @"<?tyhp class User { public string $firstName; } function f(): void { $n = nameof(fn (User $u) => $u->firstName); }");
        php.Should().Contain("'firstName'");
        php.Should().NotContain("nameof(");
        php.Should().NotContain("fn (");
    }

    [Fact]
    public void Emit_Nameof_PropertyPathFn_Nested_ReturnsLastSegment()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Address { public string $city; }
            class User { public Address $address; }
            function f(): void { $n = nameof(fn (User $u) => $u->address->city); }
            """);
        php.Should().Contain("'city'");
        php.Should().NotContain("'address'");
        php.Should().NotContain("nameof(");
    }

    [Fact]
    public void Emit_Default_Int_ReturnsZero()
    {
        var php = CompileAndEmit(@"<?tyhp function f(int $x): void { $d = default(int); }");
        php.Should().Contain("0");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_Default_String_ReturnsEmpty()
    {
        var php = CompileAndEmit(@"<?tyhp function f(string $x): void { $d = default(string); }");
        php.Should().Contain("''");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_Default_Bool_ReturnsFalse()
    {
        var php = CompileAndEmit(@"<?tyhp function f(bool $x): void { $d = default(bool); }");
        php.Should().Contain("false");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_Default_Nullable_ReturnsNull()
    {
        var php = CompileAndEmit(@"<?tyhp function f(?int $x): void { $d = default(?int); }");
        php.Should().Contain("null");
        php.Should().NotContain("default(");
    }

    [Fact]
    public void Emit_Default_Array_ReturnsEmptyArray()
    {
        var php = CompileAndEmit(@"<?tyhp function f(array $x): void { $d = default(array); }");
        php.Should().Contain("[]");
        php.Should().NotContain("default(");
    }
}
