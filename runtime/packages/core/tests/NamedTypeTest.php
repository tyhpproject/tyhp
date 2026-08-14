<?php

declare(strict_types=1);

namespace Tyhp\Tests\Core;

use PHPUnit\Framework\TestCase;
use Tyhp\NamedType;
use Tyhp\Type;

class NamedTypeTest extends TestCase
{
    public function testConstruction(): void
    {
        $underlying = Type::int();
        $named = new NamedType('T', $underlying);

        self::assertInstanceOf(NamedType::class, $named);
        self::assertInstanceOf(Type::class, $named);
    }

    public function testGetParameterName(): void
    {
        $named = new NamedType('TKey', Type::string());

        self::assertSame('TKey', $named->getParameterName());
    }

    public function testGetUnderlyingType(): void
    {
        $underlying = Type::int();
        $named = new NamedType('T', $underlying);

        self::assertSame($underlying, $named->getUnderlyingType());
    }

    public function testAsReadOnly(): void
    {
        $named = new NamedType('T', Type::int());
        self::assertFalse($named->isReadOnly());

        $ro = $named->asReadOnly();
        self::assertTrue($ro->isReadOnly());
        self::assertInstanceOf(NamedType::class, $ro);
        self::assertNotSame($named, $ro);
        self::assertSame('T', $ro->getParameterName());
        self::assertSame($named->getUnderlyingType(), $ro->getUnderlyingType());
    }

    public function testAsReadOnlyAlreadyReadOnly(): void
    {
        $named = new NamedType('T', Type::int(), readOnly: true);
        self::assertTrue($named->isReadOnly());

        $ro = $named->asReadOnly();
        self::assertSame($named, $ro);
    }

    public function testToString(): void
    {
        $named = new NamedType('T', Type::int());
        self::assertSame('int', (string) $named);

        $namedStr = new NamedType('U', Type::string());
        self::assertSame('string', (string) $namedStr);

        $namedNullable = new NamedType('V', Type::nullable(Type::float()));
        self::assertSame('?float', (string) $namedNullable);
    }

    public function testGetName(): void
    {
        $named = new NamedType('T', Type::int());

        // getName() returns the parameter name, not the underlying type's name
        self::assertSame('T', $named->getName());
    }

    public function testNullableInherited(): void
    {
        $nonNullable = new NamedType('T', Type::int());
        self::assertFalse($nonNullable->isNullable());

        $nullable = new NamedType('T', Type::nullable(Type::string()));
        self::assertTrue($nullable->isNullable());
    }

    public function testGetKind(): void
    {
        $named = new NamedType('T', Type::int());

        self::assertSame('named', $named->getKind());
    }
}
