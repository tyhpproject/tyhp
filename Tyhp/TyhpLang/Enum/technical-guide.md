# TyhpLang Enum — Technical Guide

Shared compiler/language enumerations under `Tyhp.TyhpLang.Enum`. These types are consumed across AST, binder, checker, emitter, lexer, and CLI. Most files pair an enum with a small `*Helper` / `*Extensions` class that maps ANTLR token types (or strings) to enum values.

This guide is grounded in the sources under `Tyhp/TyhpLang/Enum/` and call sites elsewhere in the repo. Where an enum appears unused outside its defining file, that is called out explicitly.

## Conventions

| Pattern | What you see in this folder |
| --- | --- |
| Namespace | Always `Tyhp.TyhpLang.Enum` (brace style varies: some files use `namespace X {`, others `namespace X\n{`). |
| Flags | `[Flags]` only on `MemberModifier` and `ObjectModifier` (power-of-two values, `None = 0`). |
| Token mapping | Many enums expose `FromToken(IToken?)` and `FromToken(int)` returning the enum, `null`, or a sentinel (`None` / `Invalid`). |
| Naming | Most members are PascalCase. **Exception:** `BraceType` uses lowercase (`square`, `round`, `curly`). |
| Helpers | Named `*Helper` (`AccessorTypeHelper`, `MemberModifierHelper`, …) or `*Extensions` (`PhpBinaryOperatorExtensions`, …). No single naming rule — follow the neighboring file. |
| Php\* prefix | Token/AST-oriented classifications that mirror PHP grammar concepts (even when Tyhp extends them). |
| Non-Php names | Binder/checker/emitter domain concepts (`SymbolType`, `ScopeType`, `EmitType`, `UtilityBehavior`, …). |

**Dual modifier systems:** AST nodes typically store `PhpModifier` (including PHP 8.4 asymmetric visibility `PublicSet` / `ProtectedSet` / `PrivateSet`). Binder symbols and checker state use `[Flags] MemberModifier` (includes Tyhp-only `Async` and `Operator`). Conversion lives in `TyhpBinder.TopStatements.ConvertModifiers` and `CheckerHelpers.ToMemberModifiers`. Tyhp `async` is **not** a `PhpModifier` value — the visitor attaches an `isAsync` grammar addon; the binder ORs `MemberModifier.Async`.

---

## Catalog (by file)

### `AccessorType` (+ `AccessorTypeHelper`)

**Purpose:** Kind of property accessor hook: `Get`, `Set`, `Lazy`, `Guard`, `Isset`, `Unset`, plus `Invalid`.

**Where used:**
- `ObjectPropertySymbol.AccessorKind` (`AccessorType?`)
- `ObjectAccessorMethodSymbol.AccessorKind`

**Non-obvious:**
- `AccessorTypeHelper.FromToken` has **all** token arms commented out and always returns `Invalid`. The historical token names (`T_TYHP_PROP_ACCESSOR_*`) are left as comments.
- Binder currently sets `HasAccessor` from `prop.Hooks != null` (`TyhpBinder.ObjectBody`) but does not appear to construct `ObjectAccessorMethodSymbol` or assign `AccessorKind` from tokens in the current tree.

### `AsyncForeachKind`

**Purpose:** How `foreach (await $expr as …)` should be emitted (Story 11 Phase 9).

| Value | Meaning (from XML docs / checker) |
| --- | --- |
| `None` | Not await-foreach, or classification failed |
| `AsyncIterable` | `$expr` is `AsyncIterable<T>` → while-loop with `_await(next/current)` |
| `PromiseIterable` | `$expr` is `Promise<Iterable<T>>` → `foreach (_await($expr) as …)` |
| `PromiseAsyncIterable` | `$expr` is `Promise<AsyncIterable<T>>` → await then async-iterate |

