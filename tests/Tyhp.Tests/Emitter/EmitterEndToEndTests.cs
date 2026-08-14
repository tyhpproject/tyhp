using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class EmitterEndToEndTests
{
    [Fact]
    public void Emit_TyhpPassThrough_RoundTripsSimpleClass()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            declare(strict_types=1);
            namespace App\Models;
            class User {
                public function greet(): void {
                    echo 'hello';
                }
            }
            """);

        parseResult.Diagnostics.Errors.Should().BeEmpty();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<Tyhp.TyhpLang.Ast.SrcFileAst>().Subject;

        var context = EmitContext.Create(null, new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);

        var php = outputFiles.Should().ContainSingle().Subject.GeneratedContent ?? "";
        php.Should().Contain("class User");
        php.Should().Contain("function greet");
        php.Should().StartWith("<?php");
    }
}
