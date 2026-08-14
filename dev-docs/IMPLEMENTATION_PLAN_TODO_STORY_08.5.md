# Implementation Plan: Story 08.5 — Symbol-Name Types (checker feature, split from Story 31)

> **Roadmap position:** Story 08.5 — **Tier 0 — Spine** (additive sub-story, inserted after Story 08)
> **Direct dependencies (new numbering):** 06, 08
> **New story:** carved out of Story 31 ("Tyhp Link") during the prioritization review — this is the linker-independent
> "Part A" (the type-system/checker surface). The linker-specific "Part B" (emit-time canonicalization + the
> lowering/relocation invariant) stays in Story 31.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating
> ranges. See `ROADMAP.md` for the full tiered sequence.

> **Source:** Prioritization review of Story 31 (Tyhp Link)
> **Branch:** TBD
> **Generated:** 2026-06-19
> **Prerequisites:** Story 06 (built-in type surface + utility-type registration), Story 08 (checker, narrowing, `nameof()`).
> Both are already implemented; this story is **additive** and does not block the spine.
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — symbol-name types, template strings, nameof typing, and `tests/conformance/story08_5/` fixtures landed. Residual gaps (emit erasure of `__ClassName` hints; broader fixture coverage) in `INCOMPLETE.md`.

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Architecture Overview](#architecture-overview)
- [Phase 1: Symbol-Name Type Definitions (binder) + erasure to `string`](#phase-1-symbol-name-type-definitions-binder--erasure-to-string)
- [Phase 2: Type-Guard Narrowing (checker)](#phase-2-type-guard-narrowing-checker)
- [Phase 3: Literal-Assignment Existence Verification (checker)](#phase-3-literal-assignment-existence-verification-checker)
- [Phase 4: `nameof()` Result Typing](#phase-4-nameof-result-typing)
- [Phase 5: Type / Struct Utilities](#phase-5-type--struct-utilities)
- [Phase 6: Template String Types (general feature)](#phase-6-template-string-types-general-feature)
- [Phase 7: Type-Name String Algebra](#phase-7-type-name-string-algebra)
- [Configuration](#configuration)
- [Decisions (defaults chosen)](#decisions-defaults-chosen)
- [Risks & Edge Cases](#risks--edge-cases)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

This story makes the **symbol-name types** (`__ClassName`, `__FunctionName`, `__MethodName`, …) first-class built-in
types that the checker understands. They are string subtypes that point back to a real, in-scope symbol (a class,
function, method, property, etc.), so the compiler can *see* string-based symbol references that PHP normally hides
inside opaque string literals.

It delivers five things, all of which are pure front-end (binder + checker) concerns:

1. **Type definitions** — the `__`-prefixed symbol-name types registered in the binder's built-in surface, all
   **erasing to plain `string`** in emitted PHP.
2. **Type-guard narrowing** — `\class_exists($n)` narrows `$n` to `__ClassName`, `\function_exists($n)` to
   `__FunctionName`, etc.
3. **Compile-time existence verification** — assigning a string *literal* to a symbol-name type verifies the symbol
   exists (`__ClassName $c = 'App\Models\User';` errors if the class is unknown).
4. **`nameof()` result typing** — `nameof(SomeClass)` yields `__ClassName` (etc.) instead of bare `string`.
5. **Template string types** (Phase 6) — a general, first-class type feature: types that denote *sets of strings*
   built from literal text, interpolation holes (`${ T }`), and regex-style quantifiers (`+ * ? {n} {n,} {,m} {n,m}`).
   The type-name string algebra (`__TypeName`, `__UnionTypeName`, …, Phase 7) is its first consumer, but it is usable
   anywhere a type is accepted. Like everything here, template string types **erase to `string`**.

> **What is intentionally NOT here:** the emit-time **canonicalization** (alias→FQN rewriting, remapping
> lowered/relocated symbols) and the **lowering/relocation invariant**. Those have no meaning until function/constant
> lowering and bundle relocation exist, and they belong to **Story 31 (Tyhp Link)**, governed by the interop contract
> (Story 15). This story produces the *typed, validated* symbol strings that Story 31 later consumes.

---

## Motivation

During the Story 31 prioritization review we found the symbol-name types were really **two features wearing one
hat**:

- **Part A (this story):** a developer-facing type-system feature — typed `nameof`, existence-verified symbol
  strings, narrowing via `\class_exists()`/`\function_exists()`/etc. Its only real prerequisites are the built-in
  type surface (Story 06) and the checker (Story 08), both of which are **already done**.
- **Part B (stays in Story 31):** emit-time canonicalization and the lowering/relocation invariant, which genuinely
  need the linker context (emitter expansion, interop contract, optimizer, `internal` visibility).

Reasons to land Part A now rather than waiting for Tier 4:

1. **The infrastructure already exists.** `Tyhp/TyhpLang/Binder/BuiltIn/UtilityTypes.cs` and
   `BuiltInUtilityTypeSymbol` already register checker-resolved built-in types (`Readonly`, `Pick`, `Omit`, …) via the
   `UtilityBehavior` enum and a `UtilityTypeResolver`. Adding the symbol-name types extends an established pattern
   instead of inventing new machinery.
2. **`nameof()` already ships.** `Tyhp/TyhpLang/Ast/TyhpNameofAst.cs` and `Tyhp/TyhpLang/Checker/Rules/CompileTimeRule.cs`
   already implement `nameof()`. Today it yields an untyped `string`; if we defer symbol-name typing to Tier 4 we
   touch `nameof`'s inference **twice**. Doing it alongside is cheaper and avoids drift.
3. **Standalone value.** Even if Tyhp Link never ships, users get typed dynamic class/function references and
   existence-checked symbol literals.

---

## Scope (In / Out)

| In scope (this story) | Out of scope (Story 31) |
|-----------------------|--------------------------|
| `__`-prefixed symbol-name type definitions in the binder | Emit-time alias→FQN canonicalization |
| Erasure of every symbol-name type to plain `string` in emit | Remapping lowered functions/constants to static-member homes |
| Type-guard narrowing (`\class_exists`, `\function_exists`, …) | Remapping relocated classes to emitted file/name |
| Compile-time existence verification on literal assignment | The lowering/relocation invariant (owned by interop contract, Story 15) |
| `nameof()` result typing | `[GlobalFunction]` / `[Preload]` attributes, bundling, loader |
| Symbol-name subset of the type-name string algebra (Phase 7) | Anything that depends on a symbol being *moved* or *renamed* |
| Template string types (Phase 6) — grammar, checker type kind, quantifiers, membership/inclusion | Character-class regex, `infer`-capture, inline grouping (all deferred — see Phase 6) |

---

## Architecture Overview

```
   Story 06 built-in type surface          Story 08 checker
   (UtilityTypes.cs / BuiltInUtilityType-   (narrowing, nameof, type inference)
    Symbol / UtilityTypeResolver)                    │
              │                                       │
              ▼                                       ▼
   ┌──────────────────────────────┐     ┌────────────────────────────────────┐
   │ PHASE 1: type definitions    │     │ PHASE 2: type-guard narrowing       │
   │ register __ClassName/__Fn/…  │────▶│ \class_exists → __ClassName …       │
   │ all erase to `string`        │     │ PHASE 3: literal existence verify   │
   └──────────────────────────────┘     │ PHASE 4: nameof() → __ClassName …   │
                                          └────────────────────────────────────┘
                                                          │
                                                          ▼
                              (Story 31 later: PHASE B — canonicalize/relocate
                               typed symbol-strings at emit time)
```

### Key file locations (anticipated)

| Component | Path | Notes |
|-----------|------|-------|
| Symbol-name type registration | `Tyhp/TyhpLang/Binder/BuiltIn/UtilityTypes.cs` (or a sibling `SymbolNameTypes.cs`) | Extends the existing built-in surface; `__`-prefixed names registered in global scope |
| Utility-type symbol | `Tyhp/TyhpLang/Binder/Symbols/BuiltInUtilityTypeSymbol.cs` | Reuse, or add a sibling symbol kind if a distinct `SymbolType` is warranted |
| Behavior enum | `Tyhp/TyhpLang/Enum/UtilityBehavior.cs` | Add symbol-name behaviors (or a parallel `SymbolNameKind`) |
| Resolution / erasure | `Tyhp/TyhpLang/Checker/UtilityTypeResolver.cs` | Resolve symbol-name types to their underlying `string` for emit |
| Type-guard narrowing | `Tyhp/TyhpLang/Checker/Rules/TypeNarrowingRule.cs` | Extend `BuiltInTypeGuards` + guard-call narrowing to produce symbol-name types |
| Literal existence verification | `Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.cs` (assignment path) | Verify symbol existence when a string literal is assigned to a symbol-name type |
| `nameof()` typing | `Tyhp/TyhpLang/Checker/Rules/CompileTimeRule.cs`, `Tyhp/TyhpLang/Ast/TyhpNameofAst.cs` | Infer the appropriate symbol-name type for `nameof()` |

> These are anchor points based on the current tree; confirm exact symbols at implementation time (per
> `CONVENTIONS.md`, `MessageCode.cs` and `Project.cs` are authoritative over any path/key restated here).

---

## Phase 1: Symbol-Name Type Definitions (binder) + erasure to `string`

Register the symbol-name types as built-in types, following the existing utility-type pattern.

**Foundation — `__TyhpInternal<T>`.** The foundational internal wrapper: it resolves to `T` but **cannot be directly
assigned by the developer** — a value of this type is only produced by a function/method return value or a type guard.
Nearly all `__`-prefixed types are defined in terms of `__TyhpInternal<>`. This is what prevents code from fabricating
a symbol-name-typed value out of an arbitrary string without going through narrowing or a verified literal. Like the
symbol-name types, it **erases to its underlying type** in emit.

**Symbol-name subset (the part that points back to a real symbol):**

| Type | Erases to | Meaning |
|------|-----------|---------|
| `__VarName` | `string` | Variable name valid in scope; alias of `__TypedVarName<mixed>` |
| `__TypedVarName<T>` | `__VarName` | Variable name + the referenced variable's declared type |
| `__FunctionName` | `string` | Function name in scope |
| `__StructName` | `string` | Struct type name in scope |
| `__ClassName<TObject extends object = object>` | `string` | Class-name string for type `TObject` (≈ PHPStan `class-string<T>`). Bare `__ClassName` ≡ `__ClassName<object>`. |
| `__EnumName<TObject extends object = object>` | `__ClassName<TObject>` | Enum-name string; same optional type-arg pattern as `__ClassName`. |
| `__TraitName<TObject extends object = object>` | `string` | Trait-name string; same optional type-arg pattern. |
| `__UsedTraitName<T>` | `__TraitName` | A trait specifically used by class/enum `T` |
| `__InterfaceName<TObject extends object = object>` | `string` | Interface-name string; same optional type-arg pattern. |
| `__CompatibleTypeName<T>` | `__InterfaceName\|__ClassName\|__EnumName` | A type name same-as-or-descendant-of `T` |
| `__PropertyName<T>` | `string` | Property name on `T` |
| `__MethodName<T>` | `__FunctionName` | Method name on owner type `T` (single type arg = owner). Exact-method precision uses `__MethodReturnType<TType, TMethodName>` (Phase 5), not a second `__MethodName` parameter. |
| `__ConstName` | `string` | Constant name in scope |
| `__ObjectConstName<T>` | `__ConstName` | Constant name scoped to class/enum `T` |
| `__EnumCaseName<T>` | `__ObjectConstName` | Enum case name on enum `T` |

> **Parametric `__ClassName` / siblings.** The binder accepts 0-or-1 type arguments and both forms
> **erase to `string`**. Bare `__ClassName` ≡ `__ClassName<object>`; `nameof(User)` / `User::class`
> brand as `__ClassName<User>`; literal existence is checked against `T`; `__ClassName<A>` vs
> `__ClassName<B>` is invariant with erasure widening to `<object>` / `string`. Subclass-as-
> `class-string` assignability is **not** a property of `__ClassName` — use
> `__CompatibleTypeName<T>` (covariant in `T`; accepts `__ClassName<S>` when `S <: T`).

Definitions are taken verbatim from `New and changed functionality.md` (lines ~200–214) and
`IMPLEMENTATION_PLAN_TODO_STORY_31.md` Phase 2. All symbol-name types **erase to plain `string`** in emitted PHP —
there is no runtime representation; they exist only in the checker.

**Implementation notes:**
- Reuse `BuiltInUtilityTypeSymbol` if the existing generic-parameter-requirements machinery fits; otherwise add a
  thin sibling symbol with its own `SymbolType`. Prefer reuse.
- The `__`-prefixed names are registered in **global scope** (not the `\Tyhp` namespace where `Readonly`/`Pick`
  live), matching the spelling used in `New and changed functionality.md` and the runtime tyhpdef docs.

---

## Phase 2: Type-Guard Narrowing (checker)

Extend `TypeNarrowingRule` so the standard PHP existence/identity guards narrow a `string` to the corresponding
symbol-name type on the true branch:

```
\function_exists($n)            → __FunctionName
\class_exists($n)               → __ClassName
\interface_exists($n)           → __InterfaceName
\trait_exists($n)               → __TraitName
\enum_exists($n)                → __EnumName
\property_exists($o, $n)        → __PropertyName<typeof($o)>
\method_exists($o, $n)          → __MethodName<typeof($o)>
\is_a($o, $c) / is_subclass_of  → __CompatibleTypeName<typeof($o)>
isset($$v) / variable_exists    → __VarName / __TypedVarName<T>
```

These guards are already documented in `docs/content/tyhp_0200_typeNarrowingAndGuards.json` and
`tyhp_0151_newFunctions.json`. The existing `BuiltInTypeGuards` map and `TryApplyTypeGuardCallNarrowing` in
`TypeNarrowingRule.cs` are the integration point; the parametric guards (`__PropertyName<T>`, `__MethodName<T>`,
`__CompatibleTypeName<T>`) require capturing the receiver's type into the narrowed symbol-name type's generic
argument.

---

## Phase 3: Literal-Assignment Existence Verification (checker)

When a **string literal** is assigned (or passed) where a symbol-name type is expected, the checker verifies the
named symbol exists in scope:

```tyhp
__ClassName $c = 'App\Models\User';   // OK if App\Models\User is known; error otherwise
__FunctionName $f = 'strlen';         // OK; error if no such function in scope
```

- Resolution uses the binder's `SymbolTree` / scope lookup (the same machinery name resolution already uses).
- This applies only to *literals* (and `nameof()` results). A non-literal `string` value is **not** verified here —
  it simply is not a symbol-name type until narrowed by a Phase 2 guard.
- Add the existence-verification diagnostic(s) to `Tyhp/Domain/Exceptions/MessageCode.cs` in the checker band, with
  matching `.resx` entries (`ERROR_TYHP####`), per `CONVENTIONS.md` §1 and the `cli-localization` rule. Codes are
  **not** allocated in this doc.

---

## Phase 4: `nameof()` Result Typing

`nameof()` already exists (`TyhpNameofAst`, `CompileTimeRule`). Update its result type so it returns the precise
symbol-name type rather than bare `string`:

| `nameof(...)` argument | Result type |
|------------------------|-------------|
| a class / enum / interface / trait | `__ClassName` / `__EnumName` / `__InterfaceName` / `__TraitName` (target: `__ClassName<ThatType>` etc.; today still bare until parametric nameof lands — see `FOUND_BUGS.md`) |
| a function | `__FunctionName` |
| a method | `__MethodName<T>` |
| a property | `__PropertyName<T>` |
| a constant / enum case | `__ConstName` / `__ObjectConstName<T>` / `__EnumCaseName<T>` |
| a variable | `__VarName` / `__TypedVarName<T>` |

Because every symbol-name type erases to `string`, this is a checker-only refinement: emitted PHP is unchanged
(`nameof()` still lowers to the literal string it always did).

---

## Phase 5: Type / Struct Utilities

These TypeScript-style type transforms have **no template-string dependency** — they mirror utility types the binder
already supports (`Pick`/`Omit` use `BuiltInGenericParameterConstraint.StringLiteralUnion`; `ReturnType`, `Exclude`,
`NonNullable`, `Nullable`, `Readonly` already exist as `UtilityBehavior` values). They can be implemented on the
existing `UtilityTypeResolver` path and **land with Phases 1–4**.

- **Struct/type utilities:** `__StructKey<T>`, `__StructRecord<…>`, `__StructDef<…>`, `__StructPartial<…>`,
  `__Properties<T>`, `__FunctionReturnType<…>`, `__MethodReturnType<TType, TMethodName>` (exact-method /
  return-type utility — keep `__MethodName<T>` as **owner-only**; do not add a second type parameter to
  `__MethodName`), `__TypeDiff<…>`.
- **Type-level `__As*` (value types, not type-name strings):** `__AsNotNullable<T>`, `__AsNullable<T>`,
  `__AsReadOnly<T>` — expressible directly via the existing `NonNullable` / `Nullable` / `Readonly` utility behaviors.

> The type-name *string* algebra (`__TypeName`, `__UnionTypeName`, the `__As*TypeName`/`__AsType` converters) is
> **Phase 7**, sequenced **after** Phase 6 because it consumes the template string type feature Phase 6 delivers.

---

## Phase 6: Template String Types (general feature)

> A **general, first-class language feature**, not just plumbing for the type-name algebra. Devs can use template
> string types anywhere a type is accepted (parameters, returns, properties, generic arguments, aliases). Phase 7
> (the type-name string algebra) is merely its first consumer.

### Prerequisite status (as of 2026-06-19)

Phase 6 has prerequisites that do not yet exist, but **less than first assumed** — the interpolation *parse* already
exists (verified against the current tree):

- **Parsing (mostly already there):** a double-quoted interpolated string already parses into a structured AST
  (`string → T_DOUBLE_QUOTE encapsList T_DOUBLE_QUOTE`; holes via `encapsVar`/`T_STRING_VARNAME`). The remaining gap is
  that **type position** only accepts single-quoted/constant strings today (`Tyhp/TyhpLang/Grammar/TyhpParser.g4` → `tyhpScalarType`
  → `T_CONSTANT_ENCAPSED_STRING`), so an interpolated double-quoted string isn't yet *accepted as a type* and isn't
  reinterpreted as a template. With the quantifier-after-hole syntax (see Parsing strategy) **no new lexer mode or
  string grammar is required** — only (a) accepting an interpolated string in type position and (b) the checker-side
  pattern reader.
- **Checker type model (the real work):** `Tyhp/TyhpLang/Checker/CheckedType.cs` has only `CheckedTypeKind.Literal`
  (a single concrete value). There is **no template/pattern type kind**, and `TypeComparer` (`Subtyping`/
  `Assignability`) has no pattern-matching or language-inclusion logic — `LiteralCheckedType` only subtypes to its
  `UnderlyingType` and participates in literal unions.

Story 08's remaining work (its Phase 7 — "Pipeline Integration, Configuration, and Validation") does **not** address
these. This Phase 6 therefore lands: accepting interpolated strings in type position + the pattern reader + the new
checker type kind + the automaton model described below. Once it lands, **Phase 7 (type-name string algebra) is
unblocked** within this story.

A **template string type** denotes a *set of strings* — the language described by literal text, interpolation holes,
and quantifiers. It composes other string-valued types (string literals, unions of literals, symbol-name types, and
other template string types). All template string types **erase to plain `string`** in emit (like every other type in
this story).

### Syntax

A template string type is a **double-quoted** string in type position containing literal characters and
interpolation **holes**:

```tyhp
"prefix-${T}-suffix"
```

- **Literal text** — every character *outside* a `${…}` hole is literal, including `|`, `&`, `?`, `(`, `)`.
- **Interpolation hole** `${T}` — `T` is any string-valued type expression (a string literal, `string` as the open
  wildcard, a symbol-name type, a named union of string literals/templates, or another template string type).
- **Quantifier** `${T}Q` — an optional quantifier `Q` written **immediately after** the hole's `}` applies to the
  preceding hole (see table).
- **Escaping** — a leading backslash forces the next character to be a **literal**, so any operator character can
  always be written literally (full rules in [Escaping](#escaping-complete-rules) below).

### Parsing strategy — rides on PHP's interpolated-string AST (no new grammar)

This syntax is **chosen to reuse the existing double-quoted interpolated-string parse**, so a template string type
produces a valid AST with no new lexer mode or grammar production:

- `"…${Name}…"` already parses as `string → T_DOUBLE_QUOTE encapsList T_DOUBLE_QUOTE`, where literal chunks are
  `T_ENCAPSED_AND_WHITESPACE` and each `${Name}` hole is an `encapsVar` (`#encapsVarDollarBraceExpr`, with `Name`
  captured as `T_STRING_VARNAME` when the hole is a bare identifier; a more complex hole is captured as `Expr`).
- **Quantifiers go *after* the `}`** precisely because the lexer only treats a bare identifier in `${…}` as a name
  when it is immediately followed by `[` or `}` (`PhpLexer.g4` → `ST_LOOKING_FOR_VARNAME`). An in-brace `${Name+}`
  drops into expression mode and fails to parse; a trailing `}+`/`}*`/`}{1,3}` lexes as ordinary literal text and the
  string stays a valid AST.
- The **checker** (not the parser) interprets the resulting `encapsList`: literal chunks are pattern literals, holes
  are type interpolations (the `T_STRING_VARNAME`/`Expr` is resolved as a type), and a quantifier token *immediately
  following a hole* is that hole's quantifier. Everywhere else the quantifier characters remain literal.
- A tyhp-mode grammar addon is only needed if we later want quantifiers attached structurally in the parse (the
  grammar already uses `…GrammarAddon` override hooks); the default plan keeps the parse pure-PHP and does the work in
  the checker.

> **Note:** the legacy notation in `New and changed functionality.md` writes the quantifier *inside* the braces
> (`${__BaseUnionTypeName+}`). That form does not parse; the canonical form is quantifier-after-hole
> (`${__BaseUnionTypeName}+`). Update the example/spec when this lands.

### Quantifiers (suffix immediately after the hole's `}`)

| Operator | Meaning | Range |
|----------|---------|-------|
| *(none)* | exactly once | 1 |
| `?` | optional | 0–1 |
| `+` | one or more | 1–∞ |
| `*` | zero or more | 0–∞ |
| `{n}` | exactly `n` | n |
| `{n,}` | at least `n` | n–∞ |
| `{,m}` | at most `m` | 0–m |
| `{n,m}` | between `n` and `m` (inclusive) | n–m |

`{n}`/`{n,}`/`{,m}`/`{n,m}` take non-negative integer literals; `n ≤ m` is required for ranges (else a diagnostic).

### Escaping (complete rules)

A user must always be able to put a **literal** character in the pattern even when that character would otherwise be
an operator. A leading backslash (`\`) escapes the next character to a literal. The pattern's **metacharacters** (the
only characters that can ever be operators) are:

| Metacharacter | When it is special | Literal form |
|---------------|--------------------|--------------|
| `$` (as `${`) | starts an interpolation hole | `\$` (e.g. `\${` for a literal `${`) |
| `\` | escape character | `\\` |
| `+` `*` `?` | quantifier, **only immediately after a hole `}`** | `\+` `\*` `\?` |
| `{` | starts a `{n,m}` quantifier, **only immediately after a hole `}`** | `\{` |
| `}` `,` | close/separator **inside** a `{n,m}` quantifier | `\}` `\,` (only needed inside a quantifier context) |

Rules:

- **Outside the "immediately-after-a-hole" position, `+ * ? { } ,` are already literal** and need no escaping — this is
  why the type-name algebra's literal `|`, `&`, `?` (and any non-adjacent `+`) work as-is.
- `\$` and `\\` are honored **by the lexer** as well (PHP double-quote escapes), so `\${` does not start a hole — the
  parse stays valid. The quantifier escapes (`\+`, `\*`, `\?`, `\{`) are honored **by the checker's pattern reader**
  when it scans the literal chunk that follows a hole.
- **Standard PHP double-quote escape sequences** (`\n`, `\t`, `\r`, `\"`, `\e`, `\f`, `\v`, `\xHH`, `\u{…}`, `\0`) are
  decoded to their literal characters, so a pattern can contain real newlines/tabs/etc.
- A backslash before **any other** character is a **diagnostic** (unknown escape) — this catches typos rather than
  silently keeping the backslash.

> Implementation: the pattern reader decodes escapes from the raw `T_ENCAPSED_AND_WHITESPACE` chunk text using the
> rules above. Because escapes are resolved before pattern compilation, an escaped operator char becomes a plain
> literal symbol in the automaton — never a quantifier or hole.

### Composition (no new operators needed)

- **Alternation** is expressed by a **union type inside the hole**: `"${ 'GET' | 'POST' | 'PUT' }"`. No regex `|`
  alternation operator is introduced (and bare `|` stays literal, as the algebra needs).
- **Grouping** is expressed by **factoring a sub-sequence into a named template type** and interpolating it with a
  quantifier — exactly how the type-name algebra works:

  ```tyhp
  type Segment = "|${__BaseTypeName}";            // one "|<type>" chunk
  type Union   = "${__BaseTypeName}${Segment}+";  // a type followed by 1+ chunks
  ```

  Inline grouping syntax (`(…)+`) is **deferred** as later sugar; named sub-templates cover every case today.
- **Wildcard** — `${string}` matches any string (useful for partial/loose patterns).

### Semantics (the checker)

Template string types are **regular languages**; the operators above are exactly the regular operations
(concatenation = adjacency, alternation = union, quantifiers = Kleene/bounded repetition). The checker models a
template string type as a finite automaton (NFA) built from its pattern:

- **Membership** — a string *literal* type `'foo'` is assignable to template `P` iff `'foo' ∈ L(P)`. Cheap (run the
  literal through the NFA).
- **Subtyping / assignability between templates** — `A <: B` iff `L(A) ⊆ L(B)` (regular-language inclusion). Decidable
  but worst-case expensive, so it is **size-guarded** (below).
- **`string` ↔ template** — every template is assignable *to* `string` (erasure target); `string` is **not**
  assignable to a non-trivial template (a `string` value is not known to match the pattern) unless narrowed.
- **Narrowing** — a runtime guard that validates a string against a pattern (e.g. a user type-guard returning a
  template type) narrows `string` to the template type on the true branch, consistent with Phase 2.

### Implementation outline

| Concern | Where | Note |
|---------|-------|------|
| Grammar production | *(none required)* | Rides on the existing double-quoted interpolated-string parse (`string → T_DOUBLE_QUOTE encapsList T_DOUBLE_QUOTE`); optional tyhp `…GrammarAddon` only if structural quantifier attachment is wanted later |
| Pattern reader | `Tyhp/TyhpLang/Checker/` (new) | Walks the `encapsList` AST: literal chunks → pattern literals; `encapsVar` holes → type interpolations; a quantifier token immediately after a hole → that hole's quantifier |
| Checked type kind | `Tyhp/TyhpLang/Checker/CheckedType.cs` | New `CheckedTypeKind.TemplateString` + `TemplateStringCheckedType` (pattern → compiled NFA) |
| Resolution | `Tyhp/TyhpLang/Checker/TypeInferrer.TypeExpressions.cs` | New case: when a `string`-literal type expression is an interpolated double-quoted string in type position, build the template type via the pattern reader; recursively resolve hole types |
| Subtyping / assignability | `Tyhp/TyhpLang/Checker/TypeComparer.*.cs` | Membership (literal∈pattern) + automaton inclusion (pattern⊆pattern), behind the size guard |
| Erasure | `Tyhp/TyhpLang/Checker/UtilityTypeResolver.cs` / emit path | Template string types erase to `string` |

### Deliberately deferred (flagged for your call)

- **Character classes / `.` / `\d` / `[a-z]`** — would require a *character-level* regex engine; the type-name algebra
  never needs it. **Deferred** unless you want general string-shape validation.
- **Capture / `infer`-style decomposition** (`"${infer K}=${infer V}"`) — the most powerful "other use" (parse a
  string type into its parts), but only meaningful once Tyhp has **conditional types** (`T extends U ? X : Y`).
  **Deferred**; the syntax above does not preclude adding `infer` holes later.
- **Inline grouping `( … )`** — sugar for named sub-templates; **deferred**.
- **Non-greedy quantifiers / anchors** — irrelevant to set-membership semantics; **not planned**.

---

## Phase 7: Type-Name String Algebra

> **Depends on Phase 6** (the template string type feature), which precedes it in this story — so once Phase 6 lands,
> Phase 7 is **unblocked**. It is the first consumer of template string types.

These are the literal/template-string types and the converters that operate on them — restored verbatim from
`New and changed functionality.md` (lines ~215–241) and expressed directly with the Phase 6 syntax.

- **Type-name string types:** `__BaseTypeName`, `__NullableBaseTypeName`, `__BaseUnionTypeName`, `__UnionTypeName`,
  `__BaseIntersectTypeName`, `__IntersectTypeName`, `__NotNullableUnionTypeName`, `__NotNullableIntersectTypeName`,
  `__NotNullableTypeName`, `__TypeName`, `__NonMatchingStringType`. Example, in canonical (quantifier-after-hole)
  form: `__UnionTypeName = "${__BaseTypeName}${__BaseUnionTypeName}+"`.
- **Type-name converters (consume/produce the above):** `__AsNotNullableTypeName<…>`, `__AsNullableTypeName<…>`,
  `__AsTypeName<…>`, `__AsType<…>`.

Each erases to plain `string`. The converters resolve via the Phase 6 pattern machinery (membership / inclusion) plus
the existing `UtilityTypeResolver` dispatch. Because Phase 6 ships earlier in this same story, there is no external
gate — Phase 7 simply follows it.

---

## Configuration

Phases 1–5 and Phase 7 introduce no new `tyhp.json` keys (symbol-name types are always-on built-in types).

Phase 6 introduces one **provisional** guard key (register in `CONVENTIONS.md` §4 and `Tyhp/Config/Project.cs` when
scheduled; names there are authoritative):

| Key (provisional) | Meaning |
|-------------------|---------|
| `checker.templateStringMaxStates` | Upper bound on automaton size for template-string subtyping/inclusion checks. When a comparison would exceed it, the checker is conservative (treats inclusion as unprovable) and emits a diagnostic rather than risking a pathological (PSPACE) check. |

---

## Decisions (defaults chosen)

1. **Split from Story 31.** Part A (this story) is linker-independent and lands now; Part B (canonicalization +
   lowering/relocation invariant) stays in Story 31.
2. **Reuse the existing utility-type infrastructure** (`UtilityTypes.cs` / `BuiltInUtilityTypeSymbol` /
   `UtilityBehavior` / `UtilityTypeResolver`) rather than building a parallel registry, unless a distinct symbol kind
   proves cleaner during implementation.
3. **Erasure to `string` is mandatory and total** — no symbol-name type has a runtime representation.
4. **Existence verification applies to literals and `nameof()` only** — non-literal strings become symbol-name types
   only through Phase 2 narrowing.
5. **Phase ordering:** Phase 5 (struct/type utilities + type-level `__As*`) is ungated and lands with Phases 1–4;
   Phase 6 (template string types) lands the general feature; Phase 7 (type-name string algebra + converters) follows
   Phase 6 and consumes it. None of these block the symbol-name subset (Phases 1–4).
6. **Template strings are a general feature (Phase 6), not algebra-only plumbing.** Usable anywhere a type is accepted.
7. **Quantifiers reuse regex semantics:** `+ * ? {n} {n,} {,m} {n,m}`, written **immediately after** the `${…}` hole.
   This rides on PHP's existing interpolated-string parse (no new grammar/lexer mode); the checker reinterprets the
   `encapsList`. Canonical form is `${T}+`, **not** the legacy `${T+}` (which does not parse).
8. **Alternation = union-in-hole; grouping = named sub-template.** No new alternation/group operators in core.
9. **Deferred from Phase 6:** character classes / `.` / `\d`, `infer`-capture (gated on conditional types), and inline
   grouping `( … )`. Easy to revisit — call it out if you want any pulled into core.
10. **Template-string subtyping is automaton inclusion, size-guarded** (`checker.templateStringMaxStates`) to avoid
    pathological checks.
11. **Any operator char is escapable to a literal** via a leading `\` (`\$ \\ \+ \* \? \{ \} \,`); standard PHP
    double-quote escapes are decoded; unknown escapes are a diagnostic. So a user can always write a literal char even
    where it would otherwise be an operator.

---

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Template-literal string types may not yet be supported by the checker | Only the template-string track (Phases 6–7) needs the new checker machinery; Phases 1–5 (symbol-name types, narrowing, verification, `nameof`, struct/type utilities) ship independently |
| 2 | Parametric guards (`__PropertyName<T>`, `__MethodName<T>`) need the receiver's type captured into the generic arg | Reuse `typeof($o)` inference already used by instanceof narrowing |
| 3 | `__As*` family overlaps the existing `\Tyhp` utility types | Express `__AsNotNullable`/`__AsNullable`/`__AsReadOnly` via existing `NonNullable`/`Nullable`/`Readonly` behaviors |
| 4 | Symbol-name types could be mistaken as carrying runtime identity | Document and test erasure to plain `string`; emitted PHP must be byte-identical to the untyped-string version |
| 5 | Existence verification false-negatives for symbols defined later / dynamically | Verify only literals against the resolved `SymbolTree`; never verify arbitrary runtime strings |
| 6 | Drift from Story 31's Phase 2 spec | Story 31's Phase 2 now back-references this story; keep this doc the source of truth for the definitions |
| 7 | Template-string subtyping (`L(A) ⊆ L(B)`) is worst-case PSPACE | Model as NFAs; bound automaton size via `checker.templateStringMaxStates`; be conservative + diagnose past the bound |
| 8 | Operator/literal ambiguity (`|`, `&`, `?`, `+`, `*` appear literally in the type-name algebra) | Quantifiers are recognized **only immediately after a `${…}` hole**; everywhere else those chars are literal, so the algebra's `|`/`&`/`?` are unaffected; a literal quantifier-char right after a hole is escaped (`\+`) |
| 9 | Catastrophic patterns (huge `{n,m}` bounds, deeply nested repetition) blow up the automaton | Treat large bounded repetitions by counting, not unrolling; reject/clamp absurd bounds with a diagnostic |
| 10 | Reinterpreting the `encapsList` could mis-handle holes meant as real PHP interpolation | Template-type reinterpretation applies only in **type position** (`string`-literal type expressions); value-position string interpolation is untouched; cover both with fixtures |

---

## Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-diagnostics) golden fixtures under
`tests/conformance/story08_5/<feature>/` with a `manifest.json` the runner asserts.

- **Definitions / erasure:** every symbol-name type erases to plain `string` in emitted PHP (byte-identical to the
  untyped-string baseline).
- **Narrowing:** each guard (`\class_exists`, `\function_exists`, `\interface_exists`, `\trait_exists`,
  `\enum_exists`, `\property_exists`, `\method_exists`, `\is_a`/`is_subclass_of`, `isset($$v)`/`variable_exists`)
  narrows to the correct symbol-name type on the true branch; the false branch does not.
- **Existence verification:** literal assignment to `__ClassName`/`__FunctionName`/… succeeds for known symbols and
  errors for unknown ones (positive + negative fixtures, asserting the diagnostic code).
- **`nameof()` typing:** `nameof()` of a class/function/method/property/const yields the corresponding symbol-name
  type (checker-level assertion); emitted PHP for `nameof()` is unchanged.
- **Type / struct utilities (Phase 5):** `__StructKey<T>`, `__Properties<T>`, `__FunctionReturnType<…>`,
  `__TypeDiff<…>`, and type-level `__AsNotNullable`/`__AsNullable`/`__AsReadOnly` resolve and erase correctly.
- **Template string types (Phase 6):**
  - *Parsing* — `"…${Name}…"` produces the existing interpolated-string AST (`encapsList`) with **no new grammar**;
    a trailing quantifier (`${Name}+`, `${Name}{1,3}`, …) lexes as literal text and the checker's pattern reader
    attaches it to the preceding hole; literal `|`/`&`/`?` (and a non-adjacent `+`) stay literal; `\${` escapes;
    `n > m` ranges error.
  - *Escaping* — `\${` yields a literal `${` (no hole); `${X}\+` yields a hole followed by a **literal** `+` (not a
    quantifier); `\\` yields a literal backslash; standard escapes (`\n`, `\t`, `\xHH`) decode; an unknown escape
    (`\q`) is a diagnostic.
  - *Membership* — `'int|float' ∈ __UnionTypeName` holds; a malformed literal does not; positive + negative fixtures.
  - *Subtyping/inclusion* — `__BaseTypeName <: __TypeName`; disjoint patterns are not subtypes; oversized comparison
    hits the `checker.templateStringMaxStates` guard and diagnoses conservatively.
  - *Erasure* — every template string type erases to plain `string` in emitted PHP.
  - *General use* — a template string type used on a parameter/return/property (not just the algebra) checks and
    erases correctly.
- **Type-name string algebra (Phase 7):** `__TypeName`/`__UnionTypeName`/`__IntersectTypeName` and the
  `__As*TypeName`/`__AsType` converters resolve and erase correctly (built on the Phase 6 machinery).
- **Self-host:** keep the runtime self-host conformance diff green (symbol-name types must not alter emitted runtime
  PHP).
