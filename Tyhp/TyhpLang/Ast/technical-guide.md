# Tyhp AST Layer — Developer Technical Guide

This guide documents the Abstract Syntax Tree (AST) layer under `Tyhp/TyhpLang/Ast/` (~153 files). Everything here is grounded in the current source. Where behavior is unclear after research, see [Open Questions / Needs Clarification](#open-questions--needs-clarification).

---

## Table of contents

1. [Purpose in the pipeline](#1-purpose-in-the-pipeline)
2. [Directory layout and taxonomy](#2-directory-layout-and-taxonomy)
3. [Core model: `Base2Ast` and `IBase2Ast`](#3-core-model-base2ast-and-ibase2ast)
4. [Source-file roots](#4-source-file-roots)
5. [Node lists](#5-node-lists)
6. [Interfaces](#6-interfaces)
7. [How nodes are built from the parser](#7-how-nodes-are-built-from-the-parser)
8. [Conventions and patterns](#8-conventions-and-patterns)
9. [Flags and enum offsets](#9-flags-and-enum-offsets)
10. [Grammar addons](#10-grammar-addons)
11. [Attributes](#11-attributes)
12. [Serialization and cache](#12-serialization-and-cache)
13. [Node type registry](#13-node-type-registry)
14. [Helpers and utilities](#14-helpers-and-utilities)
15. [Error recovery and special nodes](#15-error-recovery-and-special-nodes)
16. [Php\* / Tyhp\* / Tyhpdef\* catalogs](#16-php--tyhp--tyhpdef-catalogs)
17. [Interactions with Binder / Checker / Emitter](#17-interactions-with-binder--checker--emitter)
18. [Weirdness (with evidence)](#18-weirdness-with-evidence)
19. [Pitfalls](#19-pitfalls)
20. [Open Questions / Needs Clarification](#open-questions--needs-clarification)

---

## 1. Purpose in the pipeline

The AST is the **stable, language-mode-aware intermediate representation** between ANTLR parse trees and the Binder / Checker / Emitter.

```
Source (.php / .tyhp / .tyhpdef)
  → Lexer (TyhpLexer) + Parser (TyhpParser)
  → Parse tree (ANTLR RuleContext)
  → Visitor (TyhpParserAstVisitor : PhpParserAstVisitor)
  → SrcFileAst (PhpSrcFileAst | TyhpSrcFileAst | TyhpdefSrcFileAst)
  → [optional] AstCacheService serialize/deserialize
  → Binder (sets BoundSymbol, OwningFile; builds symbol table)
  → Checker (reads AST + BoundSymbol)
  → Emitter (walks/transforms AST → PHP text)
```

**Who builds ASTs**

- `CompilationService` lexes/parses each user file, then `new TyhpParserAstVisitor(...).Visit(ctx)` and casts the result to `SrcFileAst`. Error-free trees may be written via `AstCacheService.AddOrUpdate`.
- Built-in / package tyhpdefs use the same visitor path in `Binder/BuiltIn/Tyhpdef.ParseContent`. Those caches are always on (independent of `CompilationOptions.EnableAstCache`). Cache hits **deserialize a fresh tree each time** so binder mutations (`BoundSymbol`, `OwningFile`) do not leak across `Bind()` calls.

**What the AST is not**

- It is not the ANTLR parse tree (no parent pointers to `ParserRuleContext` on most nodes; only a few error/placeholder types keep a non-serialized `Context`).
- It is not the symbol table. Symbols hang off nodes via `BoundSymbol` after binding.
- It is not emit output. Emitter may synthesize additional nodes (`CreateFromContext`, `EmittedPhpExprAst`) that never came from the parser.

---

## 2. Directory layout and taxonomy

| Location | Count (approx.) | Role |
|---|---|---|
| `Ast/*.cs` (root) | ~130 | Concrete node types + bases |
| `Ast/Interfaces/` | 23 | Marker / capability interfaces |
| **Total** | **153** | |

### Naming prefixes

| Prefix | Meaning |
|---|---|
| `Php*` | Shared PHP-shaped syntax (also used inside Tyhp / tyhpdef). Majority of nodes (~86 files). |
| `Tyhp*` (not `Tyhpdef*`) | Tyhp-language extensions (~22 files): structs, extensions, generics identifiers, `nameof`/`typeof`/`default`, using-blocks, typed locals, etc. |
| `Tyhpdef*` | Declaration-only / import / stub surface for `.tyhpdef` files (~13 files). |
| Unprefixed / special | `Base2Ast`, `SrcFileAst`, `NodeListAst<>`, `TokenValueAst`, `ErrorAst`, `UnexpectedNodeAst`, `EmittedPhpExprAst`, `Base2AstExtensions`. |

### Inheritance sketch

```
IBase2Ast
  └── Base2Ast
        ├── SrcFileAst
        │     ├── PhpSrcFileAst
        │     ├── TyhpSrcFileAst
        │     └── TyhpdefSrcFileAst
        ├── NodeListAst<TChild, TSelf>
        │     └── PhpTopStatementListAst, PhpClassBodyAst, …
        ├── TokenValueAst
        │     ├── PhpNameAst
        │     │     └── TyhpGenericIdentifierAst
        │     └── PhpMagicConstantAst
        └── (all other concrete nodes)
```

Tyhp features often **reuse Php nodes** (e.g. `PhpFunctionDeclAst` also implements `IExtensionMemberAst`; `TyhpTypeAliasAst` implements both `ITopStatement` and `IClassMember`).

---

## 3. Core model: `Base2Ast` and `IBase2Ast`

### Storage model

Every node is a thin wrapper over a **fixed, serializable payload**:

| Field | Access | Serialized? | Notes |
|---|---|---|---|
| `Children` | protected `List<IBase2Ast?>`; public `AstChildren` | Yes | Positional slots; **nulls are meaningful** and preserved |
| `Flags` | protected `List<short>` | Yes | Booleans + enum-offset values |
| `Attributes` | protected list; public `AstAttributes` | Yes | PHP attributes; separate from children |
| `GrammarAddons` | protected dict; public `AstGrammarAddons` | Yes (trailing section) | Keyed extras from grammar-addon rules |
| `Identifier` | string | Yes | Names, labels, anon ids, etc. |
| `ValueString` / `ValueInt64` / `ValueDecimal` / `ValueBoolean` | optional scalars | Yes (bit-gated) | Literals, token text, parameter names, file hashes, … |
| `LanguageMode` | `"php"` / `"tyhp"` / `"tyhpdef"` / `""` | Yes | Derived from enclosing parse block |
| `Line` / `Column` / `StartIndex` | ints | Yes | From ANTLR `context.Start` when present |
| `EndLine` / `EndColumn` / `EndIndex` | ints | Yes | From ANTLR `context.Stop` when present; `EndColumn` is **exclusive** (one past last char of stop token), matching `IDiagnostic.EndColumn`; default `-1` when unknown |
| `DocComment` | string? | Yes | Attached at Create time from visitor docblock scan |
| `BoundSymbol` | `IBaseSymbol?` | **No** | Set by Binder; recreated each bind |
| `OwningFile` | `SrcFileAst?` | **No** | Set by Binder recursively on `AstChildren` |

Typed accessors on concrete classes are almost always:

```csharp
public SomeChild? Foo => Children.ElementAtOrDefault(N) as SomeChild;
```

There is **no parent pointer**. Downstream phases that need the file use `OwningFile` (after bind) or carry file context separately.

### Construction

- Nodes use **static `Create(...)` factories**, not public constructors (constructors are parameterless / protected for deserialization via `Activator.CreateInstance`).
- Factories take `ParserRuleContext context` and optional `languageMode`, then call `SetContext`.
- `SetContext(ParserRuleContext?)` copies line/column/start from `context.Start` when non-null (ANTLR recovery trees may omit `Start` — defaults stay `-1` rather than throwing).
- The same call copies end positions from `context.Stop` when the stop token has usable indices: `EndLine = Stop.Line`, `EndIndex = Stop.StopIndex`, and exclusive `EndColumn = Stop.Column + (Stop.StopIndex - Stop.StartIndex + 1)`. Missing/`-1` stop indices leave `End*` at `-1`.
- If `languageMode` is null, `SetContext` falls back to `TyhpParserAstVisitor.GetCurrentLanguageMode(context)`.
- Emitter/binder synthesis uses `internal static …CreateFromContext(..., Base2Ast context)` overloads that copy position/language (including end positions) from an existing node (no ANTLR context).

### Mutation APIs (internal)

`Base2Ast` exposes `ReplaceChild`, `ReplaceChildAt`, `AddChild`, `ClearChildren` as `internal` for emitter rewrites (`AstWalker.TransformTree`).

---

## 4. Source-file roots

`SrcFileAst` (abstract) stores:

- `Identifier` = absolute file path (or `"_"` / whitespace-preserving special cases)
- `ValueString` = content hash (string whose chars are also exposed as `FileHash` bytes)
- `FileName` = project-relative path via `AstCacheService.GetRelativePath`

Concrete roots:

| Type | Created for | Children |
|---|---|---|
| `PhpSrcFileAst` | `.php` (and default) | Sequence of `ISrcElement` (typically top-statement lists / inline output) |
| `TyhpSrcFileAst` | `.tyhp` | Starting inline output + code blocks + ending inline output |
| `TyhpdefSrcFileAst` | `.tyhpdef` | Same shape as Tyhp root (blocks + optional inline output) |

All three go through `SrcFileAst.AbstractCreate<TType>(...)`.

---

## 5. Node lists

`NodeListAst<TChild, TSelf>` is the CRTP-style base for homogeneous lists:

- `Create(IEnumerable<TChild?>?, context)` — by default **filters out nulls** (`FilterNullChildren => true`)
- `Wrap(item, context)` — if `item` is already `TSelf`, reuses it; otherwise wraps in a one-element list
- `GetAll()` / `GetAllNotNull()`

Important list types include:

`PhpTopStatementListAst`, `PhpStatementBlockAst`, `PhpClassBodyAst`, `PhpParameterListAst`, `PhpArgumentListAst`, `PhpExpressionListAst`, `PhpTypeExpressionListAst`, `PhpAttributeListAst`, `PhpImportDeclListAst`, `PhpConstDeclListAst`, `PhpPropertyListAst`, `PhpPropertyHookListAst`, `PhpConditionalArmListAst`, `PhpCatchListAst`, `PhpClassNameListAst`, `PhpTraitAdaptationListAst`, `PhpArrayPairListAst`, `PhpVariableListAst`, `PhpEncapsListAst`, `PhpInlineOutputListAst`, `TokenValueListAst`, `TyhpGenericsTypeArgumentListAst`, `TyhpStructPropertyListAst`, `TyhpExtensionFunctionListAst`, `TyhpdefPropertyListAst`, `TyhpdefConstDeclListAst`, `TyhpdefImportConstDeclListAst`.

`PhpTopStatementListAst` also implements `IStatementList<ITopStatement>` with mutating `Add` / `InsertAt` / `RemoveAt` used while the visitor builds the file.

**Contrast:** fixed-slot nodes (e.g. `PhpBinaryOpAst`, `PhpObjectTypeDeclAst`) keep **null children in place** so indices stay stable. Lists usually drop nulls.

---

## 6. Interfaces

Interfaces under `Ast/Interfaces/` are mostly **empty markers** used for typing visitor returns and child slots.

### Hierarchy (high level)

```
IBase2Ast
  ├── ITopStatement
  │     └── IStatement
  │           └── IExpression  (+ IClassNameReference, IEncapsVarOrString)
  │                 ├── IScalar
  │                 │     └── IDereferenceableBase (+ IExpression)
  │                 └── IDereferenceableSuffix
  ├── IClassMember
  ├── ITypeExpression
  ├── ISrcElement
  ├── IAttributedStatement (: ITopStatement, IStatement)
  ├── IExtensionMemberAst
  ├── ITraitAdaptation
  ├── ICaseItem
  ├── IForeachVariable (: IExpression)
  ├── IClassName (: IClassNameReference, IDereferenceableBase)
  ├── IClassNameReference
  ├── IClassMemberName
  ├── IMemberAccessor
  ├── IEncapsVarOrString
  ├── IArgumentList          ← defined but unused by concrete types (see Open Questions)
  ├── IUnexpectedNode        ← defined but not implemented by UnexpectedNodeAst
  └── IStatementList<TChild>
```

### Design consequences

1. **`IExpression : IStatement : ITopStatement`** — PHP allows expression statements; Tyhp typed locals (`TyhpTypedVarExprAst`) deliberately implement `IExpression` so they can appear in `for` init lists modeled as `PhpExpressionListAst`.
2. **Wide interface bags** — `PhpNameAst` implements `IExpression`, `IMemberAccessor`, `IClassName`, `IScalar`, `IClassMemberName`, `IDereferenceableBase` so one name node can fill many grammar slots.
3. **`ErrorAst` implements nearly every major interface** so recovery can substitute an error node without changing call-site types.

---

## 7. How nodes are built from the parser

### Visitor types

- `PhpParserAstVisitor : TyhpParserBaseVisitor<IBase2Ast?>` — PHP-oriented `Visit*` methods (partial classes by area: Root, Expressions, Statements, Objects, Types, …).
- `TyhpParserAstVisitor : PhpParserAstVisitor` — Tyhp / tyhpdef overrides and addons (partials: TyhpExpressions, TyhpObjects, Tyhpdef, TyhpExtensions, …).

Compilation always constructs **`TyhpParserAstVisitor`**, even for pure PHP files.

### Language mode

`PhpParserAstVisitor.GetCurrentLanguageMode(RuleContext?)` walks parents until it finds:

| Context | Mode |
|---|---|
| `TyhpdefBlockContext` / `TyhpdefTaglessFileContext` | `"tyhpdef"` |
| `TyhpBlockContext` / `TyhpTaglessFileContext` | `"tyhp"` |
| `PhpBlockContext` | `"php"` |
| File roots / null | `""` |

Mode is stamped onto each node at `SetContext` time and survives serialization.

### Doc comments

Visitors call `FindPossibleDocComment` **before** visiting declaration children. The scan walks the token stream’s `DocBlockCommentsChannel` backward from the declaration token, then advances `_docCommentLastStop` so nested declarations cannot steal an outer docblock. Absence is `null` (not `""`) so serialization stays identical to “never had a docblock.”

### Typical Visit → Create flow

```csharp
// Illustrative pattern used throughout visitors
return PhpBinaryOpAst.Create(
    TokenValueAst.Create(opToken, context),
    leftExpr,
    rightExpr,
    context,
    GetCurrentLanguageMode(context)
).WithAttributes(attributeList); // when applicable
```

Grammar-extension hooks often attach extras via `.WithGrammarAddon("key", …)` rather than expanding fixed child slots (see [Grammar addons](#10-grammar-addons)).

### Entry rules

| Extension | Parser entry (non-tagless) |
|---|---|
| `.tyhpdef` | `tyhpdefSrcFile()` |
| `.tyhp` | `tyhpSrcFile()` |
| `.php` / other | `phpSrcFile()` |

Tagless package mode uses `tyhpdefTaglessSrcFile()` / `tyhpTaglessSrcFile()`.

---

## 8. Conventions and patterns

### Create factories

1. Prefer `public static T Create(..., ParserRuleContext context, string? languageMode = null)`.
2. Assign `Children = [slot0, slot1, …]` with **stable indices**; document slot order in comments when non-obvious (`PhpParameterAst`: type, default, modifiers, property hooks).
3. Put names in `Identifier` and/or `ValueString` (parameters use `ValueString` for the `$name`).
4. Set flags after constructing the object.
5. Call `SetContext` last (or after flags — both patterns exist; language/position come from context either way).

### CreateError / CreateFromContext

- `CreateError(context, …)` — recovery placeholders (`Identifier` / `ValueString` often `"<error>"`, null child slots preserved).
- `CreateFromContext(...)` — **emitter synthesis** without a parse context; copies location from an existing `Base2Ast`.

### Anonymous declarations

Anonymous classes/structs get a generated `Identifier` like `anonClass@{guid}` / `anonStruct@{guid}` and a boolean flag (`IS_ANONYMOUS_CLASS_FLAG` / `IS_ANONYMOUS_FLAG`). The GUID is chosen at Create time and then **serialized**, so cache round-trips keep a stable id for that tree instance.

### Inheritance reuse

- `TyhpGenericIdentifierAst : PhpNameAst` — name plus `Children[0]` generic args/params.
- `PhpMagicConstantAst : TokenValueAst` — magic constants as token values.
- Tyhpdef import decls mirror Php decl shapes but are signature-only (no bodies) and carry deprecated/obsolete flags.

### Variable-length child layouts

Most nodes use fixed slots. Exceptions that pack a **variable prefix + trailing body/expression**:

- `TyhpUsingBlockAst` — N `TyhpUsingResourceAst` then body (`Body => Children.LastOrDefault()`).
- `TyhpUsingResourceAst` — optional type / variable then expression; presence tracked by flags `7200` / `7201`, and accessors compute indices from those flags.

---

## 9. Flags and enum offsets

Flags are packed into `List<short>`. Two conventions coexist:

### Boolean flags (negative or small positive constants)

Examples:

| Constant | Typical meaning |
|---|---|
| `-1` | anonymous class/struct; unary prefix; inline-output “echo” mode |
| `-3` | type expression `static` |
| `-4` | short array syntax |
| `-7` | match (vs switch) |
| `-8` | default arm / array expansion |
| `-9` | by-ref |
| `-10` / `-11` / `-12` / … | variadic, returns-ref, async, extension, parenthesized typed-var, arrow fn, … |
| `-14`, `-15` | method / property-hook returns-ref |
| `-20`, `-21` | tyhpdef deprecated / obsolete |
| `7100` | async using-block |
| `7200`, `7201` | using-resource has type / has variable |

### Enum offsets (positive thousands)

`SetFlag(offset, enumValue)` stores `offset + (short)enumValue`. Readers use `GetEnumFlags<TEnum>(offset)`, which selects shorts in `[offset, offset + 1000)`.

| Offset | Enum | Used by |
|---|---|---|
| 5000 | `PhpStringType` | `PhpEncapsListAst` |
| 7000 | `PhpLoopType` | `PhpLoopAst` |
| 8000 | `PhpScalarType` | `PhpScalarAst` |
| 9000 | `PhpStringType` | `PhpStringAst` |
| 11000 | `PhpTypeKind` | `PhpTypeExpressionAst` |
| 13000 | `PhpModifier` | `PhpModifierListAst` |
| 14000 | `PhpModifier` | `TyhpOperatorOverloadAst` |
| 15000 | `PhpModifier` | `PhpTraitAliasAst` (new visibility) |
| 16000 | `PhpJumpType` | `PhpJumpStatementAst` |

Enums live under `Tyhp/TyhpLang/Enum/` (`PhpLoopType`, `PhpScalarType`, `PhpTypeKind`, `PhpModifier`, `PhpJumpType`, `PhpStringType`, …).

**Why offsets:** one shared `Flags` list can hold many orthogonal enums without separate fields, and the binary format stays uniform.

---

## 10. Grammar addons

`GrammarAddons` is a `Dictionary<string, IBase2Ast>` for **optional / extension data** that should not blow up fixed child layouts. Serialized after Children when `reserved[0] == 0x01` (older caches with `0x00` still deserialize).

Fluent helpers: `WithGrammarAddon` / `AddGrammarAddon` in `Base2AstExtensions` / `Base2Ast`.

### Keys observed in Visitor / Binder / Checker / Emitter

| Key | Typical payload | Consumers (examples) |
|---|---|---|
| `identifier` | generics / renamed identifier AST | Binder, Checker, Emitter generics |
| `modifiers` | modifier list / async markers | Binder top-statements & tyhpdef, Checker async/closure |
| `parameters` | parameter-list addon | Checker extension rule |
| `typeName` | named/builtin type spelling | Checker/Emitter type spelling & generics |
| `memberName` | member identifier addon | TypeInferrer dereferenceables |
| `ctorReturnType` | `TyhpCtorReturnTypeAst` | Emitter declarations |
| `functionCall` | call-related addon | Visitor dereferenceables |
| `alias` | alias name | Visitor objects |
| `isAsync` | token/marker | Binder / visitor objects & tyhpdef |
| `isOverloadSignature` | token | Visitor Tyhp functions |
| `GenericArguments` / `GenericParameters` | generics lists | Tyhp top-statements / tyhpdef |
| `aliasedAs` / `aliasOf` / `nameOrAlias` | tyhpdef naming | Tyhpdef visitor / binder |
| `deprecatedOrObsolete` | marker node | Tyhpdef visitor |
| `typeExpr` | type on const list | Binder tyhpdef |

Addons are first-class serialized subtrees. **Binder’s `SetOwningFileRecursive` only walks `AstChildren`**, not addons (see Pitfalls).

---

## 11. Attributes

PHP `#[Attr]` lists become `PhpAttributeListAst` of `PhpAttributeAst` (`Children`: name, arguments). Visitors attach them with `node.AddAttributes(list)` / `.WithAttributes(list)`, which **copies attribute nodes into the target’s `Attributes` list** (not into `Children`).

Downstream:

- Binder resolves attribute class names via `AstAttributes`.
- Checker `AttributeRule` / `TyhpChecker` explicitly walk attributes (they are not in `AstChildren`).
- Emitter emits attributes from `AstAttributes`.

Attributes **are** included in binary serialization.

---

## 12. Serialization and cache

### Binary layout (per node)

Documented in detail in `Base2Ast` comments. Summary:

1. `int32` block size (includes itself)
2. `byte` NodeType
3. `byte` bit array (children / flags / attributes / doc / value fields)
4. `int64` CustomNodeType
5. `2` reserved bytes (`reserved[0]` = GrammarAddons present)
6. LanguageMode, Identifier (length-prefixed UTF-8 **byte** counts)
7. Line, Column, StartIndex, EndLine, EndColumn, EndIndex
8. Optional value sections gated by bits
9. Optional Flags, Attributes, Children (null child = block size `0`)
10. Optional GrammarAddons map

Strings are length-prefixed by UTF-8 **byte** length so readers can skip without decoding (`TryReadSrcFileKey`, lean cache probes).

`AstCacheService` namespaces on-disk blobs by `CacheFormatVersion` (currently `"4"`, which includes the end-position fields). Old format directories are never reused.

### Cache integration

- `AstCacheService.AddOrUpdate(SrcFileAst)` stores `node.Serialize()`.
- Hits validate with `TryReadSrcFileKey` (Identifier + ValueString hash) **without** constructing nodes, then full `Deserialize`.
- Only **error-free** parses are cached (user files and tyhpdefs). Partial recovery trees are never cached so diagnostics are not silently dropped on the next run.
- `BoundSymbol` / `OwningFile` are not in the blob; bind must re-run after deserialize.

### Non-serialized CLR properties

Anything that is a normal C# auto-property **outside** the Children/Flags/Values model is **lost on deserialize**. Known cases:

- `TyhpOperatorOverloadAst.ExtensionTargetType`
- `TyhpOperatorOverloadAst.IsInlineExtension`
- `EmittedPhpExprAst.PhpText` (emitter-only; not cache-backed)
- `ErrorAst.Context` / `UnexpectedNodeAst.Context`

See [Weirdness](#18-weirdness-with-evidence) and [Open Questions](#open-questions--needs-clarification).

---

## 13. Node type registry

`Tyhp.TyhpLang.Attributes.AstNodeTypeRegistry`:

- On `Initialize()`, scans the assembly for **non-abstract** subclasses of `Base2Ast`, orders by `FullName`, assigns sequential `byte` ids starting at `0`.
- Max built-in id is `0xFE`; `0xFF` is `CustomNodeTypeByte` for types not in the initial scan (hash of full name via SHA-256 folded to `int64`).
- Deserialization uses `Activator.CreateInstance` + registry lookup.

**Implication:** adding/renaming/moving a concrete AST class can **renumber NodeType ids** for all types after it in `FullName` order, invalidating on-disk AST caches. Treat cache format as coupled to the concrete type set + names.

`AstNodeTypeRegistryInitializer.Initialize()` is the startup wrapper.

---

## 14. Helpers and utilities

| Helper | Location | Role |
|---|---|---|
| `Base2AstExtensions.WithAttributes` / `WithGrammarAddon` | `Ast/` | Fluent attachment after Create |
| `Base2Ast.Serialize` / `Deserialize` / `TryDeserialize` / `TryReadSrcFileKey` | `Base2Ast` | Cache + integrity |
| `AstWalker.Walk` / `TransformTree` | `Emitter/AstWalker.cs` | Preorder walk; rewrite children via `ReplaceChild` |
| `DebugJson.SerializeAst` | `CLI/Support/DebugJson.cs` | `dump-ast` JSON (node type, fields, children, attributes, addons) |
| `AstCacheService` | `Domain/Services/` | Persist/load serialized roots |

There is **no** general AST visitor interface inside `Ast/` itself — traversal is ad hoc (`foreach AstChildren`) or via Emitter’s `AstWalker`.

---

## 15. Error recovery and special nodes

### `ErrorAst`

- Implements a large set of interfaces so it can stand in for almost any expected node.
- Stores message in `ValueString`, `MessageCode` in `ValueInt64`.
- `IsValid()` always returns `false`.
- Optional non-serialized `ParserRuleContext? Context`.

### Per-type `CreateError`

Used when a **specific** concrete type is required by the surrounding structure (e.g. `PhpObjectTypeDeclAst.CreateError`, `PhpMethodDeclAst.CreateError`, `PhpNameAst.CreateError`, `TyhpdefIdentifierAliasAst.CreateError`, `TyhpImportExtensionAst.CreateError`, `TyhpOperatorOverloadAst.CreateError`, …).

### `UnexpectedNodeAst`

Created when a visitor hits an unexpected alternative; implements `IExpression`, `IAttributedStatement`, `ISrcElement`, `ITypeExpression`. Keeps a non-serialized `Context`. Does **not** implement `IUnexpectedNode` (the interface exists unused).

### `EmittedPhpExprAst`

Emitter-synthesized `IExpression` holding raw `PhpText` for cases that cannot be expressed as ordinary AST (e.g. anonymous-class `with` wrappers in `WithKeywordHelper`). Not produced by the parser. `PhpText` is not part of the binary schema.

### `TokenValueAst`

Carries lexer token text (`ValueString`) and token type (`ValueInt64`). Basis for operators, names, magic constants. `CreateError` uses `"<error>"` / `-1`.

---

## 16. Php\* / Tyhp\* / Tyhpdef\* catalogs

### Php\* (shared syntax) — by concern

**Files / structure:** `PhpSrcFileAst`, `PhpTopStatementListAst`, `PhpNamespaceDeclAst`, `PhpBlockNamespaceDeclAst`, `PhpImportDeclAst` / `List`, `PhpHaltCompilerAst`, `PhpInlineOutputAst` / `List`, `PhpDeclareAst`.

**Declarations:** `PhpObjectTypeDeclAst`, `PhpClassBodyAst`, `PhpFunctionDeclAst`, `PhpMethodDeclAst`, `PhpParameterAst` / `List`, `PhpPropertyDeclAst`, `PhpPropertyAst` / `List`, `PhpPropertyHookAst` / `List`, `PhpConstDeclAst` / `List`, `PhpEnumCaseAst`, `PhpTraitUseAst`, trait adaptation nodes (`PhpTraitAliasAst`, `PhpTraitPrecedenceAst`, `PhpTraitMemberRefAst`, lists).

**Statements / control flow:** `PhpStatementBlockAst`, `PhpIfAst`, `PhpConditionalAst` / `Arm` / `ArmList`, `PhpLoopAst`, `PhpTryCatchAst`, `PhpCatchClauseAst` / `List`, `PhpReturnStatementAst`, `PhpJumpStatementAst`, `PhpGotoStatementAst`, `PhpLabelStatementAst`, `PhpEchoStatementAst`, `PhpGlobalStatementAst`, `PhpStaticStatementAst`, `PhpUnsetStatementAst`, `PhpNopStatementAst`, `PhpEmptyStatementAst`, `PhpIssetStatementAst`, `PhpEvalStatementAst` (file currently named `PhpEvalStatementAst copy.cs`).

**Expressions:** `PhpBinaryOpAst`, `PhpUnaryOpAst`, `PhpTernaryOpAst`, `PhpScalarAst`, `PhpStringAst`, `PhpArrayAst`, `PhpArrayPairAst` / `List`, `PhpVariableAst` / `List`, `PhpNewAst`, `PhpYieldAst`, `PhpInlineFunctionAst`, `PhpExpressionListAst`, `PhpMagicConstantAst`, `PhpNameAst`.

**Dereferenceables:** `PhpDereferenceableAst`, `PhpDereferenceableExpressionAst`, `PhpCallAst`, `PhpArgumentAst` / `List`, `PhpArrayAccessAst`, `PhpInstanceMemberAccessAst`, `PhpStaticMemberAccessAst`, `PhpMemberAccessAst`, `PhpClassConstantAccessAst`.

**Types:** `PhpTypeExpressionAst` / `List`, `PhpBuiltinTypeAst`, `PhpNamedTypeAst`, `PhpModifierListAst`, `PhpClassNameListAst`.

**Misc:** `PhpAttributeAst` / `List`, `PhpEncapsListAst`, `PhpEncapsStringAst`.

### Tyhp\* (language extensions)

| Node | Role |
|---|---|
| `TyhpSrcFileAst` | `.tyhp` root |
| `TyhpStructDeclAst` / `TyhpStructPropertyAst` / `List` | Struct decls (named + anonymous). `AliasOf` may be a quoted string or decimal integer key (`IsNumericAlias`); emitter erases to string vs int PHP array keys. |
| `TyhpExtensionDeclAst` / `TyhpExtensionFunctionListAst` | `extension Name extends T { … }` |
| `TyhpImportExtensionAst` | `use extension …` |
| `TyhpOperatorOverloadAst` | `operator` members (class / extension / tyhpdef inline) |
| `TyhpTypeAliasAst` | `type Alias = …` (file or class member) |
| `TyhpGenericIdentifierAst` | Name + generics |
| `TyhpGenericsTypeArgumentAst` / `List` | `<T extends U = V>` |
| `TyhpTypedVarExprAst` | `int $x = …` |
| `TyhpAsyncBlockAst` | `async { … }` Promise-valued block (not a callable) |
| `TyhpUsingBlockAst` / `TyhpUsingResourceAst` | `using` / `using await` |
| `TyhpNameofAst` / `TyhpTypeofAst` / `TyhpDefaultAst` / `TyhpVariableExistsAst` | Compile-time helpers |
| `TyhpReturnTypeGuardAst` | `: $x is T` return guards |
| `TyhpCtorReturnTypeAst` | `: void` / `: parent(...)` |
| `TyhpTemplateStringTypeAst` | Template string types in type position |

### Tyhpdef\* (stubs / imports)

| Node | Role |
|---|---|
| `TyhpdefSrcFileAst` | `.tyhpdef` root |
| `TyhpdefImportObjectDeclAst` | class/trait/interface/enum import shells |
| `TyhpdefImportFunctionDeclAst` | function signatures (+ async/extension/deprecated flags) |
| `TyhpdefImportConstAst` / `Decl` / `DeclList` | const imports |
| `TyhpdefImportVariableAst` | variable imports |
| `TyhpdefConstDeclAst` / `List` | const members inside import objects |
| `TyhpdefPropertyAst` / `List` | property name lists |
| `TyhpdefIdentifierAliasAst` | `Name as Alias` / `Class::Member as Alias` |
| `TyhpdefInlineExtensionFunctionAst` | wraps lowered `PhpMethodDeclAst` for `extension function` in tyhpdef bodies |

---

## 17. Interactions with Binder / Checker / Emitter

### Binder

- `SetOwningFileRecursive(srcFile, srcFile)` stamps `OwningFile` on the root and **every `AstChildren` descendant** (not Attributes / GrammarAddons / non-child CLR properties).
- Creating a symbol with a declaring node sets `declaringNode.BoundSymbol = this` (`BaseSymbol` ctor).
- Name resolution also assigns `BoundSymbol` on reference nodes (`NameResolver`).
- Reads GrammarAddons heavily for modifiers, generics, tyhpdef metadata; reads `AstAttributes` for attribute resolution.
- Operator overloads: binder inspects `ExtensionTargetType` / `IsInlineExtension` on the **live** tree and may copy the target type onto `ObjectOperatorOverloadMethodSymbol.PendingExtensionTargetType`.

### Checker

- Walks `AstChildren`; separately walks `AstAttributes` where needed (`TyhpChecker`, `AttributeRule`, declaration rules).
- Uses `BoundSymbol` for semantic facts; falls back to `OwningFile?.FileName` for diagnostic locations when checker state has no current file.
- Reads GrammarAddons (`typeName`, `identifier`, `memberName`, `modifiers`, …) during inference and rules.
- Must tolerate `ErrorAst` / `CreateError` placeholders (`IsValid() == false`).

### Emitter

- Pattern-matches concrete AST types; uses `AstWalker` for whole-tree transforms.
- May **mutate** children (`ReplaceChild`) and synthesize nodes via `CreateFromContext` / `EmittedPhpExprAst`.
- Reads GrammarAddons (`ctorReturnType`, `identifier`, `modifiers`, `typeName`).
- Emits from `AstAttributes` for attributes.

### Visitor (construction only)

Visitors own Create/Error/GrammarAddon attachment. They should not be treated as a second AST API — once Visit returns, the tree is owned by cache/binder/checker/emitter.

---

## 18. Weirdness (with evidence)

1. **`PhpEvalStatementAst copy.cs`** — The class is `PhpEvalStatementAst`, but the filename contains a space and `copy`. Likely an accidental duplicate-save; it still compiles as one type.

2. **`IExpression` extends `IStatement`** — Unusual vs typical compiler ASTs; matches PHP’s expression-statement grammar and enables typed locals in expression-list slots (`TyhpTypedVarExprAst` comment documents this).

3. **`PhpInlineOutputAst` implements `IStatementList<ITopStatement>`** — Echo-mode nodes lazily ensure `Children[0]` is a `PhpTopStatementListAst` inside the getter (mutation on read).

4. **Dual string-type offsets** — `PhpEncapsListAst` uses `5000`, `PhpStringAst` uses `9000`, both for `PhpStringType`.

5. **Non-serialized operator-overload fields** — `ExtensionTargetType` and `IsInlineExtension` are set by visitors after `Create` and are not Children/Flags. Cache deserialize would drop them unless something rehydrates them (binder currently reads them from the live tree; tyhpdefs that rely on these after a cache hit need verification — see Open Questions).

6. **`EmittedPhpExprAst.PhpText`** — Outside the serialize model; fine for ephemeral emitter nodes, dangerous if ever cached.

7. **`SetOwningFileRecursive` skips Attributes and GrammarAddons** — Attribute/addon subtrees may have `OwningFile == null` even after bind, while still being walkable via their own lists.

8. **`IArgumentList` / `IUnexpectedNode`** — Interfaces exist under `Interfaces/` but no concrete Ast class implements them (`PhpArgumentListAst` implements `IExpression` + `IDereferenceableSuffix` instead).

9. **`PhpNewAst` unused import** — File contains `using Microsoft.CodeAnalysis.CSharp.Syntax;` with no usages in that file.

10. **Anonymous GUID identifiers** — Stable after Create/serialize, but **re-parsing** the same source yields a new GUID (not content-addressed).

11. **Null children are load-bearing** — Fixed-slot nodes depend on `ElementAtOrDefault` + nulls; list nodes usually strip nulls. Mixing the two incorrectly breaks index assumptions.

12. **Registry id assignment by `FullName` sort** — Cache bytes embed `NodeType` bytes; renaming/adding types can shift ids and break old caches without a format version bump in the AST blob itself.

---

## 19. Pitfalls

1. **Do not put semantic state only in non-serialized properties** if the node must survive `AstCacheService` round-trips. Prefer Children, Flags, Values, Attributes, or GrammarAddons.

2. **Do not assume `OwningFile` on attribute or grammar-addon nodes** after bind. Use the owning declaration’s `OwningFile` or checker state.

3. **Do not walk only `AstChildren` and expect to see attributes.** Checker/Binder already special-case `AstAttributes`; new passes must too.

4. **Preserve child slot indices** when editing Create factories — Binder/Checker/Emitter access by position, not by name.

5. **Call `FindPossibleDocComment` before visiting children** of a declaration, or nested decls will consume the docblock cursor.

6. **Never cache trees that produced parse/visit errors** — existing services already enforce this; bypassing it drops diagnostics on cache hit.

7. **Adding a new concrete `Base2Ast` subclass** requires understanding registry renumbering / cache invalidation.

8. **`ErrorAst` is assignable to almost everything** — type checks like `is IExpression` do not prove the node is semantically valid; check `is ErrorAst` / `IsValid()` when needed.

9. **LanguageMode empty string vs null** — roots often use `""`; optional LanguageMode serializes empty as null-ish (length 0). Do not rely on distinguishing empty vs null after deserialize.

10. **Emitter `CreateFromContext` trees are not parse-faithful** — they exist for lowering; checkers should generally run before those rewrites (or ignore synthetic nodes).

---

## Open Questions / Needs Clarification

1. **Cache round-trip for `TyhpOperatorOverloadAst.ExtensionTargetType` / `IsInlineExtension`**  
   These fields are not in the binary schema. Are extension / tyhpdef-inline operator overloads always re-parsed in practice for the scenarios that need these flags, or is there another rehydration path not found in Ast/? Needs confirmation against real cache-hit compiles of files containing `operator +<T>(...)` and tyhpdef `extension operator`.

2. **Should `SetOwningFileRecursive` also visit `AstAttributes` and `AstGrammarAddons`?**  
   Current binder only walks `AstChildren`. Unclear if null `OwningFile` on attribute names has caused bugs or is papered over by always using the declaration’s file name.

3. **`IArgumentList` and `IUnexpectedNode`**  
   Appear unused. Intentional future hooks, leftovers, or incomplete refactors?

4. **`PhpEvalStatementAst copy.cs` filename**  
   Should be renamed for hygiene; confirm there is no second `PhpEvalStatementAst.cs` expected.

5. **Registry stability / cache versioning**  
   There is no explicit AST binary format version field beyond layout heuristics (`reserved[0]` for GrammarAddons). How production builds invalidate caches after NodeType renumbering is outside this folder (likely content hash + toolchain version elsewhere) — not fully traced here.

6. **Whether GrammarAddon keys are a closed set**  
   Keys are stringly typed across Visitor/Binder/Checker/Emitter. No central enum/constants file was found under `Ast/`.

7. **`UnexpectedNodeAst` vs `ErrorAst` policy**  
   When visitors choose one over the other is scattered; a single recovery policy doc was not found in Ast source.

---

## Quick reference: adding a new AST node

1. Add `MyFeatureAst.cs` under `Ast/` (or `Interfaces/` if only a marker).
2. Extend `Base2Ast` (+ interfaces for the slots it must fill).
3. Implement `public static MyFeatureAst Create(..., ParserRuleContext context, string? languageMode = null)` using Children/Flags/`SetContext`.
4. Wire `Visit*` in the appropriate `PhpParserAstVisitor` / `TyhpParserAstVisitor` partial.
5. Handle the type in Binder / Checker / Emitter as needed.
6. Expect `AstNodeTypeRegistry` to assign a new id (and possibly shift others) — invalidate AST caches.
7. If optional data does not fit fixed slots, use `GrammarAddons` with a stable string key and teach consumers to `TryGetValue`.
8. Do **not** rely on non-serialized auto-properties for anything that must survive cache.

---

## Related code (read-only cross-refs)

| Area | Path |
|---|---|
| Registry | `Tyhp/TyhpLang/Attributes/AstNodeTypeRegistry.cs` |
| Visitors | `Tyhp/TyhpLang/Visitor/` |
| Binder owning-file / bind entry | `Tyhp/TyhpLang/Binder/TyhpBinder.cs` |
| Cache | `Tyhp/Domain/Services/AstCacheService.cs` |
| Compile/parse orchestration | `Tyhp/Domain/Services/CompilationService.cs` |
| Tyhpdef parse/cache | `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` |
| Emitter walk/rewrite | `Tyhp/TyhpLang/Emitter/AstWalker.cs` |
| Debug dump | `Tyhp/CLI/Support/DebugJson.cs` |
| Flag enums | `Tyhp/TyhpLang/Enum/` |
