# Tyhp Project Conventions (Single Sources of Truth)

> **Purpose:** This file is the anti-drift reference for the whole project. Implementation-plan stories
> (`IMPLEMENTATION_PLAN_TODO_STORY_NN.md`) and the runtime/compiler code should **cite this file** rather than
> restating diagnostic-code ranges, config keys, or canonical paths. When something here conflicts with a story,
> this file wins; update the story to match.
>
> See `ROADMAP.md` for the tiered, dependency-ordered story sequence and the old→new numbering map.

---

## 1. Diagnostic Codes — Single Source of Truth

**`Tyhp/Domain/Exceptions/MessageCode.cs` is the one and only registry of diagnostic codes.**

- Stories **must not** re-declare or re-document code ranges as if they own them. If a story needs a new code,
  it adds the enum value in `MessageCode.cs` and a localized entry in the `.resx` (below) — nowhere else.
- Localized text lives in `Resources/CLI.TyhpHostedService.en-US.resx` (and `Resources/CLI.TyhpHostedService.resx`),
  keyed `ERROR_TYHP{code}` / `WARNING_TYHP{code}` / `INFO_TYHP{code}`, resolved through `Tyhp/CLI/Message.cs`
  (`LocalizeErrorCode()` / `LocalizeWarningCode()` / `LocalizeInfoCode()`).
- Audits of codes/text must read both files **dynamically** — never hardcode an audit table.

### Code numbering scheme (reference only — defined in `MessageCode.cs`)

| Range | Component |
|-------|-----------|
| 1000s | Parser / Lexer / Grammar |
| 2000s | Visitor / AST generation |
| 3000s | Binder |
| 4000s | Checker |
| 5000s | Emitter |
| 6000s | Configuration (reserved) |
| 7000s | CLI (incl. 7000–7999 sub-ranges) |
| 8000s | Tyhpdef |
| 9000s | Internal compiler errors |

> Checker note: Story 08 (checker) owns the core checker band; **feature-story** checker diagnostics
> (Stories 16, 20.5, 25, 26, 27, 28) live in the separate `4300–4399` band so they never collide. The exact
> allocations are in `MessageCode.cs` — do not duplicate them in story docs.

---

## 2. Diagnostic Message Style (enforced by Story 14)

Authoring rules for every `ERROR_TYHP*` / `WARNING_TYHP*` / `INFO_TYHP*` short message in
`Resources/CLI.TyhpHostedService*.resx`. The Story 14 consistency gate (Phase 5) enforces the
mechanical subset of these rules; humans enforce tone.

### Message anatomy

A diagnostic is composed of:

| Part | Required | Role |
|------|----------|------|
| `code` | yes | Stable `MessageCode` value (`TYHP####`); never renumber |
| `severity` | yes | `error` / `warning` / `info` / `hint` |
| `primary span` | yes | File + start (and optional end) location of the offending construct |
| `short message` | yes | One-line localized text from the `.resx` key (this section) |
| `labels` | no | Secondary spans with short labels (e.g. "defined here") — Story 14 Phase 2 |
| `help` / `note` | no | Extra prose attached to the diagnostic — Story 14 Phases 2–4 |
| `suggestion` | no | Machine-applicable edit (span + replacement) — Story 14 Phase 3 |

The `.resx` entry is **only** the short message. Help text, notes, and suggestions are not stuffed into
the short-message string.

### Short-message rules

1. **Present tense.** Describe the program as it is, not what the user "did". Prefer "is not found" /
   "does not implement" over "was not found" / "were provided".
2. **No trailing period** on the short message. Do not end with `.` (ellipsis `...` is fine when
   truncating). Prefer a single clause; if two ideas are needed, join with `;` rather than a second
   sentence.
3. **Backticks around offending symbols/types/paths/keywords.** Interpolated identifiers and types use
   `` `{0}` `` (not `'…'` or `"…"`). Language keywords and config keys cited in the message use
   backticks too (`await`, `source.tagless`, `tyhp.json`). Counts and free-form detail strings (raw
   exception text, signature digests) stay unquoted.
4. **Never blame the user.** Avoid "you" / "your" / "please" / "check your …". State the fact; put
   remediation in `help`/`suggestion` when those fields exist.
5. **Prefer "expected `X`, found `Y`"** (or "expected …, found …") over "got", "but received", or
   passive blame framing.
6. **Do not invent or renumber codes.** New diagnostics get a new `MessageCode` enum member; existing
   codes keep their numbers forever. See §1.

### Examples

