# Implementation Plan: Story 29.1 — Pre-Stable Compatibility Reservations

> **Roadmap position:** Story 29.1 — **Tier 3 — Advanced** (additive sub-story, after Story 29 and before the
> Story 30 capstone). It does not depend on Story 29.
> **Direct dependencies (new numbering):** 08, 08.5, 10, 15, 21, 21.6.
> **New story.** From the 2026-11-04 review of Tier 4 (Stories 31–47) for changes that would break code valid at the
> end of Tier 3. Stories 32, 34, 35, and 41 moved into Tier 3 whole. This story covers the rest: small reservations
> and decisions that let the remaining Tier 4 stories ship after the first stable release without breaking it.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`). Diagnostic names below are
> **provisional**; allocate codes per `CONVENTIONS.md` when implementation starts.

> **Branch:** TBD
> **Generated:** 2026-11-04
> **Status:** Planned. Not started. Parts A–E are independent and can ship in any order, but all must land before
> the first release candidate.

---

## Table of Contents

- [Summary](#summary)
- [Part A — Symbol-Name Strings Match PHP's Runtime Resolution](#part-a--symbol-name-strings-match-phps-runtime-resolution)
- [Part B — `internal` Emit Location Is Not Contract](#part-b--internal-emit-location-is-not-contract)
- [Part C — Reject `is` with Type Arguments on `\Closure` / `\Fiber`](#part-c--reject-is-with-type-arguments-on-closure--fiber)
- [Part D — Reserve a Leading `!` in Glob Arrays](#part-d--reserve-a-leading--in-glob-arrays)
- [Part E — Lock Decisions in Later Plans](#part-e--lock-decisions-in-later-plans)
- [Diagnostics](#diagnostics)
- [Risks & Edge Cases](#risks--edge-cases)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)
- [Documentation & AIDevGuide Phase](#documentation--aidevguide-phase)
- [Out of Scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)

---

## Summary

| Part | Change | Protects |
|------|--------|----------|
| A | A string literal typed as a class, interface, enum, trait, function, or constant name must be fully qualified; `nameof()` of those symbols is typed `string` | Story 31 (emit-time canonicalization) |
| B | The interop contract states that the emitted name and location of `internal` symbols can change | Story 31 (function/constant lowering) |
| C | `is` / `instanceof` with type arguments on `\Closure` or `\Fiber` is a compile error | Story 43 (runtime `is \Closure<…>`) |
| D | A glob string starting with `!` is a config error | Story 42 (anti-glob prefix) |
| E | Record compatibility decisions in the plans for Stories 31, 36, and 47 | Stories 31, 36, 47 |

Parts A, C, and D turn code that is accepted today, but would change meaning later, into an error now, while the
change is still allowed. Parts B and E are wording and planning changes with no compiler work.

---

## Part A — Symbol-Name Strings Match PHP's Runtime Resolution

### Problem

PHP resolves a class, function, or constant name held in a **string** as fully qualified. `use` imports and the
current namespace never apply to strings: `new $c`, `\class_exists($c)`, `\call_user_func($f)`, and
`\constant($k)` all read the string as written, from the global root.

The checker does not follow that rule today:

- **Literals.** `SymbolNameExistenceVerifier.ResolveName` (`Tyhp/TyhpLang/Checker/SymbolNameExistenceVerifier.cs:164–183`)
  resolves a literal assigned to `__ClassName` / `__FunctionName` / … through the enclosing scope, so `use` aliases
  and the current namespace apply. `namespace App; use App\Models\User as U; __ClassName $c = 'U';` type-checks, and
  `new $c()` fails at runtime.
- **`nameof()`.** `nameof(User)` emits the short name `'User'`, as documented
  (`tyhp-docs-src/content/tyhp_2700_compileTimeConstructs.md:13`, `:34–37`). `NameofTypeInferrer.InferObjectDeclarationName`
  (`Tyhp/TyhpLang/Checker/NameofTypeInferrer.cs:305–324`) types it as `__ClassName<User>`, "parity with
  `TypeName::class`". `User::class` is the fully qualified name; `nameof(User)` is not, so for a namespaced class the
  brand promises a usable name the value does not hold. Functions and constants have the same gap.

Story 31 Part B plans to fix both at emit time by rewriting these strings to the fully qualified name
(`IMPLEMENTATION_PLAN_TODO_STORY_31.md:227–230`, `:253–260`). Shipped after stable, that changes the runtime value of
existing literals and of every `nameof()` of a top-level symbol, and it contradicts Story 08.5's promise that
symbol-name types emit byte-identical to plain strings (`IMPLEMENTATION_PLAN_TODO_STORY_08.5.md:516`, `:531`).

### Decisions

1. **Literals resolve the way PHP does.** A string literal assigned to `__ClassName`, `__InterfaceName`,
   `__EnumName`, `__TraitName`, `__FunctionName`, `__ConstName`, or their parameterized forms resolves from the
   global root, with or without a leading `\`. A literal that names a symbol only through a `use` import or the
   current namespace is an error whose fix-it offers the fully qualified string or `X::class`. Code in the global
   namespace is unaffected. Emit is unchanged: the literal is already the right runtime string.
2. **`nameof()` of a top-level symbol is typed `string`.** `nameof()` of a class, interface, enum, trait,
   function, or constant keeps emitting the short name and is typed plain `string`. Structs (`__StructName`) are
   not PHP symbols; confirm how their names are registered before deciding whether they follow the same rule. `nameof()` of a member
   (property, method, class constant, enum case, variable) keeps its current brand (`__PropertyName<T>`,
   `__MethodName<T>`, …), because member names are not namespaced. For a usable class name, write `X::class`.
3. **Consequence for Story 31.** With 1 and 2, typed symbol-name strings already hold the runtime name, so Story 31
   Part B has no alias expansion left to do. It keeps only the remapping of lowered and relocated symbols, which
   applies to `internal` symbols (Part B of this story) and to the opt-in release profile.

### Touches

- `SymbolNameExistenceVerifier.ResolveName`: resolve from the global root only (`ResolveQualifiedName` on the
  trimmed segments). When that fails but scope resolution succeeds, report the new diagnostic with the resolved FQN.
- `NameofTypeInferrer.InferFromSymbol` (`:271–303`): return `CheckedTypes.String` for `ObjectDeclarationSymbol`,
  `FunctionDeclarationSymbol`, and `ConstantSymbol`. Keep the member cases.
- Narrowing through `\class_exists($s)` and the other guards is unchanged; it already tests the runtime string.
- Update `Tyhp/TyhpLang/Checker/technical-guide.md` (symbol-name types and `nameof()` sections).

---

## Part B — `internal` Emit Location Is Not Contract

The interop contract says `internal` is "Stripped; members emit `public`, top-level unprefixed"
(`tyhp-docs-src/content/cli_interopContract.md:146`). That reads as a promise that an `internal` free function stays
a global PHP function. Story 31 lowers `internal` functions and constants to static members on a synthetic class
by default (`IMPLEMENTATION_PLAN_TODO_STORY_31.md:280–282`, `:401`).

**Decision:** The contract states that the emitted name and location of an `internal` symbol are not part of the
interop contract and can change in any release. PHP code must not call an `internal` symbol by name. Public symbols
keep their emitted names and locations.

No compiler change.

---

## Part C — Reject `is` with Type Arguments on `\Closure` / `\Fiber`

### Problem

The docs say generic arguments on `\Closure` and `\Fiber` are compile-time only, and that `instanceof` / `is` test
the PHP class (`tyhp-docs-src/content/tyhp_0150_newTypes.md:181–182`). The emitter guide says a parameterized
nominal in `is` reifies to `\Tyhp\Type::is($x, Type::generic(…))` (`Tyhp/TyhpLang/Emitter/technical-guide.md:340`),
so `$fn is \Closure<callable(int): string>` most likely takes that path today, and what it returns at runtime is
not documented. No test covers it. Story 43 gives the form a real meaning: a reflection-based check of the closure's
signature, and its acceptance criteria already allow a compile error instead (`IMPLEMENTATION_PLAN_TODO_STORY_43.md:30–39`). Either way, the runtime result changes.

### Decision

`$x is \Closure<…>`, `$x instanceof \Closure<…>`, and the same with `\Fiber<…>` are compile errors that point to
`as` (`$fn as \Closure<callable(int): string>`) for a compile-time shape assertion, or to `is \Closure` for the class
test. Bare `is \Closure` / `is \Fiber` and `as` are unchanged. Story 43 can then give the form a runtime meaning
without changing the behavior of any accepted program.

### Touches

- Before implementing, compile `$fn is \Closure<callable(int): string>` and record what it emits and returns today.
- The checker rule that validates `is` / `instanceof` targets: report the new diagnostic when the target resolves to
  `\Closure` or `\Fiber` with type arguments, including through a transparent alias of either.
- While there, check `is` with type arguments on other builtin generic classes whose arguments PHP does not track
  (`\Generator<…>`, `\WeakMap<…>`). If they share the same undocumented path, apply the same rule and list them in
  the diagnostic text; otherwise note them as out of scope.

---

## Part D — Reserve a Leading `!` in Glob Arrays

Story 42 makes a glob string that starts with `!` an anti-glob in every JSON glob array (`tyhp.json` `include`,
`exclude`, `tyhpdefInclude`, `tyhpdefExclude`; `extra.tyhp.package` `include`, `overlay`)
(`IMPLEMENTATION_PLAN_TODO_STORY_42.md:46–53`, `:80–88`). Today a leading `!` has no special meaning, so the same
config would select different files after Story 42.

**Decision:** A glob string whose first character is `!` reports `ConfigInvalidGlobPattern` (TYHP6004, already in
`MessageCode.cs:1387` and not yet used) in every glob array listed above. The message says a leading `!` is reserved
and that a literal path starting with `!` is written `./!name`, the spelling Story 42 keeps for literals
(`IMPLEMENTATION_PLAN_TODO_STORY_42.md:82–83`). Whitespace before `!` is not a leading `!`.

### Touches

- `Project.GetProjectSourceFiles()` (`tyhp.json` globs) and `TyhpdefOverlayApplier.ExpandGlob`
  (`extra.tyhp.package` globs) validate entries before expanding.
- Add the `ERROR_TYHP6004` / `EXPLAIN_TYHP6004` text to both `.resx` files if it is not there yet.

---

## Part E — Lock Decisions in Later Plans

Planning-only. Each named plan's header points here (added 2026-11-04). Confirm each decision, then fold it into
that plan's body, so the story ships additive after stable.

1. **Story 36 (conditional return types) — overload selection.** `.tyhp` overload sets keep the documented rule: the
   first declared signature that accepts the call as written wins (`tyhp-docs-src/content/tyhp_1200_functionOverloads.md:243`).
   Specificity scoring (`IMPLEMENTATION_PLAN_TODO_STORY_36.md:179–180`) applies only to tyhpdef overloads, which
   already score, and to the new `contains` arms. Desugaring an existing overload set must never change which
   signature a call selects.
2. **Story 47 (variance) — arrays.** Declared `in` / `out` variance coexists with the existing `arrayLikeCovariant`
   rule in `TypeComparer.Subtyping.cs`; array assignability does not change (`IMPLEMENTATION_PLAN_TODO_STORY_47.md:58–60`).
3. **Story 31 (Tyhp Link) — defaults.**
   - Drop Part B's alias→FQN expansion, which Part A of this story makes unnecessary; keep the lowering and
     relocation remap.
   - When the lowering and relocation invariant cannot be proven, keep the symbol in place (or shimmed). Never fail a
     build that type-checks (the plan currently allows either, `IMPLEMENTATION_PLAN_TODO_STORY_31.md:463–464`).
   - The Tyhp loader (`tyhp_autoload.php`), bundling, and preload stay limited to `link.profile: "release"`. The
     default `"debug"` profile emits only what Composer PSR-4 loads, plus lowering of `internal` symbols (Part B).

---

## Diagnostics

Provisional names; allocate codes and add `ERROR_TYHP####` / `EXPLAIN_TYHP####` to both
`Resources/CLI.TyhpHostedService.resx` and `Resources/CLI.TyhpHostedService.en-US.resx`.

