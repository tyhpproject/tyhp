# Implementation Plan: Story 27.2 — Block-Target Extension Syntax

> **Roadmap position:** Story 27.2 — **Tier 2 — DX & Ecosystem** (after **27.1** in the numbered sequence, before **27.3** repo split / deferred **22**). **In the beta.**
> **Direct dependencies (new numbering):** **03**, **11**, **20**, **20.6** (existing standalone `extension { }` pipeline, three member forms, `use extension`). Does **not** depend on Stories **27** / **27.1**.
> **May be implemented in parallel with 27 / 27.1.** Must finish **before 27.3** — this story rewrites `runtime/packages` (`tyhp/core` catalogs), docs, AIDevGuide, tests, and samples.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Source:** Design discussion — replace per-member `extends T $this` / `operator +<T>` with a class-like `extension Name extends Type` header plus nested `extends Type { }` groups (2026-09-18)
> **Branch:** TBD
> **Prerequisites:** Stories through **21.12** (extension emit, tyhpdef standalone extensions, `tyhp/core` scalar catalogs). Optimizer (23–24), `internal` enforcement (25), and `?->` assignment (26) are **not** prerequisites.
> **Zero users / one syntax:** this is a breaking grammar change. Do not keep the old per-member forms, do not dual-emit, do not deprecate-for-a-release.

Standalone `extension { }` today has no type on the header. Each method names a receiver (`extends T $this`) and each operator names a target (`operator +<T>`). That is the C# extension-method shape. This story moves the target onto the **block**, so methods look like instance methods (`$this` implied) and operators list every operand with `self` meaning the target. PHP emit stays a class of `public static` methods; only the Tyhp spelling and the `self` rewrite change.

Class-body tyhpdef `extension fn` / `extension operator` (Story 20.6 thin mappings) are **out of scope**. Those already have an implicit receiver (the enclosing type) and must not grow `extends T $this` or lose it. Do not conflate the two features.

---

## Architecture Overview

### Locked syntax

One spelling for live Tyhp and standalone tyhpdef `extension Name { }`. Header XOR nested groups.

**Single-target (header):**

```tyhp
extension StringExtensions extends string {
    fn reverse(): string => \implode('', \array_reverse(\Tyhp\StringHelper::strSplit($this)));

    function match(string $pattern, int $flags = 0, int $offset = 0): ?\PregMatchSet { … }

    function sort(&$this): void { \sort($this); }   // mutating receiver annotation only
}

extension MoneyOps extends \App\Money {
    operator + (self $left, self $right): self => …;
    operator + (int $left, self $right): self => …;   // int + Money
}
```

**Mixed bag (nested groups, no header target):**

```tyhp
extension NumericHelpers {
    extends int {
        fn abs(): int => \abs($this);
    }

    extends float {
        fn abs(): float => \abs($this);
    }

    extends<T> \App\MyClass<T> {
        function id(): T { return $this->id; }
    }

    extends<TRight> \App\Pair<string, TRight> {
        function right(): TRight { return $this->right; }
    }
}
```

**Generic binder on the extension name** (same meaning as `extends<T>` on a nested group):

```tyhp
extension MyClassOps<T> extends \App\MyClass<T> {
    function id(): T { return $this->id; }
}
```

`extension Name<T extends int|string> extends \App\MyClass<T>` is legal. The first `extends` is a generic constraint; the second is the target. The parser already distinguishes those roles on classes.

### Locked decisions

1. **Keyword is `extends`.** `extension Name …` already reads as a type declaration; `extends Type` after the name matches `class Name extends Type`. No new keywords (`on`, `for`, …). Nested groups reuse the same `extends Type { }` / `extends<T> Type { }`. Rejected alternatives (do not implement): `for`, `as`, `use`, `implements`, `extension Name(Type)`, `extension Name: Type`, per-member `extends T $this`.
2. **Drop** `function f(extends T $this, …)` and `operator +<T>(…)`. One syntax. Helpful diagnostic on the old forms (do not leave a silent parse error).
3. **Operators list every operand.** `self` in an operator signature or body is the **target type of that header/group**, not the extension class. There is no `<Type>` on the `operator` token. `int + Money` is `operator + (int $left, self $right)`.
4. **`$this` is implied** and never appears in the caller argument list. It is **not** always by-ref on the PHP backer:
   - **Object / class / interface / enum targets:** the emitted first parameter is always by-ref (`&$this_`). Object handles already share identity; `&` is required if the body reassigns `$this`.
   - **Scalar, `array`, struct targets:** by-value unless the member writes `$this` (assignment, `++`/`--`, pass to `&`). Writing `$this` requires the receiver annotation `function sort(&$this): void`. Call sites then need a referenceable receiver (existing TYHP4174 / TYHP4180). Pure methods stay `'hello'->length()`.
   - There is no `extends T $this` parameter. `function sort(&$this)` is a receiver annotation (optional `&$this` only; no type on it — the type comes from the block).
