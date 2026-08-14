<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\CancellationTokenSource;
use Tyhp\Deferred;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\Exceptions\AggregateException;
use Tyhp\Exceptions\OperationCancelledException;
use Tyhp\Exceptions\TimeoutException;

final class CombinatorTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testAll(): void
    {
        $result = Promise::run(function () {
            return Promise::_await(Promise::all([
                'a' => Promise::resolve(1),
                'b' => Promise::resolve(2),
                'c' => Promise::resolve(3),
            ]));
        });

        $this->assertSame(['a' => 1, 'b' => 2, 'c' => 3], $result);
    }

    public function testAllWithRejection(): void
    {
        $this->expectException(\RuntimeException::class);

        Promise::run(function () {
            return Promise::_await(Promise::all([
                Promise::resolve(1),
                Promise::reject(new \RuntimeException('fail')),
            ]));
        });
    }

    public function testWhenAll(): void
    {
        $result = Promise::run(function () {
            return Promise::_await(Promise::whenAll(
                Promise::resolve(10),
                Promise::resolve(20),
                Promise::resolve(30),
            ));
        });

        $this->assertSame([10, 20, 30], $result);
    }

    public function testAllSettled(): void
    {
        $result = Promise::run(function () {
            return Promise::_await(Promise::allSettled([
                Promise::resolve(42),
                Promise::reject(new \RuntimeException('err')),
            ]));
        });

        $this->assertCount(2, $result);

        $this->assertSame('fulfilled', $result[0]->status);
        $this->assertSame(42, $result[0]->value);

        $this->assertSame('rejected', $result[1]->status);
        $this->assertInstanceOf(\RuntimeException::class, $result[1]->reason);
        $this->assertSame('err', $result[1]->reason->getMessage());
    }

    public function testRace(): void
    {
        $result = Promise::run(function () {
            $immediate = Promise::resolve('winner');
            $deferred = new Deferred();

            $raceResult = Promise::_await(Promise::race([$immediate, $deferred->getPromise()]));
            $deferred->resolve('loser');
            return $raceResult;
        });

        self::assertSame('winner', $result);
    }

    public function testRaceEmptyThrows(): void
    {
        $this->expectException(\InvalidArgumentException::class);

        Promise::run(function () {
            return Promise::_await(Promise::race([]));
        });
    }

    public function testWhenAny(): void
    {
        $result = Promise::run(function () {
            return Promise::_await(Promise::whenAny(
                Promise::resolve('winner'),
                Promise::resolve('loser'),
            ));
        });

        $this->assertSame('winner', $result);
    }

    public function testAny(): void
    {
        $result = Promise::run(function () {
            return Promise::_await(Promise::any([
                Promise::reject(new \RuntimeException('nope')),
                Promise::resolve('yes'),
                Promise::resolve('also yes'),
            ]));
        });

        $this->assertSame('yes', $result);
    }

    public function testAnyAllRejected(): void
    {
        $this->expectException(AggregateException::class);

        Promise::run(function () {
            return Promise::_await(Promise::any([
                Promise::reject(new \RuntimeException('a')),
                Promise::reject(new \RuntimeException('b')),
            ]));
        });
    }

    public function testAnyEmptyThrows(): void
    {
        $this->expectException(AggregateException::class);

        Promise::run(function () {
            return Promise::_await(Promise::any([]));
        });
    }

    public function testDelay(): void
    {
        $result = Promise::run(function () {
            return Promise::_await(Promise::delay(10));
        });

        $this->assertNull($result);
    }

    public function testTimeout(): void
    {
        $this->expectException(TimeoutException::class);

        Promise::run(function () {
            $slow = Promise::delay(5000);
            return Promise::_await(Promise::timeout($slow, 100));
        });
    }

    public function testTimeoutSuccess(): void
    {
        $result = Promise::run(function () {
            $fast = Promise::resolve(42);
            return Promise::_await(Promise::timeout($fast, 1000));
        });

        $this->assertSame(42, $result);
    }

    public function testBatch(): void
    {
        $result = Promise::run(function () {
            $items = [1, 2, 3, 4, 5];
            return Promise::_await(Promise::batch($items, function ($item) {
                return Promise::resolve($item * 2);
            }, 2));
        });

        $this->assertSame([2, 4, 6, 8, 10], $result);
    }

    public function testFromGenerator(): void
    {
        $result = Promise::run(function () {
            $gen = (function () {
                $a = yield Promise::resolve(10);
                $b = yield Promise::resolve(20);
                return $a + $b;
            })();
            return Promise::_await(Promise::fromGenerator($gen));
        });

        $this->assertSame(30, $result);
    }

    public function testDelayWithCancellation(): void
    {
        $this->expectException(OperationCancelledException::class);

        Promise::run(function () {
            $cts = new CancellationTokenSource();
            $token = $cts->getToken();

            $delayPromise = Promise::delay(5000, $token);

            $cts->cancel();

            return Promise::_await($delayPromise);
        });
    }
}
