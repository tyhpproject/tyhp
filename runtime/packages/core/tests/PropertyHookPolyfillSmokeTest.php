<?php

declare(strict_types=1);

namespace Tyhp\Tests\Core;

use PHPUnit\Framework\TestCase;
use Tyhp\Concerns\UsesPropertyAccessors;
use Tyhp\Type;

class PolyfillParent
{
    public string $val = 'parent-val';
}

class PolyfillChild extends PolyfillParent
{
    use UsesPropertyAccessors;

    public function __construct()
    {
        $this->tyhpBootTraits();
        // Emitter seeds defaultValue via Reflection before register; runtime register also
        // auto-captures when defaultValue is omitted. Smoke the runtime path here.
        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'val',
            $this,
            get: fn(): string => \strtoupper($this->__tyhpPropertyHook->parentGet($this, 'val', self::class)),
            set: function (mixed $value): void {
                $this->__tyhpPropertyHook->parentSet($this, 'val', \strtolower((string) $value), self::class);
            },
            declaringClass: self::class,
        );
        $this->__tyhpPropertyHook->markBound();
    }

    public function registerPrivateSet(string $property, Type $type, mixed ...$arguments): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpPropertyHook->register__tyhpGeneric($type)($property, $this, ...$arguments);
    }

    public function readParentBacking(): mixed
    {
        return $this->__tyhpPropertyHook->parentGet($this, 'val', self::class);
    }
}

/**
 * Manual Mechanism C–style init chain for a hooked parent + child override (Critical #2).
 */
class PolyfillHookedParent
{
    use UsesPropertyAccessors;

    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'name',
            $this,
            get: $this->__get_name__tyhpPropertyHook(...),
            set: $this->__set_name__tyhpPropertyHook(...),
            backed: true,
            defaultValue: 'parent',
            declaringClass: self::class,
        );
        $this->__tyhpPropertyHook->markBound();
    }

    private function __get_name__tyhpPropertyHook(): mixed
    {
        return $this->__tyhpPropertyHook->getBacking('name', self::class);
    }

    private function __set_name__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->setBacking('name', \strtolower($value), self::class);
    }
}

class PolyfillHookedChild extends PolyfillHookedParent
{
    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'name',
            $this,
            get: $this->__get_name__tyhpPropertyHook(...),
            set: $this->__set_name__tyhpPropertyHook(...),
            backed: false,
            declaringClass: self::class,
        );
        parent::__initPropertyHooks__tyhpPropertyHook();
    }

    private function __get_name__tyhpPropertyHook(): mixed
    {
        return \strtoupper((string) $this->__tyhpPropertyHook->parentGet($this, 'name', self::class));
    }

    private function __set_name__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->parentSet($this, 'name', $value, self::class);
    }
}

/**
 * Pass-through subclass of a hooked-parent override: adds no hooks of its own and no author
 * constructor. Regression for the `parentGet`/`parentSet` ancestor search: it must anchor on the
 * *calling* level's declaring class (`self::class` inside `PolyfillHookedChild`'s inherited hook
 * methods), not on this instance's own runtime parent (`PolyfillHookedChild`) — otherwise the
 * search lands back on `PolyfillHookedChild`'s own accessor and recurses into the very hook that
 * is already running.
 */
class PolyfillHookedGrandchild extends PolyfillHookedChild
{
}

/**
 * Parent with final get + final set (Critical Medium #6).
 */
class PolyfillFinalHookParent
{
    use UsesPropertyAccessors;

    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'name',
            $this,
            get: $this->__get_name__tyhpPropertyHook(...),
            set: $this->__set_name__tyhpPropertyHook(...),
            backed: true,
            defaultValue: 'parent',
            declaringClass: self::class,
            finalGet: true,
            finalSet: true,
        );
        $this->__tyhpPropertyHook->markBound();
    }

    private function __get_name__tyhpPropertyHook(): mixed
    {
        return $this->__tyhpPropertyHook->getBacking('name', self::class);
    }

    private function __set_name__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->setBacking('name', \strtolower($value), self::class);
    }
}

/**
 * Illegal child override of final get (and set) — registration must throw.
 */
