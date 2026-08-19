# Implementation Plan: Story 25 — `internal` Visibility Modifier

> **Roadmap position:** Story 25 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** all earlier stories (01–24)
> **Renumbered from:** legacy Story 17
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Tyhp language design — `internal` access boundary
> **Branch:** TBD
> **Generated:** 2026-02-17
> **Prerequisites:** All earlier stories (01–24) must be complete — the parser, binder, checker, emitter, tyhpdef generator, Tyhp runtime packages, LSP, and testing infrastructure must be fully functional.

---

## Architecture Overview

### What `internal` Is

`internal` is a compile-time visibility modifier that restricts access to a symbol to within the **defining project**. A "project" is defined by the presence and scope of a `tyhp.json` configuration file. Everything compiled within a single `tyhp.json` project can see `internal` members; external projects consuming the library via tyhpdef files cannot.

Unlike `public`, `protected`, and `private` — which map directly to PHP visibility keywords — `internal` has **no PHP equivalent**. It is purely a Tyhp compile-time concept. Internal members compile to `public` PHP (for class members) or no modifier (for top-level declarations) because PHP has no module/assembly boundary mechanism.

### The TypeScript Analogy

Tyhp does for PHP what TypeScript does for JavaScript. In the same way that TypeScript's type annotations are erased in the JavaScript output, Tyhp's `internal` modifier is erased in the PHP output. The enforcement happens entirely at compile time, and the tyhpdef generation system serves as the primary boundary mechanism for external consumers.

### How `internal` Is Enforced

There are two complementary enforcement mechanisms:

1. **Tyhpdef exclusion (primary).** When a Tyhp library is compiled (`"type": "library"` in `tyhp.json`) and its public API is exported as a `package.tyhp.json` file (Story 20, Track C), items marked `internal` are **excluded** from the generated `package.tyhp.json`. External projects that depend on the library via `package.tyhp.json` simply cannot see internal items — they don't exist in the type definitions.

2. **Checker validation (secondary).** When project A directly depends on project B's source (e.g., a multi-project workspace), the checker validates that project A does not reference any of project B's `internal` symbols. This catches violations even before tyhpdef generation.

### Position in the Pipeline

```
ALL earlier stories complete
(Stories 01–24)
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│  STORY 25: `internal` Visibility Modifier                    │
│  ◄── THIS PLAN                                               │
│                                                              │
│  Touches: Grammar (Lexer + Parser), Binder, Checker,        │
│           Emitter, Tyhpdef Generator, LSP, Testing           │
│                                                              │
│  Phase 1: Grammar — Add `internal` token and parser rules    │
│  Phase 2: Binder — Track `IsInternal` on symbols             │
│  Phase 3: Checker — Enforce project boundary access rules    │
│  Phase 4: Emitter — Strip `internal`, emit as `public`       │
│  Phase 5: Tyhpdef Generation — Exclude internal items        │
│  Phase 6: LSP Support — Filter autocomplete by boundary      │
│  Phase 7: Testing — Comprehensive test coverage              │
└──────────────────────────────────────────────────────────────┘
```

### Design Principles

1. **`internal` is compile-time only.** It never appears in emitted PHP. It does not affect runtime behavior.

2. **The project boundary is `tyhp.json`.** A single `tyhp.json` file defines the scope of a project. All `.tyhp` files compiled under that project share internal visibility.

3. **Tyhpdef exclusion is the primary enforcement.** External consumers of a library rely on tyhpdef files. If internal items are excluded from tyhpdef, they are invisible to the external type system. Since `internal` is not a valid keyword in tyhpdef files, items that are internal in source never appear in the generated tyhpdef output.

4. **`internal` can combine with non-visibility modifiers.** A member can be `internal static`, `internal readonly`, etc. The `internal` modifier is orthogonal to `static`, `abstract`, `final`, and `readonly`, but it cannot be combined with other visibility modifiers (`public`, `protected`, `private`).

5. **`internal` is valid on any item that could appear in tyhpdef.** This includes: classes, interfaces, traits, enums, functions, constants, type aliases, methods, properties, and enum cases.

6. **`internal` is a standalone visibility modifier.** It cannot be combined with any other visibility modifier (`public`, `protected`, `private`). Using `internal` with another visibility modifier is a `CheckerMultipleVisibilities` (4002) error, the same as using `public` and `private` together. For class members, `internal` emits as `public` in PHP. For top-level declarations, `internal` emits as no modifier (PHP default).

### Tyhp Syntax Examples

**Top-level declarations:**

```tyhp
<?tyhp

// Internal class — visible within this project only
internal class InternalHelper {
    public function doWork(): void {
        // ...
    }
}

// Internal function
internal function computeHash(string $data): string {
    return hash('sha256', $data);
}

// Internal constant
internal const int MAX_RETRIES = 3;

// Internal type alias
internal type UserId = int;

// Internal interface
internal interface Cacheable {
    public function getCacheKey(): string;
}

// Internal enum
internal enum LogLevel {
    case Debug;
    case Info;
    case Warning;
    case Error;
}

// Internal trait
internal trait HasTimestamps {
    public DateTime $createdAt;
    public DateTime $updatedAt;
}

// Public class is visible to external consumers
public class PublicService {
    // Internal method — visible within project, hidden from tyhpdef
    internal function resetState(): void {
        // ...
    }

    // Internal property
    internal int $retryCount = 0;

    // Public method — visible to everyone
    public function process(): void {
        $this->resetState();
    }
}
```

**Emitted PHP output (for the public class above):**

```php
<?php

class PublicService {
    // internal is stripped; emitted as public
    public function resetState(): void {
        // ...
    }

    public int $retryCount = 0;

    public function process(): void {
        $this->resetState();
    }
}
```

**Generated tyhpdef (for external consumers):**

```tyhpdef
<?tyhpdef

// InternalHelper is EXCLUDED entirely
// computeHash is EXCLUDED entirely
// MAX_RETRIES is EXCLUDED entirely
// UserId is EXCLUDED entirely
// Cacheable is EXCLUDED entirely
// LogLevel is EXCLUDED entirely
// HasTimestamps is EXCLUDED entirely

class PublicService {
    // resetState() is EXCLUDED — it was internal
    // $retryCount is EXCLUDED — it was internal

    public function process(): void;
}
```

### MessageCode Numbering

This story uses MessageCode values starting at **4330** (4320–4324 are allocated to Story 16).

| Code | Enum Name | Message |
|------|-----------|---------|
| 4330 | `CheckerAccessToInternalMember` | "Cannot access internal member '{0}' from outside the defining project '{1}'" |
| 4331 | `CheckerAccessToInternalType` | "Cannot access internal type '{0}' from outside the defining project '{1}'" |
| 4332 | `CheckerInternalNotAllowedHere` | "The 'internal' modifier is not valid in this context" |
| 4333 | `CheckerInternalMemberExposedViaPublicApi` | "Internal type '{0}' is exposed via public API member '{1}'. The return/parameter type must be the same visibility or more visible than the member itself." |
| 4334 | `CheckerInternalInTraitAlias` | "The 'internal' modifier cannot be used in a trait alias visibility declaration. Use 'public', 'protected', or 'private' instead." |

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup using the canonical naming: `<filename>.bak.<YYYYMMDD_HHMMSS>`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Grammar — Add `internal` as a Modifier Token




