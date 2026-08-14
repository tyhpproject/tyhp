## 17. Compile-time helpers

| Expr | Result | → |
|------|--------|---|
| `nameof(x)` | short symbol name | string literal — `nameof(User)` → `'User'` |
| `default(T)` | default value of a type | `0`/`0.0`/`''`/`false`/`[]`/`null` |
| `typeof(x)` | runtime `\Tyhp\Type` | `\Tyhp\Type::int()`, `::fromClassName(Foo::class)` (pkg `tyhp/core`) |
| `variable_exists(x)` | compile-time in-scope check | `\array_key_exists('x', \get_defined_vars())` (name extracted as string literal; checker may fold to `true`/`false`) |
