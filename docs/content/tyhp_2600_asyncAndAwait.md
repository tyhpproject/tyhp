---
title: 'Async and Await in Tyhp'
status:
  tier: 0
  story: '04'
  state: complete
---

Tyhp provides first-class async and await keywords for asynchronous programming. Async functions return Promise<T> values, and await suspends execution until a promise resolves, returning the resolved value. Under the hood, the implementation uses PHP Fibers for cooperative scheduling, with a fiber-based event loop that supports real non-blocking I/O via stream_select(). The runtime is provided by the tyhp/async Composer package.

## Async Function Declaration

An async function is declared by adding the async keyword before function. The declared return type is the resolved value type — the compiler automatically wraps it in Promise<T>. You write the return type as if the function were synchronous, and the compiler handles the Promise wrapping.

```tyhp
<?tyhp

// Async standalone function — declared return type is User,
// actual return type is Promise<User>
async function fetchUser(int $id): User {
    $data = await $http->get("/api/users/{$id}");
    return new User($data);
}

// Async method in a class
class UserService {
    public async function getUser(int $id): User {
        return await $this->repository->find($id);
    }

    // Async void — returns Promise<void>
    public async function deleteUser(int $id): void {
        await $this->repository->delete($id);
    }
}
```

```php
<?php

// async function fetchUser(int $id): User compiles to:
function fetchUser(int $id): \Tyhp\Promise {
    return \Tyhp\Promise::_async(function () use ($id): User {
        $data = \Tyhp\Promise::_await($http->get("/api/users/{$id}"));
        return new User($data);
    });
}

// async methods compile similarly:
class UserService {
    public function getUser(int $id): \Tyhp\Promise {
        return \Tyhp\Promise::_async(function () use ($id): User {
            return \Tyhp\Promise::_await($this->repository->find($id));
        });
    }

    public function deleteUser(int $id): \Tyhp\Promise {
        return \Tyhp\Promise::_async(function () use ($id): void {
            \Tyhp\Promise::_await($this->repository->delete($id));
        });
    }
}
```

## Await Expression

The await keyword is a unary prefix operator that suspends the current async function until the given Promise<T> resolves, then returns the resolved value of type T. It can only be used inside an async function. If the promise rejects, the exception is thrown at the await point.

```tyhp
<?tyhp

async function processOrder(int $orderId): OrderResult {
    // await unwraps Promise<Order> to Order
    Order $order = await $this->orderRepo->find($orderId);

    // await unwraps Promise<PaymentResult> to PaymentResult
    PaymentResult $payment = await $this->paymentService->charge($order);

    // Multiple awaits can be chained sequentially
    await $this->notificationService->send($order->customerId, 'Order processed');

    return new OrderResult($order, $payment);
}
```

## The Promise<T> Type

The Promise<T> class is provided by the tyhp/async Composer package. It represents an asynchronous computation that eventually resolves to a value of type T. The generic parameter T uses the constraint extends void|mixed, allowing Promise<void> for async functions that return nothing. Unparameterized Promise defaults to Promise<void>.

## Promise Combinators

Promise provides static combinator methods for coordinating multiple async operations.

:::member[Promise::all(array<Promise<T>> $promises): array<T>]
Waits for ALL promises to resolve. Returns an array of resolved values in the same order. If any promise rejects, the entire all() rejects with that error.
:::

:::member[Promise::race(array<Promise<T>> $promises): T]
Returns the value of the FIRST promise to settle (resolve or reject). Other promises continue running but their results are discarded.
:::

:::member[Promise::resolved(T $value): Promise<T>]
Creates a pre-resolved promise with the given value. Useful for returning synchronous values from async interfaces.
:::

:::member[Promise::rejected(Throwable $error): Promise<T>]
Creates a pre-rejected promise with the given error.
:::

:::member[Promise::delay(int $ms): void]
An async method that resolves after the specified number of milliseconds. Useful for implementing backoff, throttling, or timeouts.
:::

:::member[Promise::timeout(Promise<T> $promise, int $ms): T]
Races a promise against a timeout. If the promise does not resolve within the specified milliseconds, a TimeoutException is thrown.
:::

:::member[Promise::batch(array<TItem> $items, callable $processor, int $concurrency = 5): array<TResult>]
Processes an array of items through an async processor function with a configurable concurrency limit. Returns an array of results.
:::

:::member[Promise::run(callable $fn): T]
Runs a callable inside the event loop. Used at the lowest async boundary to start the event loop. This is the entry point for async code from synchronous contexts.
:::

