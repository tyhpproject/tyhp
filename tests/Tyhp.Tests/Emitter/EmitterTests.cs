using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class OutputPathResolverTests
{
    [Fact]
    public void ResolveObjectPath_UsesNamespaceSegmentsAndClassName()
    {
        var config = new EmitConfig("build/");

        var path = OutputPathResolver.ResolveObjectPath(@"\App\Models\User", config);

        path.Should().Be("build/App/Models/User.php");
    }

    [Fact]
    public void ResolveNamespaceFunctionsPath_ProducesFunctionsFileInNamespaceDirectory()
    {
        var config = new EmitConfig("build/");

        var path = OutputPathResolver.ResolveNamespaceFunctionsPath(@"App\Helpers", config);

        path.Should().Be("build/App/Helpers/_functions.php");
    }

    [Fact]
    public void ResolveNamespaceFunctionsPath_GlobalNamespace_UsesOutputRoot()
    {
        var config = new EmitConfig("build/");

        var path = OutputPathResolver.ResolveNamespaceFunctionsPath(null, config);

        path.Should().Be("build/_functions.php");
    }

    [Fact]
    public void ResolveEntryPointPath_ReplacesTyhpExtension()
    {
        var config = new EmitConfig("src/", sourceRoot: "/project/tyhp-src");

        var path = OutputPathResolver.ResolveEntryPointPath("/project/tyhp-src/web/index.tyhp", config);

        path.Should().Be("src/web/index.php");
    }
}

[Trait("Category", "Emitter")]
public class EmitItemFactoryTests
{
    [Fact]
    public void FactoryMethods_AttachToParentAndEmitContent()
    {
        var provider = new TyhpSrcFileAst();
        var root = EmitItem.Empty(provider, Tyhp.TyhpLang.Enum.EmitType.FileHeader);

        var line = EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.RootStatement, "echo 'hi';", root);
        var block = EmitItem.Block(provider, Tyhp.TyhpLang.Enum.EmitType.SubBlockStatement, "if (true) {", "}", root);
        var multi = EmitItem.MultiLine(provider, Tyhp.TyhpLang.Enum.EmitType.FileDeclare, ["declare(strict_types=1);"], root);

        root.Children.Should().HaveCount(3);
        line.StartContent.Should().ContainSingle("echo 'hi';");
        block.StartContent.Should().ContainSingle("if (true) {");
        block.EndContent.Should().ContainSingle("}");
        multi.StartContent.Should().ContainSingle("declare(strict_types=1);");

        var output = root.emit();
        output.Should().Contain("echo 'hi';");
        output.Should().Contain("if (true) {");
        output.Should().Contain("declare(strict_types=1);");
    }

    [Fact]
    public void AttachDocComment_KeepsMembersAddedAfterwards()
    {
        var provider = new TyhpSrcFileAst();
        var block = EmitItem.Block(provider, Tyhp.TyhpLang.Enum.EmitType.ObjectDeclaration, "class Foo {", "}", null);
        var withDoc = EmitItem.AttachDocComment("/** doc */", block);

        // Identity has to survive: callers keep adding members to the returned item, so handing back
        // a wrapper would strand every one of them.
        withDoc.Should().BeSameAs(block);

        EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.ObjectInstanceMethods, "public function m() {}", withDoc);

        var output = withDoc.emit();
        output.Should().Contain("/** doc */");
        output.Should().Contain("class Foo");
        output.Should().Contain("public function m() {}");

        withDoc.CreateCopy().emit().Should().Be(output);
    }

    [Fact]
    public void AttachDocComment_RealignsContinuationLines()
    {
        var provider = new TyhpSrcFileAst();
        var block = EmitItem.Block(provider, Tyhp.TyhpLang.Enum.EmitType.ObjectDeclaration, "class Foo {", "}", null);

        // As captured from source: continuation lines carry the column they sat at, which is not
        // necessarily the column the declaration is emitted at.
        EmitItem.AttachDocComment("/**\n         * Doc.\n         *     indented sample\n         */", block);

        var output = block.emit();
        // EmitItem.Block still accepts same-line open for unit-testing the tree; production
        // emission uses BlockBraceNextLine. This assertion covers AttachDocComment only.
        output.Should().Contain("/**\n * Doc.\n *     indented sample\n */\nclass Foo {");
    }

    [Fact]
    public void Emit_SiblingStatements_AreSeparatedByNewlinesNotGlued()
    {
        // Regression for Story 11 Phase 2 #6(c): concatenating sibling EmitItems glued body
        // statements into `$a = 1;    $b = 2;` and adjacent braces into `}function …`.
        var provider = new TyhpSrcFileAst();
        var block = EmitItem.BlockBraceNextLine(
            provider,
            Tyhp.TyhpLang.Enum.EmitType.RootStatement,
            "function demo(): void");
        EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.FunctionStatement, "$a = 1;", block);
        EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.FunctionStatement, "$b = 2;", block);

        var output = block.emit().Replace("\r\n", "\n");
        output.Should().Be("function demo(): void\n{\n    $a = 1;\n    $b = 2;\n}");
        output.Should().NotContain("$a = 1;    $b");
    }
}

