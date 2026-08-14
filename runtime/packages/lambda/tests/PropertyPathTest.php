<?php

declare(strict_types=1);

namespace Tyhp\Tests\Lambda;

use PHPUnit\Framework\TestCase;
use Tyhp\PropertyPath;

class PropertyPathTest extends TestCase
{
    public function testConstructsWithSegments(): void
    {
        $source = (object) ['user' => (object) ['name' => 'Ada']];
        $path = new PropertyPath(
            'object',
            'string',
            ['user', 'name'],
            fn(object $source): string => $source->user->name,
        );

        self::assertSame(['user', 'name'], $path->getSegments());
        self::assertSame('user.name', $path->getPath());
        self::assertSame('name', $path->getPropertyName());
    }

    public function testGetValueResolvesNestedProperty(): void
    {
        $source = (object) ['user' => (object) ['city' => 'Paris']];
        $path = new PropertyPath(
            'object',
            'string',
            ['user', 'city'],
            fn(object $source): string => $source->user->city,
        );

        self::assertSame('Paris', $path->getValue($source));
    }

    public function testEmptyPathThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);
        new PropertyPath('object', 'mixed', [], fn(): mixed => null);
    }
}
