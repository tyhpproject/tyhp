<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\DisposableScope;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\Contracts\IsDisposable;
use Tyhp\Exceptions\AggregateException;

final class DisposableScopeTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testCreate(): void
    {
        $scope = DisposableScope::create();

        $this->assertInstanceOf(DisposableScope::class, $scope);
    }

    public function testUsingReturnsResource(): void
    {
        $scope = DisposableScope::create();
        $resource = new ScopeMockDisposable();

        $returned = $scope->using($resource);

        $this->assertSame($resource, $returned);
    }

    public function testDisposeInLIFOOrder(): void
    {
        $order = [];
        $scope = DisposableScope::create();

        $scope->using(new ScopeOrderTrackingDisposable($order, 'first'));
        $scope->using(new ScopeOrderTrackingDisposable($order, 'second'));
        $scope->using(new ScopeOrderTrackingDisposable($order, 'third'));

        $scope->dispose();

        $this->assertSame(['third', 'second', 'first'], $order);
    }

    public function testDisposeIdempotent(): void
    {
        $callCount = 0;
        $scope = DisposableScope::create();
        $scope->using(new ScopeCountingDisposable($callCount));

        $scope->dispose();
        $scope->dispose();

        $this->assertSame(1, $callCount);
    }

    public function testDisposeMultipleFailuresThrowsAggregate(): void
    {
        $scope = DisposableScope::create();
        $scope->using(new ScopeFailingDisposable('fail A'));
        $scope->using(new ScopeFailingDisposable('fail B'));

        $warnings = [];
        \set_error_handler(function (int $errno, string $errstr) use (&$warnings): bool {
            $warnings[] = $errstr;
            return true;
        }, E_USER_WARNING);

        try {
            $scope->dispose();
            $this->fail('Expected AggregateException was not thrown');
        } catch (AggregateException $e) {
            $inner = $e->getInnerExceptions();
            $this->assertCount(2, $inner);
            $this->assertSame('fail B', $inner[0]->getMessage());
            $this->assertSame('fail A', $inner[1]->getMessage());
        } finally {
            \restore_error_handler();
        }

        $this->assertCount(2, $warnings);
    }

    public function testLoopScopeReassignmentDisposesPrevious(): void
    {
        $resources = [];
        for ($i = 0; $i < 3; $i++) {
            $resources[$i] = new ScopeMockDisposable();
        }

        $scope = null;
        for ($i = 0; $i < 3; $i++) {
            $scope = DisposableScope::create();
            $scope->using($resources[$i]);

            if ($i > 0) {
                $this->assertTrue($resources[$i - 1]->disposed, "Resource $i-1 should be disposed when \$scope is reassigned");
            }
        }
        unset($scope);

        foreach ($resources as $i => $r) {
            $this->assertTrue($r->disposed, "Resource $i should be disposed after loop");
        }
    }

    public function testNestedScopeDisposesInnerBeforeOuter(): void
    {
        $order = [];

        $outer = DisposableScope::create();
        $outer->using(new ScopeOrderTrackingDisposable($order, 'outer'));

        {
            $inner = DisposableScope::create();
            $inner->using(new ScopeOrderTrackingDisposable($order, 'inner'));
            $inner->dispose();
        }

        $outer->dispose();

        $this->assertSame(['inner', 'outer'], $order);
    }

    public function testReleaseSkipsResourceDuringDisposal(): void
    {
        $callCount = 0;
        $scope = DisposableScope::create();
        $resource = new ScopeCountingDisposable($callCount);

        $scope->using($resource);
        $resource->dispose();
        $scope->release($resource);
        $scope->dispose();

        $this->assertSame(1, $callCount, 'Resource should only be disposed once (manually), not again by scope');
    }

    public function testManualDisposeWithReleaseNoDoubleDisposal(): void
    {
        $order = [];
        $scope = DisposableScope::create();

        $first = new ScopeOrderTrackingDisposable($order, 'first');
        $second = new ScopeOrderTrackingDisposable($order, 'second');
        $third = new ScopeOrderTrackingDisposable($order, 'third');

        $scope->using($first);
        $scope->using($second);
        $scope->using($third);

        $second->dispose();
        $scope->release($second);

        $scope->dispose();

        $this->assertSame(['second', 'third', 'first'], $order);
    }

    public function testAutoDisposeOnDestruct(): void
    {
        $mock = new ScopeMockDisposable();

        $scope = DisposableScope::create();
        $scope->using($mock);

        unset($scope);

        $this->assertTrue($mock->disposed);
    }

    public function testDestructSwallowsExceptions(): void
    {
        $warnings = [];
        \set_error_handler(function (int $errno, string $errstr) use (&$warnings): bool {
            $warnings[] = $errstr;
            return true;
        }, E_USER_WARNING);

        try {
            $scope = DisposableScope::create();
            $scope->using(new ScopeFailingDisposable('destruct fail'));

            unset($scope);

            $this->assertNotEmpty($warnings);
            $found = false;
            foreach ($warnings as $w) {
                if (\str_contains($w, 'destruct fail')) {
                    $found = true;
                    break;
                }
            }
            $this->assertTrue($found, 'Expected a warning containing "destruct fail"');
        } finally {
            \restore_error_handler();
        }
    }
}

// ── Test helper classes ─────────────────────────────────────────────

class ScopeMockDisposable implements IsDisposable
{
    public bool $disposed = false;

    public function dispose(): void
    {
        $this->disposed = true;
    }
}

class ScopeFailingDisposable implements IsDisposable
{
    public function __construct(private readonly string $message = 'dispose failed') {}

    public function dispose(): void
    {
        throw new \RuntimeException($this->message);
    }
}

class ScopeOrderTrackingDisposable implements IsDisposable
{
    /** @param list<string> $order */
    public function __construct(
        private array &$order,
        private readonly string $label,
    ) {}

    public function dispose(): void
    {
        $this->order[] = $this->label;
    }
}

class ScopeCountingDisposable implements IsDisposable
{
    public function __construct(private int &$count) {}

    public function dispose(): void
    {
        $this->count++;
    }
}
