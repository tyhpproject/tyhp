<?php

declare(strict_types=1);

namespace Tyhp\Contracts;

/**
 * Test-only copy of the runtime int-convertible contract used by executed emit tests.
 */
interface IntConvertible extends Convertible
{
    public function __toInt(): int;
}
