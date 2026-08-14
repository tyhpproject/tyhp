---
title: 'Disposables and the := Operator'
status:
  tier: 1
  story: '11'
  state: complete
---

Tyhp provides deterministic resource management through the IsDisposable and AsyncIsDisposable interfaces, the := (using assignment) operator, and the using block syntax. These features ensure that resources like database connections, file handles, and network sockets are properly cleaned up when they are no longer needed.

## The IsDisposable Interface

Any class that manages resources should implement `\Tyhp\Contracts\IsDisposable` (provided by the `tyhp/core` package). This interface requires a single dispose() method that performs cleanup.

```tyhp
<?tyhp

class DatabaseConnection implements \Tyhp\Contracts\IsDisposable {
    private mixed $handle;

    public function __construct(string $dsn): void {
        $this->handle = \pg_connect($dsn);
    }

    public function query(string $sql): mixed {
        return \pg_query($this->handle, $sql);
    }

    public function dispose(): void {
        \pg_close($this->handle);
    }
}
```

## The AsyncIsDisposable Interface

For resources that require asynchronous cleanup (e.g., closing a connection over the network), implement `\Tyhp\Contracts\AsyncIsDisposable` (provided by the `tyhp/async` package). This interface requires a disposeAsync() method that returns a Promise.

```tyhp
<?tyhp

class AsyncConnection implements \Tyhp\Contracts\AsyncIsDisposable {
    public async function disposeAsync(): void {
        await $this->sendCloseCommand();
        await $this->waitForAcknowledgement();
    }
}
```

## The := Operator (Using Assignment)

The := operator declares a variable as disposable. When the variable leaves scope, its dispose() method is called automatically. This is the simplest way to ensure resources are cleaned up.

```tyhp
<?tyhp

function processData(): void {
    // := marks $db as disposable — it is disposed when it leaves scope
    $db := new DatabaseConnection('host=localhost dbname=mydb');

    $result = $db->query('SELECT * FROM users');
    // ... process result ...

    // $db->dispose() is called automatically when the function returns
}
```

## Compiled PHP Output — DisposableScope

The := operator compiles to a `\Tyhp\DisposableScope` pattern from the `tyhp/async` package. A scope variable is created and resources are registered with it via `$__scope_0->using()`. When the scope variable goes out of scope, PHP's __destruct() fires and disposes all registered resources in reverse order.

```php
<?php

function processData(): void {
    $__scope_0 = \Tyhp\DisposableScope::create();
    $db = $__scope_0->using(new DatabaseConnection('host=localhost dbname=mydb'));

    $result = $db->query('SELECT * FROM users');
    // ... process result ...

    // When $__scope_0 goes out of scope, __destruct() fires
    // and calls $db->dispose() automatically
}
```

## Multiple Disposable Resources

Multiple disposable resources in the same scope share a single DisposableScope. Resources are disposed in reverse order of registration (LIFO — Last In, First Out), matching the behavior of C#'s using statement.

```tyhp
<?tyhp

function transferData(): void {
    $source := new DatabaseConnection($sourceDsn);
    $target := new DatabaseConnection($targetDsn);
    $logger := new FileLogger('/var/log/transfer.log');

    // ... transfer data ...

    // Disposed in reverse order: $logger, $target, $source
}
```

```php
<?php

function transferData(): void {
    $__scope_0 = \Tyhp\DisposableScope::create();
    $source = $__scope_0->using(new DatabaseConnection($sourceDsn));
    $target = $__scope_0->using(new DatabaseConnection($targetDsn));
    $logger = $__scope_0->using(new FileLogger('/var/log/transfer.log'));

    // ... transfer data ...

    // $__scope_0->__destruct() disposes: $logger, $target, $source
}
```

## Nested Disposable Scopes

Each nested scope that contains disposable resources gets its own DisposableScope with a unique suffix (`$__scope_0`, `$__scope_1`, …). Inner scopes dispose independently of outer scopes.

```tyhp
<?tyhp

function process(): void {
    $outer := new ResourceA();

    if ($condition) {
        $inner := new ResourceB();
        // $inner is disposed when this if-block exits
    }

    // $outer is disposed when the function returns
}
```

```php
<?php

function process(): void {
    $__scope_0 = \Tyhp\DisposableScope::create();
    $outer = $__scope_0->using(new ResourceA());

    if ($condition) {
        $__scope_1 = \Tyhp\DisposableScope::create();
        $inner = $__scope_1->using(new ResourceB());
        // $__scope_1->__destruct() fires here
    }

    // $__scope_0->__destruct() fires here
}
```

## The using Block Syntax

For guaranteed deterministic disposal regardless of circular references or GC behavior, Tyhp provides the using block syntax. Unlike the := operator which uses DisposableScope and __destruct(), the using block always compiles to a try/finally block.

```tyhp
<?tyhp

// Single resource using block
using ($db = new DatabaseConnection($dsn)) {
    $result = $db->query('SELECT * FROM users');
    // $db->dispose() is guaranteed in the finally block
}
```

```php
<?php

// Single resource compiles to try/finally
$db = new \DatabaseConnection($dsn);
try {
    $result = $db->query('SELECT * FROM users');
} finally {
    if ($db instanceof \Tyhp\Contracts\IsDisposable) {
        $db->dispose();
    }
}
```

