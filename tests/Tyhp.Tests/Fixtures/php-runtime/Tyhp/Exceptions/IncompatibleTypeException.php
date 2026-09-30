<?php

declare(strict_types=1);

namespace Tyhp\Exceptions;

use Tyhp\Type;

final class IncompatibleTypeException extends \RuntimeException
{
    public function __construct(
        private readonly Type $expected,
        private readonly Type $actual,
        private readonly ?string $variableOrParameterName = null,
        ?\Throwable $previous = null,
    ) {
        $target = $this->variableOrParameterName !== null
            ? " for \${$this->variableOrParameterName}"
            : '';

        parent::__construct(
            \sprintf(
                'Incompatible type%s: expected %s, got %s.',
                $target,
                (string) $this->expected,
                (string) $this->actual,
            ),
            0,
            $previous,
        );
    }
}
