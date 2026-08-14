using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class UsingBlockEmitterTests
{
    [Fact]
    public void Emit_UsingBlock_EmitsTryFinallyDispose()
    {
        var php = EmitOnly("""
            <?tyhp
            function useResource(): void {
                using ($handle = new stdClass()) {
                    echo 'ok';
                }
            }
            """);

        php.Should().Contain("$handle = new stdClass();");
        php.Should().Contain("try {");
        php.Should().Contain("finally {");
        php.Should().Contain("if ($handle instanceof \\Tyhp\\Contracts\\IsDisposable)");
        php.Should().Contain("$handle->dispose();");
        php.Should().NotContain("PLACEHOLDER: using");
        php.Should().NotContain("DisposableScope");
    }

    [Fact]
    public void Emit_MultiResourceUsingBlock_EmitsNullInitReverseDisposeAggregateException()
    {
        var php = EmitOnly("""
            <?tyhp
            function useMultipleResources(): void {
                using ($db = new stdClass(), $cache = new stdClass()) {
                    echo 'ok';
                }
            }
            """);

        // Null-init all resources before try
        php.Should().Contain("$db = null;");
        php.Should().Contain("$cache = null;");

        // Assign inside try block
        php.Should().Contain("$db = new stdClass();");
        php.Should().Contain("$cache = new stdClass();");

        // Error collection and aggregate exception
        php.Should().Contain("$__disposeErrors = []");
        php.Should().Contain("throw new \\Tyhp\\Exceptions\\AggregateException");
        php.Should().Contain("One or more errors during disposal");
    }

    [Fact]
    public void Emit_MultiResourceUsingBlock_DisposesInReverseOrder()
    {
        // 3 resources: $a, $b, $c
        // Dispose order in finally should be: $c, $b, $a
        var php = EmitOnly("""
            <?tyhp
            function useThreeResources(): void {
                using ($a = new stdClass(), $b = new stdClass(), $c = new stdClass()) {
                    echo 'ok';
                }
            }
            """);

        // Null-init
        php.Should().Contain("$a = null;");
        php.Should().Contain("$b = null;");
        php.Should().Contain("$c = null;");

        // Assign inside try
        php.Should().Contain("$a = new stdClass();");
        php.Should().Contain("$b = new stdClass();");
        php.Should().Contain("$c = new stdClass();");

        // Dispose in reverse order: c, then b, then a
        var cPos = php.IndexOf("$c->dispose()", StringComparison.Ordinal);
        var bPos = php.IndexOf("$b->dispose()", StringComparison.Ordinal);
        var aPos = php.IndexOf("$a->dispose()", StringComparison.Ordinal);
        cPos.Should().BeGreaterThanOrEqualTo(0);
        bPos.Should().BeGreaterThan(cPos);
        aPos.Should().BeGreaterThan(bPos);
    }

    [Fact]
    public void Emit_UsingAwaitBlock_EmitsAsyncDisposeWithSyncFallback()
    {
        var php = EmitOnly("""
            <?tyhp
            function useAsyncResource(): void {
                using await ($conn = new stdClass()) {
                    echo 'ok';
                }
            }
            """);

        php.Should().Contain("$conn = new stdClass();");
        php.Should().Contain("try {");
        php.Should().Contain("finally {");
        php.Should().Contain("if ($conn instanceof \\Tyhp\\Contracts\\AsyncIsDisposable)");
        php.Should().Contain("\\Tyhp\\Promise::_await($conn->disposeAsync());");
        php.Should().Contain("elseif ($conn instanceof \\Tyhp\\Contracts\\IsDisposable)");
        php.Should().Contain("$conn->dispose();");
        php.Should().NotContain("}elseif");
        php.Should().NotContain("DisposableScope");
    }

    [Fact]
    public void Emit_MultiResourceUsingAwaitBlock_EmitsAsyncDisposeInReverseOrder()
    {
        var php = EmitOnly("""
            <?tyhp
            function useMultipleAsync(): void {
                using await ($a = new stdClass(), $b = new stdClass()) {
                    echo 'ok';
                }
            }
            """);

        php.Should().Contain("$a = null;");
        php.Should().Contain("$b = null;");
        php.Should().Contain("instanceof \\Tyhp\\Contracts\\AsyncIsDisposable");
        php.Should().Contain("\\Tyhp\\Promise::_await($b->disposeAsync())");
        php.Should().Contain("\\Tyhp\\Promise::_await($a->disposeAsync())");

        var bAsyncPos = php.IndexOf("\\Tyhp\\Promise::_await($b->disposeAsync())", StringComparison.Ordinal);
        var aAsyncPos = php.IndexOf("\\Tyhp\\Promise::_await($a->disposeAsync())", StringComparison.Ordinal);
        bAsyncPos.Should().BeGreaterThanOrEqualTo(0);
        aAsyncPos.Should().BeGreaterThan(bAsyncPos);
    }

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
