using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class ImportConsolidationTests
{
    /// <summary>
    /// Sub-task 1: the late post-walk pass reads <see cref="EmitContext.AdditionalImports"/> and emits
    /// consolidated <c>use</c> statements into the file header. Entries the body actually references
    /// survive usage pruning; entries it does not reference are dropped.
    /// </summary>
    [Fact]
    public void PruneFileImports_ConsumesAdditionalImports_AndEmitsUseStatements()
    {
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), new EmitConfig());
        context.AdditionalImports.Add(@"Foo\Bar");
        context.AdditionalImports.Add(@"Foo\Baz");
        context.AdditionalImports.Add(@"Foo\Unused");

        var file = NewFile(
            bodyLine: "echo Bar::class; echo Baz::class;",
            imports: Array.Empty<PhpImportDeclAst>());

        file.PruneFileImports(context);
        var output = file.Generate(context);

        // AdditionalImports are consolidated into one-per-line `use` statements (PSR-12), sorted.
        output.Should().Contain("use Foo\\Bar;");
        output.Should().Contain("use Foo\\Baz;");
        output.Should().NotContain("Foo\\Unused");
        output.Should().NotContain("use Foo\\Bar, Foo\\Baz;");
    }

    /// <summary>
    /// Sub-task 2: <c>use</c> statements are sorted alphabetically within each group and grouped
    /// classes → functions → constants (the order fixed by the Story 09 Phase 6 audit).
    /// </summary>
    [Fact]
    public void Generate_SortsAndGroupsUseStatements_ClassesThenFunctionsThenConstants()
    {
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), new EmitConfig());
        var provider = new TyhpSrcFileAst();

        // Deliberately unsorted and mixed across groups.
        var imports = new PhpImportDeclAst[]
        {
            MakeImport(@"App\func_b", "function", provider),
            MakeImport(@"App\CONST_Z", "const", provider),
            MakeImport(@"App\Zebra", null, provider),
            MakeImport(@"App\func_a", "function", provider),
            MakeImport(@"App\CONST_A", "const", provider),
            MakeImport(@"App\Apple", null, provider),
        };

        var file = NewFile(bodyLine: "/* Apple Zebra func_a func_b CONST_A CONST_Z */", imports);

        file.PruneFileImports(context);
        var output = file.Generate(context);

        // Each import is its own `use` line (PSR-12), clauses sorted alphabetically within the
        // group, and groups ordered classes → functions → constants with a blank line between groups.
        var appleLine = IndexOf(output, "use App\\Apple;");
        var zebraLine = IndexOf(output, "use App\\Zebra;");
        var funcALine = IndexOf(output, "use function App\\func_a;");
        var funcBLine = IndexOf(output, "use function App\\func_b;");
        var constALine = IndexOf(output, "use const App\\CONST_A;");
        var constZLine = IndexOf(output, "use const App\\CONST_Z;");

        appleLine.Should().BeLessThan(zebraLine, "classes sorted alphabetically");
        zebraLine.Should().BeLessThan(funcALine, "classes before functions");
        funcALine.Should().BeLessThan(funcBLine, "functions sorted alphabetically");
        funcBLine.Should().BeLessThan(constALine, "functions before constants");
        constALine.Should().BeLessThan(constZLine, "constants sorted alphabetically");

        // Blank line between import groups (PSR-12 §3).
        output.Should().Contain("use App\\Zebra;\n\nuse function App\\func_a;");
        output.Should().Contain("use function App\\func_b;\n\nuse const App\\CONST_A;");
    }

    /// <summary>
    /// Sub-task 3: <c>use</c> statements for erased types (type aliases, generic type parameters, struct
    /// declarations) are dropped even when the short name appears in the body. A real class import is kept.
    /// </summary>
    [Fact]
    public void PruneFileImports_DropsImportsForErasedTypes()
    {
        var result = Compile("""
            <?tyhp
            namespace App\Models;
            struct MyStruct { int $x = 0; }
            type MyAlias = int;
            class RealClass {}
            """);

        var context = EmitContext.Create(result.GlobalScope!, result.Diagnostics);
        var provider = new TyhpSrcFileAst();

        var imports = new PhpImportDeclAst[]
        {
            MakeImport(@"App\Models\MyStruct", null, provider),
            MakeImport(@"App\Models\MyAlias", null, provider),
            MakeImport(@"App\Models\RealClass", null, provider),
        };

        // Body references all three short names so the existing usage-pruning pass alone would keep them;
        // the erased-type drop must remove the struct + alias.
        var file = NewFile(bodyLine: "/* MyStruct MyAlias RealClass */", imports);

        file.PruneFileImports(context);
        var output = file.Generate(context);

        output.Should().NotContain("use App\\Models\\MyStruct;", "structs erase to array");
        output.Should().NotContain("use App\\Models\\MyAlias;", "type aliases are erased");
        output.Should().Contain("use App\\Models\\RealClass;", "real class import is kept");
    }

    /// <summary>
    /// Sub-task 4: an extension-class import that was rewritten to a fully-qualified static call is
    /// dropped from the file header — the leading backslash on the emitted call
    /// (<c>\App\MoneyFormatting::...</c>) makes the <c>use</c> clause redundant.
    /// </summary>
    [Fact]
    public void PruneFileImports_DropsFullyQualifiedStaticCallImports()
    {
        var result = Compile("""
            <?tyhp
            namespace App;
            class MoneyFormatting {}
            """);

        var context = EmitContext.Create(result.GlobalScope!, result.Diagnostics);
        var provider = new TyhpSrcFileAst();

        var imports = new PhpImportDeclAst[]
        {
            MakeImport(@"App\MoneyFormatting", null, provider),
        };

        // The rewrite emits the leading-backslash fully-qualified static call.
        var file = NewFile(bodyLine: @"echo \App\MoneyFormatting::doThing();", imports);

        context.CurrentOutputFile = file;
        context.TrackFullyQualifiedStaticCallImport(@"App\MoneyFormatting");
        context.CurrentOutputFile = null;

        file.PruneFileImports(context);
        var output = file.Generate(context);

        output.Should().NotContain("use App\\MoneyFormatting;", "FQ static call makes the use redundant");
        output.Should().Contain(@"\App\MoneyFormatting::doThing()", "body is preserved");
    }

    /// <summary>
    /// Sub-task 4 (safety): the FQ-static-call drop must NOT remove a <c>use</c> that a bare
    /// (non-fully-qualified) reference still needs. If the body references the class both via the
    /// rewritten <c>\App\MoneyFormatting::...</c> call and directly as <c>MoneyFormatting::...</c>, the
    /// import is kept so the bare reference resolves.
    /// </summary>
    [Fact]
    public void PruneFileImports_KeepsFullyQualifiedStaticCallImport_WhenBareReferenceExists()
    {
        var result = Compile("""
            <?tyhp
            namespace App;
            class MoneyFormatting {}
            """);

        var context = EmitContext.Create(result.GlobalScope!, result.Diagnostics);
        var provider = new TyhpSrcFileAst();

        var imports = new PhpImportDeclAst[]
        {
            MakeImport(@"App\MoneyFormatting", null, provider),
        };

        // Rewritten FQ call AND a separate bare short-name reference that relies on the `use`.
        var file = NewFile(
            bodyLine: @"echo \App\MoneyFormatting::doThing(); echo MoneyFormatting::other();",
            imports);

        context.CurrentOutputFile = file;
        context.TrackFullyQualifiedStaticCallImport(@"App\MoneyFormatting");
        context.CurrentOutputFile = null;

        file.PruneFileImports(context);
        var output = file.Generate(context);

        output.Should().Contain("use App\\MoneyFormatting;",
            "a bare short-name reference still needs the import");
        output.Should().Contain("MoneyFormatting::other()", "bare reference is preserved");
    }

    private static PHPOutputFile NewFile(string bodyLine, PhpImportDeclAst[] imports)
    {
        var provider = new TyhpSrcFileAst();
        var root = EmitItem.Empty(provider, EmitType.FileHeader);
        if (!string.IsNullOrWhiteSpace(bodyLine))
        {
            // EmitItem.Line attaches the item to `parent` internally; do not also Add it.
            _ = EmitItem.Line(new TyhpSrcFileAst(), EmitType.RootStatement, bodyLine, root);
        }

        var file = new PHPOutputFile
        {
            FileImports = imports.Length == 0 ? [] : [BuildList(imports)],
            Statements = [],
            RootEmitItem = root,
        };
        return file;
    }

    private static PhpImportDeclListAst BuildList(PhpImportDeclAst[] imports)
    {
        var list = new PhpImportDeclListAst();
        foreach (var import in imports)
        {
            list.AddChild(import);
        }
        return list;
    }

    private static PhpImportDeclAst MakeImport(string fqn, string? useType, TyhpSrcFileAst provider)
    {
        var useTypeToken = useType == null
            ? null
            : TokenValueAst.CreateFromContext(useType, 0, provider);
        return PhpImportDeclAst.CreateFromContext(fqn, alias: null, useType: useTypeToken, provider);
    }

    private static int IndexOf(string output, string needle)
    {
        var idx = output.IndexOf(needle, StringComparison.Ordinal);
        idx.Should().BeGreaterThanOrEqualTo(0, $"expected generated output to contain '{needle}':\n{output}");
        return idx;
    }

    private static CompilationResult Compile(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-import-consolidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        using var compilationService = new CompilationService();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            PhpVersion = "8.4",
            ProjectPath = tempDir,
        };

        return compilationService.ParseFiles([filePath], options);
    }
}
