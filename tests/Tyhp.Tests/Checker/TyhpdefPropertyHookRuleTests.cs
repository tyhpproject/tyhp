using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 20.7 Phase 3: property-hook checker rules run on bodyless tyhpdef properties
/// (same AST as Tyhp). <c>&amp;get</c> does not raise TYHP4167 on tyhpdef declarations.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story20.7")]
public class TyhpdefPropertyHookRuleTests
{
    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_DuplicateGetHook_Reports3002(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public string $name { get; get; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.BinderDuplicateSymbolDeclaration
            && d.Message.Contains("$name::get", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_DuplicateGetHook_LabelsFirstHook(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public string $name { get; get; }
            }
            """);

        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
            .Subject;
        error.Labels.Should().ContainSingle();
        error.Labels[0].Message.Should().Be(Message.Localize("CLI_DiagnosticLabelDeclaredHere"));
        error.Labels[0].Span.FileName.Should().Be(error.FileName);
        error.Labels[0].Span.Column.Should().BeLessThan(error.Column);
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_InvalidHookName_Reports4006(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public string $name { lazy; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidPropertyAccessorType
            && d.Message.Contains("lazy", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_ByRefSetHook_Reports4006(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public string $name { &set; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerInvalidPropertyAccessorType
            && d.Message.Contains("&set", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TyhpdefByRefGetHook_AtPhp82_DoesNotReport4167()
    {
        var diagnostics = CompileAndCheck("test.tyhpdef", """
            <?tyhpdef
            class Holder {
                public array $items { &get; }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84
            && (d.FileName ?? "").EndsWith("test.tyhpdef", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TyhpByRefGetHook_AtPhp82_StillReports4167()
    {
        var diagnostics = CompileAndCheck("test.tyhp", """
            <?tyhp
            class Holder {
                public array $items { &get; }
            }
            """, phpVersion: "8.2");

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84);
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_IllegalStaticHookModifier_Reports4154(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public string $name { static get; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier
            && d.Message.Contains("static", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_PrivateSetHook_DoesNotReport4154(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public string $name { get; private set; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_HookMoreVisibleThanProperty_Reports4004(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                private string $name { public get; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerAccessorVisibilityCannotBeMoreVisibleThanProperty
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_ReadonlyHookedProperty_Reports4155(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Holder {
                public readonly string $name { get; set; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerHookedPropertyReadonly
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("test.tyhp", "<?tyhp")]
    [InlineData("test.tyhpdef", "<?tyhpdef")]
    public void Check_OverrideOfFinalGetHook_Reports4166(string fileName, string tag)
    {
        var diagnostics = CompileAndCheck(fileName, $$"""
            {{tag}}
            class Base {
                public string $name { final get; set; }
            }
            class Child extends Base {
                public string $name { get; set; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith(fileName, StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TyhpGetHookWithParameter_Reports4007()
    {
        var diagnostics = CompileAndCheck("test.tyhp", """
            <?tyhp
            class Holder {
                public string $name {
                    get(string $x) {
                        return $x;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerParameterNotAllowedOnPropertyAccessorType
            && d.Message.Contains("get", StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_TyhpdefGatedHookedProperty_OmitsSymbolAtPhp82()
    {
        const string source = """
            <?tyhpdef
            class Holder {
                #[\Tyhp\Php(">=8.4")]
                public string $hooked { get; set; }
                public string $always;
            }
            """;

        var (global82, diagnostics82) = CompileAndCheckWithScope(
            "test.tyhpdef", source, phpVersion: "8.2");
        diagnostics82.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget
            && (d.FileName ?? "").EndsWith("test.tyhpdef", StringComparison.Ordinal));

        var holder82 = FindObject(global82, "Holder");
        holder82.Should().NotBeNull();
        FindPropertyOrNull(holder82!, "always").Should().NotBeNull(
            "ungated tyhpdef property must remain visible at PHP 8.2");
        FindPropertyOrNull(holder82!, "hooked").Should().BeNull(
            "#[\\Tyhp\\Php(\">=8.4\")] on a hooked tyhpdef property must omit the symbol at 8.2");

        var (global84, _) = CompileAndCheckWithScope(
            "test.tyhpdef", source, phpVersion: "8.4");
        var holder84 = FindObject(global84, "Holder");
        holder84.Should().NotBeNull();
        FindPropertyOrNull(holder84!, "hooked").Should().NotBeNull(
            "#[\\Tyhp\\Php(\">=8.4\")] hooked tyhpdef property must bind at PHP 8.4");
        FindPropertyOrNull(holder84!, "always").Should().NotBeNull();
    }

    [Fact]
    public void Check_HookLevelTyhpPhpAttribute_Reports8016_AndDoesNotOmitProperty()
    {
        // Version gates belong on the property or an enclosing declare(php=…), not on
        // individual get/set. Hook-level #[\Tyhp\Php] is TYHP8016 (error, not a silent drop).
        const string source = """
            <?tyhpdef
            class Holder {
                public string $name {
                    #[\Tyhp\Php(">=8.4")]
                    get;
                    set;
                }
            }
            """;

        var (global82, diagnostics82) = CompileAndCheckWithScope(
            "test.tyhpdef", source, phpVersion: "8.2");
        diagnostics82.Errors.Should().Contain(d =>
            d.Code == MessageCode.TyhpdefPhpVersionGateOnPropertyHook
            && (d.FileName ?? "").EndsWith("test.tyhpdef", StringComparison.Ordinal));
        diagnostics82.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPhpVersionAttributeInvalidTarget);

        var holder82 = FindObject(global82, "Holder");
        holder82.Should().NotBeNull();
        FindPropertyOrNull(holder82!, "name").Should().NotBeNull(
            "hook-level #[\\Tyhp\\Php] must not gate/omit the property; only 8016");

        var (_, diagnostics84) = CompileAndCheckWithScope(
            "test.tyhpdef", source, phpVersion: "8.4");
        diagnostics84.Errors.Should().Contain(d =>
            d.Code == MessageCode.TyhpdefPhpVersionGateOnPropertyHook);
    }

    [Fact]
    public void Check_TyhpSourceHookLevelTyhpPhp_DoesNotReport8016()
    {
        var diagnostics = CompileAndCheck("test.tyhp", """
            <?tyhp
            class Holder {
                private string $_name = 'x';
                public string $name {
                    #[\Tyhp\Php(">=8.4")]
                    get {
                        return $this->_name;
                    }
                    set {
                        $this->_name = $value;
                    }
                }
            }
            """, phpVersion: "8.4");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.TyhpdefPhpVersionGateOnPropertyHook);
    }

    [Fact]
    public void Check_NonVersionHookAttribute_DoesNotReport8016()
    {
        var diagnostics = CompileAndCheck("test.tyhpdef", """
            <?tyhpdef
            class Holder {
                public string $name {
                    #[SomeAttr]
                    get;
                    set;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.TyhpdefPhpVersionGateOnPropertyHook);
    }

    [Fact]
    public void Check_TyhpChildOverridesFinalGetFromTyhpdefBase_Reports4166()
    {
        // A consumer project extends a package base class that a `.tyhpdef` describes with a
        // `final get;` hook. The override check must walk across the tyhpdef/tyhp boundary the
        // same as a same-language override (TryResolveEnclosingObject / TryGetParentDeclaration
        // do not care which file declared which ancestor).
        var diagnostics = CompileAndCheckMultiFile(
            ("Base.tyhpdef", """
                <?tyhpdef
                class Base {
                    public string $name { final get; set; }
                }
                """),
            ("Child.tyhp", """
                <?tyhp
                class Child extends Base {
                    public string $name {
                        get {
                            return 'child';
                        }
                        set {
                        }
                    }
                }
                """));

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith("Child.tyhp", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TyhpdefChildOverridesFinalGetFromTyhpBase_Reports4166()
    {
        // Same boundary check in reverse: a `.tyhp` base class declares `final get`, and a
        // `.tyhpdef` describes a derived type that redeclares `get`.
        var diagnostics = CompileAndCheckMultiFile(
            ("Base.tyhp", """
                <?tyhp
                class Base {
                    private string $_name = 'x';
                    public string $name {
                        final get {
                            return $this->_name;
                        }
                        set {
                            $this->_name = $value;
                        }
                    }
                }
                """),
            ("Child.tyhpdef", """
                <?tyhpdef
                class Child extends Base {
                    public string $name { get; set; }
                }
                """));

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal)
            && (d.FileName ?? "").EndsWith("Child.tyhpdef", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_TyhpByRefGetHook_InProjectThatIncludesTyhpdefBase_StillReports4167()
    {
        // The tyhpdef skip must key off the hook declaration's own language mode, not merely
        // "this compilation touches a tyhpdef file". A `.tyhp` class extending a `.tyhpdef`-
        // described base still emits toward `output.phpVersion`, so its own `&get` is checked.
        var diagnostics = CompileAndCheckMultiFile(
            phpVersion: "8.2",
            ("Base.tyhpdef", """
                <?tyhpdef
                class Base {
                    public string $unrelated;
                }
                """),
            ("Child.tyhp", """
                <?tyhp
                class Child extends Base {
                    private array $_items = [];
                    public array $items {
                        &get {
                            return $this->_items;
                        }
                    }
                }
                """));

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84
            && (d.FileName ?? "").EndsWith("Child.tyhp", StringComparison.Ordinal));
    }

    private static DiagnosticBag CompileAndCheck(string fileName, string content, string phpVersion = "8.4")
        => CompileAndCheckWithScope(fileName, content, phpVersion).Diagnostics;

    private static (GlobalScope Global, DiagnosticBag Diagnostics) CompileAndCheckWithScope(
        string fileName,
        string content,
        string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: phpVersion,
                configure: o =>
                {
                    o.Checker = new CheckerOptions
                    {
                        PhpVersion = phpVersion,
                    };
                });
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            return (result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static DiagnosticBag CompileAndCheckMultiFile(
        params (string FileName, string Content)[] files)
        => CompileAndCheckMultiFile(phpVersion: "8.4", files);

    private static DiagnosticBag CompileAndCheckMultiFile(
        string phpVersion,
        params (string FileName, string Content)[] files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePaths = new List<string>();
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, content);
            filePaths.Add(filePath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: phpVersion,
                configure: o =>
                {
                    o.Checker = new CheckerOptions
                    {
                        PhpVersion = phpVersion,
                    };
                });
            var result = compilationService.ParseFiles(filePaths, options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null)
            {
                return;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol obj
                    && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = obj;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        return found;
    }

    private static ObjectPropertySymbol? FindPropertyOrNull(ObjectDeclarationSymbol type, string name)
    {
        foreach (var member in type.EnumerateMembersAndConstants())
        {
            if (member is ObjectPropertySymbol property
                && string.Equals(
                    property.Name.TrimStart('$'),
                    name.TrimStart('$'),
                    StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }
}
