using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// A name after <c>::</c> is a method or constant, even when a class has the same
/// spelling. Class lookup is case-insensitive, so resolving that name emits illegal PHP.
/// </summary>
[Trait("Category", "Emitter")]
public class StaticMemberNameEmitterTests
{
    [Fact]
    public void StaticCall_MethodNameMatchesImportedClass_EmitsTheIdentifier()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["types.tyhp"] = """
                <?tyhp
                namespace Demo;

                class View {
                    public static function make(string $name): string { return $name; }
                }

                class Route {
                    public static function view(string $path): string { return $path; }
                }
                """,
            ["app.tyhp"] = """
                <?tyhp
                namespace Demo\App;

                use Demo\Route;
                use Demo\View;

                function home(): string {
                    return Route::view('home');
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("Route::view(");
        result.Php.Should().NotContain("::\\");
    }

    [Fact]
    public void StaticCall_MethodNameMatchesSiblingClass_EmitsTheIdentifier()
    {
        var result = SnippetEmit.Compile("""
            <?tyhp
            class User {
            }

            class Auth {
                public static function user(): string { return 'a'; }
            }

            function current(): string {
                return Auth::user();
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("Auth::user(");
        result.Php.Should().NotContain("::\\");
    }
}
