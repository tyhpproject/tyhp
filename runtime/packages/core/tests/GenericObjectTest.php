<?php

declare(strict_types=1);

namespace Tyhp\Tests\Core;

use PHPUnit\Framework\TestCase;
use Tyhp\Concerns\BootsTraits;
use Tyhp\Concerns\HasGenerics;
use Tyhp\Exceptions\IncompatibleTypeException;
use Tyhp\NamedType;
use Tyhp\Type;

class TestGenericClass
{
    use HasGenerics;
    use BootsTraits;

    public mixed $item = null;
    public mixed $key = null;

    public function initGeneric(string $declaringClass, NamedType ...$args): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpGeneric->init(static::class, $declaringClass, ...$args);
    }

    public function initInterface(string $interface, NamedType ...$args): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpGeneric->initInterface($interface, ...$args);
    }

    public function needsInit(): bool
    {
        $this->tyhpBootTraits();
        return $this->__tyhpGeneric->needsInit();
    }

    public function markBound(): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpGeneric->markBound();
    }

    public function isInitialized(string $declaringClass): bool
    {
        $this->tyhpBootTraits();
        return $this->__tyhpGeneric->isInitialized($declaringClass);
    }

    public function setPropertyType(string $prop, Type $type): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpGeneric->setPropertyType($prop, $type);
    }

    public function enablePropertyChecks(): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpGeneric->enablePropertyChecks();
    }

    public function checkProperty(string $prop, mixed $value): void
    {
        $this->tyhpBootTraits();
        $this->__tyhpGeneric->checkProperty($prop, $value);
    }
}

class GenericObjectTest extends TestCase
{
    public function testInit(): void
    {
        $obj = new TestGenericClass();
        $namedT = new NamedType('T', Type::int());

        $obj->initGeneric(TestGenericClass::class, $namedT);

        $objectType = $obj->__tyhpGeneric->objectType();
        self::assertNotNull($objectType);
        self::assertSame('generic', $objectType->getKind());
        self::assertSame(TestGenericClass::class, $objectType->getName());
    }

    public function testCheckPropertySkippedUntilEnabled(): void
    {
        $obj = new TestGenericClass();
        $obj->setPropertyType('item', Type::int());

        // Before enable: wrong type is ignored (ctor / promoted window).
        $obj->checkProperty('item', 'not an int');
        self::assertTrue(true);
    }

    public function testCheckPropertyEnforcesAfterEnabled(): void
    {
        $obj = new TestGenericClass();
        $obj->setPropertyType('item', Type::int());
        $obj->enablePropertyChecks();

        $this->expectException(IncompatibleTypeException::class);
        $obj->checkProperty('item', 'not an int');
    }

    public function testCheckPropertyAllowsMatchingType(): void
    {
        $obj = new TestGenericClass();
        $obj->setPropertyType('item', Type::int());
        $obj->enablePropertyChecks();
        $obj->checkProperty('item', 42);
        self::assertTrue(true);
    }

    public function testCheckPropertyNoConstraintIsNoOp(): void
    {
        $obj = new TestGenericClass();
        $obj->enablePropertyChecks();
        $obj->checkProperty('item', 'anything');
        self::assertTrue(true);
    }

    public function testInterfaceGenerics(): void
    {
        $obj = new TestGenericClass();
        $namedTKey = new NamedType('TKey', Type::string());
        $namedTValue = new NamedType('TValue', Type::int());

        $obj->initInterface('SomeInterface', $namedTKey, $namedTValue);

        $objectType = $obj->__tyhpGeneric->objectType();
        self::assertNull($objectType, 'Object type should be null when only interface generics are set.');
    }

    public function testGetGenericType(): void
    {
        $obj = new TestGenericClass();
        $namedT = new NamedType('T', Type::int());
        $namedU = new NamedType('U', Type::string());

        $obj->initGeneric(TestGenericClass::class, $namedT, $namedU);

        $resolvedT = $obj->__tyhpGeneric->genericType(TestGenericClass::class, 'T');
        self::assertNotNull($resolvedT);
        self::assertSame('T', $resolvedT->getParameterName());
        self::assertSame(Type::int(), $resolvedT->getUnderlyingType());

        $resolvedU = $obj->__tyhpGeneric->genericType(TestGenericClass::class, 'U');
        self::assertNotNull($resolvedU);
        self::assertSame('U', $resolvedU->getParameterName());
        self::assertSame(Type::string(), $resolvedU->getUnderlyingType());

        self::assertNull(
            $obj->__tyhpGeneric->genericType(TestGenericClass::class, 'Nonexistent'),
        );
    }

