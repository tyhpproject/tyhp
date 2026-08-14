## 7. Generics (compile-time only, erased)

```tyhp
class Container<T> {}
class Pair<TKey extends int|string, TValue> {}   // constraint via `extends`
class Box<T = string> {}                          // default via `= Type`
function map<T, U>(callable<T, U> $fn, array<T> $in): array<U> {}
type StringMap<V> = array<string, V>;
$b = new Box<int>(5);
class Repo<T> extends Base<T> implements Query<T> {}   // generic args on extends/implements
```
- Omitted trailing type args use declared defaults (`Box` with `T = string` → `Box<string>`;
  partial `MyMap<int>` with `TValue = mixed` → `MyMap<int, mixed>`). Resolution order at call
  sites remains explicit → inference → defaults.
- Arg count must match; each arg is checked against its `extends` constraint.
- Erasure: `Box<int>` → `Box`; unconstrained `T` acts like `mixed`, `T extends Foo` like `Foo`;
  `array<…>`/`iterable<…>`/`callable<…>` → bare `array`/`iterable`/`callable`.

**`array<T>` / `array<K,V>`:** `array<T>` = keys `int|string`, values `T`; `array<K,V>` = keys `K`
(`extends int|string`), values `V`. `iterable<…>` = same shape.

**`callable<…>` — return type is LAST:**

| Written | Signature |
|---------|-----------|
| `callable<string, int>` | `(string): int` |
| `callable<int, int, bool>` | `(int,int): bool` |
| `callable<void>` | `(): void` |
| `callable` | untyped |

`\Closure<TReturn>` same rule. A `type` aliasing a callable must carry the return constraint:
`type Handler<TReturn extends void|never|mixed> = callable<Request, TReturn>;`

**Optional params → arity facets:** a function/closure/method with trailing defaults is typed as an
intersection of return-last callables (one facet per valid arity prefix). Facets are siblings —
`callable<A, B, R>` does not subtype `callable<A, R>` by itself. Shared helper:
`ArityFacetExpansion.GetValidArityPrefixes` (also for Story 27 `new<>`).

**Relative types vs generics:** bare `self` / bare `static` inherit receiver / call-site type
arguments; parameterized `self<…>` / `parent<…>` are allowed; parameterized `static<…>` is
**forbidden** (TYHP4168). Factories that stamp a method generic onto the class use
`: self<T>` or the class name — not `: static<T>`. See `docs/content/tyhp_0150_newTypes.md`.