[Trait("Category", "Emitter")]
public class PHPOutputFileSplitterTests
{
    private static IEnumerable<PHPOutputFile> SplitContent(string content, string fileName = "test.tyhp")
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(content, fileName);
        parseResult.Diagnostics.HasErrors.Should().BeFalse($"parse errors: {string.Join(", ", parseResult.Diagnostics)}");
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), new EmitConfig("build/"));
        return PHPOutputFile.FromAstTree(srcFile, context).ToList();
    }

    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "emit.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

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

    [Fact]
    public void Emit_TopLevelFunctions_StatementsAndDeclsUseNewlines()
    {
        // End-to-end lock for Story 11 Phase 2 #6(c): _functions.php must not cram sibling
        // statements onto one line or glue `}` to the next `function`.
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            function first(): void {
                $a = 1;
                $b = 2;
            }
            function second(): void {
                $c = 3;
            }
            """).Replace("\r\n", "\n");

        php.Should().Contain("function first(): void\n{\n    $a = 1;\n    $b = 2;\n}");
        php.Should().Contain("}\n\nfunction second(): void\n{");
        php.Should().NotContain("$a = 1;    $b");
        php.Should().NotContain("}function second");
    }

    [Fact]
    public void Split_ClassInNamespace_ProducesPsr4ObjectFile()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App\Models;
            class User {}
            """);

        files.Should().ContainSingle();
        var file = files.Single();
        file.IsPSR4ObjectDeclaration.Should().BeTrue();
        file.OutputFilePath.Should().Be("build/App/Models/User.php");
        file.Statements.Should().ContainSingle().Which.Should().BeOfType<PhpObjectTypeDeclAst>();
        file.SourceFileAst.Should().NotBeNull();
    }

    [Fact]
    public void Split_MultipleNamespaceBlocks_ProducesOneFilePerClass()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App\Alpha { class AlphaType {} }
            namespace App\Beta { class BetaType {} }
            """);

        files.Should().HaveCount(2);
        files.Select(f => f.OutputFilePath).Should().BeEquivalentTo(
        [
            "build/App/Alpha/AlphaType.php",
            "build/App/Beta/BetaType.php",
        ]);
    }

    [Fact]
    public void Split_NamespaceFunction_GroupsIntoFunctionsFile()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App\Helpers;
            function helperOne(): void {}
            function helperTwo(): void {}
            """);

        files.Should().ContainSingle();
        var file = files.Single();
        file.IsPSR4ObjectDeclaration.Should().BeFalse();
        file.OutputFilePath.Should().Be("build/App/Helpers/_functions.php");
        file.Statements.Should().HaveCount(2);
        file.Statements.Should().AllBeOfType<PhpFunctionDeclAst>();
    }

    [Fact]
    public void Split_RootCode_ProducesEntryPointFile()
    {
        var files = SplitContent("""
            <?tyhp
            $value = 1;
            """);

        files.Should().ContainSingle();
        files.Single().OutputFilePath.Should().Be("build/test.php");
        files.Single().IsPSR4ObjectDeclaration.Should().BeFalse();
    }

    [Fact]
    public void Split_NamespacedRootCode_PreservesNamespaceOnEntryPoint()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App\Demo;
            function main(): int { return 0; }
            exit(main());
            """);

        var entry = files.Should().ContainSingle(f => f.IsEntryPoint).Subject;
        entry.FileNameSpace.Should().BeOfType<PhpNamespaceDeclAst>()
            .Which.Identifier.Should().Be(@"App\Demo");
        entry.Statements.Should().ContainSingle();

        var functions = files.Should().ContainSingle(f =>
            f.OutputFilePath.EndsWith("_functions.php", StringComparison.OrdinalIgnoreCase)).Subject;
        functions.FileNameSpace.Should().BeOfType<PhpNamespaceDeclAst>()
            .Which.Identifier.Should().Be(@"App\Demo");
    }

    [Fact]
    public void Split_BlockNamespacedRootCode_PreservesNamespaceOnEntryPoint()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App\Demo {
                function main(): int { return 0; }
                exit(main());
            }
            """);

        var entry = files.Should().ContainSingle(f => f.IsEntryPoint).Subject;
        // Braced namespaces may be PhpNamespaceDeclAst (with body) or PhpBlockNamespaceDeclAst.
        entry.FileNameSpace.Should().NotBeNull();
        var nsName = entry.FileNameSpace switch
        {
            PhpNamespaceDeclAst ns => ns.Identifier,
            PhpBlockNamespaceDeclAst block => block.Identifier,
            _ => null,
        };
        nsName.Should().Be(@"App\Demo");
    }

    [Fact]
    public void Split_StructAndTypeAlias_AreSkipped()
    {
        var files = SplitContent("""
            <?tyhp
            type MyAlias = string;
            struct Point { int $x; int $y; }
            $value = 1;
            """);

        files.Should().ContainSingle();
        files.Single().Statements.Should().HaveCount(1);
    }

    [Fact]
    public void Split_PropagatesFileLevelImportsAndDeclares()
    {
        var files = SplitContent("""
            <?tyhp
            declare(strict_types=1);
            use App\Foo;
            namespace App\Models;
            class User {}
            """);

        var file = files.Single();
        file.FileImports.Should().HaveCount(1);
        file.FileDeclares.Should().HaveCount(1);
    }

    [Fact]
    public void Split_WrappedObjectDeclaration_GoesToEntryPointNotPsr4()
    {
        // Non-existence wrappers (while/switch/etc.) stay on the entry point. A valid
        // class_exists gate moves with the class — covered by Split_GatedClass_MovesWithGate.
        var files = SplitContent("""
            <?tyhp
            while (false) {
                class Foo {}
            }
            """);

        files.Should().ContainSingle();
        var file = files.Single();
        file.IsPSR4ObjectDeclaration.Should().BeFalse();
        file.Statements.Should().ContainSingle().Which.Should().BeOfType<PhpLoopAst>();
    }

    [Fact]
    public void Split_GatedClass_MovesWithGateToPsr4File()
    {
        var files = SplitContent("""
            <?tyhp
            if (!\class_exists('Foo')) {
                class Foo {}
            }
            """);

        files.Should().ContainSingle();
        var file = files.Single();
        file.IsPSR4ObjectDeclaration.Should().BeTrue();
        file.OutputFilePath.Should().Contain("Foo.php");
        file.Statements.Should().ContainSingle().Which.Should().BeOfType<PhpIfAst>();
    }

    [Fact]
    public void Split_GatedFunction_MovesWithGateToFunctionsFileAtEnd()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App;
            function early(): void {}
            if (!\function_exists("\\App\\demo")) {
                function demo(): void {}
            }
            function late(): void {}
            """);

        var functionsFile = files.Should().ContainSingle(f =>
            (f.OutputFilePath ?? "").Contains("_functions.php", StringComparison.OrdinalIgnoreCase)).Subject;

        functionsFile.Statements.Should().HaveCount(3);
        functionsFile.Statements[0].Should().BeOfType<PhpFunctionDeclAst>()
            .Which.Identifier.Should().Be("early");
        functionsFile.Statements[1].Should().BeOfType<PhpFunctionDeclAst>()
            .Which.Identifier.Should().Be("late");
        functionsFile.Statements[2].Should().BeOfType<PhpIfAst>();
    }

    [Fact]
    public void Split_GatedFunction_UnqualifiedName_NotMovedAsGate()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App;
            if (!\function_exists("demo")) {
                function demo(): void {}
            }
            """);

        files.Should().NotContain(f =>
            (f.OutputFilePath ?? "").Contains("_functions.php", StringComparison.OrdinalIgnoreCase));
        files.Should().ContainSingle(f => f.IsEntryPoint)
            .Which.Statements.Should().ContainSingle().Which.Should().BeOfType<PhpIfAst>();
    }

    [Fact]
    public void Split_GatedFunction_NamespaceConcat_MovesWithGate()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App;
            if (!\function_exists(__NAMESPACE__ . '\\demo')) {
                function demo(): void {}
            }
            """);

        var functionsFile = files.Should().ContainSingle(f =>
            (f.OutputFilePath ?? "").Contains("_functions.php", StringComparison.OrdinalIgnoreCase)).Subject;
        functionsFile.Statements.Should().ContainSingle().Which.Should().BeOfType<PhpIfAst>();
    }

    [Fact]
    public void Emit_GatedFunction_EmitsIfAroundFunctionInFunctionsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "emit.tyhp");
        File.WriteAllText(filePath, """
            <?tyhp
            namespace App;
            function early(): void {}
            if (!\function_exists("\\App\\demo")) {
                function demo(): void {
                    $x = 1;
                }
            }
            """);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!).ToList();

            // Gated function leaves no entry-point remnant — only _functions.php.
            outputFiles.Should().NotContain(f => f.IsEntryPoint);
            var functionsFile = outputFiles.Should().ContainSingle(f =>
                (f.OutputFilePath ?? "").Contains("_functions.php", StringComparison.OrdinalIgnoreCase)).Subject;
            var php = functionsFile.GeneratedContent ?? "";

            php.Should().Contain("function early(): void");
            php.Should().Contain("if (!\\function_exists(__NAMESPACE__ . '\\demo'))");
            php.Should().NotContain("\\App\\demo");
            php.Should().Match(s =>
                s.Contains("if (!\\function_exists")
                && s.IndexOf("function early", StringComparison.Ordinal)
                    < s.IndexOf("if (!\\function_exists", StringComparison.Ordinal)
                && s.IndexOf("if (!\\function_exists", StringComparison.Ordinal)
                    < s.IndexOf("function demo", StringComparison.Ordinal));
            php.Should().Contain("function demo(): void");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Split_WrappedWhileDeclaration_GoesToEntryPointNotPsr4()
    {
        var files = SplitContent("""
            <?tyhp
            while (false) {
                class GuardedWhile {}
            }
            """);

        files.Should().ContainSingle();
        files.Single().IsPSR4ObjectDeclaration.Should().BeFalse();
        files.Single().Statements.Should().ContainSingle().Which.Should().BeOfType<PhpLoopAst>();
    }

    [Fact]
    public void Split_WrappedSwitchDeclaration_GoesToEntryPointNotPsr4()
    {
        var files = SplitContent("""
            <?tyhp
            switch (1) {
                case 0:
                    class GuardedSwitch {}
                    break;
            }
            """);

        files.Should().ContainSingle();
        files.Single().IsPSR4ObjectDeclaration.Should().BeFalse();
        files.Single().Statements.Should().ContainSingle().Which.Should().BeOfType<PhpConditionalAst>();
    }

    [Fact]
    public void Split_NamespaceBlockDeclare_PropagatesToNamespaceOutputFiles()
    {
        var files = SplitContent("""
            <?tyhp
            namespace App {
                declare(strict_types=1);
                class Foo {}
            }
            """);

        files.Should().ContainSingle();
        files.Single().FileDeclares.Should().ContainSingle();
        files.Single().OutputFilePath.Should().Be("build/App/Foo.php");
    }

    [Fact]
    public void Split_OutputFileDirectiveWithoutBody_DoesNotPolluteFileDeclares()
    {
        var files = SplitContent("""
            <?tyhp
            declare(output_file="custom/out.php");
            $value = 1;
            """);

        files.Should().ContainSingle();
        var file = files.Single();
        file.OutputFilePath.Should().Be("build/custom/out.php");
        file.FileDeclares.Should().BeEmpty();
    }

    [Fact]
    public void Split_AnonymousNamespaceBlock_FlagsAnonymousNamespace()
    {
        var files = SplitContent("""
            <?tyhp
            namespace {
                class Local {}
            }
            """);

        files.Should().ContainSingle();
        files.Single().IsAnonymousNamespace.Should().BeTrue();
    }
}

[Trait("Category", "Emitter")]
public class TyhpEmitterPipelineTests
{
    [Fact]
    public void Emit_RunsPipelineWithoutThrowing()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Example { public function run(): void {} }
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var emitter = new TyhpEmitter(context);

        var outputFiles = emitter.Emit([srcFile]);

        outputFiles.Should().NotBeEmpty();
        outputFiles.Should().OnlyContain(f => f.SourceFileAst == srcFile);
        outputFiles.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.OutputFilePath));
        outputFiles.Should().OnlyContain(f => f.RootEmitItem != null);
        outputFiles.Should().OnlyContain(f => f.GeneratedContent != null);
    }

    [Fact]
    public void Emit_PhpPassThrough_ProducesExpectedClassOutput()
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

        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);

        var file = outputFiles.Should().ContainSingle().Subject;
        file.OutputFilePath.Should().Be("build/App/Models/User.php");
        var php = file.GeneratedContent ?? "";
        php.Should().StartWith("<?php");
        php.Should().Contain("declare(strict_types=1);");
        php.Should().Contain("namespace App\\Models;");
        php.Should().Contain("class User");
        php.Should().Contain("public function greet(): void");
        php.Should().Contain("echo 'hello';");
        php.Should().NotEndWith("?>");
        php.IndexOf("declare(strict_types=1);", StringComparison.Ordinal)
            .Should().BeLessThan(php.IndexOf("namespace App", StringComparison.Ordinal));
    }
}

[Trait("Category", "Emitter")]
public class AliasConverterTests
{
    [Fact]
    public void ConvertAliases_ReplacesTyhpMagicConstants()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $line = __TYHP_LINE__;
            $file = __TYHP_FILE__;
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        context.CurrentSourceFile = srcFile;
        var outputFiles = PHPOutputFile.FromAstTree(srcFile, context).ToList();
        var file = outputFiles.Should().ContainSingle().Subject;
        file.ConvertAliases(context);

        var hasIntLiteral = false;
        var hasStringLiteral = false;
        AstWalker.WalkStatements(file.Statements, node =>
        {
            if (node is PhpScalarAst scalar && scalar.ScalarType == Tyhp.TyhpLang.Enum.PhpScalarType.Integer)
            {
                hasIntLiteral = true;
            }

            if (node is PhpScalarAst str && str.ScalarType == Tyhp.TyhpLang.Enum.PhpScalarType.String
                && !string.IsNullOrEmpty(str.ValueString))
            {
                hasStringLiteral = true;
            }
        });

        hasIntLiteral.Should().BeTrue();
        hasStringLiteral.Should().BeTrue();
    }
}

[Trait("Category", "Emitter")]
public class PHPOutputFileGenerateTests
{
    [Fact]
    public void Generate_ProducesPhpOpenTagAndStrictTypes()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo {}
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);
        var file = outputFiles.Should().ContainSingle().Subject;
        var php = file.Generate(context);

        php.Should().StartWith("<?php\n");
        php.Should().Contain("declare(strict_types=1);");
        php.Should().Contain("namespace App;");
        php.Should().Contain("class Foo");
        php.Should().NotContain("?>");
        file.GeneratedContent.Should().Be(php);
    }

    [Fact]
    public void Generate_EntryPoint_IncludesAutoloaderWhenConfigured()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $value = 1;
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var config = new EmitConfig("build/", entryPointAutoloader: "vendor/autoload.php");
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]).ToList();
        var file = outputFiles.Should().ContainSingle().Subject;
        file.IsEntryPoint.Should().BeTrue();

        var php = file.GeneratedContent ?? "";

        php.Should().Contain("require_once __DIR__ . '/vendor/autoload.php';");
    }

    [Fact]
    public void Generate_NestedEntryPoint_UsesRelativeAutoloaderPath()
    {
        var provider = new TyhpSrcFileAst();
        var file = new PHPOutputFile
        {
            IsEntryPoint = true,
            OutputFilePath = "build/src/TestEmitter/test_struct.php",
            FileNameSpace = PhpNamespaceDeclAst.CreateFromContext("TestEmitter", provider),
            RootEmitItem = EmitItem.Empty(provider, Tyhp.TyhpLang.Enum.EmitType.FileHeader),
        };
        file.RootEmitItem.Children.Add(
            EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.RootStatement, "exit(main());", file.RootEmitItem));

        var config = new EmitConfig("build/", entryPointAutoloader: "vendor/autoload.php");
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = file.Generate(context);

        php.Should().Contain("namespace TestEmitter;");
        php.Should().Contain("require_once __DIR__ . '/../../vendor/autoload.php';");
        php.Should().NotContain("require_once __DIR__ . '/vendor/autoload.php';");
    }

    [Fact]
    public void ResolveAutoloaderPathFromEntryPoint_ComputesNestedRelativePath()
    {
        PHPOutputFile.ResolveAutoloaderPathFromEntryPoint(
                "build/src/TestEmitter/test.php",
                "build/",
                "vendor/autoload.php")
            .Should().Be("../../vendor/autoload.php");

        PHPOutputFile.ResolveAutoloaderPathFromEntryPoint(
                "build/test.php",
                "build/",
                "vendor/autoload.php")
            .Should().Be("vendor/autoload.php");

        PHPOutputFile.ResolveAutoloaderPathFromEntryPoint(
                "./build/web/index.php",
                "./build",
                "vendor/autoload.php")
            .Should().Be("../vendor/autoload.php");
    }

    [Theory]
    [InlineData("composer", null, "vendor/autoload.php")]
    [InlineData("composer", "custom/autoload.php", "custom/autoload.php")]
    [InlineData("none", null, null)]
    [InlineData("", null, null)]
    [InlineData("boot/loader.php", null, "boot/loader.php")]
    public void ResolveAutoloadDirectiveValue_ResolvesComposerSpecialCaseAndLiterals(
        string directive,
        string? configuredComposer,
        string? expected)
    {
        IReadOnlyDictionary<string, string>? map = configuredComposer is null
            ? null
            : new Dictionary<string, string> { ["composer"] = configuredComposer };

        EmitConfig.ResolveAutoloadDirectiveValue(directive, map).Should().Be(expected);
    }

    [Fact]
    public void ResolveAutoloadDirectiveValue_LooksUpNonComposerConfigKeys()
    {
        var map = new Dictionary<string, string> { ["alt"] = "other/autoload.php" };
        EmitConfig.ResolveAutoloadDirectiveValue("alt", map).Should().Be("other/autoload.php");
    }

    [Fact]
    public void ResolveAutoloadDirectiveValue_ComposerConfiguredNone_Disables()
    {
        var map = new Dictionary<string, string> { ["composer"] = "none" };
        EmitConfig.ResolveAutoloadDirectiveValue("composer", map).Should().BeNull();
    }

    [Fact]
    public void Emit_DeclareAutoloadNone_DisablesRequireOnEntryPoint()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            declare(autoload="none");
            namespace App;
            exit(0);
            """);
        parseResult.Diagnostics.HasErrors.Should().BeFalse();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;

        var config = new EmitConfig("build/", entryPointAutoloader: "vendor/autoload.php");
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = new TyhpEmitter(context).Emit([srcFile]).Single(f => f.IsEntryPoint).GeneratedContent ?? "";

        php.Should().Contain("namespace App;");
        php.Should().Contain("exit(0);");
        php.Should().NotContain("require_once");
        php.Should().NotContain("declare(autoload");
    }

    [Fact]
    public void Emit_DeclareAutoloadComposer_UsesDefaultWhenUnconfigured()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            declare(autoload="composer");
            namespace App;
            exit(0);
            """);
        parseResult.Diagnostics.HasErrors.Should().BeFalse();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;

        // Project default disabled, but per-file declare(autoload="composer") re-enables.
        var config = new EmitConfig("build/", entryPointAutoloader: null);
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = new TyhpEmitter(context).Emit([srcFile]).Single(f => f.IsEntryPoint).GeneratedContent ?? "";

        php.Should().Contain("require_once __DIR__ . '/vendor/autoload.php';");
        php.Should().NotContain("declare(autoload");
    }

    [Fact]
    public void Emit_DeclareAutoloadLiteralPath_UsesRelativeRequire()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            declare(autoload="boot/init.php");
            exit(1);
            """);
        parseResult.Diagnostics.HasErrors.Should().BeFalse();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;

        var config = new EmitConfig("build/", entryPointAutoloader: null);
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = new TyhpEmitter(context).Emit([srcFile]).Single(f => f.IsEntryPoint).GeneratedContent ?? "";

        php.Should().Contain("require_once __DIR__ . '/boot/init.php';");
    }

    [Fact]
    public void Emit_DeclareAutoloadComposer_UsesConfiguredComposerPath()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            declare(autoload="composer");
            exit(0);
            """);
        parseResult.Diagnostics.HasErrors.Should().BeFalse();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;

        var map = new Dictionary<string, string> { ["composer"] = "lib/autoload.php" };
        var config = new EmitConfig(
            "build/",
            entryPointAutoloader: null,
            entryPointAutoloaderMap: map);
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = new TyhpEmitter(context).Emit([srcFile]).Single(f => f.IsEntryPoint).GeneratedContent ?? "";

        php.Should().Contain("require_once __DIR__ . '/lib/autoload.php';");
    }

    [Fact]
    public void Generate_StatementNamespacedEntryPoint_EmitsAutoloaderAfterNamespace()
    {
        var provider = new TyhpSrcFileAst();
        var file = new PHPOutputFile
        {
            IsEntryPoint = true,
            FileNameSpace = PhpNamespaceDeclAst.CreateFromContext("App", provider),
            RootEmitItem = EmitItem.Empty(provider, Tyhp.TyhpLang.Enum.EmitType.FileHeader),
        };
        file.RootEmitItem.Children.Add(
            EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.RootStatement, "$value = 1;", file.RootEmitItem));

        var config = new EmitConfig("build/", entryPointAutoloader: "vendor/autoload.php");
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = file.Generate(context);

        const string requireLine = "require_once __DIR__ . '/vendor/autoload.php';";
        var namespaceIndex = php.IndexOf("namespace App;", StringComparison.Ordinal);
        var requireIndex = php.IndexOf(requireLine, StringComparison.Ordinal);

        namespaceIndex.Should().BeGreaterThan(-1);
        requireIndex.Should().BeGreaterThan(namespaceIndex, "the autoloader require_once must appear after the namespace statement");

        AssertPhpLintClean(php);
    }

    [Fact]
    public void Generate_BlockNamespacedEntryPoint_EmitsAutoloaderInsideBlock()
    {
        var provider = new TyhpSrcFileAst();
        var file = new PHPOutputFile
        {
            IsEntryPoint = true,
            FileNameSpace = PhpBlockNamespaceDeclAst.CreateFromContext("App", provider),
            RootEmitItem = EmitItem.Empty(provider, Tyhp.TyhpLang.Enum.EmitType.FileHeader),
        };
        file.RootEmitItem.Children.Add(
            EmitItem.Line(provider, Tyhp.TyhpLang.Enum.EmitType.RootStatement, "$value = 1;", file.RootEmitItem));

        var config = new EmitConfig("build/", entryPointAutoloader: "vendor/autoload.php");
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
        var php = file.Generate(context);

        const string requireLine = "require_once __DIR__ . '/vendor/autoload.php';";
        var namespaceIndex = php.IndexOf("namespace App {", StringComparison.Ordinal);
        var requireIndex = php.IndexOf(requireLine, StringComparison.Ordinal);

        namespaceIndex.Should().BeGreaterThan(-1);
        requireIndex.Should().BeGreaterThan(namespaceIndex, "the autoloader require_once must appear inside the namespace block");
        php.Should().Contain("    require_once __DIR__ . '/vendor/autoload.php';", "the require_once is indented as the first statement inside the block");

        AssertPhpLintClean(php);
    }

    private static void AssertPhpLintClean(string php)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"tyhp_entrypoint_{Guid.NewGuid():N}.php");
        try
        {
            File.WriteAllText(tempFile, php);
            var result = PhpToolchain.RunPhpLint(tempFile);
            if (result.ExitCode == -1
                && result.StandardError.Contains("was not found on PATH", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result.ExitCode.Should().Be(0, $"generated PHP must be syntactically valid:\n{result.CombinedOutput}\n---\n{php}");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Merge_DetectsNamespaceMismatch()
    {
        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), new EmitConfig());
        var provider = new TyhpSrcFileAst();
        var left = new PHPOutputFile
        {
            FileNameSpace = PhpNamespaceDeclAst.CreateFromContext(@"App\Left", provider),
        };
        var right = new PHPOutputFile
        {
            FileNameSpace = PhpNamespaceDeclAst.CreateFromContext(@"App\Right", provider),
        };

        left.Merge(right, context);

        context.Diagnostics.HasErrors.Should().BeTrue();
        context.Diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.EmitterNamespaceMismatch);
    }

    [Fact]
    public void PruneFileImports_RemovesUnusedImports()
    {
        var import = PhpImportDeclAst.CreateFromContext(@"App\Unused", null, null, new TyhpSrcFileAst());
        var importList = new PhpImportDeclListAst();
        importList.AddChild(import);

        var file = new PHPOutputFile
        {
            FileImports = [importList],
            Statements = [],
            RootEmitItem = EmitItem.Empty(new TyhpSrcFileAst(), Tyhp.TyhpLang.Enum.EmitType.FileHeader),
        };
        file.RootEmitItem.Children.Add(
            EmitItem.Line(new TyhpSrcFileAst(), Tyhp.TyhpLang.Enum.EmitType.RootStatement, "echo 'hi';", file.RootEmitItem));

        var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), new EmitConfig());
        file.PruneFileImports(context);

        file.FileImports.SelectMany(i => i.GetAllNotNull()).Should().BeEmpty();
    }
}

[Trait("Category", "Emitter")]
public class TyhpEmitterGlobalClassTests
{
    [Fact]
    public void Emit_GlobalClassWithoutNamespace_ProducesOutputFile()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp

            class TestParse {
                public int $value = 0;
            }
            """);

        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);

        outputFiles.Should().NotBeEmpty();
        outputFiles.Should().ContainSingle().Which.GeneratedContent.Should().Contain("class TestParse");
    }
}

