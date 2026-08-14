<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\Deferred;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\PromiseState;
use Tyhp\Exceptions\InvalidPromiseStateException;

final class DeferredTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testResolve(): void
    {
        $deferred = new Deferred();
        $promise = $deferred->getPromise();

        $this->assertSame(PromiseState::Pending, $promise->getState());

        $result = Promise::run(function () use ($deferred) {
            $deferred->resolve(42);
            return Promise::_await($deferred->getPromise());
        });

        $this->assertSame(42, $result);
        $this->assertTrue($promise->isFulfilled());
    }

    public function testReject(): void
    {
        $deferred = new Deferred();
        $promise = $deferred->getPromise();
        $exception = new \RuntimeException('deferred-err');

        try {
            Promise::run(function () use ($deferred, $exception) {
                $deferred->reject($exception);
                return Promise::_await($deferred->getPromise());
            });
            $this->fail('Expected exception was not thrown');
        } catch (\RuntimeException $e) {
            $this->assertSame($exception, $e);
        }

        $this->assertTrue($promise->isFaulted());
        $this->assertSame($exception, $promise->getError());
    }

    public function testDoubleResolveThrows(): void
    {
        $this->expectException(InvalidPromiseStateException::class);

        $deferred = new Deferred();
        $deferred->resolve('first');
        $deferred->resolve('second');
    }

    public function testDoubleRejectThrows(): void
    {
        $this->expectException(InvalidPromiseStateException::class);

        $deferred = new Deferred();
        $deferred->reject(new \RuntimeException('first'));
        $deferred->reject(new \RuntimeException('second'));
    }

    public function testResolveAfterRejectThrows(): void
    {
        $this->expectException(InvalidPromiseStateException::class);

        $deferred = new Deferred();
        $deferred->reject(new \RuntimeException('rejected'));
        $deferred->resolve('value');
    }

    public function testGetPromise(): void
    {
        $deferred = new Deferred();
        $promise = $deferred->getPromise();

        $this->assertInstanceOf(Promise::class, $promise);
        $this->assertSame($promise, $deferred->getPromise());
    }

    public function testDeferredInAsyncContext(): void
    {
        $result = Promise::run(function () {
            $deferred = new Deferred();

            Promise::_async(function () use ($deferred) {
                $deferred->resolve('from-async');
            });

            return Promise::_await($deferred->getPromise());
        });

        $this->assertSame('from-async', $result);
    }
}
