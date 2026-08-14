<?php

declare(strict_types=1);

namespace Tyhp\Tests\Core;

use PHPUnit\Framework\TestCase;
use Tyhp\Concerns\BootsTraits;
use Tyhp\Concerns\HandlesGet;
use Tyhp\Concerns\HandlesIsset;
use Tyhp\Concerns\HandlesSet;
use Tyhp\Concerns\HandlesUnset;
use Tyhp\Concerns\HasPropertyAccessors;
use Tyhp\Concerns\UsesPropertyAccessors;
use Tyhp\Exceptions\IncompatibleTypeException;
use Tyhp\Generic;
use Tyhp\PropertyAccessor;
use Tyhp\Type;

class TestAccessorClass
{
    use UsesPropertyAccessors;

    public function registerTestAccessor(string $property, Type $type, mixed ...$arguments): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpPropertyHook->register__tyhpGeneric($type)($property, $this, ...$arguments);
    }

    public function getTestBacking(string $property): mixed
    {
        return $this->__tyhpPropertyHook->getBacking($property);
    }

    public function setTestBacking(string $property, mixed $value): void
    {
        $this->__tyhpPropertyHook->setBacking($property, $value);
    }
}

/**
 * Host with helpers that exercise private/protected hooked visibility from inside the
 * declaring class (stack frames must resolve to this class, not PropertyAccessorObject).
 */
class TestAccessorClassWithVisibilityHelpers extends TestAccessorClass
{
    public function registerPrivateProp(string $name, string $initial): void
    {
        $stored = $initial;
        $this->registerTestAccessor(
            $name,
            Type::string(),
            get: static function () use (&$stored): string {
                return $stored;
            },
            set: static function (mixed $value) use (&$stored): void {
                $stored = (string) $value;
            },
            backed: true,
            defaultValue: $initial,
            declaringClass: self::class,
            visibility: 'private',
            setVisibility: 'private',
        );
    }

    public function readSecret(): string
    {
        return $this->secret;
    }

    public function writeSecret(string $value): void
    {
        $this->secret = $value;
    }

    public function issetSecret(): bool
    {
        return isset($this->secret);
    }
}

class TestAccessorSubclass extends TestAccessorClassWithVisibilityHelpers
{
    public function registerProtectedProp(string $name, string $initial): void
    {
        $stored = $initial;
        // Declaring class is the parent so protected checks allow this subclass.
        $this->registerTestAccessor(
            $name,
            Type::string(),
            get: static function () use (&$stored): string {
                return $stored;
            },
            set: static function (mixed $value) use (&$stored): void {
                $stored = (string) $value;
            },
            backed: true,
            defaultValue: $initial,
            declaringClass: TestAccessorClassWithVisibilityHelpers::class,
            visibility: 'protected',
            setVisibility: 'protected',
        );
    }

    public function readProt(): string
    {
        return $this->prot;
    }

    public function writeProt(string $value): void
    {
        $this->prot = $value;
    }
}

/**
 * Ancestor with a method that reaches into a *descendant's own* protected hooked property.
 * `debug_backtrace`'s `class` frame for an unoverridden inherited method is the class where the
 * method is lexically defined (here, this base), not the runtime object's class — so this
 * exercises the bidirectional half of the protected check: `declaringClass` (the descendant) is a
 * subclass of `callerClass` (this base), the reverse of the common case.
 */
class TestAccessorProtectedAncestor
{
    use UsesPropertyAccessors;

    public function readDescendantOwnProp(TestAccessorProtectedDescendant $obj): string
    {
        return $obj->descendantOwn;
    }

    public function writeDescendantOwnProp(TestAccessorProtectedDescendant $obj, string $value): void
    {
        $obj->descendantOwn = $value;
    }
}

class TestAccessorProtectedDescendant extends TestAccessorProtectedAncestor
{
    public function registerDescendantOwnProp(string $initial): void
    {
        $this->tyhpBootTraits();
        $stored = $initial;
        $this->__tyhpPropertyHook->register__tyhpGeneric(Type::string())(
            'descendantOwn',
            $this,
            get: static function () use (&$stored): string {
                return $stored;
            },
            set: static function (mixed $value) use (&$stored): void {
                $stored = (string) $value;
            },
            backed: true,
            defaultValue: $initial,
            declaringClass: self::class,
            visibility: 'protected',
            setVisibility: 'protected',
        );
    }
}