| Name (provisional) | Part | Message sketch |
|---|---|---|
| `CheckerSymbolNameLiteralNotFullyQualified` | A | `` `'{0}'` names `{1}` only through a `use` import or the current namespace. PHP reads class, function, and constant names in strings as fully qualified; write `'{1}'` or `{2}::class`. `` |
| `CheckerIsTypeArgumentsNotCheckable` | C | `` `is {0}` cannot test type arguments at runtime. Use `as {0}` to assert the shape, or `is {1}` to test the class. `` |
| `ConfigInvalidGlobPattern` (TYHP6004, existing code) | D | `` Glob `{0}` in `{1}` starts with `!`, which is reserved. Write `./{0}` for a path whose name starts with `!`. `` |

---

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Part A breaks namespaced code that assigns short or alias literals to symbol-name types | That code fails at runtime today; the fix-it supplies the FQN. Log under `### Changed` in `CHANGELOG.md` |
| 2 | Part A breaks code that passes `nameof(SomeClass)` where a `__ClassName` is expected | Every `nameof()` of a class in the runtime packages and examples is string concatenation (checked 2026-11-04). The diagnostic on the assignment suggests `SomeClass::class` |
| 3 | `nameof(T)` on a generic parameter | Already plain `string` (`NameofTypeInferrer.cs:133–137`); unchanged |
| 4 | Part C breaks code that relies on today's `Type::is` result for `\Closure<…>` | Record today's behavior first (Part C Touches); the docs never promised more than a class test |
| 5 | Part D breaks a config with a real path starting with `!` | `./!name` keeps working; the error message says so |
| 6 | Part E decisions drift before Stories 31, 36, 47 are scheduled | Each plan states the decision in its header and Decisions section |