```text
❌ The specified symbol "{0}" is not found.
✅ Symbol `{0}` is not found

❌ Condition must be of type 'bool', got '{0}'
✅ Expected `bool`, found `{0}`

❌ No source files found. Check your include/exclude paths in tyhp.json.
✅ No source files match the include/exclude paths in `tyhp.json`

❌ Generic type '{0}' expects {1} type argument(s), but {2} were provided
✅ Generic type `{0}` expects {1} type argument(s), found {2}
```

### Consistency

Every `MessageCode` has exactly one conforming `.resx` entry (matching severity prefix) and vice
versa, except for the small multi-severity allowlist enforced by
`Tyhp/Domain/Diagnostics/MessageConsistencyGate.cs` (`BinderUnknownError`, `LintNoSourceFiles`).
Audits and the Story 14 gate read both `.resx` files **dynamically** — never hardcode an audit
table. Run the gate via `dotnet test --filter MessageConsistencyGateTests`.

---

## 3. Canonical Paths

| Concern | Canonical location |
|---------|--------------------|
| Compiler source (C#, .NET 9 / C# 13) | `Tyhp/` |
| Diagnostic code registry | `Tyhp/Domain/Exceptions/MessageCode.cs` |
| Localized message resources | `Resources/CLI.TyhpHostedService*.resx` |
| Optional long-form explain text | `EXPLAIN_TYHP{code}` in the same `.resx` files (fallback stub via `tyhp --explain`) |
| Generated diagnostic code index | `docs/content/diagnostics_reference.md` (from `MessageCodeCatalog` / `DiagnosticsReferenceGenerator`) |
| Message consistency gate | `Tyhp/Domain/Diagnostics/MessageConsistencyGate.cs` (test: `MessageConsistencyGateTests`) |
| CLI actions / host | `Tyhp/CLI/` (`TyhpHostedService.cs`, `ActionRunnerBase.cs`, `Message.cs`) |
| Compilation pipeline services | `Tyhp/Domain/Services/` (e.g. `CompilationService.cs`, `AstCacheService.cs`) |
| Parser (ANTLR) | `Tyhp/TyhpLang/Parser/`, grammars `Tyhp/TyhpLang/Grammar/` (`TyhpLexer.g4`, `TyhpParser.g4`, `PhpParser.g4`) |
| AST / Visitor / Binder / Checker / Emitter | `Tyhp/TyhpLang/{Ast,Visitor,Binder,Checker,Emitter}/` |
| Tyhp runtime (written in Tyhp, compiled to PHP) | `runtime/packages/{core,decimal,async,lambda}/` (`tyhp_src/` → `src/`) |
| Bundled PHP-extension tyhpdefs | `runtime/php-extensions/` (local sources; load only via vendor `tyhp/php-*` or explicit `tyhp.json` includes); published layout `runtime/packages/php/` + `runtime/packages/php-ext-*` (Story 21) |
| Tests / conformance fixtures | `tests/` (see §5) |
| Project config | `tyhp.json` per project |
| Documentation site content | `docs/content/` (generated by `docs/generate_docs.php`) |

---

## 4. Canonical Config Keys (`tyhp.json`)

Use these exact keys; do not invent synonyms in story docs.

| Key | Meaning |
|-----|---------|
| `quiet` | Suppress rich/decorative console output |
| `locale` | Localization (e.g. `en-US`) |
| `include` | Glob(s) of source/tyhpdef inputs (e.g. `./src/**/*.tyhp`, `./src/**/*.tyhpdef`). Patterns ending in `.tyhpdef` or `package.tyhp.json` are also loaded as type definitions |
| `exclude` | Glob(s) to exclude |
| `tyhpdefInclude` | Glob(s) for additional tyhpdefs **and** `package.tyhp.json` manifests (vendor is auto-scanned; `runtime/` is not — list local packages here) |
| `tyhpdefExclude` | Glob(s) to exclude from the tyhpdef load set |
| `source.tagless` | Opt-in extension-driven source mode (Story 06, Phase 7). Default `false`. When `true`: the `<?tyhp`/`<?tyhpdef` open tag is optional (allowed but not required) and the closing tag `?>` is always an error. A future release may flip the default to `true`. |
| `output.path` | Build output directory (e.g. `./build`) |
| `output.namespacePrefix` | PHP namespace prefix for emitted code |
| `type` | `"library"` triggers auto-generation of `package.tyhp.json` (Story 20, Track C) |
| `build.optimizations` | Per-module optimizer toggles (optimizer modules: Stories 23–24) |
| `build.runtimeGenericChecks` | Emit runtime generic checks |
| `build.allowEval` | Permit `eval` constructs |
| `checker.*` | Checker resource limits / tooling knobs (e.g. `checker.templateStringMaxStates`, `checker.maxFixIterations`). Language strictness is unconditional — there are no toggles for null safety, required annotations, or relaxing `mixed` |
| `lint.format` | Lint diagnostic output format: `"text"` (default), `"json"`, or `"sarif"` (also CLI `--format`) |
| `lint.fix` | Whether `tyhp lint --fix` applies auto-fixes (default `false`; also CLI `--fix`) |

> The authoritative getters live in `Tyhp/Config/Project.cs`; if a key name differs there at implementation
> time, `Project.cs` wins and this table should be corrected.

---

## 5. Testing & Conformance (backbone)

- The **golden conformance fixture suite** (established in **Story 07 — Testing Infrastructure**, Phase **5A**,
  pulled forward to Tier 0) is the project backbone. Every story adds `.tyhp → .php` (+ expected-diagnostics)
  golden fixtures under `tests/conformance/storyNN/<feature>/` with a **`manifest.json`** the runner asserts;
  the committed fixtures are the source of truth for expected compiler output.
- The .NET test project lives under `tests/Tyhp.Tests/`; PHP runtime tests accompany `runtime/packages/*`.
- Each story's "Golden Fixtures / Tests (Acceptance)" subsection states its fixture obligations uniformly.

### Runtime self-host conformance ("compiler builds its own runtime")

The Tyhp runtime is maintained as **dual sources**: Tyhp source (`runtime/packages/*/tyhp_src/`) and the committed
compiled PHP (`runtime/packages/*/src/`). To catch drift between the two, the conformance suite includes a
**runtime self-host check**: recompile the Tyhp runtime sources with the current compiler and **diff** the result
against the committed PHP. A clean diff is the "compiler builds its own runtime" milestone. Any runtime-affecting
story must keep this check green (see its acceptance subsection).

---

## 6. tyhpdef Regeneration — Baseline + Overlay (non-destructive)

Generated type surfaces (`.tyhpdef`, `package.tyhp.json`) must survive regeneration without clobbering hand-tuned
signatures. Adopt the **"generate baseline + curated, idempotent, non-destructive overlay"** pattern (Stories 20, 21):

- **Baseline:** mechanically generated from PHP reflection / from the compiled Tyhp public API. Always safe to
  regenerate from scratch.
- **Overlay:** curated, hand-authored refinements (e.g. generic parameters on PHP built-ins like `Iterator<TKey,TValue>`,
  type-guard signatures, `T = DefaultType` defaults). Overlays are applied **on top of** the baseline idempotently and
  are **never overwritten** by regeneration.
- Regeneration = re-derive baseline, then re-apply overlays. The author's hand-tuned signatures are preserved.

---

## 7. PHP Code Conventions (for emitted/runtime PHP)

- Root-level built-ins and global classes are **backslash-prefixed**: `\array_replace`, `\str_contains`,
  `\Tyhp\ObjectHelper`, etc.
- Prefer `catch (\Throwable $e)` over catching `\Exception`.
- Compiled output targets the PHP version matrix defined by Story 21 (`tyhp/php-{phpVersion}` packages).

---

## 8. Interop contract (Story 15)

Canonical user-facing doc: **`docs/content/cli_interopContract.md`**. Cite that page (and this section) rather than
restating synthetic names or the full runtime surface in story plans.

| Concern | Location |
|---------|----------|
| Contract version (runtime) | `extra.tyhp.interopContractVersion` in each `runtime/packages/*/composer.json` |
| Contract version (compiler) | `Tyhp.TyhpLang.Interop.InteropContract.CurrentVersion` |
| Version mismatch diagnostic | **TYHP5018** (error) — registered only in `MessageCode.cs` |
| Operator / convert synthetic names | `OperatorMethodNameGenerator` (not duplicated here) |
| Generics / property-hook synthetic names | `GeneratedNames` (not duplicated here) |
| Enumerable emitter-required symbols | `InteropContractSurface` |

**Principle:** the emitter design dictates the runtime API, not vice versa. Breaking emitted names or signatures
bumps `interopContractVersion` on both sides; see also `VERSIONING.md` (interop contract vs semver).

---

## 9. Git / Working-Tree Hygiene

- The roadmap restructure is delivered as **uncommitted working-tree changes**; do not commit as part of it.
- Ignore `._*` AppleDouble files entirely (never read/edit/rename/delete them).
- Renames preserve history via `git mv` for tracked files (untracked files are plain-moved).
