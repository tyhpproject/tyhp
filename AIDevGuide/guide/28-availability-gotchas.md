## 28. Availability & gotchas

**Use freely (compile cleanly):** typed locals; generics (pass all type args); top-level `type`
aliases; structs (`new`/`with`/property access); operator overloads; extensions (simple `$x->m()`);
`async`/`await` (via Promise API); `using`/`:=`; guard functions + narrowing; `nameof`, `default(T)`,
`typeof`, `variable_exists`; `decimal` type hints; symbol-name & template string types; property accessors
on PHP 8.4+.

**Annotate, don't assume:** every parameter/property and return type — locals infer from
initializers (including array literals).

**Use the PHP form instead (don't compile yet):**

| Not | Use |
|-----|-----|
| `(decimal) $x` | `\Tyhp\decimal($x)` |
| `clone $obj with […]` / `$obj with […]` (classes) | construct/assign explicitly |
| `foreach (await $iter as $x)` | supported — desugars by `AsyncIterable` / `Promise<Iterable>` / `Promise<AsyncIterable>` |
| property hooks on PHP < 8.4 | getter/setter methods |
| member/class-scoped or generic `type` aliases | a top-level non-generic `type` |

**Not in the language yet (don't use):** `internal` visibility; null-conditional assignment
(`$a?->b = …`); `new<TArgs…>` constructable type; type-position anonymous `struct {…}`
(expression-form `new struct {…}` is supported).
