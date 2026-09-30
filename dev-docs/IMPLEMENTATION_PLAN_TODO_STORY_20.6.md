# Implementation Plan: Story 20.6 — Thin Extension Mappings & Call-Site Splicing

> **Roadmap position:** Story 20.6 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **20.7**, before Story 21)
> **Direct dependencies (new numbering):** 03, 08, 09, 11, 20, **20.7**
> **New story:** tyhpdef class-body `extension fn` / `extension operator` are **thin mappings**; Tyhp `extension { }` members are spliced at every Tyhp call site, and **the body form decides whether PHP keeps a backer method**.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-21
> **Last design lock:** 2026-08-24 — form decides the PHP backer (`=>` omits, `{ }` emits); extension members always splice regardless of `optimize`; shared splice engine (a written parameter must be declared `&` and only those are, a by-reference argument must be referenceable, repeated evaluation hoists into a by-value or ref-bound local, cycles are errors, and an erased member whose call site cannot be spliced faithfully is an error rather than a fallback); **Story 20.7** supplies tyhpdef `{ get; set; }` / `{ &get; }` so `4180` and by-value read-local see hooked properties on generated tyhpdefs
> **Prerequisites:** Story 03 (class-body tyhpdef `extension function` / `extension operator`, extension operator `<Type>`); Story 08 (checker); Story 09 / 11 (extension emit, `AliasConverter` rewrite pass); Story 20 (standalone tyhpdef `extension Name { }`, Track C backer classes); **Story 20.7** (tyhpdef hooked-property syntax + binder flags).
> **Consumers:** Story 21 (package tyhpdefs); Story 23 (`#[\Tyhp\Optimize\Inline]` on **non-extension** functions / methods / operators reuses this story's splice engine).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Phase 1: Grammar — thin tyhpdef class-body members only](#phase-1-grammar--thin-tyhpdef-class-body-members-only)
- [Phase 2: Short-syntax flag on AST nodes](#phase-2-short-syntax-flag-on-ast-nodes)
- [Phase 3: Call-site splice engine](#phase-3-call-site-splice-engine)
- [Phase 4: Emit — tyhpdef thin mappings](#phase-4-emit--tyhpdef-thin-mappings)
- [Phase 5: Emit — Tyhp `extension { }` backer methods by form](#phase-5-emit--tyhp-extension---backer-methods-by-form)
- [Phase 6: Track C — generated tyhpdef per member form](#phase-6-track-c--generated-tyhpdef-per-member-form)
- [Phase 7: Owned-type operators & existing fixtures](#phase-7-owned-type-operators--existing-fixtures)
- [Phase 8: User documentation](#phase-8-user-documentation)
- [Diagnostic Codes](#diagnostic-codes)
- [Generated Names](#generated-names)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Two related changes.

**1. Tyhpdef class-body extension members are thin mappings.** Story 03 allowed brace bodies (`extension function … { … }`, `extension operator … { … }`) in a `.tyhpdef` class body. A brace body is executable logic that would need a synthetic PHP backer (`__TyhpInlineExt_*`) to have a runtime home, and **tyhpdef must not cause build artifacts**. After this story, class-body members match standalone tyhpdef `extension Name { }` (Story 20): short `=>` only, return type required, fully erased, call sites spliced.

**2. In Tyhp `extension { }`, the member's form decides whether PHP keeps a method.** Extension call sites are *always* rewritten (extensions have no PHP receiver method to call), so the only real question is whether a PHP backer method also exists for PHP developers:

| Tyhp `extension { }` member | PHP backer method | Tyhpdef backer stub | Tyhpdef `extension` mapping | Tyhp call sites |
|---|---|---|---|---|
| `fn` / `operator` … `=> expr;` | **omitted** | omitted | copy `expr` | splice `expr` |
| `function` / `operator` … `{ return expr; }` | **emitted** | emitted | copy `expr` | splice `expr` |
| `function` / `operator` … `{ …statements… }` | **emitted** | emitted | `=> Backer::method(…)` | call the backer |

A single-`return` brace body is the escape hatch for "splice it in Tyhp **and** leave a helper PHP developers can call." Short `=>` means "Tyhp-only." Multi-statement means "real method."

Both changes need one shared **splice engine**: substitute receiver/arguments into the expression, parenthesize, reduce nested splices to a fixpoint, hoist repeated arguments into a temp, and reject bodies that mutate a parameter or that form a cycle. Story 23 reuses that engine for `#[\Tyhp\Optimize\Inline]` on **non-extension** members.

---

## Motivation

Tyhpdef describes PHP that already exists (or that compiling Tyhp will emit). A brace body in a `.tyhpdef` file is an algorithm with nowhere to live:

- Emitting a synthetic backer from tyhpdef violates "tyhpdef does not produce PHP."
- Copying a statement block into every call site is a statement inliner (`return` / locals / control flow), not a mapping.
- A `=>` expression **can** replace the call site in every situation, the same way a PHP arrow-function body can be substituted (with wrapping parentheses).

On the Tyhp side, Story 20 said `#[\Tyhp\Optimize\Inline]` marks an extension member for call-site splicing but the PHP backer method **must** still be emitted so the generated tyhpdef has a callee. That produced a PHP method whose only purpose was to be a mapping target, and consumers spliced `Backer::toUpper($x)` instead of `\strtoupper($x)` — the intent never left the library. Using the **form** instead removes the attribute from this decision entirely and gives authors a direct way to say "keep a PHP helper too" (single-`return` brace).

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|--------------|
| Tyhpdef class-body `extension fn` / `extension operator` as thin `=>` mappings | Statement inlining / IIFE or closure wrapping of multi-statement bodies |
| Dropping `extension function { … }` and brace `extension operator { … }` from the tyhpdef grammar | `#[\Tyhp\Optimize\Inline]` semantics for non-extension members (Story 23) |
| `IsShortSyntax` flag on `PhpFunctionDeclAst`, `PhpMethodDeclAst`, `TyhpOperatorOverloadAst` | An `inline` modifier keyword (rejected — form is the contract) |
| Splice engine: substitution, parentheses, fixpoint reduction, temp hoisting, safety errors | Constant folding / dead-code elimination and other optimizer modules (Story 23) |
| Tyhp `extension { }`: omit the PHP backer method for `=>`, emit it for brace bodies | Class-owned methods and operators (`class Money { operator + … }` → `__add`) — always emitted |
| Track C generation per member form (copy `expr` vs map to `Backer::…`) | New standalone tyhpdef `extension Name { }` rules (already thin in Story 20) |
| Docs, tests, fixtures, runtime tyhpdefs still using brace class-body members | Overlay / `partial` / Story 21 package catalog content except syntax fixes |

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Term | Call tyhpdef members **thin mappings**: they map call syntax onto an expression and are fully erased. |
| Expression rules | Same as PHP arrow functions (`fn(…) => expr`). Any expression PHP allows there; no statements. |
| Tyhpdef function keyword | Class-body uses `extension fn … => expr;` only. Drop `extension function … { … }`. |
| Tyhpdef operators | `extension operator +(…): T => expr;` only. Bodyless `extension operator +(…): T;` stays `TYHP8013`. Brace bodies illegal. |
| Tyhpdef artifacts | Never emit `__TyhpInlineExt_*` (or any other PHP) from a tyhpdef member. |
| Form is the contract | Tyhp `extension { }`: `=>` omits the PHP backer method; `{ return expr; }` emits it **and** still splices; multi-statement `{ }` emits it and is called. |
| `#[\Tyhp\Optimize\Inline]` on an extension member | **Error** (`4176`). Extension splicing is decided by form, not by attribute. |
| `optimize` levels | Extension splicing is **always on** — it is emit, not optimization. `optimize: none` does not turn it off. (It only affects Story 23's attributed non-extension members.) |
| Written parameters | A parameter the expression writes — assign, compound-assign, `++` / `--` in either position, or pass to a callee's `&` parameter — must be declared `&`, and a `&` on a parameter the expression never writes is equally an **error** (`4174`). Mutating `$this` is `4174` with no escape. |
| By-reference arguments | A `&` parameter's argument must be referenceable: not a literal, const, call result, `readonly` property, or a member whose read path is by value. Error `4180`, at every optimization level. **Read** `HasAccessor` + `GetHookReturnsRef` from tyhpdef-bound symbols (Story 20.7). `&get` (including a generated tyhpdef `{ &get; }`), `&__get`, and `&offsetGet` are referenceable. A tyhpdef `{ get; }` / `{ get; set; }` without `&get` is **not**. `&__get` / `&offsetGet` stay as they are. |
| Repeated use | A parameter or `$this` used more than once is allowed: emit hoists the first evaluation into a generated local — by value for a read, ref-bound for a `&` parameter. A by-value hooked property read (`HasAccessor && !GetHookReturnsRef`) uses that same by-value read-local, including when the property came from a generated tyhpdef. |
| Unspliceable call sites | If the rewrite would drop an argument, make it lazy, reorder side effects, or needs a ref-bound hoist with no statement slot, an **erased** member reports `4181`. There is no PHP method to fall back to; that fallback exists only for Story 23's attributed members. |
| Cycles | Direct or transitive self-splicing is an **error** (`4175`) — reduction would not terminate. |
| Nested reduction | Reduce spliced expressions repeatedly until no spliceable callee remains (fixpoint). |
| Call-site splice | Substitute receiver/args, then wrap the result in parentheses. |
| Defaults | Omitted arguments splice the parameter's default expression. |
| Native operators | Unchanged: bodyless `operator +(…): T;` (no `extension`) still means native PHP passthrough. |
| Empty backer | If **every** member of a Tyhp extension is `=>`, emit no PHP backer class and no `class Name as Name__tyhpExtensionBacker` stub in the tyhpdef. |
| Mixed members | The backer class (PHP and tyhpdef stub) contains only members that have a PHP body. Short `=>` members exist only as thin mappings. |
| PHP visibility | A short `=>` member is Tyhp-only — PHP cannot call it. Brace members stay callable from PHP on the backer class. |
| Activation | Unchanged. Class-body tyhpdef members are auto-active on the enclosing type; standalone `extension Name { }` and Tyhp `extension { }` require `use extension` / `global use extension`. |
| Copied expression | Must be valid at a **consumer** call site: builtins, public members of the target type, or backer methods via the backer alias. |

### Choosing a form

| Goal | Write |
|------|-------|
| Auto-active on a type, no new PHP | Thin class-body map in that type's tyhpdef |
| Opt-in, Tyhp-only, no new PHP | Tyhp `extension { fn … => expr; }` + `use extension` |
| Opt-in, spliced in Tyhp **and** callable from PHP | Tyhp `extension { function … { return expr; } }` + `use extension` |
| Opt-in, multi-statement logic | Tyhp `extension { function … { … } }` + `use extension` — a real PHP method Tyhp calls |

The single-`return` brace body is also the escape hatch when a member has to be usable at a call site the engine cannot splice faithfully. A short `=>` member is erased, so such a call site is error `4181`; the brace form emits a PHP method the call site can fall back to, which turns the same situation into a warning.

---

## Phase 1: Grammar — thin tyhpdef class-body members only

### Current (Story 03)

`tyhpdefExtensionFunction` has a full alt (`extension function` + `methodBody`) and a short alt (`extension fn` + `=> expr;`). `tyhpdefExtensionOperator` allows `methodBody` **or** `=> expr;`, plus a bodyless signature alt the checker rejects as `TYHP8013`.

### Target

```
tyhpdefExtensionFunction
    : T_TYHP_EXTENSION fn … ReturnType=returnType T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON
    ;

tyhpdefExtensionOperator
    : T_TYHP_EXTENSION T_TYHP_OPERATOR … ConvertReturnType=returnType T_SYM_SEMICOLON
        // bodyless — still parses, checker reports TYHP8013
    | T_TYHP_EXTENSION T_TYHP_OPERATOR … ConvertReturnType=returnType
        T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON
    ;
```

No `methodBody` on either rule. Requires an ANTLR regeneration (`./compile_grammar.sh`).

A brace after a tyhpdef `extension fn` / `extension operator` is a **parse error**; the grammar is the source of truth.

### Acceptance

- [ ] `extension fn name(…): T => expr;` parses in a tyhpdef class body
- [ ] `extension operator +(…): T => expr;` parses
- [ ] `extension function name(…): T { … }` does **not** parse
- [ ] `extension operator +(…): T { … }` does **not** parse
- [ ] Bodyless `extension operator +(…): T;` still parses and is `TYHP8013`
- [ ] Standalone tyhpdef `extension Name { fn … => …; }` unchanged
- [ ] Tyhp `.tyhp` `extension { function … { … } }` still parses

---

## Phase 2: Short-syntax flag on AST nodes

The visitor desugars short syntax at parse time: `TyhpParserAstVisitor.TyhpFunctions.cs` builds `fn name(…) => expr;` into a `PhpFunctionDeclAst` whose body is `{ return expr; }` (same for methods and for operator shorthand). By the time the checker or emitter runs, a short function and a single-`return` brace function are **indistinguishable** — but Phase 5 needs to tell them apart to decide whether a PHP backer method is emitted.

Add a short-syntax flag (same `SetFlag` / `HasFlag` pattern as `ReturnsRef`) and set it in the visitor wherever `=>` was consumed:

| File | Node | Set by |
|---|---|---|
| `Tyhp/TyhpLang/Ast/PhpFunctionDeclAst.cs` | `PhpFunctionDeclAst` | short function / short extension function visits |
| `Tyhp/TyhpLang/Ast/PhpMethodDeclAst.cs` | `PhpMethodDeclAst` | short method visits (incl. tyhpdef class-body lowering) |
| `Tyhp/TyhpLang/Ast/TyhpOperatorOverloadAst.cs` | `TyhpOperatorOverloadAst` | operator `=> expr` shorthand visits |

Notes:

- Expose it as `IsShortSyntax` (a real flag, not a grammar addon) so it survives the AST cache round-trip. `TyhpOperatorOverloadAst.ExtensionTargetType` / `IsInlineExtension` are plain properties today and are **not** cache-safe (see `Tyhp/TyhpLang/Ast/technical-guide.md` open questions) — do not repeat that pattern for this flag.
- Add the parameter to the `Create(...)` factories rather than mutating after construction.
- Update `Tyhp/TyhpLang/Ast/technical-guide.md` (flag table + the short-function desugaring note).

### Acceptance

- [ ] `fn f(): int => 1;` produces `IsShortSyntax == true`; `function f(): int { return 1; }` produces `false`
- [ ] Same for methods and for `operator … => expr`
- [ ] Flag survives an AST cache write/read round-trip
- [ ] `Ast/technical-guide.md` documents the flag

---

## Phase 3: Call-site splice engine

One shared component performs every call-site substitution in this story (and, once Story 23 lands, for attributed non-extension members). It runs on the emit side, in the same pre-emit AST rewrite pass that already converts extension calls to static calls (`AliasConverter`, Story 09 §5.4–5.5) — **not** in the optimizer, because extension splicing must happen at every optimization level.

**Substitution**

1. Take the member's single expression (from `{ return expr; }` — short syntax has already been desugared to that shape).
2. Replace `$this` with the receiver expression and each parameter with its call-site argument; use a parameter's default expression when the argument is omitted.
3. Wrap the result in parentheses.
4. Re-run on the result until no spliceable callee remains (fixpoint), so an `=>` member that calls another `=>` member reduces all the way down.
5. Set `OriginalAst` on replacement nodes so sourcemaps (Story 17) map back to the original call site.

**By-reference contract**

Splicing puts the caller's own expression where the parameter was, which behaves like by-reference passing. Three rules keep that identical to a real call. They are specified in full under *Argument passing and by-reference semantics* in [Story 23 Phase 5](IMPLEMENTATION_PLAN_TODO_STORY_23.md#phase-5-tyhpoptimizeinline-for-non-extension-members), and this engine is where they are implemented. In brief:

1. **A written parameter is by reference, and only those are.** A parameter the expression assigns to, compound-assigns, increments or decrements in either position, or passes to a callee's `&` parameter must be declared `&`. Declaring `&` on a parameter the expression never writes is equally an error. Both are `4174`. Mutating `$this` stays `4174` with no escape, since a receiver is not a parameter.
2. **A by-reference argument must be referenceable.** Error `4180` (allocated by Story 23). It lands in the shipped general call checker, not the optimizer, because it covers every call with a by-reference parameter at every optimization level. **Read** `HasAccessor` and `GetHookReturnsRef` from tyhpdef-bound property symbols (Story **20.7** prerequisite). `{ get; }` / `{ get; set; }` without `&get` is not referenceable; `{ &get; }` is. Those shapes in a **generated** `package.tyhpdef` are visible to a consumer. `&__get` / `&offsetGet` stay as they are (`tyhpdefImportClassMethod.ReturnsRef`).
3. **Hoist repeated evaluation; decline when hoisting cannot help.** Detailed below.

For a Tyhp `extension { }` member that also emits a PHP backer method, the backer's signature carries `&` exactly where the Tyhp declaration does, and Track C mirrors it onto both the backer declaration and the mapping.

**Hoisting repeated evaluation**

If a parameter or `$this` appears more than once in the expression, evaluating the argument twice would be wrong:

```tyhp
extension MathHelpers {
    fn doubled(extends int $this): int => $this + $this;
}

int $n = \readAndAdvance()->doubled();   // a naive splice would advance twice
```

Emit hoists the first evaluation into a generated local and reuses it:

```php
$n = (($__tyhpInlineTemp1 = \readAndAdvance()) + $__tyhpInlineTemp1);
```

The kind of local depends on the parameter or property read path:

| Repeated | Local |
|---|---|
| By-value parameter, or `$this` | By value — `$t = $arg` |
| `&` parameter | Ref-bound — `$t = &$arg;` |
| By-value hooked property (`HasAccessor && !GetHookReturnsRef`) | By value — same flags as `4180` |
| `{ &get; }` (`GetHookReturnsRef`) | Ref-bound when the read must stay a reference |

The by-value read-local (and `4180`) **read** `HasAccessor` + `GetHookReturnsRef` from tyhpdef-bound symbols, including properties that arrived only through a generated tyhpdef. `&get` / `{ get; }` in that generated tyhpdef are visible to a consumer; `&__get` / `&offsetGet` stay as they are.

A by-value local severs a `&` parameter's reference, so the write never reaches the caller. A ref-bound local is exact, and it is also what keeps a side-effecting lvalue path correct: `crazy($a[$i++])` spliced naively evaluates the index twice.

A reference bind is a statement, not an expression, so a ref-bound hoist requires a **statement slot** at the call site. Skip the local entirely when the argument is a simple variable, literal, or constant. Generated names must not collide with anything in scope (see [Generated Names](#generated-names)).

**Declining a splice**

Some call sites cannot be spliced faithfully no matter what is hoisted:

| Situation | Why hoisting cannot help |
|---|---|
| The expression never reads a parameter | The argument would not be evaluated at all |
| A parameter is reached only past a short circuit (`$a ?? $b`, `$a && $b`) | The argument would become lazy |
| Parameters are read in a different order than declared | Argument side effects would run out of order |
| A ref-bound hoist is needed but there is no statement slot | Nowhere to put the bind |

A member that emits a PHP method can keep the real call and warn — that is Story 23's path. **An erased member has no method to call**: a short `=>` Tyhp `extension { }` member emits no backer, and a tyhpdef thin mapping has no PHP behind it at all. For those the same condition is error `4181`, and the author's remedies are to bind the offending argument to a variable first or to switch the member to a single-`return` brace body, which emits a backer and therefore restores the fallback.

This is the one place where an erased member is stricter than an attributed one, and the *Choosing a form* guide must say so.

**Other safety rules (checker)**

- Mutation *through* a call is a write for rule 1's purposes: `\ksort($a)` means `$a` must be declared `&`. Every callee's signature is known from tyhpdef, so this is decidable.
- Direct or transitive cycles are an error (`4175`).

### Acceptance

- [ ] Receiver / arguments / defaults substitute correctly and the result is parenthesized
- [ ] Nested spliceable callees reduce to a fixpoint
- [ ] A repeated by-value parameter or receiver hoists into a by-value local; a repeated `&` parameter hoists into a ref-bound local; simple arguments skip the local
- [ ] Generated local names never collide with call-site variables
- [ ] A written parameter not declared `&`, a `&` on an unwritten parameter, and any mutation of `$this` each report `4174`; pre-increment counts as a write
- [ ] Mutation through a callee's `&` parameter (`\ksort`) counts as a write and requires `&`
- [ ] A non-referenceable argument to a `&` parameter reports `4180`; a `&get` / tyhpdef `{ &get; }` / `&__get` / `&offsetGet` read path is accepted; a tyhpdef `{ get; }` (by-value hook) is rejected — including when those shapes come from a generated `package.tyhpdef` (`HasAccessor` / `GetHookReturnsRef`)
- [ ] An erased member whose call site cannot be spliced faithfully reports `4181`, and the same shape on a member with a PHP backer warns and keeps the call instead
- [ ] Cycles report `4175`
- [ ] `OriginalAst` is set for sourcemap provenance

---

## Phase 4: Emit — tyhpdef thin mappings

**Binder / checker**

- `$this` / `self` on a tyhpdef class-body thin member still mean the enclosing tyhpdef type (no `extends` on the first parameter, no `<Type>` on the operator).
- Public-only access to the enclosing type's API (existing `TYHP8010` / access rules).
- `#[\Tyhp\Optimize\Inline]` on a tyhpdef thin member is an error (`4176`).
- Update the `TYHP8013` message: a mapped operator needs a thin `=>` expression; bodyless means native `operator`.
- Keep synthetic scopes if that is how auto-activation is modeled today — they must never become PHP.

**Emit**

- Splice every call site through the Phase 3 engine.
- Emit no synthetic extension class and no static forwarding method.

This is the same behavior Story 20 already specified for standalone tyhpdef `extension Name { }` members.

### Acceptance

- [ ] `$name->toUpper()` with `extension fn toUpper(): string => \strtoupper($this);` emits `(\strtoupper($name))`
- [ ] `$a + $b` with `extension operator +(self $l, self $r): self => $l->plus($r);` emits `($a->plus($b))`
- [ ] No `__TyhpInlineExt_*` (or other backer) appears in PHP because of a tyhpdef member
- [ ] Defaulted arguments splice the default expression
- [ ] `#[Inline]` on a tyhpdef thin member reports `4176`

---

## Phase 5: Emit — Tyhp `extension { }` backer methods by form

Extension call sites are always rewritten. The form decides what PHP keeps.

```tyhp
<?tyhp

extension StringHelpers {
    // multi-statement: real PHP method, called (not spliced)
    function complexStringProcess(extends string $this): string {
        string $finalString = \trim($this);
        // … more statements …
        return $finalString;
    }

    // short: Tyhp-only, no PHP method
    fn shortProcess(extends string $this): string => ' ' . $this;

    // single-return brace: spliced in Tyhp AND kept for PHP developers
    function simpleProcess(extends string $this): string {
        return $this . ' ';
    }
}
```

Emitted PHP:

```php
<?php

class StringHelpers
{
    public static function complexStringProcess(string $this_): string
    {
        $finalString = \trim($this_);
        // … more statements …
        return $finalString;
    }

    public static function simpleProcess(string $this_): string
    {
        return $this_ . ' ';
    }
}
```

`shortProcess` has no PHP method. `simpleProcess` does, even though Tyhp never calls it.

**Rules**

- `IsShortSyntax` (Phase 2) selects row 1 vs row 2 of the [Summary](#summary) table.
- A member whose body is a single `return expr;` is spliced at Tyhp call sites **and** emitted.
- A multi-statement member is emitted and called through the backer.
- If every member is short `=>`, emit no backer class at all.
- `$this_` (`GeneratedNames.ExtensionReceiverThisAlias`) is the emitted PHP parameter name; it never appears in Tyhp or tyhpdef source.

### Acceptance

- [ ] Short `=>` members produce no PHP backer method
- [ ] Single-`return` brace members produce a PHP backer method **and** splice Tyhp call sites
- [ ] Multi-statement members produce a PHP backer method and Tyhp calls it
- [ ] All-`=>` extension emits no backer class
- [ ] Splicing happens at `optimize: none`

---

## Phase 6: Track C — generated tyhpdef per member form

Story 20 Phase 6.6 mapped every member to `=> Name__tyhpExtensionBacker::method(…)` and required the backer method to exist even when the Tyhp member was `#[Inline]`. Replace that with the form table. For the Phase 5 example:

```tyhp
<?tyhpdef

class StringHelpers as StringHelpers__tyhpExtensionBacker {
    public static function complexStringProcess(string $this_): string;
    public static function simpleProcess(string $this_): string;
}

extension StringHelpers {
    fn complexStringProcess(extends string $this): string
        => StringHelpers__tyhpExtensionBacker::complexStringProcess($this);
    fn shortProcess(extends string $this): string => ' ' . $this;
    fn simpleProcess(extends string $this): string => $this . ' ';
}
```

- `complexStringProcess` — multi-statement, so the mapping targets the backer.
- `shortProcess` — no backer stub; the expression is copied.
- `simpleProcess` — backer stub is declared (PHP developers may call it), but the mapping copies the expression so consumers splice instead of calling it.

**Rules**

- The copied expression is the Tyhp expression as authored, after the library's name resolution, with `$this` / parameters expressed as the tyhpdef mapping's receiver and parameters. Do not rewrite it into a backer call.
- Write `$this` in the mapping. `$this_` is emit-side only; a copied expression must not leak it.
- A copied expression must be resolvable by a consumer: builtins, public members of the target type, or backer methods via `Name__tyhpExtensionBacker::…`. Do not copy an expression that depends on library-private helpers.
- Backer stub class lists only members that have a PHP method (rows 2 and 3). All-`=>` extensions get no stub class.
- Track C never emits `use extension` / `global use extension` for the library's own extensions (Story 20 decision 11 stands).

### Acceptance

- [ ] Short `=>` members are copied into the mapping and absent from the backer stub
- [ ] Single-`return` brace members are copied into the mapping **and** present in the backer stub
- [ ] Multi-statement members map to `Backer::method(…)`
- [ ] All-`=>` extension emits `extension Name { … }` with no backer stub class
- [ ] Copied expressions never contain `$this_`
- [ ] A consumer compiling against the generated tyhpdef splices the copied expression

---

## Phase 7: Owned-type operators & existing fixtures

**Owned-type operators are unchanged.** `class Money { operator +(self $a, int $b): self { … } }` compiles to a real PHP method (`Money::__add`) that PHP callers need. Track C maps it with a thin tyhpdef member and **no** `#[Inline]`:

```tyhp
<?tyhpdef

class \MyLib\Money {
    public static function __add(self|int $l, self|int $r): static|int;

    extension operator +(self $a, int $b): self => self::__add($a, $b);
}
```

Never omit `__add` (or any other class-owned method) from PHP. Row 1 of the form table applies to **extension** members only.

Migrate existing brace-bodied tyhpdef class-body members:

- Parser / checker fixtures using `extension function … { … }` in `.tyhpdef`
- `tests/Tyhp.Tests/Domain/Services/TyhpdefOutputWriterTests.cs` expected strings
- `tyhp-lang/vscode/samples/highlight-audit.tyhpdef`
- Runtime / package tyhpdefs still using the Story 03 full form
- User-doc examples (see Phase 8)

If a fixture's brace body is not a single expression, move the logic into Tyhp or PHP and keep a thin map.

### Acceptance

- [ ] Owned-type operators still emit their PHP method; tyhpdef maps `=> self::__add(…)` with no `#[Inline]`
- [ ] Output-writer / generator tests expect the new syntax
- [ ] No remaining `.tyhpdef` fixture uses `extension function {` or a brace `extension operator`

---

## Phase 8: User documentation

Tyhpdef pages:

- Call class-body members **thin mappings**; show the splice (`$name->toUpper()` → `(\strtoupper($name))`).
- State that PHP cannot call an erased member.

Tyhp extension pages:

- Document the three forms and what each keeps in PHP (short `=>` = Tyhp-only; single-`return` brace = spliced *and* callable from PHP; multi-statement = a real method Tyhp calls).
- Document that a parameter a member writes must be declared `&`, that only those parameters are by reference, and that `++` counts as a write in either position — a non-mutating increment is written `$v + 1`.
- Document that an argument for a `&` parameter must be something PHP can reference, and which shapes are not: literals, constants, call results, `readonly` properties, and properties read by value through a hook (`{ get; }` in tyhpdef), `__get`, or `offsetGet`. A tyhpdef `{ &get; }` is referenceable.
- Note that a repeated parameter is evaluated once (the compiler introduces a local).

Do not mention story numbers, tracks, or rejected syntax in user docs.

Pages at minimum: `docs/content/tyhpdef_extensions.md`, `docs/content/tyhp_2100_extensions.md`, `docs/content/quickref_tyhpdef.md`, `docs/content/quickref.md`, `docs/content/faq_tyhpdefSyntax.md`, `docs/content/diagnostics_reference.md` (new codes).

### AIDevGuide

`AIDevGuide/` is the bundle an agent loads to write Tyhp applications, and it is **regenerated** from the prompt in `AIDevGuide/REGEN.md`. A claim corrected only in a section file comes back the next time the bundle is regenerated, so update the prompt as well as the section.

| File | What this story changes |
|---|---|
| `AIDevGuide/guide/12-extensions.md` | The three Tyhp `extension { }` forms and which one leaves a PHP method; tyhpdef class-body members are `=>` mappings that erase; a written parameter must be declared `&` |
| `AIDevGuide/guide/23-tyhpdef.md` | Tyhpdef extension members are thin mappings only — no brace bodies, no generated PHP |
| `AIDevGuide/guide/29-php-mapping.md` | Spliced call site → expression, versus a backer static call for a multi-statement member |
| `AIDevGuide/QUICK_GUIDE.md` | Extension and tyhpdef-extension lines point at the updated sections |
| `AIDevGuide/REGEN.md` | Prompt items 12 (extensions) and 23 (tyhpdef) currently describe brace-bodied tyhpdef inline extensions; restate as thin `=>` mappings with the by-reference rule |

### Acceptance

- [ ] Tyhpdef docs describe thin mappings only
- [ ] Tyhp docs describe the three forms and what PHP keeps
- [ ] The `&` requirement for written parameters, and the referenceability requirement for `&` arguments, are documented
- [ ] New diagnostics appear in `diagnostics_reference.md`
- [ ] `AIDevGuide/guide/12-extensions.md` and `23-tyhpdef.md` match the shipped forms, and `REGEN.md` items 12 and 23 would regenerate that same text

---

## Diagnostic Codes

New checker codes (`4174`–`4177` and `4181`; `4173` is the last allocated before the 4200 code-quality range). Register in `Tyhp/Domain/Exceptions/MessageCode.cs` and add `ERROR_TYHP####` entries to both `.resx` files.

| Code | Enum | Severity | When |
|------|------|----------|------|
| 4174 | `CheckerInlineParameterMutation` | Error | A spliced member's expression writes a parameter not declared `&`, declares `&` on a parameter it does not write, or mutates `$this` |
| 4175 | `CheckerInlineCycle` | Error | A spliced member reduces to itself directly or transitively |
| 4176 | `CheckerInlineAttributeOnExtensionMember` | Error | `#[\Tyhp\Optimize\Inline]` on an extension member (tyhpdef thin member or Tyhp `extension { }` member) |
| 4177 | `CheckerReservedInlineTempPrefix` | Error | User code declares a variable colliding with the generated inline temp prefix |
| 4181 | `CheckerErasedMemberUnsafeSplice` | Error | An erased member's call site cannot be spliced faithfully, and has no PHP method to fall back to |

Updated:

| Code | Change |
|------|--------|
| 8013 | `TyhpdefExtensionOperatorRequiresBody` — message now says a mapped operator requires a thin `=>` expression; bodyless is native `operator`; brace bodies are not this form |

`4178`–`4180` are Story 23: `#[\Tyhp\Optimize\Inline]` on an invalid target or body (`4178`), a spliced body referencing a less accessible member (`4179`), and a non-referenceable argument to a by-reference parameter (`4180`). `4179` and `4180` are enforced by this story's engine even though Story 23 allocates them. Do not allocate optimizer (`47xx`) codes in this story.

---

## Generated Names

Add to `Tyhp/TyhpLang/GeneratedNames.cs` (checker must reserve it, same as `ExtensionBackerSuffix`):

| Name | Value | Purpose |
|---|---|---|
| `InlineTempVariablePrefix` | `__tyhpInlineTemp` | Call-site temp for a repeated parameter (`$__tyhpInlineTemp1`, `$__tyhpInlineTemp2`, …), unique per call site |

Add a `StartsWithInlineTempPrefix(string?)` helper mirroring `EndsWithExtensionBackerSuffix`, and reserve the prefix in the checker (`4177`). Unlike `ExtensionBackerSuffix`, this name **does** appear in emitted PHP.

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **03** | Introduced class-body `extension function` / `extension operator` with brace or `=>` bodies plus synthetic-class emit. **Do not rewrite 03**; this story supersedes that syntax. |
| **09 / 11** | Emit currently generates `__TyhpInlineExt_*` for class-body tyhpdef members and always emits extension backer methods. This story splices instead, and omits backer methods for short `=>` members. The splice engine lives in the same pre-emit rewrite pass as extension-call conversion. |
| **17** | Spliced nodes carry `OriginalAst` so sourcemaps point at the original Tyhp call site. |
| **20** | Standalone tyhpdef `extension Name { }` is already thin. **Reverses** "do not omit the PHP backer method because it was `#[Inline]`" and removes `#[Inline]` from Track C output; form now decides. |
| **20.5** | Unrelated gating. Thin members still gate with `declare(php=…) { … }`; `#[\Tyhp\Php]` is not allowed on `extension` declarations. |
| **20.7** | **Prerequisite.** Tyhpdef `{ get; set; }` / `{ &get; }` and binder flags `HasAccessor` / `HasGetHook` / `HasSetHook` / `GetHookReturnsRef`. This story's `4180` and by-value hooked read-local **read those flags** on tyhpdef-bound properties. `&get` / `{ get; }` in a **generated** tyhpdef are visible to a consumer; `&__get` / `&offsetGet` stay as they are. Do not re-implement hook grammar here. |
| **21** | Package tyhpdefs attach methods/operators through thin mappings. Multi-statement logic stays in `tyhp_src/` and keeps a PHP backer. |
| **23** | Extension splicing is **not** optimizer work — Story 23's extension inlining and synthetic-class elimination phases are superseded. Story 23 keeps `#[\Tyhp\Optimize\Inline]` for **non-extension** functions / methods / operators, reusing this story's splice engine and safety rules, where PHP always keeps the member and `optimize: none` means "call it instead of splicing." Story 23 Phase 5 is the specification for the by-reference rules, the accessibility rule (`4179`), and the `self::` / `parent::` / `static::` rewrite; this engine implements them. Because an attributed member always has a PHP method, an unspliceable call site there warns and keeps the call, where an erased member reports `4181`. |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). See `CONVENTIONS.md` and Story 07.

- [ ] **Golden fixtures:** tyhpdef thin `extension fn` / `extension operator` call-site splice (parenthesized) and default-arg splice; parse rejection of tyhpdef brace forms; `TYHP8013` for a bodyless mapped operator; Tyhp extension with all three member forms → PHP has only the two brace members, tyhpdef copies two expressions and maps one to the backer; all-`=>` extension → no backer class; repeated-parameter hoist as a by-value local and as a ref-bound local; `&` mirrored onto an emitted backer method and its tyhpdef stub; `4174` / `4175` / `4176` / `4180` / `4181`; `4180` / by-value read-local against a generated tyhpdef `{ get; }` vs `{ &get; }` (`HasAccessor` / `GetHookReturnsRef`)
- [ ] **Unit / integration tests:** grammar, `IsShortSyntax` flag (incl. cache round-trip), splice engine (substitution, fixpoint, temp naming), checker rules, emit, Track C writer
- [ ] **Conformance run green** before story done
- [ ] **Runtime self-host:** rebuild `runtime/packages/*` after the tyhpdef syntax migration and diff committed PHP
- [ ] **No tyhpdef-only backer** in emitted PHP
- [ ] **No PHP backer method** for Tyhp `extension { }` short `=>` members
