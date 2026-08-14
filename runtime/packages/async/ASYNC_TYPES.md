# Tyhp Async Type Definitions — Verification (Story 06 Phase 5)

Authoritative type surface: `package.tyhpdef` (loaded via `package.tyhp.json`).

Runtime implementation: `src/*.php` (compiled from `tyhp_src/*.tyhp`).

## Package discovery

The binder loads this package through Phase 4 package discovery:

- Production: `vendor/tyhp/async/package.tyhp.json`
- Development: `runtime/packages/async/package.tyhp.json`

Verify loading:

```bash
dotnet run --project tyhp.csproj -- lint runtime/packages/async/package.tyhpdef
```

Expected: 0 parse/bind errors (TyhpSpec missing warning is acceptable in dev).

## Promise&lt;TReturn extends void|mixed&gt;

| Requirement | Tyhpdef location | Runtime |
|-------------|------------------|---------|
| Generic class with `TReturn extends void\|mixed` | `Promise<TReturn extends void\|mixed>` | `Promise.php` (untyped PHP) |
| Generic default `= void` | Deferred to Story 28 | `Promise.tyhp` has default |
| `__construct(callable<TReturn>)` | ✓ | ✓ |
| `_async<T>(callable<T>): Promise<T>` | ✓ (tyhpdef uses `Promise<T>`; parameterized `static<…>` banned — TYHP4168) | ✓ (`tyhp_src` author-migrated to `: self<T>` / `new self<T>`) |
| `_await<T>(Promise<T>): T` | ✓ | ✓ |

### Static combinators (async methods, return-last callable generics)

| Method | Tyhpdef | Notes |
|--------|---------|-------|
| `all<T>(array<Promise<T>>): array<T>` | ✓ async | Returns unwrapped array |
| `race<T>(array<Promise<T>>): T` | ✓ async | First settled wins |
| `allSettled`, `any`, `whenAll`, `whenAny` | ✓ | C#/JS-style extras |

### Static factories

| Plan name | Runtime method | Tyhpdef |
|-----------|----------------|---------|
| `resolved<T>` | `resolve<T>` | ✓ `Promise<T>` |
| `rejected<T>` | `reject<T>` | ✓ `Promise<T>` |
| `delay` | `delay` | ✓ async → `void` |
| `timeout<T>` | `timeout` | ✓ async → `T` |
| `batch<TItem, TResult>` | `batch` | ✓ callable return-last on processor |
| `run<T>` | `run` | ✓ blocking sync entry (returns `T`) |
| `fromGenerator` | `fromGenerator` | ✓ async → `mixed` |

### Instance methods (callable return-last on handlers)

| Method | Tyhpdef |
|--------|---------|
| `then<TResult>(?callable<TReturn, TResult>, ?callable<Throwable, TResult>)` | ✓ |
| `catch<TResult>(callable<Throwable, TResult>)` | ✓ |
| `finally(callable<void>)` | ✓ |

## EventLoop

Public API in tyhpdef matches `EventLoop.php`:

- `getInstance()`, `resetInstance()` (internal test hook)
- Fiber scheduling: `scheduleFiber`, `scheduleFiberResume`, `scheduleFiberThrow`
- Timers: `delay`, `cancelTimer`, `interval`
- Stream I/O: `addReadStream`, `addWriteStream`, `removeReadStream`, `removeWriteStream`
- Microtasks: `defer`, `queueMicrotask`
- Loop control: `run`, `runUntilSettled`, `tick`, `stop`, `isRunning`

Note: the plan mentions `start()`; the runtime uses `run(Promise)` as the entry point.

## Async iteration interfaces

Defined in `namespace Tyhp\Contracts`:

| Interface | Methods |
|-----------|---------|
| `AsyncIterator<T>` | `next(): Promise<bool>`, `current(): Promise<T>` |
| `AsyncIterable<T>` | `getAsyncIterator(): AsyncIterator<T>` |
| `AsyncKeyValueIterator<TKey, TValue>` | extends above + `currentKey(): Promise<TKey>`, `currentValue(): Promise<TValue>` |

Emitter desugars `foreach (await $iter as $item)` using `_await` on `next()` and `current()`.

## Async generators (design constraint)

Tyhp does **not** support async generators. Use `Generator<...>` yielding `Promise<T>` values and `Promise::fromGenerator()` to adapt coroutine-style code. Documented in `package.tyhpdef` class docblock.

## Examples cross-reference

`Examples/AsyncAwait.tyhp` uses legacy `TyhpTask` naming from an older design. Current async/await desugars to `\Tyhp\Promise::_async()` / `\Tyhp\Promise::_await()` and returns `\Tyhp\Promise<TReturn>`.

## Tyhpdef vs `.tyhp` source

Factory methods `_async`, `resolve`, and `reject` declare `Promise<T>` in `package.tyhpdef`.
Parameterized `static<…>` is forbidden (TYHP4168); bare `static` remains valid for late-bound
non-parameterized returns. The author migrated `tyhp_src/Promise.tyhp` to `: self<T>` /
`new self<T>` (etc.); keep those sources — no pending keep/revert decision.

## Phase 5 acceptance (Story 06)

| Criterion | Status |
|-----------|--------|
| `package.tyhpdef` contains full `Promise<TReturn extends void\|mixed>` surface | ✓ |
| `_async` / `_await` desugar targets | ✓ |
| Static combinators and factories | ✓ |
| Instance methods (`then`, `catch`, `finally`) with return-last callables | ✓ |
| `EventLoop` public API | ✓ (`run` entry point, not plan's `start`) |
| `AsyncIterator` / `AsyncIterable` / `AsyncKeyValueIterator` | ✓ |
| Async generator constraint documented | ✓ (class docblock + this doc) |
| Parses via `tyhp lint` | ✓ (0 errors) |
| Loaded via `package.tyhp.json` + Phase 4 binder | ✓ (21 tyhpdef files bound when linting) |
| Consistent with runtime | ✓ |

`AggregateException` lives in `tyhp/core` (`Tyhp\Exceptions`); async runtime imports it from there — not duplicated in this package's tyhpdef.

## Callable return-last convention

All `callable<...>` generic parameters in this package follow the Phase 3 convention documented in `Tyhp/TyhpLang/Binder/BuiltIn/README.md`: parameters first, return type last; `void` and `never` require explicit constraint opt-in on the return parameter.
