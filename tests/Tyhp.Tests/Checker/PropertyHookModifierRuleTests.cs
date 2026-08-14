using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers property-hook modifier rules (invalid modifiers → TYHP4154), final-hook override
/// checking (FOUND_BUGS property-hook Medium #10 → TYHP4166), and by-ref <c>&amp;get</c>
/// rejection below PHP 8.4 (FOUND High #5 → TYHP4167).
/// </summary>
[Trait("Category", "Checker")]
public class PropertyHookModifierRuleTests
{
    [Theory]
    [InlineData("public")]
    [InlineData("protected")]
    [InlineData("private")]
    [InlineData("static")]
    [InlineData("readonly")]
    [InlineData("abstract")]
    public void Check_IllegalHookModifier_Reports4154(string modifier)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            final class Widget {
                private string $_name = 'x';
                public string $name {
                    {{modifier}} get {
                        return $this->_name;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier);
        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier
            && d.Message.Contains(modifier, StringComparison.Ordinal));
    }

    [Fact]
    public void Check_FinalHookModifier_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = 'x';
                public string $name {
                    final get {
                        return $this->_name;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier);
    }

    [Theory]
    [InlineData("8.0")]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Check_ByRefGetHook_BelowPhp84_Reports4167(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private array $_items = [];
                public array $items {
                    &get {
                        return $this->_items;
                    }
                }
            }
            """, phpVersion);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84
            && d.Message.Contains(phpVersion, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Check_ByRefGetHook_Php84OrLater_IsAccepted(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private array $_items = [];
                public array $items {
                    &get {
                        return $this->_items;
                    }
                }
            }
            """, phpVersion);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84);
    }

    [Fact]
    public void Check_ByRefGetArrowHook_BelowPhp84_Reports4167()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public bool $flag = false {
                    &get => $this->flag;
                }
            }
            """, "8.2");

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84);
    }

    [Fact]
    public void Check_ByRefGetOnPromotedParam_BelowPhp84_Reports4167()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function __construct(
                    public array $items {
                        &get {
                            return $this->items;
                        }
                    },
                ) {}
            }
            """, "8.3");

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84);
    }

    [Fact]
    public void Check_ByValueGetHook_BelowPhp84_DoesNotReport4167()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private array $_items = [];
                public array $items {
                    get {
                        return $this->_items;
                    }
                }
            }
            """, "8.2");

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerByRefPropertyGetHookRequiresPhp84);
    }

    [Fact]
    public void Check_OverrideOfFinalGetHook_Reports4166()
    {
        var diagnostics = CompileAndCheck("""
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
            class Child extends Base {
                public string $name {
                    get {
                        return 'child';
                    }
                    set {
                        // keep set
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_OverrideOfFinalSetHook_Reports4166()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_name = '';
                public string $name {
                    get {
                        return $this->_name;
                    }
                    final set {
                        $this->_name = $value;
                    }
                }
            }
            class Child extends Base {
                public string $name {
                    get {
                        return $this->name;
                    }
                    set {
                        // illegal override of final set
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::set()", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_FinalGetAllowsSetOnlyChild_DoesNotReport4166()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_name = '';
                public string $name {
                    final get {
                        return $this->_name;
                    }
                    set {
                        $this->_name = $value;
                    }
                }
            }
            class Child extends Base {
                public string $name {
                    set {
                        // only set — final get stays inherited
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden);
    }

    [Fact]
    public void Check_OverrideOfFinalGetHook_ThroughGrandparent_Reports4166()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_name = 'x';
                public string $name {
                    final get {
                        return $this->_name;
                    }
                }
            }
            class Middle extends Base {}
            class Child extends Middle {
                public string $name {
                    get {
                        return 'child';
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_PartialOverride_FinalGetStillVisibleThroughMiddle_Reports4166()
    {
        // Middle redeclares only set; Base's final get remains the inherited get.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_name = '';
                public string $name {
                    final get {
                        return $this->_name;
                    }
                    set {
                        $this->_name = $value;
                    }
                }
            }
            class Middle extends Base {
                public string $name {
                    set {
                        // partial override — get stays Base's final get
                    }
                }
            }
            class Child extends Middle {
                public string $name {
                    get {
                        return 'child';
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_PrivateFinalHookInParent_DoesNotReport4166()
    {
        // A `private` hooked property is not inherited (same as private methods) — a child
        // declaring a same-named public hooked property is an unrelated, independent property,
        // not an override, so it must not trigger the final-hook diagnostic.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_backing = 'x';
                private string $name {
                    final get {
                        return $this->_backing;
                    }
                }
            }
            class Child extends Base {
                public string $name {
                    get {
                        return 'child';
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden);
    }

    [Fact]
    public void Check_PrivateMiddlePropertySkipped_FinalHookThroughGrandparent_Reports4166()
    {
        // Middle's `private $name` is an unrelated slot (same name, no relation to Base's public
        // hooked property) — the walk must skip over it and keep looking further up so Child still
        // sees Base's final `get` as the nearest visible declaration it is overriding.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_name = 'x';
                public string $name {
                    final get {
                        return $this->_name;
                    }
                }
            }
            class Middle extends Base {
                private string $name = 'middle-only';
            }
            class Child extends Middle {
                public string $name {
                    get {
                        return 'child';
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_NonFinalHookOverride_DoesNotReport4166()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private string $_name = 'x';
                public string $name {
                    get {
                        return $this->_name;
                    }
                }
            }
            class Child extends Base {
                public string $name {
                    get {
                        return 'child';
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden);
    }

    [Fact]
    public void Check_OverrideOfFinalHook_OnPromotedParameter_Reports4166()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function __construct(
                    public string $name {
                        final get {
                            return $this->name;
                        }
                    }
                ) {}
            }
            class Child extends Base {
                public function __construct(
                    public string $name {
                        get {
                            return 'child';
                        }
                    }
                ) {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerFinalPropertyHookOverridden
            && d.Message.Contains("Base::$name::get()", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_NoHookModifier_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = 'x';
                public string $name {
                    get {
                        return $this->_name;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier);
    }

    [Fact]
    public void Check_IllegalModifierOnSetHook_Reports4154()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = '';
                public string $name {
                    get {
                        return $this->_name;
                    }
                    public set(string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier
            && d.Message.Contains("public", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_IllegalModifierOnPromotedPropertyHook_Reports4154()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function __construct(
                    public string $name {
                        public get {
                            return $this->name;
                        }
                    }
                ) {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerPropertyHookInvalidModifier);
    }

    [Fact]
    public void Check_ReadonlyHookedProperty_Reports4155()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = 'x';
                public readonly string $name {
                    get {
                        return $this->_name;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerHookedPropertyReadonly);
    }

    [Fact]
    public void Check_ReadonlyPromotedParameterWithHook_Reports4155()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public function __construct(
                    public readonly string $name {
                        get {
                            return $this->name;
                        }
                    }
                ) {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerHookedPropertyReadonly);
    }

    [Fact]
    public void Check_NonReadonlyHookedProperty_DoesNotReport4155()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = 'x';
                public string $name {
                    get {
                        return $this->_name;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerHookedPropertyReadonly);
    }

    [Fact]
    public void Check_GetHookBody_TypeMismatch_Reports4009()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public string $name {
                    get {
                        return 42;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_SetHookBody_UsesImplicitValue()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = '';
                public string $name {
                    set {
                        $this->_name = $value;
                    }
                    get {
                        return $this->_name;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_SetHookBody_WrongValueUse_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public string $name {
                    set {
                        int $n = $value;
                    }
                    get {
                        return '';
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SetArrowBody_StrtolowerValue_No4009()
    {
        // PHP 8.4 `set => expr`: expr is the written value (property type), not a void return.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public string $name = '' {
                    set => \strtolower($value);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_SetArrowBody_WrongType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                public string $name = '' {
                    set => 42;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_GetHookReadsSiblingProperty_ResolvesThis()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private bool $modified = false;
                public string $name = '' {
                    get {
                        if ($this->modified) {
                            return 'x';
                        }
                        return $this->name;
                    }
                    set {
                        $this->modified = true;
                        $this->name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerConditionNotBool);
        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_ParentPropertyHookGet_TypesAsPropertyType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public string $label = 'x';
            }

            class Child extends Base {
                public string $label {
                    get => \strtoupper(parent::$label::get());
                    set => \strtolower($value);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_GetBlockHook_MissingReturnOnAllPaths_ReportsMissingReturn()
    {
        // A block `get { … }` hook fatals at runtime ("must return a value") if a code path
        // falls through without returning. The checker must catch this like a normal method body.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = '';
                public string $name {
                    get {
                        if ($this->_name === 'x') {
                            return 'y';
                        }
                    }
                    set {
                        $this->_name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_SetBlockHook_NoReturn_DoesNotReportMissingReturn()
    {
        // Block `set { … }` is implicitly void — no return statement is required.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Widget {
                private string $_name = '';
                public string $name {
                    get {
                        return $this->_name;
                    }
                    set {
                        $this->_name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    private static DiagnosticBag CompileAndCheck(string content)
        => CompileAndCheck(content, phpVersion: "8.4");

    private static DiagnosticBag CompileAndCheck(string content, string phpVersion)
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
                PhpVersion = phpVersion,
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                Checker = new CheckerOptions
                {
                    PhpVersion = phpVersion,
                },
            };
            var result = compilationService.ParseFiles([filePath], options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
