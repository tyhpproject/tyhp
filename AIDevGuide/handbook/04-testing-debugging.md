## 4. Testing & debugging

- ⚠️ **No `tyhp test` / test runner.** Test the **emitted PHP** with normal tooling (PHPUnit, Pest,
  `php`). Since output is ordinary PHP in the same namespaces/class names, tests target the compiled
  classes directly.
- ⚠️ **No source maps yet** — you debug the emitted `.php` (readable; keep `output.comments: true`),
  not the `.tyhp`. A `tyhp↔php` XDebug proxy is planned but not implemented.
- **Verify erasure:** read the generated PHP under `build/` to confirm how a feature lowered (structs
  → arrays, generics erased, `async` → `\Tyhp\Promise`, `using` → try/finally). See [guide §29](../guide/29-php-mapping.md) for the
  mapping table.