[Trait("Category", "Emitter")]
public class OutputFileWriterTests
{
    [Fact]
    public void WriteAll_WritesGeneratedContentWithUtf8NoBom()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var config = new EmitConfig(tempDir + "/out/");
            var context = new EmitContext(new GlobalScope(), new DiagnosticBag(), config);
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = tempDir + "/out/App/Example.php",
                GeneratedContent = "<?php\necho 'hello';\n",
                IsEntryPoint = true,
                SourceFileAst = TyhpSrcFileAst.Create("test.tyhp", "hash"),
            };

            var result = new OutputFileWriter(context).WriteAll([outputFile]);

            result.FilesWritten.Should().Be(1);
            result.DirectoriesCreated.Should().Be(1);
            var writtenPath = result.WrittenPaths.Single();
            File.Exists(writtenPath).Should().BeTrue();

            var bytes = File.ReadAllBytes(writtenPath);
            if (bytes.Length >= 3)
            {
                (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse();
            }
            File.ReadAllText(writtenPath).Should().Be("<?php\necho 'hello';\n");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteAll_SkipsEmptyOutputFileWithWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(outputPath);

        try
        {
            var config = new EmitConfig(outputPath + "/");
            var diagnostics = new DiagnosticBag();
            var context = new EmitContext(new GlobalScope(), diagnostics, config);
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "build/_functions.php",
                GeneratedContent = "<?php\n",
                Statements = [],
                SourceFileAst = TyhpSrcFileAst.Create("empty.tyhp", "hash"),
            };

            var result = new OutputFileWriter(context).WriteAll([outputFile]);

            result.FilesWritten.Should().Be(0);
            result.FilesSkipped.Should().Be(1);
            diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.EmitterEmptyOutputFile);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WriteAll_DetectsPsr4PathConflict()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(outputPath);

        try
        {
            var config = new EmitConfig(outputPath + "/");
            var diagnostics = new DiagnosticBag();
            var context = new EmitContext(new GlobalScope(), diagnostics, config);
            var first = new PHPOutputFile
            {
                OutputFilePath = "build/App/User.php",
                GeneratedContent = "<?php\nclass User {}\n",
                IsPSR4ObjectDeclaration = true,
                Statements = [new PhpNopStatementAst()],
                SourceFileAst = TyhpSrcFileAst.Create("a.tyhp", "hash"),
            };
            var second = new PHPOutputFile
            {
                OutputFilePath = "build/App/User.php",
                GeneratedContent = "<?php\nclass User2 {}\n",
                IsPSR4ObjectDeclaration = true,
                Statements = [new PhpNopStatementAst()],
                SourceFileAst = TyhpSrcFileAst.Create("b.tyhp", "hash"),
            };

            var result = new OutputFileWriter(context).WriteAll([first, second]);

            result.FilesWritten.Should().Be(1);
            result.Conflicts.Should().ContainSingle();
            diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.EmitterOutputPathConflict);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
