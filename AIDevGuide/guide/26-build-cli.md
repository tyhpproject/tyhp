## 26. Build, output layout & CLI

`tyhp.json`:
```json
{
  "include": ["src/**/*.tyhp"],
  "exclude": ["vendor/**"],
  "source":  { "tagless": false },
  "output":  { "path": "build/", "phpVersion": "8.4", "strictTypes": true, "namespacePrefix": null },
  "psr4":    { "App\\": "src/" },
  "build":   { "decimalBacking": "bcmath", "decimalScale": 28, "structBacking": "array", "updateComposer": false }
}
```
- **Output layout** (all under `output.path`, default `build/`):
  - Classes/extensions → `build/<Namespace/Segments>/<ClassName>.php` (from the FQN; `psr4` does **not**
    change disk layout — it only feeds generated `composer.json` autoload).
  - Entry-point / non-class files → mirror the source path, `.tyhp`→`.php`.
  - Namespace-level functions → `<Namespace/Segments>/_functions.php`.
- **`composer.json`** is written/merged only when `build.updateComposer: true` (default false); it then
  adds `require` for the runtime packages you used, registers `runtime/packages/*` as Composer `path`
  repositories (`@dev`), and runs `composer install`. Otherwise the build just logs which packages you
  need.
- **Pipeline:** parse → bind → check → emit. **All-or-nothing error gate:** if there is any error
  (or, with `--strict`, any warning) **nothing is emitted** — fix diagnostics first. The checker
  itself keeps going after errors so you see many at once (capped at 100/file).
- **Incremental:** state in `build/tyhp-build-state.json`; unchanged files skip re-parse. `--clean`
  wipes outputs + state.
- **CLI (working today):** `build`, `lint <paths>`, `tokenize`, `dump_ast`, `clear_cache`. ⚠️ `init`,
  `composer`, `generate_tyhpdef`, `watch`, `version --json`, `lint --format json` are stubs — don't
  rely on them. Flags: `--clean --dry-run --strict --quiet/--verbose --no-cache`. Full workflow,
  autoloading, and interop in the `../handbook/` files.
