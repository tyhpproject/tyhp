# Implementation Plan: Story 24.1 — Promise Interop

> **Roadmap position:** Story 24.1 — **Tier 3 — Advanced** (additive sub-story, numbered between **24** and **25**). Independent of the optimizer. Does **not** gate Story 25.
> **Direct dependencies (new numbering):** the shipped `tyhp/async` runtime (`\Tyhp\Promise`, `\Tyhp\Deferred`, `\Tyhp\EventLoop`, `CancellationToken`). No dependency on Stories 23 or 24.
> **New story:** adopt foreign thenables into `\Tyhp\Promise`, export `\Tyhp\Promise` as the promise types common libraries typehint, and (optionally) run Tyhp fibers on one foreign event loop. Recommended Composer packages; nothing new is required to use `async` / `await`.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`. This story adds runtime exceptions and docs, not checker diagnostics. See `ROADMAP.md` for the tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-28
> **Status:** Design locked. Implement from this plan; do not reopen the loop model unless a spike in Phase 4 or 5 fails closed as written.
> **Prerequisites:** `runtime/packages/async` (`Promise.tyhp`, `Deferred.tyhp`, `EventLoop.tyhp`).
> **Consumers:** application code that `await`s Guzzle, HTTPlug, ReactPHP, or Amp values; libraries that must return `\Http\Promise\Promise`, `\GuzzleHttp\Promise\PromiseInterface`, or `\React\Promise\PromiseInterface`.

---

## Table of Contents

- [Summary](#summary)
- [Why this is confusing today](#why-this-is-confusing-today)
- [Decisions (locked)](#decisions-locked)
- [What already exists (do not reinvent)](#what-already-exists-do-not-reinvent)
- [Interop matrix](#interop-matrix)
- [What must be avoided](#what-must-be-avoided)
- [Inbound: `Promise::from`](#inbound-promisefrom)
- [Guest pumps](#guest-pumps)
- [Outbound packages (recommended, not required)](#outbound-packages-recommended-not-required)
- [One loop owns the thread](#one-loop-owns-the-thread)
- [Concurrent work is not threads](#concurrent-work-is-not-threads)
- [User-facing contract (docs phase copies this)](#user-facing-contract-docs-phase-copies-this)
- [Phases](#phases)
- [Out of scope](#out-of-scope)
- [Cross-story references](#cross-story-references)

---

## Summary

`async` / `await` in Tyhp always mean `\Tyhp\Promise` and `\Tyhp\EventLoop`. Guzzle, php-http, ReactPHP, and Amp each ship a different promise (or future) and, where they have one, a different scheduler. Users should not have to keep those types in their heads at every call.

This story adds a boundary:

- **In** — `Promise::from($thenable)` returns a `\Tyhp\Promise` you can `await`. Lives in `tyhp/async`. No new Composer requirement.
- **Out** — small recommended packages wrap a `\Tyhp\Promise` in the interface a library typehints. `tyhp/async` only `suggest`s them.
- **Same thread** — the Tyhp loop stays the default owner. A recommended package may *replace* that owner with ReactPHP's loop or Revolt. Two owners never run together. Adopting a thenable suspends one fiber and lets the owner keep turning so other Tyhp work, timers, streams, and guest pumps make progress.

Prophecy's `PromiseInterface` is not part of this. It is a test-double return hook.

---

## Why this is confusing today

| Spelling the user sees | What it actually is |
| --- | --- |
| `await $x` / `async function` | `\Tyhp\Promise`. Fiber on `\Tyhp\EventLoop` (`stream_select`, timers, microtasks). |
| `\GuzzleHttp\Promise\PromiseInterface` | Promises/A+ plus a task queue. `wait(bool $unwrap)` runs a wait function, then `GuzzleHttp\Promise\Utils::queue()->run()`. Rejection reasons are any value. |
| `\React\Promise\PromiseInterface` | Promises/A+. `then` / `catch` / `finally` / `cancel`. Rejection reasons are `\Throwable`. No `wait()`. I/O progress needs `react/event-loop`. |
| `\Http\Promise\Promise` | The small HTTP interop interface: `then`, `getState`, `wait(bool $unwrap)`. Rejection reasons are `\Throwable`. No scheduler of its own. |
| `\Amp\Future` | Fiber future on Revolt. `await` / `map` / `catch` / `finally`. No `then()`. |
| `\Prophecy\Promise\PromiseInterface` | `execute(array $args, ObjectProphecy, MethodProphecy)`. Not async. |

`\Tyhp\Promise::doResolve()` unwraps only another `\Tyhp\Promise`. `Promise::resolve($guzzlePromise)` fulfills with that object as the value. `await` accepts only `\Tyhp\Promise`. A foreign promise left in user code will not suspend a Tyhp fiber, and a Tyhp promise handed to Guzzle or React will not follow their scheduler by itself.

Those libraries already try to wrap any object that has `then()`:

- Guzzle `Create::promiseFor()` also stores `wait` when the object has that method, then calls `wait(true)`. `\Tyhp\Promise::wait` is `wait(int $timeoutMs)`. Emitted PHP is `strict_types=1`, and Guzzle's call site is strict, so `wait(true)` is a `TypeError`.
- React `React\Promise\resolve()` only calls `then()`. Our `then()` schedules a Fiber. The React promise stays pending until `\Tyhp\EventLoop` runs.
- Amp has no `then()`, so duck-typing never sees a `Future`.

Folding their methods onto `\Tyhp\Promise` does not remove the extra types. The class is `final`. One `then()` cannot return `\Tyhp\Promise`, `\GuzzleHttp\Promise\PromiseInterface`, `\React\Promise\PromiseInterface`, and `\Http\Promise\Promise` at once. One `wait()` cannot be both `wait(int $timeoutMs)` and `wait(bool $unwrap)`.

---

## Decisions (locked)

1. **`await` stays `\Tyhp\Promise` only.** Conversion is explicit: `await Promise::from($foreign)`. Implicit `await` on any `then()` object hides a promise that never settles.
2. **Inbound lives in `tyhp/async` and duck-types `then()`.** It does not `use` or typehint Guzzle, React, php-http, or Amp. `class_exists` guards are allowed. Installing those libraries is not required.
3. **Outbound wrappers are separate packages** that `tyhp/async` `suggest`s and does not `require`. A class that `implements` a foreign interface fatals if that interface is not loaded, so those classes cannot live in `tyhp/async`.
4. **One loop owns the thread.** The default owner remains today's `\Tyhp\EventLoop`. An optional driver *replaces* it for the process. The default driver and a React or Revolt driver are never both running.
5. **Do not call a foreign `wait()` from a fiber.** Do not call `\Tyhp\Promise::wait()` / `EventLoop::run` / `runUntilSettled` from inside `async` (already re-entrancy-forbidden).
6. **Do not pass a raw `\Tyhp\Promise` to `Create::promiseFor()` or `React\Promise\resolve()`.** Use the outbound wrapper for that library.
7. **Cancellation stays `CancellationToken` on our side.** Inbound `from($thenable, $token)` calls foreign `cancel()` when the token fires and `cancel()` exists. Outbound `cancel()` on a wrapper does not stop Tyhp work; Tyhp work stops when the token the async function already holds is cancelled.
8. **Rejection on our side is always `\Throwable`.** A Guzzle rejection that is not a `\Throwable` is wrapped. We do not take a dependency on `GuzzleHttp\Promise\RejectionException`.
9. **Foreign promises are not OS threads.** Adopting one suspends the current fiber. The owning loop keeps turning. That is the yield. `ext-parallel` and pthreads are out of scope.
10. **Prophecy is ignored** except for a clear error if someone passes it to `Promise::from`.

---

## What already exists (do not reinvent)

| Piece | Where | Keep |
| --- | --- | --- |
| `\Tyhp\Promise<TReturn>` | `runtime/packages/async/tyhp_src/Promise.tyhp` | `final`. `then` / `catch` / `finally` / `continueWith`. `wait(int $timeoutMs = -1)` runs our loop. |
| `\Tyhp\Deferred<T>` | `Deferred.tyhp` | External settle path. `from()` settles a `Deferred`. |
| `\Tyhp\PromiseState` | `PromiseState.tyhp` | `Pending` / `Fulfilled` / `Rejected`, string values `pending` / `fulfilled` / `rejected`. Same strings as Guzzle and php-http. |
| `\Tyhp\EventLoop` | `EventLoop.tyhp` | Singleton. Turn order: microtasks (fiber start/resume), timers, `stream_select`, I/O callbacks. `runUntilSettled` throws if the loop is already running. |
| `Promise::yield()` / `Promise::yieldTick()` | `Promise.tyhp` | Cooperative yield of the current fiber. Pumps (below) run on those turns too. Do not add a third yield API. |
| `CancellationToken` | `CancellationToken.tyhp` | Cooperative cancel. Not the same as Guzzle/React `cancel()`. |
| User doc | `docs/content/tyhp_2600_asyncAndAwait.md` | Stays the `async` / `await` page. Phase 6 adds a pointer, not a rewrite. |

`doResolve()` continues to unwrap only `\Tyhp\Promise`. Adoption of foreign thenables is `from()`, not a change to `resolve()`.

---

## Interop matrix

| Foreign type | Inbound (`Promise::from`) | What makes it settle | Outbound package | Loop |
| --- | --- | --- | --- | --- |
| `\Tyhp\Promise` | Returned as-is | Our loop | — | Default owner |
| Object with `then(callable, callable)` (php-http fulfilled/rejected, React settled, any sync thenable) | Subscribe. Sync `then` settles the deferred before `from()` returns | The thenable itself | — | None needed when already settled |
| `\GuzzleHttp\Promise\PromiseInterface` (2.5 and 3.0; same methods) | Subscribe via `then`. While that adoption is pending, pump `Utils::queue()` if the class exists | Queue flush runs `then` callbacks. **Does not** run Guzzle HTTP's curl wait function | `tyhp/async-guzzle` | Guest pump only. Curl progress is the Phase 5 spike |
| `\Http\Promise\Promise` | Same duck-typed `then`. Settled `FulfilledPromise` / `RejectedPromise` call the handler inline | Implementor | `tyhp/async-http-promise` | Whatever the implementor uses |
| `\React\Promise\PromiseInterface` | Same duck-typed `then`. No `wait()` to call | Whoever resolves it. Socket/timer I/O needs React's loop **or** the React driver | `tyhp/async-react` | Default loop sees callback settlement only. React I/O needs Phase 4's driver |
| `\Amp\Future` | Not a thenable. `from()` rejects it. `fromFuture()` exists only in `tyhp/async-amp` | Revolt | `tyhp/async-amp` | Phase 4 spike. If Revolt suspensions do not compose, the package documents that and does not ship `fromFuture()` |
| `\Prophecy\Promise\PromiseInterface` | `from()` throws. It has `execute()`, not `then()` | — | None | — |

Guzzle 2.5.3 and 3.0.2 both defer `then` handlers onto `Utils::queue()`, including handlers on an already-fulfilled `FulfilledPromise`. php-http and React invoke handlers on an already-settled promise immediately. The pump exists so Guzzle adoption does not wait for process shutdown (Guzzle's queue also runs from a shutdown function).

---

## What must be avoided

These are product rules. The user doc and the AIDevGuide section must state them. Implementations must not "helpfully" do the forbidden thing.

| Do not | Why |
| --- | --- |
| `await $guzzleOrReactOrHttpOrAmpPromise` | `await` lowers to `\Tyhp\Promise::_await(\Tyhp\Promise)`. The checker rejects other types. A cast does not run their scheduler. |
| `Promise::resolve($foreign)` / `doResolve($foreign)` to adopt | Unwraps only `\Tyhp\Promise`. The foreign object becomes the fulfillment value. |
| `Create::promiseFor($tyhpPromise)` or `React\Promise\resolve($tyhpPromise)` | Guzzle calls `wait(true)` and fatals. React's `then` callback does not run until our loop runs, so their promise sits pending. Use the outbound wrapper. |
| Foreign `wait()`, or `wait(true)` / `wait(false)`, inside `async` | Blocks the thread inside their wait function (often curl or their queue) or hits our `wait(int)` with a bool. Every other fiber stops. |
| `\Tyhp\Promise::wait()` / `EventLoop::run()` / `runUntilSettled()` inside `async` | Our loop is already running. `runUntilSettled` throws. |
| Running `\Tyhp\EventLoop` and `React\EventLoop\Loop::run()` or `Revolt\EventLoop::run()` in one process | Both block in `stream_select` (or equivalent). One owner. Opt into a driver instead. |
| `Amp\Future::await()` inside a Tyhp fiber on the default loop | Revolt suspends the fiber and expects to resume it. Our loop will not. Deadlock. |
| Treating `cancel()` and `CancellationToken` as the same knob | Inbound token may call their `cancel()`. Their `cancel()` on a value you did not adopt does nothing to our fibers. Outbound `cancel()` does not cancel Tyhp work. |
| Scraping a foreign promise with reflection to find its curl handle, process, or stream | Private layout differs by version. Progress hooks are the pump API and the explicit driver, not property walks. |
| Assuming a foreign promise is a thread or a process | It is a callback or a fiber on this thread. See [Concurrent work is not threads](#concurrent-work-is-not-threads). |
| Passing a Prophecy promise to `Promise::from` | Different concept. The error must say so when that interface is loaded. |

---

## Inbound: `Promise::from`

```tyhp
public static function from<T extends void|mixed = mixed>(
    object $thenable,
    ?CancellationToken $token = null
): Promise<T>
```

Algorithm:

1. If `$thenable instanceof Promise`, return it (generic identity; do not wrap).
2. If `class_exists(\Prophecy\Promise\PromiseInterface::class)` and `$thenable instanceof` that interface, throw `\InvalidArgumentException` that this is a Prophecy test-double hook, not an async promise, and that `async` / `await` use `\Tyhp\Promise`.
3. If `method_exists($thenable, 'then')` is false, throw `\InvalidArgumentException` naming the class and stating that `from()` needs a thenable (`then()`). Mention Amp `Future` has no `then()` and points at `tyhp/async-amp` when `class_exists(\Amp\Future::class)`.
4. Create `Deferred<T>`.
5. Call `$thenable->then($onFulfilled, $onRejected)`. Ignore the return value of `then` (their chain); our promise is the deferred.
   - `$onFulfilled` calls `$deferred->resolve($value)` once.
   - `$onRejected` calls `$deferred->reject(...)`. If the reason is `\Throwable`, use it. Otherwise wrap it in `\Tyhp\Exceptions\ForeignRejectionException` (`getReason(): mixed`).
6. If step 5 throws, reject the deferred with that throwable (or return an already-rejected promise). Do not leave a pending promise for a thenable that failed to subscribe.
7. If `$token` is not null and `method_exists($thenable, 'cancel')`, `$token->register()` calls `$thenable->cancel()` once. Cancelling does not by itself settle our deferred; their `cancel()` is expected to reject, and the rejection handler settles us. If they no-op `cancel()`, our promise stays pending. Document that.
8. If `class_exists(\GuzzleHttp\Promise\Utils::class)`, register the Guzzle queue pump (once per process) and keep it active while at least one Guzzle adoption is outstanding. The pump calls `Utils::queue()->run()`. It does not call `wait()`.
9. Return `$deferred->getPromise()`.

`from()` must not call `wait` on the thenable. For a sync thenable (php-http `FulfilledPromise`, React `FulfilledPromise`), step 5 settles the deferred before `from()` returns. For Guzzle, step 5 only queues a callback; the pump in step 8 (or any later loop turn) flushes it. If the caller then `await`s the result, the loop is running and the pump runs. If the caller is synchronous, `->wait()` on the **Tyhp** promise runs our loop, which runs the pump.

Pending Guzzle HTTP transfers (the promise's wait function drives `curl_multi`) do not progress from the queue pump. That is the Phase 5 spike, not a behavior of `from()`.

`from()` does not accept `mixed` scalars. A plain value is `Promise::resolved($value)`.

---

## Guest pumps

`\Tyhp\EventLoop` gains:

```tyhp
public function addPump(callable(): void $pump): void
public function removePump(callable(): void $pump): void
```

A pump is a `callable(): void` invoked at the start of every loop turn, beside the microtask drain, including turns that are about to block in `stream_select` / `waitForIO`. Pumps also run from `runUntilSettled`.

Rules:

- Pumps are how a recommended package cooperates without being required. Core registers none until `from()` sees Guzzle.
- A pump must not block and must not call `EventLoop::run` / `runUntilSettled` / `Promise::wait`.
- A pump may enqueue microtasks, timers, and stream watchers. Those are processed on the same turn after pumps, or the following turn if added during I/O — match today's ordering and document it in the package docblock.
- Exceptions from a pump propagate like settlement-callback failures: one throwable escapes, more than one is wrapped. Do not swallow them.
- `Promise::yield()` and `Promise::yieldTick()` already return to the loop. No new yield method. After this story, a yield turn also runs pumps, so a Guzzle queue flush happens on `await Promise::yield()` as well as on `await Promise::from($guzzlePromise)`.

The Guzzle pump is reference-counted by outstanding `from()` calls whose thenable's class is under `GuzzleHttp\Promise\` or that `instanceof` the interface when it is loaded. When the count hits zero, leave `Utils` loaded but stop calling `queue()->run()` every turn (an empty `run()` is cheap; still gate it so we do not depend on Guzzle internals more than needed). If the class exists and the user adopted a thenable that only duck-types, still run the pump while any `from()` not-yet-settled — a Guzzle promise is the reason the pump exists, and an extra `queue()->run()` while adoptions are pending is the safe side. Stop when no `from()` adoption is pending.

---

## Outbound packages (recommended, not required)

`tyhp/async` `composer.json` `suggest` (descriptions are user-facing, no story numbers):

| Package | Requires | Suggest text |
| --- | --- | --- |
| `tyhp/async-http-promise` | `tyhp/async`, `php-http/promise` | Wrap a Tyhp Promise as `Http\Promise\Promise` for HTTPlug-style clients. |
| `tyhp/async-guzzle` | `tyhp/async`, `guzzlehttp/promises` `^2.5 \|\| ^3.0` | Wrap a Tyhp Promise as `GuzzleHttp\Promise\PromiseInterface`. Do not pass a Tyhp Promise to `Create::promiseFor()`. |
| `tyhp/async-react` | `tyhp/async`, `react/promise`; driver code also needs `react/event-loop` | Wrap a Tyhp Promise as `React\Promise\PromiseInterface`. Optionally run Tyhp async on ReactPHP's event loop. |
| `tyhp/async-amp` | `tyhp/async`, `amphp/amp`, `revolt/event-loop` | Run Tyhp async on Revolt, or learn why an Amp Future cannot be awaited on the default loop. |

Each package lives next to `tyhp/async` in whichever tree contains it (this monorepo's `runtime/packages/` until Story 27.3 moves that tree). One namespace, `Tyhp\AsyncInterop`, split by package so a missing foreign interface is never loaded.

### `HttpPromise`

`Tyhp\AsyncInterop\HttpPromise implements \Http\Promise\Promise`

- `then(?callable $onFulfilled, ?callable $onRejected)` returns another `HttpPromise` (or `\Http\Promise\Promise`) chained with our `then`.
- `getState()` maps `\Tyhp\PromiseState` to `pending` / `fulfilled` / `rejected`.
- `wait(true)` returns the value or throws. `wait(false)` settles and returns null.
- `wait()` calls `\Tyhp\Promise::wait()` only when our loop is **not** running. When it is running, throw `\LogicException` telling the caller to `await` the original `\Tyhp\Promise` instead. Do not re-enter `runUntilSettled`.

### `GuzzlePromise`

`Tyhp\AsyncInterop\GuzzlePromise implements \GuzzleHttp\Promise\PromiseInterface`

Verify one class can implement both 2.5.3 and 3.0.2 (both are in `runtime/packages/guzzlehttp-promises`). If parameter types on `then` / `wait` are incompatible across majors, constrain the package to the majors a single class can implement and say so in that package's README. Do not fork the runtime behavior.

- `then` / `otherwise` follow their interface and return this package's promise type.
- `getState()` uses the same three strings.
- `resolve` / `reject` throw `\LogicException`. Settlement is owned by the inner `\Tyhp\Promise`, not by Guzzle callers. (Their interface requires the methods. They must not silently no-op and look settled.)
- `cancel()` is a no-op once we document that Tyhp cancellation is the token inside the async work. Required by the interface. Do not reject the Tyhp promise from `cancel()`, or callers will think `cancel()` aborts Tyhp work.
- `wait(bool $unwrap = true)` matches their meaning: run our loop if it is not running, then return the value or throw (`$unwrap === true`) or return null (`false`). Same re-entrancy error as `HttpPromise` when our loop is already running.
- `waitFn` / task-queue merging inside Guzzle's own `Promise` class is not our job. This wrapper is a separate class.

### `ReactPromise`

`Tyhp\AsyncInterop\ReactPromise implements \React\Promise\PromiseInterface`

- `then` / `catch` / `finally` / deprecated `otherwise` / `always` as the interface requires. Deprecated methods forward and may be marked deprecated in Tyhp.
- `cancel()` is a no-op, same rule as Guzzle.
- No `wait()` on this interface. Do not add one.

### Factory shape (all three)

```tyhp
HttpPromise::from(Promise<T> $promise): HttpPromise
GuzzlePromise::from(Promise<T> $promise): GuzzlePromise
ReactPromise::from(Promise<T> $promise): ReactPromise
```

Identity if the value is already that wrapper. Do not double-wrap.

These factories are the supported way to hand a Tyhp promise to code typehinted with the foreign interface. The user doc shows that call, not `Create::promiseFor`.

---

## One loop owns the thread

### Default

`\Tyhp\EventLoop` keeps today's `stream_select` behavior. Extract the body into an internal `StreamSelectLoopDriver` only if Phase 4 needs a seam. Public methods on `EventLoop` stay. Behavior of programs that never call `setDriver` stays.

### Driver seam (Phase 4)

```tyhp
namespace Tyhp\Contracts;

