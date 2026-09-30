using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.9 Phase 4 — rewrite class-kind alias <c>use</c> to <c>use function</c> when the
/// factory is referenced by short name; prune hints-only and fully-qualified-only usage.
/// </summary>
[Trait("Category", "Emitter")]
public class TypeAliasFactoryImportEmitterTests
{
    [Fact]
    public void Emit_UseAliasPlusFactoryCall_RewritesToUseFunction()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                function demo(): mixed {
                    return UserId();
                }
            }
            """);

        php.Should().Contain("use function App\\Types\\UserId;");
        php.Should().NotContain("use App\\Types\\UserId;");
        php.Should().Contain("UserId()");
    }

    [Fact]
    public void Emit_UseAliasPlusTypeof_RewritesToUseFunction()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                function demo(): mixed {
                    return typeof(UserId);
                }
            }
            """);

        php.Should().Contain("use function App\\Types\\UserId;");
        php.Should().NotContain("use App\\Types\\UserId;");
    }

    [Fact]
    public void Emit_HintsOnlyUseOfAlias_IsPruned()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                function take(UserId $id): int {
                    return $id;
                }
            }
            """);

        php.Should().NotContain("use App\\Types\\UserId;");
        php.Should().NotContain("use function App\\Types\\UserId;");
        php.Should().Contain("int $id");
    }

    [Fact]
    public void Emit_MixedGroup_SplitsClassAndFunctionImports()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                class User {}
                type UserId = int;
            }
            namespace App {
                use App\Types\{ User, UserId };
                function take(UserId $id): User {
                    UserId();
                    return new User();
                }
            }
            """);

        php.Should().Contain("use App\\Types\\User;");
        php.Should().Contain("use function App\\Types\\UserId;");
        php.Should().NotContain("use App\\Types\\UserId;");
    }

    [Fact]
    public void Emit_FullyQualifiedFactoryCall_DropsImport()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                function demo(): mixed {
                    return \App\Types\UserId();
                }
            }
            """);

        php.Should().NotContain("use App\\Types\\UserId;");
        php.Should().NotContain("use function App\\Types\\UserId;");
        php.Should().Contain("\\App\\Types\\UserId()");
    }

    [Fact]
    public void Emit_HintsOnlyUseWithSimilarlyPrefixedFunctionName_IsPruned()
    {
        // `getUserId(` textually contains `UserId(` as a substring. The alias is used only as a
        // hint here (no factory call), so the import must still be pruned rather than kept as
        // `use function` due to a naive substring match on the unrelated `getUserId` function.
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                function take(UserId $id): int {
                    return $id;
                }
                function getUserId(): int {
                    return 1;
                }
            }
            """);

        php.Should().NotContain("use App\\Types\\UserId;");
        php.Should().NotContain("use function App\\Types\\UserId;");
        php.Should().Contain("int $id");
    }

    [Fact]
    public void Emit_HintsOnlyUseWithMatchingCommentAndStringLiteral_IsPruned()
    {
        // A doc comment and a string literal both mention `UserId(` textually. Neither is an
        // actual factory call, so the import must still be pruned (hints-only usage).
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Types {
                type UserId = int;
            }
            namespace App {
                use App\Types\UserId;
                /** Callers should not call UserId() directly here. */
                function take(UserId $id): string {
                    return "call UserId() elsewhere";
                }
            }
            """);

        php.Should().NotContain("use App\\Types\\UserId;");
        php.Should().NotContain("use function App\\Types\\UserId;");
        php.Should().Contain("int $id");
    }

    [Fact]
    public void Emit_ImportedClassPlusStaticFactory_KeepsClassImport()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App {
                class UserService {
                    public type NameType = string;
                }
            }
            namespace Other {
                use App\UserService;
                function demo(): mixed {
                    return UserService::NameType();
                }
            }
            """);

        php.Should().Contain("use App\\UserService;");
        php.Should().NotContain("use function App\\UserService");
        php.Should().Contain("UserService::NameType()");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "aliases.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

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
}