### Phase Overview

Add `internal` as a recognized keyword in the Tyhp lexer and parser. It must be usable in the same positions as other modifiers: as a class/function/declaration modifier at the top level, and as a member modifier within class/interface/trait/enum bodies. The keyword is only recognized in `tyhp` language mode (not raw PHP mode or tyhpdef mode).

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` — Add `T_TYHP_INTERNAL` token
- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — Add `internal` to modifier rules via grammar addon overrides
- `Tyhp/TyhpLang/Grammar/PhpParser.g4` — No changes (internal is Tyhp-only; the base PHP grammar is untouched)

**Regenerated files:**
- `Tyhp/TyhpLang/Parser/` — Regenerated ANTLR4 C# parser/lexer classes (via `compile_grammar.sh`)
- `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.interp`, `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.tokens` — Regenerated ANTLR4 metadata files

### Implementation Details

#### 1.1 Lexer: Add `T_TYHP_INTERNAL` Token

**File: `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`**

Add `T_TYHP_INTERNAL` to the `tokens` block alongside the existing Tyhp-specific tokens:

```antlr
tokens {
    // ... existing tokens ...
    T_TYHP_INTERNAL,
    // ...
}
```

Add the lexer rule in the `ST_IN_SCRIPTING` mode, following the same pattern as other Tyhp keywords (`T_TYHP_ASYNC`, `T_TYHP_OPERATOR`, etc.):

```antlr
T_TYHP_INTERNAL options{caseInsensitive=true;}:
                            'internal' {(this._languageMode == "tyhp")}? -> type(T_TYHP_INTERNAL);
```

This rule:
- Is case-insensitive (matching Tyhp's convention for keywords)
- Only activates in `tyhp` language mode (not raw PHP or tyhpdef)
- Uses the same semantic predicate pattern as existing Tyhp keywords

#### 1.2 Parser: Add `internal` to Member Modifier Rule

**File: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`**

Override the `memberModifierGrammarAddon` rule to include `T_TYHP_INTERNAL` alongside the existing `T_TYHP_ASYNC`:

```antlr
// ! OVERRIDE
memberModifierGrammarAddon
    : TokenValue=T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
    | TokenValue=T_TYHP_INTERNAL {this.isLanguageMode("tyhp")}?
    ;
```

This makes `internal` valid in the `memberModifier` rule (defined in `PhpParser.g4`), which is used by:
- `nonEmptyMemberModifiers` → property declarations, method declarations, constant declarations within classes/interfaces/traits/enums
- `traitAliasVisibility` → trait alias visibility changes

#### 1.3 Parser: Add `internal` to Class/Function/Declaration Modifiers

**File: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`**

Add `internal` as a valid modifier for top-level declarations. Since `classModifier` in `PhpParser.g4` only supports `abstract` and `final`, we need a grammar addon override for class declarations that allows `internal`:

Override `classDeclarationStatementGrammarAddon` to allow `internal` as a class modifier:

```antlr
// ! OVERRIDE
classDeclarationStatementGrammarAddon
    : T_TYHP_INTERNAL Modifiers=classModifiers? ObjectType=T_CLASS Identifier=T_STRING
        classNameGrammarAddon Extends=extendsFrom
        Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=classStatementList T_CLOSE_CURLY_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalClassDeclaration
    ;
```

Similarly, add grammar addon overrides for functions at the top level. For function declarations, update `functionModifiersGrammarAddon`:

```antlr
// ! OVERRIDE
functionModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? IsAsync=T_TYHP_ASYNC? {this.isLanguageMode("tyhp")}?
    ;
```

Similarly, override the modifier grammar addons for traits, interfaces, and enums to accept `T_TYHP_INTERNAL`:

**Trait declarations:**

```antlr
// ! OVERRIDE
traitModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? {this.isLanguageMode("tyhp")}?
    ;
```

**Interface declarations:**

```antlr
// ! OVERRIDE
interfaceModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? {this.isLanguageMode("tyhp")}?
    ;
```

**Enum declarations:**

```antlr
// ! OVERRIDE
enumModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? {this.isLanguageMode("tyhp")}?
    ;
```

For top-level constants and type aliases, update `topStatementGrammarAddon` to handle `internal` prefixed declarations. Since top-level constants and type aliases are already handled via `topStatementGrammarAddon`, add `T_TYHP_INTERNAL` as an optional prefix to the existing alternatives.

**Extension declarations:** Extension class declarations can be `internal` (making the entire extension internal). Extension classes do not allow modifiers such as `abstract`, `final`, or `readonly`. Update the extension declaration grammar addon to accept an optional `T_TYHP_INTERNAL` prefix.

**The full list of contexts where `internal` is valid:**
- Class declarations (in addition to abstract/final/readonly)
- Interface declarations
- Trait declarations
- Enum declarations
- Function declarations
- Constant declarations (class level and namespace level)
- Type alias declarations (class level and namespace level)
- Method declarations (as a visibility modifier, cannot be used with other visibility modifiers)
- Property declarations (as a visibility modifier, cannot be used with other visibility modifiers)
- Enum case declarations
- Extension method and operator overload declarations
- Class operator overload declarations
- Extension class declarations (the entire extension class is internal)

#### 1.4 Regenerate Parser

After modifying the grammar files:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone.

### Acceptance Criteria

- [ ] `internal` is recognized as a keyword in Tyhp mode: `<?tyhp internal class Foo {}`
- [ ] `internal` is NOT recognized as a keyword in Tyhpdef mode: `<?tyhpdef internal class Foo {}` → parsed as identifier
- [ ] `internal` is NOT recognized in raw PHP mode: `<?php internal class Foo {}` → parsed as identifier
- [ ] `internal` works as a member modifier: `internal function foo(): void;`
- [ ] `internal` works as a class modifier: `internal class Foo {}`
- [ ] `internal` works as a function modifier: `internal function bar(): void {}`
- [ ] `internal` can combine with other modifiers: `internal static function baz(): void {}`
- [ ] `internal abstract class Foo {}` is syntactically valid
- [ ] ANTLR4 generates clean parser/lexer C# code without errors
- [ ] All existing tests continue to pass (no grammar regressions)

### Dependencies

- **Requires:** All earlier stories complete (01–24 — grammar/parser infrastructure functional)
- **Provides for:** Phase 2 (binder needs to read the `internal` modifier from AST nodes)

---

## Phase 2: Binder — Track `IsInternal` on Symbols




### Phase Overview

Update the binder to recognize the `internal` modifier on AST nodes and propagate it to the symbol system. Every symbol that can carry visibility (`ObjectDeclarationSymbol`, `FunctionDeclarationSymbol`, `ConstantSymbol`, `ObjectMethodSymbol`, `ObjectPropertySymbol`, `ObjectConstantSymbol`, `TypeAliasSymbol`, etc.) must track whether it was declared `internal`.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Binder/Symbols/BaseSymbol.cs` — Add `IsInternal` property
- `Tyhp/TyhpLang/Binder/Symbols/ObjectDeclarationSymbol.cs` — Propagate `IsInternal` from AST
- `Tyhp/TyhpLang/Binder/Symbols/FunctionDeclarationSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ConstantSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectMethodSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectPropertySymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectConstantSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/TypeAliasSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectAccessorMethodSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectOperatorOverloadMethodSymbol.cs` — Propagate `IsInternal`
- Extension-related symbol classes — Propagate `IsInternal` for extension declarations marked internal
- Enum case symbols — Propagate `IsInternal` for internal enum case declarations
- `Tyhp/TyhpLang/Binder/SymbolTree.cs` — Store project identity (from `tyhp.json` path) on the symbol tree
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.cs` — Extract `internal` modifier from parsed nodes

### Implementation Details

#### 2.1 Add `IsInternal` to `BaseSymbol`

**File: `Tyhp/TyhpLang/Binder/Symbols/BaseSymbol.cs`**

Add a boolean property to the base symbol class:

```csharp
public class BaseSymbol : Interfaces.IBaseSymbol
{
    /// <summary>
    /// Whether this symbol was declared with the `internal` modifier.
    /// Internal symbols are only visible within the defining project.
    /// </summary>
    public bool IsInternal { get; set; } = false;

