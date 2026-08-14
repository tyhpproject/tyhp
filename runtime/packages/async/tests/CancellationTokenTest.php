<?php

declare(strict_types=1);

namespace Tyhp\Tests\Async;

use PHPUnit\Framework\TestCase;
use Tyhp\CancellationToken;
use Tyhp\CancellationTokenSource;
use Tyhp\EventLoop;
use Tyhp\Promise;
use Tyhp\Exceptions\OperationCancelledException;

final class CancellationTokenTest extends TestCase
{
    protected function tearDown(): void
    {
        EventLoop::resetInstance();
    }

    public function testInitialState(): void
    {
        $token = new CancellationToken();

        $this->assertFalse($token->isCancellationRequested());
    }

    public function testCancel(): void
    {
        $token = new CancellationToken();
        $token->cancel();

        $this->assertTrue($token->isCancellationRequested());
    }

    public function testCancelFiresCallbacks(): void
    {
        $token = new CancellationToken();
        $fired = false;

        $token->register(function () use (&$fired) {
            $fired = true;
        });

        $token->cancel();

        $this->assertTrue($fired);
    }

    public function testRegisterBeforeCancel(): void
    {
        $token = new CancellationToken();
        $order = [];

        $token->register(function () use (&$order) {
            $order[] = 'cb1';
        });
        $token->register(function () use (&$order) {
            $order[] = 'cb2';
        });

        $token->cancel();

        $this->assertSame(['cb1', 'cb2'], $order);
    }

    public function testRegisterAfterCancel(): void
    {
        $token = new CancellationToken();
        $token->cancel();

        $fired = false;
        $token->register(function () use (&$fired) {
            $fired = true;
        });

        $this->assertTrue($fired);
    }

    public function testDeregistration(): void
    {
        $token = new CancellationToken();
        $fired = false;

        $deregister = $token->register(function () use (&$fired) {
            $fired = true;
        });

        $deregister();
        $token->cancel();

        $this->assertFalse($fired);
    }

    public function testThrowIfNotCancelled(): void
    {
        $this->expectNotToPerformAssertions();

        $token = new CancellationToken();

        $token->throwIfCancellationRequested();
    }

    public function testThrowIfCancelled(): void
    {
        $token = new CancellationToken();
        $token->cancel();

        $this->expectException(OperationCancelledException::class);
        $token->throwIfCancellationRequested();
    }

    public function testNoneToken(): void
    {
        $a = CancellationToken::none();
        $b = CancellationToken::none();

        $this->assertSame($a, $b);
        $this->assertFalse($a->isCancellationRequested());
    }

    public function testNoneTokenCancelNoOp(): void
    {
        $token = CancellationToken::none();
        $token->cancel();

        $this->assertFalse($token->isCancellationRequested());
    }

    public function testSourceGetToken(): void
    {
        $source = new CancellationTokenSource();
        $token = $source->getToken();

        $this->assertInstanceOf(CancellationToken::class, $token);
        $this->assertSame($token, $source->getToken());
    }

    public function testSourceCancel(): void
    {
        $source = new CancellationTokenSource();
        $token = $source->getToken();

        $this->assertFalse($token->isCancellationRequested());
        $source->cancel();
        $this->assertTrue($token->isCancellationRequested());
        $this->assertTrue($source->isCancellationRequested());
    }

    public function testSourceAutoCancel(): void
    {
        $source = new CancellationTokenSource(50);
        $token = $source->getToken();

        $this->assertFalse($token->isCancellationRequested());

        Promise::run(function () use ($token) {
            Promise::_await(Promise::delay(250));
        });

        $this->assertTrue($token->isCancellationRequested());
    }

    public function testSourceDispose(): void
    {
        $source = new CancellationTokenSource();
        $source->dispose();

        $source->cancel();
        $this->assertFalse($source->isCancellationRequested());
    }

    public function testSourceCancelAfter(): void
    {
        $source = new CancellationTokenSource();
        $token = $source->getToken();

        $source->cancelAfter(50);

        $this->assertFalse($token->isCancellationRequested());

        Promise::run(function () use ($token) {
            Promise::_await(Promise::delay(250));
            return $token->isCancellationRequested();
        });

        $this->assertTrue($token->isCancellationRequested());
    }

    public function testSourceCancelAfterNegativeThrows(): void
    {
        $source = new CancellationTokenSource();

        $this->expectException(\InvalidArgumentException::class);
        $source->cancelAfter(-1);
    }

    public function testSourceDoubleDispose(): void
    {
        $this->expectNotToPerformAssertions();

        $source = new CancellationTokenSource();
        $source->dispose();
        $source->dispose();
    }

    public function testDelayWithCancellation(): void
    {
        $this->expectException(\Tyhp\Exceptions\OperationCancelledException::class);

        Promise::run(function () {
            $source = new CancellationTokenSource();
            $source->cancel();

            Promise::_await(Promise::delay(5000, $source->getToken()));
        });
    }
}