**Where used:**
- Classified in `ControlFlowRule.Helpers` (checker)
- Stored/read via `EmitContext`
- Consumed in `TyhpEmitter.Async` / statement emit; `TyhpChecker` gates related checks
- Tests in `AsyncAwaitFinishEmitterTests`

**Non-obvious:** Emitter treats unclassified (`None`) await-foreach as `PromiseIterable` as a fallback (`TyhpEmitter.Async`).

### `BraceType`

**Purpose:** Bracket kind for the lexer’s nesting stack: `square`, `round`, `curly`.

**Where used:** Only `TyhpLexer` / `TyhpLexer.GrammarMethods` (`enterNesting` / `exitNesting`, curly-depth checks for string/heredoc modes).

**Non-obvious:** Lowercase member names (unlike every other enum here). Pure lexer concern — not on AST.

### `EmitType`

**Purpose:** Classification of an `EmitItem` in the emitter tree. **Numeric order matters:** `EmitItem.SortedChildren()` orders children by `(int)EmitType` then original index, so values define declaration/statement emission order inside a parent (e.g. trait uses before methods).

**Values (0 → 21, then sentinels):**

| Value | Intended role (from comments / usage) |
| --- | --- |
| `OutsideItems` | PHP blocks / inline HTML (e.g. `?>…<?php`) |
| `TyhpBlock` | Documented; **no current `EmitType.TyhpBlock` references found in Emitter** |
| `FileHeader` | File-level header (often empty root) |
| `FileDeclare` | Top-level `declare()` (non-block) |
| `FileNamespaceDeclaration` | File namespace line |
| `BlockNamespaceDeclaration` | `namespace X { … }` |
| `ImportUse` | `use` imports |
| `RootStatement` | Root decls / root-level code |
| `ObjectDeclaration` | Class/interface/enum/trait wrapper |
| `ObjectTraitUse` | Trait `use` inside a type |
| `ObjectConstantDeclaration` | Constants / enum cases |
| `ObjectStaticPropertyDeclaration` / `ObjectInstancePropertyDeclaration` | Properties |
| `ObjectConstructor` / `ObjectDestructor` | ctor/dtor |
| `ObjectStaticMethods` / `ObjectInstanceMethods` | Methods (and some overload emit) |
| `FunctionGlobalReference` | Documented as `global` imports in functions; **no current Emitter references found** |
| `FunctionStatement` | Statements inside functions/methods |
| `BlockDeclare` | `declare() { … }` |
| `SubBlockStatement` | Nested block statements |
| `OutputFileBlock` / `OutputFileStatement` | **Both equal `21`** (duplicate enum member values) |
| `Empty` | `Int32.MaxValue - 1` |
| `Group` | `Int32.MaxValue` |

**Where used heavily:** `TyhpEmitter.*`, `EmitItem`, `PHPOutputFile` (filters/reorder by emit type).

**Non-obvious:**
- `OutputFileBlock` and `OutputFileStatement` share the same underlying value; they are aliases, not distinct order slots.
- `Empty` / `Group` / `TyhpBlock` / `FunctionGlobalReference` / `OutputFile*` appear **unused as enum discriminators** in current Emitter code (confirm before relying on them). Factory method `EmitItem.Empty(...)` is unrelated — it creates an empty content item with some *other* `EmitType`.

### `MemberModifier` (+ `MemberModifierHelper`) `[Flags]`

**Purpose:** Binder/checker visibility and member flags.

| Flag | Bit | Source token / notes |
| --- | --- | --- |
| `None` | 0 | |
| `Public` / `Protected` / `Private` | 1/2/4 | Standard visibility |
| `Static` | 8 | |
| `Abstract` / `Final` / `Readonly` | 16/32/64 | |
| `Async` | 128 | `T_TYHP_ASYNC` |
| `Operator` | 256 | `T_TYHP_OPERATOR` |
| `Var` | 512 | `T_VAR` |

**Where used:** Nearly all binder symbols’ `Visibility`, checker modifier validation, `with` / readonly checks, method override rules.

