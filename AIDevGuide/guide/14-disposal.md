## 14. Deterministic disposal: `using` / `:=`

Resource must implement `\Tyhp\Contracts\IsDisposable` (`dispose(): void`), or `AsyncIsDisposable`
for `await using`.
```tyhp
using (FileHandle $h = new FileHandle()) { /* disposed on exit */ }
using ($a = openA(), $b = openB()) { /* disposed reverse order */ }
await using ($c = openAsync()) { /* awaits disposeAsync() */ }
$conn := new DbConnection();   // `:=` = disposable-scoped local, disposed at scope end
```
`using` → try/finally:
```php
$h = new FileHandle();
try { /* body */ } finally { if ($h instanceof \Tyhp\Contracts\IsDisposable) { $h->dispose(); } }
```
`:=` → `$__scope = \Tyhp\DisposableScope::create(); $conn = $__scope->using(new DbConnection());`.

**Failure semantics (know these):**
- Disposal order is **reverse** (LIFO).
- A `dispose()` that throws in a `using` finally **masks the body exception** (PHP `finally`).
- Multiple resources whose disposals throw → thrown as `\Tyhp\Exceptions\AggregateException`
  (`getInnerExceptions()`); it collects **disposal** errors only — the body exception is not merged.
- `:=` disposal runs via the scope's `__destruct`, which only emits a warning on failure (does **not**
  rethrow) — so prefer `using` when you need dispose errors to propagate.
