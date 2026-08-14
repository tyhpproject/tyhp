## 1. Mental model

- You write `.tyhp`; the compiler emits `<?php` + `declare(strict_types=1);`. No special VM.
- **Types are erased.** Generics, `struct`, `type` aliases, typed locals, guards, and string-types
  collapse to plain PHP at compile time.
- A few features use a `\Tyhp\` runtime (Composer packages): `decimal`, `async`/`await`, `typeof`,
  `using`/`:=`, operator overloads, expression trees. The rest is pure erasure.
- Stricter than PHP: **non-nullable by default**; **conditions must be `bool`** ([§3](03-strict-rules.md)); **every
  parameter, property, and (by default) return type and local must have a known type** ([§5](05-type-inference.md)).

```tyhp
<?tyhp
namespace App;
type Id = int;                               // alias (erased)
struct Point { int $x = 0; int $y = 0; }     // value struct → array
class Box<T = string> {                       // generics (erased)
    public T $value;
    public function __construct(T $value): void { $this->value = $value; }
    operator ==(self $l, self $r): bool => $l->value === $r->value;
}
function isString(mixed $v): $v is string { return \is_string($v); }   // type guard
async function load(): int { int $n = await fetchAsync(); return $n; }
int $count = 0;                               // typed local (erased)
using ($f = open()) { /* auto-disposed */ }
```