**Non-obvious:**
- Not a 1:1 map of `PhpModifier` — no asymmetric `*Set` flags here.
- `MemberModifierHelper.FromToken` can produce `Operator` / `Async`; AST→symbol conversion often goes through `PhpModifier` lists plus the `isAsync` addon instead.

### `ObjectModifier` (+ `ObjectModifierHelper`) `[Flags]`

**Purpose:** Type-level modifiers: `Abstract`, `Final`, `Readonly`.

**Where used:** **No references outside `ObjectModifier.cs` found.** Object-level modifiers in practice flow through `MemberModifier` / `PhpModifier` on declarations.

**Non-obvious:** `FromTokens` uses `Aggregate` with confusing parameter names `(token, result)` but the lambda is `accumulator | FromToken(source)` — behavior is correct despite the names.

### `OverloadableOperator` (+ `OverloadableOperatorHelper`)

**Purpose:** Operators that Tyhp allows to be overloaded; drives checker rules, binder binding of overload methods, and emitter rewrite / method naming.

**Important value pairs:**
- Binary arithmetic: `Add`, `Subtract`, … vs unary: `Plus`, `Minus` (same `+`/`-` tokens; distinguished by `isAlternateKind`).
- `Convert`, `IsEmpty` — “word” operators (`convert`, `empty` as `T_STRING` or `T_EMPTY`).
- `Invalid` — unknown / unsupported token.

**Token mapping quirks (`FromToken`):**
- `T_SYM_PLUS` / `T_SYM_MINUS` → `Plus`/`Minus` when `isAlternateKind`, else `Add`/`Subtract`.
- `T_SYM_GT` → `BitwiseShiftRight` when `isAlternateKind`, else `CompareGreaterThan` (alternate path for `>>`-related parsing).
- Compound assignments: `FromAssignmentToken` maps `+=`, `-=`, … to the underlying binary overload (`Add`, `Subtract`, …), not a separate assign enum.

**Where used:** `OperatorOverloadRule`, `DeclarationRule.ObjectType`, `AliasConverter`, `TyhpEmitter.OperatorOverloads`, `OperatorMethodNameGenerator` (`Plus` → `__asNumeric`, `Minus` → `__negate`, `Add` → `__add`, etc.), binder object body.

### `ParseMode`

**Purpose:** Which grammar entry point / language mode to parse: `Php`, `Tyhpdef`, `Tyhp`.

**Where used:** Built-in tyhpdef loading (`Tyhpdef.cs`, package loading), CLI debug/tokenize/integrity (`DebugCommandSupport`, `TokenizeAction`, `DumpAstAction`, `TyhpdefCheck`).

**Related but distinct:** `SrcFileType` (same three concepts as a `short` enum) — see below; do not assume they are interchangeable in code today.

### `PhpAccessType` (+ extensions)

**Purpose:** Kind of dereference/access: `ArrayAccess`, `PropertyAccess`, `StaticPropertyAccess`, `MethodCall`, `StaticMethodCall`.

**Where used:** **No references outside `PhpAccessType.cs` found.**

**Non-obvious:** `FromToken` only maps `[`, `->`, `?->`, `::` → array/property/static-property. `MethodCall` / `StaticMethodCall` are never produced by the helper (call parentheses are not part of this mapping).

### `PhpAssignmentOperator` (+ extensions)

**Purpose:** Assignment operators including Tyhp `UsingEqual` (`T_TYHP_USING_EQUAL`).

**Where used:** Emitter (`TyhpEmitter.Expressions` / Helpers / Statements), disposable/`using` analysis (`DisposableRule`, `CheckerHelpers`, `NullSafetyRule`).

**Non-obvious:** `Assign` and `UsingEqual` are often treated as the same “plain assignment” family for disposable tracking and emit.

### `PhpBinaryOperator` (+ extensions)

