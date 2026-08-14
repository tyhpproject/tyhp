<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\DisposableHelper;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\Contracts\AsyncIsDisposable;
use Tyhp\Contracts\IsDisposable;
use Tyhp\Exceptions\AggregateException;

final class DisposableHelperTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testDisposeSync(): void
    {
        $a = new MockDisposable();
        $b = new MockDisposable();

        Promise::run(function () use ($a, $b) {
            Promise::_await(DisposableHelper::dispose($a, $b));
        });

        $this->assertTrue($a->disposed);
        $this->assertTrue($b->disposed);
    }

    public function testDisposeLIFOOrder(): void
    {
        $order = [];

        $a = new OrderTrackingDisposable($order, 'A');
        $b = new OrderTrackingDisposable($order, 'B');
        $c = new OrderTrackingDisposable($order, 'C');

        Promise::run(function () use ($a, $b, $c) {
            Promise::_await(DisposableHelper::dispose($a, $b, $c));
        });

        $this->assertSame(['C', 'B', 'A'], $order);
    }

    public function testDisposeAsync(): void
    {
        $a = new MockAsyncDisposable();
        $b = new MockAsyncDisposable();

        Promise::run(function () use ($a, $b) {
            Promise::_await(DisposableHelper::dispose($a, $b));
        });

        $this->assertTrue($a->disposed);
        $this->assertTrue($b->disposed);
    }

    public function testDisposeMixed(): void
    {
        $sync = new MockDisposable();
        $async = new MockAsyncDisposable();

        Promise::run(function () use ($sync, $async) {
            Promise::_await(DisposableHelper::dispose($sync, $async));
        });

        $this->assertTrue($sync->disposed);
        $this->assertTrue($async->disposed);
    }

    public function testDisposeMultipleFailures(): void
    {
        $a = new FailingDisposable('error A');
        $b = new FailingDisposable('error B');

        $warnings = [];
        \set_error_handler(function (int $errno, string $errstr) use (&$warnings): bool {
            $warnings[] = $errstr;
            return true;
        }, E_USER_WARNING);

        try {
            Promise::run(function () use ($a, $b) {
                Promise::_await(DisposableHelper::dispose($a, $b));
            });
            $this->fail('Expected AggregateException was not thrown');
        } catch (AggregateException $e) {
            $inner = $e->getInnerExceptions();
            $this->assertCount(2, $inner);
            $this->assertSame('error B', $inner[0]->getMessage());
            $this->assertSame('error A', $inner[1]->getMessage());
        } finally {
            \restore_error_handler();
        }

        $this->assertCount(2, $warnings);
    }

    public function testDisposeSingleFailure(): void
    {
        $good = new MockDisposable();
        $bad = new FailingDisposable('single fail');

        $warnings = [];
        \set_error_handler(function (int $errno, string $errstr) use (&$warnings): bool {
            $warnings[] = $errstr;
            return true;
        }, E_USER_WARNING);

        try {
            Promise::run(function () use ($good, $bad) {
                Promise::_await(DisposableHelper::dispose($good, $bad));
            });
            $this->fail('Expected RuntimeException was not thrown');
        } catch (\RuntimeException $e) {
            $this->assertSame('single fail', $e->getMessage());
        } finally {
            \restore_error_handler();
        }

        $this->assertTrue($good->disposed);
        $this->assertCount(1, $warnings);
    }

    public function testDisposeEmpty(): void
    {
        $result = Promise::run(function () {
            Promise::_await(DisposableHelper::dispose());
            return 'done';
        });

        $this->assertSame('done', $result);
    }
}

// ── Test helper classes ─────────────────────────────────────────────

class MockDisposable implements IsDisposable
{
    public bool $disposed = false;

    public function dispose(): void
    {
        $this->disposed = true;
    }
}

class FailingDisposable implements IsDisposable
{
    public function __construct(private readonly string $message = 'dispose failed') {}

    public function dispose(): void
    {
        throw new \RuntimeException($this->message);
    }
}

class MockAsyncDisposable implements AsyncIsDisposable
{
    public bool $disposed = false;

    public function disposeAsync(): Promise
    {
        $this->disposed = true;
        return Promise::resolve(null);
    }
}

class OrderTrackingDisposable implements IsDisposable
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