interface EventLoopDriver {
    // Fiber queue, timers, read/write streams, microtasks,
    // run / runUntilSettled / tick / stop / isRunning.
    // Same operations EventLoop exposes today. EventLoop delegates.
}
```

```tyhp
EventLoop::setDriver(EventLoopDriver $driver): void
```

- Allowed only before the loop has started and before any fiber, timer, or stream has been scheduled. Otherwise throw `\LogicException`.
- One driver per process. A second `setDriver` throws.
- Installing `tyhp/async-react` or `tyhp/async-amp` does **not** call `setDriver`. The user opts in. Auto-switching would surprise a process that only uses those promises as values.

### React driver (`tyhp/async-react`)

`Tyhp\AsyncInterop\ReactLoopDriver implements \Tyhp\Contracts\EventLoopDriver`

Requires `react/event-loop`. Maps:

| Ours | Theirs |
| --- | --- |
| Microtasks and fiber start/resume/throw | `futureTick` |
| `delay` / `interval` | `addTimer` / `addPeriodicTimer` |
| Read / write streams | `addReadStream` / `addWriteStream` |
| `run` / `runUntilSettled` | `Loop::run()` until the root promise settles, then `stop()` |
| Pumps | Invoked from the same `futureTick` as microtasks, before resuming fibers |

When this driver is selected, Tyhp `async` / `await` and ReactPHP timers/streams share **that** loop. Our `stream_select` loop does not also run.

React promises that are only callbacks (no React I/O) still use `Promise::from` on the default loop. The driver is for when React's loop must tick for the work to finish.

### Revolt / Amp (`tyhp/async-amp`) — spike, then ship or document

`Future::await()` suspends via `Revolt\EventLoop::getSuspension()`. That composes with Tyhp only if our fiber was started by Revolt and our suspend path **is** that suspension, not a raw `\Fiber::suspend` beside it.

Phase 4 spike, one test:

- `setDriver(new RevoltLoopDriver())`.
- A Tyhp `async` function calls `\Amp\Future::await` on a future that a Revolt watcher completes (a zero-delay timer is enough).
- The Tyhp function resumes with the value, and a second Tyhp fiber scheduled on the same driver also runs.

If that passes, ship `RevoltLoopDriver` and `AmpFuture::awaitAsPromise(Future $future): Promise` which is `async` and calls `Future::await` **only** after checking the active driver is the Revolt one. On the default driver, throw `\Tyhp\Exceptions\AsyncContextException` with the reason: Amp futures suspend through Revolt, and the default Tyhp loop will not resume them.

If the spike deadlocks or throws from nested suspension, **do not** ship `awaitAsPromise`. Ship the package as the driver only if the driver itself works for Tyhp timers/streams, or ship README-only incompatibility if the driver cannot own the loop either. The user doc then says Amp and Tyhp async do not share a fiber, and the user keeps Amp's functions on Revolt and Tyhp's `async` on the default loop, in separate call stacks — not nested.

---

## Concurrent work is not threads

PHP Fibers are cooperative and single-threaded. `Promise::from` does not start a thread, a process, or a worker.

What "their promise is doing real work" usually means:

| Kind of work | What the owning loop must do | This story |
| --- | --- | --- |
| Callbacks already queued (Guzzle task queue, a settled thenable) | Run the queue / the callback | `from()` + Guzzle pump |
| Non-blocking I/O on this thread (React streams, our streams, a process pipe the user registered) | Poll the stream | Default loop, or the React/Revolt driver if that library owns the watcher |
| Guzzle curl_multi inside a promise wait function | Something must tick curl without blocking the fiber | Phase 5 spike only. Until it lands, `await Promise::from($client->getAsync(...))` is **not** a supported way to run the transfer |
| Another OS process | This thread must poll the pipes or sockets the process uses | User or a bridge calls `EventLoop::addReadStream` / `addWriteStream`. `from()` does not discover those resources |
| `ext-parallel`, pthreads | Not a promise type we can adopt | Out of scope |

Yielding so "others" run:

- `await Promise::from($thenable)` suspends **this** fiber. The owner loop keeps running other fibers, timers, streams, and pumps.
- `await Promise::yield()` flushes fibers already queued (and pumps).
- `await Promise::yieldTick()` also waits out the timer phase (`delay(0)`).

That is the cooperation model. It is not a second loop, and it is not a thread join.

---

## User-facing contract (docs phase copies this)

Phase 6 publishes this contract. Write it for a Tyhp user who already uses `async` / `await` and just called a PHP library. No story numbers, no "we decided not to", no scheduler novel. State what to write, what is compatible, and why the incompatible call fails.

### Page

- New page `docs/content/tyhp_2610_promiseInterop.md`, frontmatter in the same shape as `tyhp_2600_asyncAndAwait.md` (`title`, `status.tier`, `status.story`, `status.state`).
- `docs/content/toc.json`: list it immediately after `tyhp_2600_asyncAndAwait.md`.
- At the top of `docs/content/tyhp_2600_asyncAndAwait.md`, after the opening paragraph, one short pointer: library promises (Guzzle, HTTPlug, ReactPHP, Amp) are not `\Tyhp\Promise`; link the new page.
- `AIDevGuide/guide/13-async-await.md`: a short "Library promises" subsection with the same rules (agents otherwise tell users to `await` a Guzzle promise or to pass a Tyhp promise to `promiseFor`).
- `AIDevGuide/handbook/07-runtime-api.md`: signatures for `Promise::from`, `EventLoop::addPump` / `removePump` / `setDriver`, and the `Tyhp\AsyncInterop` factories.
- Package docblocks on `from` and each factory. Each recommended package gets a README that repeats the avoid-list rows for that library only.
- `runtime/packages/async/ASYNC_TYPES.md`: add `from` to the factory table when the tyhpdef is updated.

### Outline the page must follow

1. **`async` / `await` are Tyhp's promise and Tyhp's loop.** The keywords lower to `\Tyhp\Promise`. They do not lower to Guzzle, React, php-http, or Amp. One process has one loop owner. The default owner is Tyhp's event loop.

2. **Bring a library promise in.**

```tyhp
use GuzzleHttp\Promise\PromiseInterface as GuzzlePromise;
use Tyhp\Promise;

