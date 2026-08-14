using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers FOUND_BUGS item 24: class-constant visibility must be recorded on
/// <c>ObjectConstantSymbol</c> and enforced on <c>::</c> access via
/// <c>CheckStaticMemberAccess</c> → <c>CheckMemberVisibility</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ClassConstantVisibilityTests
{
    [Fact]
    public void PrivateConstant_ReadFromAnotherClass_ReportsMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                private const SECRET = 's';
            }

            class Reader {
                public function read(): string {
                    return Owner::SECRET;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void PrivateConstant_ReadFromDeclaringClass_NoMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                private const SECRET = 's';

                public function read(): string {
                    return self::SECRET;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void PrivateConstant_ReadFromFunctionScope_ReportsMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                private const SECRET = 's';
            }

            function read(): string {
                return Owner::SECRET;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void PublicConstant_ReadFromAnotherClass_NoMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                public const TAG = 't';
            }

            class Reader {
                public function read(): string {
                    return Owner::TAG;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void BareConstant_ReadFromAnotherClass_NoMemberNotAccessible()
    {
        // PHP treats modifier-less class constants as public.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                const TAG = 't';
            }

            class Reader {
                public function read(): string {
                    return Owner::TAG;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void TypedPrivateConstant_ReadFromAnotherClass_ReportsMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                private const string SECRET = 's';
            }

            class Reader {
                public function read(): string {
                    return Owner::SECRET;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void EnumCase_ReadFromAnotherClass_NoMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Color {
                case Red;
            }

            class Reader {
                public function read(): Color {
                    return Color::Red;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void PrivateConstant_AssignedFromAnotherClass_ReportsMemberNotAccessible()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                private const SECRET = 's';
            }

            class Reader {
                public function read(): string {
                    string $value = Owner::SECRET;
                    return $value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void ProtectedConstant_ReadFromUnrelatedClass_NotYetRejected()
    {
        // Protected enforcement is deferred (trait declaring-object edge cases); document the
        // current private-only behavior so a future protected pass can flip this expectation.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Owner {
                protected const SECRET = 's';
            }

            class Reader {
                public function read(): string {
                    return Owner::SECRET;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.3",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
