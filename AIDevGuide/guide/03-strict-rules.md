## 3. Two rules that break PHP habits

**Non-nullable by default** — add `?`/`|null` explicitly:
```tyhp
string $n = null;    // ERROR
?string $n = null;   // OK
```
**Conditions must be `bool`** (no truthiness):
```tyhp
if ($user) {}          // ERROR (TYHP4043)
if ($user !== null) {} // OK  (also: $count !== 0, $list !== [])
```
Narrowing ([§16](16-type-guards.md)) resets on reassignment.
