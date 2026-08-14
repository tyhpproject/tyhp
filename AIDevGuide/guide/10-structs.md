## 10. Structs

Structural value type = "typed associative array": **typed properties only, no methods**, → PHP
`array`.

```tyhp
struct Point { int $x = 0; int $y = 0; ?string $label; }   // nullable prop may omit default
struct ColoredPoint extends Point { string $color = 'red'; }
```

| | `struct` | `class` |
|-|----------|---------|
| identity | value (array) | object ref |
| compatibility | **structural** — target's props ⊆ source's, types assignable | **nominal** (inheritance) |
| members | typed properties only | full OO |
| rule | non-nullable props need a default | — |

```tyhp
Point $p = new Point();            // → ['x'=>0,'y'=>0]
$p->x                              // → $p['x']
new Point() with [x => 1, y => 2]  // → ['x'=>1,'y'=>2]
```
Anonymous `new struct {…}` and `clone` on a struct both work (`clone` is a no-op for
array-backed structs; `clone … with` uses `\array_replace`).