    /// <summary>
    /// The project identity (tyhp.json path) that defines this symbol's visibility boundary.
    /// Null for symbols from external tyhpdef files (which by definition are public).
    /// </summary>
    public string? DefiningProjectPath { get; set; } = null;
}
```

By placing `IsInternal` on `BaseSymbol`, all symbol subclasses inherit it automatically. The `DefiningProjectPath` allows the checker to compare whether two symbols belong to the same project.

#### 2.2 Add `IInternalizable` Interface

`IInternalizable` is a required interface that provides a clear type-safe contract for which AST nodes support the `internal` modifier:

```csharp
namespace Tyhp.TyhpLang.Binder.Symbols.Interfaces
{
    public interface IInternalizable
    {
        bool IsInternal { get; set; }
        string? DefiningProjectPath { get; set; }
    }
}
```

Since `IsInternal` is on `BaseSymbol`, every symbol already has the property. The interface provides explicit documentation of intent and enables type-safe checks in the checker and emitter.

#### 2.3 Store Project Identity on SymbolTree

**File: `Tyhp/TyhpLang/Binder/SymbolTree.cs`**

Add a `ProjectPath` property to the `SymbolTree` class that records the `tyhp.json` file path for the current compilation:

```csharp
/// <summary>
/// The absolute path to the tyhp.json file that defines this project.
/// Used to determine project boundaries for `internal` visibility checks.
/// </summary>
public string? ProjectPath { get; set; }
```

When the binder initializes, it sets `ProjectPath` from `Project.GetProjectPath()`. Every symbol created during binding inherits this project path via `DefiningProjectPath`.

#### 2.4 Visitor: Extract `internal` from AST Nodes

**Files: `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.cs`, `.Tyhpdef.cs`**

When visiting member modifier nodes, check for `T_TYHP_INTERNAL` in the modifier list and set the `IsInternal` flag on the corresponding AST node. The modifier is represented by the `TokenValueGrammarAddon` alternative in the `memberModifier` rule (via `memberModifierGrammarAddon`).

The visitor must also handle the top-level declaration variants where `internal` appears as a prefix (from Phase 1's grammar changes).

#### 2.5 Binder: Propagate `internal` to Symbols

When the binder creates symbols from AST nodes, check the AST's modifier flags and set `IsInternal = true` on the symbol if the `internal` modifier is present. Also set `DefiningProjectPath` to the current compilation's project path.

For symbols loaded from external `.tyhpdef` files, `IsInternal` defaults to `false` and `DefiningProjectPath` is set to the path of the project that generated the tyhpdef. Since internal items are excluded from tyhpdef generation (Phase 5), this case should not arise in practice — but the binder should handle it gracefully if it does.

### Acceptance Criteria

- [ ] `BaseSymbol.IsInternal` property exists and defaults to `false`
- [ ] `BaseSymbol.DefiningProjectPath` property exists and defaults to `null`
- [ ] `SymbolTree.ProjectPath` is set from `tyhp.json` during binder initialization
- [ ] When parsing `internal class Foo {}`, the `ObjectDeclarationSymbol` for `Foo` has `IsInternal = true`
- [ ] When parsing `internal function bar(): void {}`, the `FunctionDeclarationSymbol` for `bar` has `IsInternal = true`
- [ ] When parsing `public class Baz { internal function qux(): void {} }`, the `ObjectMethodSymbol` for `qux` has `IsInternal = true`
- [ ] Symbols loaded from `.tyhpdef` files have `IsInternal = false`
- [ ] All symbols created during binding have `DefiningProjectPath` set to the current project path
- [ ] No binder regressions — existing tests pass

### Dependencies

- **Requires:** Phase 1 (grammar must recognize `internal` so the binder can read it from AST)
- **Provides for:** Phase 3 (checker needs `IsInternal` and `DefiningProjectPath` to validate access)

---

## Phase 3: Checker — Enforce Project Boundary Access Rules




### Phase Overview

Implement checker validations that prevent `internal` members from being accessed outside their defining project. The checker compares the `DefiningProjectPath` of the referenced symbol against the current compilation's project path. If they differ, and the symbol is `internal`, the checker reports an error.

Additionally, validate modifier combinations: `internal` cannot combine with any other visibility modifier (`public`, `protected`, `private`), and `internal` is only valid in contexts where visibility makes sense.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Checker/TyhpChecker.cs` — Add `internal` access checks and modifier validation
- `Tyhp/Domain/Exceptions/MessageCode.cs` — Add new error codes (4330–4334)
- `Resources/CLI.TyhpHostedService.en-US.resx` — Add localized error messages

### Implementation Details

#### 3.1 Add MessageCode Values

**File: `Tyhp/Domain/Exceptions/MessageCode.cs`**

Add the following values to the `Checker` region:

```csharp
#region Checker

// ... existing codes 4001–4007 ...

// Internal visibility errors (4330–4334) — Story 25
CheckerAccessToInternalMember = 4330,
CheckerAccessToInternalType = 4331,
CheckerInternalNotAllowedHere = 4332,
CheckerInternalMemberExposedViaPublicApi = 4333,
CheckerInternalInTraitAlias = 4334,

#endregion Checker
```

#### 3.2 Add Localized Error Messages

**File: `Resources/CLI.TyhpHostedService.en-US.resx`**

Add the following entries:

| Key | Value |
|-----|-------|
| `ERROR_TYHP4330` | `Cannot access internal member '{0}' from outside the defining project '{1}'.` |
| `ERROR_TYHP4331` | `Cannot access internal type '{0}' from outside the defining project '{1}'.` |
| `ERROR_TYHP4332` | `The 'internal' modifier is not valid in this context.` |
| `ERROR_TYHP4333` | `Internal type '{0}' is exposed via public API member '{1}'. The return/parameter type must be the same visibility or more visible than the member itself.` |
| `ERROR_TYHP4334` | `The 'internal' modifier cannot be used in a trait alias visibility declaration. Use 'public', 'protected', or 'private' instead.` |

#### 3.3 Checker: Validate Modifier Combinations

**File: `Tyhp/TyhpLang/Checker/TyhpChecker.cs`**

Add a check in the modifier validation logic (near existing `CheckerMultipleVisibilities` / `CheckerMemberModifierConflict` checks):

```
When processing member modifiers:
1. If `internal` is present AND any other visibility modifier (`public`, `protected`, `private`) is present
   → report CheckerMultipleVisibilities (4002, existing code). `internal` is a standalone visibility
   modifier and cannot be combined with other visibility modifiers.
2. If `internal` is present in a context where visibility is not applicable
   (e.g., local variables, loop constructs) → report CheckerInternalNotAllowedHere (4332)
```