## Multiple Resources in a using Block

Multiple resources in a using block compile to a flat try/finally with null initialization and error collection. Disposal errors are caught individually and aggregated into an AggregateException, ensuring all resources are disposed even if one fails.

```tyhp
<?tyhp

using ($db = new DatabaseConnection($dsn), $cache = new CacheConnection()) {
    // both are disposed in reverse order in finally
}
```

```php
<?php

$db = null;
$cache = null;
try {
    $db = new \DatabaseConnection($dsn);
    $cache = new \CacheConnection();
    // body
} finally {
    $__disposeErrors = [];
    if ($cache instanceof \Tyhp\Contracts\IsDisposable) {
        try { $cache->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
    }
    if ($db instanceof \Tyhp\Contracts\IsDisposable) {
        try { $db->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
    }
    if (!empty($__disposeErrors)) {
        throw new \Tyhp\Exceptions\AggregateException($__disposeErrors, 'One or more errors during disposal');
    }
}
```

## Reverse Order Disposal

Both := and using dispose resources in reverse order (LIFO — Last In, First Out). This ensures that resources are cleaned up in the opposite order they were acquired, which is important when resources depend on each other (e.g., a query depends on a connection).

## Choosing := vs using Block

Both approaches achieve automatic disposal, but they differ in guarantees and syntax:

:::member[:= operator]
Uses DisposableScope + __destruct(). Lighter syntax, works anywhere. Relies on PHP's reference counting for timely disposal. May be delayed by circular references.
:::

:::member[using block]
Uses try/finally. Deterministic disposal guaranteed regardless of circular references or GC behavior. Scopes the resource to the block body.
:::

## Async Disposal

When disposing async resources, DisposableScope automatically detects the context. Inside a Fiber (async context), it uses Promise::_await(). Outside a Fiber, it uses EventLoop::run() to block-wait. The using await syntax provides explicit async disposal in the finally block. There is no dedicated diagnostic for `using await` outside an async function — TYHP4028 (`CheckerAwaitOutsideAsync`) applies to `await` expressions. The emitter still emits `Promise::_await` in the `using await` finally block.

```tyhp
<?tyhp

async function fetchData(): void {
    $conn := new AsyncConnection($url);
    $data = await $conn->fetch('/api/data');
    // $conn->disposeAsync() is awaited when scope exits
}

// using await for explicit async disposal
using await ($conn = new AsyncConnection($url)) {
    $data = await $conn->fetch('/api/data');
}
```

```php
<?php

// using await compiles to try/finally with async dispose
$conn = new \AsyncConnection($url);
try {
    $data = \Tyhp\Promise::_await($conn->fetch('/api/data'));
} finally {
    if ($conn instanceof \Tyhp\Contracts\AsyncIsDisposable) {
        \Tyhp\Promise::_await($conn->disposeAsync());
    }
}
```

## Circular Reference Mitigation

The DisposableScope relies on PHP's __destruct() for disposal, which requires reference counting to work. The compiler handles the most common circular reference pattern automatically: closures that capture $this and are stored as properties on the same object are rewritten to use WeakReference-based captures.

```php
<?php

// Instead of this (creates circular reference):
$this->onReady = function() {
    $this->emit('ready');
};

// The compiler generates this (breaks the cycle):
$__weakSelf = \WeakReference::create($this);
$this->onReady = function() use ($__weakSelf) {
    $__weakSelf->get()?->emit('ready');
};
```

:::tip
When the compiler detects unresolvable circular references between disposable objects (e.g., bidirectional parent-child object graphs), it falls back to try/finally for that specific scope and emits a warning.
:::

## Best Practices

:::tip
Use := for resources that should be auto-disposed when leaving scope — it is the simplest and most concise approach for most cases.
:::

:::tip
Use the using block when you need guaranteed deterministic disposal, especially in code with complex object graphs or circular references.
:::

:::tip
Implement IsDisposable on any class that manages external resources like database connections, file handles, or network sockets.
:::

:::tip
Implement AsyncIsDisposable for resources that require async cleanup (e.g., closing a network connection that requires a server acknowledgement).
:::

:::tip
Make dispose() calls idempotent — safe to call multiple times without side effects.
:::

## Common Mistakes

:::danger
Do not use := with types that don't implement IsDisposable — the compiler reports an error.
:::

:::danger
Do not manually call dispose() on := variables — disposal is automatic and calling it manually can result in double-disposal.
:::

:::danger
Do not mix := operator and using block syntax for the same resource — pick one approach per scope.
:::

:::danger
Do not create circular references between disposable objects without the compiler's WeakReference mitigation — this can delay __destruct() calls.
:::

:::danger
Do not forget that disposal errors are collected into an AggregateException in using blocks — handle them appropriately.
:::

```tyhp
<?tyhp

class NotDisposable {
    public string $value;
}

// ERROR: Right-hand side does not implement IsDisposable
// $x := new NotDisposable();

// ERROR: Do not manually call dispose on := variables
// function bad(): void {
//     $db := new DatabaseConnection($dsn);
//     $db->dispose();  // Wrong! dispose() will be called again automatically
// }
```

## Compiler Errors

- Using := with a type that does not implement IsDisposable or AsyncIsDisposable.
- Using using block with a non-disposable type.
