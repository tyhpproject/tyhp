<?php

namespace Acme\Demo;

use Acme\Log\LoggerInterface;

/**
 * A widget.
 *
 * @template T of \Stringable
 * @method string getName()
 * @property-read int $id
 */
class Widget extends \ArrayObject implements LoggerInterface
{
    public const KIND = "widget";

    /** @var array<string, int> */
    public array $counts = [];

    private string $secret = "x";

    public function __construct(
        public string $label,
        protected int $size = 1,
        private bool $hidden = false,
    ) {
    }

    /**
     * Maps items.
     *
     * @param array<string, T> $items
     * @return list<T>
     */
    public function map(array $items): array
    {
        return $items;
    }

    protected function touch(): void
    {
    }

    private function hide(): void
    {
    }

    /**
     * @deprecated Use map()
     */
    public function old(): mixed
    {
        return null;
    }
}

/**
 * Namespaced helper.
 *
 * @param array<int, string> $values
 */
function helper(array $values): int
{
    return 0;
}