```tyhp
<?tyhp

// Run multiple operations in parallel with Promise::all()
async function fetchDashboard(int $userId): Dashboard {
    array $results = await Promise::all([
        $this->fetchProfile($userId),
        $this->fetchOrders($userId),
        $this->fetchNotifications($userId)
    ]);

    return new Dashboard($results[0], $results[1], $results[2]);
}

// Race: first to resolve wins
async function fetchWithFallback(string $url): Response {
    return await Promise::race([
        $this->primaryApi->get($url),
        $this->fallbackApi->get($url)
    ]);
}

// Delay for backoff
async function retryWithBackoff(int $attempt): Data {
    await Promise::delay($attempt * 1000);
    return await $this->fetchData();
}

// Batch processing with concurrency control
async function processAll(array<Item> $items): array<Result> {
    return await Promise::batch(
        $items,
        async fn(Item $item): Result => await $this->process($item),
        concurrency: 5
    );
}

// Timeout: throw if not resolved within 5 seconds
async function fetchWithTimeout(string $url): Response {
    return await Promise::timeout($this->http->get($url), 5000);
}
```

## Instance Methods: then, catch, finally

Promise instances support chaining with then(), catch(), and finally() for callback-based composition.

```tyhp
<?tyhp

async function withHandlers(): string {
    string $result = await $this->fetchData()
        ->then(fn(Data $d): string => $d->format())
        ->catch(fn(\Throwable $e): string => 'fallback')
        ->finally(fn(): void => $this->cleanup());

    return $result;
}
```

## Async Closures and Arrow Functions

The async keyword can be applied to closures and arrow functions, creating async lambdas that return Promise<T>.

```tyhp
<?tyhp

// Async closure
$handler = async function(Request $req): Response {
    User $user = await $this->auth->getUser($req);
    return new Response($user);
};

// Async arrow function
$fetch = async fn(int $id): User => await $repo->find($id);

// Async closures as callbacks
$results = await Promise::all(
    \array_map(
        async fn(int $id): User => await $this->fetchUser($id),
        $userIds
    )
);
```

```php
<?php

// Async closure compiles to closure returning Promise
$handler = function(Request $req): \Tyhp\Promise {
    return \Tyhp\Promise::_async(function () use ($req): Response {
        $user = \Tyhp\Promise::_await($this->auth->getUser($req));
        return new Response($user);
    });
};

// Async arrow function compiles similarly
$fetch = function(int $id) use ($repo): \Tyhp\Promise {
    return \Tyhp\Promise::_async(function () use ($id, $repo): User {
        return \Tyhp\Promise::_await($repo->find($id));
    });
};
```

## Async Overloads

Tyhp supports async function overloads, allowing a function to have both synchronous and asynchronous signatures. The compiler selects the appropriate overload based on the calling context.

```tyhp
<?tyhp

class DataService {
    // Synchronous overload
    public function process(Data $d): Result {
        return $this->doProcess($d);
    }

    // Async overload
    public async function process(Data $d): Promise<Result> {
        await $this->validate($d);
        return $this->doProcess($d);
    }
}
```

## Error Handling in Async Functions

Errors in async functions work naturally with try/catch. When an awaited promise rejects, the exception is thrown at the await point, just like a synchronous exception. This makes async error handling feel identical to synchronous error handling.

```tyhp
<?tyhp

async function safeFetch(string $url): ?Data {
    try {
        return await $http->get($url);
    } catch (\Throwable $e) {
        $this->logger->error('Fetch failed', ['error' => $e->getMessage()]);
        return null;
    }
}

// Errors propagate through await chains
async function pipeline(): Result {
    try {
        Data $data = await $this->fetchData();
        Data $validated = await $this->validate($data);
        return await $this->transform($validated);
    } catch (ValidationException $e) {
        return Result::failed($e->getMessage());
    } catch (\Throwable $e) {
        throw new ProcessingException('Pipeline failed', 0, $e);
    }
}
```

## Event Loop and Promise::run()

The event loop is managed by the \Tyhp\EventLoop class from the tyhp/async package. At the lowest async call boundary — the first non-async function that calls an async function — the event loop is auto-started via Promise::run(). The event loop uses stream_select() for non-blocking I/O and supports timers, deferred operations, and fiber scheduling.

```tyhp
<?tyhp

// Entry point: synchronous context calling into async code
function main(): void {
    // Promise::run() starts the event loop and blocks until complete
    Result $result = Promise::run(async function(): Result {
        Data $data = await fetchData();
        return await processData($data);
    });

    echo $result->__toString();
}
```

## Async Iteration

Tyhp supports async iteration via foreach (await $expr as $item). The expression can be an AsyncIterable<T>, a Promise<Iterable<T>>, or a Promise<AsyncIterable<T>>. Each case compiles differently — AsyncIterable uses a while-loop with awaited next()/current() calls, while Promise<Iterable<T>> resolves the promise first then iterates synchronously.

```tyhp
<?tyhp

// Case 1: AsyncIterable<T> — true async iteration
// Each element is awaited individually as it becomes available
async function processMessages(MessageQueue $queue): void {
    foreach (await $queue->messagesAsync() as Message $message) {
        await $this->handle($message);
    }
}

// Case 2: Promise<Iterable<T>> — resolve then iterate
// The entire collection is fetched first, then iterated synchronously
async function processAll(ApiClient $api): void {
    foreach (await $api->fetchAllAsync() as Item $item) {
        $this->process($item);
    }
}

// Key-value async iteration
async function processKeyValues(AsyncIterable $source): void {
    foreach (await $source as string $key => mixed $value) {
        echo "{$key}: {$value}";
    }
}
```