5. **Header XOR nests.** A header `extends Type` forbids nested `extends` groups. Nested groups forbid loose members at the extension-body top level. Mixing is a compile error.
6. **Mixed generic application is allowed:** `extends<TRight> Pair<string, TRight>`, `extension Foo<T> extends Pair<string, T>`.
7. **Overlapping same member is an error, not “more specific wins.”** If `MyClass<string>` and `MyClass<T>` (given `T`’s constraints) can both apply to the same receiver, and both groups define the same method name or the same operator, that is a compile-time error. Different members on overlapping targets are fine. Non-overlapping targets (`Money` vs `DateTime`) may share a method name inside one extension.
8. **`self` / `new self()` / `self::` are legal** in extension members and mean the **target type**, not the extension class. PHP emit **must rewrite** them to the actual target (FQCN or builtin lowering). These methods live on the extension class; a raw PHP `self` would be the extension, which is wrong. To name the extension class (constants, later), write the extension identifier (`MoneyOps::DEFAULT_CURRENCY`). **`static::` is illegal** in extension members (no late-static referent). **`parent::` is illegal.**
9. **Unused type parameter `T`** on `extension Name<T>` or `extends<T>` is an error.
10. **Extra method generics** (`function mapTo<R>(…): R`) are allowed and must **not shadow** an in-scope `T` from the extension or group binder.
11. **Target is one type:** a named class / interface / enum / alias / builtin / generic application. **Not** nullable (`?T`), **not** `never` / `void`, **not** a union. No `mixed`. Intersections are not a single named target — reject them (write a type alias if needed).
12. **No wildcard `_`.** `extends<T> MyClass<T>` is the open-parameter form.
13. **One syntax** across Tyhp and standalone tyhpdef `extension Name { }`.

### What does not change

- PHP lowering: static methods on the extension class; call sites `$x->m()` → `\E::m($x)` (or splice for `fn … =>`).
- Three member forms (`fn … =>` erased; single-`return` brace spliced + backer; multi-statement backer). Story 20.6.
- `use extension` / `global use extension` / `insteadof` / `as` / `hide`.
- Operator adaptations still name a target as `E::operator +<Money> hide` (that `<Money>` is an **adaptation qualifier**, not the old declaration form). Method `as` / `insteadof` on a name that exists on **more than one** target group in that extension is an error; `hide` of that name hides every group.
- `#[\Tyhp\Optimize\Pure]`; `#[Inline]` still TYHP4176.
- Class-body tyhpdef `extension fn` / `extension operator` (implicit `$this`, no header `extends`).
- No static extension methods. No `Type::method` declaration spelling.

### Keyword choice (why `extends`, not a new word)

Existing keywords that could sit after the name without adding lexer tokens:

| Spelling | Why not (or why yes) |
|---|---|
| `extension Name extends Type` | **Chosen.** Same rhyme as `class Name extends Type`. `extension` already feels like a type declaration. |
| `extension Name for Type` | `for` is a PHP keyword, but it reads as purpose/loop, not heritage. Rejected to keep the class parallel. |
| `extension Name as Type` | `as` already means alias / import rename. |
| `extension Name use Type` | Collides with `use` / `use extension`. |
| `extension Name implements Type` | False friend: the target is not an interface the extension implements. |
| `extension Name(Type)` / `Name: Type` | Looks like a constructor or a return type. |

The only real cost of `extends` is density when a generic constraint is also present: `extension Ops<T extends int|string> extends MyClass<T>`. That is accepted.

### Overlap rule (worked example)

```tyhp
extension A {
    extends \App\MyClass<string> { function label(): string { return 's'; } }
    extends<T> \App\MyClass<T>    { function label(): string { return 't'; } }
}
```

`MyClass<string>` is a substitution of `MyClass<T>`, so both `label` members can apply to a `MyClass<string>` receiver → **error**. Removing one `label`, or constraining `T` so it cannot be `string` (`T extends int`), makes them disjoint → OK.

Do **not** implement C# / Kotlin “more specific wins.”

### `self` emit

```tyhp
extension MoneyOps extends \App\Money {
    function doubled(): self { return $this + $this; }
    operator + (self $left, self $right): self { return $left->plus($right); }
}
```

