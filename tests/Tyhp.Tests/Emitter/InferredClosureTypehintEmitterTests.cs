using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// FOUND_BUGS audit "emit inferred PHP typehints omitted from Tyhp source" #1 — when a closure
/// omits authored param/return types because call-site / annotation context supplies them, emit
/// still spells the recoverable PHP typehints.
/// </summary>
[Trait("Category", "Emitter")]
public class InferredClosureTypehintEmitterTests
{
    [Fact]
    public void Emit_UntypedClosure_PassedToTypedCallable_EmitsInferredParamAndReturn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function take(callable<int, string> $fn): string {
                return $fn(1);
            }

            function demo(): string {
                return take(function ($n) {
                    return (string) $n;
                });
            }
            """);

        php.Should().Contain("function (int $n): string");
        php.Should().NotContain("function ($n)");
    }

    [Fact]
    public void Emit_UntypedArrow_PassedToTypedCallable_EmitsInferredParamAndReturn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function take(callable<int, string> $fn): string {
                return $fn(1);
            }

            function demo(): string {
                return take(fn($n) => (string) $n);
            }
            """);

        php.Should().Contain("fn(int $n): string =>");
    }

    [Fact]
    public void Emit_UntypedClosure_AssignedToTypedCallableVar_EmitsInferredReturn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): int {
                callable<int> $fn = function () {
                    return 42;
                };
                return $fn();
            }
            """);

        php.Should().Contain("function (): int");
    }

    [Fact]
    public void Emit_UntypedClosure_PassedToGenericAsyncShape_EmitsErasedArrayReturn()
    {
        // Mirrors Promise::batch / _async<array<…>>(function () { … }) — type arg is known at the
        // call site; PHP must see `: array` after generic erasure.
        var php = CompileAndEmit("""
            <?tyhp
            class Host {
                public static function wrap<T>(callable<T> $fn): T {
                    return $fn();
                }

                public static function batch(): array {
                    return self::wrap<array<int, string>>(function () {
                        array<int, string> $out = [0 => "a", 1 => "b"];
                        return $out;
                    });
                }
            }
            """);

        php.Should().MatchRegex(@"function\s*\(\)\s*(?:use\s*\([^)]*\)\s*)?:\s*array");
    }

    [Fact]
    public void Emit_AuthoredClosureTypes_StillWinOverInferred()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function take(callable<int, string> $fn): string {
                return $fn(1);
            }

            function demo(): string {
                return take(function (int $n): string {
                    return (string) $n;
                });
            }
            """);

        php.Should().Contain("function (int $n): string");
    }

    [Fact]
    public void Emit_BareCallableExpectation_OmitsUselessMixedHints()
    {
        // Bare `callable` contextualizes to mixed params/return — emitting `: mixed` adds no
        // recoverable surface beyond PHP's default untyped form.
        var php = CompileAndEmit("""
            <?tyhp
            function take(callable $fn): mixed {
                return $fn();
            }

            function demo(): mixed {
                return take(function () {
                    return 1;
                });
            }
            """);

        php.Should().Contain("function ()");
        php.Should().NotContain("function (): mixed");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "inferred_closure.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            result.InferredClosureSignatures.Should().NotBeNull();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                result.RequiresWeakReferenceCapture,
                result.RequiresDisposableTryFinally,
                result.AsyncForeachKinds,
                result.RequiresGenericVariant,
                result.GenericCallTargets,
                result.InferredClosureSignatures);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
