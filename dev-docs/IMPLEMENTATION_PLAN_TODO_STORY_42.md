# Implementation Plan: Story 42 — Anti-Glob (`!` Prefix) in JSON Config Glob Arrays

> **Roadmap position:** Story 42 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 10, 21, 29.1
> **New story:** split from Story 31 Idea 13 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.
> **Pre-stable reservation (Story 29.1 Part D):** before the first stable release, a leading `!` in every glob
> array listed below reports `ConfigInvalidGlobPattern`, so this story only gives an error case a meaning.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).
> Config-string syntax, not a language construct: update `project_optionsList.md`, `faq_project.md`,
> `tyhpdef_overlays.md`, and add a glob-string note in `CONVENTIONS.md` §4 (no new keys; `!` is a prefix on
> existing glob arrays).

> **Status:** Config-string syntax only; no language change.
> **Prerequisites:** Story 10 (`tyhp.json` `include` / `exclude` / `tyhpdefInclude` / `tyhpdefExclude`), Story 21
> (`extra.tyhp.package` `include` / `overlay`). Story 38’s `plugin.tyhp.json` if that manifest grows glob arrays.
> **Related:** `Project.GetProjectSourceFiles()` (`Microsoft.Extensions.FileSystemGlobbing.Matcher` + separate
> `exclude`), `TyhpdefOverlayApplier.ExpandGlob` (hand-rolled, no exclude today). `extra.tyhp.package` has no
> `overlayExclude` key — that is the gap this prefix fills.
> **Source:** JSON glob-array exclude brainstorm, 2026-08-26.

## Summary

Every glob string in a JSON configuration array may start with `!`. That entry is an **anti-glob**: it does not
load files. After the other sibling globs in the **same array** expand to a path set, any path that matches an
anti-glob in that array is dropped.

```json
{
    "include": [
        "./src/**/*.tyhp",
        "!./src/**/*.draft.tyhp",
        "!./src/generated/**"
    ]
}
```

`./src/app.tyhp` loads. `./src/app.draft.tyhp` and anything under `./src/generated/` do not, even though they
match `./src/**/*.tyhp`.

The same prefix works in every glob-valued JSON array the toolchain already has, and in any later one:

| File | Arrays |
| --- | --- |
| `tyhp.json` | `include`, `exclude`, `tyhpdefInclude`, `tyhpdefExclude` |
| `extra.tyhp.package` | `include`, `overlay` |
| `plugin.tyhp.json` (Story 38) | any glob array that file grows |

No new keys. `!` is a prefix on an existing string, not a sibling `"exclude"` field.

## Why overlay needs this

`tyhp.json` already has `exclude` / `tyhpdefExclude`. `extra.tyhp.package` `"overlay"` does not. Today authors
split overlay globs so a later pattern cannot recurse into an earlier folder:

```json
"overlay": [
    "./_tyhpdef/overlays/stubs/*.tyhpdef",
    "./_tyhpdef/overlays/*.tyhpdef"
]
```

`overlays/*.tyhpdef` does not recurse into `stubs/` only because `*` is one path segment. A recursive glob
cannot say “everything under overlays except this subtree” without a second key. Anti-glob is that spelling:

```json
"overlay": [
    "./_tyhpdef/overlays/**/*.tyhpdef",
    "!./_tyhpdef/overlays/wip/**"
]
```

`tyhp.json` authors may use either a sibling `exclude` array **or** `!` inside `include`. Both remain valid;
they compose (a path dropped by either mechanism is not loaded).

## Semantics

1. **Leading `!` only.** The first character of the JSON string is `!`. `./!draft.tyhp` is a literal path whose
   name starts with `!`. Whitespace before `!` is not an anti-glob (`" !foo"` is a normal glob, almost certainly
   invalid). Strip the `!`, then trim the remainder; a bare `"!"` (empty pattern) is `ConfigInvalidGlobPattern`.
2. **Per array, not per file.** `!` in `include` does not affect `overlay` or `tyhpdefInclude`. Each array
   computes its own path set.
3. **Union then subtract.** Collect the union of all non-`!` siblings. Subtract the union of all `!` siblings.
   Position of `!` entries in the array does not matter. A later positive glob does **not** re-include a path
   an anti-glob already dropped (this is not gitignore).