`internal` is a standalone visibility modifier. It cannot be combined with `public`, `protected`, or `private`. When `internal` is the only visibility modifier, the PHP output uses `public` for class members and no modifier for top-level declarations (PHP classes/functions are public by default).

#### 3.4 Checker: Validate Access to Internal Symbols

Add a method `CheckInternalAccess(BaseSymbol referencedSymbol, string currentProjectPath)` called whenever the checker resolves a symbol reference (type reference, function call, property access, constant reference, etc.):

```
CheckInternalAccess(referencedSymbol, currentProjectPath):
    if (!referencedSymbol.IsInternal) return;  // not internal, always accessible

    if (referencedSymbol.DefiningProjectPath == null) return;  // from tyhpdef, already filtered

    if (referencedSymbol.DefiningProjectPath == currentProjectPath) return;  // same project, OK

    // Different project, internal symbol → ERROR
    if (referencedSymbol is type declaration):
        report CheckerAccessToInternalType (4331) with symbol name and project name
    else:
        report CheckerAccessToInternalMember (4330) with symbol name and project name
```

This check must be integrated at every point where the checker resolves a reference:

- **Type references:** class names in `extends`, `implements`, type hints, `new` expressions, `instanceof`/`is` checks, generic arguments, return types, parameter types
- **Function calls:** free function calls, static method calls
- **Property access:** `$obj->property`, `Class::$staticProp`
- **Constant access:** `Class::CONSTANT`, global constants
- **Method calls:** `$obj->method()`, `Class::staticMethod()`
- **Trait use:** `use TraitName;`
- **Type alias references:** references to type aliases declared as `internal`

#### 3.5 Checker: Error on Internal Type Exposure in Public API

When a `public` method, property, or function has a parameter type, return type, or property type that references an internal type that cannot be resolved to a public type (i.e., an internal class, interface, trait, or enum — NOT a type alias), report an error:

```
CheckInternalTypeExposure(memberSymbol):
    if (memberSymbol.IsInternal) return;  // internal member can freely use internal types
    if (memberSymbol is not public) return;  // non-public members don't expose API

    for each type reference in memberSymbol's signature (params, return, property type):
        resolvedType = resolveTypeAliases(referencedType)  // recursively resolve internal aliases
        if (resolvedType.IsInternal && resolvedType is not TypeAlias && resolvedType.DefiningProjectPath == currentProjectPath):
            report ERROR CheckerInternalMemberExposedViaPublicApi (4333)
            message: "Internal type '{resolvedType.Name}' is exposed via public API member '{memberSymbol.Name}'. The return/parameter type must be the same visibility or more visible than the member itself."
```

Note: Internal type ALIASES are resolved by the tyhpdef generator (Phase 5, section 5.3). Only unresolvable internal types (classes, interfaces, traits, enums) produce this error.

#### 3.6 Interaction with Trait Alias Visibility

`internal` cannot appear in trait alias visibility declarations (e.g., `use TraitName { method as internal; }` is a compile error). This is because `internal` is a compile-time-only concept, while trait alias visibility changes (`as public`, `as protected`, `as private`) are runtime PHP constructs. The checker must produce a diagnostic error when `internal` is used in a trait `as` clause.

**Diagnostic code:** `CheckerInternalInTraitAlias = 4334` — "The 'internal' modifier cannot be used in a trait alias visibility declaration. Use 'public', 'protected', or 'private' instead."

### Acceptance Criteria

- [ ] `CheckerAccessToInternalMember` (4330) is reported when accessing an internal method/property/constant from outside the project
- [ ] `CheckerAccessToInternalType` (4331) is reported when referencing an internal class/interface/trait/enum/type-alias from outside the project
- [ ] `CheckerMultipleVisibilities` (4002) is reported for `internal public`, `internal private`, `internal protected` declarations
- [ ] `CheckerInternalNotAllowedHere` (4332) is reported for `internal` in invalid contexts
- [ ] `CheckerInternalMemberExposedViaPublicApi` (4333) error is reported when a public method returns/accepts an unresolvable internal type (class, interface, trait, enum)
- [ ] `internal` members are freely accessible within the same project (no errors)
- [ ] `internal static`, `internal readonly`, `internal abstract` are accepted without errors
- [ ] All existing checker tests pass (no regressions)
- [ ] Error messages include the symbol name and project name for debugging clarity

### Dependencies

- **Requires:** Phase 2 (binder must set `IsInternal` and `DefiningProjectPath` on symbols)
- **Provides for:** Phase 4 (emitter needs to know about `internal` to strip it), Phase 5 (tyhpdef generator needs checker validation complete)

---

## Phase 4: Emitter — Strip `internal`, Emit as `public`




### Phase Overview

The emitter must handle the `internal` modifier by **stripping it** from the output. Since PHP has no `internal` keyword, internal members are emitted with their effective PHP visibility:

- `internal` alone → emitted as `public` (for class members) or nothing (for top-level declarations, since PHP classes/functions are public by default)
- `internal static` → emitted as `public static`
- `internal abstract` → emitted as `abstract` (with default public visibility)
- `internal final` → emitted as `final`
- `internal readonly` → emitted as `public readonly`

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Update modifier emission logic to strip `internal`

### Implementation Details

#### 4.1 Modifier Emission Logic

**File: `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs`**

In the emitter's modifier processing logic, add handling for the `internal` modifier:

```
When emitting modifiers for a declaration:
1. Collect all modifiers from the AST/symbol
2. Remove `internal` from the modifier list
3. If no explicit PHP visibility remains (i.e., `internal` was the only visibility):
   a. For class members: emit `public` (default visibility for internal members)
   b. For top-level declarations: emit nothing (PHP classes/functions are public by default)
4. Emit remaining modifiers in standard PHP order: visibility, static, abstract/final, readonly
```

**Examples of modifier transformation:**

| Tyhp Modifiers | Emitted PHP Modifiers |
|---|---|
| `internal` | `public` (for members) / nothing (for top-level) |
| `internal static` | `public static` |
| `internal readonly` | `public readonly` |
| `internal abstract` | `abstract` |
| `internal final` | `final` |
| `internal static readonly` | `public static readonly` |

#### 4.2 Class Declaration Emission

For top-level declarations with `internal`:

```php
// Tyhp input:
internal class Helper { }

// PHP output:
class Helper { }
// (no modifier — PHP classes are implicitly public)
```

```php
// Tyhp input:
internal abstract class BaseHelper { }

// PHP output:
abstract class BaseHelper { }
```

#### 4.3 No Changes to Method/Property Bodies

The `internal` modifier only affects the declaration line. Method bodies, property initializers, and all other code within internal declarations are emitted identically to their public counterparts.

### Acceptance Criteria

- [ ] `internal class Foo {}` emits as `class Foo {}`
- [ ] `internal function bar(): void {}` emits as `function bar(): void {}`
- [ ] `internal abstract class Baz {}` emits as `abstract class Baz {}`
- [ ] `internal static function qux(): void {}` emits as `function qux(): void {}` (top-level) or `public static function qux(): void {}` (class member)
- [ ] `internal readonly int $x = 0;` emits as `public readonly int $x = 0;`
- [ ] `internal const int C = 1;` emits as `const C = 1;` (top-level) or `public const C = 1;` (class member)
- [ ] No `internal` keyword ever appears in emitted PHP
- [ ] All emitter tests pass (no regressions)

