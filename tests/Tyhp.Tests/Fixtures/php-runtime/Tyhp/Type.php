<?php

declare(strict_types=1);

namespace Tyhp;

/**
 * Minimal Type implementation for C# emitter tests that execute PHP.
 * Overlay contracts are covered by runtime/packages/test-all-tyhpdef.sh.
 *
 * `is()` for 'struct' only checks `\is_array($value)`; it does not validate field types or
 * required keys the way `\Tyhp\Type::is()` in `runtime/packages/core` does. Emit-and-run cases
 * that exercise struct/union `is` lowering assert on the compiled `Type::struct(...)` /
 * `Type::union(...)` call arguments (see `tests/conformance/emit-and-run/manifest.json`) so a
 * wrong-fields regression is still caught even though this stub would not catch it at runtime.
 * Object-shape `is()` here matches public method/property existence (v1), same contract as core.
 * Callable-shape `is()` here is existence-only (`\is_callable`), same contract as core v1.
 */
class Type implements \Stringable
{
    private static array $singletons = [];

    public function __construct(
        private readonly string $kind,
        private readonly ?string $name = null,
        private readonly array $typeArgs = [],
        private readonly bool $isReadOnly = false,
        private readonly bool $isNullable = false,
        private readonly array $structFields = [],
        private readonly array $structRequiredKeys = [],
        private readonly array $objectShapeMethods = [],
        private readonly array $objectShapeProperties = [],
    ) {
    }

    public static function string(): self
    {
        return self::$singletons['string'] ??= new self('scalar', 'string');
    }

    public static function int(): self
    {
        return self::$singletons['int'] ??= new self('scalar', 'int');
    }

    public static function float(): self
    {
        return self::$singletons['float'] ??= new self('scalar', 'float');
    }

    public static function bool(): self
    {
        return self::$singletons['bool'] ??= new self('scalar', 'bool');
    }

    public static function null(): self
    {
        return self::$singletons['null'] ??= new self('scalar', 'null', isNullable: true);
    }

    public static function void(): self
    {
        return self::$singletons['void'] ??= new self('scalar', 'void');
    }

    public static function mixed(): self
    {
        return self::$singletons['mixed'] ??= new self('scalar', 'mixed');
    }

    public static function never(): self
    {
        return self::$singletons['never'] ??= new self('scalar', 'never');
    }

    public static function array(): self
    {
        return self::$singletons['array'] ??= new self('scalar', 'array');
    }

    public static function object(): self
    {
        return self::$singletons['object'] ??= new self('scalar', 'object');
    }

    public static function callable(): self
    {
        return self::$singletons['callable'] ??= new self('scalar', 'callable');
    }

    public static function iterable(): self
    {
        return self::$singletons['iterable'] ??= new self('scalar', 'iterable');
    }

    public static function resource(): self
    {
        return self::$singletons['resource'] ??= new self('scalar', 'resource');
    }

    public static function generic(string $className, NamedType ...$params): self
    {
        return new self('generic', $className, $params);
    }

    public static function union(self ...$types): self
    {
        return new self('union', null, $types);
    }

    public static function struct(
        string $name,
        array $fields,
        array $requiredKeys = [],
    ): self {
        return new self('struct', $name, [], false, false, $fields, $requiredKeys);
    }

    /**
     * Existence-only object-shape descriptor. Matches the real `\Tyhp\Type::objectShape`
     * contract used by `$x is Shape` emit. Do not add `newInstance` helpers here.
     */
    public static function objectShape(
        string $name,
        array $methods = [],
        array $properties = [],
    ): self {
        return new self(
            'objectShape',
            $name,
            [],
            false,
            false,
            [],
            [],
            $methods,
            $properties,
        );
    }

    /**
     * Existence-only callable-shape descriptor. Matches the real `\Tyhp\Type::callableShape`
     * contract used by `$fn is Predicate` emit. Signatures are not compared.
     */
    public static function callableShape(string $name): self
    {
        return new self('callableShape', $name);
    }

    public static function fromClassName(string $className): self
    {
        return self::$singletons["class:$className"] ??= new self('class', $className);
    }

    public static function nullable(self $type): self
    {
        if ($type->isNullable) {
            return $type;
        }

        if ($type->kind === 'scalar' && ($type->name === 'null' || $type->name === 'mixed')) {
            return $type;
        }

        return new self(
            $type->kind,
            $type->name,
            $type->typeArgs,
            $type->isReadOnly,
            true,
            $type->structFields,
            $type->structRequiredKeys,
            $type->objectShapeMethods,
            $type->objectShapeProperties,
        );
    }

