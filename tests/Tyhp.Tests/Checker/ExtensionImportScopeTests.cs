using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A user-declared extension is in scope in the file that declares it; other files and namespaces
/// bring it into scope with <c>use extension</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ExtensionImportScopeTests
{
    private const string Declaring = """
        <?tyhp
        namespace App\Extensions;

        extension StringHelpers extends string {
            fn shout(): string => $this . '!';
        }
        """;

    [Fact]
    public void Check_ExtensionFromOtherFileAndNamespace_WithoutUseExtension_Reports4101()
    {
        var diagnostics = Check(
            Declaring,
            """
            <?tyhp
            namespace App\Other;

            function go(string $name): string {
                return $name->shout();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "shout"));
    }

    [Fact]
    public void Check_ExtensionFromOtherFileSameNamespace_WithoutUseExtension_Reports4101()
    {
        var diagnostics = Check(
            Declaring,
            """
            <?tyhp
            namespace App\Extensions;

            function go(string $name): string {
                return $name->shout();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "shout"));
    }

    [Fact]
    public void Check_ExtensionFromOtherFile_WithUseExtension_Resolves()
    {
        var diagnostics = Check(
            Declaring,
            """
            <?tyhp
            namespace App\Other;

            use extension App\Extensions\StringHelpers;

            function go(string $name): string {
                return $name->shout();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_ExtensionDeclaredInSameFile_NeedsNoUseExtension()
    {
        var diagnostics = Check(
            """
            <?tyhp
            namespace App\Extensions;

            extension StringHelpers extends string {
                fn shout(): string => $this . '!';
            }

            function go(string $name): string {
                return $name->shout();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    private static bool HasParam(IDiagnostic diagnostic, string substring)
        => diagnostic.FormatParams is { } args
            && args.Any(p => (p?.ToString() ?? string.Empty)
                .Contains(substring, StringComparison.OrdinalIgnoreCase));

    private static DiagnosticBag Check(params string[] files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new List<string>();
        for (var i = 0; i < files.Length; i++)
        {
            var path = Path.Combine(tempDir, $"file{i}.tyhp");
            File.WriteAllText(path, files[i]);
            paths.Add(path);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles(paths, options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