### Dependencies

- **Requires:** Phase 1 (grammar), Phase 2 (binder knows about `internal`)
- **Provides for:** Phase 5 (emitter must be working before tyhpdef generation can rely on the symbol system)

---

## Phase 5: Tyhpdef Generation — Exclude Internal Items




### Phase Overview

Update the tyhpdef generator (Story 20, Track C) to **exclude** all items marked `internal` when generating `package.tyhp.json` for a compiled library. This is the primary enforcement mechanism: external projects consuming the library via `package.tyhp.json` simply cannot see internal items because they are not present in the type definitions.

### Deliverables

**Modified files:**
- `Tyhp/Domain/Services/TyhpCodeTyhpdefGenerator.cs` — **Primary integration point.** This is Story 20's Track C generator that walks the bound/checked symbol tree and emits `package.tyhp.json` (plus `_tyhpdef/` contents). Add `IsInternal` filtering here so internal symbols are never written.
- `Tyhp/CLI/GenerateTyhpdefAction.cs` — Apply the same `IsInternal` filtering for any direct generation paths it drives.

> **Dependency note:** This phase depends directly on Story 20's tyhpdef-writer internals — specifically the symbol-tree-walk and serialization logic in `TyhpCodeTyhpdefGenerator` (Story 20 Phase 6, Track C). Story 20 must be complete, and the exclusion filter must hook into Story 20's actual member/declaration emission loop (do not duplicate the walk). If Story 20's class names differ at implementation time, attach the filter to whatever class owns the Track C `package.tyhp.json` emission.

### Implementation Details

#### 5.1 Filter Internal Symbols During Tyhpdef Output

When the tyhpdef generator walks the symbol tree to produce `.tyhpdef` output, add a filter at each level:

```
GenerateTyhpdef(SymbolTree tree):
    for each top-level symbol in tree:
        if (symbol.IsInternal) → SKIP (do not write to tyhpdef)

        if (symbol is class/interface/trait/enum):
            write class/interface/trait/enum header
            for each member in symbol:
                if (member.IsInternal) → SKIP
                write member declaration
            write closing brace

        if (symbol is function):
            write function declaration

        if (symbol is constant):
            write constant declaration

        if (symbol is type alias):
            write type alias declaration
```

#### 5.2 Handle Partial Internal Classes

A class may have a mix of public and internal members. In this case, the class itself is included in the tyhpdef, but only its non-internal members are written:

```tyhp
// Source:
public class UserService {
    public function getUser(int $id): User { }
    internal function invalidateCache(): void { }
    public int $timeout = 30;
    internal int $retryCount = 0;
}

// Generated tyhpdef:
class UserService {
    public function getUser(int $id): User;
    public int $timeout;
}
```

#### 5.3 Handle Internal Types in Public Signatures

If a public method's signature references an internal type, the tyhpdef generator must resolve the type to a publicly visible equivalent:

- **Internal type aliases:** Resolve the alias to its underlying type. If the underlying type is also an internal alias, continue resolving recursively until all types in the signature are publicly visible within the tyhpdef context. For example:
  - `internal type UserId = int;` → resolve to `int`
  - `internal type UserMap = array<int, User>;` (where `User` is public) → resolve to `array<int, User>`
  - `internal type UserIds = UserId[];` (where `UserId` is `internal type UserId = int;`) → resolve to `int[]`

- **Internal classes/interfaces/traits/enums in public signatures:** If after full type alias resolution, any type in a public member's signature is still an internal class, interface, trait, or enum (i.e., an internal type that is NOT a type alias and cannot be resolved further), the checker reports an error: `CheckerInternalMemberExposedViaPublicApi` (4333) — "Internal type '{0}' is exposed via public API member '{1}'. The return/parameter type must be the same visibility or more visible than the member itself."

  This is a **compile error** (not a warning). The developer must either:
  1. Make the referenced type public, or
  2. Make the method/property internal, or
  3. Change the signature to use a public type

  The tyhpdef generator does NOT attempt to replace internal class types with `mixed` — this would create invisible issues at the consumer side.

#### 5.4 Exclude Entire Internal Namespaces

If all declarations within a namespace are internal, the namespace block itself is excluded from the tyhpdef output. If only some declarations are internal, the namespace is included with only the non-internal declarations.

### Acceptance Criteria

- [ ] Internal classes are not present in generated tyhpdef files
- [ ] Internal functions are not present in generated tyhpdef files
- [ ] Internal constants are not present in generated tyhpdef files
- [ ] Internal type aliases are not present in generated tyhpdef files
- [ ] Internal interfaces, traits, and enums are not present in generated tyhpdef files
- [ ] Internal methods within public classes are excluded from tyhpdef
- [ ] Internal properties within public classes are excluded from tyhpdef
- [ ] Internal constants within public classes are excluded from tyhpdef
- [ ] Internal type aliases in public signatures are resolved to their underlying public types in tyhpdef
- [ ] Public methods referencing unresolvable internal types (classes, interfaces, etc.) produce a checker error (4333)
- [ ] Non-internal members of public classes are correctly preserved in tyhpdef
- [ ] Namespace blocks containing only internal items are excluded entirely
- [ ] Generated tyhpdef files parse correctly with the tyhpdef parser
- [ ] Round-trip test: compile library → generate tyhpdef → parse tyhpdef → no errors

### Dependencies

- **Requires:** Phase 2 (binder `IsInternal` flags), Story 20 (tyhpdef generator infrastructure)
- **Provides for:** Phase 6 (LSP needs tyhpdef-aware filtering), Phase 7 (testing needs tyhpdef generation)

---

## Phase 6: LSP Support — Filter by Project Boundary




### Phase Overview

Update the Language Server (Story 19) to respect `internal` visibility in autocomplete suggestions, hover information, go-to-definition, and diagnostics. Internal members should be visible when editing files within the defining project and hidden when editing files in a dependent project.

### Deliverables

> **Conditional enhancement — builds on Story 19.** These items layer onto Story 19's existing handlers (under `Tyhp/LanguageServer/Handlers/`) and may be deferred without blocking the core `internal` feature, since tyhpdef exclusion (Phase 5) is the primary external-boundary enforcement. When implemented, modify these Story 19 files:

**Modified files:**
- `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/CompletionHandler.cs` — Filter internal symbols based on project boundary (`IsInternal` + `DefiningProjectPath`)
- `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/HoverHandler.cs` — Show `internal` modifier in hover tooltips
- `Tyhp/LanguageServer/Handlers/DiagnosticsPublisher.cs` — Surface checker errors (4330–4334) for internal access violations in real-time
- `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/CodeActionHandler.cs` — Quick-fix to make an internal symbol public
- Workspace symbol search handler (Story 19) — Exclude internal symbols from workspace symbol results for external projects

### Implementation Details

#### 6.1 Autocomplete Filtering

When providing completions for a file in project A that depends on project B:

```
GetCompletions(position, currentProjectPath):
    candidates = getAllCandidateSymbols(position)

    for each candidate in candidates:
        if (candidate.IsInternal &&
            candidate.DefiningProjectPath != currentProjectPath):
            remove candidate from results

    return filtered candidates
```