Emitted PHP (illustrative):

```php
class MoneyOps {
    public static function doubled(\App\Money &$this_) : \App\Money {
        return \MoneyOps::op_plus($this_, $this_);
    }
    public static function op_plus(\App\Money $left, \App\Money $right) : \App\Money {
        return $left->plus($right);
    }
}
```

Checker / binder `self` inside the member still means `\App\Money` (the target), so `new self()` type-checks as `Money`. Only the **PHP text** of `self` is rewritten. Inliner canonicalization (Story 31 / DESIGN_OPEN_QUESTIONS §8) is the same rewrite if a body is spliced.

---

## Pipeline

```
Stories 03 / 11 / 20.6 (current per-member extends T $this and operator +<T>)
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 27.2: Block-target extension syntax               │
│                                                          │
│  Phase 1: Grammar + AST (header, nested groups, drop old)│
│  Phase 2: Binder (target per group, self, generic binders)│
│  Phase 3: Checker (XOR, overlap, unused T, target shape, │
│           &$this annotation, shadowing)                  │
│  Phase 4: Emitter (implied $this, self rewrite, & rules) │
│  Phase 5: Mechanical rewrite (tyhp/core, tests, samples) │
│  Phase 6: LSP / highlighting                             │
│  Phase 7: Docs, AIDevGuide, Story 03/29 example refresh  │
└──────────────────────────────────────────────────────────┘
    │
    ▼
Story 27.3 — split four source repos — then skip deferred Story 22
```

### Diagnostic codes

Add `MessageCode` values in the **4300–4399** feature-checker band at implementation time (`MessageCode.cs` + both `.resx` files). Do **not** reuse numbers already assigned (including Stories 27 / 27.1). See `CONVENTIONS.md`. Do not document specific numbers in this plan.

Reuse existing codes where the rule is the same (empty extension TYHP4172, inline attribute TYHP4176, by-ref receiver TYHP4174 / TYHP4180, operator target not instantiable TYHP3025). Retarget `CheckerExtensionMissingExtends` (TYHP4147) from “first parameter must be `extends T $this`” to “extension must have a header `extends Type` or nested `extends` groups.”

Expected new / retargeted diagnostics (names indicative):

- Old `extends T $this` / `operator +<T>` spelling (parser recovery + message pointing at the block form)
- Header `extends` mixed with nested `extends` groups
- Nested groups with a loose top-level member
- Target is nullable / union / `void` / `never` / `mixed` / intersection
- Unused type parameter on `extension Name<T>` or `extends<T>`
- Method generic shadows an in-scope extension/group `T`
- Two applicable groups define the same method or operator (overlap)
- `static::` / `parent::` in an extension member
- `function sort(&$this)` on a member that never writes `$this` (error — unused by-ref annotation)
- `&$this` annotation with a type written (`&string $this` / `extends T &$this`) — gone; use `&$this` only
- `self` in an extension with no target in scope (should be unreachable if XOR is enforced)

Binder codes in the 3000s if a target type cannot be resolved (keep TYHP3016 / 3025 behavior, now on the block target instead of `operator +<T>`).

---

## Phase 1: Grammar and AST

