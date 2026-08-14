## 6. Assignability & subtyping

- **Nullable:** `null`/`?T` assignable to `?T`; a nullable value is **not** assignable to a
  non-nullable target.
- **Union:** source assignable if *every* member fits the target; a union target accepts a source
  matching *any* member.
- **Intersection (`A&B`):** target requires *all* members; an object/struct must declare all of them.
- **Literals:** a literal is assignable to its base (`5`→`int`, `'x'`→`string`, `true`→`bool`).
- **Numbers:** `int`→`float` allowed; no implicit string↔number.
- **`iterable`** ≡ `array|\Traversable`: `array`/`\Traversable` → `iterable` (keys/values covariant),
  but `iterable` alone is **not** assignable to `array` or `\Traversable` alone.
- **Structs** use width subtyping ([§10](10-structs.md)). `mixed` = top, `never` = bottom, any object → `object`.
- **Variance:** user generic parameters are **invariant** (no `in`/`out` keywords exist), with one
  carve-out: `G<T>` is assignable to `G<mixed>` when `T` is neither `void` nor `never` (heterogeneous
  bags such as `PropertyAccessor<mixed>`). Built-in `array<…>`/`iterable<…>` args are covariant;
  `callable<…>` is **contravariant in params, covariant in return**. `Promise<T>`, user classes:
  otherwise invariant (`G<string>` ↛ `G<int>`).