This means:
- When editing a file in the same project, internal members appear in autocomplete
- When editing a file in a dependent project, internal members do NOT appear in autocomplete
- Internal members from the same project should be visually distinguished (e.g., with a modifier badge or different icon)

#### 6.2 Hover Information

When hovering over an `internal` symbol, include the modifier in the display:

```
internal class InternalHelper
Defined in: MyLibrary (project)
```

#### 6.3 Go-to-Definition

Go-to-definition should work for internal symbols within the same project. For symbols loaded from tyhpdef (which would have internal items excluded), go-to-definition naturally won't find them because they don't exist in the tyhpdef.

#### 6.4 Real-time Diagnostics

The LSP should surface the checker errors (4330, 4331, 4332, 4333, 4334) in real-time as the user types, following the same diagnostic reporting mechanism used for other checker errors.

#### 6.5 Code Actions

Provide a quick-fix code action when the user references an internal symbol from outside the project:

- **"Make '{symbolName}' public"** — If the user controls the source of the symbol, offer to remove the `internal` modifier
- **"Change visibility to public"** — Alternative wording

### Acceptance Criteria

- [ ] Autocomplete hides internal symbols from external projects
- [ ] Autocomplete shows internal symbols within the same project
- [ ] Hover tooltip displays `internal` modifier
- [ ] Go-to-definition works for internal symbols within the same project
- [ ] Checker diagnostics (4330–4334) appear in real-time in the editor
- [ ] Quick-fix code action is available for internal access violations (when applicable)
- [ ] No LSP regressions — existing completion, hover, and diagnostic features work correctly

### Dependencies

- **Requires:** Phase 3 (checker errors defined), Story 19 (LSP infrastructure)
- **Provides for:** Phase 7 (testing includes LSP behavior verification)

---

## Phase 7: Testing — Comprehensive Coverage




### Phase Overview

Create comprehensive tests covering all aspects of the `internal` modifier: grammar parsing, binder symbol tracking, checker validation (positive and negative cases), emitter output, tyhpdef generation exclusion, and LSP behavior.

### Deliverables

**New test files:**
- Grammar tests for `internal` keyword parsing
- Binder tests for `IsInternal` propagation
- Checker tests for access validation and modifier combination validation
- Emitter tests for `internal` stripping
- Tyhpdef generation tests for internal exclusion
- LSP tests for completion filtering
- End-to-end integration tests

**New example files:**
- `Examples/InternalVisibility.tyhp` — Comprehensive example of `internal` usage
- `Examples/InternalVisibility.php` — Expected PHP output

### Implementation Details

#### 7.1 Grammar Tests

Test that the parser correctly handles `internal` in all valid positions:

```
Test cases:
- internal class Foo {}                          → parses with internal modifier
- internal interface Bar {}                      → parses with internal modifier
- internal trait Baz {}                          → parses with internal modifier
- internal enum Qux { case A; }                  → parses with internal modifier
- internal function helper(): void {}            → parses with internal modifier
- internal const int MAX = 10;                   → parses with internal modifier
- internal type UserId = int;                    → parses with internal modifier
- class Foo { internal function bar(): void {} } → parses member with internal
- class Foo { internal int $x = 0; }             → parses property with internal
- class Foo { internal const int C = 1; }        → parses constant with internal
- internal abstract class Foo {}                 → parses combined modifiers
- internal final class Foo {}                    → parses combined modifiers
- internal static function foo(): void {}        → parses combined modifiers
```

Negative test cases (syntax errors):
```
- internal internal class Foo {}                 → duplicate modifier error
- <?php internal class Foo {}                    → not a keyword in PHP mode
```

#### 7.2 Binder Tests

Test that `IsInternal` and `DefiningProjectPath` are correctly set:

```
Test cases:
- internal class → symbol.IsInternal == true
- public class → symbol.IsInternal == false
- class (no modifier) → symbol.IsInternal == false
- internal method → symbol.IsInternal == true
- symbols from tyhpdef → symbol.IsInternal == false
- all symbols → DefiningProjectPath matches current project
```

#### 7.3 Checker Tests — Positive Cases (Access Allowed)

```
Test cases:
- Same project, access internal class → no error
- Same project, access internal method → no error
- Same project, access internal function → no error
- Same project, access internal constant → no error
- Same project, access internal type alias → no error
- Same project, internal static method → no error
- Public method with internal type alias parameter → type alias resolved in tyhpdef, no error
```

#### 7.4 Checker Tests — Negative Cases (Access Denied)

```
Test cases:
- Different project, access internal class → CheckerAccessToInternalType (4331)
- Different project, access internal method → CheckerAccessToInternalMember (4330)
- Different project, access internal function → CheckerAccessToInternalMember (4330)
- Different project, access internal constant → CheckerAccessToInternalMember (4330)
- Different project, access internal type alias → CheckerAccessToInternalType (4331)
- Different project, extend internal class → CheckerAccessToInternalType (4331)
- Different project, implement internal interface → CheckerAccessToInternalType (4331)
- Different project, use internal trait → CheckerAccessToInternalType (4331)
- internal + any other visibility (public, private, protected) → CheckerMultipleVisibilities (4002)
- internal on local variable → CheckerInternalNotAllowedHere (4332)
- Public method with internal class/interface return type → CheckerInternalMemberExposedViaPublicApi (4333) error
```

#### 7.5 Emitter Tests

```
Test cases:
- internal class → emits class (no modifier)
- internal function → emits function (no modifier)
- internal method → emits public method
- internal property → emits public property
- internal static method → emits public static method
- internal abstract class → emits abstract class
- internal readonly property → emits public readonly property
- Verify: the word "internal" never appears in ANY emitted PHP output
```

#### 7.6 Tyhpdef Generation Tests

```
Test cases:
- Compile library with internal class → tyhpdef does not contain the class
- Compile library with internal function → tyhpdef does not contain the function
- Compile library with mixed public/internal class members → tyhpdef contains only public members
- Compile library with internal constant → tyhpdef does not contain the constant
- Compile library with internal type alias → tyhpdef does not contain the alias
- Compile library with all-internal namespace → namespace block excluded from tyhpdef
- Public method with internal type alias parameter → tyhpdef resolves alias to underlying type
- Generated tyhpdef parses without errors
```

#### 7.7 End-to-End Integration Tests

Create a two-project test scenario:

**Project A (Library):**
```tyhp
<?tyhp

namespace MyLib;

public class Calculator {
    public function add(int $a, int $b): int {
        return $this->doAdd($a, $b);
    }

    internal function doAdd(int $a, int $b): int {
        return $a + $b;
    }

    internal int $precision = 2;
}

internal class CalculatorImpl {
    // implementation details
}

internal function helperFn(): void {
    // ...
}
```

**Project A's generated tyhpdef:**
```tyhpdef
<?tyhpdef

namespace MyLib;

class Calculator {
    public function add(int $a, int $b): int;
    // doAdd and $precision are excluded
}
// CalculatorImpl is excluded
// helperFn is excluded
```

**Project B (Consumer) — should compile with errors:**
```tyhp
<?tyhp

use MyLib\Calculator;
use MyLib\CalculatorImpl;  // ERROR: CheckerAccessToInternalType (4331)

$calc = new Calculator();
$calc->add(1, 2);         // OK
$calc->doAdd(1, 2);       // ERROR: CheckerAccessToInternalMember (4330)
$calc->precision;          // ERROR: CheckerAccessToInternalMember (4330)
helperFn();                // ERROR: symbol not found (excluded from tyhpdef)
```

