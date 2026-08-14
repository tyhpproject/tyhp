## 3. Build & CLI workflow (what works today)

**Dev loop:** `tyhp lint` (fast type-check, no output) → `tyhp build` (emit) → run/test the emitted PHP.

**Commands:**

| Command | Status | Use |
|---------|--------|-----|
| `build` | ✅ | parse→bind→check→emit→write to `output.path`. |
| `lint [paths]` | ✅ | same checks, no emit; accepts positional files; `--format=text\|json\|sarif`, `--file`, `--quiet`. |
| `tokenize [paths]` | ✅ (debug) | lexer output as JSON (`--out=`, `--mode=php\|tyhp\|tyhpdef`). |
| `dump_ast` / `dump-ast` | ✅ (debug) | parse tree as JSON (no bind/check). |
| `clear_cache` | ✅ | delete the AST parse cache. |
| `init`, `composer`, `generate_tyhpdef`, `watch`, `language_server`, `xdebug_proxy`, `version --json` | ⚠️ | stubs / not wired — don't rely on them. |

**`build` flags:** `--clean` (wipe outputs + build state, force full rebuild), `--dry-run` (check +
emit, no write), `--strict` (warnings block emit; note `lint` ignores `--strict`), `--verbose`,
`--no-cache`, `--quiet`.

**Incremental:** state lives in `{output.path}/tyhp-build-state.json` (source hashes, config hash,
compiler version, written output paths). Unchanged inputs + existing outputs → "Nothing to build". If
you deleted outputs it rebuilds; if the cache seems stale use `--clean` / `--no-cache` / `clear_cache`.

**Diagnostics:** console format `file(line,col): error TYHP####: message` (see [guide §27](../guide/27-diagnostics.md)).
Machine-readable lint output is available via `tyhp lint --format=json` (single JSON document) and
`tyhp lint --format=sarif` (SARIF v2.1.0). Progress/banner text is suppressed from stdout for those
formats (progress goes to stderr) so stdout stays clean for CI. Use `--quiet` to suppress the banner
and progress messages in text format.
