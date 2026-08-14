<?php

declare(strict_types=1);

namespace Tyhp\Tests\Core;

use PHPUnit\Framework\TestCase;
use Tyhp\Exceptions\InvalidTypeException;
use Tyhp\NamedType;
use Tyhp\Type;

class TypeTest extends TestCase
{
    public function testScalarTypeSingletons(): void
    {
        $factories = [
            'string', 'int', 'float', 'bool', 'null', 'void',
            'mixed', 'never', 'array', 'object', 'callable',
            'iterable', 'resource',
        ];

        foreach ($factories as $factory) {
            $first = Type::$factory();
            $second = Type::$factory();
            self::assertSame($first, $second, "Type::$factory() should return the same singleton instance.");
        }
    }

    public function testTypeOf(): void
    {
        self::assertSame(Type::null(), Type::of(null));
        self::assertSame(Type::int(), Type::of(42));
        self::assertSame(Type::float(), Type::of(3.14));
        self::assertSame(Type::string(), Type::of('hello'));
        self::assertSame(Type::bool(), Type::of(true));
        self::assertSame(Type::array(), Type::of([1, 2, 3]));
        self::assertSame(Type::fromClassName(\stdClass::class), Type::of(new \stdClass()));

        $resource = \fopen('php://memory', 'r');
        try {
            self::assertNotNull(Type::of($resource));
        } finally {
            \fclose($resource);
        }
    }

    public function testTypeIs(): void
    {
        self::assertTrue(Type::is('hello', Type::string()));
        self::assertFalse(Type::is(42, Type::string()));

        self::assertTrue(Type::is(42, Type::int()));
        self::assertTrue(Type::is(true, Type::bool()));
        self::assertTrue(Type::is(null, Type::null()));
        self::assertTrue(Type::is(new \stdClass(), Type::object()));

        // float widening: int passes float check
        self::assertTrue(Type::is(42, Type::float()));
        self::assertTrue(Type::is(3.14, Type::float()));
        self::assertFalse(Type::is('nope', Type::float()));
    }

    public function testUnionType(): void
    {
        $union = Type::union(Type::int(), Type::string());

        self::assertTrue(Type::is(42, $union));
        self::assertTrue(Type::is('hello', $union));
        self::assertFalse(Type::is(true, $union));

        self::assertTrue(Type::compatible($union, Type::int()));
        self::assertTrue(Type::compatible($union, Type::string()));
        self::assertFalse(Type::compatible($union, Type::bool()));

        self::assertSame('int|string', (string) $union);
    }

    public function testUnionTypeFlattensNested(): void
    {
        $inner = Type::union(Type::int(), Type::string());
        $outer = Type::union($inner, Type::bool());

        self::assertSame('int|string|bool', (string) $outer);
        self::assertTrue(Type::is(42, $outer));
        self::assertTrue(Type::is('hello', $outer));
        self::assertTrue(Type::is(true, $outer));
    }

    public function testUnionTypeTooFewThrows(): void
    {
        $this->expectException(InvalidTypeException::class);
        Type::union(Type::int());
    }

    public function testIntersectionType(): void
    {
        $a = Type::fromClassName(\Countable::class);
        $b = Type::fromClassName(\Traversable::class);
        $intersection = Type::intersection($a, $b);

        self::assertSame('Countable&Traversable', (string) $intersection);

        $obj = new \ArrayObject();
        self::assertTrue(Type::is($obj, $intersection));

        $plain = new \stdClass();
        self::assertFalse(Type::is($plain, $intersection));

        self::assertTrue(Type::compatible($intersection, $intersection));
    }

    public function testIntersectionTypeTooFewThrows(): void
    {
        $this->expectException(InvalidTypeException::class);
        Type::intersection(Type::fromClassName(\Countable::class));
    }

    public function testNullableType(): void
    {
        $nullable = Type::nullable(Type::string());

        self::assertTrue(Type::is(null, $nullable));
        self::assertTrue(Type::is('hello', $nullable));
        self::assertFalse(Type::is(42, $nullable));
        self::assertTrue($nullable->isNullable());
        self::assertSame('?string', (string) $nullable);
    }

    public function testNullableOfNullableReturnsSame(): void
    {
        $nullable = Type::nullable(Type::string());
        $doubleNullable = Type::nullable($nullable);

        self::assertSame($nullable, $doubleNullable);
    }

    public function testNullableOfMixedReturnsMixed(): void
    {
        $result = Type::nullable(Type::mixed());

        self::assertSame(Type::mixed(), $result);
    }

    public function testGenericType(): void
    {
        $tParam = new NamedType('T', Type::int());
        $uParam = new NamedType('U', Type::string());
        $generic = Type::generic('Foo', $tParam, $uParam);

        self::assertSame('Foo<int, string>', (string) $generic);
        self::assertSame('generic', $generic->getKind());
        self::assertSame('Foo', $generic->getName());

        $foundT = $generic->genericParameter('T');
        self::assertNotNull($foundT);
        self::assertSame($tParam, $foundT);

        $foundU = $generic->genericParameter('U');
        self::assertNotNull($foundU);
        self::assertSame($uParam, $foundU);

        self::assertNull($generic->genericParameter('V'));
    }

