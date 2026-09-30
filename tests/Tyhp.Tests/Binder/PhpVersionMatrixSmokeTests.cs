using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Story 20.5 Phase 8 matrix smoke: the same gated sources compiled at PHP 8.2–8.5
/// expose different symbols, do not treat sibling alternates as unreachable (4302),
/// and never emit <c>declare(php=)</c> or <c>\Tyhp\Php</c>. Uses a synthetic
/// <c>Php</c>/<c>NoEmit</c> stub — not live runtime packages.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Emitter")]
[Trait("Category", "Story20.5")]
public class PhpVersionMatrixSmokeTests
{
    private const string MatrixSource = """
        <?tyhp
        function always_present(): void {}

        #[\Tyhp\Php(">=8.3")]
        function from_83(): void {}

        #[\Tyhp\Php(">=8.4")]
        function from_84(): void {}

        #[\Tyhp\Php(">=8.5")]
        function from_85(): void {}

        declare(php=">=8.4") {
            function block_84(): void {}
        }

        #[\Tyhp\Php(">=8.2 <8.4")]
        function example(string $v): mixed { return $v; }

        #[\Tyhp\Php(">=8.4")]
        function example(string $v, bool $strict = false): string { return $v; }

        class Widget {
            public function always(): void {}
            #[\Tyhp\Php(">=8.5")]
            public function only_85(): void {}
        }
        """;

    public static IEnumerable<object[]> MatrixCases()
    {
        yield return
        [
            "8.2",
            new[] { "always_present" },
            new[] { "from_83", "from_84", "from_85", "block_84" },
            1,
            false,
        ];
        yield return
        [
            "8.3",
            new[] { "always_present", "from_83" },
            new[] { "from_84", "from_85", "block_84" },
            1,
            false,
        ];
        yield return
        [
            "8.4",
            new[] { "always_present", "from_83", "from_84", "block_84" },
            new[] { "from_85" },
            2,
            false,
        ];
        yield return
        [
            "8.5",
            new[] { "always_present", "from_83", "from_84", "from_85", "block_84" },
            Array.Empty<string>(),
            2,
            true,
        ];
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void Matrix_BindCheckEmit_DiffersByTarget(
        string phpVersion,
        string[] presentFunctions,
        string[] absentFunctions,
        int exampleArity,
        bool widgetHasOnly85)
    {
        var compiled = Compile(MatrixSource, phpVersion);

        compiled.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionUnreachable);
        compiled.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpVersionDuplicateDeclaration);

        foreach (var name in presentFunctions)
        {
            FindFunction(compiled.Global, name).Should().NotBeNull($"'{name}' should bind at PHP {phpVersion}");
        }

        foreach (var name in absentFunctions)
        {
            FindFunction(compiled.Global, name).Should().BeNull($"'{name}' should be omitted at PHP {phpVersion}");
        }

        var example = FindFunction(compiled.Global, "example");
        example.Should().NotBeNull();
        example!.Parameters.Should().HaveCount(exampleArity);

        var widget = FindObject(compiled.Global, "Widget");
        widget.Should().NotBeNull();
        FindMethod(widget!, "always").Should().NotBeNull();
        if (widgetHasOnly85)
        {
            FindMethod(widget!, "only_85").Should().NotBeNull();
        }
        else
        {
            FindMethod(widget!, "only_85").Should().BeNull();
        }

        compiled.Php.Should().NotContain("declare(php=");
        compiled.Php.Should().NotContain("Tyhp\\Php");
        compiled.Php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\Php");

        foreach (var name in presentFunctions)
        {
            compiled.Php.Should().Contain($"function {name}(");
        }

        foreach (var name in absentFunctions)
        {
            compiled.Php.Should().NotContain(name);
        }

        compiled.Php.Should().Contain("function always()");
        if (widgetHasOnly85)
        {
            compiled.Php.Should().Contain("function only_85()");
        }
        else
        {
            compiled.Php.Should().NotContain("only_85");
        }
    }

    private static CompiledMatrix Compile(string content, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "matrix.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = phpVersion,
                })
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: phpVersion,
                skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull();

            var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
            bindErrors.Should().BeEmpty(
                $"parse/bind errors at {phpVersion}: {string.Join(", ", bindErrors.Select(e => e.Message))}");

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics,
                symbolTree,
                result.GlobalScope!,
                options.Checker);
            checker.Check(result.ParsedFiles!);

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            var files = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            var php = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));

            return new CompiledMatrix(result.GlobalScope!, result.Diagnostics, php);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed record CompiledMatrix(GlobalScope Global, DiagnosticBag Diagnostics, string Php);

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol func
                && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = func;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static ObjectMethodSymbol? FindMethod(ObjectDeclarationSymbol obj, string name)
    {
        if (obj.Members.TryGetValue(name, out var member) && member is ObjectMethodSymbol method)
        {
            return method;
        }

        if (obj.ContainingScope is not IBaseScope scope)
        {
            return null;
        }

        foreach (var childScope in scope.GetAllChildScopes())
        {
            if (childScope.DeclarationSymbol is ObjectDeclarationSymbol same
                && ReferenceEquals(same, obj))
            {
                foreach (var symbol in childScope.GetAllChildSymbols())
                {
                    if (symbol is ObjectMethodSymbol found
                        && string.Equals(found.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    private static void Walk(IBaseScope scope, Action<Tyhp.TyhpLang.Binder.Symbols.Interfaces.IBaseSymbol> visit)
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
