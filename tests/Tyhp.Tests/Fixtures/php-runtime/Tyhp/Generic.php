<?php

declare(strict_types=1);

namespace Tyhp;

/**
 * Test-only copy of the part of the runtime binder that executed emit tests call:
 * a wrapped first-class callable is the factory's closure over the native callable.
 */
final class Generic
{
    public static function wrapCallable(\Closure $target, \Closure $factory): \Closure
    {
        return $factory($target);
    }
}