---

## Golden Fixtures / Tests (Acceptance)

Fixtures under `tests/conformance/story29.1/` with a `manifest.json`, plus xUnit tests beside the existing Story 08.5
symbol-name tests (`tests/conformance/story08_5/symbol-name-check/`).

- **Part A, literals:** in `namespace App`, `__ClassName $c = 'App\Models\User';` and `'\App\Models\User'` pass;
  `'User'` (same namespace) and `'U'` (`use … as U`) report the new diagnostic with the FQN in the fix-it. The
  global-namespace fixture `known_class_literal.tyhp` still passes. The same cases for `__FunctionName` and
  `__ConstName`.
- **Part A, `nameof()`:** `nameof(User)` is typed `string` and still emits `'User'`; passing it to a `__ClassName`
  parameter is an error; `nameof(User::$name)` is still `__PropertyName<User>`.
- **Part C:** `is \Closure<callable(int): string>`, `instanceof \Fiber<…>`, and an alias of `\Closure<…>` report the
  new diagnostic; `is \Closure` and `as \Closure<…>` are unchanged.
- **Part D:** `"!./src/**"` in `include`, `exclude`, `tyhpdefInclude`, `tyhpdefExclude`, and `extra.tyhp.package`
  `include` / `overlay` report TYHP6004; `"./!draft.tyhp"` and `" !x"` do not.
