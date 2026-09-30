using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>typeof</c> of a struct and a generic decode helper emit
/// <c>\Tyhp\Type::struct(...)</c> rather than a fake <c>StructName::class</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class JsonDecodeEmitterTests
{
    [Fact]
    public void Typeof_NamedStruct_EmitsTypeStructNotClassName()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                float $x;
                float $y;
            };
            function demo(): void {
                \Tyhp\Type $t = typeof(Point);
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::struct(");
        php.Should().Contain("'x'");
        php.Should().Contain("'y'");
        php.Should().Contain("\\Tyhp\\Type::float()");
        php.Should().NotContain("Point::class");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void JsonDecode_StructTypeArg_EmitsTypeStructAtCallSite()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Test;
            type User = struct {
                string $name;
                int $age;
            };
            final class Json {
                public static function decode<T>(string $json): T {
                    typeof(T);
                    return default(T);
                }
            }
            function load(string $json): User {
                return Json::decode<User>($json);
            }
            """);

        php.Should().Contain("decode__tyhpGeneric(");
        php.Should().Contain("\\Tyhp\\Type::struct(");
        php.Should().Contain("'name'");
        php.Should().Contain("'age'");
        php.Should().NotContain("User::class");
        php.Should().NotContain("fromClassName");
    }

    [Fact]
    public void JsonDecodeAs_ForwardingWrapper_PassesStructTypeToDecode()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Test;
            type User = struct {
                string $name;
            };
            final class Json {
                public static function decode<T>(string $json): T {
                    typeof(T);
                    return default(T);
                }
            }
            function decodeAs<T>(string $json): T {
                return Json::decode<T>($json);
            }
            function load(string $json): User {
                return decodeAs<User>($json);
            }
            """);

        php.Should().Contain("decodeAs__tyhpGeneric(");
        php.Should().Contain("Json::decode__tyhpGeneric($__generic_T)");
        php.Should().Contain("\\Tyhp\\Type::struct(");
        php.Should().NotContain("User::class");
    }

    private static string CompileAndEmit(string content)
    {
        var result = IsolatedCompilation.ParseSnippet(content, phpVersion: "8.2");

        var unexpectedErrors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        unexpectedErrors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        var context = EmitContext.Create(
            result.GlobalScope,
            result.Diagnostics,
            requiresGenericVariant: result.RequiresGenericVariant,
            genericCallTargets: result.GenericCallTargets);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }
}