    public function testCompatibilityMixedAcceptsAll(): void
    {
        self::assertTrue(Type::compatible(Type::mixed(), Type::int()));
        self::assertTrue(Type::compatible(Type::mixed(), Type::string()));
        self::assertTrue(Type::compatible(Type::mixed(), Type::bool()));
        self::assertTrue(Type::compatible(Type::mixed(), Type::null()));
        self::assertTrue(Type::compatible(Type::mixed(), Type::array()));
        self::assertTrue(Type::compatible(Type::mixed(), Type::object()));
    }

    public function testCompatibilityNeverFitsAll(): void
    {
        self::assertTrue(Type::compatible(Type::int(), Type::never()));
        self::assertTrue(Type::compatible(Type::string(), Type::never()));
        self::assertTrue(Type::compatible(Type::bool(), Type::never()));
        self::assertTrue(Type::compatible(Type::array(), Type::never()));
    }

    public function testCompatibilityUnionNarrow(): void
    {
        $union = Type::union(Type::int(), Type::string());

        self::assertTrue(Type::compatible($union, Type::int()));
        self::assertTrue(Type::compatible($union, Type::string()));
        self::assertFalse(Type::compatible($union, Type::bool()));
    }

    public function testCompatibilityStrictNoFloatWidening(): void
    {
        // Unlike Type::is(), compatible() does NOT allow int → float widening
        self::assertFalse(Type::compatible(Type::float(), Type::int()));
        self::assertTrue(Type::compatible(Type::float(), Type::float()));
    }

    public function testReadOnly(): void
    {
        $type = Type::int();
        self::assertFalse($type->isReadOnly());

        $ro = $type->asReadOnly();
        self::assertTrue($ro->isReadOnly());
        self::assertNotSame($type, $ro);
        self::assertSame('int', (string) $ro);

        // Already read-only returns same instance
        $roAgain = $ro->asReadOnly();
        self::assertSame($ro, $roAgain);
    }

    public function testNonNullable(): void
    {
        $nullable = Type::nullable(Type::string());
        self::assertTrue($nullable->isNullable());

        $nonNullable = $nullable->asNonNullable();
        self::assertFalse($nonNullable->isNullable());
        self::assertSame('string', (string) $nonNullable);

        // Already non-nullable returns same instance
        $plain = Type::string();
        self::assertSame($plain, $plain->asNonNullable());
    }

    public function testFromClassName(): void
    {
        $type = Type::fromClassName(\stdClass::class);

        self::assertSame('class', $type->getKind());
        self::assertSame('stdClass', $type->getName());

        $obj = new \stdClass();
        self::assertTrue(Type::is($obj, $type));
        self::assertFalse(Type::is('not an object', $type));

        // Singleton behavior
        self::assertSame($type, Type::fromClassName(\stdClass::class));
    }

    public function testToString(): void
    {
        self::assertSame('int', (string) Type::int());
        self::assertSame('?string', (string) Type::nullable(Type::string()));

        $tParam = new NamedType('T', Type::int());
        $uParam = new NamedType('U', Type::string());
        self::assertSame('Foo<int, string>', (string) Type::generic('Foo', $tParam, $uParam));

        self::assertSame('int|string', (string) Type::union(Type::int(), Type::string()));

        $a = Type::fromClassName('A');
        $b = Type::fromClassName('B');
        self::assertSame('A&B', (string) Type::intersection($a, $b));
    }

    public function testGetKind(): void
    {
        self::assertSame('scalar', Type::int()->getKind());
        self::assertSame('scalar', Type::string()->getKind());
        self::assertSame('scalar', Type::mixed()->getKind());
        self::assertSame('class', Type::fromClassName('Foo')->getKind());
        self::assertSame('union', Type::union(Type::int(), Type::string())->getKind());

        $a = Type::fromClassName('A');
        $b = Type::fromClassName('B');
        self::assertSame('intersection', Type::intersection($a, $b)->getKind());

        $named = new NamedType('T', Type::int());
        self::assertSame('generic', Type::generic('Box', $named)->getKind());
    }

    public function testGetName(): void
    {
        self::assertSame('int', Type::int()->getName());
        self::assertSame('string', Type::string()->getName());
        self::assertSame('mixed', Type::mixed()->getName());
        self::assertSame('MyClass', Type::fromClassName('MyClass')->getName());

        // Composite types have null name
        self::assertNull(Type::union(Type::int(), Type::string())->getName());

        $a = Type::fromClassName('A');
        $b = Type::fromClassName('B');
        self::assertNull(Type::intersection($a, $b)->getName());
    }
}
