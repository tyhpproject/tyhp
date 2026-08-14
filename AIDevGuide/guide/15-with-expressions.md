## 15. `with` expressions (record-style update)

```tyhp
Config $copy = clone $base with [enabled => false, name => 'copy'];   // new object, overrides
Point  $p    = new Point() with [x => 0, y => 0];                     // construct + override
$user with [name => 'Eve'];                                            // in-place mutate
```
Every key must be a real property; each value must fit its type; in-place is rejected on `readonly`.
Only `new Struct() with […]` (→ array literal) compiles today; for classes prefer explicit
assignment.
