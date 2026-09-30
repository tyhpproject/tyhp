<?php

declare(strict_types=1);

namespace Tyhp;

final class NamedType extends Type
{
    public function __construct(
        private readonly string $parameterName,
        private readonly Type $underlyingType,
        bool $readOnly = false,
    ) {
        parent::__construct(
            'named',
            $parameterName,
            [],
            $readOnly,
            $underlyingType->isNullable(),
        );
    }

    public function getParameterName(): string
    {
        return $this->parameterName;
    }

    public function getUnderlyingType(): Type
    {
        return $this->underlyingType;
    }

    public function __toString(): string
    {
        return (string) $this->underlyingType;
    }
}