**Purpose:** Binary operators on expressions (arithmetic, comparison, logical, bitwise, concat, coalesce, `instanceof`).

**Where used:** Expression inferrer / operator checker paths, AST binary ops via token mapping.

**Non-obvious:** `T_TYHP_IS` (`is` / `isa` / … aliases) maps to `InstanceOf`, same as `T_INSTANCEOF` (comment references grammar addon). Returns `null` for unknown tokens (nullable API).

### `PhpBuiltinType` (+ `FromString`)

**Purpose:** Enum of PHP built-in type keywords (`Array`, `Callable`, `String`, … `Iterable`, including `True`/`False`/`Null`).

**Where used:** The **enum** itself has **no call sites outside its file** in the current tree. Built-in types in the pipeline are represented as `PhpBuiltinTypeAst` with a string `Identifier`. `FromString` is available but unused by callers found so far.

### `PhpJumpType` (+ extensions)

**Purpose:** `None`, `Break`, `Continue`, `Return`, `Goto`.

**Where used:** `PhpJumpStatementAst.JumpType`; control-flow checker (`ControlFlowRule`).

### `PhpLoopType` (+ extensions)

**Purpose:** `None`, `While`, `DoWhile`, `For`, `Foreach`.

**Where used:** `PhpLoopAst` factory helpers; control-flow / emit loop dispatch.

### `PhpModifier` (+ extensions)

**Purpose:** AST-level modifiers including asymmetric visibility: `PublicSet`, `ProtectedSet`, `PrivateSet`, plus `Var`, visibility, `Static`, `Abstract`, `Final`, `Readonly`.

**Where used:** Modifier lists on AST (`PhpModifierListAst`, operator overload AST), emitter spelling (`public(set)` etc.), binder conversion to `MemberModifier` (asymmetric set modifiers currently collapse to `MemberModifier.None` in `ConvertModifiers`’s default arm).

**Non-obvious:** No `Async` / `Operator` values — those are Tyhp-only on `MemberModifier`.

### `PhpNameType` (+ extensions)

**Purpose:** Name form: `Unqualified`, `Qualified`, `FullyQualified`, `Relative` (from `T_STRING` / `T_NAME_*`).

**Where used:** **No references outside `PhpNameType.cs` found.**

### `PhpScalarType` (+ extensions)

**Purpose:** Literal scalar kinds: `Integer`, `Float`, `OctalNumber`, `HexNumber`, `BinaryNumber`, `String`.

**Where used:** `PhpScalarAst`; type inference, narrowing, emit formatting, alias conversion (int-like vs float vs string).

### `PhpStringType` (+ extensions)

**Purpose:** String literal / encaps kinds: `SingleQuoted`, `DoubleQuoted`, binary variants, `Heredoc`, backquotes.

**Where used:** `PhpStringAst`, `PhpEncapsListAst`; emitter quote choice.

**Non-obvious:** `FromToken` maps `T_CONSTANT_ENCAPSED_STRING` → `SingleQuoted` (PHP uses that token for both quote styles in some lexer paths — treat the mapping as lexer-contract-specific).

### `PhpTypeDeclType` (+ extensions)

**Purpose:** Object declaration kind: `Class`, `Interface`, `Trait`, `Enum`.

**Where used:** Object symbols (`ObjectKind`), name resolution, declaration rules, nameof / symbol-name existence, generic constraints. Structs are often a **flag** on the class-like symbol (`IsStruct`), not a fifth `PhpTypeDeclType` value.

### `PhpTypeKind` (+ extensions)

**Purpose:** Shape of a type expression: `Simple`, `Union`, `Intersection`, `Invalid`.

**Where used:** `PhpTypeExpressionAst.TypeKind`; spelling (`|` / `&`) in binder/emitter.