/**
 * Unrelated sibling of {@see TestAccessorProtectedDescendant} (common ancestor only) — must still
 * be denied, since neither direction of the bidirectional subclass check holds between siblings.
 * Declares its own accessor method (rather than reusing the inherited one) so the
 * `debug_backtrace` calling scope reported is this class, not the common ancestor.
 */
class TestAccessorProtectedSibling extends TestAccessorProtectedAncestor
{
    public function readOtherDescendantOwnProp(TestAccessorProtectedDescendant $obj): string
    {
        return $obj->descendantOwn;
    }
}

/**
 * Class that owns `__get` and must forward to `tyhpTryGet` (emitter merge pattern).
 */
class TestAccessorClassWithOwnGet
{
    use BootsTraits;
    use HandlesGet;
    use HandlesSet;
    use HandlesIsset;
    use HandlesUnset;
    use HasPropertyAccessors;

    public function registerTestAccessor(string $property, Type $type, mixed ...$arguments): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpPropertyHook->register__tyhpGeneric($type)($property, $this, ...$arguments);
    }

    public function __get(string $name): mixed
    {
        $value = null;
        if ($this->tyhpTryGet($name, $value)) {
            return $value;
        }

        if ($name === 'legacy') {
            return 'legacy-value';
        }

        \trigger_error(
            \sprintf('Undefined property: %s::$%s', static::class, $name),
            \E_USER_WARNING,
        );

        return null;
    }
}

class BootsTraitsProbe
{
    use BootsTraits;

    public array $booted = [];

    public function __bootTrait_Zebra(): void
    {
        $this->booted[] = 'Zebra';
    }

    public function __bootTrait_Alpha(): void
    {
        $this->booted[] = 'Alpha';
    }

    public function __bootTrait_NotARealFqN(): void
    {
        $this->booted[] = 'NotARealFqN';
    }
}

class PropertyAccessorTest extends TestCase
{
    /**
     * PropertyAccessor<TValue> reads its own type argument, so TValue has to be bound before the
     * constructor runs. From PHP that means going through \Tyhp\Generic; the constructor itself takes
     * no type arguments. Kept for tests that still need a raw PropertyAccessor instance.
     */
    private static function accessor(Type $valueType, mixed ...$arguments): PropertyAccessor
    {
        $accessor = Generic::bind(PropertyAccessor::class, $valueType)->new(...$arguments);

        self::assertInstanceOf(PropertyAccessor::class, $accessor);

        return $accessor;
    }

    public function testBootTraitsRunsPrefixMethodsInSortedOrder(): void
    {
        $obj = new BootsTraitsProbe();
        $obj->tyhpBootTraits();
        $obj->tyhpBootTraits(); // idempotent

        self::assertSame(['Alpha', 'NotARealFqN', 'Zebra'], $obj->booted);
    }

    public function testGetWithGetter(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'name',
            Type::string(),
            get: fn(): string => 'computed-value',
        );

