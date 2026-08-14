## 29. PHP-mapping reference

| Tyhp | → PHP |
|------|-------|
| `int $x = 5;` | `$x = 5;` |
| `Box<int>` | `Box` |
| `array<T>` / `callable<…>` / `iterable<T>` | `array` / `callable` / `iterable` |
| generic `T` / `T extends Foo` | `mixed` / `Foo` |
| `struct S` / `type X = …` decl | *(nothing)* |
| struct value / `new S() with […]` | `array` / `['k'=>v]` |
| `decimal` (hint) | `\Tyhp\Decimal` |
| symbol-name / template / string types | `string` |
| `nameof(x)` | `'x'` |
| `default(int)` | `0` (`''`/`false`/`[]`/`null` by type) |
| `typeof(T)` | `\Tyhp\Type::…()` |
| `fn(...): $v is T` | `: bool` |
| `async function f(): T` | `function f(): \Tyhp\Promise { return \Tyhp\Promise::_async(...); }` |
| `await $p` | `\Tyhp\Promise::_await($p)` |
| `$x := new R()` | `\Tyhp\DisposableScope::create()->using(new R())` |
| `using ($r = …) {}` | `try {} finally { $r->dispose(); }` |
| `$a + $b` (overloaded) | method call on the operand type |
| `$recv->extMethod(a)` | `\ExtClass::extMethod($recv, a)` |
| `<?tyhp` | `<?php` + `declare(strict_types=1);` |