**Non-obvious:**
- `FromToken` returns `Union` for `|`, `Intersection` for `&`, and **`Simple` for everything else** (including unknown tokens) — despite a nullable return type, it does not return `null`.
- `Invalid` is set explicitly on AST error paths (`SetFlag(..., PhpTypeKind.Invalid)`), not via `FromToken`.

### `PhpUseType` (+ extensions)

**Purpose:** Import kind: `Class`, `Function`, `Const`, `Variable`.

**Where used:**
- Binder import binding / `UseIncludeSymbol`
- `ImportRule` (duplicate/unused import)
- `NameResolver` (class-use filtering)
- Tyhpdef variable aliases use `PhpUseType.Variable` (`TyhpBinder.Tyhpdef`)

**Non-obvious:** `FromToken` defaults **unknown** tokens to `Class` (not `null`). Call sites often re-derive type from the import’s keyword string (`"function"` / `"const"`) instead of the extension method.

### `ScopeType`

**Purpose:** Coarse scope kind for binder scopes and checker `CheckerState.Split(...)`: `Root`, `File`, `Namespace`, `NamespaceBlock`, function/method/object variants, `Statement`, `CodeBlock`, `DeclareBlock`, `Label`, `Unknown`.

**Where used:** Scope construction; checker state splitting for nested analysis; mapped from `SymbolType` via `SymbolTypeHelper.GetScopeType`.

**Non-obvious:** Many `SymbolType` values (e.g. most leaf members) map to `Unknown` if not listed in `_scopeTypeBySymbolType`.

### `SrcFileType` (`: short`)

**Purpose:** `Php = 0`, `Tyhp = 1`, `Tyhpdef = 2`.

**Where used:** **No references outside `SrcFileType.cs` found.** Prefer `ParseMode` for actual parse dispatch today.

### `SymbolType` (+ large `SymbolTypeHelper`)

**Purpose:** Discriminator for every binder symbol kind — the richest enum in this folder (~file-level documentation comments describe uniqueness/parent rules).

**Major groups:**
- Structure: `Root`, `File`, `Namespace`, `NamespaceBlock`, `CodeBlock`, `DeclareBlock`, `Statement`, `Label`
- Imports / emits: `UseInclude`, `IncludeTag` (output_file include tag)
- Types / builtins: `BuiltInType`, `BuiltInUtilityType`, `BuiltInFunction`, `MagicConstant`, `TypeAlias`, generics (`ClassGenericTypeParameter`, `FunctionGenericTypeParameter`)
- Callables: `FunctionDeclaration`, `AnonymousFunctionDeclaration`
- Objects: `ObjectTypeDeclaration`, `AnonymousObjectDeclaration`, members (constants, properties, methods, accessors, ctor/dtor, operator overloads, PHP magic methods)
- `Variable` (locals, params, globals, etc.)

**`SymbolTypeHelper` responsibilities:**
- Map `SymbolType` → `ScopeType` (`GetScopeType`)
- Scope predicate sets (`IsFileScope`, `IsInstanceMethodDeclarationScope`, …)
- **Allowed children** per scope (`GetAllowedChildren`) — used to validate symbol tree shape (`AllowMultiple` flag per child kind)
- Instance vs static method scope lists include specific magic methods; `ObjectOperatorOverload` is treated as **static** method scope; `ObjectMagicSetStateMethod` / `ObjectMagicCallStaticMethod` are static; most other magics are instance

**Non-obvious:**
- Comments on enum members document **identity / uniqueness** conventions (case sensitivity, parent, FQN shape) — treat them as binder design notes, not enforced solely by the enum.
- `AllowedChildrenByScope` does not list every `ScopeType` (e.g. anonymous object uses method-style fallbacks only when instance/static method predicates match; otherwise empty).
- `Root` may contain builtins, files, namespaces, anonymous types/functions, variables.

### `TypeVariance`

**Purpose:** Generic parameter variance: `Invariant`, `Covariant`, `Contravariant`.