    public static function of(mixed $value): self
    {
        if ($value === null) {
            return self::null();
        }

        $debugType = \get_debug_type($value);

        return match ($debugType) {
            'string' => self::string(),
            'int' => self::int(),
            'float' => self::float(),
            'bool' => self::bool(),
            'array' => self::array(),
            'resource', 'resource (closed)' => self::resource(),
            default => \is_object($value)
                ? self::resolveObjectType($value)
                : self::fromClassName($debugType),
        };
    }

    public static function is(mixed $value, self $type): bool
    {
        if ($type instanceof NamedType) {
            return self::is($value, $type->getUnderlyingType());
        }

        if ($type->isNullable && $value === null) {
            return true;
        }

        return match ($type->kind) {
            'scalar' => self::isScalar($value, $type),
            'class', 'generic' => \is_object($value) && \is_a($value, $type->name ?? ''),
            'union' => self::isUnion($value, $type),
            'struct' => \is_array($value),
            'objectShape' => self::isObjectShape($value, $type),
            'callableShape' => \is_callable($value),
            default => false,
        };
    }

    public static function check(mixed $value, self $type): void
    {
        if (!self::is($value, $type)) {
            throw new Exceptions\IncompatibleTypeException($type, self::of($value));
        }
    }

    public function defaultValue(): mixed
    {
        if ($this->isNullable) {
            return null;
        }

        return match ($this->kind) {
            'scalar' => match ($this->name) {
                'string' => '',
                'int' => 0,
                'float' => 0.0,
                'bool' => false,
                'array', 'iterable' => [],
                default => null,
            },
            default => null,
        };
    }

    public function isNullable(): bool
    {
        return $this->isNullable;
    }

    public function isReadOnly(): bool
    {
        return $this->isReadOnly;
    }

    public function getName(): ?string
    {
        return $this->name;
    }

    public function __toString(): string
    {
        $str = match ($this->kind) {
            'scalar' => $this->name ?? 'unknown',
            'class' => $this->name ?? 'object',
            'generic' => ($this->name ?? 'object')
                . '<' . \implode(', ', \array_map(\strval(...), $this->typeArgs)) . '>',
            'union' => \implode('|', \array_map(\strval(...), $this->typeArgs)),
            'intersection' => \implode('&', \array_map(\strval(...), $this->typeArgs)),
            default => 'unknown',
        };

        if ($this->isNullable) {
            if ($this->kind === 'union') {
                $str .= '|null';
            } elseif ($this->kind !== 'scalar' || $this->name !== 'null') {
                $str = '?' . $str;
            }
        }

        return $str;
    }

    private static function resolveObjectType(object $value): self
    {
        if (\property_exists($value, '__tyhpGeneric')) {
            $bag = $value->__tyhpGeneric ?? null;
            if (\is_object($bag) && \method_exists($bag, 'objectType')) {
                $objectType = $bag->objectType();
                if ($objectType instanceof self) {
                    return $objectType;
                }
            }
        }

        return self::fromClassName($value::class);
    }

    private static function isUnion(mixed $value, self $type): bool
    {
        foreach ($type->typeArgs as $member) {
            if ($member instanceof self && self::is($value, $member)) {
                return true;
            }

            if ($member instanceof NamedType && self::is($value, $member->getUnderlyingType())) {
                return true;
            }
        }

        return false;
    }

    private static function isObjectShape(mixed $value, self $type): bool
    {
        if (!\is_object($value)) {
            return false;
        }

        try {
            $ref = new \ReflectionObject($value);
        } catch (\Throwable) {
            return false;
        }

        foreach ($type->objectShapeMethods as $methodName) {
            try {
                $method = $ref->getMethod($methodName);
            } catch (\Throwable) {
                return false;
            }

            if (!$method->isPublic() || $method->isConstructor()) {
                return false;
            }
        }

        foreach ($type->objectShapeProperties as $propertyName) {
            try {
                $property = $ref->getProperty($propertyName);
            } catch (\Throwable) {
                return false;
            }

            if (!$property->isPublic()) {
                return false;
            }
        }

        return true;
    }

    private static function isScalar(mixed $value, self $type): bool
    {
        return match ($type->name) {
            'string' => \is_string($value),
            'int' => \is_int($value),
            'float' => \is_float($value) || \is_int($value),
            'bool' => \is_bool($value),
            'null', 'void' => $value === null,
            'array' => \is_array($value),
            'object' => \is_object($value),
            'callable' => \is_callable($value),
            'iterable' => \is_iterable($value),
            'mixed' => true,
            default => false,
        };
    }
}
