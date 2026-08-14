<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\CancellationTokenSource;
use Tyhp\Deferred;
use Tyhp\DisposableScope;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\Contracts\IsDisposable;
use Tyhp\Exceptions\OperationCancelledException;

final class IntegrationTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testNestedAsync(): void
    {
        $result = Promise::run(function () {
            $inner = Promise::_async(function () {
                return Promise::_await(Promise::resolve(10));
            });
            $middle = Promise::_async(function () use ($inner) {
                $val = Promise::_await($inner);
                return $val * 2;
            });
            return Promise::_await($middle);
        });

        $this->assertSame(20, $result);
    }

    /**
     * Verifies cooperative (not preemptive) concurrent access to shared state.
     * Ordering is deterministic through the Fiber scheduler — each async task
     * runs until it yields (via _await), then the next scheduled task resumes.
     */
    public function testConcurrentWithSharedState(): void
    {
        $result = Promise::run(function () {
            $counter = 0;
            $promises = [];
            for ($i = 0; $i < 5; $i++) {
                $promises[] = Promise::_async(function () use (&$counter) {
                    $counter++;
                    return $counter;
                });
            }
            Promise::_await(Promise::all($promises));
            return $counter;
        });

        $this->assertSame(5, $result);
    }

    public function testAsyncWithCancellation(): void
    {
        $this->expectException(OperationCancelledException::class);

        Promise::run(function () {
            $cts = new CancellationTokenSource();
            $token = $cts->getToken();

            Promise::_async(function () use ($cts) {
                Promise::_await(Promise::delay(10));
                $cts->cancel();
            });

            Promise::_await(Promise::delay(5000, $token));
        });
    }

    public function testDisposableInAsync(): void
    {
        $mock = new IntegrationMockDisposable();

        $result = Promise::run(function () use ($mock) {
            $scope = DisposableScope::create();
            $scope->using($mock);
            $scope->dispose();
            return $mock->disposed;
        });

        $this->assertTrue($result);
    }

    public function testComplexWorkflow(): void
    {
        $result = Promise::run(function () {
            $items = [1, 2, 3, 4, 5];
            $results = Promise::_await(Promise::batch($items, function ($item) {
                return Promise::_async(function () use ($item) {
                    Promise::_await(Promise::delay(1));
                    return $item * $item;
                });
            }, 2));
            return $results;
        });

        $this->assertSame([1, 4, 9, 16, 25], $result);
    }

    public function testFromGeneratorIntegration(): void
    {
        $result = Promise::run(function () {
            $gen = (function () {
                $a = yield Promise::resolve(5);
                $b = yield Promise::delay(1)->then(fn() => 10);
                return $a + $b;
            })();
            return Promise::_await(Promise::fromGenerator($gen));
        });

        $this->assertSame(15, $result);
    }

    public function testAsyncWithIO(): void
    {
        if (\PHP_OS_FAMILY === 'Windows') {
            $this->markTestSkipped('stream_socket_pair not available on Windows');
        }

        $result = Promise::run(function () {
            $pair = \stream_socket_pair(\STREAM_PF_UNIX, \STREAM_SOCK_STREAM, 0);
            try {
                \stream_set_blocking($pair[0], false);
                \stream_set_blocking($pair[1], false);

                $writePromise = Promise::_async(function () use ($pair) {
                    \fwrite($pair[1], 'hello from async IO');
                    \fclose($pair[1]);
                });

                $readPromise = Promise::_async(function () use ($pair) {
                    $data = '';
                    $loop = EventLoop::getInstance();

                    $deferred = new Deferred();
                    $loop->addReadStream($pair[0], function ($stream) use (&$data, $loop, $deferred) {
                        $chunk = \fread($stream, 8192);
                        if ($chunk === '' || $chunk === false) {
                            $loop->removeReadStream($stream);
                            $deferred->resolve($data);
                        } else {
                            $data .= $chunk;
                        }
                    });

                    return Promise::_await($deferred->getPromise());
                });

                Promise::_await($writePromise);
                return Promise::_await($readPromise);
            } catch (\Throwable $e) {
                \fclose($pair[0]);
                throw $e;
            }
        });

        self::assertSame('hello from async IO', $result);
    }

    public function testAsyncIterator(): void
    {
        $result = Promise::run(function () {
            $items = [10, 20, 30];
            $index = 0;

            $iterator = new class($items) implements \Tyhp\Contracts\AsyncIterator {
                private int $index = 0;
                public function __construct(private array $items) {}
                public function next(): \Tyhp\Promise {
                    $this->index++;
                    return \Tyhp\Promise::resolve($this->index < \count($this->items));
                }
                public function current(): \Tyhp\Promise {
                    return \Tyhp\Promise::resolve($this->items[$this->index]);
                }
            };

            $collected = [];
            $collected[] = Promise::_await($iterator->current());
            while (Promise::_await($iterator->next())) {
                $collected[] = Promise::_await($iterator->current());
            }

            return $collected;
        });

        self::assertSame([10, 20, 30], $result);
    }
}

// ── Test helper classes ─────────────────────────────────────────────

class IntegrationMockDisposable implements IsDisposable
{
    public bool $disposed = false;

    public function dispose(): void
    {
        $this->disposed = true;
    }
}