#### 7.8 Example File

**New file: `Examples/InternalVisibility.tyhp`**

```tyhp
<?tyhp

namespace App\Services;

// Public API — visible to external consumers
public class UserService {
    public function getUser(int $id): User {
        $cache = $this->checkCache($id);
        if ($cache !== null) {
            return $cache;
        }
        return $this->fetchFromDb($id);
    }

    public function listUsers(): array {
        return $this->queryAll();
    }

    // Internal methods — implementation details hidden from consumers
    internal function checkCache(int $id): ?User {
        return self::$cache[$id] ?? null;
    }

    internal function fetchFromDb(int $id): User {
        // database logic
    }

    internal function queryAll(): array {
        // query logic
    }

    // Internal state
    internal static array $cache = [];
}

// Internal helper — not visible to consumers
internal class UserQueryBuilder {
    internal function buildQuery(string $table): string {
        return "SELECT * FROM {$table}";
    }
}

// Internal constant
internal const string CACHE_PREFIX = 'user_';

// Internal type alias
internal type UserMap = array<int, User>;
```

**New file: `Examples/InternalVisibility.php`** (expected output)

```php
<?php

namespace App\Services;

class UserService {
    public function getUser(int $id): User {
        $cache = $this->checkCache($id);
        if ($cache !== null) {
            return $cache;
        }
        return $this->fetchFromDb($id);
    }

    public function listUsers(): array {
        return $this->queryAll();
    }

    public function checkCache(int $id): ?User {
        return self::$cache[$id] ?? null;
    }

    public function fetchFromDb(int $id): User {
        // database logic
    }

    public function queryAll(): array {
        // query logic
    }

    public static array $cache = [];
}

class UserQueryBuilder {
    public function buildQuery(string $table): string {
        return "SELECT * FROM {$table}";
    }
}

const CACHE_PREFIX = 'user_';
```

### Acceptance Criteria

- [ ] All grammar parse tests pass
- [ ] All binder symbol tracking tests pass
- [ ] All checker positive cases (access allowed) pass
- [ ] All checker negative cases (access denied, modifier conflicts) pass and produce correct error codes
- [ ] All emitter tests pass — `internal` never appears in output
- [ ] All tyhpdef generation exclusion tests pass
- [ ] End-to-end two-project test demonstrates full internal boundary enforcement
- [ ] `Examples/InternalVisibility.tyhp` compiles without errors
- [ ] `Examples/InternalVisibility.php` matches expected emitted output
- [ ] No regressions in any existing test suites

### Dependencies

- **Requires:** Phases 1–6 (all implementation phases must be complete)
- **Provides:** Complete verification that the `internal` modifier works correctly across the entire compilation pipeline

---

## New MessageCode Values Summary

```csharp
// Internal visibility errors (4330–4334) — Story 25
CheckerAccessToInternalMember = 4330,          // "Cannot access internal member '{0}' from outside the defining project '{1}'."
CheckerAccessToInternalType = 4331,            // "Cannot access internal type '{0}' from outside the defining project '{1}'."
CheckerInternalNotAllowedHere = 4332,          // "The 'internal' modifier is not valid in this context."
CheckerInternalMemberExposedViaPublicApi = 4333, // "Internal type '{0}' is exposed via public API member '{1}'. The return/parameter type must be the same visibility or more visible than the member itself."
CheckerInternalInTraitAlias = 4334,              // "The 'internal' modifier cannot be used in a trait alias visibility declaration. Use 'public', 'protected', or 'private' instead."
```

---

## Cross-Phase Consistency Checklist

After completing all phases, verify these cross-cutting concerns:

1. **Grammar consistency** — The `internal` keyword is recognized in exactly the same positions in Tyhp mode (not Tyhpdef mode)
2. **Binder-to-checker data flow** — Every symbol that can be `internal` has its `IsInternal` and `DefiningProjectPath` correctly set before the checker runs
3. **Checker-to-emitter data flow** — The emitter correctly reads modifier information and strips `internal` regardless of whether the checker has run (defensive coding)
4. **Tyhpdef round-trip** — Generate tyhpdef → parse tyhpdef → symbols have `IsInternal = false` (because internal items were excluded)
5. **Error code consistency** — `MessageCode.cs` enum values, `.resx` entries, and documented error codes (4330–4334, including `CheckerInternalInTraitAlias = 4334`) are all in sync
6. **LSP consistency** — LSP behavior matches checker behavior: if the checker would report an error, the LSP should show a diagnostic; if the checker allows access, the LSP should show the symbol in completions
7. **Example file correctness** — `Examples/InternalVisibility.tyhp` compiles to `Examples/InternalVisibility.php` exactly
8. **No PHP output leakage** — The string `internal` never appears as a keyword in any emitted PHP file

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the `internal` visibility modifier implementation. Steps can be skipped, reordered, or modified as needed. You need: a built `tyhp` binary and PHP 8.4+ installed.

### Step 1: Verify the Build Compiles

If grammar files were modified, regenerate the parser first:

```bash
cd /path/to/tyhp
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Confirm zero errors. The regenerated ANTLR parser files and new checker/emitter logic should compile cleanly.

### Step 2: Set Up Two Test Projects (Library + Consumer)

The `internal` modifier is about project boundaries, so testing requires two separate projects.

**Create the library project:**

```bash
mkdir -p /tmp/tyhp-internal-test/my-lib/src
cd /tmp/tyhp-internal-test/my-lib
```

Create `tyhp.json`:

```json
{
  "type": "library",
  "include": ["src/**/*.tyhp"],
  "output": {
    "path": "build/",
    "phpVersion": "8.4",
    "strictTypes": true
  }
}
```

**Create the consumer project:**

```bash
mkdir -p /tmp/tyhp-internal-test/my-app/src
cd /tmp/tyhp-internal-test/my-app
```

Create `tyhp.json`:

```json
{
  "include": ["src/**/*.tyhp"],
  "output": {
    "path": "build/",
    "phpVersion": "8.4",
    "strictTypes": true
  }
}
```

### Step 3: Test Internal Class — Emitter Output

Create `/tmp/tyhp-internal-test/my-lib/src/Helpers.tyhp`:

```tyhp
<?tyhp

namespace MyLib;

internal class InternalHelper {
    public function compute(int $x): int {
        return $x * 2;
    }
}

public class PublicService {
    internal function resetState(): void {
        // internal method
    }

    public function process(): string {
        $this->resetState();
        return "processed";
    }

    internal int $retryCount = 0;
    public int $timeout = 30;
}

internal function helperFn(): void {
    echo "internal function\n";
}

internal const int MAX_RETRIES = 3;