Files: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`, `TyhpLexer.g4` (no new tokens), matching tyhpdef grammar, AST (`TyhpExtensionDeclAst`, operator AST), visitor, `Ast/technical-guide.md`.

**Header.** Allow optional generic parameters on the extension name and optional `extends typeExprWithoutStatic`:

```
internal? 'extension' T_STRING genericParams? ('extends' typeExprWithoutStatic)? '{' body '}'
```

**Nested group.** A member alternative:

```
'extends' genericParams? typeExprWithoutStatic '{' member* '}'
```

Groups nest **one** level. Do not allow `extends` groups inside groups.

**Members.** Functions/short-fns keep `parameterList` but **drop** `extends` on the first parameter (`functionParametersGrammarAddon` no longer allows `extends` there for extension members). Allow an optional leading receiver annotation `&$this` (by-ref token + `$this` only, no type, not a caller parameter). Operators drop `T_SYM_LT TargetType T_SYM_GT` after the operator token; parameter list is a normal list (unary = one param, binary = two, convert as today).

**Old spelling.** Prefer a dedicated parse alternative that matches `extends type $this` as first parameter / `operator op <type>` and reports the new diagnostic, so authors see a language error rather than “unexpected `extends`.” Same for tyhpdef standalone `fn length(extends string $this)`.

**Tyhpdef standalone** `tyhpdefStandaloneExtensionDeclarationStatement` gets the same header + nested groups. Short `fn` / `operator` members only (existing tyhpdef rule). Class-body `extension fn` grammar **unchanged**.

**`use extension` adaptations.** Keep `E::operator +<Money>` as a qualifier. Do not reintroduce `<Type>` on operator **declarations**.

Update the grammar comments that currently say “Extensions cannot `extends`.”

---

## Phase 2: Binder

- Bind a **target type** on the extension (header) or on each nested group. Contribute methods/operators to that target’s extension lists (same lists as today: `ExtensionContributedOperators` / method contribution).
- `extension Name<T>` and `extends<T>` introduce a type-parameter scope for the header/group. Method generics nest inside and must not shadow (checker can flag; binder should still record both).
- Inside a targeted member, `$this` is a bound receiver of the target type (not a source parameter). `self` resolves to the target type (not the extension class).
- `operator + (self $left, self $right)` — `self` in those parameter types is the group target.
- Unresolvable / non-instantiable targets: existing TYHP3016 / TYHP3025 (and retarget “not allowed” TYHP3015) on the block type.
- Synthetic inline extension scopes for class-body tyhpdef (20.6) stay as they are.

---

## Phase 3: Checker

- Empty extension still TYHP4172.
- Enforce header XOR nests.
- Validate target shape (decision 11).
- Unused `T` (decision 9).
- No shadowing of `T` by method generics (decision 10).
- Overlap: for each pair of groups (including across **different** extensions that are both in scope? **No** — today’s `use extension` / `insteadof` already resolves cross-extension conflicts. This overlap rule is **within one extension declaration** (and within one compilation of the same extension type). Two separate extensions that both add `label` to `MyClass<string>` remain an activation/`insteadof` problem, not this diagnostic.
- `&$this`: if the body writes `$this` and the annotation is missing, error (same spirit as “parameter that is written must be declared `&`”). If `&$this` is present and the body never writes `$this`, error. Object-target members that only *mutate the object through methods* without assigning `$this` do **not** need `&$this` (the handle is shared); they still emit `&$this_` on the backer because the target is an object (decision 4).
- `static::` / `parent::` illegal.
- Visibility / `internal` on the extension declaration unchanged.
- Instance-call arity still excludes the receiver (`ExcludeExtensionReceiver`).

---

## Phase 4: Emitter

- First PHP parameter is the receiver (`$this_`), never a source-level argument at Tyhp call sites.
- Rewrite every `self` / `self::` / `new self()` in the member body **and** in emitted signatures to the target type’s PHP spelling. Do this **before** splicing (`fn … =>` / single-return) so inlined call sites do not capture `self` as the caller class.
- Object targets: always `&$this_` on the backer. Scalar/array/struct: `&` only when `&$this` was annotated.
- Operator method names stay `OperatorMethodNameGenerator` output. Convert-to/from still `E::__to{T}` / `E::__from`.
- Do not emit a `$this` parameter into PHP named-argument positions.

---

## Phase 5: Mechanical rewrite (greenfield)

Rewrite every in-tree use of the old spelling. No dual parse.

**Runtime (must finish before 27.3):**

- `runtime/packages/core/tyhp_src/StringExtensions.tyhp` (and `*Versioned`) → `extension StringExtensions extends string`
- `ArrayExtensions`, `IntExtensions`, `FloatExtensions`, `BoolExtensions`, `ClosureExtensions` — same pattern (single-target header)
- `runtime/packages/php-ext-intl/_tyhpdef/extensions/string.tyhpdef` if it uses standalone `extends T $this`
- Re-emit via `runtime/packages/base-build-all.sh` after the compiler accepts the new syntax. Do **not** hand-edit `src/*.php`. Do **not** git-restore generated PHP.

**Tests / samples / conformance:** `tests/Tyhp.Tests/**` (parser, binder, checker, emitter extension tests), `tests/conformance/**`, `tests/Tyhp.Tests/TestData/**`, `tyhp-lang/vscode/samples/highlight-audit.tyhp` (+ `.tyhpdef`).

**Minimal tyhpdefs** (`__minimal_php.tyhpdef`) if they declare standalone extensions.

---

## Phase 6: LSP and highlighting

- Completion / hover / signature help: implied `$this`, no first-parameter `extends`.
- Semantic tokens / vscode TextMate: `extends` after `extension Name` and nested `extends Type {`; stop highlighting `operator +<Type>` as a declaration (keep it on `use extension` adaptations).
- PhpStorm: same if the plugin has dedicated extension highlighters.

---

## Phase 7: Docs and guides

Update to the new spelling only (current contract; do not document the removed form except as a one-line migration note if a diagnostic already names it):

- `docs/content/tyhp_2100_extensions.md`
- `docs/content/tyhpdef_extensions.md`
- `docs/content/tyhp_1600_operatorOverloads.md`
- `docs/content/quickref.md`, `quickref_tyhpdef.md`, `faq_tyhpSyntax.md`
- `README.md` extension sample
- `AIDevGuide/guide/12-extensions.md`, `guide/23-tyhpdef.md`, `QUICK_GUIDE.md`, `SKILL.md`, `REGEN.md`
- Grammar/binder/checker/emitter `technical-guide.md` comments
- Story **03** header: add a “Story 27.2 supersedes `operator +<T>` declaration syntax” note (do not rewrite the whole completed plan).
- Story **29** examples that still show `extends Account $this`
- `docs/content/release_roadmap.md` is updated as part of the numbering shuffle (this story), not as a user-facing “we used to write `extends $this`” history dump

`DESIGN_OPEN_QUESTIONS.md` §8 (extension **properties**) stays open. Point its “no block-level receiver” bullet at this story: methods/operators now have a block target; property syntax can reuse nested `extends Type { public string $formatted; }` when that feature is scheduled. Do not schedule properties here.

---

## Golden Fixtures / Tests (Acceptance)

- Parse: header target, nested groups, `extension Name<T> extends MyClass<T>`, `extends<T> MyClass<T>`, `extends<TRight> Pair<string, TRight>`, `operator + (self, self)`, `operator + (int, self)`, `function sort(&$this)`, short `fn` with implied `$this`.
- Parse-error / diagnostic: old `extends T $this`, old `operator +<T>`, header+nests mix, loose member + nests, `?string` / `A|B` / `void` / `never` targets, unused `T`, shadowed `T`, overlapping `label` on `MyClass<string>` vs `MyClass<T>`, `static::`, `'hello'->mutating()` when `&$this`.
- Overlap OK: `MyClass<T extends int>` vs `MyClass<string>` same method name (disjoint); `Money` vs `DateTime` same method name in one extension.
- Emit: `self` rewritten to target FQN; object backer `&$this_`; `string` `length` by-value so a literal call site compiles; splice of `fn reverse(): string =>` still omits the PHP backer.
- `tyhp/core` catalogs compile; `'abc'->length()` still works; `use extension` + `insteadof` / `hide` still resolve.
- Class-body tyhpdef `extension fn` fixtures unchanged.

---

## Out of scope

- Extension instance properties / constants / static members (`DESIGN_OPEN_QUESTIONS.md` §8)
- Static extension methods / `Type::method` declarations
- New keywords
- Specificity ranking for generic applications
- Wildcard `_`
- Changing PHP lowering away from static methods on the extension class
- Class-body tyhpdef thin mappings (20.6)
- Repo split (Story **27.3**)

---

## Risks / notes

- **Double `extends`.** `extension Ops<T extends Constraint> extends Target` is grammatical; add a parse test so the constraint `extends` is not eaten as the target.
- **`use extension` vs nested `extends`.** Nested groups are not `use` statements. Keep the keywords’ jobs distinct.
- **`$this` always-by-ref was rejected for scalars.** PHP cannot pass a literal by reference. Object targets still always `&` on the backer.
- **Overlap is intra-extension.** Do not invent a whole-program specificity lattice.
- **Story 03 text still shows `operator +<T>`.** Add a supersession note; implementers follow this story, not 03’s declaration examples.

---

## Definition of Done

- [ ] Grammar, AST, binder, checker, emitter implement header + nested groups; old per-member forms diagnostic.
- [ ] `self` rewritten on PHP emit; `&$this` rules as locked.
- [ ] Overlap / unused T / XOR / single-type target diagnostics green.
- [ ] `tyhp/core` catalogs and all in-tree tests/docs/guides on the new spelling.
- [ ] Class-body tyhpdef `extension fn` unchanged.
- [ ] `dotnet test` for extension parser/binder/checker/emitter filters green; `runtime/packages/base-build-all.sh` re-emits after the compiler lands.

---

## Relationships

- **Requires:** Stories **03**, **11**, **20**, **20.6** (extension pipeline). Sequenced after **27.1** only so the numbered beta list is 27 → 27.1 → **27.2** → **27.3**; implementation may proceed in parallel with 27 / 27.1.
- **Unblocks:** Story **27.3** (repo split — do not extract trees until this rewrite is in them); later extension properties (§8) can reuse nested `extends Type { }`.
- **Does not unblock:** Story 22 (still deferred).