- **Part E:** no tests; review the three plan edits.

---

## Documentation & AIDevGuide Phase

- **`tyhp-docs-src`:**
  - Part A: `tyhp_0150_newTypes.md` (symbol-name types: literals are fully qualified), `tyhp_2400_dynamicLanguageFeatures.md`, `tyhp_2700_compileTimeConstructs.md` (`nameof()` of a top-level symbol is a display name typed `string`; use `X::class` for a usable name).
  - Part B: `cli_interopContract.md`.
  - Part C: `tyhp_0150_newTypes.md`.
  - Part D: `project_optionsList.md`.
  - `diagnostics_reference.md` for the new codes and TYHP6004.
  - Log in that repo's `CHANGELOG.md`.
- **`tyhp-ai-dev-guide`:** `guide/18-string-as-type.md` and `guide/17-compile-time-helpers.md` (Part A),
  `guide/16-type-guards.md` and `guide/07-generics.md` (Part C), `guide/26-build-cli.md` and
  `handbook/01-project-setup.md` (Part D), `handbook/05-php-interop.md` (Part B), plus the matching `REGEN.md` items.
  Log in that repo's `CHANGELOG.md`.
- **`tyhp`:** `CHANGELOG.md` (`### Changed` for Parts A, C, and D).

---

## Out of Scope

- Implementing Stories 31, 36, 42, 43, or 47. This story only reserves syntax and records decisions.
- Emit-time remapping of lowered or relocated symbols (Story 31).
- A reflection-based runtime check for `\Closure` / `\Fiber` type arguments (Story 43).
- Anti-glob semantics (Story 42).

---

## Cross-Story References

- **Story 08.5** — symbol-name types and typed `nameof()`; Part A corrects its literal resolution and `nameof()` typing.
- **Story 15** — the interop contract that Part B amends.
- **Story 21.6** — compile-time `as` for `\Closure` / `\Fiber`, which Part C's diagnostic points to.
- **Stories 31, 36, 42, 43, 47** — the Tier 4 stories each part protects.
