<?php

declare(strict_types=1);

namespace Tyhp\Contracts;

/**
 * Test-only copy of the runtime disposable contract used by executed emit tests.
 */
interface IsDisposable
{
    public function dispose(): void;
}
