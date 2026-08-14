## 4. Type system

**Known types, now enforced:** `int float string bool array callable iterable object mixed void
never null true false self parent static resource`. Stricter: `static` is not allowed as a
parameter or property type (return types, locals, and generic args like
`ReflectionClass<static>` are fine);
`resource` can't appear in a type you write.

**New types:**

| Type | Meaning | → |
|------|---------|---|
| `decimal` | arbitrary-precision decimal | `\Tyhp\Decimal` (pkg `tyhp/decimal`) |
| `struct` | structural value type | `array` |
| literals (`42`, `'GET'`, `3.14`) | single-value type | the scalar |

```tyhp
type HttpMethod = 'GET'|'POST'|'PUT';   // union of literal types
```
Unions/intersections/`?T` as in PHP; a union containing `mixed` → `mixed`.

**`decimal`:** use in signatures/locals; build values with `\Tyhp\decimal('19.99')`. ⚠️ `(decimal)`
cast doesn't compile.
