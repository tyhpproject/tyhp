# Story 20.5 — PHP version gating conformance

`declare(php="…")` and `#[\Tyhp\Php]` are compile-time gates against `output.phpVersion`.
Suites:

| Suite | Action | What it covers |
|-------|--------|----------------|
| `diagnostics/` | lint | 4300–4306, sibling alternates (not 4302), tyhpdef attributes, struct/extension reject |
| `matrix/` | build | Same sources at PHP **8.2 / 8.3 / 8.4 / 8.5**; emit stripping |

Tyhpdef file/block `declare(php=…)` does not parse yet (see `FOUND_BUGS.md`). Tyhpdef fixtures use `#[\Tyhp\Php]`. Tyhp `struct` / `extension` cannot parse inside `declare(php=…) { }`; 4304 is tested at top level, and extensions are gated with file-level `declare(php=…);` in `matrix/`.
