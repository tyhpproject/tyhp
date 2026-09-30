<?php

declare(strict_types=1);

namespace Tyhp;

final class GenericObject
{
    private ?Type $objectType = null;

    /** @var array<string, array<string, NamedType>> */
    private array $generics = [];

    private bool $bound = false;
    private bool $propertyChecksEnabled = false;

    /** @var array<string, Type> */
    private array $typedProperties = [];

    public function needsInit(): bool
    {
        return !$this->bound;
    }

    public function markBound(): void
    {
        $this->bound = true;
    }

    public function isInitialized(string $declaringClass): bool
    {
        return isset($this->generics[$declaringClass]);
    }

    public function init(string $hostClass, string $declaringClass, NamedType ...$genericArguments): void
    {
        if (!isset($this->generics[$declaringClass])) {
            $this->generics[$declaringClass] = [];
        }

        foreach ($genericArguments as $argument) {
            $parameterName = $argument->getParameterName();
            if (isset($this->generics[$declaringClass][$parameterName])) {
                continue;
            }

            $this->generics[$declaringClass][$parameterName] = $argument;
        }

        $this->objectType ??= Type::generic($hostClass, ...$genericArguments);
    }

    public function initInterface(string $interface, NamedType ...$args): void
    {
    }

    public function setPropertyType(string $property, Type $type): void
    {
        $this->typedProperties[$property] = $type;
    }

    public function enablePropertyChecks(): void
    {
        $this->propertyChecksEnabled = true;
    }

    public function checkProperty(string $property, mixed $value): void
    {
        if (!$this->propertyChecksEnabled) {
            return;
        }

        if (!\array_key_exists($property, $this->typedProperties)) {
            return;
        }

        Type::check($value, $this->typedProperties[$property]);
    }

    public function objectType(): ?Type
    {
        return $this->objectType;
    }

    public function genericType(string $declaringClass, string $parameterName): ?NamedType
    {
        return $this->generics[$declaringClass][$parameterName] ?? null;
    }

    public function resolvedType(string $declaringClass, string $parameterName): Type
    {
        $named = $this->genericType($declaringClass, $parameterName);
        return $named?->getUnderlyingType() ?? Type::mixed();
    }

    public function defaultValue(string $declaringClass, string $parameterName): mixed
    {
        return $this->resolvedType($declaringClass, $parameterName)->defaultValue();
    }
}
