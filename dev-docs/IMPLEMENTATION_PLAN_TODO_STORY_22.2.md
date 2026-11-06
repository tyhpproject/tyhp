# Implementation Plan: Story 22.2 — Struct Initializers (`new Name[...]`)

> **Roadmap position:** Story 22.2 — **Tier 3 — Advanced** (additive sub-story, numbered after Story 22.1; it does
> not depend on 22 or 22.1).
> **Direct dependencies (new numbering):** 08 (checker), 11 (struct and `with` emit), 19 (language server),
> 19.5 (editor grammars), 27.1 (`type Name = struct { };`).
> **New story.** Resolves `DESIGN_OPEN_QUESTIONS.md` §23 (constructing structs with named arguments) with a
> different spelling. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`). Diagnostic names below are
> **provisional**; allocate codes per `CONVENTIONS.md` when implementation starts. See `ROADMAP.md` for the tiered
> sequence.

> **Source:** Public beta feedback on the r/PHP launch thread (2026-11): a commenter asked why struct construction
> is `new Opts() with [name => 'ann']` instead of named arguments (`new Opts(name: 'Ann')`).
> **Branch:** TBD
> **Generated:** 2026-11-04
> **Status:** Planned. Not started.

---

## Table of Contents

- [Summary](#summary)
- [Syntax](#syntax)
- [Decisions](#decisions)
- [Part A — Parser and AST](#part-a--parser-and-ast)
- [Part B — Checker](#part-b--checker)
- [Part C — Emitter](#part-c--emitter)
- [Part D — Language Server](#part-d--language-server)
- [Part E — Editor Grammars](#part-e--editor-grammars)
- [Diagnostics](#diagnostics)
- [Risks & Edge Cases](#risks--edge-cases)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)
- [Documentation & AIDevGuide Phase](#documentation--aidevguide-phase)
- [Out of Scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)

---

## Summary

A struct can be constructed with a bracket list directly after its name:

```tyhp
<?tyhp

type UserRecord = struct {
    ?int $id;
    string $name;
    bool $isAdmin = false;
};

