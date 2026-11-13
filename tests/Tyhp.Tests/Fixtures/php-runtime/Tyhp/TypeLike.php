<?php

declare(strict_types=1);

namespace Tyhp;

/**
 * Minimal TypeLike for emitter tests. Type implements it so a layout-2 binder
 * parameter (`TypeLike|string|null`) accepts a Type value.
 */
interface TypeLike
{
    public function toType(): Type;

    public function or(TypeLike|string ...$others): Type;
}
