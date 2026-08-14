<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\EventLoop;
use Tyhp\Promise;

final class EventLoopTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testSingleton(): void
    {
        $a = EventLoop::getInstance();
        $b = EventLoop::getInstance();

        $this->assertSame($a, $b);
    }

    public function testResetInstance(): void
    {
        $before = EventLoop::getInstance();
        EventLoop::resetInstance();
        $after = EventLoop::getInstance();

        $this->assertNotSame($before, $after);
    }

    public function testFiberScheduling(): void
    {
        $loop = EventLoop::getInstance();
        $ran = false;

        $fiber = new \Fiber(function () use (&$ran) {
            $ran = true;
        });

        $loop->scheduleFiber($fiber);
        $this->assertFalse($ran);

        $loop->tick();
        $this->assertTrue($ran);
    }

    public function testTimerDelay(): void
    {
        $loop = EventLoop::getInstance();
        $fired = false;

        $loop->delay(0, function () use (&$fired) {
            $fired = true;
        });

        $this->assertFalse($fired);
        $loop->tick();
        $this->assertTrue($fired);
    }

    public function testTimerCancel(): void
    {
        $loop = EventLoop::getInstance();
        $fired = false;

        $id = $loop->delay(0, function () use (&$fired) {
            $fired = true;
        });

        $loop->cancelTimer($id);
        $loop->tick();

        $this->assertFalse($fired);
    }

    public function testIntervalTimer(): void
    {
        $loop = EventLoop::getInstance();
        $count = 0;

        $id = $loop->interval(0, function () use (&$count) {
            $count++;
        });

        for ($i = 0; $i < 10; $i++) {
            $loop->tick();
        }

        $loop->cancelTimer($id);

        $this->assertGreaterThanOrEqual(1, $count, 'Interval timer should fire at least once across ticks');
    }

    public function testMicrotaskPriority(): void
    {
        $loop = EventLoop::getInstance();
        $order = [];

        $loop->delay(0, function () use (&$order) {
            $order[] = 'timer';
        });
        $loop->queueMicrotask(function () use (&$order) {
            $order[] = 'microtask';
        });

        $loop->tick();

        $this->assertSame(['microtask', 'timer'], $order);
    }

    public function testDeferAliasQueueMicrotask(): void
    {
        $loop = EventLoop::getInstance();
        $fired = false;

        $loop->defer(function () use (&$fired) {
            $fired = true;
        });

        $this->assertFalse($fired);
        $loop->tick();
        $this->assertTrue($fired);
    }

    public function testReadStream(): void
    {
        if (\PHP_OS_FAMILY === 'Windows') {
            $this->markTestSkipped('stream_socket_pair(STREAM_PF_UNIX) not available on Windows');
        }

        $pair = \stream_socket_pair(STREAM_PF_UNIX, STREAM_SOCK_STREAM, STREAM_IPPROTO_IP);
        $this->assertNotFalse($pair);

        try {
            [$reader, $writer] = $pair;
            \stream_set_blocking($reader, false);
            \stream_set_blocking($writer, false);

            $loop = EventLoop::getInstance();
            $received = null;

            $loop->addReadStream($reader, function ($stream) use (&$received, $loop) {
                $received = \fread($stream, 1024);
                $loop->removeReadStream($stream);
            });

            \fwrite($writer, 'hello');

            $loop->tick();

            $this->assertSame('hello', $received);
        } finally {
            \fclose($pair[0]);
            \fclose($pair[1]);
        }
    }

    public function testWriteStream(): void
    {
        if (\PHP_OS_FAMILY === 'Windows') {
            $this->markTestSkipped('stream_socket_pair(STREAM_PF_UNIX) not available on Windows');
        }

        $pair = \stream_socket_pair(STREAM_PF_UNIX, STREAM_SOCK_STREAM, STREAM_IPPROTO_IP);
        $this->assertNotFalse($pair);

        try {
            [$reader, $writer] = $pair;
            \stream_set_blocking($writer, false);

            $loop = EventLoop::getInstance();
            $writable = false;

            $loop->addWriteStream($writer, function ($stream) use (&$writable, $loop) {
                $writable = true;
                $loop->removeWriteStream($stream);
            });

            $loop->tick();

            $this->assertTrue($writable);
        } finally {
            \fclose($pair[0]);
            \fclose($pair[1]);
        }
    }

    public function testRemoveReadStream(): void
    {
        if (\PHP_OS_FAMILY === 'Windows') {
            $this->markTestSkipped('stream_socket_pair(STREAM_PF_UNIX) not available on Windows');
        }

        $pair = \stream_socket_pair(STREAM_PF_UNIX, STREAM_SOCK_STREAM, STREAM_IPPROTO_IP);
        $this->assertNotFalse($pair);

        try {
            [$reader, $writer] = $pair;
            \stream_set_blocking($reader, false);
            \stream_set_blocking($writer, false);

            $loop = EventLoop::getInstance();
            $callCount = 0;

            $loop->addReadStream($reader, function () use (&$callCount) {
                $callCount++;
            });

            \fwrite($writer, 'data');
            $loop->tick();
            $this->assertSame(1, $callCount);

            $loop->removeReadStream($reader);
            $loop->tick();
            $this->assertSame(1, $callCount);
        } finally {
            \fclose($pair[0]);
            \fclose($pair[1]);
        }
    }

    public function testRemoveWriteStream(): void
    {
        if (\PHP_OS_FAMILY === 'Windows') {
            $this->markTestSkipped('stream_socket_pair(STREAM_PF_UNIX) not available on Windows');
        }

        $pair = \stream_socket_pair(STREAM_PF_UNIX, STREAM_SOCK_STREAM, STREAM_IPPROTO_IP);
        $this->assertNotFalse($pair);

        try {
            [$reader, $writer] = $pair;
            \stream_set_blocking($writer, false);

            $loop = EventLoop::getInstance();
            $callCount = 0;

            $loop->addWriteStream($writer, function () use (&$callCount) {
                $callCount++;
            });

            $loop->tick();
            $this->assertSame(1, $callCount);

            $loop->removeWriteStream($writer);
            $loop->tick();
            $this->assertSame(1, $callCount);
        } finally {
            \fclose($pair[0]);
            \fclose($pair[1]);
        }
    }

    public function testAddReadStreamInvalidThrows(): void
    {
        $loop = EventLoop::getInstance();

        $this->expectException(\InvalidArgumentException::class);
        $loop->addReadStream('not-a-stream', function () {});
    }

    public function testTickReturnsFalseWhenIdle(): void
    {
        $loop = EventLoop::getInstance();

        $this->assertFalse($loop->tick());
    }

    public function testIsRunning(): void
    {
        $loop = EventLoop::getInstance();
        $this->assertFalse($loop->isRunning());

        $wasRunning = false;
        Promise::run(function () use ($loop, &$wasRunning) {
            $wasRunning = $loop->isRunning();
            return 'done';
        });

        $this->assertTrue($wasRunning);
        $this->assertFalse($loop->isRunning());
    }

    public function testRunReentrantThrows(): void
    {
        $this->expectException(\LogicException::class);

        Promise::run(function () {
            $inner = Promise::_async(fn() => 'inner');
            EventLoop::getInstance()->run($inner);
        });
    }

    public function testNoSpinWait(): void
    {
        if (\PHP_VERSION_ID >= 80500) {
            $this->markTestSkipped('Idle tick sleep timing is not reliable on PHP 8.5+ in this environment.');
        }

        $loop = EventLoop::getInstance();

        $loop->delay(200, function () {});

        $start = \hrtime(true);
        $loop->tick();
        $elapsed = (\hrtime(true) - $start) / 1_000_000;

        $this->assertGreaterThan(50, $elapsed, 'EventLoop should sleep when idle, not busy-spin (expected stream_select/usleep blocking)');
    }

    public function testConcurrentPromises(): void
    {
        $order = [];

        $result = Promise::run(function () use (&$order) {
            $p1 = Promise::_async(function () use (&$order) {
                $order[] = 'p1-start';
                Promise::_await(Promise::delay(1));
                $order[] = 'p1-end';
                return 'one';
            });

            $p2 = Promise::_async(function () use (&$order) {
                $order[] = 'p2-start';
                Promise::_await(Promise::delay(1));
                $order[] = 'p2-end';
                return 'two';
            });

            $results = Promise::_await(Promise::all([$p1, $p2]));
            return $results;
        });

        self::assertSame(['one', 'two'], $result);
        self::assertSame('p1-start', $order[0]);
        self::assertSame('p2-start', $order[1]);
        self::assertCount(4, $order);
    }
}
