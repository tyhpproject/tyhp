<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\Deferred;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\PromiseState;
use Tyhp\Exceptions\AsyncContextException;
use Tyhp\Exceptions\InvalidPromiseStateException;
use Tyhp\Exceptions\TimeoutException;

final class PromiseTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testResolve(): void
    {
        $promise = Promise::resolve(42);

        $this->assertTrue($promise->isFulfilled());
        $this->assertSame(PromiseState::Fulfilled, $promise->getState());
        $this->assertSame(42, $promise->getResult());

        $result = $promise->wait();

        $this->assertSame(42, $result);
        $this->assertTrue($promise->isFulfilled());
        $this->assertSame(PromiseState::Fulfilled, $promise->getState());
    }

    public function testReject(): void
    {
        $exception = new \RuntimeException('err');
        $promise = Promise::reject($exception);

        $this->assertTrue($promise->isFaulted());
        $this->assertSame(PromiseState::Rejected, $promise->getState());
        $this->assertSame($exception, $promise->getError());

        try {
            $promise->wait();
            $this->fail('Expected exception was not thrown');
        } catch (\Throwable $e) {
            $this->assertSame($exception, $e);
        }

        $this->assertTrue($promise->isFaulted());
        $this->assertSame(PromiseState::Rejected, $promise->getState());
        $this->assertSame($exception, $promise->getError());
    }

    public function testAsyncAwait(): void
    {
        $result = Promise::run(function () {
            $p = Promise::resolve(42);
            return Promise::_await($p);
        });

        $this->assertSame(42, $result);
    }

    public function testAwaitOutsideFiber(): void
    {
        $this->expectException(AsyncContextException::class);

        $promise = new Deferred();
        Promise::_await($promise->getPromise());
    }

    public function testAwaitAlreadyResolved(): void
    {
        $result = Promise::run(function () {
            $p = Promise::resolve('immediate');
            // resolve() is already fulfilled, so _await returns without scheduling a Fiber.
            return Promise::_await($p);
        });

        $this->assertSame('immediate', $result);
    }

    public function testThenChaining(): void
    {
        $result = Promise::run(function () {
            $p = Promise::resolve(10)
                ->then(fn(int $v) => $v * 2)
                ->then(fn(int $v) => $v + 5);
            return Promise::_await($p);
        });

        $this->assertSame(25, $result);
    }

    public function testCatchHandling(): void
    {
        $result = Promise::run(function () {
            $p = Promise::reject(new \RuntimeException('fail'))
                ->catch(fn(\Throwable $e) => 'recovered: ' . $e->getMessage());
            return Promise::_await($p);
        });

        $this->assertSame('recovered: fail', $result);
    }

    public function testFinallyOnResolve(): void
    {
        $finallyCalled = false;

        $result = Promise::run(function () use (&$finallyCalled) {
            $p = Promise::resolve('ok')
                ->finally(function () use (&$finallyCalled) {
                    $finallyCalled = true;
                });
            return Promise::_await($p);
        });

        $this->assertSame('ok', $result);
        $this->assertTrue($finallyCalled);
    }

    public function testFinallyOnReject(): void
    {
        $finallyCalled = false;
        $caught = null;

        try {
            Promise::run(function () use (&$finallyCalled) {
                $p = Promise::reject(new \RuntimeException('boom'))
                    ->finally(function () use (&$finallyCalled) {
                        $finallyCalled = true;
                    });
                return Promise::_await($p);
            });
        } catch (\RuntimeException $e) {
            $caught = $e;
        }

        $this->assertTrue($finallyCalled);
        $this->assertNotNull($caught);
        $this->assertSame('boom', $caught->getMessage());
    }

    public function testContinueWithOnSuccess(): void
    {
        $result = Promise::run(function () {
            $p = Promise::resolve(100)
                ->continueWith(function (mixed $value, ?\Throwable $error) {
                    return ['value' => $value, 'error' => $error];
                });
            return Promise::_await($p);
        });

        $this->assertSame(100, $result['value']);
        $this->assertNull($result['error']);
    }

    public function testContinueWithOnRejection(): void
    {
        $result = Promise::run(function () {
            $p = Promise::reject(new \RuntimeException('oops'))
                ->continueWith(function (mixed $value, ?\Throwable $error) {
                    return ['value' => $value, 'errorMsg' => $error?->getMessage()];
                });
            return Promise::_await($p);
        });

        $this->assertNull($result['value']);
        $this->assertSame('oops', $result['errorMsg']);
    }

    public function testWait(): void
    {
        $promise = Promise::resolve(99);
        $result = $promise->wait();

        $this->assertSame(99, $result);
    }

    public function testWaitTimeout(): void
    {
        $this->expectException(TimeoutException::class);

        $deferred = new Deferred();
        $deferred->getPromise()->wait(100);
    }

    public function testWithResolvers(): void
    {
        $result = Promise::run(function () {
            $resolvers = Promise::withResolvers();
            $resolvers->resolve(777);
            return Promise::_await($resolvers->promise);
        });

        $this->assertSame(777, $result);
    }

    public function testPromiseOfPromise(): void
    {
        $inner = Promise::resolve('inner-value');
        $outer = Promise::resolve($inner);

        $this->assertSame($inner, $outer);

        $result = $outer->wait();
        $this->assertSame('inner-value', $result);
    }

    public function testGetResultPendingThrows(): void
    {
        $this->expectException(InvalidPromiseStateException::class);

        $deferred = new Deferred();
        try {
            $deferred->getPromise()->getResult();
        } finally {
            $deferred->reject(new \RuntimeException('cleaned up'));
        }
    }

    public function testGetResultRejectedThrows(): void
    {
        $promise = Promise::reject(new \RuntimeException('test-reject'));

        try {
            $promise->wait();
        } catch (\Throwable) {
        }

        try {
            $promise->getResult();
            $this->fail('Expected exception was not thrown');
        } catch (\RuntimeException $e) {
            $this->assertSame('test-reject', $e->getMessage());
        }
    }

    public function testGetState(): void
    {
        $deferred = new Deferred();
        $promise = $deferred->getPromise();

        $this->assertSame(PromiseState::Pending, $promise->getState());

        $result = Promise::run(function () use ($deferred) {
            $deferred->resolve('done');
            return Promise::_await($deferred->getPromise());
        });

        $this->assertSame(PromiseState::Fulfilled, $promise->getState());
        $this->assertSame('done', $result);
    }

    public function testIsCompleted(): void
    {
        $deferred = new Deferred();
        $promise = $deferred->getPromise();

        $this->assertFalse($promise->isCompleted());

        Promise::run(function () use ($deferred) {
            $deferred->resolve('val');
            return Promise::_await($deferred->getPromise());
        });

        $this->assertTrue($promise->isCompleted());
    }

    public function testIsCompletedAfterReject(): void
    {
        $deferred = new Deferred();
        $promise = $deferred->getPromise();

        $this->assertFalse($promise->isCompleted());

        try {
            Promise::run(function () use ($deferred) {
                $deferred->reject(new \RuntimeException('nope'));
                return Promise::_await($deferred->getPromise());
            });
        } catch (\Throwable) {
            // expected
        }

        $this->assertTrue($promise->isCompleted());
    }
}
