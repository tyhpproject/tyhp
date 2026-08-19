# Tyhp PHP extension overlay backup (not loaded)

This directory is a **disaster-recovery snapshot** of hand-authored / hand-enriched PHP extension tyhpdefs that already existed in the live trees. It is **not** the overlay mechanism and is **not** loaded by the compiler.

Story 21 Layer 2 stub harvest and Layer 3 hand overlays are separate tyhpdef files under each package's `_tyhpdef/overlays/` (`stubs/` first, hand-written last; `package.tyhp.json` `"overlay"` array order, last wins).

## What is here

Full-file copies of the live trees as of the snapshot recorded in `MANIFEST.txt`:

| Path | Source |
|------|--------|
| `php8.2.9/*.tyhpdef` | `runtime/php-extensions/php8.2.9/` (hand-enriched Core/Standard/SPL/…) |
| `Decimal/ExtDecimal.tyhpdef` | `runtime/php-extensions/Decimal/` (PECL Decimal — becomes `tyhp/php-ext-decimal`, not `tyhp/decimal`) |

Until those hand edits are extracted into package overlay files, this tree is the recovery source if a regen overwrites the live harvest. Regenerators **must not** delete or rewrite this directory.

## Related docs

- `IMPLEMENTATION_PLAN_TODO_STORY_20.md` — Three-Layer Generation Model
- `IMPLEMENTATION_PLAN_TODO_STORY_21.md` — `tyhp/php` / `tyhp/php-ext-*` + load-time overlays
- `CONVENTIONS.md` §6 — baseline + overlay
- `runtime/README.md` — stub corpus URLs