class PolyfillFinalHookChildOverrideBoth extends PolyfillFinalHookParent
{
    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'name',
            $this,
            get: $this->__get_name__tyhpPropertyHook(...),
            set: $this->__set_name__tyhpPropertyHook(...),
            backed: false,
            declaringClass: self::class,
        );
        parent::__initPropertyHooks__tyhpPropertyHook();
    }

    private function __get_name__tyhpPropertyHook(): mixed
    {
        return \strtoupper((string) $this->__tyhpPropertyHook->parentGet($this, 'name', self::class));
    }

    private function __set_name__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->parentSet($this, 'name', $value, self::class);
    }
}

/**
 * Parent with final set only — child may add/override get.
 */
class PolyfillFinalSetOnlyParent
{
    use UsesPropertyAccessors;

    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'username',
            $this,
            set: $this->__set_username__tyhpPropertyHook(...),
            backed: true,
            defaultValue: 'user',
            declaringClass: self::class,
            finalSet: true,
        );
        $this->__tyhpPropertyHook->markBound();
    }

    private function __set_username__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->setBacking('username', \strtolower($value), self::class);
    }
}

class PolyfillFinalSetOnlyChildAddsGet extends PolyfillFinalSetOnlyParent
{
    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'username',
            $this,
            get: $this->__get_username__tyhpPropertyHook(...),
            backed: false,
            declaringClass: self::class,
        );
        parent::__initPropertyHooks__tyhpPropertyHook();
    }

    private function __get_username__tyhpPropertyHook(): mixed
    {
        return \strtoupper((string) $this->__tyhpPropertyHook->parentGet($this, 'username', self::class));
    }
}

/**
 * Illegal child override of final set while adding get.
 */
class PolyfillFinalSetOnlyChildOverridesSet extends PolyfillFinalSetOnlyParent
{
    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'username',
            $this,
            get: $this->__get_username__tyhpPropertyHook(...),
            set: $this->__set_username__tyhpPropertyHook(...),
            backed: false,
            declaringClass: self::class,
        );
        parent::__initPropertyHooks__tyhpPropertyHook();
    }

    private function __get_username__tyhpPropertyHook(): mixed
    {
        return \strtoupper((string) $this->__tyhpPropertyHook->parentGet($this, 'username', self::class));
    }

    private function __set_username__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->parentSet($this, 'username', \strtoupper($value), self::class);
    }
}

/**
 * Critical #11: natural partial get-only override with self-referencing `$this->prop`
 * (no parentGet/parentSet delegation). Parent set and child get must share one backing cell,
 * and assignment must dispatch to the inherited set hook.
 */
class PolyfillPartialOverrideParent
{
    use UsesPropertyAccessors;

    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'prop',
            $this,
            get: $this->__get_prop__tyhpPropertyHook(...),
            set: $this->__set_prop__tyhpPropertyHook(...),
            backed: true,
            defaultValue: 'parent',
            declaringClass: self::class,
            finalSet: true,
        );
        $this->__tyhpPropertyHook->markBound();
    }

    private function __get_prop__tyhpPropertyHook(): string
    {
        return $this->__tyhpPropertyHook->getBacking('prop', self::class);
    }

    private function __set_prop__tyhpPropertyHook(string $value): void
    {
        $this->__tyhpPropertyHook->setBacking('prop', \strtolower($value), self::class);
    }
}

class PolyfillPartialOverrideChild extends PolyfillPartialOverrideParent
{
    public function __construct()
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->needsInit()) {
            self::__initPropertyHooks__tyhpPropertyHook();
        }
    }

    protected function __initPropertyHooks__tyhpPropertyHook(): void
    {
        $this->tyhpBootTraits();
        if ($this->__tyhpPropertyHook->isInitialized(self::class)) {
            return;
        }

        // Redeclare get only — self-referencing backing, leave set inherited.
        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'prop',
            $this,
            get: $this->__get_prop__tyhpPropertyHook(...),
            backed: true,
            declaringClass: self::class,
        );
        parent::__initPropertyHooks__tyhpPropertyHook();
    }

    private function __get_prop__tyhpPropertyHook(): string
    {
        return $this->__tyhpPropertyHook->getBacking('prop', self::class);
    }
}