UserRecord $user = new UserRecord[
    name => "Joe Snuffy",
    isAdmin => true,
];
```

`new UserRecord[...]` means exactly what `new UserRecord() with [...]` means today: same required-field checks,
same unknown-key errors, same value-type checks, same emitted PHP. It drops the `() with` that carries no
information for a struct, and it looks like the associative array it compiles to.

The story also adds checked spread (`new Opts[...$base, name => 'bob']`) to every struct initializer and struct
`with` list, and rejects unkeyed entries there. Today the checker skips both kinds of entry without validating them
(`WithKeywordRule.cs:62–66` and `:143–147`).

`new X()` and `new X() with [...]` keep working unchanged.

---

## Syntax

| Form | Meaning |
|------|---------|
| `new Opts[name => 'ann']` | Same as `new Opts() with [name => 'ann']` |
| `new Opts[]` | Same as `new Opts()`: every field from its default; required fields are an error |
| `new Box<int>[value => 42]` | Generic struct with explicit type arguments; `new Box[value => 42]` infers them as `new Box() with` does |
| `new struct { string $name; int $age; }[name => 'Alice', age => 30]` | Anonymous struct |
| `new Opts[...$base, name => 'bob']` | Spread a struct value, then override |

Keys follow the `with` rules: bare or quoted property names (`name =>`, `'name' =>`), with keyword and builtin
names reclassified in key position (`class =>`). Trailing commas and multi-line lists are allowed. Whitespace,
including newlines, may separate the name (or the anonymous struct's closing `}`) from `[`.

```tyhp
<?tyhp

type Point = struct {
    float $x;
    float $y;
};

Point $p = new Point[x => 10.5, y => 20.3];
```

```php
<?php

$p = ['x' => 10.5, 'y' => 20.3];
```

---

## Decisions

1. **Spelling: brackets, not named arguments.** `new Opts(name: 'ann')` suggests a constructor, invites positional
   arguments (`new Point(1.0, 2.0)`) that break silently when fields are reordered, and makes a struct look like a
   class. The bracket list reuses the key syntax of `with`, `clone … with`, and in-place `with`, and matches the
   emitted PHP. This answers `DESIGN_OPEN_QUESTIONS.md` §23; named-argument construction is not added.
2. **Structs only.** `new X[...]` where `X` is a class, interface, enum, `self` / `static` / `parent`, an object
   shape (Story 27), or a non-struct type alias is an error that suggests `new X() with [...]`. For a class, the
   bracket list would blur constructor arguments and property assignment.
3. **`new Inner[]` is valid** and equals `new Inner()`. Both spellings stay. Tyhp has no `T[]` type syntax, so
   there is no "array of `Inner`" reading inside the language. The required-field check applies to both
   (TYHP4159).
4. **The bracket must follow the type directly.** After the name, after generic arguments
   (`new Box<int>[`), or after the anonymous struct body (`new struct { … }[`). Two existing PHP forms keep their
   meaning:
   - `new $classes['key']` — dynamic class name read from an array. Only a static type name starts an
     initializer.
   - `new Inner()['a']` — construct, then index the result. The `()` decides which form was meant.
5. **Entries must be keyed or spread.** An unkeyed entry (`new Point[1.0, 2.0]`) is an error, as it is in every
   struct `with` list. There are no positional fields.
6. **Spread is checked** (rules S1–S8 below), in the bracket form and in every struct `with` list
   (`new X() with`, `clone $x with`, `$x with`).
7. **One initializer per expression.** `new Opts[name => 'a'] with [retries => 5]` is an error; put every entry in
   the brackets. An initializer is not dereferenceable without parentheses: `(new Point[x => 1.0, y => 2.0])->x`.
8. **No deprecation.** `new X()` and `new X() with [...]` raise no warning. The docs and AI dev guide show the
   bracket form first for structs.

### Spread rules

`...$src` copies every key of `$src` into the result at runtime, so the checker holds the source to the target's
shape rather than to plain assignability:

- **S1 — Source type.** `$src` must have a struct type: named, generic instance, or anonymous. `array`,
  `array<K, V>`, `mixed`, nullable structs (`?Opts`), and unions are errors.
- **S2 — No extra fields.** Every field of the source must exist on the target (through `extends`). Spreading a
  struct with a field the target lacks is an error naming the field. Plain assignment allows extra fields, but
  spreading one would put an undeclared key into the constructed array.
- **S3 — Field types.** Each source field's type must be assignable to the target field's type.
- **S4 — Same array keys.** A source field and its target field must map to the same PHP array key. If either
  side uses an alias (`int 'status_code' as $statusCode;`, `mixed 0 as $arg1;`) and the keys differ, it is an
  error. Spread copies raw keys and cannot rename them.
- **S5 — Required fields.** A required target field is satisfied by a keyed entry, or by a spread whose source
  type guarantees the field: required or defaulted in the source. A nullable source field without a default may
  be absent from the array, so it does not satisfy a required target field.
- **S6 — Order.** Defaults apply first, then entries left to right. A later entry or spread overrides an earlier
  one, as in a PHP array literal.
- **S7 — Generic inference.** Spread source types take part in inferring a generic struct's type arguments the
  same way keyed values do.
- **S8 — Scope.** S1–S7 apply to the bracket form and to every struct `with` list. Object `with` lists are
  unchanged (see [Out of Scope](#out-of-scope)).

---

## Part A — Parser and AST

Today struct construction is a `PhpNewAst` on the left of a `with` binary operator (`PhpBinaryOpAst`) whose right
side is a `PhpArrayPairListAst` (`PhpParserAstVisitor.Included.cs:976–980`). The initializer desugars to that same
tree, so the checker and emitter reuse their existing paths.

- **Named and generic structs.** Add an alternative beside `#newClassInstanceNonDereferenceable`
  (`Tyhp/TyhpLang/Grammar/shared/TyhpOverrides.reached.g4:129–133`):
  `T_NEW <static class name> tyhpGenericTypeArguments? T_OPEN_SQUARE_BRACE arrayPairList? T_CLOSE_SQUARE_BRACE`
  (label `#tyhpNewStructInitializer`). It must not match the `newVariable` branch of `classNameReference`
  (`PhpParser.reached.g4:487–488`, `#newVariableArrayIndex` at `:1493–1496`), so `new $classes['k']` keeps
  parsing as a dynamic class name.
- **Generic disambiguation.** `newIsFollowedByArgumentList()` (`TyhpParser.GrammarMethods.cs:303–338`) already
  separates `new Box<int>()` from comparison chains. Extend the lookahead to accept `[` after the closing `>`,
  including when the closing `>>` is a single token (`new Box<array<int>>[`).
- **Anonymous structs.** Extend `#tyhpNewAnonStructInstance` (`TyhpOverrides.reached.g4:117–119`, predicate
  `looksLikeAnonymousStruct()` at `TyhpParser.GrammarMethods.cs:168–200`) with an optional bracket list after the
  struct body.
- **Bare keyword keys.** `ReclassifyWithInitializerKey` (`TyhpLexer.GrammarMethods.cs:308–378`) retokenizes
  reserved words as `T_STRING` in key position after `with [`. Extend the trigger to `[` that opens an
  initializer: after `T_NEW` + name (optionally namespaced) + optional generic arguments, and after an anonymous
  struct body that follows `new`.
- **AST.** The visitor builds `PhpBinaryOpAst(T_TYHP_WITH, PhpNewAst, PhpArrayPairListAst)` with a new
  `PhpNewAst.IsStructInitializer` flag. `new X[]` builds an empty pair list. The flag drives the struct-only
  diagnostic, the TYHP4159 wording, and the language server code action. Spans: the `new` expression covers
  `new X`; the pair list covers `[...]`.
- **Orphan rule.** `tyhpWithList` (`Tyhp/TyhpLang/Grammar/TyhpParser.g4:127–129`, `{ arrayPairList }`) is
  declared but referenced by no rule. Leave it unless it blocks the new alternative; remove it in a separate
  cleanup.
- Update `Tyhp/TyhpLang/Parser/technical-guide.md`.

---

## Part B — Checker

All rules live in `WithKeywordRule.cs` unless noted.

- **Struct-only.** When `PhpNewAst.IsStructInitializer` is set and the resolved type is not a struct, report
  the struct-initializer-on-non-struct diagnostic and skip key checks, so no TYHP4031 cascade follows.
- **Unkeyed entries.** `ValidateWithProperties` (`:140–147`) and `ValidateRequiredStructProperties` (`:60–66`)
  skip pairs whose key is not a property name. For struct receivers, report the unkeyed-entry diagnostic instead.
  This covers every struct `with` list, not only the bracket form.
- **Spread.** For `PhpArrayPairAst.IsExpansion` entries on struct receivers, apply S1–S4 per entry. Extend the
  `provided` set in `ValidateRequiredStructProperties` with the guaranteed fields of each spread source (S5).
- **Keyed entries.** Unchanged: TYHP4031 for unknown keys (with did-you-mean), assignability through
  `context.CheckAssignment`, and readonly checks.
- **Bare `new X[]`.** It reaches `ValidateRequiredStructProperties` through the desugared `with`, which calls
  `MarkStructNewCheckedViaWith`, so `TypeCompatibilityRule.Dereferenceables.cs:700–704` does not report TYHP4159
  a second time.
- **One initializer per expression.** `new X[...] with [...]` has a `with` binary operator on its left side.
  Confirm the existing "with on a non-addressable expression" error fires. If it does not, add a dedicated
  diagnostic.
- **TYHP4159 wording.** The message says "must be set via `new ... with [...]`" (`CLI.TyhpHostedService.resx:867`)
  and the explanation shows only the `with` form (`:871`). Reword both to name the form the user wrote (two
  message keys, or a neutral wording that shows `new StructName[prop => value]` first). Update both `.resx` files.
- Update `Tyhp/TyhpLang/Checker/technical-guide.md`.

---

## Part C — Emitter

- **No spread.** The desugared tree goes through `StructEmissionHelper.TryRewriteStructWith`
  (`Tyhp/TyhpLang/Emitter/StructEmissionHelper.cs:131–187`) unchanged. Output is byte-identical to
  `new X() with [...]`. It folds to one array literal when every value is known; key aliases resolve through
  `GetStructArrayKey` / `ResolveStructPropertyKey`.
- **With spread.** Emit `\array_replace` with one argument per run, in source order: the defaults array, then
  each spread source, with each run of consecutive keyed entries merged into one array literal. Do not emit a
  PHP literal spread (`[...$a]`): PHP renumbers integer keys during unpacking, which breaks integer-key aliases.
  `\array_replace` preserves them. Today `NormalizeArrayPair` (`:325–327`) passes keyless pairs through to
  `BuildArrayPair` (`TyhpEmitter.Expressions.cs:1257–1259`), which emits literal spread; struct receivers must
  stop taking that path.
- **Defaults pruning.** Drop a default from the leading array when a later spread guarantees that key (S5) or a
  later keyed entry sets it. Drop the defaults argument when it becomes empty.
- **`clone $x with` and in-place `$x with`** with spread emit `\array_replace($x, $src, [...])` the same way.
- Every PHP version Tyhp targets has `\array_replace`; no version gate.
- Update `Tyhp/TyhpLang/Emitter/technical-guide.md`.

```tyhp
<?tyhp

type Opts = struct {
    string $name;
    int $retries = 3;
    ?string $nick;
};

Opts $base = new Opts[name => 'ann'];
Opts $copy = new Opts[...$base, nick => 'a'];
```

```php
<?php

$base = ['retries' => 3, 'name' => 'ann'];
$copy = \array_replace($base, ['nick' => 'a']);
```

`$base` guarantees `name` (required) and `retries` (defaulted), so the defaults argument is pruned. `nick` has no
default and is left out of `$base`, as it is today.

---

## Part D — Language Server

- **Key completion** inside `new X[` and inside struct `with [` lists. Nothing provides it today; `CompletionEngine.cs`
  only completes the struct name after `new` (`:207`, `:573–574`). Offer field names not yet set, required fields
  first and marked as required, with the field type and default as detail. Respect aliases: complete the property
  name, not the array key.
- **Hover** on a key shows the field's type and default (reuse `SymbolFormatter.cs` struct formatting).
- **Semantic tokens:** keys in an initializer or struct `with` list get the property token kind
  (`SemanticTokenCollector.cs:551` handles declarations only).
- **Code action "Use struct initializer":** rewrite `new X() with [...]` on a struct (named, generic, anonymous)
  to `new X[...]`, moving the list text verbatim so comments and formatting survive. Register it in
  `CodeActionEngine.Collect` (`Handlers/TextDocumentHandlers/CodeActionHandler.cs:19–46`); today that engine only
  offers import fixes. The code action does not touch `new X()`.
- **Quick fix** on the struct-only diagnostic: rewrite `new SomeClass[...]` to `new SomeClass() with [...]`.
- Update `Tyhp/LanguageServer/` technical guide(s) if present.

---

## Part E — Editor Grammars

In `tyhp-ide-plugin` (the VS Code grammar also ships in the PhpStorm plugin):

- `vscode/syntaxes/tyhp.tmLanguage.json`: scope the name in `new Name[` and `new Name<…>[` as a type, and bare
  keys inside the bracket list as property names, the same way keys after `with [` are (`:767–768` scopes only
  the `with` keyword today).
- Log the change in both `vscode/CHANGELOG.md` and `phpstorm/CHANGELOG.md`.

---

## Diagnostics

Provisional names; allocate codes and add `ERROR_TYHP####` / `EXPLAIN_TYHP####` to both
`Resources/CLI.TyhpHostedService.resx` and `Resources/CLI.TyhpHostedService.en-US.resx`.

| Name (provisional) | Fires when | Message sketch |
|---|---|---|
| `CheckerStructInitializerOnNonStruct` | `new X[...]` where `X` is not a struct | `` `new {0}[...]` constructs a struct, but `{0}` is a {1}. Use `new {0}() with [...]`. `` |
| `CheckerStructEntryUnkeyed` | An unkeyed entry in a struct initializer or struct `with` list | `` Struct entries need a property name: `name => value`. `` |
| `CheckerStructSpreadNotStruct` | S1 | `` Only a struct value can be spread into `{0}`; `{1}` is not a struct. `` |
| `CheckerStructSpreadExtraField` | S2 | `` Spread source `{1}` has field `{0}`, which `{2}` does not declare. `` |
| `CheckerStructSpreadKeyMismatch` | S4 | `` Field `{0}` maps to key {1} in `{2}` but key {3} in `{4}`; spread cannot rename keys. `` |

Reused: TYHP4031 (unknown key), TYHP4159 (required field; reworded in Part B), and the existing assignability
diagnostic for S3 and for keyed values.

---

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | `new $classes['k']` changes meaning | Initializer requires a static type name (Part A); fixture |
| 2 | Readers from Java or C# take `new Inner[]` for array allocation | Tyhp has no `T[]` types; the structs page introduces the form as a struct initializer before any example |
| 3 | `new struct { … }['k']` may have parsed as indexing the result | `[` right after the body now always opens an initializer; `['k']` becomes an unkeyed-entry error. Index with parentheses: `(new struct { … })['k']`. Fixture confirms the current parse first |
| 4 | New spread and unkeyed-entry errors in existing struct `with` lists | No `.tyhp` file in the sibling repos spreads into a `with` list (checked 2026-11-04). Recheck unkeyed entries before shipping, and log under `### Changed` in `CHANGELOG.md` |
| 5 | Integer-key aliases renumbered by spread | Emit `\array_replace`, never literal spread (Part C); fixture with `mixed 0 as $arg1` |
| 6 | Lexer key reclassification misses keys after generic arguments | Fixtures with `new Box<int>[class => …]` and `new Box<array<int>>[…]` |
| 7 | Two spellings in diagnostics and docs | TYHP4159 names the form the user wrote; docs show the bracket form first and state the equivalence once |
| 8 | Spread source evaluated more than once | Each source is one `\array_replace` argument, evaluated once, in source order |

---

## Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` golden fixtures under `tests/conformance/story22.2/` with a
`manifest.json`, plus xUnit tests alongside `StructEmitterTests.cs`, `Phase6RuleTests.cs`,
`StructKeywordParseTests.cs`, and the LSP tests.

- **Equivalence:** for named, generic, anonymous, aliased, and `extends` structs, `new X[...]` emits
  byte-identical PHP to `new X() with [...]`. `new X[]` emits identically to `new X()`.
- **Required fields:** `new Point[]` and `new Point[x => 1.0]` report TYHP4159 with initializer wording;
  `new Point() with [x => 1.0]` keeps the `with` wording.
- **Unknown key:** `new Point[z => 1.0]` reports TYHP4031 with did-you-mean.
- **Struct-only:** `new User[...]` (class), `new static[...]`, enum, interface, object shape, and `type Id = int`
  each report `CheckerStructInitializerOnNonStruct`, with no cascade errors.
- **PHP forms unchanged:** `new $classes['k']()` still builds a dynamic class; `new Inner()['a']` still indexes.
- **Keys:** quoted keys, keyword keys (`class =>`), trailing comma, multi-line lists, and whitespace before `[`.
- **Unkeyed entries:** `new Point[1.0, 2.0]` and `$p with [1.0]` report `CheckerStructEntryUnkeyed`.
- **Spread:** S1–S5 each have a passing and a failing fixture. S6 override order is asserted by running the
  emitted PHP. Integer-alias spread keeps keys `0` and `1`. Defaults pruning emits no redundant defaults array.
- **Spread in `with`:** `clone $p with [...$q]` and `$p with [...$q]` follow S1–S5 and emit `\array_replace`.
- **Chaining:** `new Opts[name => 'a'] with [retries => 5]` is an error;
  `(new Point[x => 1.0, y => 2.0])->x` type-checks as `float`.
- **Language server:** key completion in `new X[` and `with [` (unset fields only, required first); hover on a
  key; the code action rewrites `new X() with [...]` to `new X[...]` and preserves comments inside the list; the
  quick fix rewrites `new SomeClass[...]`.

---

## Documentation & AIDevGuide Phase

The bracket form becomes the primary struct construction form everywhere structs are shown, with
`new X() with [...]` and `new X()` stated once as equivalent.

- **`tyhp-docs-src`:** `tyhp_0400_structs.md` (Defining a Struct, Property Types and Defaults, Instantiation,
  Generic Structs, Anonymous Structs, a new Spread section, Common Mistakes); `tyhp_2200_withKeyword.md` (with on
  Structs: the initializer form and spread rules); `tyhp_0700_typeAliases.md`; `quickref.md`;
  `faq_tyhpSyntax.md`; struct examples in `package_core.md`, `package_io.md`, `package_parallel.md`;
  `diagnostics_reference.md` (new codes, reworded TYHP4159). Log in that repo's `CHANGELOG.md`.
- **`tyhp-ai-dev-guide`:** `guide/10-structs.md`, the structs line in `QUICK_GUIDE.md`, and a `REGEN.md` item so a
  regeneration keeps the change. Log in that repo's `CHANGELOG.md`.
- **`tyhp`:** `CHANGELOG.md` (`### Added` for the initializer, spread, language server features; `### Changed`
  for the new unkeyed-entry and spread errors in struct `with` lists and the TYHP4159 wording).
- **`DESIGN_OPEN_QUESTIONS.md` §23:** mark resolved by this story.

---

## Out of Scope

- Named-argument (`new Opts(name: 'ann')`) and positional struct construction.
- The bracket form for classes or object shapes.
- Computed keys (`$k => $v`) in struct initializers or struct `with` lists.
- Spread and unkeyed entries in **object** `with` lists. Tracked as `FOUND_BUGS.md` #14 (Ready): unkeyed entries
  are an error, and a spread is allowed when checked under rules S1–S3 and S6. Implement it alongside this story
  so both share the unkeyed-entry diagnostic and the spread checks.
- Array `with` (`$arr with [...]`) and its open spread question (`DESIGN_OPEN_QUESTIONS.md`, array `with`
  section).
- Rewriting existing runtime package `tyhp_src` and example code to the bracket form. The code action makes this
  a mechanical follow-up.
- Deprecating `new X()` or `new X() with [...]`.

---

## Cross-Story References

- **Story 11** — struct `with` emit (`StructEmissionHelper`), reused unchanged for the no-spread case.
- **Story 27** — object shapes; `new Shape[...]` is rejected.
- **Story 27.1** — `type Name = struct { };`, the declaration form this story constructs.
- **Story 19 / 19.5** — language server features and editor grammars.
- **Story 22.1** Part A — struct-shaped `@param array{…}` docblocks are unaffected; the emitted arrays are the
  same.
- **`DESIGN_OPEN_QUESTIONS.md` §23** — the named-argument proposal this story answers.
