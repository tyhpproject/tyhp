## 13. async / await (Fiber/Promise-based, not native PHP; pkg `tyhp/async`)

```tyhp
async function fetchCount(): int { int $v = await loadCountAsync(); return $v; }
```
→
```php
function fetchCount(): \Tyhp\Promise {
    return \Tyhp\Promise::_async(function (): int {
        return \Tyhp\Promise::_await(loadCountAsync());
    });
}
```
- `async` makes the outer return type `\Tyhp\Promise`; `await $x` → `\Tyhp\Promise::_await($x)`,
  valid inside `async` or at application entry-point top level (emitter wraps with `Promise::run`).
- **No async generators** — yield `Promise<T>` from a `Generator` + `Promise::fromGenerator()`.
- `Promise<TReturn extends void|mixed>`. API (`\Tyhp`): `resolve reject all race any allSettled
  whenAll whenAny delay timeout batch run` (blocking sync entry) `then catch finally`; plus
  `EventLoop`, `Deferred<T>`, `CancellationToken(Source)`, `Contracts\AsyncIterator`/`AsyncIterable`.
- **Cancellation:** pass a `CancellationToken` to `run/all/race/any/delay/timeout/batch` (not
  `allSettled`). On cancel, the awaited call throws `\Tyhp\Exceptions\OperationCancelledException`;
  `timeout` throws `TimeoutException`. Use `$token->throwIfCancellationRequested()` in loops.
  `CancellationTokenSource`: `getToken()`, `cancel()`, `cancelAfter(ms)`.
- Unhandled promise rejections surface when the promise is awaited or when `Promise::run()` observes
  the root — there is no global handler.
- `foreach (await $iter as $x)` desugars by operand type:
  - `AsyncIterable<T>` → `$__asyncIter_N = $iter->getAsyncIterator(); while (_await(…->next())) { $x = _await(…->current()); … }`
  - `Promise<Iterable<T>>` → `foreach (_await($iter) as $x)`
  - `Promise<AsyncIterable<T>>` → await then async-iterate; key-value uses `currentKey()`/`currentValue()`
- Application entry points with top-level `await` wrap in `\Tyhp\Promise::run(function() { … });`
  (library projects skip). Async closures/arrows emit as Promise-returning anonymous functions.
