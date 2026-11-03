# Implementation Plan: Story 22.1 — Static-Analysis Docblocks & Tyhp Templates

> **Roadmap position:** Story 22.1 — **Tier 3 — Advanced**, the first Tier 3 item (additive sub-story,
> numbered after the deferred Story 22; it does not depend on 22).
> **Direct dependencies (new numbering):** 08 (checker types), 08.5 (symbol-name types → `class-string`),
> 09 / 11 (emitter), 15 (interop contract), 19.5 (editor grammars, Part B only). Part B also needs
> `FOUND_BUGS.md` #40 fixed first.
> **New story:** Part A lifted from Story 31 Idea 4; Part B lifted from Story 31 Idea 18. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`). New `tyhp.json` keys proposed
> here are **provisional** and must be registered in `CONVENTIONS.md` §4 when implementation starts. See
> `ROADMAP.md` for the tiered sequence.

> **Source:** Public beta feedback on the r/PHP launch thread (2026-11): three commenters asked whether the
> emitted PHP carries PHPDoc types for PHPStan, Psalm, and IDEs, and one pointed out that `<?=` has no Tyhp
> equivalent for HTML templates.
> **Branch:** TBD
> **Generated:** 2026-11-03
> **Status:** Planned, priority 1 in Tier 3. Not started. The two parts are independent and can ship in either
> order.

---

## Table of Contents

- [Summary](#summary)
- [Part A — Emit Static-Analysis Docblocks (PHPStan/Psalm/PHPDoc)](#part-a--emit-static-analysis-docblocks-phpstanpsalmphpdoc)
- [Part B — Tyhp Templates (`<?=` as a Tyhp Echo in `.tyhp` Files)](#part-b--tyhp-templates--as-a-tyhp-echo-in-tyhp-files)
- [Documentation & AIDevGuide Phase](#documentation--aidevguide-phase)
- [Out of Scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)

---

## Summary

- **Part A** — the emitter generates `@param`, `@return`, `@var`, `@template`, `@extends`, and related tags
  wherever the native PHP declaration loses type information (generics, array shapes, `class-string`, callable
  signatures), so PHPStan, Psalm, and PhpStorm see the types Tyhp checked.
- **Part B** — in a `.tyhp` file, `<?= expr ?>` is a Tyhp echo, so HTML templates can be written in Tyhp as typed
  functions.

---

## Part A — Emit Static-Analysis Docblocks (PHPStan/Psalm/PHPDoc)

> **Prerequisites:** Story 06 (built-in type surface), Story 08 (checker — the authoritative source of the precise
> types), Story 09/11 (emitter — where docblocks are attached today via `ApplyDocComment`). Interacts with
> Story 08.5 (symbol-name types → `class-string`), Story 25 (`internal` → `@internal`), and Story 31
> Phase 3 (canonicalization of symbol strings — extended to docblock type positions when that story lands).

### Summary

Teach the emitter to **generate the static-analysis docblock tags** (`@param`, `@return`, `@var`, `@template`,
`@extends`, `@property`, …) that carry the type information native PHP declarations **cannot** express — generics,
array shapes, `class-string`, callable argument types, refined scalars — so the emitted PHP type-checks under
PHPStan/Psalm and autocompletes in IDEs with the same precision as the Tyhp source.

Today the emitter only *passes through* author-written docblocks (`TyhpEmitter.Helpers.ApplyDocComment` →
`EmitItem.AttachDocComment`, gated on `Config.IncludeComments`) and already **reads** docblocks for its own emitter
directives (`@tyhpEmitterStart(onCall)`, `inheritFrom`, … — see `Tyhp/TyhpLang/Emitter/readme.md`). It generates
**no** analysis tags. This part adds a generator that derives tags from the checker's typed signatures and merges
them with any author docblock (without disturbing the emitter directives).

> **Note (2026-07-29):** that pass-through only became true on 2026-07-29. Until then no docblock reached any
> emitted file, and a class-level one silently deleted the class body — see FOUND_BUGS.md items 19–21. Any
> estimate that assumed a working pass-through baseline predates that fix.

### Motivation

Tyhp's value proposition is static types richer than PHP's runtime type system. When those lower to native PHP, the
precision is lost at the language level:

| Tyhp declares | Native PHP can only say | Precision lost |
|---------------|--------------------------|----------------|
| `array<int, User>` | `array` | key + value types |
| `array<User>` / a list | `array` | value type; list-ness |
| `Collection<User>` | `Collection` | element type |
| `callable<int, string>` | `callable` | signature |
| `__ClassName` / `__ClassName<T>` (symbol type) | `string` | it's a `class-string` / `class-string<T>` |
| generic class `Box<T>` | `Box` | the type parameter entirely |
| struct `Money` | `array` | the shape |

PHPStan/Psalm/PhpStorm recover all of it from docblocks. Emitting them means downstream consumers of the generated
PHP (vendored Tyhp packages, mixed Tyhp+PHP projects, third parties who never see `.tyhp`) get full type safety and
IDE support "for free," and the generated code passes a strict static-analysis baseline instead of collapsing to
`mixed`/`array`.

`LANGUAGE_ASSESSMENT.md` §4.3 / §6.6 names this as the cheapest way to narrow the two-world gap: a struct-typed
parameter today emits as bare `array`, so PHPStan/Psalm users lose even the shape they would have written by hand.
`@param array{cents: int, currency: string}` (and `@template` on erased generics) is that recovery. Runtime
enforcement of the same facts is Story 44; this part is static-only.

### What we can emit ("everything we possibly can")

Emit each tag **wherever the checker has the information and the native declaration is lossier than the docblock**:

- **Signatures:** `@param` (precise type per parameter), `@return`, `@param-out` (by-ref outputs),
  `@return $this`/`static`/`self`, and `@throws` *(only if/when the checker tracks thrown types — otherwise
  deferred; see `DESIGN_OPEN_QUESTIONS.md` §7)*.
- **Generics:** `@template` (with bounds `@template T of X`; variance `@template-covariant`/`-contravariant` once
  Tyhp models it — Story 47; defaults `@template T = X`), and `@extends` / `@implements` / `@use` for
  generic parents / interfaces / traits.
- **Properties & magic:** `@var` on properties whose native type is lossy; `@property` / `@property-read` /
  `@property-write` for accessor/magic properties (ties into `HasPropertyAccessors`); `@method` for magic methods.
- **Class/member modifiers:** `@readonly` / `@immutable`, `@final`, `@internal` (from `internal` visibility,
  Story 25), `@deprecated` (from Tyhp's deprecation markers — `T_TYHPDEF_DEPRECATED` / `_OBSOLETE`), `@api`.
- **Constants:** `@var` (or `@type`) for constant value types.
- **Refined types the dialects understand:** array shapes `array{id: int, name: string}`, `list<T>`,
  `class-string<T>` (from Story 08.5 symbol-name types), `callable(T): U`, `key-of<…>` / `value-of<…>`, literal and
  `int<min,max>` / `non-empty-string` / `positive-int` refinements *where the checker can prove them*.

### Where it hooks

1. **`DocblockTypeSpellingHelper` (new)** — a companion to the existing `TypeSpellingHelper` that renders a checked
   type into a **PHPStan/Psalm type string** (the lossy-recovery counterpart to the native spelling). Single source
   of truth for the mapping table above.
2. **A docblock builder** — per emitted declaration (function/method/property/class/const), assemble the tag set from
   the checked signature via the helper, then **merge** with the author's `node.DocComment` and hand the result to a
   merge-aware `ApplyDocComment`.
3. **Emit gating** — on by default in every build; `emit.docblocks: "off"` is the only switch that disables it
   (below). Not tied to `Config.IncludeComments`.

### Merge policy (with author docblocks and emitter directives)

The author docblock is parsed into: free-form summary/description, structured analysis tags, and **Tyhp emitter
directives**. Then:

- **Emitter directives are untouchable.** `@tyhpEmitterStart(onCall)`, `inheritFrom`, signed blocks, etc. pass
  through verbatim — they drive emission and must never be reordered, deduped, or dropped.
- **Author-written analysis tags win per key.** If the author already wrote `@param $x …` or `@return …`, keep it
  (the author may encode a refinement the checker can't infer); the generator only **fills gaps**.
- **Summary/description prose is preserved** and kept above the generated tags.
- **Lint on contradiction (optional):** if an author tag provably contradicts the checked type, emit a warning rather
  than silently overriding either way.
- **Deterministic ordering** of generated tags (params in signature order, then `@return`, etc.) for stable diffs.

### Configuration

Provisional `tyhp.json` keys (register in `CONVENTIONS.md` §4 when implementation starts; `Tyhp/Config/Project.cs`
authoritative):

| Key | Meaning |
|-----|---------|
| `emit.docblocks` | `"off"` \| `"lossy-only"` \| `"all"`. Default `"lossy-only"` (emit only where the docblock beats the native type). `"off"` disables generated docblocks. |
| `emit.docblockDialect` | `"phpstan"` \| `"psalm"` \| `"both"` \| `"phpdoc"`. Default `"both"` (shared syntax; prefixed `@phpstan-`/`@psalm-` variants only where they diverge). |

Generated docblocks are emitted in every build profile (`build.profile` `debug`, `balanced`, and `release`) and
regardless of `output.comments`: they carry type information for PHPStan, Psalm, and IDEs, and published packages
are usually release builds. `output.comments: false` still strips author comments; when an author docblock is
stripped, its analysis tags are kept in the generated block and its prose is dropped.

### Canonicalization

Generated docblock types frequently **name symbols** — `class-string<App\User>`, `Collection<App\Models\Order>`,
FQNs in `@param`/`@return`/`@extends`. Those symbol names are emitted fully qualified, with `use … as X` aliases
expanded to the real FQN. When Story 31 lands, its Phase 3 lowering/relocation invariant must include the
symbol-name positions inside emitter-generated docblocks, so a relocated or lowered symbol is rewritten to its
emitted home. (Author-written docblock text is treated as opaque unless it is a recognized analysis tag the
generator also manages.)

### Decisions (defaults chosen)

1. **Dialect → PHPStan/Psalm shared syntax** (`emit.docblockDialect="both"`); prefixed variants only where they diverge.
2. **Volume → `lossy-only`** by default (skip redundant `@param int $x` when native `int $x` already says it); `all` available.
3. **Merge → author tags and emitter directives win / are preserved; generator fills gaps only.**
4. **Profile → on in every build by default** (all `build.profile` values, independent of `output.comments`);
   `emit.docblocks: "off"` disables it.
5. **Canonicalization → generated docblock symbol names are fully qualified** and alias-expanded.
6. **`@throws`/purity/variance → emit only where the checker actually has the data**; otherwise deferred, not guessed.

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Clobbering Tyhp emitter directives in a docblock | Directives are parsed out and passed through verbatim; never deduped/reordered (Merge policy) |
| 2 | Generated type contradicts the native declaration → tool errors | `DocblockTypeSpellingHelper` derives from the same checked type as the native spelling; they cannot disagree by construction |
| 3 | Author tag conflicts with generated tag | Author wins per key; optional contradiction lint (Decision 3) |
| 4 | Docblock byte bloat in production | `lossy-only` default skips tags the native type already states; docblocks do not execute; `emit.docblocks: "off"` for projects that want none (Decision 4) |
| 5 | Stale symbol names after lowering/relocation | Fully qualified names now; Story 31 Phase 3 invariant extended when it lands |
| 6 | Dialect drift (PHPStan vs Psalm syntax differences) | Default to shared syntax; emit prefixed variants only where required |
| 7 | Over-claiming refinements the checker can't prove (`positive-int`, etc.) | Emit refined types only when statically proven; else fall back to the broad type |
| 8 | Round-trip / re-emit stability | Deterministic tag ordering; generator is idempotent over its own output |

### Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-output) golden fixtures under
`tests/conformance/story22.1/docblocks/` with a `manifest.json` the runner asserts.

- **Generics:** `array<int, User>` param emits `@param array<int, User> $x` alongside native `array $x`; a generic
  class emits `@template` + `@extends`/`@implements`.
- **Structs:** a struct-typed parameter emits `@param array{…} $x` with the struct's keys, including optional
  (nullable) keys as `key?:`.
- **class-string:** a Story-08.5 `__ClassName` param emits `@param class-string $c` (and `class-string<T>` when bound).
- **Callable:** `callable<int, string>` emits `@param callable(int): string`.
- **Lossy-only default:** a plain `int $x` emits **no** redundant `@param int $x`; `all` mode does.
- **Merge:** an author docblock with a summary + `@throws` + `@tyhpEmitterStart(onCall)` keeps all three, gains the
  generated `@param`/`@return`, and does not duplicate an author-written `@return`.
- **Directive safety:** `@tyhpEmitterStart(onCall)` / signed blocks emit byte-identical to today.
- **Profiles:** `debug`, `balanced`, and `release` builds all include docblocks; `output.comments: false` keeps
  generated tags; `emit.docblocks: "off"` emits none.
- **Aliases:** a `use App\Models\User as U;` alias inside a generated `@param Collection<U>` emits the real FQN.
- **Static-analysis gate:** run PHPStan (max level) / Psalm over an emitted fixture package and assert zero type
  errors that the docblocks are responsible for resolving.

---

## Part B — Tyhp Templates (`<?=` as a Tyhp Echo in `.tyhp` Files)

> **Prerequisites:** `FOUND_BUGS.md` #40 (inline HTML emits in source order, `<?tyhp` reopens after HTML,
> `<?php` blocks rejected in `.tyhp`). Story 19.5 for editor highlighting.

### Summary

`.tyhp` files can output inline HTML between `<?tyhp` blocks, but there is no Tyhp echo tag. `<?=` is PHP's tag and
lexes in PHP echo mode. Once #40 bans `<?php` blocks from `.tyhp` files, `<?=` has no other meaning there, so in a
`.tyhp` file it becomes a Tyhp echo: `<?= expr ?>` is `echo expr;` with the expression parsed and checked as Tyhp.
`.php` files are unchanged.

Because `include` / `require` are always TYHP4801, a Tyhp template is a function or method, and its parameters are
the template's typed contract:

```tyhp
<?tyhp

