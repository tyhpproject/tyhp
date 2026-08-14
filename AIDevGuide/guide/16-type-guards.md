## 16. Type guards, narrowing, `is`

**Guard functions** — the "return type" narrows an argument (body must return `bool` on all paths;
named var must be a parameter); → `: bool`:
```tyhp
function isString(mixed $value): $value is string { return \is_string($value); }
function isUser(mixed $v): $v instanceof User { return $v instanceof User; }
```
**`is`/`isa`/`isan`/`is_a`/`is_an`** = aliases of `instanceof`, expression is `bool`.
They parse, narrow, and emit as `instanceof`.

**What narrows in an `if`:**

| Condition | true | false |
|-----------|------|-------|
| `$x instanceof T` | `$x: T` | `$x` minus `T` |
| `$x === null` / `$x !== null` | `null` / non-null | non-null / `null` |
| `\is_string/\is_int/\is_float/\is_bool/\is_array/\is_null/\is_object/\is_callable($x)` | that type | negative |
| your guard `isFoo($x)` | guarded type | negative |
| `$a && $b` | narrows both | — |
| `$a \|\| $b` | — | negative-narrows both |

Not narrowed: loose `==`/`!=`, truthiness, after reassignment.