internal type UserId = int;
```

Compile the library:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Build succeeds. Inspect `build/Helpers.php`:

- `InternalHelper` should be emitted as `class InternalHelper` (no `internal` keyword, no visibility modifier on the class itself).
- `resetState()` should be emitted as `public function resetState()` (internal stripped, emitted as public).
- `$retryCount` should be emitted as `public int $retryCount = 0`.
- `helperFn()` should be emitted as `function helperFn()` (no modifier).
- `MAX_RETRIES` should be emitted as `const MAX_RETRIES = 3`.
- The word `internal` should **never** appear anywhere in the PHP output.

Verify no leakage:

```bash
grep -r "internal" /tmp/tyhp-internal-test/my-lib/build/
```

**Expected:** No matches (or only matches within string literals/comments, NOT as a PHP keyword).

### Step 4: Test Internal Access Within Same Project (Should Succeed)

Create `/tmp/tyhp-internal-test/my-lib/src/SameProjectUsage.tyhp`:

```tyhp
<?tyhp

namespace MyLib;

class InternalConsumer {
    public function useInternal(): void {
        $helper = new InternalHelper();
        $result = $helper->compute(5);
        echo $result . "\n";

        helperFn();
        $max = MAX_RETRIES;

        $svc = new PublicService();
        $svc->resetState();
        $count = $svc->retryCount;
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Build succeeds with zero errors. All internal symbols are accessible within the same project.

### Step 5: Test Tyhpdef Generation — Internal Items Excluded

If the library build produces a `package.tyhp.json` (tyhpdef output for the library), inspect it:

```bash
cat /tmp/tyhp-internal-test/my-lib/package.tyhp.json
```

**Expected:**
- `InternalHelper` class is **not present**.
- `helperFn` function is **not present**.
- `MAX_RETRIES` constant is **not present**.
- `UserId` type alias is **not present**.
- `PublicService` class **is present**, but:
  - `resetState()` method is **not present**.
  - `$retryCount` property is **not present**.
  - `process()` method **is present**.
  - `$timeout` property **is present**.

### Step 6: Test Cross-Project Access Violation (Should Fail)

Create `/tmp/tyhp-internal-test/my-app/src/Consumer.tyhp`:

```tyhp
<?tyhp

namespace App;

use MyLib\PublicService;
use MyLib\InternalHelper;

$svc = new PublicService();
$svc->process();

// These should all produce errors:
$svc->resetState();
$count = $svc->retryCount;

$helper = new InternalHelper();
```

Compile the consumer project (ensure it references the library's tyhpdef):

```bash
cd /tmp/tyhp-internal-test/my-app
dotnet run --project /path/to/tyhp -- build
```

**Expected errors:**
- `InternalHelper` is either not found (excluded from tyhpdef) or produces error 4331 ("Cannot access internal type 'InternalHelper' from outside the defining project").
- `$svc->resetState()` produces error 4330 ("Cannot access internal member 'resetState' from outside the defining project").
- `$svc->retryCount` produces error 4330.

### Step 7: Test Modifier Combination Errors

Create `/tmp/tyhp-internal-test/my-lib/src/ModifierErrors.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Errors;

// ERROR: Cannot combine internal with other visibility modifiers
internal public class BadClass1 {}

class BadClass2 {
    // ERROR: Cannot combine internal with private
    internal private function badMethod(): void {}

    // ERROR: Cannot combine internal with protected
    internal protected int $badProp = 0;
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Error 4002 (`CheckerMultipleVisibilities`) for each `internal + public/private/protected` combination.

### Step 8: Test Valid Modifier Combinations

Create `/tmp/tyhp-internal-test/my-lib/src/ValidModifiers.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Valid;

internal abstract class InternalAbstractBase {
    internal abstract function doWork(): void;
}

internal final class InternalFinalHelper {
    internal static function create(): static {
        return new static();
    }

    internal readonly string $id = "abc";
}

internal interface InternalContract {
    public function execute(): void;
}

internal enum InternalStatus {
    case Active;
    case Inactive;
}

internal trait InternalLogging {
    public function log(string $msg): void {
        echo $msg . "\n";
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Build succeeds with zero errors. `internal` combines freely with `abstract`, `final`, `static`, `readonly`.

Inspect the output PHP:
- `internal abstract class` → `abstract class`
- `internal final class` → `final class`
- `internal static function` → `public static function`
- `internal readonly string` → `public readonly string`
- `internal interface` → `interface`
- `internal enum` → `enum`
- `internal trait` → `trait`

### Step 9: Test Internal Type Exposed via Public API Error

Create `/tmp/tyhp-internal-test/my-lib/src/ExposureError.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Exposure;

internal class InternalData {
    public string $value;
}

// ERROR: public method returns internal type
public class PublicApi {
    public function getData(): InternalData {
        return new InternalData();
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Error 4333 (`CheckerInternalMemberExposedViaPublicApi`) — "Internal type 'InternalData' is exposed via public API member 'getData'."

### Step 10: Test Internal Type Alias Resolution in Tyhpdef

Create `/tmp/tyhp-internal-test/my-lib/src/AliasResolution.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Aliases;

internal type UserId = int;

public class UserService {
    // Uses internal type alias in public signature — should resolve to `int` in tyhpdef
    public function getUser(UserId $id): string {
        return "User {$id}";
    }
}
```

Compile and inspect the generated tyhpdef:

**Expected:** In the tyhpdef output, `getUser` should have its parameter typed as `int` (the alias `UserId` resolved to its underlying type), NOT as `UserId`.

### Step 11: Test Trait Alias Visibility Error

Create `/tmp/tyhp-internal-test/my-lib/src/TraitAliasError.tyhp`:

```tyhp
<?tyhp

namespace MyLib\TraitTest;

trait MyTrait {
    public function hello(): void {}
}

class MyClass {
    use MyTrait {
        hello as internal;
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Error 4334 (`CheckerInternalInTraitAlias`) — "The 'internal' modifier cannot be used in a trait alias visibility declaration."

### Step 12: Test PHP Mode and Tyhpdef Mode Rejection

Verify that `internal` is NOT recognized as a keyword outside Tyhp mode.

Create `/tmp/tyhp-internal-test/my-lib/src/PhpMode.tyhp`:

```tyhp
<?php

// In raw PHP mode, "internal" should be treated as an identifier, not a keyword
$internal = "some value";
echo $internal;
```

Compile:

**Expected:** Compiles without errors. `$internal` is treated as a variable name, not a keyword.

### Step 13: Verify Runtime Behavior

Create `/tmp/tyhp-internal-test/my-lib/src/RuntimeTest.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Runtime;

internal class Calculator {
    public function add(int $a, int $b): int {
        return $a + $b;
    }
}

public class MathService {
    internal Calculator $calc;

    public function __construct() {
        $this->calc = new Calculator();
    }

    public function sum(int $a, int $b): int {
        return $this->calc->add($a, $b);
    }
}

$svc = new MathService();
echo "3 + 4 = " . $svc->sum(3, 4) . "\n";
```

Compile and run:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
php build/RuntimeTest.php
```

**Expected output:**

```
3 + 4 = 7
```

The `internal` modifier is compile-time only — at runtime, everything is public and works normally.

### Step 14: Verify LSP Behavior (if Story 19 is Complete)

Open the library project in VS Code with the Tyhp extension:

1. In `SameProjectUsage.tyhp`, type `new Internal` — autocomplete should suggest `InternalHelper`.
2. Hover over `InternalHelper` — tooltip should show the `internal` modifier.
3. If you could open a file in the consumer project referencing the library, `InternalHelper` should NOT appear in autocomplete.
4. Typing `$svc->` in the consumer project should NOT suggest `resetState()` or `$retryCount`.

### Step 15: Clean Up

```bash
rm -rf /tmp/tyhp-internal-test
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