async function body(GuzzlePromise $request): mixed {
    return await Promise::from($request);
}
```

   Say when this completes: the foreign promise settles through `then()`, and for Guzzle the Tyhp loop also flushes Guzzle's task queue. Say when it does not: a pending Guzzle HTTP transfer that only runs inside `wait()` / curl. Point at the Guzzle section. Say Amp `Future` is not a thenable.

3. **Hand a Tyhp promise out.** Show `HttpPromise::from`, `GuzzlePromise::from`, `ReactPromise::from`. Say these packages are suggested by `tyhp/async` and installed only when that library's typehint is required. Say `Create::promiseFor` and `React\Promise\resolve` on a raw `\Tyhp\Promise` are the wrong direction (`wait(true)` type error; React callback waits for a loop that is not running).

4. **One loop.** Table: default Tyhp loop; opt-in `EventLoop::setDriver(new ReactLoopDriver())`; opt-in Revolt driver and the Amp rule that came out of the spike (either "await a Future only after this driver is set" or "do not await an Amp Future from Tyhp async"). State that both loops must not be `run()` in one process, because each blocks waiting for I/O.

5. **What is not a thread.** Short paragraph: a foreign promise is callbacks or a fiber on this thread, or I/O the loop must poll. `await Promise::from(...)` lets other Tyhp work run. It does not join a worker. Blocking calls (`wait()`, `\file_get_contents()`, PDO, synchronous curl) still stall every fiber. Process pipes work when something registers them with the loop; `from()` does not find them for you.

6. **Do / don't table.** The rows in [What must be avoided](#what-must-be-avoided), rewritten without internal method names where the user has a public substitute (`from`, the wrappers, `await`, `CancellationToken`). Keep `Create::promiseFor`, `React\Promise\resolve`, `Future::await`, `wait(true)`, and Prophecy — those are the names users will see in PHP docs.

7. **Cancellation and errors.** Our token can call their `cancel()` when passed to `from()`. Their `cancel()` on an outbound wrapper does not cancel the Tyhp function; cancel the token you passed into that function. A Guzzle rejection that is not an exception arrives as `\Tyhp\Exceptions\ForeignRejectionException`.

8. **Prophecy.** One paragraph. `Prophecy\Promise\PromiseInterface` configures a mock's return value. It is not awaitable. `Promise::from` throws if you pass one.

---

## Phases

### Phase 1 — Inbound `from()` and pumps

- [ ] `ForeignRejectionException` in `tyhp/async`.
- [ ] `Promise::from` as specified. Tyhpdef in `package.tyhpdef` updated to match.
- [ ] `EventLoop::addPump` / `removePump`. Guzzle queue pump registered from `from()` behind `class_exists`.
- [ ] Tests in `runtime/packages/async/tests` use fakes only (sync thenable, queued thenable that settles when the pump runs, non-throwable rejection, missing `then`, identity, cancel hook). No required dependency on Guzzle, React, php-http, Amp, or Prophecy.
- [ ] `composer.json` `suggest` block for the four packages (packages may land in later phases; suggest text can land here).

Acceptance: `await Promise::from($syncThenable)` returns the value; a queued thenable settles on a loop turn after the pump runs; `from()` never calls `wait`.

### Phase 2 — Outbound packages

- [ ] `tyhp/async-http-promise`, `tyhp/async-guzzle`, `tyhp/async-react` (promise wrapper only; driver is Phase 4).
- [ ] PHPUnit in each package, against the real interface. Guzzle tests run on the versions already vendored for tyhpdef (2.5.3 and 3.0.2) or the package constraint is narrowed to the major that typechecks.
- [ ] `wait()` re-entrancy test: calling wrapper `wait()` from inside a Tyhp `async` function throws the documented `\LogicException`.
- [ ] These packages are not `require`d by `tyhp/async` and not required for `tyhp/async` CI.

Acceptance: a function typehinted as `\Http\Promise\Promise` / Guzzle `PromiseInterface` / React `PromiseInterface` accepts `*::from($tyhpPromise)` and observes fulfillment and rejection.

### Phase 3 — User docs and AIDevGuide (can start from the contract above; finish after Phases 4–5 so the Amp and Guzzle-HTTP paragraphs match what shipped)

- [ ] `docs/content/tyhp_2610_promiseInterop.md` and toc entry.
- [ ] Pointer in `tyhp_2600_asyncAndAwait.md`.
- [ ] `AIDevGuide/guide/13-async-await.md` and `handbook/07-runtime-api.md`.
- [ ] READMEs and docblocks.
- [ ] `ASYNC_TYPES.md` factory row for `from`.

Acceptance: a reader can answer, without reading this plan, what `await` accepts, how to cross each library, why two loops must not run, and why Prophecy and Guzzle `promiseFor` are the wrong tools.

### Phase 4 — Loop drivers

- [ ] `EventLoopDriver` + `setDriver` + default behavior unchanged when unset.
- [ ] `ReactLoopDriver` in `tyhp/async-react` (`react/event-loop`). Opt-in only.
- [ ] Test: a Tyhp `delay` and a React timer both fire under the React driver; the default driver is not also polling.
- [ ] Revolt/Amp spike as specified. Ship `awaitAsPromise` only on success. On failure, README + user doc state the incompatibility and the package does not expose a method that deadlocks.

Acceptance: with the React driver set before any async work, one loop runs both. With no `setDriver`, `stream_select` behavior matches pre-story tests.

### Phase 5 — Guzzle HTTP tick spike (bounded)

- [ ] Read `CurlMultiHandler` for the Guzzle majors this repo already vendors (`guzzlehttp/guzzle` 7.x and 8.x).
- [ ] If a public non-blocking tick exists, `tyhp/async-guzzle` may register it as a pump **when the user opts in** (explicit enable, not merely `class_exists`). Document the enable call.
- [ ] If the only progress API blocks, do not call it. User doc states that `getAsync` + `Promise::from` does not run the transfer.

Acceptance: either an opted-in non-blocking tick test with a local transfer, or a documented refusal. No fiber calls `wait()`.

---

## Out of scope

- Making `\Tyhp\Promise` implement any foreign interface.
- Changing `await` to accept thenables implicitly.
- Changing `Promise::resolve` / `doResolve` to adopt foreign thenables.
- Renaming `wait(int)` to match `wait(bool)`, or accepting `true` as a timeout sentinel.
- Adding `otherwise`, stringly `getState`, or public `resolve` / `reject` to `\Tyhp\Promise`.
- Prophecy integration beyond the `from()` error.
- `ext-parallel` / pthreads.
- Reflection over private curl handles or process resources.
- Running two event loops.
- A checker diagnostic when someone passes a foreign promise to `await` beyond the existing type error (the type error is the right diagnostic).

---

## Cross-story references

| Story | Relationship |
| --- | --- |
| 04 / 06 | `tyhp/async` already ships. This story extends it. |
| 23 / 24 | Optimizer. No dependency either way. |
| 25 | `internal`. Does not depend on 24.1. 24.1 does not depend on 25. |
| 27.3 | Moves `runtime/packages`. Implement 24.1 in the tree that contains `tyhp/async` at the time. |
| 30 | Capstone docs. Phase 3 of this story writes the interop page; 30 should not contradict it. |
