# Regenerating the Tyhp language guide

Use this when Tyhp changes (new features, cleared gotchas, syntax updates) and the guide needs to be
rebuilt. It contains (1) context on how the guide was produced and (2) a ready-to-use prompt.

Run this **from inside the Tyhp compiler repo** (this repo), because the prompt relies on reading the
compiler's grammar, source, stories, and roadmap. Output is the `AIDevGuide/` bundle:

```
AIDevGuide/
  AGENTS.md  CLAUDE.md  QUICK_GUIDE.md  README.md  REGEN.md
  guide/     00-index.md + 01-…-29-*.md   (the dense language delta, one file per section)
  handbook/  00-index.md + 01-…-07-*.md   (setup/CLI/interop/testing/examples/runtime API)
```

The content is authored as two logical documents (the **guide**, ~29 sections, and the **handbook**,
~7 sections) but written out **one file per section** so an agent loads only what it needs.
`QUICK_GUIDE.md` is the one-line-per-feature index whose `→` pointers name the exact section files;
`AGENTS.md`/`CLAUDE.md` are the entry points.

---

## How the guides are built (method)

1. **Authoritative sources only** (everything else in the repo may be stale):
   - Grammar: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`, `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` (syntax = ground truth).
   - Current compiler code under `Tyhp/TyhpLang/**` and runtime under `runtime/packages/**`
     (**behavior/emit = ground truth; code wins over stories**).
   - Implementation stories `IMPLEMENTATION_PLAN_TODO_STORY_*.md` (intent/semantics).
   - `ROADMAP.md` (sequencing, what's planned vs done).
   - Concrete fixtures `tests/Tyhp.Tests/TestData/ValidTyhp/**/*.tyhp` and emitter tests
     `tests/Tyhp.Tests/Emitter/*.cs` (real syntax + expected PHP output).
2. **Explore in parallel, then verify.** Extract semantics per area (type system, checker/narrowing,
   emitter/PHP lowering, tyhpdef/runtime/config) — then confirm emit/availability claims against the
   emitter and checker code before writing.
3. **Maturity is author-facing, not compiler-jargon.** For each feature decide: compiles cleanly →
   "use freely"; parses/checks but emit is broken or missing → "use the PHP form instead"; not in the
   toolchain → "not in the language yet." Base this on the **code**, not the stories.

---

## The prompt (paste to an agent running in the Tyhp compiler repo)

> **Task:** Regenerate the Tyhp AI dev docs under `AIDevGuide/` (English). Author the language guide
> (~29 sections) and the handbook (~7 sections), but **write one file per section** into
> `AIDevGuide/guide/NN-slug.md` and `AIDevGuide/handbook/NN-slug.md`; also (re)produce
> `QUICK_GUIDE.md`, `AGENTS.md`, `CLAUDE.md`, and a `00-index.md` in each folder. Overwrite existing
> files. Produce **English only**.
>
> **Audience:** an AI agent that is already 100% proficient in PHP 8.x and will write/maintain a Tyhp
> *application* (not the compiler). The guide is a dense, token-efficient info dump of only what is
> **new or different** vs PHP, and how each construct maps back to PHP. Anything that behaves like
> PHP is omitted.
>
> **Sources of truth (use only these; other repo docs may be outdated):**
> - `Tyhp/TyhpLang/Grammar/TyhpParser.g4`, `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` — syntax.
> - Current code in `Tyhp/TyhpLang/**` and `runtime/packages/**` — **behavior and PHP emission; the
>   code wins over any story when they disagree.**
> - `IMPLEMENTATION_PLAN_TODO_STORY_*.md` — semantics/intent.
> - `ROADMAP.md` — planned vs done.
> - `tests/Tyhp.Tests/TestData/ValidTyhp/**/*.tyhp` and `tests/Tyhp.Tests/Emitter/*.cs` — real syntax
>   and expected PHP output.
> Prefer delegating the per-area extraction to parallel exploration subagents, then verify emit and
> availability claims directly against the emitter/checker code.
>
> **Content — cover every section below, in order. Keep it complete; do not drop features.**
> 1. Mental model (compiles to PHP; types erased; `\Tyhp\` runtime for a few features; stricter than
>    PHP) + one small orientation example.
> 2. Files & open tags (`.tyhp`/`.tyhpdef`/`.php`, `<?tyhp `/`<?tyhpdef `, tagless mode).
> 3. The two strict rules: non-nullable by default; conditions must be `bool`; narrowing resets on
>    reassignment.
> 4. Type system: known types now enforced (`static` return-only; `resource` not user-writable); new
>    types (`decimal`→`\Tyhp\Decimal`, `struct`→`array`, literal types); unions/intersections/`?T`;
>    `mixed` collapse; `(decimal)` cast status.
> 5. Type inference / what must be annotated: params & properties & consts always typed; return types
>    and non-initialized locals always required; locals inferred from the
>    initializer; **array literals are NOT inferred — annotate them**; literal-type inference; a call
>    to a callee with no declared return → `mixed`; closure params inferred from call-site context;
>    `mixed` is one-way. Cite `TYHP4016`/`TYHP4138`.
> 6. Assignability & subtyping: nullable/union/intersection rules; literal→base; `int`→`float`;
>    `iterable` vs `array`/`\Traversable`; struct width subtyping; `mixed` top / `never` bottom;
>    variance (user generics **invariant**, `array`/`iterable` covariant, `callable` params
>    contravariant + return covariant).
> 7. Generics (erased): declaration, `extends` constraints, `= Type` defaults (note defaults not yet
>    applied — pass all type args), generic args on `extends`/`implements`, erasure rules,
>    `array<T>`/`array<K,V>`, `iterable<…>`, and the **return-last `callable<…>`** convention +
>    `\Closure<…>` + alias return-constraint propagation.
> 8. Typed locals (erased; only params/props/returns keep spelled types).
> 9. Type aliases (top-level reliable; member/class-scoped + generic-alias params only partly wired —
>    say so).
> 10. Structs (structural value type, properties-only, `extends`; struct vs class table incl. width
>     subtyping; `new`/`with`/property-access lowering to arrays; anon/`clone` gotchas).
> 11. Operator overloading (class + shorthand + `convert`; arity/`self` rules; overloadable set;
>     left-first resolution; synthetic-method lowering).
> 12. Extensions (`extends T $this` methods; `operator +<Type>`; static-class lowering; `use
>     extension`; tyhpdef inline auto-active).
> 13. async/await (Fiber/Promise; `\Tyhp\Promise::_async/_await`; outer return `\Tyhp\Promise`; no
>     async generators; Promise API surface; **cancellation** via `CancellationToken(Source)` +
>     `OperationCancelledException`/`TimeoutException`, unhandled-rejection behavior; `foreach (await
>     …)` gotcha).
> 14. Deterministic disposal `using`/`:=` (`IsDisposable`/`AsyncIsDisposable`; try/finally and
>     `DisposableScope` lowering; **failure semantics**: reverse order, dispose-throw masks the body
>     exception, multi-resource → `AggregateException` of disposal errors only, `:=` `__destruct` only
>     warns).
> 15. `with` expressions (clone/new/in-place forms; readonly rule; only struct+`new` compiles today).
> 16. Type guards + narrowing + `is` aliases (guard `: $x is T`→`: bool`; `is/isa/…` = `instanceof`,
>     ⚠️ use `instanceof`; narrowing table; what does not narrow).
> 17. Compile-time helpers (`nameof`, `default(T)`, `typeof`, `variable_exists` → `\array_key_exists`).
> 18. String-as-type features (symbol-name types list + existence-check narrowing; template string
>     types + quantifiers; all erase to `string`).
> 19. Utility types (`\Tyhp\Readonly<T>` … `Awaited<T>`).
> 20. Property accessors (= PHP 8.4 hooks; 8.4+ only).
> 21. Expression trees / parsable lambdas (`Expression<…>`/`PropertyPath<T,R>`; inline-`fn` only;
>     return-last; `->callable`; `tyhp/lambda`; experimental).
> 22. Declarations — deltas vs PHP (declaration-site generics on class/interface/trait/enum/function;
>     **constructor return type `: void` / `: parent(...)`**; `async` modifier; return-type guards;
>     top-level overload signatures; trait property alias `$prop as $x`; `internal` not in language;
>     everything else identical to PHP — say so explicitly).
> 23. `.tyhpdef` declaration files (what's declarable; `deprecated`/`obsolete`; `extends` import;
>     inline extensions; `package.tyhp.json`).
> 24. PHP interop & name resolution (compiled output is ordinary PHP in same namespaces; resolution
>     like PHP; **symbol discovery order** built-in → embedded tyhpdef → package → user tyhpdef → user
>     `.tyhp`, first-registered-wins).
> 25. Runtime packages table (`tyhp/core`/`decimal`/`async`/`lambda`, key symbols, PHP ≥ 8.1).
> 26. Build, output layout & CLI: `tyhp.json` keys; **output layout** (classes → FQN segments under
>     `output.path`; entrypoints mirror source; `_functions.php`); `composer.json` only when
>     `build.updateComposer` (path repos + `composer install`); **all-or-nothing error gate** (no
>     partial emit); incremental build state file; CLI commands/flags.
> 27. Diagnostics: `TYHP4xxx` codes, error/warning/info, checker continues after errors (cap
>     100/file), common-codes table; note there is no dedicated unknown-member / arg-count error.
> 28. Availability & gotchas: "use freely" list; "annotate don't assume" (arrays/params/returns);
>     "use the PHP form instead" table; "not in the language yet" list. **Derive strictly from the
>     current code.**
> 29. PHP-mapping reference table.
>
> **Output format (the split):**
> - One file per section: `guide/NN-slug.md` (e.g. `guide/07-generics.md`) and `handbook/NN-slug.md`.
>   Keep the `## N. Title` heading at the top of each file.
> - Rewrite every cross-reference as a link to the **sibling section file**, not a `§`-only pointer or
>   a line number (e.g. `[§16](16-type-guards.md)`; from handbook to guide use `../guide/NN-slug.md`).
>   Never hardcode line numbers.
> - `00-index.md` in each folder: the shared intro/legend + a list of the section files.
> - `QUICK_GUIDE.md`: one line per feature, cross-language analogy, `→` naming the exact file(s).
>   Frame it as "PHP plus additions" — no compile-target/erasure detail there.
> - `AGENTS.md` (+ `CLAUDE.md` pointing to it): entry point; how to load section-by-section, and the
>   handful of PHP habit-breakers.
>
> **Style (token-efficient but complete):**
> - Dense; no filler, hedging, or meta commentary. Prefer tables over prose.
> - Define shorthands up front: `→` = "compiles to"; `⚠️` = doesn't compile cleanly yet, use the PHP
>   form.
> - Use fenced code blocks tagged `tyhp`, `php`, or `json`. Keep examples minimal but real (draw from
>   fixtures/tests).
> - Do **not** reference compiler internals (story numbers, phase names, C# file names) in the guide
>   itself — it's for app authors, not compiler devs.
> - Do **not** apply automated token-pruners (LLMLingua etc.) — they corrupt the verbatim code. Keep
>   density gains lossless (tables over prose, minimal examples, no cross-section repetition).
>
> **Acceptance checks before finishing:**
> - All 29 guide + 7 handbook section files exist, are lint-clean, and each `00-index.md` lists them.
> - Cross-references resolve to real sibling files; no `§`-only or line-number references remain.
> - `QUICK_GUIDE.md` pointers name files that exist; `AGENTS.md`/`CLAUDE.md` present.
> - Every ⚠️/"use the PHP form" claim matches the current emitter/checker behavior.
> - No compiler-internal jargon leaked into the guide/handbook prose.
> - Measure total token count with `tiktoken` (`o200k_base`) and note it in the summary.
>
> **Handbook content** (same audience, on-demand load), split across `handbook/` section files, covering:
> project setup + `tyhp.json` layout keys; namespace→folder output mapping (from FQN; `psr4` is
> composer-only); `.tyhp`/`.php`/`.tyhpdef` handling; autoloading (`build.updateComposer`,
> `entryPointAutoloader`, `declare(output_file=…)`); the build/CLI dev loop with **honest** command
> status (mark stubs ⚠️: `init`, `composer`, `generate_tyhpdef`, `watch`, source maps, `lint --format
> json`); testing (no runner — test the emitted PHP) and debugging; a concrete PHP-interop walkthrough
> (`.tyhpdef` for an untyped library); fuller worked examples (extension, operator overload,
> `using`/`:=`, `with`, guards, async+cancellation); and a runtime API signature reference (Promise,
> Deferred, EventLoop, CancellationToken(Source), Contracts, DisposableScope, Decimal, Type,
> Exceptions, Expression). Verify every tooling claim against `Tyhp/CLI/**`, `Tyhp/Config/**`, the
> emitter, and `runtime/packages/**` — **prefer each package's `.tyhpdef` for public signatures**, and
> mark unimplemented behavior ⚠️ rather than describing the story's intent as fact.

---

## After regenerating

- Skim `guide/26-build-cli.md`, `guide/27-diagnostics.md`, and `guide/28-availability-gotchas.md` to
  confirm build/diagnostics/availability reflect the current toolchain.
- Update the "still maturing / not yet in the language" items if features have since landed.
- Re-copy the updated `AIDevGuide/` folder into any downstream Tyhp projects that vendor it (see
  `README.md`).
