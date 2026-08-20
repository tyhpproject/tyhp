using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class AsyncAwaitEmitterTests
{
    [Fact]
    public void Emit_AwaitExpression_WrapsWithPromiseAwait()
    {
        var php = EmitOnly("""
            <?tyhp
            async function load(): int {
                return await fetch();
            }
            function fetch(): int { return 1; }
            """);

        php.Should().Contain("\\Tyhp\\Promise::_await(");
        php.Should().NotContain("await $");
        php.Should().NotContain("await fetch");
    }

    [Fact]
    public void Emit_AsyncFunction_WrapsBodyWithPromiseAsync()
    {
        var php = EmitOnly("""
            <?tyhp
            async function load(): int {
                return await fetch();
            }
            function fetch(): int { return 1; }
            """);

        php.Should().Contain("function load(): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function (");
        php.Should().NotContain("async function");
    }

    [Fact]
    public void Emit_AsyncMethod_WrapsBodyWithPromiseAsync_EvenWhenEmpty()
    {
        var php = EmitOnly("""
            <?tyhp
            class DbConnectionAsync {
                async public function disposeAsync(): void {
                }
            }
            """);

        php.Should().Contain("function disposeAsync(): \\Tyhp\\Promise");
        php.Should().Contain("return \\Tyhp\\Promise::_async(function (): void {");
        php.Should().NotContain("function disposeAsync(): void");
        php.Should().NotContain("async function");
        php.Should().NotContain("async public");
    }

    [Fact]
    public void Emit_AsyncMethod_PreservesInnerReturnTypeAndAwait()
    {
        var php = EmitOnly("""
            <?tyhp
            class Loader {
                async public function load(int $id): int {
                    return await fetch($id);
                }
            }
            function fetch(int $id): int { return $id; }
            """);

        php.Should().Contain("function load(int $id): \\Tyhp\\Promise");
        php.Should().Contain("return \\Tyhp\\Promise::_async(function () use ($id): int {");
        php.Should().Contain("\\Tyhp\\Promise::_await(");
        php.Should().NotContain("function load(int $id): int");
    }

    [Fact]
    public void Emit_AsyncBlock_DesugarsToPromiseAsyncNotCallableWrapper()
    {
        var php = EmitOnly("""
            <?tyhp
            function wrap(int $id): mixed {
                return async {
                    return await fetch($id);
                };
            }
            function fetch(int $id): int { return $id; }
            """);

        php.Should().Contain("\\Tyhp\\Promise::_async(function ()");
        php.Should().Contain("use ($id)");
        php.Should().Contain("\\Tyhp\\Promise::_await(");
        php.Should().NotContain("async {");
        php.Should().NotContain("function (): \\Tyhp\\Promise");
        php.Should().NotContain("async function");
    }

    [Fact]
    public void Emit_EmptyAsyncBlock_EmitsPromiseAsync()
    {
        var php = EmitOnly("""
            <?tyhp
            function wrap(): mixed {
                return async {};
            }
            """);

        php.Should().Contain("\\Tyhp\\Promise::_async(function ()");
        php.Should().NotContain("async {");
    }

    // Parse-only emit avoids loading runtime/packages/async (pre-existing WeakMap TYHP3019).
    private static string EmitOnly(string content)
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(content);
        parseResult.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", parseResult.Diagnostics)}");
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }
}
