using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// File-level <c>use extension</c> / <c>global use extension</c> must resolve after Pass 1
/// so a forward reference activates, and a missing name reports TYHP8011 instead of a silent no-op.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class FileUseExtensionBinderTests
{
    [Fact]
    public void GlobalUseExtension_BeforeDeclaration_Activates()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            global use extension \Foo\Bar;
            namespace Foo;
            extension Bar extends string {
                fn greet(): string => $this;
            }
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ext = FindObject(global, "Bar");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();
        global.GloballyActivatedExtensions.Should().Contain(ext);
    }

    [Fact]
    public void GlobalUseExtension_AfterDeclaration_StillActivates()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            namespace Foo;
            extension Bar extends string {
                fn greet(): string => $this;
            }
            global use extension \Foo\Bar;
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ext = FindObject(global, "Bar");
        ext.Should().NotBeNull();
        global.GloballyActivatedExtensions.Should().Contain(ext!);
    }

    [Fact]
    public void GlobalUseExtension_UnknownName_Reports8011()
    {
        var (_, diagnostics) = BindFixture("""
            <?tyhpdef
            namespace Foo;
            extension Bar extends string {
                fn greet(): string => $this;
            }
            global use extension \Foo\DoesNotExist;
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefExtensionNotFound);
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.TyhpdefExtensionNotFound
            && d.Message.Contains("DoesNotExist", StringComparison.Ordinal));
    }

    [Fact]
    public void FileUseExtension_BeforeSameFileDeclaration_Imports()
    {
        var (global, diagnostics) = BindTyhp("""
            <?tyhp
            use extension LaterOps;
            extension LaterOps extends string {
                function greet(): string {
                    return $this;
                }
            }
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ext = FindObject(global, "LaterOps");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();

        var file = FindFileScopeWithImport(global, ext);
        file.Should().NotBeNull();
        file!.ImportedExtensions.Should().Contain(ext);
    }

    [Fact]
    public void FileUseExtension_UnknownName_Reports8011()
    {
        var (_, diagnostics) = BindTyhp("""
            <?tyhp
            use extension DoesNotExist;
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefExtensionNotFound);
    }

    [Fact]
    public void FileUseExtension_HideKnownMember_ForwardRef_No4173()
    {
        var (_, diagnostics) = BindTyhp("""
            <?tyhp
            use extension LaterOps {
                LaterOps::greet hide;
            }
            extension LaterOps extends string {
                function greet(): string {
                    return $this;
                }
            }
            function demo(): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionHideUnknownMember);
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void FileUseExtension_HideUnknownMember_ForwardRef_Reports4173()
    {
        var (_, diagnostics) = BindTyhp("""
            <?tyhp
            use extension LaterOps {
                LaterOps::missing hide;
            }
            extension LaterOps extends string {
                function greet(): string {
                    return $this;
                }
            }
            function demo(): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerExtensionHideUnknownMember);
    }

    [Fact]
    public void FileUseExtension_OfTyhpdefExtension_ImportsWithoutGlobal()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            namespace Foo;
            extension Bar extends string {
                fn greet(): string => $this;
            }
            """, """
            <?tyhp
            use extension \Foo\Bar;
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ext = FindObject(global, "Bar");
        ext.Should().NotBeNull();
        global.GloballyActivatedExtensions.Should().NotContain(ext!);

        var file = FindFileScopeWithImport(global, ext!);
        file.Should().NotBeNull();
        file!.ImportedExtensions.Should().Contain(ext!);
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) BindFixture(
        string tyhpdef,
        string tyhp)
    {
        var result = Compile(tyhpdef, tyhp);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) BindTyhp(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpPath = Path.Combine(tempDir, "demo.tyhp");
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([tyhpPath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = tempDir,
                TyhpdefIncludePaths = [],
                SkipChecking = true,
            });
            result.GlobalScope.Should().NotBeNull();
            return (result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static CompilationResult Compile(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "ext.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "demo.tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            return compilationService.ParseFiles([tyhpPath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = tempDir,
                TyhpdefIncludePaths = ["ext.tyhpdef"],
                SkipChecking = true,
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && obj.IsExtension
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static FileScope? FindFileScopeWithImport(GlobalScope global, ObjectDeclarationSymbol ext)
    {
        foreach (var child in ((IBaseScope)global).GetAllChildScopes())
        {
            if (child is FileScope file && file.ImportedExtensions.Contains(ext))
            {
                return file;
            }
        }

        return null;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }
}