4. **Anti-globs never add files.** An array that is only `!` patterns yields an empty set (warn once).
5. **Exclude-style arrays use the same set math.** `"exclude": ["./src/tests/**", "!./src/tests/keep.tyhp"]`
   produces an exclude set that omits `keep.tyhp` — the `!` subtracts from sibling *exclude* globs, so that file
   is not excluded. Same rule, different use of the resulting set.
6. **Overlay last-wins is among remaining files.** Expand positive overlay globs in array order (lexicographic
   within one glob, as today). Drop any path that matches an anti-glob in that overlay array. Last-wins /
   `omit` / `partial` then run on what is left. Anti-globs are not overlay files.
7. **Same glob language as today.** `*`, `**`, `?`; paths relative to the project or package root that already
   owns the JSON file. Case-matching follows the existing expander for that call site.

## Relationship to `exclude`

| Want | Write |
| --- | --- |
| Skip drafts from project sources | `"include": ["./src/**/*.tyhp", "!./src/**/*.draft.tyhp"]` **or** a separate `"exclude"` entry |
| Skip a subtree from overlays (no `overlayExclude` key) | `"overlay": ["./_tyhpdef/overlays/**/*.tyhpdef", "!./_tyhpdef/overlays/wip/**"]` |
| Keep stubs listed first, then hand overlays, without pulling stubs twice | Keep today’s two-glob split **or** anti-glob the stubs folder on a recursive hand-overlay glob. Do **not** anti-glob stubs if an earlier sibling is supposed to load them — subtraction applies to the whole array |

The last row is the footgun: `"overlay": ["stubs/*.tyhpdef", "overlays/**/*.tyhpdef", "!overlays/stubs/**"]`
drops the stub files from **both** siblings. Put the anti-glob only on arrays whose positives should not keep
those paths.

## Implementation sketch

One shared glob-array expander used by `Project.GetProjectSourceFiles`, tyhpdef include/exclude, overlay
load, and any later JSON glob list. Split entries on leading `!`; feed positives as includes and anti-globs as
excludes of **that** matcher (today `Matcher.AddIncludePatterns` / `AddExcludePatterns` already exist for
`tyhp.json`; overlay’s `ExpandGlob` does not — fold it into the shared helper so last-wins order is preserved
on the filtered sequence). CLI `--include` / `--exclude` / `--source` that concatenate into those arrays should
accept the same prefix so JSON and flags stay one language. `tyhp overlay create` still appends a positive glob;
it never writes `!`.

When scheduled, register the prefix in `CONVENTIONS.md` §4 as glob-string syntax on the existing keys (not a
new key). Reuse `ConfigInvalidGlobPattern` for empty / malformed anti-globs.

## Decisions (defaults chosen)

1. **`!` prefix, no new keys** — including no `overlayExclude`.
2. **Union-then-subtract per array** — not gitignore later-wins / re-include.
3. **Every JSON glob array** — one rule for `tyhp.json`, `extra.tyhp.package`, and future manifests.
4. **Existing `exclude` / `tyhpdefExclude` stay** — anti-glob is an in-array spelling, not a replacement.

## Risks & Edge Cases

- Recursive overlay glob plus `!stubs/**` unintentionally dropping Layer 2 stubs (document the footgun).
- Literal filenames that start with `!` — require `./!name.tyhp`.
- Anti-glob that matches nothing — silent (same as a positive glob that matches nothing today).
- Duplicate positives and an anti-glob that matches only some of them — subtract once from the union.
- Windows vs POSIX path separators after `!` — normalize the same way current globs do.
- Two expanders drifting (`Matcher` vs `ExpandGlob`) until the shared helper lands.

## Golden Fixtures / Tests (Acceptance)

- `include` with `./src/**/*.tyhp` + `!./src/**/*.draft.tyhp` compiles `app.tyhp` and skips `app.draft.tyhp`.
- `extra.tyhp.package` `"overlay"` recursive glob + `!…/wip/**` does not load `wip/` files; other overlay files
  still last-wins in array order.
- `"exclude": ["./src/tests/**", "!./src/tests/keep.tyhp"]` still compiles `keep.tyhp` when `include` matches it.
- Array of only anti-globs: empty set + warning; no crash.
- `"!"` / `"!   "` → `ConfigInvalidGlobPattern`.
- `./!literal.tyhp` is not treated as an anti-glob.
- Existing overlay goldens (stubs then hand, no `!`) stay byte-identical.