```php
<?php

// Case 1: AsyncIterable<T> compiles to while-loop with _await:
$__asyncIter_1 = $queue->messagesAsync()->getAsyncIterator();
while (\Tyhp\Promise::_await($__asyncIter_1->next())) {
    $message = \Tyhp\Promise::_await($__asyncIter_1->current());
    \Tyhp\Promise::_await($this->handle($message));
}

// Case 2: Promise<Iterable<T>> compiles to resolve then foreach:
foreach (\Tyhp\Promise::_await($api->fetchAllAsync()) as $item) {
    $this->process($item);
}

// Key-value compiles with currentKey() and currentValue():
$__asyncIter_2 = $source->getAsyncIterator();
while (\Tyhp\Promise::_await($__asyncIter_2->next())) {
    $key = \Tyhp\Promise::_await($__asyncIter_2->currentKey());
    $value = \Tyhp\Promise::_await($__asyncIter_2->currentValue());
    echo "{$key}: {$value}";
}
```

## Cancellation

The tyhp/async package provides CancellationToken and CancellationTokenSource for cooperative cancellation of async operations. CancellationTokenSource implements IsDisposable and can be used with the := operator for automatic disposal.

```tyhp
<?tyhp

async function fetchWithTimeout(string $url): Data {
    // CancellationTokenSource auto-cancels after 5000ms
    $cts := new CancellationTokenSource(timeout: 5000);
    return await $http->get($url, $cts->token);
    // CancellationTokenSource is disposed when scope exits
}

async function fetchWithManualCancel(string $url): ?Data {
    $cts := new CancellationTokenSource();

    try {
        return await $http->get($url, $cts->token);
    } catch (\Throwable $e) {
        $cts->cancel(); // manually cancel on error
        return null;
    }
}
```

## Awaiting in Loops

The await keyword works naturally inside loops. Each iteration suspends and resumes independently.

```tyhp
<?tyhp

async function fetchSequentially(array<string> $urls): array<Response> {
    array<Response> $results = [];
    foreach ($urls as string $url) {
        $results[] = await $http->get($url);
    }
    return $results;
}

// For parallel execution, use Promise::all() instead:
async function fetchInParallel(array<string> $urls): array<Response> {
    return await Promise::all(
        \array_map(
            async fn(string $url): Response => await $http->get($url),
            $urls
        )
    );
}
```

## Best Practices

:::tip
Use await to unwrap promises — it provides clean, sequential-looking code that is easier to read and debug than callback chains.
:::

:::tip
Use Promise::all() for independent operations that can run concurrently. This significantly improves performance compared to sequential awaits.
:::

:::tip
Use Promise::batch() with a concurrency limit when processing large collections to avoid overwhelming external services or running out of memory.
:::

:::tip
Always handle errors in async functions with try/catch around await expressions. Unhandled rejections in async functions propagate silently.
:::

:::tip
Use CancellationToken with the := operator for timeout and cancellation scenarios. The disposal mechanism ensures tokens are cleaned up when the scope exits.
:::

## Common Mistakes

:::danger
Using await outside of an async function. The compiler reports error 4028: "await can only be used inside an async function." Wrap the calling code in an async function or use Promise::run() at the entry point.
:::

:::danger
Forgetting to await a promise (fire-and-forget). An unawaited promise executes but errors are silently lost. Always await promises or explicitly handle them with then()/catch().
:::

:::danger
Using blocking I/O inside async functions. Blocking calls (like synchronous file_get_contents()) block the entire event loop, preventing other fibers from running. Use the event loop's non-blocking I/O facilities instead.
:::

:::danger
Mixing raw Fiber usage with async/await. The tyhp/async event loop manages fibers internally — creating and managing fibers directly can interfere with the event loop's scheduling.
:::

:::danger
Iterating an AsyncIterable<T> without await in the foreach. The compiler reports an error — async iterables must use foreach (await $expr as $item) inside an async function.
:::

## Compiler Errors

```tyhp
<?tyhp

// ERROR 4028: await can only be used inside an async function
function notAsync(): int {
    // return await somePromise();  // Compiler error!
    return 0;
}

// ERROR: async function must have Promise-compatible return type
// async function bad(): int {  // Error: declared return is int,
//     return 42;                 // but async wraps in Promise<int>
// }
// FIX: The return type IS the unwrapped type — Tyhp handles wrapping
async function good(): int {
    return 42; // OK: returns Promise<int>, declared as int
}

// ERROR: Cannot iterate AsyncIterable<T> synchronously
function badIteration(AsyncIterable<Message> $msgs): void {
    // foreach ($msgs as $m) {}  // Compiler error!
}
// FIX: Use await in foreach inside async function
async function goodIteration(AsyncIterable<Message> $msgs): void {
    foreach (await $msgs as Message $m) {
        await $this->handle($m);
    }
}
```

:::note
The tyhp/async Composer package is automatically added as a dependency to your output project when the compiler detects usage of async/await keywords or disposable features. You do not need to manually install it.
:::
