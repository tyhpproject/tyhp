<?php

declare(strict_types=1);

namespace Tyhp\Concerns;

use Tyhp\GenericObject;

trait HasGenerics
{
    use BootsTraits;

    public ?GenericObject $__tyhpGeneric = null;

    public function __bootTrait_Tyhp_Concerns_HasGenerics(): void
    {
        $this->__tyhpGeneric ??= new GenericObject();
    }
}
