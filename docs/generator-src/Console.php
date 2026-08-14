<?php

declare(strict_types=1);

namespace Tyhp\Docs;

final class Console
{
    public const RED = "\033[0;31m";
    public const GREEN = "\033[0;32m";
    public const YELLOW = "\033[1;33m";
    public const NC = "\033[0m";

    public static function echoStd(string $text): void
    {
        \fwrite(\STDOUT, $text . "\n");
    }

    public static function echoErr(string $text, bool $throw = false): void
    {
        \fwrite(\STDERR, $text . "\n");

        if ($throw) {
            throw new \RuntimeException($text);
        }
    }
}
