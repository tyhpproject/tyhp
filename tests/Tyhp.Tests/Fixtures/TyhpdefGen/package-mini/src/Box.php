<?php

namespace Acme\Widget;

class Box
{
    public function volume(int $w, int $h, int $d): int
    {
        return $w * $h * $d;
    }
}
