## 7. Runtime API reference (public signatures)

Signatures follow the intended public API (`.tyhpdef`). `async`-marked methods return `T` when
awaited; the compiled call returns `\Tyhp\Promise<T>` until you `await` it. Callable generics are
return-last.

**`\Tyhp\Promise<TReturn extends void|mixed>`** — *`_async`/`_await` are compiler-emitted; never call
them by hand.*
```
static resolve<T>(T $value = null): Promise<T>
static reject<T>(Throwable $reason): Promise<T>
static withResolvers<T>(): Deferred<T>
static run<T>(callable<T> $fn, ?CancellationToken $token = null): T     // blocking sync entry point
async static delay(int $ms, ?CancellationToken $token = null): void
async static all<T>(array<Promise<T>> $ps, ?CancellationToken $token = null): array<T>
async static allSettled(array<Promise> $ps): array                     // no cancellation
async static race<T>(array<Promise<T>> $ps, ?CancellationToken $token = null): T
async static any<T>(array<Promise<T>> $ps, ?CancellationToken $token = null): T
async static timeout<T>(Promise<T> $p, int $ms, ?CancellationToken $token = null): T
async static batch<I,R>(array<I> $items, callable<I, Promise<R>> $fn, int $concurrency = 5, ?CancellationToken $token = null): array<R>
async static fromGenerator(Generator $gen): mixed                      // no async generators
static whenAll(Promise ...$ps): static    // alias → all
static whenAny(Promise ...$ps): static    // alias → race
// instance:
async then<R>(?callable<TReturn,R> $onOk = null, ?callable<Throwable,R> $onErr = null): TReturn|R
async catch<R>(callable<Throwable,R> $onErr): TReturn|R
async finally(callable<void> $onFinally): TReturn
wait(int $timeoutMs = -1): mixed          getResult(): mixed          getState(): PromiseState
isCompleted(): bool   isFulfilled(): bool   isFaulted(): bool   getError(): ?Throwable
// enum PromiseState: string { Pending | Fulfilled | Rejected }
```

**`\Tyhp\Deferred<T extends void|mixed>`**: `getPromise(): Promise<T>` · `resolve(T $v = null): void`
· `reject(Throwable $r): void`.

**`\Tyhp\CancellationToken`**: `static none(): CancellationToken` · `isCancellationRequested(): bool`
· `throwIfCancellationRequested(): void` · `register(callable $cb): Closure` (call the returned
closure to unregister). *(`cancel()` is internal — go through the source.)*

**`\Tyhp\CancellationTokenSource` (IsDisposable)**: `__construct(?int $cancelAfterMs = null)` ·
`getToken(): CancellationToken` · `cancel(): void` · `cancelAfter(int $ms): void` ·
`isCancellationRequested(): bool` · `dispose(): void`.

**`\Tyhp\DisposableScope` (IsDisposable)**: `static create(): DisposableScope` ·
`using(IsDisposable|AsyncIsDisposable $r): mixed` · `release(IsDisposable|AsyncIsDisposable $r): void`
· `dispose(): void`.

**Contracts**: `\Tyhp\Contracts\IsDisposable { dispose(): void }` ·
`AsyncIsDisposable { disposeAsync(): Promise<void> }` ·
`AsyncIterator<T> { next(): Promise<bool>; current(): Promise<T> }` ·
`AsyncIterable<T> { getAsyncIterator(): AsyncIterator<T> }`.

**`\Tyhp\Decimal`** — readonly `string $value`, `int $scale`, `int $roundingMode`.
```
__construct(float|int|string|DecimalConvertible|null $value = null, ?int $scale = null, int $roundingMode = PHP_ROUND_HALF_UP)
add/subtract/multiply/modulo/power(...)   divide(..., ?int $scale = null)   negate() abs() sqrt(?int $scale = null)
compareTo(...): int   equals/greaterThan/greaterThanOrEqual/lessThan/lessThanOrEqual(...)
isZero() isPositive() isNegative()   __toInt() __toFloat() withScale(int $scale)   round(int $p = 0, int $mode = …) floor() ceil()
format(int $decimals = 2, string $dec = '.', string $thou = ','): string   __toString(): string
static zero(int $scale = 2) one(int $scale = 2) min(...) max(...) sum(...) avg(...)
// helper: \Tyhp\decimal(float|int|string|DecimalConvertible|null $v = null): Decimal
// operators + - * / % ** and == != < <= >= <=> plus `convert` are defined
```

**`\Tyhp\Type` (Stringable)** — factories: `string() int() float() bool() null() void() mixed()
never() array() object() callable() iterable() resource()`, plus `union(self ...$t)`,
`intersection(self ...$t)`, `nullable(self $t)`, `generic(string $class, NamedType ...$p)`,
`fromClassName(string $c)`, `of(mixed $v)`, `is(mixed $v, self $t): bool`,
`compatible(self $broad, self $narrow): bool`. Instance: `asReadOnly()/asNullable()/asNonNullable()`,
`getKind(): string`, `getName(): ?string`, `isNullable(): bool`, `isReadOnly(): bool`, `__toString()`.

**Exceptions**: `\Tyhp\Exceptions\AggregateException(array<Throwable> $inner, string $msg = …)` →
`getInnerExceptions(): array<Throwable>` · `OperationCancelledException(CancellationToken $t, …)` →
`getToken(): CancellationToken` · `TimeoutException(string $msg = …, int $timeoutMs = 0, …)` →
`getTimeoutMs(): int`.

**`\Tyhp\Expression<TSource,TReturn>` / `PropertyPath` — experimental** (end-to-end wiring
incomplete): `Expression` carries `->body`, `->parameters`, `->callable`, `->returnType`, is callable
via `__invoke(...)`, and `compile(): Closure`. Translate a captured tree with an `ExpressionVisitor`
(`visitBinary`, `visitPropertyAccess`, `visitMethodCall`, `visitConstant`, … per node type);
`ExpressionSerializer::toJson(Expression $e): string`. `PropertyPath` adds
`getPath()/getSegments()/getValue(TSource $s)`.

**`\Tyhp\EventLoop`** (low-level; you rarely touch it directly): `static getInstance()`,
`run(Promise $root): mixed`, `runUntilSettled(Promise $p, int $timeoutMs = -1)`, `delay(int $ms,
callable $cb): string` (+ `cancelTimer`, `interval`), `defer/queueMicrotask(callable $cb)`, stream
registration, `tick(): bool`, `stop()`, `isRunning(): bool`.