**Where used:**
- Stored on `GenericTypeParameterSymbol.Variance`
- Consumed in `TypeComparer.Subtyping` when comparing generic type arguments
- Array/`iterable`-like assignability forces **covariant** argument checking even without declared variance

**Non-obvious:** No binder assignment to `.Variance` was found in the current tree (defaults to `Invariant = 0`). Variance-aware comparison still runs off the property and the array-like special case.

### `UtilityBehavior`

**Purpose:** Dispatch key for built-in `\Tyhp\…` utility / symbol-name / type-name-algebra types. Values are intended to match utility type names for `UtilityTypeResolver`.

**Groups (as commented in source):**
1. Classic utilities: `Readonly`, `Partial`, `Required`, `Pick`, `Omit`, `Record`, `Exclude`, `Extract`, `NonNullable`, `Nullable`, `ReturnType`, `Parameters`, `Awaited`
2. Symbol-name brands (erase to string): `TyhpInternal`, `VarName`, `TypedVarName`, `FunctionName`, `ClassName`, …
3. Struct/type utilities: `StructKey`, `StructRecord`, `Properties`, `TypeDiff`, `AsNotNullable`, `CallableReturnType`, `CallableParametersStruct`, `CallableParametersTuple`, `CallableParametersRest` (Story 16.5; callable-keyed, one type argument)
4. Type-name string algebra: `BaseTypeName`, `UnionTypeName`, `AsType`, …

**Where used:** Registered in `Binder/BuiltIn/{UtilityTypes,SymbolNameTypes,StructUtilityTypes,TypeNameAlgebraTypes}.cs` onto `BuiltInUtilityTypeSymbol`; resolved in `UtilityTypeResolver`, `TypeNameAlgebraResolver`, `SymbolNameExistenceVerifier`, `NameofTypeInferrer`, `TypeNarrowingRule` (`*_exists` maps), emit spelling (`TypeSpellingHelper`).

---

## Cross-cutting relationships

```text
ParseMode ──► lexer/parser entry (Php / Tyhp / Tyhpdef)
SrcFileType   (same three labels; currently unused)

PhpModifier (AST) ──ConvertModifiers──► MemberModifier (symbols/checker)
ObjectModifier        (flags for types; currently unused)

SymbolType ──GetScopeType──► ScopeType ──CheckerState.Split──► nested checking
         └──GetAllowedChildren──► binder tree shape

OverloadableOperator ◄── FromToken(isAlternateKind) / FromAssignmentToken
PhpBinaryOperator / PhpAssignmentOperator ── expression AST ops (broader than overloads)

UtilityBehavior ── BuiltInUtilityTypeSymbol ── UtilityTypeResolver
AsyncForeachKind ── checker classify ── emitter await-foreach
EmitType ── EmitItem tree sort order ── PHP text layout
BraceType ── lexer nesting only
```

---

## Open Questions / Needs Clarification

1. **Dead or future enums:** `SrcFileType`, `ObjectModifier`, `PhpAccessType`, `PhpNameType`, and the `PhpBuiltinType` enum (vs `PhpBuiltinTypeAst`) have no external usages found. Intended for upcoming work, leftover from earlier designs, or safe to remove?
2. **`AccessorTypeHelper`:** When will token mapping be restored, and who assigns `ObjectAccessorMethodSymbol.AccessorKind`?
3. **`TypeVariance`:** Is variance syntax/binder population planned, or is everything invariant except array-like covariance in the comparer?
4. **`EmitType` sentinels / unused values:** Are `Empty`, `Group`, `TyhpBlock`, `FunctionGlobalReference`, and the `OutputFile*` aliases still part of the emit plan?
5. **`MemberModifier.Operator`:** Is this flag set on overload symbols today, or only available via `FromToken`?
6. **`PhpModifier` asymmetric set → `MemberModifier`:** Conversion drops `*Set` to `None` — is that intentional until asymmetric visibility is modeled on symbols?
