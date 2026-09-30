<?php

declare(strict_types=1);

namespace Tyhp\Concerns;

trait BootsTraits
{
    private bool $__tyhpTraitsBooted = false;

    public function tyhpBootTraits(): void
    {
        if ($this->__tyhpTraitsBooted) {
            return;
        }

        $this->__tyhpTraitsBooted = true;

        $reflection = new \ReflectionClass($this);
        $bootMethods = [];

        foreach ($reflection->getMethods() as $method) {
            $name = $method->getName();
            if (\str_starts_with($name, '__bootTrait_')) {
                $bootMethods[] = $name;
            }
        }

        \sort($bootMethods);

        foreach ($bootMethods as $bootMethod) {
            $this->{$bootMethod}();
        }
    }
}