/**
 * Runtime smoke for PHP &lt; 8.4 property-hook polyfill helpers (parent hooks, shadow, visibility).
 */
class PropertyHookPolyfillSmokeTest extends TestCase
{
    public function testParentGetSetAndShadow(): void
    {
        $child = new PolyfillChild();
        self::assertSame('PARENT-VAL', $child->val);

        $child->val = 'HELLO';
        self::assertSame('HELLO', $child->val);
        self::assertSame('hello', $child->readParentBacking());
    }

    public function testHookedParentOverrideUsesParentHooksWithoutParentConstruct(): void
    {
        $child = new PolyfillHookedChild();
        self::assertSame('PARENT', $child->name);

        $child->name = 'HELLO';
        self::assertSame('HELLO', $child->name);
    }

    public function testPassThroughSubclassOfHookedOverrideDoesNotRecurseIntoOwnHook(): void
    {
        $grandchild = new PolyfillHookedGrandchild();
        self::assertSame('PARENT', $grandchild->name);

        $grandchild->name = 'HELLO';
        self::assertSame('HELLO', $grandchild->name);
    }

    public function testPrivateSetVisibilityEnforced(): void
    {
        $obj = new PolyfillChild();
        $obj->registerPrivateSet(
            'secret',
            Type::string(),
            set: function (mixed $value): void {},
            backed: true,
            declaringClass: PolyfillChild::class,
            setVisibility: 'private',
        );

        $this->expectException(\Error::class);
        $this->expectExceptionMessage('private(set)');
        $obj->secret = 'nope';
    }

    public function testProtectedPropertyVisibilityEnforcedOnGetAndSet(): void
    {
        $obj = new PolyfillChild();
        $obj->registerPrivateSet(
            'hidden',
            Type::string(),
            get: fn(): string => 'secret',
            set: function (mixed $value): void {},
            backed: true,
            defaultValue: 'secret',
            declaringClass: PolyfillChild::class,
            visibility: 'protected',
            setVisibility: 'protected',
        );

        try {
            $_ = $obj->hidden;
            self::fail('Expected Error on protected get');
        } catch (\Error $e) {
            self::assertStringContainsString('Cannot access protected property', $e->getMessage());
        }

        $this->expectException(\Error::class);
        $this->expectExceptionMessage('protected(set)');
        $obj->hidden = 'nope';
    }

    public function testPrivatePropertyVisibilityIssetReturnsFalseFromOutside(): void
    {
        $obj = new PolyfillChild();
        $obj->registerPrivateSet(
            'hidden',
            Type::string(),
            get: fn(): string => 'secret',
            backed: true,
            defaultValue: 'secret',
            declaringClass: PolyfillChild::class,
            visibility: 'private',
            setVisibility: 'private',
        );

        self::assertFalse(isset($obj->hidden));
    }

    public function testGetIsFinalOnUsesPropertyAccessors(): void
    {
        $ref = new \ReflectionMethod(PolyfillChild::class, '__get');
        self::assertTrue($ref->isFinal());
    }

    public function testFinalGetAndSetRejectChildOverrideOnConstruct(): void
    {
        $this->expectException(\Error::class);
        $this->expectExceptionMessage(
            'Cannot override final property hook ' . PolyfillFinalHookParent::class . '::$name::',
        );
        new PolyfillFinalHookChildOverrideBoth();
    }

    public function testFinalSetOnlyAllowsChildToAddGet(): void
    {
        $child = new PolyfillFinalSetOnlyChildAddsGet();
        self::assertSame('USER', $child->username);
    }

    public function testFinalSetOnlyRejectsChildSetOverride(): void
    {
        $this->expectException(\Error::class);
        $this->expectExceptionMessage(
            'Cannot override final property hook ' . PolyfillFinalSetOnlyParent::class . '::$username::set()',
        );
        new PolyfillFinalSetOnlyChildOverridesSet();
    }

    public function testPartialGetOnlyOverrideSharesBackingWithInheritedSet(): void
    {
        $child = new PolyfillPartialOverrideChild();
        self::assertSame('parent', $child->prop);

        $child->prop = 'MixedCase';
        self::assertSame('mixedcase', $child->prop);
    }
}
