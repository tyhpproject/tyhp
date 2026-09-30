using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// FOUND_BUGS #52: file-level typed const is illegal in Tyhp source; class-level
/// consts require a type or inherit it invariantly from the original parent.
/// </summary>
[Trait("Category", "Checker")]
public class ClassAndFileConstTypeTests
{
    [Fact]
    public void FileLevelTypedConst_Reports4193()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            const int LIMIT = 10;
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerFileLevelTypedConstNotAllowed,
            Describe(result));
        UserErrors(result).Should().NotContain(
            e => e.Code == MessageCode.ParserUnexpectedError,
            "must not be a raw TYHP1002: " + Describe(result));
    }

    [Fact]
    public void FileLevelUntypedConst_Accepted()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            const LIMIT = 10;
            """);

        UserErrors(result).Should().NotContain(
            e => e.Code == MessageCode.CheckerFileLevelTypedConstNotAllowed,
            Describe(result));
    }

    [Fact]
    public void TyhpdefFileLevelTypedConst_AcceptedFromTyhpUse()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): int {
                return LIMIT;
            }
            """,
            """
            <?tyhpdef
            const int LIMIT ?? 10;
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void ClassConst_MissingType_Reports4194()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Owner {
                public const TAG = 't';
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerClassConstTypeRequired,
            Describe(result));
    }

    [Fact]
    public void ClassConst_WithType_Accepted()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Owner {
                public const string TAG = 't';
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void ChildClassConst_InfersParentType()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class ParentC {
                public const int X = 1;
            }
            class ChildC extends ParentC {
                public const X = 2;
            }
            function demo(): int {
                return ChildC::X;
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void ChildClassConst_ChangedType_Reports4195()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class ParentC {
                public const int X = 1;
            }
            class ChildC extends ParentC {
                public const string X = 'a';
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerClassConstTypeMismatch,
            Describe(result));
    }

    [Fact]
    public void ChildClassConst_InferredType_RejectsIncompatibleValue()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class ParentC {
                public const int X = 1;
            }
            class ChildC extends ParentC {
                public const X = 'a';
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            Describe(result));
    }

    [Fact]
    public void ChildClassConst_InferredType_EmitsExplicitTypeInPhp()
    {
        // PHP 8.3+ requires every override of a typed constant to redeclare a compatible type;
        // omitting it is a fatal error at class-load time. The checker infers `int` for
        // `ChildC::X` from `ParentC::X` (no diagnostic), so the emitted PHP must spell that
        // inferred type explicitly rather than reproducing the untyped source declaration.
        var php = CompileAndEmit("""
            <?tyhp
            class ParentC {
                public const int X = 1;
            }
            class ChildC extends ParentC {
                public const X = 2;
            }
            """);

        php.Should().Contain("public const int X = 2;");
    }

    [Fact]
    public void InterfaceConst_ImplementingClass_InfersType()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            interface Flags {
                public const int X = 1;
            }
            class Impl implements Flags {
                public const X = 2;
            }
            function demo(): int {
                return Impl::X;
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    /// <summary>
    /// Tyhpdef <c>interface Child extends Parent</c> stores the base list on
    /// <c>Extends</c>, not <c>ImplementsTypes</c>. A constant declared on a later
    /// base must still be visible, and typed, through the child.
    /// </summary>
    [Fact]
    public void TyhpdefInterfaceExtends_InheritedConstant_IsString()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): string {
                return \Writer\FilesystemOperator::TAG;
            }
            """,
            """
            <?tyhpdef
            namespace Writer {
                interface Marker {
                }
                interface FilesystemWriter {
                    public const string TAG ?? 'writer';
                }
                interface FilesystemOperator extends \Writer\Marker, \Writer\FilesystemWriter {
                }
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void TyhpdefInterfaceExtends_InheritedConstant_RejectsInt()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): int {
                return \Writer\FilesystemOperator::TAG;
            }
            """,
            """
            <?tyhpdef
            namespace Writer {
                interface Marker {
                }
                interface FilesystemWriter {
                    public const string TAG ?? 'writer';
                }
                interface FilesystemOperator extends \Writer\Marker, \Writer\FilesystemWriter {
                }
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType
                && e.Message.Contains("string", StringComparison.Ordinal)
                && e.Message.Contains("int", StringComparison.Ordinal),
            Describe(result));
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "consts.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            UserErrors(result).Should().BeEmpty(Describe(result));

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project: null,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
