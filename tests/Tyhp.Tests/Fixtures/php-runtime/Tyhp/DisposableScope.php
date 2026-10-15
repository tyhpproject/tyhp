<?php

declare(strict_types=1);

namespace Tyhp;

use Tyhp\Contracts\IsDisposable;

/**
 * Test-only minimal scope used by executed emit tests: disposes registered resources in reverse
 * order when the scope object is destroyed.
 */
final class DisposableScope implements IsDisposable
{
    /** @var list<IsDisposable> */
    private array $resources = [];

    private function __construct() {}

    public static function create(): DisposableScope
    {
        return new DisposableScope();
    }

    public function using(IsDisposable $resource): mixed
    {
        $this->resources[] = $resource;

        return $resource;
    }

    public function dispose(): void
    {
        while ($this->resources !== []) {
            $resource = \array_pop($this->resources);
            $resource->dispose();
        }
    }

    public function __destruct()
    {
        $this->dispose();
    }
}
