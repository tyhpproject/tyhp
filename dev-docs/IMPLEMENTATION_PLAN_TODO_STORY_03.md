# Implementation Plan: Story 03 — Extension Operator Overloads & Tyhpdef Inline Extensions

> **Roadmap position:** Story 03 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 02
> **Renumbered from:** legacy Story 1.2
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Design discussion — operator overloads on tyhpdef types, extension operator `<Type>` syntax, tyhpdef inline extension members
> **Branch:** TBD
> **Generated:** 2026-03-19
> **Prerequisites:** Story 02 (Binder — symbols, scopes, name resolution, tyhpdef loading)
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — grammar/AST/visitor/binder for extension operators and tyhpdef inline extensions landed. Residual: bare `>` operator overload parse gap (see `FOUND_BUGS.md` / `INCOMPLETE.md`).

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Grammar Changes — Extension Operator Overloads with Target Type](#phase-1-grammar-changes--extension-operator-overloads-with-target-type)
- [Phase 2: Grammar Changes — Tyhpdef Inline Extension Members](#phase-2-grammar-changes--tyhpdef-inline-extension-members)
- [Phase 3: Grammar Changes — `use extension` in Tyhpdef Class Bodies](#phase-3-grammar-changes--use-extension-in-tyhpdef-class-bodies)
- [Phase 4: AST and Visitor — Extension Operator Overloads](#phase-4-ast-and-visitor--extension-operator-overloads)
- [Phase 5: AST and Visitor — Tyhpdef Inline Extension Members](#phase-5-ast-and-visitor--tyhpdef-inline-extension-members)
- [Phase 6: Binder — Extension Operator Overloads and Tyhpdef Inline Extensions](#phase-6-binder--extension-operator-overloads-and-tyhpdef-inline-extensions)
- [Phase 7: Update Documentation and Examples](#phase-7-update-documentation-and-examples)

---

## Architecture Overview

### Problem Statement

When creating tyhpdef files for existing PHP classes that should support operator overloads in Tyhp, there is no mechanism to map the PHP class's existing methods to operator behavior. For example, a PHP `Money` class with a `plus()` method should allow Tyhp developers to write `$a + $b`, but the tyhpdef system has no way to express this mapping.

Additionally, Tyhp extensions support operator overloads in the grammar but the visitor and AST do not process them, and tyhpdef files are strictly declaration-only with no way to embed the small amount of mapping code needed.

### Solution Overview

This story introduces three interconnected features:

1. **Extension operator overloads with `<Type>` target syntax** — Operators in extensions use `operator +<TargetType>(...)` to specify which type the operator belongs to, since extensions are type-external and operators are static in nature.

2. **Tyhpdef inline extension members via `extension` qualifier** — Tyhpdef class bodies can contain `extension function` / `extension fn` (short arrow form) and `extension operator` members with code bodies that compile to extension methods/operators for the declared type.

3. **`use extension` inside tyhpdef class bodies** — Tyhpdef classes can reference external extension objects, automatically activating them for any code that uses the type. Supports trait-like adaptations (`as`, `insteadof`) for conflict resolution.

### Design Decisions

**Extension operator `<Type>` syntax:**
- Operators cannot be generic, so the `<Type>` position in `operator +<Type>(...)` is unambiguous.
- The type inside `<>` establishes what `self` resolves to in the operator's parameters, return type, and body.
- A single extension class can hold operators for multiple target types.
- Extension operators compile to static methods on the extension class, following the same pattern as extension methods.
- `abstract` and `final` are not allowed on extension operators. Unlike class-body operator overloads, extensions are not slots in the target type's inheritance or override model.

**Tyhpdef inline `extension` qualifier:**
- `extension` replaces the visibility modifier (`public`/`protected`/`private`) since extension members are always public.
- `abstract` and `final` are not allowed on `extension function` or `extension operator` — same rule as extension members in standalone `extension { }` blocks.
- `extension function` does not use the `extends` keyword on the first parameter; `$this` is implicitly available and refers to the enclosing tyhpdef class.
- `extension operator` does not use `<Type>` since the target is the enclosing tyhpdef class.
- Only `function` / `fn` (extension functions) and `operator` can be qualified with `extension`. Properties, constants, and other members are errors.
- Extension **function** names use `tyhpGenericIdentifierWithoutConstructor` (same as other Tyhp generic methods), not a plain `identifier`.
- **Full** extension functions use `extension function … { … }` (brace `methodBody` only). **Short** extension functions use `extension fn … => expr ;` (arrow body, trailing semicolon), parallel to class-body `fn` short methods.
- **Short** extension operators use `=> expr ;` after the return type (expression plus required semicolon), distinct from class-body operator shorthand (which does not use a trailing `;` on the arrow form).

**Emitted PHP method naming for extension operators:**
- In class-level operator overloads, `This` represents `self` (the declaring class) in generated method names like `__OP_This_ADD_Int`.
- In extension operators, `This` is replaced with the actual target type name: `__OP_Money_ADD_Money`, `__OP_Percentage_ADD_Percentage`.
- This naturally avoids name conflicts when a single extension class holds operators for multiple target types.
- Unified dispatch methods (like `__add`) are NOT generated for extension operators — the compiler resolves directly to the specific static method at compile time.

### Position in the Pipeline

```
Parser/AST (DONE)
    │
    ▼
Story 01: Foundation (DiagnosticBag, CompilationService, BuildAction)
    │
    ▼
Story 02: Binder (Symbols, Scopes, Name Resolution, Tyhpdef Loading)
    │
    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  STORY 03: Extension Operator Overloads & Tyhpdef Inline Exts     │
│  ◄── THIS PLAN                                                     │
│                                                                     │
│  Grammar:   Extension operator <Type> syntax                        │
│             Tyhpdef inline extension members (extension qualifier)  │
│             use extension inside tyhpdef class bodies               │
│                                                                     │
│  AST:       New AST nodes for extension operators and tyhpdef       │
│             inline extensions                                       │
│                                                                     │
│  Visitor:   Dispatch for extension members (functions + operators)   │
│                                                                     │
│  Binder:    Bind extension operators to target type symbols          │
│             Process tyhpdef inline extensions                        │
│             Process use extension in tyhpdef class bodies            │
└─────────────────────────────────────────────────────────────────────┘
    │
    ▼
Story 04: Tyhp Runtime Library Modules
    │
    ▼
Story 06: TyhpSpec (uses tyhpdef inline extensions for decimal operators)
    │
    ▼
Story 08: Checker (validates extension operators and tyhpdef inline exts)
    │
    ▼
Story 09: Emitter (rewrites extension operator usage to static calls)
```

### Key File Locations

| Component | Path | Current State |
|-----------|------|---------------|
| Tyhp grammar (parser) | `Tyhp/TyhpLang/Grammar/TyhpParser.g4` | Has `tyhpExtensionMember` with `tyhpClassOperatorOverload` (not handled by visitor) |
| Tyhp grammar (lexer) | `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` | `T_TYHP_OPERATOR` and `T_TYHP_EXTENSION` tokens exist |
| Extension visitor | `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpExtensions.cs` | Only handles `functionDeclarationStatement`, ignores operators |
| Extension AST | `Tyhp/TyhpLang/Ast/TyhpExtensionFunctionListAst.cs` | Typed as `NodeListAst<PhpFunctionDeclAst>` — cannot hold operators |
| Operator overload AST | `Tyhp/TyhpLang/Ast/TyhpOperatorOverloadAst.cs` | Exists, used by class-level operator overloads |
| Tyhpdef visitor | `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs` | Has `VisitTyhpdefClassOperatorDecl`, no inline extension support |
| Binder (object body) | `Tyhp/TyhpLang/Binder/TyhpBinder.ObjectBody.cs` | `BindOperatorOverload` exists for class-level operators |
| Binder (tyhpdef) | `Tyhp/TyhpLang/Binder/TyhpBinder.Tyhpdef.cs` | `BindTyhpdefObjectBody` handles operator overloads in tyhpdef |
| Extensions doc | `docs/content/tyhp_2100_extensions.json` | Documents `extends` on operator parameter (needs update) |
| Operator overloads example | `Examples/OperatorOverloads.tyhp` | Class-level operator overloads only |

---

## Phase 1: Grammar Changes — Extension Operator Overloads with Target Type




### Phase Overview

Update the Tyhp grammar to support operator overloads inside extensions using the `<Type>` syntax to specify the target type. Replace the current `tyhpClassOperatorOverload` reference in `tyhpExtensionMember` with a new extension-specific operator overload rule.

### Deliverables

- New grammar rule `tyhpExtensionOperatorOverload` with `<Type>` target specification
- Updated `tyhpExtensionMember` to reference the new rule
- Grammar compiles and generates parser without errors

### Implementation Details

#### 1.1 Create the `tyhpExtensionOperatorOverload` Rule

**File:** `Tyhp/TyhpLang/Grammar/TyhpParser.g4`

Replace the current `tyhpExtensionMember` rules:

**Current grammar:**
```
tyhpExtensionMember
    : functionDeclarationStatement
    | tyhpClassOperatorOverload
    ;
```

**New grammar:**
```
tyhpExtensionMember
    : functionDeclarationStatement
    | tyhpExtensionOperatorOverload
    ;

tyhpExtensionOperatorOverload
    : T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp
        T_LESS_THAN TargetType=typeHint T_GREATER_THAN
        T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr))
    ;
```

The key differences from `tyhpClassOperatorOverload` are: (1) the addition of `T_LESS_THAN TargetType=typeHint T_GREATER_THAN` between the operator token and the parameter list — the `typeHint` rule names the target type (simple names, qualified names, namespace paths); and (2) no `abstract` or `final` modifier (class-body operators keep `Modifier=(T_ABSTRACT | T_FINAL)?`; extension operators do not).

#### 1.2 Verify Parser Generation

After modifying the grammar:
- Regenerate the ANTLR parser by running `./compile_grammar.sh`
- Verify no grammar conflicts arise from the `<Type>` syntax (the `T_LESS_THAN` after `tyhpClassOperatorOverloadOp` is unambiguous because operator tokens cannot be followed by `<` in any other context)
- Verify that existing `tyhpClassOperatorOverload` (used in class bodies) remains unchanged

### Acceptance Criteria

- [x] `tyhpExtensionOperatorOverload` rule exists in `TyhpParser.g4` with `TargetType` field and without `abstract`/`final`
- [x] `tyhpExtensionMember` references `tyhpExtensionOperatorOverload` (not `tyhpClassOperatorOverload`)
- [x] The grammar compiles and generates a valid parser without conflicts
- [x] Existing class-level operator overload grammar (`tyhpClassOperatorOverload`) is unaffected
- [x] Extension operator overloads can be parsed: `operator +<Money>(self $left, int $right): self { ... }`

### Dependencies

- Existing grammar infrastructure (ANTLR4, `TyhpLexer.g4`, `TyhpParser.g4`)
- `T_TYHP_OPERATOR` and `T_TYHP_EXTENSION` tokens already exist in the lexer

---

## Phase 2: Grammar Changes — Tyhpdef Inline Extension Members




### Phase Overview

Add grammar support for the `extension` qualifier on function and operator declarations inside tyhpdef class bodies. These members have code bodies (unlike regular tyhpdef declarations) and compile to extension methods/operators for the declared type.

### Deliverables

- New grammar rules for `tyhpdefExtensionFunction` and `tyhpdefExtensionOperator`
- Updated `tyhpdefClassStatement` to include the new rules
- Grammar compiles without conflicts

### Implementation Details

#### 2.1 Add `extension` as a Recognized Modifier in Tyhpdef Context

The `extension` keyword (`T_TYHP_EXTENSION`) is already a lexer token. It needs to be recognized as a valid qualifier in the tyhpdef class body context.

#### 2.2 Create Tyhpdef Inline Extension Grammar Rules

**File:** `Tyhp/TyhpLang/Grammar/TyhpParser.g4`

Add new alternatives to `tyhpdefClassStatement` (with optional `tyhpdefDeprecatedOrObsolete?`, consistent with other class members):

```
tyhpdefClassStatement
    : ... (existing alternatives)
    | tyhpdefDeprecatedOrObsolete? tyhpdefExtensionFunction                     #tyhpdefExtensionFunctionDecl
    | tyhpdefDeprecatedOrObsolete? tyhpdefExtensionOperator                     #tyhpdefExtensionOperatorDecl
    ;

tyhpdefExtensionFunction
    : T_TYHP_EXTENSION function ReturnsRef=returnsRef
        GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType
        StatementList=methodBody                                                #tyhpdefExtensionFunctionFullDecl
    | T_TYHP_EXTENSION fn ReturnsRef=returnsRef
        GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType T_DOUBLE_ARROW
        Expr=expr T_SYM_SEMICOLON                                               #tyhpdefExtensionFunctionShortDecl
    ;

tyhpdefExtensionOperator
    : T_TYHP_EXTENSION T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody | (T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON)) #tyhpdefExtensionOperatorFullDecl
    ;
```

Key design points:
- `T_TYHP_EXTENSION` precedes `function`, `fn`, or `T_TYHP_OPERATOR` — the `extension` keyword acts as a qualifier.
- **Extension function — full:** `extension function` + `tyhpGenericIdentifierWithoutConstructor` + parameters + return type + **brace** `methodBody` only (`#tyhpdefExtensionFunctionFullDecl`).
- **Extension function — short:** `extension fn` + same name/params/return + `=> expr ;` (`#tyhpdefExtensionFunctionShortDecl`), aligned with Tyhp class `fn` short methods.
- **Extension operator:** brace `methodBody` **or** `=> expr ;` after the return type (`#tyhpdefExtensionOperatorFullDecl`); the arrow form **requires** `T_SYM_SEMICOLON` (unlike `tyhpClassOperatorOverload`, where shorthand is `=> expr` without a semicolon).
- `tyhpdefExtensionOperator` does NOT have `<Type>` — the target is the enclosing tyhpdef class.
- No visibility modifier — `extension` replaces visibility (always public).
- No `abstract` or `final` on these members (grammar does not allow it; same semantics as standalone extension members).

#### 2.3 Verify No Conflicts with Existing Tyhpdef Grammar

The `T_TYHP_EXTENSION` token at the start of a tyhpdef class statement is unambiguous because:
- Existing class statements start with `tyhpdefDeprecatedOrObsolete?`, modifiers, `T_USE`, or `tyhpdefClassOperator`
- `T_TYHP_EXTENSION` does not appear as the leading token in any existing alternative

### Acceptance Criteria

- [x] `tyhpdefExtensionFunction` has **full** and **short** alternatives (`#tyhpdefExtensionFunctionFullDecl`, `#tyhpdefExtensionFunctionShortDecl`), uses `tyhpGenericIdentifierWithoutConstructor`, and disallows `abstract`/`final`
- [x] `tyhpdefExtensionOperator` exists as `#tyhpdefExtensionOperatorFullDecl` without `<Type>` (implicit enclosing class), brace body or `=> expr ;`, without `abstract`/`final`
- [x] Both extension member rules are referenced from `tyhpdefClassStatement` (with optional `tyhpdefDeprecatedOrObsolete?`)
- [x] The grammar compiles without conflicts by running `./compile_grammar.sh`
- [x] Full extension function parses, e.g. `extension function format(): string { return $this->value; }` (and generic names, e.g. `extension function map<T>(...)` when valid)
- [x] Short extension function parses, e.g. `extension fn label(): string => $this->name;`
- [x] Extension operator parses with brace body or arrow + semicolon, e.g. `extension operator +(self $a, self $b): self { ... }` or `extension operator +(self $a, self $b): self => $a->plus($b);`
- [x] Error: `extension int $foo;` is not parseable (only `function`, `fn`, and `operator` are valid after `extension`)

### Dependencies

- Phase 1 (extension operator grammar in standalone extensions)
- Existing tyhpdef grammar rules in `TyhpParser.g4`

---

## Phase 3: Grammar Changes — `use extension` in Tyhpdef Class Bodies




### Phase Overview

Add `use extension` as a valid statement inside tyhpdef class bodies, with trait-like adaptations for conflict resolution.

### Deliverables

- Updated `tyhpdefClassStatement` to include `use extension` with adaptations
- Grammar compiles without conflicts by running `./compile_grammar.sh`

### Implementation Details

#### 3.1 Add `use extension` to Tyhpdef Class Statement

**File:** `Tyhp/TyhpLang/Grammar/TyhpParser.g4`

Add a new alternative to `tyhpdefClassStatement` (with optional `tyhpdefDeprecatedOrObsolete?`, consistent with `tyhpdefTraitUse`):

```
tyhpdefClassStatement
    : ... (existing alternatives)
    | tyhpdefDeprecatedOrObsolete? T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations                                    #tyhpdefClassUseExtension
    ;
```

This reuses the existing `useDeclarations` and `traitAdaptations` rules, which already support:
- `ExtensionName::method as newName;` (alias)
- `ExtensionName::method insteadof OtherName;` (precedence)
- `ExtensionName::method as protected;` (visibility change — **parsed but invalid for extensions**)
- `;` (no adaptations)

The adaptation syntax matches PHP trait adaptations at the grammar level for a familiar feel. However, the **visibility change** variant (`as modifier`) is not valid for extensions because extension members are always public. The grammar will accept it (since it reuses `traitAdaptations`), but the checker must report an error when a visibility adaptation is used on an extension. See Story 08, section 6.2.1 for the validation rule.

### Acceptance Criteria

- [x] `use extension ExtensionName;` parses inside a tyhpdef class body
- [x] `use extension ExtensionName { Ext::method as newName; };` parses with adaptations
- [x] The grammar compiles without conflicts by running `./compile_grammar.sh`
- [x] Existing `tyhpdefTraitUse` (for traits) is unaffected

### Dependencies

- Existing `traitAdaptations` and `useDeclarations` grammar rules

---

## Phase 4: AST and Visitor — Extension Operator Overloads




### Phase Overview

Create AST nodes and visitor methods for extension operator overloads with the `<Type>` target syntax. Fix the existing extension visitor to properly dispatch between functions and operator overloads.

### Deliverables

- `TyhpOperatorOverloadAst` extended with nullable `ExtensionTargetType` for extension operators with `<Type>` syntax
- Updated `TyhpExtensionFunctionListAst` to hold both functions and operators
- New `VisitTyhpExtensionMember` dispatch method in the visitor
- `VisitTyhpExtensionOperatorOverload` method in the visitor

### Implementation Details

#### 4.1 Create or Extend AST for Extension Operator Overloads

Extend `TyhpOperatorOverloadAst` with an optional extension target:

Add to `TyhpOperatorOverloadAst`:
- `ITypeExpression? ExtensionTargetType` — null for class-level operators, populated for extension operators
- `bool IsExtensionOperator => ExtensionTargetType != null`

All operator overloads share this single AST type; extension operators are distinguished by a non-null `ExtensionTargetType`.

#### 4.2 Update `TyhpExtensionFunctionListAst`

**File:** `Tyhp/TyhpLang/Ast/TyhpExtensionFunctionListAst.cs`

The current class is typed as `NodeListAst<PhpFunctionDeclAst, TyhpExtensionFunctionListAst>`. This cannot hold operator overload AST nodes.

**Change to:** Introduce an `IExtensionMemberAst` marker interface implemented by both `PhpFunctionDeclAst` and `TyhpOperatorOverloadAst`, and type the list as `NodeListAst<IExtensionMemberAst, TyhpExtensionFunctionListAst>`. This keeps the list type-safe without widening to arbitrary `IBase2Ast` nodes or splitting into parallel function/operator lists.

#### 4.3 Fix Extension Visitor to Dispatch on Member Type

**File:** `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpExtensions.cs`

The current visitor has a bug — it tries to call `VisitFunctionDeclarationStatement` on all `_Items`, but `_Items` are `TyhpExtensionMemberContext` not `FunctionDeclarationStatementContext`.

Replace `VisitTyhpExtensionFunctionList` with proper dispatching:

```csharp
public override TyhpExtensionFunctionListAst VisitTyhpExtensionFunctionList(
    [NotNull] TyhpParser.TyhpExtensionFunctionListContext context)
    => TyhpExtensionFunctionListAst.Create(
        context._Items.Select(item => VisitTyhpExtensionMember(item)),
        context
    );

private IExtensionMemberAst VisitTyhpExtensionMember(
    TyhpParser.TyhpExtensionMemberContext context)
{
    if (context.functionDeclarationStatement() != null)
        return this.VisitFunctionDeclarationStatement(context.functionDeclarationStatement());

    if (context.tyhpExtensionOperatorOverload() != null)
        return this.VisitTyhpExtensionOperatorOverload(context.tyhpExtensionOperatorOverload());

    return ErrorAst.Create(context);
}
```

#### 4.4 Implement `VisitTyhpExtensionOperatorOverload`

**File:** `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpExtensions.cs`

```csharp
public TyhpOperatorOverloadAst VisitTyhpExtensionOperatorOverload(
    [NotNull] TyhpParser.TyhpExtensionOperatorOverloadContext context)
{
    var op = this.VisitTyhpClassOperatorOverloadOp(context.Op);
    var targetType = this.VisitTypeHint(context.TargetType);
    var leftParam = this.VisitParameter(context.LeftParameter);
    var rightParam = context.RightParameter != null
        ? this.VisitParameter(context.RightParameter)
        : null;
    var returnType = this.VisitReturnType(context.ConvertReturnType);
    var body = context.StatementList != null
        ? this.VisitMethodBody(context.StatementList)
        : null;
    var shorthandExpr = context.ShorthandExpr != null
        ? this.VisitExpr(context.ShorthandExpr)
        : null;

    // Extension operators never use abstract/final (not in grammar).
    var ast = TyhpOperatorOverloadAst.Create(
        op, leftParam, rightParam, returnType, body, null,
        context, GetCurrentLanguageMode(context)
    );
    ast.ExtensionTargetType = targetType;
    if (shorthandExpr != null) ast.ShorthandBody = shorthandExpr;
    return ast;
}
```

### Acceptance Criteria

- [x] `TyhpOperatorOverloadAst` has an `ExtensionTargetType` property (nullable `ITypeExpression`)
- [x] `TyhpExtensionFunctionListAst` can hold both function and operator AST nodes
- [x] `VisitTyhpExtensionMember` dispatches correctly to function or operator visitor
- [x] `VisitTyhpExtensionOperatorOverload` passes `null` for the operator modifier (grammar has no `abstract`/`final`)
- [x] Extension operators with `<Type>` produce `TyhpOperatorOverloadAst` nodes with `ExtensionTargetType` populated
- [x] Existing class-level operator overloads produce nodes with `ExtensionTargetType = null`
- [x] The visitor no longer has a type inference error on `context._Items.Select(...)`

### Dependencies

- Phase 1 (grammar changes for extension operators)
- Existing AST infrastructure (`TyhpOperatorOverloadAst`, `NodeListAst`, `Base2Ast`)

---

## Phase 5: AST and Visitor — Tyhpdef Inline Extension Members




### Phase Overview

Create AST nodes and visitor methods for tyhpdef inline extension members (`extension function`, `extension fn`, and `extension operator`) and for `use extension` inside tyhpdef class bodies.

### Deliverables

- New `TyhpdefInlineExtensionFunctionAst` (wraps `PhpFunctionDeclAst` for full and short extension function bodies)
- Extension operators represented by existing `TyhpOperatorOverloadAst` with `IsInlineExtension = true`
- `VisitTyhpdefExtensionFunctionDecl` and `VisitTyhpdefExtensionOperatorDecl` visitor methods
- `VisitTyhpdefClassUseExtension` visitor method
- Updated `PhpClassBodyAst` to accommodate inline extension members

### Implementation Details

#### 5.1 AST Design for Tyhpdef Inline Extension Members

**Extension functions in tyhpdef:** These behave like named functions with bodies (not like abstract tyhpdef method stubs): always public, treat `$this` as the enclosing tyhpdef instance (no `extends` on the first parameter). The name comes from **`tyhpGenericIdentifierWithoutConstructor`** (generics on the method name, same as other Tyhp methods). **Full** declarations use `extension function` + brace `methodBody`; **short** declarations use `extension fn` + `=> expr ;` — both map to a **`PhpFunctionDeclAst`** (short form lowers to a body wrapping a return, same pattern as class `fn` short methods). Wrap in **`TyhpdefInlineExtensionFunctionAst`** when that dedicated node is introduced.

**Extension operators in tyhpdef:** These use the same **`TyhpOperatorOverloadAst`** shape as class-level operators. Set **`IsInlineExtension = true`**. The target type is always the enclosing tyhpdef class and is established during binding, not stored as a separate field on the AST. Pass **`null`** for the operator modifier (no `abstract`/`final`, same as standalone extension operators). **Grammar note:** shorthand is `=> expr T_SYM_SEMICOLON` (field `Expr` on `TyhpdefExtensionOperatorContext`), not the class-operator `ShorthandExpr` shape without a semicolon — the visitor should build the same AST as class operators (e.g. wrap `Expr` in a return block) after reading `Expr`.

#### 5.2 Implement Visitor for `tyhpdefExtensionFunctionDecl`

**File:** `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs`

The child rule `tyhpdefExtensionFunction` has two labeled alternatives: **`TyhpdefExtensionFunctionFullDeclContext`** and **`TyhpdefExtensionFunctionShortDeclContext`**. Either override **`VisitTyhpdefExtensionFunction`** with a switch on the concrete context type, or override the two per-alternative visitor methods, then call the shared builder from **`VisitTyhpdefExtensionFunctionDecl`**.

```csharp
public override IBase2Ast VisitTyhpdefExtensionFunctionDecl(
    [NotNull] TyhpParser.TyhpdefExtensionFunctionDeclContext context)
{
    var funcCtx = context.tyhpdefExtensionFunction();
    // Dispatch: TyhpdefExtensionFunctionFullDeclContext vs TyhpdefExtensionFunctionShortDeclContext
    // Full: VisitMethodBody(funcCtx.StatementList)
    // Short: VisitExpr(funcCtx.Expr) wrapped in a return statement block (and note trailing ; is token, not stored on Expr)
    var funcDeclAst = /* PhpFunctionDeclAst from GenericIdentifier, ReturnsRef, ParameterList, ReturnType, body */;
    return TyhpdefInlineExtensionFunctionAst.Create(funcDeclAst, context);
}
```

Use **`VisitTyhpGenericIdentifierWithoutConstructor`** (or the project’s existing visitor for `tyhpGenericIdentifierWithoutConstructor`) for the method name, not **`VisitIdentifier`**.

#### 5.3 Implement Visitor for `tyhpdefExtensionOperatorDecl`

Inner rule label: **`#tyhpdefExtensionOperatorFullDecl`**. Body: **`StatementList=methodBody`** or **`T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON`**.

```csharp
public override TyhpOperatorOverloadAst VisitTyhpdefExtensionOperatorDecl(
    [NotNull] TyhpParser.TyhpdefExtensionOperatorDeclContext context)
{
    var opCtx = context.tyhpdefExtensionOperator();
    var op = this.VisitTyhpClassOperatorOverloadOp(opCtx.Op);
    var leftParam = this.VisitParameter(opCtx.LeftParameter);
    var rightParam = opCtx.RightParameter != null
        ? this.VisitParameter(opCtx.RightParameter)
        : null;
    var returnType = this.VisitReturnType(opCtx.ConvertReturnType);
    PhpStatementBlockAst? body;
    if (opCtx.StatementList != null)
    {
        body = this.VisitMethodBody(opCtx.StatementList);
    }
    else if (opCtx.Expr != null)
    {
        var expr = this.VisitExpr(opCtx.Expr);
        body = /* single return statement wrapping expr, same as class operator shorthand */;
    }
    else
    {
        body = null;
    }

    var ast = TyhpOperatorOverloadAst.Create(
        op, leftParam, rightParam, returnType, body, null,
        context, GetCurrentLanguageMode(context)
    );
    ast.IsInlineExtension = true;
    return ast;
}
```

#### 5.4 Implement Visitor for `tyhpdefClassUseExtension`

Reuse **`TyhpImportExtensionAst`** (same shape as file-level `use extension` in tyhpdef) and implement **`IClassMember`** on that type so it can sit in **`PhpClassBodyAst`**. Visitor builds `TyhpImportExtensionAst.Create(VisitUseDeclarations(UseDecl), VisitTraitAdaptations(Adaptations), …)` and applies optional `deprecatedOrObsolete` grammar addon when present.

```csharp
public override TyhpImportExtensionAst VisitTyhpdefClassUseExtension(
    [NotNull] TyhpParser.TyhpdefClassUseExtensionContext context)
{
    var useDecls = this.VisitUseDeclarations(context.UseDecl);
    var adaptations = this.VisitTraitAdaptations(context.Adaptations);
    return TyhpImportExtensionAst.Create(useDecls, adaptations, context, GetCurrentLanguageMode(context));
}
```

#### 5.5 ~~Create `TyhpdefClassUseExtensionAst`~~ (superseded)

**Resolution:** Use **`TyhpImportExtensionAst`** for both top-level tyhpdef `use extension` imports and class-body `use extension`. A separate `TyhpdefClassUseExtensionAst` is unnecessary unless the binder later needs a distinct type; distinguish by parse context / parent AST if required.

### Acceptance Criteria

- [x] Tyhpdef inline extension functions produce `TyhpdefInlineExtensionFunctionAst` nodes (full `extension function` and short `extension fn` both covered)
- [x] Tyhpdef inline extension operators produce `TyhpOperatorOverloadAst` nodes with `IsInlineExtension = true` (brace body and `=> expr ;` both covered)
- [x] `use extension` inside tyhpdef class bodies produces `TyhpImportExtensionAst` nodes (implements `IClassMember`)
- [x] All new AST nodes are included in `PhpClassBodyAst.GetAllNotNull()` iteration
- [x] Visitor methods compile and produce correct AST structure

### Dependencies

- Phase 2 and 3 (grammar changes for tyhpdef inline extensions)
- Phase 4 (AST changes for extension operators)

---

## Phase 6: Binder — Extension Operator Overloads and Tyhpdef Inline Extensions




### Phase Overview

Update the binder to process extension operator overloads, tyhpdef inline extension members, and `use extension` references in tyhpdef class bodies. Extension operators must be attached to their target type's symbol so the checker and emitter can find them during operator resolution.

### Deliverables

- Extension operator binding — creates `ObjectOperatorOverloadMethodSymbol` on the target type
- Tyhpdef inline extension processing — generates synthetic extension symbols
- `use extension` in tyhpdef class body — resolves and activates external extensions
- Conflict detection between extension members and existing class methods
- New MessageCodes for extension/tyhpdef-specific errors

### Implementation Details

#### 6.1 Bind Extension Operator Overloads

**File:** `Tyhp/TyhpLang/Binder/TyhpBinder.ObjectBody.cs`

When the binder encounters a `TyhpOperatorOverloadAst` with `ExtensionTargetType != null` inside an extension declaration:

1. Resolve `ExtensionTargetType` to an `ObjectDeclarationSymbol` using name resolution
2. Create an `ObjectOperatorOverloadMethodSymbol` with:
   - The resolved operator enum
   - The extension class as the declaring scope (not the target type)
   - A reference to the target type
   - `IsExtensionOperator = true` flag
3. Register the operator on the target type's operator overload list
4. The extension class symbol should also track its operators for emission purposes

**New property on `ObjectOperatorOverloadMethodSymbol`:**
- `bool IsExtensionOperator` — distinguishes from class-level operators
- `ObjectDeclarationSymbol? ExtensionTargetType` — the type this operator applies to
- `ObjectDeclarationSymbol? DeclaringExtension` — the extension class containing this operator

**Validation during binding:**
- Error if `ExtensionTargetType` resolves to a non-existent type → `MessageCode.BinderSymbolNotFound`
- Error if `<Type>` is specified on an operator inside a class body (not an extension) → `MessageCode.ExtensionOperatorTargetNotAllowed` (3015)
- Error if `<Type>` is missing on an operator inside an extension → new `MessageCode.ExtensionOperatorMissingTarget`

#### 6.2 Bind Tyhpdef Inline Extension Members

**File:** `Tyhp/TyhpLang/Binder/TyhpBinder.Tyhpdef.cs`

When `BindTyhpdefObjectBody` encounters an inline extension member:

1. **Extension functions (`TyhpdefInlineExtensionFunctionAst`):**
   - Use the wrapped `PhpFunctionDeclAst` inside the node as the source for name, parameters, return type, and body
   - Create a synthetic extension class symbol (auto-named, e.g., `__TyhpExt_{ClassName}`)
   - Create the method as a static method on the synthetic extension class
   - Register the extension method as available on the tyhpdef class
   - `$this` parameter is implicitly the first parameter typed as the enclosing class

2. **Extension operators (`IsInlineExtension = true` on `TyhpOperatorOverloadAst`):**
   - Create the operator on the synthetic extension class
   - Register as an extension operator on the tyhpdef class (same as 6.1 but target is implicit)
   - `self` in operator parameters resolves to the enclosing tyhpdef class

3. **Synthetic extension class management:**
   - One synthetic extension class per tyhpdef class that has inline extension members
   - The synthetic class is added to the same namespace as the tyhpdef class
   - It is marked as compiler-generated (not user-visible)

#### 6.3 Bind `use extension` in Tyhpdef Class Bodies

**File:** `Tyhp/TyhpLang/Binder/TyhpBinder.Tyhpdef.cs`

When `BindTyhpdefObjectBody` encounters a `TyhpImportExtensionAst` from a class body (`#tyhpdefClassUseExtension`):

1. Resolve the extension name(s) from `UseDeclarations` to extension symbols
2. Process adaptations:
   - **`as` aliases:** Record the alias mapping — when the extension method/operator is resolved on this type, use the alias name
   - **`insteadof`:** Record precedence — when both the class and extension have a method with the same name, the `insteadof` determines which wins
3. Register all extension members (methods and operators) from the referenced extension as available on the tyhpdef class, applying adaptations
4. Store the `use extension` reference on the `ObjectDeclarationSymbol` so that any file importing this type automatically gets the extension activated

#### 6.4 Conflict Detection

During binding, check for conflicts between:

1. **Extension method name vs. tyhpdef declared method name:**
   - If a tyhpdef class declares `public function format(): string;` and also has `extension function format(): string { ... }` or `extension fn format(): string => ...;`, report error
   - MessageCode: `TyhpdefExtensionConflict = 8010` — "Extension member '{0}' conflicts with declared member on class '{1}'. Use 'as' to rename or remove the conflicting member."

2. **`use extension` method name vs. declared method name:**
   - Same as above but for externally imported extensions
   - Resolvable via `as` or `insteadof` adaptations

3. **Multiple extensions with same method/operator for same type:**
   - If two different extensions (both used via `use extension`) define the same operator for the same type, report error unless resolved via `insteadof`

#### 6.5 Auto-Activation of Extensions

When the binder resolves a type reference to an `ObjectDeclarationSymbol` that has `use extension` references (from tyhpdef), it should automatically make those extension members available in the resolving scope. This means:

- The extension methods and operators are resolvable on the type without the user explicitly writing `use extension` in their own code
- The binder's `ResolveExtensionMethod` and operator resolution should check the type's auto-included extensions
- This happens during the resolution pass (Pass 2) when resolving member access and operator expressions

#### 6.6 New MessageCodes

**File:** `Tyhp/Domain/Exceptions/MessageCode.cs`

Add in the 8000s range (tyhpdef):

| Code | Name | Description |
|------|------|-------------|
| 8010 | `TyhpdefExtensionConflict` | Extension member conflicts with declared member |
| 8011 | `TyhpdefExtensionNotFound` | Referenced extension in `use extension` not found |
| 8012 | `TyhpdefInlineExtensionInvalidMember` | Only `function`, `fn`, and `operator` allowed with `extension` qualifier |

Add in the 3000s range (binder):

| Code | Name | Description |
|------|------|-------------|
| 3014 | `ExtensionOperatorMissingTarget` | Operator in extension body missing `<Type>` target |
| 3015 | `ExtensionOperatorTargetNotAllowed` | `<Type>` on operator inside class body (not extension) |
| 3016 | `ExtensionOperatorTargetNotFound` | `<Type>` target type could not be resolved |

Add in the 4000s range (checker — see Story 08 for full implementation):

| Code | Name | Description |
|------|------|-------------|
| 4038 | `CheckerExtensionVisibilityNotAllowed` | Visibility adaptation used on extension member (extensions are always public) |

### Acceptance Criteria

- [x] Extension operators with `<Type>` are bound and create `ObjectOperatorOverloadMethodSymbol` with `IsExtensionOperator = true`
- [x] Extension operator target types are resolved to `ObjectDeclarationSymbol` via name resolution
- [x] Tyhpdef inline extension functions (`extension function` and `extension fn`) create a synthetic extension class and register the method
- [x] Tyhpdef inline `extension operator` creates an operator on the synthetic extension class
- [x] `$this` in inline extension functions resolves to the tyhpdef class type
- [x] `self` in inline extension operators resolves to the tyhpdef class type
- [x] `use extension` in tyhpdef class bodies resolves extension references and registers members
- [x] Trait-like adaptations (`as`, `insteadof`) are processed correctly on `use extension`
- [x] Conflicts between extension members and declared members are detected and reported
- [x] Auto-activation: types with `use extension` in tyhpdef make extensions available without explicit import
- [x] Missing `<Type>` on extension operators produces `ExtensionOperatorMissingTarget` error
- [x] `<Type>` on class-level operators produces `ExtensionOperatorTargetNotAllowed` error
- [x] All new MessageCodes are added to the appropriate range

### Dependencies

- Phase 4 and 5 (AST and visitor changes)
- Story 02 binder infrastructure (symbol resolution, `ObjectDeclarationSymbol`, `ObjectOperatorOverloadMethodSymbol`)

---

## Phase 7: Update Documentation and Examples




### Phase Overview

Update the extension documentation, operator overloads example, and any relevant design documents to reflect the new features.

### Deliverables

- Updated `docs/content/tyhp_2100_extensions.json` — extension operator overload section rewritten with `<Type>` syntax; state that `abstract`/`final` are not allowed on extension operators or on tyhpdef `extension function` / `extension fn` / `extension operator`
- Updated `Examples/OperatorOverloads.tyhp` — add extension operator examples
- New example file or section showing tyhpdef inline extensions

### Implementation Details

#### 7.1 Update Extension Documentation

**File:** `docs/content/tyhp_2100_extensions.json`

Update the "Extension Operator Overloads" section (currently shows `extends` on operator parameter). Replace with the `<Type>` syntax:

**Before:**
```tyhp
extension StringOperators {
    operator *(extends string $left, int $right): string
    {
        return \str_repeat($left, $right);
    }
}
```

**After:**
```tyhp
extension StringOperators {
    operator *<string>(self $left, int $right): string
    {
        return \str_repeat($left, $right);
    }
}
```

Add a new section for extension operators targeting class types:

```tyhp
extension MoneyOperators {
    operator +<Money>(self $left, self $right): self {
        return $left->plus($right);
    }

    operator ==<Money>(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }
}
```

Add a section explaining tyhpdef inline extensions:

```tyhpdef
<?tyhpdef
class Money {
    public function plus(Money $other): Money;
    public function isEqualTo(Money $other): bool;

    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }

    extension operator ==(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }

    extension operator +(self $left, Money $right): self => $left->plus($right);

    extension function formatCurrency(string $locale = 'en_US'): string {
        return $locale . ' ' . $this->__toInt();
    }

    extension fn shortLabel(): string => $this->formatCurrency();
}
```

Add a section explaining `use extension` in tyhpdef with adaptations:

```tyhpdef
<?tyhpdef
class Money {
    public function plus(Money $other): Money;
    public function format(): string;

    use extension MoneyFormatting {
        MoneyFormatting::format as formatExtended;
    };
}
```

#### 7.2 Update Operator Overloads Example

**File:** `Examples/OperatorOverloads.tyhp`

Add a section at the end demonstrating extension operator overloads:

```tyhp
// Extension operator overloads — adding operators to types you don't own
extension MyClassOperators {
    operator +<MyClass>(self $left, int $right): self {
        $newObj = new MyBlahClass();
        return $newObj;
    }
}
```

#### 7.3 Update Comments in Examples/OperatorOverloads.tyhp

Update the naming convention comments to document the extension operator naming pattern:

- Class-level operators: `__OP_This_ADD_Int` (uses `This` for `self`)
- Extension operators: `__OP_Money_ADD_Int` (uses actual target type name)

### Acceptance Criteria

- [x] Extension operator documentation uses `<Type>` syntax (not `extends` on parameter) and documents the no-`abstract`/`final` rule for extension members
- [x] Tyhpdef inline extension section added to documentation
- [x] `use extension` with adaptations section added to documentation
- [x] `Examples/OperatorOverloads.tyhp` includes extension operator examples
- [x] Naming convention comments updated for extension operators

### Dependencies

- Phases 1-6 (grammar, AST, visitor, binder work should be understood even if not fully implemented)

---

## Appendix A: Emitted PHP Method Naming Convention

### Class-Level Operator Overloads (existing design)

| Operator | Left type | Right type | Generated method name |
|----------|-----------|------------|----------------------|
| `+` (binary) | `self` | `self` | `__OP_This_ADD_This` |
| `+` (binary) | `self` | `int` | `__OP_This_ADD_Int` |
| `+` (binary) | `int` | `self` | `__OP_Int_ADD_This` |
| `==` | `self` | `self` | `__OP_This_EQ_This` |
| `convert` | `self` → `int` | — | `__OP_This_CONVERT_TO_Int` |

Unified public methods: `__add(...)`, `__isEqualTo(...)`, etc.

### Extension Operator Overloads (new design)

| Operator | Target | Left type | Right type | Generated static method name |
|----------|--------|-----------|------------|------------------------------|
| `+` (binary) | `Money` | `self` | `self` | `__OP_Money_ADD_Money` |
| `+` (binary) | `Money` | `self` | `int` | `__OP_Money_ADD_Int` |
| `==` | `Money` | `self` | `self` | `__OP_Money_EQ_Money` |
| `+` (binary) | `Percentage` | `self` | `self` | `__OP_Percentage_ADD_Percentage` |
| `convert` | `Money` | `self` → `int` | — | `__OP_Money_CONVERT_TO_Int` |

No unified public methods. Compiler resolves directly to the specific static method.

### Namespaced Type Names

When the target type is namespaced (e.g., `App\Finance\Money`), use the short class name (`Money`) in the method name since full parameter types already disambiguate. If two different types with the same short name exist in the same extension class (unlikely but possible), append a disambiguator.

---

## Appendix B: Complete Syntax Examples

### Standalone Extension with Operator Overloads

```tyhp
<?tyhp

extension MoneyOperators {
    // Binary: Money + Money
    operator +<Money>(self $left, self $right): self {
        return $left->plus($right);
    }

    // Binary: Money + int (fallback)
    operator +<Money>(int $left, self $right): int {
        return $left + $right->__toInt();
    }

    // Comparison
    operator ==<Money>(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }

    // Unary
    operator true<Money>(self $value): bool {
        return $value->getAmount() > 0;
    }

    // Conversion
    operator convert<Money>(self $value): int {
        return $value->__toInt();
    }

    // Regular extension method (uses extends on first param, as before)
    function formatCurrency(extends Money $this, string $locale): string {
        return $locale . ' ' . $this->__toInt();
    }
}
```

### Tyhpdef with Inline Extension Members

```tyhpdef
<?tyhpdef

class Money {
    public function plus(Money $other): Money;
    public function minus(Money $other): Money;
    public function isEqualTo(Money $other): bool;
    public function __toInt(): int;
    public function getAmount(): int;

    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }

    extension operator -(self $left, self $right): self {
        return $left->minus($right);
    }

    extension operator ==(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }

    extension operator convert(self $value): int {
        return $value->__toInt();
    }

    extension operator !=(self $left, self $right): bool => !$left->isEqualTo($right);

    extension function formatCurrency(string $locale = 'en_US'): string {
        return $locale . ' ' . $this->__toInt();
    }

    extension fn quickSummary(): string => (string) $this->__toInt();
}
```

### Tyhpdef with External Extension Reference

```tyhpdef
<?tyhpdef

class Money {
    public function plus(Money $other): Money;
    public function format(): string;

    use extension MoneyOperators;

    use extension MoneyFormatting {
        MoneyFormatting::format as formatExtended;
    };
}
```

### User Code (just works)

```tyhp
<?tyhp
use Money;

Money $total = $price + $tax;
if ($total == $expected) {
    int $cents = (int)$total;
    echo $total->formatCurrency('en_US');
}
```

---

*Last updated: 2026-03-19 — Initial creation from design discussion*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify that extension operator overloads and tyhpdef inline extensions work correctly across grammar, visitor, AST, and binder. Steps can be skipped, reordered, or modified as needed. All commands assume you are in the repository root.

### Step 1: Verify Grammar Compiles Without Conflicts

After the grammar changes, regenerate the ANTLR parser:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Expected: No grammar conflicts reported. The parser regenerates successfully. Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone.

### Step 2: Verify Extension Operator Overloads Parse

Create a test file with extension operator overloads using the `<Type>` syntax:

```tyhp
<?tyhp
namespace Test\ExtOps;

class Money {
    public int $amount;

    public function __construct(int $amount) {
        $this->amount = $amount;
    }

    public function plus(Money $other): Money {
        return new Money($this->amount + $other->amount);
    }

    public function isEqualTo(Money $other): bool {
        return $this->amount === $other->amount;
    }

    public function __toInt(): int {
        return $this->amount;
    }
}

extension MoneyOperators {
    operator +<Money>(self $left, self $right): self {
        return $left->plus($right);
    }

    operator ==<Money>(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }

    operator convert<Money>(self $value): int {
        return $value->__toInt();
    }

    function formatCurrency(extends Money $this, string $locale): string {
        return $locale . " " . \strval($this->__toInt());
    }
}
```

Save as `test_ext_operators.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_ext_operators.tyhp
```

Expected:
- The file parses without grammar errors
- Extension operators with `<Money>` target produce `TyhpOperatorOverloadAst` nodes with `ExtensionTargetType` populated
- The extension function with `extends` on the first param still works as before
- No crash during binding

### Step 3: Verify `<Type>` Is NOT Allowed on Class-Level Operators

Create a file that incorrectly uses `<Type>` inside a class body (not an extension):

```tyhp
<?tyhp
namespace Test\BadOps;

class Foo {
    operator +<Foo>(self $left, self $right): self {
        return $left;
    }
}
```

Save as `test_ext_bad_class_op.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_ext_bad_class_op.tyhp
```

Expected: Either a parse error (if the grammar rejects it) or a binder error `ExtensionOperatorTargetNotAllowed` (3015). The `<Type>` syntax should only be valid inside extension bodies, not class bodies.

### Step 4: Verify Tyhpdef Inline Extension Functions Parse

Create a tyhpdef file with inline extension members:

```tyhpdef
<?tyhpdef
namespace Test\TyhpdefExt;

class Counter {
    public function getValue(): int;
    public function increment(): void;

    extension function doubleIncrement(): void {
        $this->increment();
        $this->increment();
    }

    extension fn label(): string => "Counter: " . \strval($this->getValue());
}
```

Save as `test_tyhpdef_inline_ext.tyhpdef`. Run:

```bash
dotnet run --project tyhp.csproj -- lint test_tyhpdef_inline_ext.tyhpdef
```

Expected:
- The full extension function (`extension function doubleIncrement()`) parses correctly with a brace body
- The short extension function (`extension fn label()`) parses correctly with `=> expr ;` syntax
- No parse errors

### Step 5: Verify Tyhpdef Inline Extension Operators Parse

```tyhpdef
<?tyhpdef
namespace Test\TyhpdefExt;

class Score {
    public function getPoints(): int;
    public function add(Score $other): Score;
    public function isEqual(Score $other): bool;

    extension operator +(self $left, self $right): self {
        return $left->add($right);
    }

    extension operator ==(self $left, self $right): bool => $left->isEqual($right);
}
```

Save as `test_tyhpdef_inline_ops.tyhpdef`. Run:

```bash
dotnet run --project tyhp.csproj -- lint test_tyhpdef_inline_ops.tyhpdef
```

Expected:
- Inline extension operator with brace body parses correctly
- Inline extension operator with `=> expr ;` (arrow + semicolon) parses correctly
- No `<Type>` is required (target is the enclosing tyhpdef class)

### Step 6: Verify Invalid Extension Member Is Rejected

Test that `extension` only works with `function`, `fn`, and `operator`:

```tyhpdef
<?tyhpdef
namespace Test\TyhpdefBad;

class Broken {
    extension int $foo;
}
```

Save as `test_tyhpdef_bad_ext.tyhpdef`. Run:

```bash
dotnet run --project tyhp.csproj -- lint test_tyhpdef_bad_ext.tyhpdef
```

Expected: A parse error — `extension int $foo;` is not valid grammar. Only `function`, `fn`, and `operator` can follow `extension`.

### Step 7: Verify `use extension` in Tyhpdef Class Bodies

```tyhpdef
<?tyhpdef
namespace Test\UseExt;

class Widget {
    public function render(): string;

    use extension WidgetFormatting;

    use extension WidgetAnimations {
        WidgetAnimations::render as animatedRender;
    };
}
```

Save as `test_tyhpdef_use_ext.tyhpdef`. Run:

```bash
dotnet run --project tyhp.csproj -- lint test_tyhpdef_use_ext.tyhpdef
```

Expected:
- `use extension WidgetFormatting;` (no adaptations) parses correctly
- `use extension WidgetAnimations { ... };` (with `as` adaptation) parses correctly
- Produces `TyhpImportExtensionAst` nodes in the class body

### Step 8: Verify Binder Creates Extension Operator Symbols

If verbose/debug output is available, check that extension operators create `ObjectOperatorOverloadMethodSymbol` with `IsExtensionOperator = true`:

```bash
dotnet run --project tyhp.csproj -- build --verbose test_ext_operators.tyhp
```

Look for:
- Extension operators bound to target type `Money`
- `IsExtensionOperator = true` on the operator symbols
- The extension class (`MoneyOperators`) tracks its operators

If no verbose output is available, confirm no `ExtensionOperatorMissingTarget` (3014) or `BinderSymbolNotFound` (3003) diagnostics appear.

### Step 9: Verify Existing Class-Level Operators Still Work

Run the existing operator overloads example to confirm no regressions:

```bash
dotnet run --project tyhp.csproj -- build Examples/OperatorOverloads.tyhp
```

Expected: Parses and binds without errors. Class-level operator overloads should produce `TyhpOperatorOverloadAst` nodes with `ExtensionTargetType = null`.

### Step 10: Clean Up Test Files

```bash
rm -f test_ext_operators.tyhp test_ext_bad_class_op.tyhp test_tyhpdef_inline_ext.tyhpdef test_tyhpdef_inline_ops.tyhpdef test_tyhpdef_bad_ext.tyhpdef test_tyhpdef_use_ext.tyhpdef
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