function userCard(User $user): void { ?>
    <h1><?= $user->name ?></h1>
<?tyhp }
```

### Decisions (defaults chosen)

1. **Tag** — `<?=` in a `.tyhp` file lexes in Tyhp mode (Tyhp keywords such as `is`, `typeof`, `with`, and
   `nameof` are active). No new tag. `<?tyhp=` was considered and rejected as too long for templates.
2. **Semantics** — same as PHP's `<?=`: an `echo` of an expression list, no escaping. The expression must satisfy
   the existing `echo` rules (stringable, TYHP4120).
3. **Escaping** — not in this story. An escape-by-default echo (Blade `{{ }}` style) would be a separate, opt-in
   addition; changing what `<?=` prints would surprise PHP developers.
4. **Control flow** — loops and conditionals in templates use `<?tyhp … ?>` blocks. Confirm PHP's alternative syntax
   (`foreach (…):` / `endforeach;`, `if (…):` / `endif;`) parses and checks in Tyhp mode, and support it if it
   does not.
5. **Tagless source** — `source.tagless` forbids `?>`, so templates need tagged files. No change.
6. **Capturing output** — `\ob_start()` / `\ob_get_clean()` around the call, as in PHP. No helper in this story.
7. **Editors** — the VS Code and PhpStorm grammars in `tyhp-ide-plugin` highlight HTML outside `<?tyhp` blocks and
   Tyhp inside `<?=`.

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | `<?=` means different things in `.php` and `.tyhp` files | The file extension already decides the language for `<?tyhp`; document it on the open-tag page |
| 2 | Unescaped output in HTML | Same as PHP today; escaping is a separate opt-in addition (Decision 3) |
| 3 | Inline HTML inside nested blocks and closures | Covered by #40's source-order emit; add fixtures for function bodies, `foreach`, `if`, and closures |
| 4 | Sourcemap lines in templates | Fixture that maps an error inside `<?= ?>` back to the `.tyhp` line |

### Golden Fixtures / Tests (Acceptance)

Fixtures under `tests/conformance/story22.1/templates/`:

- `<?= $user->name ?>` inside a function emits `echo $user['name'];` (struct) or `echo $user->name;` (class) in
  source order, and the rendered output matches the template.
- `<?= $count->foo ?>` on an `int` reports TYHP4101; a non-stringable expression reports TYHP4120.
- `<?= $x is string ? $x : '' ?>` parses with the `is` keyword active.
- `foreach (…):` / `endforeach;` around inline HTML renders each item.
- `<?=` in a `.php` file is unchanged (passes through).

---

## Documentation & AIDevGuide Phase

Each part ships with its docs in `tyhp-docs-src` and the AI dev guide in `tyhp-ai-dev-guide`, including the
matching `REGEN.md` item so a regeneration keeps the change.

- **Part A:** a new docs page on emitted docblocks; `project_optionsList.md` and `cli_build.md` (new `emit.*`
  keys); `cli_interopContract.md` (what PHP consumers can rely on); `faq_general.md` (using PHPStan/Psalm with
  Tyhp output); `handbook/` interop section in the guide.
- **Part B:** a new templates page; `tyhp_0000_openTag.md` (`<?=` in `.tyhp` files);
  `tyhp_2900_lostFunctionality.md` (Short Open Tags); `guide/02-files-and-tags.md` and
  `guide/28-availability-gotchas.md` in the guide.

---

## Out of Scope

- Runtime enforcement of struct shapes or generic bounds (Story 44).
- Mapping production stack traces back to `.tyhp` lines (`DESIGN_OPEN_QUESTIONS.md` §22).
- Escape-by-default echo, template inheritance, layouts, or a template engine.
- Declaration-site variance syntax (Story 47). Part A emits `@template-covariant` /
  `@template-contravariant` only once that exists.

---

## Cross-Story References

- **Story 31 (Tyhp Link), Phase 3** — extends its relocation invariant to generated docblock symbol names. Its
  minified release bundles must keep generated docblocks, since they are on in every build (Story 31 risk 13).
- **Story 44** — the runtime counterpart to Part A's static docblocks.
- **Story 47** — variance; Part A emits the matching `@template-*` tags once it lands.
- **Story 25** — `internal` → `@internal`.
- **`FOUND_BUGS.md` #40** — must be fixed before Part B.
