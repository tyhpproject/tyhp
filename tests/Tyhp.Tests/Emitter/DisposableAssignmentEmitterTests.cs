using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class DisposableAssignmentEmitterTests
{
    [Fact]
    public void Emit_DisposableAssignment_UsesDisposableScope()
    {
        var php = EmitOnly("""
            <?tyhp
            function f(): void {
                $handle := new stdClass();
            }
            """);

        php.Should().Contain("\\Tyhp\\DisposableScope::create()");
        php.Should().Contain("$handle = ");
        php.Should().Contain("->using(new stdClass())");
        php.Should().NotContain(":=");
        php.Should().NotContain("try {");
    }

    [Fact]
    public void Emit_UsingCallStatement_EmitsDisposableScopeAndUsing()
    {
        var php = EmitOnly("""
            <?tyhp
            function f(): void {
                using($h = new stdClass());
            }
            """);

        php.Should().Contain("\\Tyhp\\DisposableScope::create()");
        php.Should().Contain("$h = ");
        php.Should().Contain("->using(new stdClass())");
        php.Should().NotContain(":=");
        php.Should().NotContain("try {");
    }

    [Fact]
    public void Emit_UsingCallIfStatement_EmitsDisposableScopeInCondition()
    {
        var php = EmitOnly("""
            <?tyhp
            function f(): void {
                if (using($h = new stdClass())) {
                }
            }
            """);

        php.Should().Contain("\\Tyhp\\DisposableScope::create()");
        php.Should().Contain("->using($h = new stdClass())");
        // Same scope var must be created and used in the condition.
        php.Should().MatchRegex(@"(\$__scope(?:_\d+)?) = \\Tyhp\\DisposableScope::create\(\);[\s\S]*\1->using\(\$h = new stdClass\(\)\)");
        php.Should().NotContain(":=");
        php.Should().NotContain("try {");
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
