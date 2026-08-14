## 11. Operator overloading

Define in a `class`/`enum` body (or `extension`, [§12](12-extensions.md)).
```tyhp
class Money {
    public int $amount = 0;
    operator +(self $left, int $right): self { ... }
    operator +(self $l, self $r): self => new Money($l->amount + $r->amount);   // extra form of +
    operator ==(self $left, self $right): bool { return $left->amount === $right->amount; }
    operator convert(int $v)         { ... }        // convert FROM int  -> static __from
    operator convert(self $v): int   { return $v->amount; }  // convert TO int -> instance __toInt
}
```
- Binary: 2 params, ≥1 `self`. Unary: 1 `self`. Comparisons → `bool`; `<=>` → `int`. `convert` is
  type conversion.
- Overloadable: `+ - * / % **`, unary `+ - ++ -- ~ !`, bitwise `& | ^ << >>`, `.`, `< <= > >= == !=
  === !== <=>`, word `empty`, `convert`. **`true`, `false`, and `null` operators do not exist** —
  express truthiness with `operator convert(self): bool` and emptiness with `operator empty`.

### How it compiles
- **Every generated method is `static`**, except `convert`'s to-form `__to{T}()`, which is an
  **instance** method (so it satisfies `\Stringable` and the `\Tyhp\Contracts\*Convertible`
  instance interfaces).
- **All forms of one operator collapse into a single method** with union-typed operands/return; it
  dispatches internally on the runtime operand types (`instanceof` for objects, `is_int`/`is_float`/
  `is_string`/`is_bool`/`is_array` for builtins). An unaccepted combination throws
  `\Tyhp\Exceptions\InvalidParametersForOperatorOverloadException`.
- The generated name is **reserved**: declaring `operator +` forbids a hand-written `__add`
  (compile-time error). Reservation only applies when the operator is declared.
- All forms of a given operator must be **mutually distinguishable** by operand type (compile-time
  error otherwise), so the collapsed dispatch never silently decides.
- `convert` may not be self→self.

### Generated method names (exact)
| Op | Method | Op | Method | Op | Method |
|----|--------|----|--------|----|--------|
| `+` | `__add` | `-` | `__subtract` | `*` | `__multiply` |
| `/` | `__divide` | `%` | `__mod` | `**` | `__pow` |
| `&` | `__bwAnd` | `\|` | `__bwOr` | `^` | `__bwXor` |
| `<<` | `__bwSL` | `>>` | `__bwSR` | `.` | `__concat` |
| `<` | `__isLessThan` | `<=` | `__isLessThanOrEqual` | `>` | `__isGreaterThan` |
| `>=` | `__isGreaterThanOrEqual` | `==` | `__isEqual` | `!=` | `__isNotEqual` |
| `===` | `__isExact` | `!==` | `__isNotExact` | `<=>` | `__compare` |
| unary `+` | `__asNumeric` | unary `-` | `__negate` | `++` | `__increment` |
| `--` | `__decrement` | `~` | `__bwNot` | `!` | `__not` |
| `empty` | `__isEmpty` | | | | |

Convert: `convert to T` → instance `__to{T}(): T`; `convert from T` → static `__from(T $from): self`.
Convert-to target → auto-added interface (merged with existing `implements`):
`string`→`__toString` (`StringConvertible`, extends `\Stringable`), `bool`→`__toBool`
(`BoolConvertible`), `int`→`__toInt` (`IntConvertible`), `float`→`__toFloat` (`FloatConvertible`),
`decimal`→`__toDecimal` (`DecimalConvertible`). Any other type `T` → `__to{FormattedT}()` with base
`\Tyhp\Contracts\Convertible`.

### Call-site rewriting (all static, except convert-to casts)
- Binary `$a + $b` resolves **left operand first**: `\TypeA::__add($a, $b)` if `typeof($a)` has a
  matching form, else `\TypeB::__add($a, $b)`.
- Unary `!$a` → `\Type::__not($a)`; `~$a` → `__bwNot`; unary `+$a` → `__asNumeric`; `-$a` →
  `__negate`.
- Compound `$a += $b` → `$a = \Type::__add($a, $b)`.
- Cast `(int)$a` → `$a->__toInt()` (instance convert-to); string context works via `\Stringable`.
- `empty($o)` → `(empty($o) || \Type::__isEmpty($o))`.
- `$a++;` / `++$a;` → `$a = \Type::__increment($a)`; `$b = ++$a` → `$b = ($a = \Type::__increment($a))`.

**Tyhpdef exception — native passthrough:** a bodyless `operator +(…): T;` on a tyhpdef class
(no `extension` keyword) means the PHP type already supports the operator. The checker still
types `$a + $b`, but the emitter **does not** rewrite it. Mapped overloads use
`extension operator` **with a body** (see [§23](23-tyhpdef.md) / `tyhpdef_classes`).
