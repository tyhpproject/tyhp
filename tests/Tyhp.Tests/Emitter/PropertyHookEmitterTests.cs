using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Async emit audit §1: hooked properties must emit valid PHP 8.4+ hook syntax
/// (no trailing <c>;</c> after the hook block; no empty <c>()</c> on parameter-less hooks).
/// </summary>
[Trait("Category", "Emitter")]
public class PropertyHookEmitterTests
{
    [Fact]
    public void Emit_GetOnlyHookedProperty_OmitsEmptyParamsAndTrailingSemicolon()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private string $_name = 'x';

                public string $name {
                    get {
                        return $this->_name;
                    }
                }
            }
            """);

        php.Should().Contain("public string $name {");
        php.Should().Contain("get {");
        php.Should().NotContain("get()");
        php.Should().NotMatchRegex(new Regex(@"\}\s*;", RegexOptions.Singleline));
        php.Should().MatchRegex(
            new Regex(@"public string \$name \{\s+get \{", RegexOptions.Singleline));
        php.Should().Contain("return $this->_name;");
    }

    [Fact]
    public void Emit_SetHookWithTypedParameter_KeepsParameterList()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private string $_name = '';

                public string $name {
                    get {
                        return $this->_name;
                    }
                    set(string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """);

        php.Should().Contain("get {");
        php.Should().NotContain("get()");
        php.Should().Contain("set(string $value)");
        php.Should().NotMatchRegex(new Regex(@"\}\s*;", RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_SetHookWithoutParameters_OmitsEmptyParameterList()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private string $_name = '';

                public string $name {
                    set {
                        $this->_name = $value;
                    }
                }
            }
            """);

        php.Should().Contain("set {");
        php.Should().NotContain("set()");
        php.Should().NotMatchRegex(new Regex(@"\}\s*;", RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_ByRefGetHook_PrefixesAmpersandBeforeGet()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private array $_items = [];

                public array $items {
                    &get {
                        return $this->_items;
                    }
                }
            }
            """);

        php.Should().Contain("&get {");
        php.Should().NotContain("get()");
        php.Should().NotContain(")&");
        php.Should().NotMatchRegex(new Regex(@"\}\s*;", RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_FinalGetHook_EmitsFinalModifier()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private string $_name = 'x';

                public string $name {
                    final get {
                        return $this->_name;
                    }
                }
            }
            """);

        php.Should().Contain("public string $name {");
        php.Should().Contain("final get {");
        php.Should().NotContain("get()");
        php.Should().NotMatchRegex(new Regex(@"\}\s*;", RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_PlainProperty_StillEndsWithSemicolon()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name = 'x';
            }
            """);

        php.Should().Contain("public string $name = 'x';");
    }

    [Fact]
    public void Emit_PromotedParameter_GetHook_SurvivesInConstructorSignature()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public function __construct(
                    public string $name {
                        get {
                            return \strtoupper($this->name);
                        }
                    }
                ): void {}
            }
            """);

        php.Should().Contain("public string $name {");
        php.Should().Contain("get {");
        php.Should().Contain("\\strtoupper($this->name)");
        php.Should().NotContain("get()");
        // Must not collapse to a bare promoted param with the hook body discarded.
        php.Should().NotMatchRegex(new Regex(@"__construct\(\s*public string \$name\s*\)"));
    }

    [Fact]
    public void Emit_PromotedParameter_GetAndSetHooks_SurviveInConstructorSignature()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public function __construct(
                    public string $name {
                        get {
                            return $this->name;
                        }
                        set(string $value) {
                            $this->name = \strtolower($value);
                        }
                    }
                ): void {}
            }
            """);

        php.Should().Contain("public string $name {");
        php.Should().Contain("get {");
        php.Should().Contain("set(string $value)");
        php.Should().Contain("\\strtolower($value)");
        php.Should().NotContain("get()");
        php.Should().NotContain("set()");
    }

    [Fact]
    public void Emit_PromotedParameter_DefaultBeforeHookBlock()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public function __construct(
                    public string $name = 'x' {
                        get {
                            return $this->name;
                        }
                    }
                ): void {}
            }
            """);

        php.Should().MatchRegex(
            new Regex(
                @"public string \$name = 'x' \{\s+get \{",
                RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_MultiParamCtor_MixedPromotionAndHooks_EmitsAllParametersCorrectly()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public function __construct(
                    public string $name {
                        get {
                            return \strtoupper($this->name);
                        }
                        set(string $value) {
                            $this->name = \strtolower($value);
                        }
                    },
                    private int $count = 0,
                    public bool $active = true,
                ): void {}
            }
            """);

        php.Should().Contain("public string $name {");
        php.Should().Contain("return \\strtoupper($this->name);");
        php.Should().Contain("set(string $value)");
        php.Should().Contain("$this->name = \\strtolower($value);");
        php.Should().Contain("private int $count = 0");
        php.Should().Contain("public bool $active = true");
        // Hooked promoted params force a multiline signature; plain params still appear.
        php.Should().MatchRegex(
            new Regex(
                @"__construct\(\s+public string \$name \{",
                RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_Php82_HookedProperty_LowersToPropertyAccessorPolyfill()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name = 'default' {
                    get => $this->name;
                    set => \strtolower($value);
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("use \\Tyhp\\Concerns\\UsesPropertyAccessors;");
        php.Should().Contain("$this->tyhpBootTraits();");
        php.Should().Contain("protected function __initPropertyHooks__tyhpPropertyHook(): void");
        php.Should().Contain("if ($this->__tyhpPropertyHook->needsInit()) {");
        php.Should().Contain("self::__initPropertyHooks__tyhpPropertyHook();");
        php.Should().Contain("$this->__tyhpPropertyHook->markBound();");
        php.Should().NotContain("$this->__tyhpPropertyHook ??= new \\Tyhp\\PropertyAccessorObject();");
        php.Should().NotContain("shadowInheritedProperty");
        php.Should().Contain("__tyhpPropertyHook->register__tyhpGeneric(");
        php.Should().Contain("__tyhpPropertyHook->register__tyhpGeneric(\\Tyhp\\Type::string())(");
        php.Should().Contain("$this,");
        php.Should().NotContain("\\Tyhp\\PropertyAccessor::new_Tyhp_PropertyAccessor__tyhpGeneric(");
        php.Should().NotContain("propertyName:");
        php.Should().Contain("declaringClass: self::class");
        php.Should().Contain("get: $this->__get_name__tyhpPropertyHook(...),");
        php.Should().Contain("set: $this->__set_name__tyhpPropertyHook(...),");
        php.Should().NotContain("\\Tyhp\\Generic::bind");
        php.Should().Contain("$this->__tyhpPropertyHook->getBacking('name', self::class)");
        php.Should().Contain("$this->__tyhpPropertyHook->setBacking('name'");
        php.Should().Contain(", self::class)");
        php.Should().Contain("backed: true");
        php.Should().Contain("defaultValue:");
        php.Should().NotContain("hasDefault:");
        php.Should().NotContain("defaultValueIsNull:");
        php.Should().NotContain("public string $name = 'default' {");
        php.Should().NotContain("get =>");
        php.Should().Contain("private function __get_name__tyhpPropertyHook(): mixed");
        php.Should().Contain("private function __set_name__tyhpPropertyHook(string $value): void");
        php.Should().NotContain("get: function ()");
        php.Should().NotContain("set: function (mixed");
        php.Should().NotContain("setAcceptType:");
    }

    [Fact]
    public void Emit_Php82_NullPropertyDefault_EmitsDefaultValueIsNull()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public ?string $name = null {
                    get => $this->name;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("defaultValueIsNull: true");
        php.Should().NotContain("hasDefault:");
        php.Should().NotContain("defaultValue: null");
    }

    [Fact]
    public void Emit_Php82_PromotedHook_EmitsDefaultValueAndNullGuard()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public function __construct(
                    public string $name = 'x' {
                        get => $this->name;
                    },
                ): void {}
            }
            """, phpVersion: "8.2");

        php.Should().Contain("defaultValue: $name,");
        php.Should().Contain("defaultValueIsNull: $name === null,");
        php.Should().NotContain("hasDefault:");
    }

    [Fact]
    public void Emit_Php82_GetOnlyBackedProperty_OmitsSetClosure()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $test1 = "default value" {
                    get => $this->test1;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("__tyhpPropertyHook->register__tyhpGeneric(");
        php.Should().Contain("private function __get_test1__tyhpPropertyHook(): mixed");
        php.Should().Contain("get: $this->__get_test1__tyhpPropertyHook(...),");
        php.Should().Contain("$this->__tyhpPropertyHook->getBacking('test1', self::class)");
        php.Should().NotContain("__set_test1__tyhpPropertyHook");
        php.Should().NotContain("set: $this->__set_");
        php.Should().Contain("backed: true");
        php.Should().Contain("@property string $test1");
    }

    [Fact]
    public void Emit_Php82_VirtualGetOnlyProperty_IsNotBackedAndIsPropertyRead()
    {
        // PHP 8.4: get-only with no $this->prop self-reference is virtual (no backing store);
        // omitting set does not invent default write — assignment is an error.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public bool $flag {
                    get => false;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("__tyhpPropertyHook->register__tyhpGeneric(");
        php.Should().Contain("private function __get_flag__tyhpPropertyHook(): mixed");
        php.Should().Contain("get: $this->__get_flag__tyhpPropertyHook(...),");
        php.Should().NotContain("__set_flag__tyhpPropertyHook");
        php.Should().NotContain("set: $this->__set_");
        php.Should().Contain("backed: false");
        php.Should().Contain("@property-read bool $flag");
        php.Should().NotContain("@property bool $flag");
    }

    [Fact]
    public void Emit_Php82_ArrowSetWithoutSelfReference_IsBacked()
    {
        // PHP 8.4: set => expr always writes the expression result to the backing store.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name {
                    set => \strtolower($value);
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("backed: true");
        php.Should().Contain("@property string $name");
        php.Should().Contain("__tyhpPropertyHook->setBacking('name'");
        php.Should().Contain("private function __set_name__tyhpPropertyHook(string $value): void");
        php.Should().Contain("set: $this->__set_name__tyhpPropertyHook(...),");
        php.Should().NotContain("setAcceptType:");
    }

    [Fact]
    public void Emit_Php82_WiderSetParameter_EmitsSetAcceptTypeAndSpelledParam()
    {
        // PHP 8.4 allows contravariant (wider) set parameter types; polyfill must accept them
        // before the set hook runs (FOUND Critical #3 / Low #7).
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name = '' {
                    get => $this->name;
                    set(string|\Stringable $value) {
                        $this->name = (string)$value;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("private function __set_name__tyhpPropertyHook(string | \\Stringable $value): void");
        php.Should().Contain("register__tyhpGeneric(\\Tyhp\\Type::string())(");
        php.Should().Contain("setAcceptType:");
        php.Should().Contain("\\Tyhp\\Type::union(");
        php.Should().Contain("\\Tyhp\\Type::string()");
        php.Should().Contain("\\Tyhp\\Type::fromClassName(\\Stringable::class)");
    }

    [Fact]
    public void Emit_Php82_TypedSetParameter_EmitsSetAcceptTypeMatchingProperty()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name = '' {
                    get => $this->name;
                    set(string $value) {
                        $this->name = $value;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("private function __set_name__tyhpPropertyHook(string $value): void");
        php.Should().Contain("setAcceptType: \\Tyhp\\Type::string()");
    }

    [Fact]
    public void Emit_Php82_VirtualGetAndSet_UsesPropertyTag()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private string $_name = 'x';

                public string $name {
                    get {
                        return $this->_name;
                    }
                    set(string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("backed: false");
        php.Should().Contain("@property string $name");
    }

    [Fact]
    public void Emit_Php82_MergesMagicPropertyTagsIntoExistingClassDoc()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /** Widget docs. */
            final class Widget {
                public int $count {
                    get => 0;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("Widget docs.");
        php.Should().Contain("@property-read int $count");
    }

    [Fact]
    public void Emit_Php82_SeparateBackingField_DoesNotRewriteOtherProperties()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private string $_name = 'x';

                public string $name {
                    get {
                        return $this->_name;
                    }
                    set(string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("return $this->_name;");
        php.Should().Contain("$this->_name = $value;");
        php.Should().NotContain("__tyhpPropertyHook->getBacking('_name')");
        php.Should().Contain("__tyhpPropertyHook->register__tyhpGeneric(");
    }

    [Fact]
    public void Emit_Php84_StillUsesNativeHooks()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name {
                    get => $this->name;
                }
            }
            """, phpVersion: "8.4");

        php.Should().Contain("public string $name {");
        // Arrow-form input (`get => expr;`) round-trips as arrow-form output — both are valid
        // native PHP 8.4 hook syntax with identical semantics, so the emitter preserves the
        // author's chosen form rather than always expanding to a `{ return expr; }` block.
        php.Should().Contain("get => $this->name;");
        php.Should().NotContain("UsesPropertyAccessors");
        php.Should().NotContain("__tyhpPropertyHook->register");
    }

    [Fact]
    public void Emit_Php82_ClassOwnedGet_InjectsTryGetAndPieceTraits()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public string $name = 'x' {
                    get => $this->name;
                }

                public function __get(string $name): mixed {
                    return null;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("use \\Tyhp\\Concerns\\HasPropertyAccessors;");
        php.Should().Contain("use \\Tyhp\\Concerns\\HandlesGet;");
        php.Should().Contain("use \\Tyhp\\Concerns\\BootsTraits;");
        php.Should().NotContain("use \\Tyhp\\Concerns\\UsesPropertyAccessors;");
        php.Should().Contain("tyhpTryGet");
        php.Should().Contain("$__tyhp_out = null;");
        php.Should().Contain("if ($this->tyhpTryGet($name, $__tyhp_out)) {");
        php.Should().Contain("return $__tyhp_out;");
        php.Should().NotContain("$__tyhp_out = null; if ($this->tyhpTryGet");
    }

    [Fact]
    public void Emit_Php82_ParentPropertyHookSet_RewritesToTyhpParentSet()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class Point {
                public int $x = 0;
            }

            class PositivePoint extends Point {
                public int $x {
                    set {
                        parent::$x::set($value);
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("$this->__tyhpPropertyHook->parentSet($this, 'x', $value, self::class)");
        php.Should().NotContain("parent::$x::set");
    }

    [Fact]
    public void Emit_Php82_ChildOverrideOfPlainParentProperty_ForcesBackedAndInheritedDefault()
    {
        // FOUND Critical #1 (2026-08-06): set-only / get-only overrides of plain parent props
        // must be backed with the inherited default seeded before shadow — matching PHP 8.4
        // and PropertyHookPolyfillSmokeTest.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class Point {
                public int $x = 0;
                public string $label = 'point';
            }

            class PositivePoint extends Point {
                public int $x {
                    set {
                        parent::$x::set($value);
                    }
                }

                public string $label {
                    get => \strtoupper(parent::$label::get());
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("backed: true");
        php.Should().Contain("@property int $x");
        php.Should().Contain("@property string $label");
        php.Should().NotContain("@property-write int $x");
        php.Should().NotContain("@property-read string $label");

        php.Should().Contain("$__tyhp_inherited_x = null;");
        php.Should().Contain("$__tyhp_inherited_x_isNull = false;");
        php.Should().Contain("new \\ReflectionProperty(\\Probe\\Point::class, 'x')");
        php.Should().Contain("defaultValue: $__tyhp_inherited_x,");
        php.Should().Contain("defaultValueIsNull: $__tyhp_inherited_x_isNull,");

        php.Should().Contain("$__tyhp_inherited_label = null;");
        php.Should().Contain("new \\ReflectionProperty(\\Probe\\Point::class, 'label')");
        php.Should().Contain("defaultValue: $__tyhp_inherited_label,");
        php.Should().Contain("$this->__tyhpPropertyHook->parentGet($this, 'label', self::class)");
        php.Should().Contain("$this->__tyhpPropertyHook->parentSet($this, 'x', $value, self::class)");
    }

    [Fact]
    public void Emit_Php82_HookedPropertySameNameAsPrivateAncestorProperty_IsNotTreatedAsOverride()
    {
        // A private ancestor property is not inherited storage — PHP treats a same-named child
        // declaration as a brand-new, unrelated property, so the polyfill must not force
        // `backed: true` or Reflection-capture the ancestor's private slot as a "default".
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class Point {
                private int $x = 0;
            }

            class PositivePoint extends Point {
                private int $_shadow = 0;

                public int $x {
                    set {
                        $this->_shadow = $value;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().NotContain("new \\ReflectionProperty(\\Probe\\Point::class, 'x')");
        php.Should().NotContain("$__tyhp_inherited_x");
        php.Should().Contain("@property-write int $x");
        php.Should().NotContain("@property int $x");
    }

    [Fact]
    public void Emit_Php84_PrivateSet_EmitsAsymmetricVisibility()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public private(set) string $name = 'x' {
                    get => $this->name;
                    set => $value;
                }
            }
            """, phpVersion: "8.4");

        php.Should().Contain("public private(set) string $name");
        php.Should().NotContain("publicset");
        php.Should().NotContain("privateset");
    }

    [Fact]
    public void Emit_Php82_ProtectedHookedProperty_PassesVisibilityToRegister()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                protected string $hidden = 'x' {
                    get => $this->hidden;
                    set => $value;
                }

                private string $secret = 'y' {
                    get => $this->secret;
                    set => $value;
                }

                public private(set) string $name = 'z' {
                    get => $this->name;
                    set => $value;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("visibility: 'protected'");
        php.Should().Contain("setVisibility: 'protected'");
        php.Should().Contain("visibility: 'private'");
        php.Should().Contain("setVisibility: 'private'");
        // Asymmetric: get stays public (no visibility arg); set is private.
        php.Should().Contain("setVisibility: 'private'");
        php.Should().Contain("@property string $name");
        php.Should().NotContain("@property string $hidden");
        php.Should().NotContain("@property string $secret");
    }

    [Fact]
    public void Emit_Php82_FinalHooks_PassFinalGetAndFinalSetToRegister()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class ParentWidget {
                public string $name = 'x' {
                    final get => $this->name;
                    final set => \strtolower($value);
                }

                public string $username = 'u' {
                    get => $this->username;
                    final set => \strtolower($value);
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("finalGet: true");
        php.Should().Contain("finalSet: true");
        // username: only set is final — get must not mark finalGet.
        php.Should().MatchRegex(
            new Regex(
                @"register__tyhpGeneric\(.*?\)\(\s*'username'.*?finalSet:\s*true",
                RegexOptions.Singleline));
        php.Should().NotMatchRegex(
            new Regex(
                @"register__tyhpGeneric\(.*?\)\(\s*'username'.*?finalGet:\s*true",
                RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_Php82_CompoundAssign_RewritesViaGetAndSetBacking()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public int $count = 0 {
                    get => $this->count;
                    set {
                        $this->count += $value;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain(
            "$this->__tyhpPropertyHook->setBacking('count', $this->__tyhpPropertyHook->getBacking('count', self::class) + $value, self::class)");
        php.Should().NotContain("$this->count +=");
    }

    [Fact]
    public void Emit_Php82_GenericPropertyHook_UsesPropertyAccessorFactoryNotGenericBind()
    {
        // FOUND #1d/#1h: Tyhp-compiled sites must call Mechanism C factory, not Generic::bind.
        // FOUND #1e: same-namespace unqualified names in Type::generic must emit FQCN.
        // FOUND #1f: hook closures must be multi-line (PSR-12).
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Box<TValue> {
                public function __construct(private TValue $inner): void {}
                public function getInner(): TValue { return $this->inner; }
            }

            final class Holder<TValue> {
                public Box<TValue> $box {
                    get {
                        return $this->box;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("\\Tyhp\\Type::generic(\\Probe\\Box::class");
        php.Should().NotContain("\\Tyhp\\Type::generic(\\Box::class");
        php.Should().Contain("private function __get_box__tyhpPropertyHook(): mixed");
        php.Should().Contain("get: $this->__get_box__tyhpPropertyHook(...),");
        php.Should().NotContain("\\Tyhp\\PropertyAccessor::new_Tyhp_PropertyAccessor__tyhpGeneric(");
        php.Should().NotContain("get: function ()");
        php.Should().Contain("return $this->__tyhpPropertyHook->getBacking('box', self::class);");
    }

    [Fact]
    public void Emit_Php82_GenericPlusHooks_BootsTraitsOnceInConstructor()
    {
        // Generic ctor prologue and property-hook prologue both need the bag; boot only once
        // in __construct. Init hooks also boot for factory / newInstanceWithoutConstructor.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Holder<TValue> {
                public function __construct(): void {}

                public ?TValue $value = null {
                    get => $this->value;
                    set => $value;
                }
            }
            """, phpVersion: "8.2");

        // Ctor + __initGenerics__tyhpGeneric + __initPropertyHooks__tyhpPropertyHook.
        System.Text.RegularExpressions.Regex.Matches(php, @"\$this->tyhpBootTraits\(\);")
            .Count
            .Should()
            .Be(3);
        php.Should().Contain(
            """
            $this->tyhpBootTraits();
                    if ($this->__tyhpGeneric->needsInit()) {
            """);
        php.Should().Contain("if ($this->__tyhpPropertyHook->needsInit()) {");
        php.Should().Contain("self::__initPropertyHooks__tyhpPropertyHook();");
        php.Should().Contain("protected function __initPropertyHooks__tyhpPropertyHook(): void");
        php.Should().NotContain(
            """
            }
                    $this->tyhpBootTraits();
                    $this->__tyhpPropertyHook->register
            """);
    }

    [Fact]
    public void Emit_Php82_GenericHookedProperty_PhpDocKeepsTypeParameterName()
    {
        // FOUND Low #9: @property must spell TValue (not erased mixed) and emit @template.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * Generic host with hooked type-param properties.
             */
            final class Holder<TValue> {
                public TValue $freeGeneric;

                public TValue $hookedGeneric {
                    get => $this->hookedGeneric;
                    set {
                        $this->hookedGeneric = $value;
                    }
                }

                public TValue $virtualOfGeneric {
                    get => $this->freeGeneric;
                }

                public function __construct(TValue $freeGeneric, TValue $hookedGeneric): void
                {
                    $this->freeGeneric = $freeGeneric;
                    $this->hookedGeneric = $hookedGeneric;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("@template TValue");
        php.Should().Contain("@property TValue $hookedGeneric");
        php.Should().Contain("@property TValue $freeGeneric");
        php.Should().Contain("@property-read TValue $virtualOfGeneric");
        php.Should().NotContain("@property mixed $hookedGeneric");
        php.Should().NotContain("@property mixed $freeGeneric");
        php.Should().NotContain("@property-read mixed $virtualOfGeneric");
    }

    [Fact]
    public void Emit_Php82_GenericHookedProperty_PhpDocKeepsParameterizedClassType()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Box<TValue> {
                public function __construct(private TValue $inner): void {}
            }

            final class Holder<TValue> {
                public Box<TValue> $box {
                    get {
                        return $this->box;
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("@template TValue");
        php.Should().Contain("@property \\Probe\\Box<TValue> $box");
        php.Should().NotContain("@property mixed $box");
    }

    [Fact]
    public void Emit_Php82_AuthoredTemplateTagDoesNotFalsePositiveMatchPrefixedParamName()
    {
        // A plain substring dedup check would let author-written "@template TValue" incorrectly
        // count as already documenting the unrelated "T" parameter (since "@template T" is a
        // textual prefix of "@template TValue"), silently dropping the needed "@template T" tag.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * @template TValue
             */
            final class Pair<T, TValue> {
                public T $first {
                    get => $this->first;
                    set {
                        $this->first = $value;
                    }
                }

                public function __construct(T $first): void
                {
                    $this->first = $first;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("@template T\n");
        php.Should().Contain("@template TValue");
        php.Should().Contain("@property T $first");
        // Only one "@template TValue" line — the author's own, not a duplicate emitted copy.
        System.Text.RegularExpressions.Regex.Matches(php, @"@template TValue\b")
            .Count
            .Should()
            .Be(1);
    }

    [Fact]
    public void Emit_Php82_AuthoredPropertyTagDoesNotFalsePositiveMatchPrefixedPropertyName()
    {
        // Same boundary issue as the @template dedup, for @property* tags: an author-written
        // "$valueOverride" must not suppress the needed tag for the unrelated "$value" property.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * @property string $valueOverride
             */
            final class Holder {
                public string $value {
                    get => $this->value;
                    set {
                        $this->value = $value;
                    }
                }

                public function __construct(string $value): void
                {
                    $this->value = $value;
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("@property string $value\n");
        php.Should().Contain("@property string $valueOverride");
    }

    [Fact]
    public void Emit_Php82_ChildOverrideOfHookedParent_EmitsInitChainAndParentDispatch()
    {
        // FOUND Critical #2: hooked-parent overrides must register both levels via
        // __initPropertyHooks__tyhpPropertyHook (not parent::__construct) and keep
        // parent hooks reachable for parent::$prop::get/set.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class HookedParent {
                public string $name = 'parent' {
                    get => $this->name;
                    set => \strtolower($value);
                }
            }

            class HookedChild extends HookedParent {
                public string $name {
                    get => \strtoupper(parent::$name::get());
                    set {
                        parent::$name::set($value);
                    }
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("use \\Tyhp\\Concerns\\UsesPropertyAccessors;");
        // Trait only on the topmost hooked level.
        System.Text.RegularExpressions.Regex.Matches(php, @"use \\Tyhp\\Concerns\\UsesPropertyAccessors;")
            .Count
            .Should()
            .Be(1);

        php.Should().Contain("protected function __initPropertyHooks__tyhpPropertyHook(): void");
        php.Should().Contain("parent::__initPropertyHooks__tyhpPropertyHook();");
        php.Should().Contain("$this->__tyhpPropertyHook->markBound();");
        php.Should().Contain("if ($this->__tyhpPropertyHook->needsInit()) {");
        php.Should().Contain("self::__initPropertyHooks__tyhpPropertyHook();");
        php.Should().Contain("$this->__tyhpPropertyHook->parentGet($this, 'name', self::class)");
        php.Should().Contain("$this->__tyhpPropertyHook->parentSet($this, 'name', $value, self::class)");
        php.Should().NotContain("parent::__construct");
    }

    [Fact]
    public void Emit_Php82_PassThroughGrandchildOfHookedOverride_AnchorsParentGetSetOnDeclaringClass()
    {
        // Regression: a 3rd-level pass-through subclass (no hooks/ctor of its own) that inherits
        // a hooked-parent override must not make parentGet/parentSet resolve relative to $host's
        // own runtime class — that would land back on the middle level's own accessor and recurse
        // into the very hook currently executing. self::class inside the (inherited, unmodified)
        // hook body always spells the middle level, so the runtime search anchors correctly no
        // matter how many pass-through levels sit below it.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class HookedGrandparent {
                public string $name = 'parent' {
                    get => $this->name;
                    set => \strtolower($value);
                }
            }

            class HookedParent extends HookedGrandparent {
                public string $name {
                    get => \strtoupper(parent::$name::get());
                    set {
                        parent::$name::set($value);
                    }
                }
            }

            class PassThroughChild extends HookedParent {
            }
            """, phpVersion: "8.2");

        php.Should().Contain("$this->__tyhpPropertyHook->parentGet($this, 'name', self::class)");
        php.Should().Contain("$this->__tyhpPropertyHook->parentSet($this, 'name', $value, self::class)");
        // Pass-through level still gets its own init-hook override delegating to parent.
        php.Should().MatchRegex(new Regex(
            @"class PassThroughChild extends HookedParent\s*\{.*?protected function __initPropertyHooks__tyhpPropertyHook\(\): void",
            RegexOptions.Singleline));
    }

    [Fact]
    public void Emit_Php82_PassThroughSubclassOfHookedParent_ForwardsInheritedConstructor()
    {
        // A subclass that declares no hooks of its own and no author ctor is still pulled into
        // the property-hook chain (its parent is hooked) and needs a synthesized __construct.
        // That synthesized ctor must forward the inherited constructor's parameter list and call
        // it — the normal PHP "no override => inherit the constructor" contract — instead of
        // silently replacing it with a bare no-arg constructor that never reaches the parent.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class HookedParent {
                public string $name = 'parent' {
                    get => $this->name;
                    set => \strtolower($value);
                }

                public function __construct(string $seed) {
                    $this->name = $seed;
                }
            }

            class PassThroughChild extends HookedParent {
            }
            """, phpVersion: "8.2");

        php.Should().MatchRegex(new Regex(@"class PassThroughChild extends HookedParent"));
        php.Should().Contain("public function __construct(string $seed)");
        php.Should().Contain("if ($this->__tyhpPropertyHook->needsInit()) {");
        php.Should().Contain("self::__initPropertyHooks__tyhpPropertyHook();");
        php.Should().Contain("parent::__construct($seed);");
    }

    [Fact]
    public void Emit_Php82_PromotedPropertyWithSetHook_RoutesInitialValueThroughSet()
    {
        // A promoted ctor param whose property has a `set` hook must have that hook run on the
        // initial value too — PHP 8.4 native promotion assigns through the hook exactly as if the
        // ctor body started with `$this->prop = $prop;`. Pre-seeding `defaultValue` with the raw
        // ctor arg (as for hookless / get-only promoted props) would bypass the hook's transform
        // (`\strtolower` here) and any validation it performs.
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public function __construct(
                    public private(set) string $name {
                        get => $this->name;
                        set => \strtolower($value);
                    },
                ) {
                }
            }
            """, phpVersion: "8.2");

        php.Should().Contain("__tyhpPropertyHook->register__tyhpGeneric(");
        php.Should().NotContain("defaultValue: $name,");
        php.Should().NotContain("defaultValueIsNull: $name === null,");
        php.Should().MatchRegex(new Regex(
            @"__tyhpPropertyHook->register__tyhpGeneric\(.*?\);\s*\$this->__tyhpPropertyHook->set\('name', \$name\);",
            RegexOptions.Singleline));
    }

    private static string CompileAndEmit(string tyhp) =>
        CompileAndEmit(tyhp, phpVersion: "8.4");

    private static string CompileAndEmit(string tyhp, string phpVersion) =>
        string.Join('\n', CompileToFiles(tyhp, phpVersion).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp) =>
        CompileToFiles(tyhp, phpVersion: "8.4");

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "hooks.tyhp");
        File.WriteAllText(filePath, tyhp);

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
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = phpVersion,
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
