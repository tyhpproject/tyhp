# Implementation Plan: Story 20.7 — Tyhpdef Hooked Properties

> **Roadmap position:** Story 20.7 — **Tier 2 — DX & Ecosystem** (additive sub-story). Numbered after 20.6 was allocated; **implement this story before Story 20.6.** Inserted after Story 20.5, before Story 20.6.
> **Direct dependencies (new numbering):** 02, 08, 14.5, 20, 20.5
> **New story:** tyhpdef can declare PHP 8.4 property hooks as bodyless signatures (`{ get; set; }`, `{ &get; }`, hook visibility / `final` / attributes). Generators emit that form. Binder/checker treat a tyhpdef hooked property as hooked, including whether `get` is by reference.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-24
> **Last design lock:** 2026-08-24 — bodyless hook list on tyhpdef class/interface/trait properties; Track C copies hook *shape* not bodies; Track A/B PHP hooks gate to `>=8.4` on multi-target merge; Tyhp Track C does **not** auto-gate (polyfill exists)
> **Prerequisites:** Story 02 (binder `HasAccessor` / `PhpPropertyAst.Hooks`); Story 08 (existing property-hook checker rules); Story 14.5 (PHP `get;` / `set;` / hook attributes); Story 20 (generator IR + writer + Tracks A/B/C); Story 20.5 (`#[\Tyhp\Php]` / `declare(php=…)` on members).
> **Consumers:** Story 20.6 (splice `4180` / read-local for hooked arguments); Story 21 (package tyhpdefs that describe hooked PHP APIs); Story 23 (same splice rules on attributed non-extension members); Story 29 (reflection `hasAccessor()`).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Phase 1: Grammar — bodyless tyhpdef hook lists](#phase-1-grammar--bodyless-tyhpdef-hook-lists)
- [Phase 2: Visitor, AST, binder](#phase-2-visitor-ast-binder)
- [Phase 3: Checker](#phase-3-checker)
- [Phase 4: IR + output writer](#phase-4-ir--output-writer)
- [Phase 5: Track C — Tyhp → tyhpdef](#phase-5-track-c--tyhp--tyhpdef)
- [Phase 6: Track A + Track B](#phase-6-track-a--track-b)
- [Phase 7: Story 20.5 gating](#phase-7-story-205-gating)
- [Phase 8: Unwind Stories 20 / 20.5 / 20.6](#phase-8-unwind-stories-20--205--206)
- [Phase 9: User documentation](#phase-9-user-documentation)
- [Diagnostic Codes](#diagnostic-codes)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Tyhpdef cannot currently say that a property is hooked. `tyhpdefClassProperty` is modifiers + type + `$name;`. The generator IR has unused `GetterHint` / `SetterHint` fields the writer is forbidden to emit. A consumer compiling against `package.tyhpdef` therefore treats `$obj->hooked` as a storage property: it will not reject it as a `&` argument, and it will not hoist a by-value `get` into a read local.

This story adds **bodyless** hook lists to tyhpdef, the same way methods are signatures only:

```tyhp
<?tyhpdef

class Holder {
    public string $hooked { get; set; }
    public int $hookedCount { get; }
    public array $refItems { &get; set; }
    public string $name { get; private set; }
}
```

The same `PhpPropertyAst.Hooks` / `ObjectPropertySymbol.HasAccessor` path Tyhp source already uses then lights up for tyhpdef. Tracks A/B/C emit the form. Story 20.6's splice engine can read `HasAccessor` and whether `get` returns by reference.

---

## Motivation

Story 20 asked Track C to “include property accessor declarations” / “output accessor hints,” and put `GetterHint` / `SetterHint` on the IR, but Phase 5.3 also required the writer to match the existing grammar — `Type $name;` only. The implementation left the fields unused and documented them as reserved.

Story 20.6 (not yet implemented) needs the missing syntax for two splice rules:

1. A **by-value hooked** property is not referenceable (`4180`).
2. A **`&get`** hooked property **is** referenceable; a by-value `get` is not. Without `&get` in tyhpdef, both look like storage.

`&__get` and `&offsetGet` are already expressible (`tyhpdefImportClassMethod` has `ReturnsRef`). Hooked properties are the hole.

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|--------------|
| Tyhpdef class / interface / trait property hook lists: `get;` / `set;` / `&get;`, hook visibility, `final`, hook attributes | Hook **bodies** (`get { … }`, `get => expr`) in `.tyhpdef` |
| Binder flags so a tyhpdef hooked property is indistinguishable from a Tyhp one for `HasAccessor` / by-ref get | Changing Tyhp `.tyhp` hook syntax or the PHP &lt; 8.4 polyfill |
| Replace `GetterHint` / `SetterHint` with a structured hook list on `TyhpdefProperty`; writer emits `{ get; set; }` | Default values on tyhpdef properties (still omitted) |
| Track C: copy hook *shape* (which hooks, `&get`, modifiers, attributes), never bodies | `virtual` as a distinct tyhpdef keyword (PHP has no signature for it) |
| Track A: reflect PHP 8.4 `getHooks()` / by-ref get into JSON + IR | Auto-`#[\Tyhp\Php(">=8.4")]` on Track C (Tyhp polyfill works on 8.2–8.3) |
| Track B: read `PhpPropertyAst.Hooks` from parsed PHP (including `#classPropertyAccessors`) | Struct properties (grammar is `tyhpStructProperty`; no hooks) |
| Multi-target Track A merge: hooked vs storage shapes gated with Story 20.5 | Story 20.6 splice engine itself (consumes this story) |
| Docs, tests, fixtures, TextMate sample; remove “hooks don’t exist in tyhpdef” assumptions in Stories 20 / 20.5 / 20.6 | Overlay `omit` / last-wins (Story 21); Story 29 reflection API |

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Form | Bodyless only, matching PHP abstract / interface hooks: `{ get; set; }`. Grammar rejects brace and `=>` hook bodies (same “grammar is truth” bar as Story 20.6 thin mappings). |
| One name | Hooked form is a **single** `$name`. `public int $a, $b { get; }` does not parse. Unhooked comma lists stay as they are. |
| What is declared | Presence of `get` and/or `set`; `&` on `get`; hook modifiers (`final`, visibility); attributes on each hook. No set-parameter list (`set(string $value)`); the property type is enough. |
| `&set` | Illegal, same as PHP / existing Tyhp checker. |
| Reuse PHP `hookedProperty`? | **No.** That rule allows bodies and `= default { hooks }`. Add `tyhpdefHookedProperty` / `tyhpdefPropertyHook` with `T_SYM_SEMICOLON` bodies only. |
| AST | Visitor builds the same `PhpPropertyDeclAst` + `PhpPropertyAst.Hooks` as `VisitClassPropertyAccessors`, with hook `Body == null` (identical to `get;` in an interface). |
| Symbol flags | `HasAccessor` stays `prop.Hooks != null`. Add `HasGetHook`, `HasSetHook`, `GetHookReturnsRef` on `ObjectPropertySymbol` (set for **both** Tyhp and tyhpdef so Track C and 20.6 share one read). |
| `AccessorKind` | Derive when useful (`Get` / `Set`); both hooks → leave unset or do not invent a `Both` enum value in this story. |
| Tyhpdef is description | `&get` on a tyhpdef property does **not** raise `CheckerByRefPropertyGetHookRequiresPhp84`. That diagnostic is for **emitting** Tyhp toward `output.phpVersion` &lt; 8.4. Gate the **symbol** with `#[\Tyhp\Php]` / `declare(php=…)` when the PHP API is 8.4-only. |
| Track C (Tyhp libraries) | Always emit the hook list when the Tyhp property has hooks. Do **not** auto-apply `#[\Tyhp\Php(">=8.4")]` — the polyfill is the 8.2–8.3 contract. |
| Track A / B (real PHP) | Hooks exist in the engine from 8.4. Single-target: emit what that PHP has. Multi-target (`--php-targets`): if hook shape differs across minors, emit version-disjoint members (storage form vs hooked form) with Story 20.5 gates. If every target agrees, emit one hooked (or storage) declaration. |
| Defaults | Still omitted in tyhpdef, including hooked properties that have PHP defaults. |
| Promoted ctor hooks | Class-body hooked properties are the tyhpdef form. Track C already walks `ObjectPropertySymbol`, so a promoted hooked Tyhp property becomes a class-body hooked tyhpdef property. Do not add hook lists to tyhpdef constructor parameter syntax in this story. |
| Structs / extensions | No hooked properties. Structs keep `tyhpStructProperty`. `#[\Tyhp\Php]` remains illegal on `struct` / `extension` (20.5). |

### Choosing a tyhpdef form

| PHP / Tyhp property | Write in tyhpdef |
|---------------------|------------------|
| Storage | `public string $name;` |
| Get + set hooks | `public string $name { get; set; }` |
| Get-only | `public string $name { get; }` |
| By-ref get | `public array $items { &get; set; }` (or `{ &get; }`) |
| Asymmetric hook visibility | `public string $name { get; private set; }` |
| 8.4-only PHP API (Track A/B merge) | `#[\Tyhp\Php(">=8.4")] public string $name { get; set; }` plus a gated storage twin when 8.2/8.3 had the same name without hooks |

---

## Phase 1: Grammar — bodyless tyhpdef hook lists

### Current

```
tyhpdefClassStatement
    : … propertyModifiers typeExprWithoutStatic tyhpdefPropertyList T_SYM_SEMICOLON
        #tyhpdefClassProperty
    ;
tyhpdefProperty
    : Variable=T_VARIABLE
    ;
```

PHP already has `#classPropertyAccessors` → `hookedProperty` (bodies allowed). Tyhpdef does not.

### Target

Add a second property alternative. Do **not** put a hook list on `tyhpdefPropertyList` (that would allow `$a, $b { get; }`).

```
tyhpdefClassStatement
    : Attributes=attributes? tyhpdefDeprecatedOrObsolete? Modifiers=propertyModifiers
        TypeExpr=typeExprWithoutStatic PropertyList=tyhpdefPropertyList
        T_SYM_SEMICOLON                                                         #tyhpdefClassProperty
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete? Modifiers=propertyModifiers
        TypeExpr=typeExprWithoutStatic PropertyAccessors=tyhpdefHookedProperty
                                                                                #tyhpdefClassPropertyAccessors
    | …
    ;

tyhpdefHookedProperty
    : Variable=T_VARIABLE T_OPEN_CURLY_BRACE
        Accessors=tyhpdefPropertyHookList T_CLOSE_CURLY_BRACE
    ;

tyhpdefPropertyHookList
    : Items+=tyhpdefPropertyHook+
    ;

tyhpdefPropertyHook
    : Attributes=attributes? Modifiers=propertyHookModifiers ReturnsRef=returnsRef
        Identifier=identifier T_SYM_SEMICOLON
    ;
```

Reuse `propertyHookModifiers` / `returnsRef` / `attributes` from `PhpParser.g4`. Empty `{ }` is a parse error (`+` not `*`).

Requires `./compile_grammar.sh`.

### Acceptance

- [x] `public string $name { get; set; }` parses in `.tyhpdef`
- [x] `public array $items { &get; set; }` parses
- [x] `public string $name { get; private set; }` and `final get;` parse
- [x] `#[Attr] get;` on a hook parses
- [x] `public string $name { get { return $this->x; } }` does **not** parse
- [x] `public string $name { get => $this->x; }` does **not** parse
- [x] `public int $a, $b { get; }` does **not** parse
- [x] Unhooked `public int $a, $b;` still parses
- [x] Tyhp `.tyhp` hook bodies unchanged

---

## Phase 2: Visitor, AST, binder

**Visitor** (`TyhpParserAstVisitor.Tyhpdef.cs`)

`VisitTyhpdefClassPropertyAccessors` mirrors `VisitClassPropertyAccessors`: one `PhpPropertyAst` whose `Hooks` is a `PhpPropertyHookListAst`. Each hook is `PhpPropertyHookAst.Create(..., body: null, ...)`, `ReturnsRef` from `returnsRef`, modifiers and attributes attached the same way as PHP interface `get;`.

Stop passing `hooks: null` in `VisitTyhpdefClassProperty` for the unhooked form (already null today).

**Binder** (`TyhpBinder.ObjectBody.cs` — shared for Tyhp and tyhpdef)

Today: `propSymbol.HasAccessor = prop.Hooks != null` only. Also set:

| Flag | Meaning |
|------|---------|
| `HasGetHook` | a hook named `get` |
| `HasSetHook` | a hook named `set` |
| `GetHookReturnsRef` | that `get` hook has `ReturnsRef` |

Walk `prop.Hooks`. Invalid hook names stay a checker problem (existing 4006).

**`ObjectPropertySymbol`**

Add the three flags. Do not require `ObjectAccessorMethodSymbol` for tyhpdef (no bodies to bind).

Update `Tyhp/TyhpLang/Binder/technical-guide.md` and `Tyhp/TyhpLang/Grammar/technical-guide.md`.

### Acceptance

- [ ] A tyhpdef `{ get; set; }` property binds with `HasAccessor`, `HasGetHook`, `HasSetHook`, `GetHookReturnsRef == false`
- [ ] `{ &get; }` sets `GetHookReturnsRef == true`
- [ ] A Tyhp source hooked property sets the same flags (so Track C can read them)
- [ ] Unhooked tyhpdef properties remain `HasAccessor == false`

---

## Phase 3: Checker

Reuse existing hook rules on the shared AST (duplicate `get`/`get`, invalid accessor name, illegal hook modifiers except `final` + visibility, `&set`, `final` override). They must run on tyhpdef-bound properties.

**Do not** fire `CheckerByRefPropertyGetHookRequiresPhp84` on tyhpdef declarations.

Empty hook list cannot parse. A hooked property with neither get nor set after invalid-name recovery → existing 4006.

`readonly` + `set` stays the existing Tyhp rule.

No new splice diagnostics here — Story 20.6 owns `4180` / read-local once it can see these flags.

### Acceptance

- [ ] Duplicate / invalid / `&set` diagnostics fire on `.tyhpdef` the same as on `.tyhp` for those shapes
- [ ] `&get` in a `.tyhpdef` does not report 8.4-required when `output.phpVersion` is 8.2
- [ ] `#[\Tyhp\Php(">=8.4")]` on the hooked property still omits the symbol when the target is 8.2 (Story 20.5)

---

## Phase 4: IR + output writer

**Replace** `TyhpdefProperty.GetterHint` / `SetterHint` (never populated, never emitted):

```csharp
public sealed record TyhpdefPropertyHook
{
    public string Name { get; init; } = ""; // "get" | "set"
    public bool ReturnsRef { get; init; }
    public List<string> Modifiers { get; init; } = [];
    public List<TyhpdefAttribute> Attributes { get; init; } = [];
}

// on TyhpdefProperty:
public List<TyhpdefPropertyHook> Hooks { get; init; } = [];
```

**Writer** (`TyhpdefOutputWriter.WriteProperty`):

- `Hooks` empty → `public string $name;` (today).
- `Hooks` non-empty → `public string $name { get; set; }` / `{ &get; set; }` / `{ get; private set; }`, one hook per line or a compact `{ get; set; }` — pick one style and test it; compact on one line is enough.
- Emit hook attributes before the hook name.
- Do not emit `get;` / `set;` for structs.

**Tests to invert**

`tests/Tyhp.Tests/Domain/Services/TyhpdefOutputWriterTests.cs` currently asserts generated output `Should().NotContain("get;")` / `"set;"` because “tyhpdefClassProperty has no attributes/hook syntax.” Change that fixture to **expect** hook syntax when `Hooks` is set, and keep a separate case that unhooked properties still omit `get;` / `set;`.

### Acceptance

- [ ] Writer emits `{ get; set; }` and `{ &get; set; }` that parse as tyhpdef
- [ ] Empty `Hooks` still emits `Type $name;`
- [ ] `GetterHint` / `SetterHint` are gone
- [ ] `AssertParses` on hooked writer output

---

## Phase 5: Track C — Tyhp → tyhpdef

`TyhpCodeTyhpdefGenerator.MapProperty` today copies name, type, modifiers, docs, deprecation. Also copy hooks from `ObjectPropertySymbol`:

- `HasGetHook` → `{ Name = "get", ReturnsRef = GetHookReturnsRef, … }`
- `HasSetHook` → `{ Name = "set", … }`
- Copy hook visibility / `final` / attributes from the bound hook AST when present

Never copy hook bodies. Never invent `#[\Tyhp\Php(">=8.4")]` for Tyhp-authored hooks.

### Acceptance

- [ ] A library with `public string $name { get { … } set { … } }` generates `public string $name { get; set; }`
- [ ] `&get` in Tyhp source generates `{ &get; … }`
- [ ] Unhooked properties stay `Type $name;`
- [ ] A consumer project that `include`s that `package.tyhpdef` binds `HasAccessor` / `GetHookReturnsRef` correctly

---

## Phase 6: Track A + Track B

**Track A JSON** (`Property` in Phase 2.2 / `json.schema.json` / `PhpReflectionPropertyDto` / `PhpReflectionDump.php`):

```json
"hooks": [
  { "name": "get", "returnsRef": false, "modifiers": [], "attributes": [] }
]
```

Omit `hooks` or use `[]` when PHP &lt; 8.4 or the property has no hooks. Dump via `method_exists($property, 'getHooks')` then `getHooks()`; `returnsRef` from the hook `ReflectionMethod`. Unknown JSON fields stay ignored.

**Mapper** fills `TyhpdefProperty.Hooks`.

**Track B** (`NativeTyhpdefGenerator.ExtractProperties`): today only walks `decl.Properties` and ignores `Hooks`. `#classPropertyAccessors` already produces a one-element list with `Hooks` set — copy that list into IR. If a PHP file uses hooked properties, they must appear in the tyhpdef (currently they would be emitted as storage, which is the same bug as Track C).

### Acceptance

- [ ] Reflection of a PHP 8.4 hooked property produces `hooks` in JSON and `{ get; set; }` in tyhpdef
- [ ] PHP 8.2 dump has no `hooks` (or `[]`) and emits storage form
- [ ] `--source` of a `.php` file with `public string $n { get => …; }` generates `{ get; }` (bodyless)
- [ ] `&get` round-trips through JSON `returnsRef`

---

## Phase 7: Story 20.5 gating

`#[\Tyhp\Php]` and `declare(php=…)` already apply to tyhpdef **members**. A hooked property is still a property: attributes on `tyhpdefClassPropertyAccessors` must flow to the symbol the same way as unhooked properties (visitor already has `Attributes=attributes?` on `tyhpdefClassStatement`).

Verify — and fix if the new alternative drops them:

- Property-level `#[\Tyhp\Php(">=8.4")]` on a hooked tyhpdef property
- Hook-level attributes (not version gates unless we document that `#[\Tyhp\Php]` on a **hook** is invalid / ignored; **lock:** version gates live on the property or an enclosing `declare`, not on individual `get`/`set`)

**Multi-target Track A (Story 20 Phase 8):** hook list is part of the member signature. If 8.2 has storage `$name` and 8.4 has hooked `$name`, emit two gated declarations (disjoint constraints), same as a method that gained a parameter.

**Track C:** no auto-gate (decision table).

### Acceptance

- [x] `#[\Tyhp\Php(">=8.4")] public string $n { get; set; }` is omitted at `output.phpVersion` 8.2 and present at 8.4
- [x] Hook-level `#[\Tyhp\Php]` is an error **8016** (`TyhpdefPhpVersionGateOnPropertyHook`); version gates belong on the property or an enclosing `declare(php=…)`, not on individual `get`/`set`. 4304 stays struct/extension-only.
- [x] Phase 8 merge tests: storage vs hooked across minors

---

## Phase 8: Unwind Stories 20 / 20.5 / 20.6

This phase is documentation + test/comment cleanup in those stories and the code they already shipped. **Do not implement Story 20.6 here.**

**Story 20 (implemented)**

- Phase 5.3 property syntax becomes `[modifiers] <type> $<name>;` **or** `[modifiers] <type> $<name> { get; set; … }`.
- Phase 6.1 “Include property accessor declarations” and 6.3 “Output accessor hints” are this story, not leftover 20 work.
- Delete / stop documenting `GetterHint` / `SetterHint`.
- Invert the output-writer test that forbids `get;` / `set;`.

**Story 20.5 (implemented)**

- Property-hook **language** feature in Story 21’s version table stays a language-feature row; tyhpdef can now **describe** hooked PHP APIs and gate them.
- Confirm hooked tyhpdef properties participate in member gating (Phase 7). No new declare key.

**Story 20.6 (not implemented)**

- Prerequisite **this story**.
- Remove “until tyhpdef can declare hooks” / “folded into Story 03.”
- Splice `4180` / by-value read-local **read** `HasAccessor` + `GetHookReturnsRef` from tyhpdef-bound symbols.
- `&get` / `{ get; }` in a **generated** tyhpdef are visible to a consumer; `&__get` / `&offsetGet` stay as they are.

**ROADMAP**

- The “Tyhpdef property hooks → Story 03” folded item is this story (already moved when 20.7 was filed). Do not restore it.

**In-tree comments / probes to update when coding**

- `TyhpdefDeclaration` XML on the old hint fields
- Writer test “no attributes/hook syntax”
- Any `GAP: tyhpdef has no syntax for property hooks` comments in probes/fixtures

### Acceptance

- [x] Stories 20 / 20.5 / 20.6 text matches the shipped (20, 20.5) or planned (20.6) behavior with hooks
- [x] No remaining “tyhpdef cannot declare hooks” instruction in those three plans
- [x] 20.6 lists 20.7 as a prerequisite

---

## Phase 9: User documentation

Current contract only: what an author writes and what the compiler does with it. No story numbers in user pages.

Pages at minimum:

- `docs/content/tyhpdef_classes.md` — Properties: hooked form, `&get`, visibility, no bodies
- `docs/content/quickref_tyhpdef.md` — one example
- `docs/content/tyhp_1300_newObjectDeclSyntax.md` — optional pointer that **describing** hooks in tyhpdef uses `{ get; set; }` (bodies stay Tyhp-only)
- `docs/content/faq_tyhpdefSyntax.md` if a FAQ is the natural place
- `docs/content/diagnostics_reference.md` — new 80xx codes
- `docs/content/cli_tyhpdefGeneration.md` — Track A/B/C emit hooks; Track C does not auto-gate 8.4

TextMate: add a hooked property to `tyhp-lang/vscode/samples/highlight-audit.tyhpdef` if that sample is the highlighting audit surface (Story 19.5 owns the grammar; this story only adds a sample if highlighting already covers `get;` from Tyhp).

Front-matter `story` on `tyhpdef_classes.md` can stay `02` (class tyhpdef) or note 20.7 only in the implementation plan.

### AIDevGuide

`AIDevGuide/` is the bundle an agent loads to write Tyhp applications, and it is **regenerated** from the prompt in `AIDevGuide/REGEN.md`. A claim corrected only in a section file comes back the next time the bundle is regenerated, so update the prompt as well as the section.

| File | What this story changes |
|---|---|
| `AIDevGuide/guide/20-property-accessors.md` | Hooks can now be *described* in a tyhpdef with a bodyless `{ get; set; }` / `{ &get; }` list; bodies stay Tyhp-only |
| `AIDevGuide/guide/23-tyhpdef.md` | Add hooked properties to what a tyhpdef can declare |
| `AIDevGuide/QUICK_GUIDE.md` | Property-accessor line notes the tyhpdef form |
| `AIDevGuide/REGEN.md` | Prompt items 20 (property accessors) and 23 (what is declarable in tyhpdef) should ask for the tyhpdef hook form |

### Acceptance

- [x] Classes / quickref show `{ get; set; }` and `{ &get; }`
- [x] Docs say hook bodies are illegal in tyhpdef
- [x] New diagnostics are in `diagnostics_reference.md`
- [x] `AIDevGuide/guide/20-property-accessors.md` and `23-tyhpdef.md` cover the tyhpdef hook form, and `REGEN.md` items 20 and 23 would regenerate it

---

## Diagnostic Codes

Register in `MessageCode.cs` and both `.resx` files. Prefer **8000s** for tyhpdef-only parse/bind problems; reuse existing **4000s** when the same rule already applies to Tyhp source.

| Code | Enum | Severity | When |
|------|------|----------|------|
| 8015 | `TyhpdefPropertyHookBodyNotAllowed` | Error | Only if a body can still reach the checker (should be unreachable if Phase 1 grammar holds). Keep allocated so a visitor regression has a code. |
| 8016 | `TyhpdefPhpVersionGateOnPropertyHook` | Error | `#[\Tyhp\Php]` (or equivalent) placed on a **hook** rather than the property / `declare` (Phase 7 lock) |

Reuse, do not duplicate:

| Code | Use on tyhpdef hooked properties |
|------|----------------------------------|
| 4004 / 4006 / 4007 | Existing accessor visibility / invalid type / parameter-on-get |
| `CheckerPropertyHookInvalidModifier` | Illegal hook modifier |
| `CheckerFinalPropertyHookOverridden` | `final get` / `final set` override |
| 4304 | Only if we fold hook-level `#[\Tyhp\Php]` into the existing invalid-target diagnostic **and** the message can name hooks; otherwise 8016 |

Do not allocate splice/`4180` codes here.

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **02** | `HasAccessor` / `PhpPropertyAst.Hooks` already exist; this story fills them from tyhpdef and adds get/set/by-ref flags. |
| **08** | Existing hook checker rules apply; skip 8.4-required `&get` on tyhpdef declarations. |
| **14.5** | PHP/Tyhp `{ get; set; }` and hook attributes; tyhpdef copies that *signature* shape only. |
| **20** | Supersedes unused `GetterHint` / `SetterHint`, Phase 5.3 storage-only properties, and unimplemented “accessor hints” in 6.1 / 6.3. |
| **20.5** | Member gating on hooked properties; multi-target storage vs hooked; no `#[\Tyhp\Php]` on individual hooks. |
| **20.6** | **Depends on this story.** Reads `HasAccessor` + `GetHookReturnsRef` for `4180` and by-value read-local. Do not implement the splice engine here. |
| **21** | Package tyhpdefs may describe 8.4 hooked APIs; language-feature row in the version table is unchanged. |
| **23** | Same symbol flags for attributed inlining; its “blocked on tyhpdef hook syntax” note is this story. |
| **29** | Later `hasAccessor()` / `getAccessorType()` can read these flags; do not build the reflection API here. |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). See `CONVENTIONS.md` and Story 07.

- [ ] **Golden fixtures:** tyhpdef `{ get; set; }`, `{ get; }`, `{ &get; set; }`, `private set`, `final get`, hook attributes; parse rejection of hook bodies and comma+hooks; Track C from a Tyhp hooked class; Track B from PHP `#classPropertyAccessors`; gated `#[\Tyhp\Php(">=8.4")]` hooked property
- [ ] **Unit / integration tests:** grammar, visitor hooks non-null, binder flags (Tyhp + tyhpdef), writer emit + parse, `MapProperty` / reflection DTO / native extract, 8016 if implemented
- [ ] **Conformance run green** before story done
- [ ] **Writer test** that previously forbade `get;` / `set;` now covers both hooked and unhooked
- [ ] **Consumer bind:** a second compilation that only sees generated `package.tyhpdef` gets `HasAccessor` / `GetHookReturnsRef` right
- [ ] **No Story 20.6 splice implementation** in this story’s diff