        self::assertSame('computed-value', $obj->__get('name'));
    }

    public function testSetWithSetter(): void
    {
        $obj = new TestAccessorClass();
        $stored = null;

        $obj->registerTestAccessor(
            'name',
            Type::string(),
            get: fn() => $stored,
            set: function (mixed $value) use (&$stored): void {
                $stored = $value;
            },
        );

        $obj->__set('name', 'hello');
        self::assertSame('hello', $stored);
    }

    public function testBackedGetOnlyUsesBacking(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'test1',
            Type::string(),
            backed: true,
            declaringClass: TestAccessorClass::class,
            defaultValue: 'default value',
        );

        self::assertSame('default value', $obj->test1);

        $obj->test1 = 'zxcv';
        self::assertSame('zxcv', $obj->test1);
        self::assertSame('zxcv', $obj->getTestBacking('test1'));
    }

    public function testBackedGetWithTransform(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'foo',
            Type::string(),
            get: fn(): string => $obj->getTestBacking('foo') . ' world',
            set: function (mixed $value) use ($obj): void {
                $obj->setTestBacking('foo', \strtolower((string) $value));
            },
            backed: true,
            declaringClass: TestAccessorClass::class,
            defaultValue: 'default',
        );

        self::assertSame('default world', $obj->foo);
        $obj->foo = 'HELLO';
        self::assertSame('hello world', $obj->foo);
    }

    public function testVirtualWriteOnlyIsWriteOnly(): void
    {
        $obj = new TestAccessorClass();
        $seen = null;

        $obj->registerTestAccessor(
            'writeonly',
            Type::string(),
            set: function (mixed $value) use (&$seen): void {
                $seen = $value;
            },
            declaringClass: TestAccessorClass::class,
        );

        $obj->writeonly = 'ok';
        self::assertSame('ok', $seen);

        $this->expectException(\Error::class);
        $this->expectExceptionMessage('Property ' . TestAccessorClass::class . '::$writeonly is write-only');
        $_ = $obj->writeonly;
    }

    public function testVirtualReadOnlyIsReadOnly(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'readonlyProp',
            Type::string(),
            get: fn(): string => 'readonly',
            declaringClass: TestAccessorClass::class,
        );

        self::assertSame('readonly', $obj->readonlyProp);

        $this->expectException(\Error::class);
        $this->expectExceptionMessage('Property ' . TestAccessorClass::class . '::$readonlyProp is read-only');
        $obj->readonlyProp = 'new value';
    }

    public function testSetTypeMismatch(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'count',
            Type::int(),
            set: function (mixed $value): void {},
        );

        $this->expectException(IncompatibleTypeException::class);
        $obj->__set('count', 'not an int');
    }

    public function testWiderSetAcceptTypeAllowsContravariantInput(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'name',
            Type::string(),
            get: function () use ($obj): mixed {
                return $obj->getTestBacking('name');
            },
            set: function (mixed $value) use ($obj): void {
                $obj->setTestBacking('name', (string) $value);
            },
            backed: true,
            declaringClass: TestAccessorClass::class,
            setAcceptType: Type::union(Type::string(), Type::fromClassName(\Stringable::class)),
        );

        $obj->name = new class () implements \Stringable {
            public function __toString(): string
            {
                return 'from-stringable';
            }
        };

        self::assertSame('from-stringable', $obj->name);
    }

    public function testWiderSetAcceptTypeStillRejectsUnrelatedTypes(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'name',
            Type::string(),
            set: function (mixed $value): void {},
            backed: true,
            declaringClass: TestAccessorClass::class,
            setAcceptType: Type::union(Type::string(), Type::fromClassName(\Stringable::class)),
        );

        $this->expectException(IncompatibleTypeException::class);
        $obj->__set('name', 42);
    }

    public function testSetBackingStillEnforcesPropertyTypeAfterWiderSet(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'name',
            Type::string(),
            set: function (mixed $value) use ($obj): void {
                // Intentionally store a non-string — setBacking must reject.
                $obj->setTestBacking('name', $value);
            },
            backed: true,
            declaringClass: TestAccessorClass::class,
            setAcceptType: Type::union(Type::string(), Type::fromClassName(\Stringable::class)),
        );

        $this->expectException(IncompatibleTypeException::class);
        $obj->__set('name', new class () implements \Stringable {
            public function __toString(): string
            {
                return 'x';
            }
        });
    }

    public function testUnsetThrowsError(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'prop',
            Type::string(),
            get: fn(): string => 'val',
            declaringClass: TestAccessorClass::class,
        );

        $this->expectException(\Error::class);
        $this->expectExceptionMessage('Cannot unset hooked property ' . TestAccessorClass::class . '::$prop');
        $obj->__unset('prop');
    }

    public function testPrivateVisibilityDeniedFromOutside(): void
    {
        $obj = new TestAccessorClass();
        $obj->registerTestAccessor(
            'secret',
            Type::string(),
            get: fn(): string => 'x',
            set: function (mixed $value): void {},
            backed: true,
            defaultValue: 'x',
            declaringClass: TestAccessorClass::class,
            visibility: 'private',
            setVisibility: 'private',
        );

        try {
            $_ = $obj->secret;
            self::fail('Expected Error on private get');
        } catch (\Error $e) {
            self::assertStringContainsString('Cannot access private property', $e->getMessage());
        }

        try {
            $obj->secret = 'y';
            self::fail('Expected Error on private set');
        } catch (\Error $e) {
            self::assertStringContainsString('private(set)', $e->getMessage());
        }

        self::assertFalse(isset($obj->secret));
    }

    public function testPrivateVisibilityAllowedFromDeclaringClassMethod(): void
    {
        $obj = new TestAccessorClassWithVisibilityHelpers();
        $obj->registerPrivateProp('secret', 'inside');

        self::assertSame('inside', $obj->readSecret());
        $obj->writeSecret('updated');
        self::assertSame('updated', $obj->readSecret());
        self::assertTrue($obj->issetSecret());
    }

    public function testProtectedVisibilityAllowedFromSubclass(): void
    {
        $obj = new TestAccessorSubclass();
        $obj->registerProtectedProp('prot', 'ok');

        self::assertSame('ok', $obj->readProt());
        $obj->writeProt('next');
        self::assertSame('next', $obj->readProt());
    }

    /**
     * PHP's protected check is bidirectional (matches Zend's `instanceof_function()` in both
     * directions): an ancestor method may reach into a *descendant's own* protected property, not
     * only the reverse (descendant reaching into an inherited protected property).
     */
    public function testProtectedVisibilityAllowedFromAncestorIntoDescendantOwnProperty(): void
    {
        $obj = new TestAccessorProtectedDescendant();
        $obj->registerDescendantOwnProp('ok');

        self::assertSame('ok', $obj->readDescendantOwnProp($obj));
        $obj->writeDescendantOwnProp($obj, 'next');
        self::assertSame('next', $obj->readDescendantOwnProp($obj));
    }

    public function testProtectedVisibilityDeniedBetweenUnrelatedSiblings(): void
    {
        $obj = new TestAccessorProtectedDescendant();
        $obj->registerDescendantOwnProp('ok');
        $sibling = new TestAccessorProtectedSibling();

        $this->expectException(\Error::class);
        $this->expectExceptionMessage(
            'Cannot access protected property '
                . TestAccessorProtectedDescendant::class . '::$descendantOwn',
        );
        $sibling->readOtherDescendantOwnProp($obj);
    }

    public function testUninitializedBackedReadThrows(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'backed',
            Type::string(),
            backed: true,
            declaringClass: TestAccessorClass::class,
        );

        $this->expectException(\Error::class);
        $this->expectExceptionMessage(
            'Typed property ' . TestAccessorClass::class . '::$backed must not be accessed before initialization',
        );
        $_ = $obj->backed;
    }

    public function testIssetFromGet(): void
    {
        $obj = new TestAccessorClass();
        $stored = 'hello';

        $obj->registerTestAccessor(
            'prop',
            Type::nullable(Type::string()),
            get: function () use (&$stored) {
                return $stored;
            },
        );

        self::assertTrue($obj->__isset('prop'));

        $stored = null;
        self::assertFalse($obj->__isset('prop'));
    }

    public function testIssetBackedWithoutGet(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'backed',
            Type::nullable(Type::string()),
            backed: true,
            declaringClass: TestAccessorClass::class,
        );

        self::assertFalse($obj->__isset('backed'));

        $obj->setTestBacking('backed', 'x');
        self::assertTrue($obj->__isset('backed'));

        $obj->setTestBacking('backed', null);
        self::assertFalse($obj->__isset('backed'));
    }

    public function testIssetWriteOnlyIsFalse(): void
    {
        $obj = new TestAccessorClass();

        $obj->registerTestAccessor(
            'writeonly',
            Type::string(),
            set: function (mixed $value): void {},
        );

        self::assertFalse($obj->__isset('writeonly'));
    }

    public function testGetType(): void
    {
        $type = Type::string();
        $accessor = self::accessor($type);

        self::assertSame($type, $accessor->getType());
    }

    public function testHasPropertyAccessors(): void
    {
        $obj = new TestAccessorClass();
        $stored = null;

        $obj->registerTestAccessor(
            'fullProp',
            Type::nullable(Type::string()),
            get: function () use (&$stored) {
                return $stored;
            },
            set: function (mixed $value) use (&$stored): void {
                $stored = $value;
            },
        );

        self::assertFalse(isset($obj->fullProp));

        $obj->fullProp = 'test';
        self::assertSame('test', $stored);
        self::assertSame('test', $obj->fullProp);
        self::assertTrue(isset($obj->fullProp));
    }

    public function testClassOwnedGetForwardsToTryGet(): void
    {
        $obj = new TestAccessorClassWithOwnGet();

        $obj->registerTestAccessor(
            'hooked',
            Type::string(),
            get: fn(): string => 'from-accessor',
        );

        self::assertSame('from-accessor', $obj->hooked);
        self::assertSame('legacy-value', $obj->legacy);
    }

    public function testUnregisteredPropertyGet(): void
    {
        $obj = new TestAccessorClass();
        $warningTriggered = false;

        \set_error_handler(function (int $errno, string $errstr) use (&$warningTriggered) {
            $warningTriggered = true;
            self::assertSame(\E_USER_WARNING, $errno);
            self::assertStringContainsString('Undefined property', $errstr);
            self::assertStringContainsString('nonexistent', $errstr);
            return true;
        });

        try {
            $result = $obj->__get('nonexistent');
            self::assertNull($result);
            self::assertTrue($warningTriggered, 'Expected E_USER_WARNING to be triggered.');
        } finally {
            \restore_error_handler();
        }
    }

    public function testExplicitBootBeforeAccess(): void
    {
        $obj = new TestAccessorClass();
        $obj->tyhpBootTraits();

        $obj->registerTestAccessor(
            'name',
            Type::string(),
            get: fn(): string => 'booted',
        );

        self::assertSame('booted', $obj->name);
    }

    public function testFinalGetRejectsChildRegistrationAfterParent(): void
    {
        $host = new TestAccessorSubclass();
        $host->registerTestAccessor(
            'locked',
            Type::string(),
            get: fn(): string => 'parent',
            backed: true,
            defaultValue: 'parent',
            declaringClass: TestAccessorClassWithVisibilityHelpers::class,
            finalGet: true,
        );

        $this->expectException(\Error::class);
        $this->expectExceptionMessage(
            'Cannot override final property hook '
            . TestAccessorClassWithVisibilityHelpers::class . '::$locked::get()',
        );
        $host->registerTestAccessor(
            'locked',
            Type::string(),
            get: fn(): string => 'child',
            declaringClass: TestAccessorSubclass::class,
        );
    }

    public function testFinalGetRejectsWhenChildRegistersFirst(): void
    {
        $host = new TestAccessorSubclass();
        $host->registerTestAccessor(
            'locked',
            Type::string(),
            get: fn(): string => 'child',
            declaringClass: TestAccessorSubclass::class,
        );

        $this->expectException(\Error::class);
        $this->expectExceptionMessage(
            'Cannot override final property hook '
            . TestAccessorClassWithVisibilityHelpers::class . '::$locked::get()',
        );
        $host->registerTestAccessor(
            'locked',
            Type::string(),
            get: fn(): string => 'parent',
            backed: true,
            defaultValue: 'parent',
            declaringClass: TestAccessorClassWithVisibilityHelpers::class,
            finalGet: true,
        );
    }

    public function testFinalSetAllowsChildGetOnlyOverride(): void
    {
        // Child-first init: child adds get only; parent then registers final set.
        $host = new TestAccessorSubclass();
        $host->registerTestAccessor(
            'username',
            Type::string(),
            get: fn(): string => \strtoupper((string) $host->getTestBacking('username')),
            backed: true,
            declaringClass: TestAccessorSubclass::class,
        );
        $host->registerTestAccessor(
            'username',
            Type::string(),
            set: static function (mixed $value) use ($host): void {
                $host->setTestBacking('username', \strtolower((string) $value));
            },
            backed: true,
            defaultValue: 'user',
            declaringClass: TestAccessorClassWithVisibilityHelpers::class,
            finalSet: true,
        );

        self::assertSame('USER', $host->username);
        $host->username = 'MixedCase';
        self::assertSame('MIXEDCASE', $host->username);
    }

    public function testPartialSetOnlyOverrideSharesBackingWithInheritedGet(): void
    {
        // Child-first: child overrides set only; parent supplies get + default.
        $host = new TestAccessorSubclass();
        $host->registerTestAccessor(
            'label',
            Type::string(),
            set: static function (mixed $value) use ($host): void {
                $host->setTestBacking('label', \strtoupper((string) $value));
            },
            backed: true,
            declaringClass: TestAccessorSubclass::class,
        );
        $host->registerTestAccessor(
            'label',
            Type::string(),
            get: fn(): string => (string) $host->getTestBacking('label'),
            backed: true,
            defaultValue: 'hi',
            declaringClass: TestAccessorClassWithVisibilityHelpers::class,
        );

        self::assertSame('hi', $host->label);
        $host->label = 'hello';
        self::assertSame('HELLO', $host->label);
    }
}
