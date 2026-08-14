## 9. Type aliases

```tyhp
type UserId = int;
type Map<K, V = mixed> = array<K, V>;
class Container {
    public type NullableSelf = self|null;   // member alias
}
```
Transparent (checker expands recursively) and erased. ⚠️ Top-level, non-generic aliases are the
reliable path; **member/class-scoped aliases and generic-alias type parameters are only partly wired
— prefer a top-level `type`** when you need it to type-check.
