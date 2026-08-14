# Tyhp PHP extension overlays (Layer 3)

Preservation baseline for **hand-authored / hand-enriched** PHP extension tyhpdefs.

This directory is **Layer 3** of the Story 20 three-layer model:

1. **Baseline** — Reflection-generated signatures  
2. **Enrichment** — harvested from Psalm / PHPStan / Phan / PhpStorm stubs (with attribution)  
3. **Overlays (this tree)** — Tyhp-owned edits that regen must not destroy  

## What is here

Full-file copies of the live trees as of the snapshot recorded in `MANIFEST.txt`:

| Path | Source |
|------|--------|
| `php8.2.9/*.tyhpdef` | `runtime/php-extensions/php8.2.9/` (hand-enriched Core/Standard/SPL/…) |
| `Decimal/ExtDecimal.tyhpdef` | `runtime/php-extensions/Decimal/` (operators / convert overloads) |

These copies intentionally include **everything** that was in the live files at snapshot time (Reflection surface **plus** generics, overloads, type guards, language constructs, etc.). Until programmatic overlay apply exists, this is the recovery source of truth if a regen overwrites the live tree.

## What loads today

The compiler still loads **`runtime/php-extensions/php8.2.9/`** (and `Decimal/`) via project / package manifests.  
**This `overlays/` tree is not loaded automatically.**

## Rules

- **Do not** treat regenerators as free to delete or rewrite this directory.
- Prefer writing new Reflection baselines to staging (`tyhpdef_gen/`) and merging deliberately into the live tree.
- Future markers (not required on these snapshot files yet):

  ```tyhpdef
  // @generated
  // @generated-original: …
  // @tyhp-overlay … @end-tyhp-overlay
  ```

- When overlay apply tooling lands, these files (or diffs derived from them) become the input; until then, if the live tree is damaged, **restore from here**.

## Related docs

- `IMPLEMENTATION_PLAN_TODO_STORY_20.md` — Three-Layer Generation Model, Phase 8  
- `IMPLEMENTATION_PLAN_TODO_STORY_20.5.md` — `declare(php=…)` / `#[\Tyhp\Php]` gates  
- `IMPLEMENTATION_PLAN_TODO_STORY_21.md` — `tyhp/php` package + stub harvest  
- `runtime/README.md` — stub corpus URLs  