    public function testGetResolvedType(): void
    {
        $obj = new TestGenericClass();
        $obj->initGeneric(TestGenericClass::class, new NamedType('T', Type::int()));

        self::assertSame(
            Type::int(),
            $obj->__tyhpGeneric->resolvedType(TestGenericClass::class, 'T'),
        );
        self::assertSame(
            Type::mixed(),
            $obj->__tyhpGeneric->resolvedType(TestGenericClass::class, 'Missing'),
        );
    }

    public function testDefaultValue(): void
    {
        $obj = new TestGenericClass();
        $obj->initGeneric(TestGenericClass::class, new NamedType('T', Type::int()));

        self::assertSame(0, $obj->__tyhpGeneric->defaultValue(TestGenericClass::class, 'T'));
        self::assertNull($obj->__tyhpGeneric->defaultValue(TestGenericClass::class, 'Missing'));
    }

    /**
     * Two levels of a hierarchy may spell a parameter identically while binding it to different
     * types; keying by declaring class is what keeps them apart.
     */
    public function testGenericsAreScopedToTheDeclaringClass(): void
    {
        $obj = new TestGenericClass();

        $obj->initGeneric('Derived', new NamedType('T', Type::int()));
        $obj->initGeneric('Base', new NamedType('T', Type::string()));

        self::assertSame(
            Type::int(),
            $obj->__tyhpGeneric->genericType('Derived', 'T')?->getUnderlyingType(),
        );
        self::assertSame(
            Type::string(),
            $obj->__tyhpGeneric->genericType('Base', 'T')?->getUnderlyingType(),
        );
    }

    /**
     * First write wins. A generated constructor forwards to its parent's with a plain
     * `parent::__construct(...)`, which re-enters the ancestor's injected init; a later pass
     * carrying declared defaults must not overwrite a binding an earlier pass made from real type
     * arguments. Its absence is silent, so it is pinned here directly.
     */
    public function testInitIsFirstWriteWins(): void
    {
        $obj = new TestGenericClass();

        $obj->initGeneric(TestGenericClass::class, new NamedType('T', Type::int()));
        $obj->initGeneric(TestGenericClass::class, new NamedType('T', Type::mixed()));

        self::assertSame(
            Type::int(),
            $obj->__tyhpGeneric->genericType(TestGenericClass::class, 'T')?->getUnderlyingType(),
            'a later default must not overwrite an established binding',
        );
    }

    public function testNeedsInitGatesOnACompletedChain(): void
    {
        $obj = new TestGenericClass();

        self::assertTrue($obj->needsInit(), 'a fresh object has no bindings yet');

        $obj->initGeneric(TestGenericClass::class, new NamedType('T', Type::int()));

        self::assertTrue(
            $obj->needsInit(),
            'recording a level does not by itself mean the chain completed',
        );

        $obj->markBound();

        self::assertFalse($obj->needsInit());
    }

    public function testIsInitializedIsPerDeclaringClass(): void
    {
        $obj = new TestGenericClass();

        self::assertFalse($obj->isInitialized('Base'));

        $obj->initGeneric('Base', new NamedType('T', Type::int()));

        self::assertTrue($obj->isInitialized('Base'));
        self::assertFalse($obj->isInitialized('Derived'));
    }

    public function testGetObjectType(): void
    {
        $obj = new TestGenericClass();
        self::assertNull($obj->__tyhpGeneric);

        $namedT = new NamedType('T', Type::bool());
        $obj->initGeneric(TestGenericClass::class, $namedT);

        $objectType = $obj->__tyhpGeneric->objectType();
        self::assertNotNull($objectType);
        self::assertSame('generic', $objectType->getKind());
        self::assertSame(TestGenericClass::class, $objectType->getName());
    }

    public function testGetGenericTypeBeforeInit(): void
    {
        $obj = new TestGenericClass();
        $obj->tyhpBootTraits();

        self::assertNull($obj->__tyhpGeneric->genericType(TestGenericClass::class, 'T'));
    }
}
