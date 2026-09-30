# Tyhp Binder — Developer Technical Guide

This guide explains how the Tyhp **Binder** works: its place in the compilation pipeline, data structures, two-pass binding flow, conventions, and how it interacts with the rest of TyhpLang. Everything here is grounded in the source under `Tyhp/TyhpLang/Binder/` and the call sites that invoke it.

> Related local notes: the older `readme.md` in this folder is an early design sketch (scope taxonomy, open questions). Prefer this guide + the current `.cs` sources for behavior. Tyhpdef loading details live in `BuiltIn/README.md` and `BuiltIn/TYHPDEF_DISTRIBUTION.md`.

---

## 1. Overview / purpose

The Binder turns a list of parsed ASTs (`SrcFileAst`) into a **scope/symbol tree** rooted at `GlobalScope`. It does **not** type-check assignability or emit PHP. Its jobs are:

1. **Register declarations** — files, namespaces, classes/interfaces/traits/enums/structs/extensions, functions, members, `use` imports, variables, labels, declare blocks, etc.
2. **Link declarations to AST** — `BaseSymbol` constructors set `IBase2Ast.BoundSymbol`; the resolution pass also sets `BoundSymbol` on type-name / attribute-name nodes via `NameResolver.RecordResolution`.
3. **Resolve type-name references** — after all declarations exist, walk scopes and bind type expressions (`extends`/`implements`, parameter/return/property types, generic constraints/defaults, attributes, extension operator targets, tyhpdef class-body `use extension` paths, file-level `use extension` / `global use extension` names) to declaring symbols.
4. **Load external type information** — built-in scalars/utilities plus `.tyhpdef` packages, before user files are bound.

Pipeline position (from `CompilationService.ParseFiles`):

```
Parse (parallel) → Bind (single-threaded) → Check → (later) Emit
```

Binding is skipped for a source file when that file produced parse errors. Other files that parsed still bind. Checking runs when bind produced a non-null `GlobalScope` unless `SkipChecking` is set; bind errors do not skip check. The checker wraps the bound `GlobalScope` in a `SymbolTree` and continues to use `NameResolver` for member/extension lookups. `tyhp symbol_tree` runs this bind (`SkipChecking`) and dumps the resulting declarations as JSON.

Design intent for the two-pass shape (declaration then full-tree resolution) is also documented in `IMPLEMENTATION_PLAN_TODO_STORY_31.md` (proposed deferred-resolution optimization; **not** implemented in current `TyhpBinder.Bind()`).

---

## 2. Entry points and invocation

### Primary API

```csharp
public partial class TyhpBinder
{
    public TyhpBinder(DiagnosticBag diagnostics, CompilationOptions? compilationOptions = null);
    public GlobalScope? Bind(IReadOnlyList<SrcFileAst> parsedFiles);
    public NameResolver? NameResolver { get; } // set after resolution pass
}
```

`Bind()` (`TyhpBinder.cs`):

1. Rejects null/empty `parsedFiles`.
2. Creates `GlobalScope`, calls `PopulateBuiltIns`.
3. Calls `LoadTyhpdefSymbols()` (tyhpdefs bound into the same global tree).
4. **Pass 1:** `BindFile` for each user AST.
5. **Pass 2:** `RunResolutionPass()`.
6. Returns `_globalScope` (never throws for recoverable binder logic; unexpected exceptions become `BinderUnknownError` diagnostics).

### Compilation pipeline call site

`CompilationService.BindParsedFiles` constructs the binder with the shared `DiagnosticBag` and `CompilationOptions`, then stores the result on `CompilationResult.GlobalScope`:

```743:751:Tyhp/Domain/Services/CompilationService.cs
        private static GlobalScope? BindParsedFiles(
            IReadOnlyList<SrcFileAst> parsedFiles,
            DiagnosticBag diagnostics,
            CompilationOptions options)
        {
            try
            {
                var binder = new TyhpBinder(diagnostics, options);
                return binder.Bind(parsedFiles);
```

Checker integration immediately after:

```766:777:Tyhp/Domain/Services/CompilationService.cs
        private static void CheckParsedFiles(CompilationResult result, CompilationOptions options)
        {
            try
            {
                var symbolTree = new SymbolTree(result.GlobalScope!);
                var checker = new TyhpChecker(
                    result.Diagnostics,
                    symbolTree,
                    result.GlobalScope!,
                    options.Checker);
                checker.Check(result.ParsedFiles ?? Array.Empty<SrcFileAst>());
```

### Tests

`tests/Tyhp.Tests/Binder/BinderTests.cs` exercises binding through `CompilationService.ParseFiles` with `SkipChecking = true`. Fixture sources live under `tests/Tyhp.Tests/TestData/ValidTyhp/binder/`.

### Threading

Comments and Story 02 / Story 10 plans state binding is **single-threaded**. Symbol lazy fields note that `??=` is not thread-safe because the binder is assumed single-threaded. Parsing may be parallel; binding is not.

---

## 3. Folder / file map

```
Binder/
├── TyhpBinder.cs                 # Bind(), PopulateBuiltIns, BindFile, OwningFile walk
├── TyhpBinder.TopStatements.cs   # namespaces, objects, functions, imports, consts, structs, helpers
├── TyhpBinder.ObjectBody.cs      # methods, properties, consts, enums, traits, operators
├── TyhpBinder.CodeBlocks.cs      # statement/code-block scopes, closures, using, global/static
├── TyhpBinder.Extensions.cs      # extension decls + bind-only synthetic __TyhpInlineExt_* scopes
├── TyhpBinder.Tyhpdef.cs         # tyhpdef AST binding + package tracking hooks
├── TyhpBinder.Extern.cs          # tyhpdef extern merge (real-wins), kind-mismatch, 3026/3027 helpers
├── TyhpBinder.Overlays.cs        # overlay last-wins, omit, partial, stamp checks
├── TyhpBinder.Resolution.cs      # Pass 2: ResolveInScope, unresolved diagnostics + DidYouMean
├── TyhpdefSymbolRegistrar.cs     # ordered tyhpdef bind + cross-package FQN conflict tracking
├── SymbolTree.cs                 # GlobalScope wrapper + extension-method index + resolver factories
├── SymbolIdentifier.cs           # thin namespace-path + name carrier (used by SymbolTree)
├── Resolution/
│   ├── NameResolver.cs           # name/type/member/extension/self/parent resolution
│   └── InScopeNameCandidates.cs  # candidate names for DidYouMean suggestions
├── Scopes/                       # GlobalScope, FileScope, Namespace*, Object*, Method*, CodeBlock*, …
├── Scopes/Interfaces/            # marker parent/child interfaces for typed Add*ChildScope
├── Symbols/                      # BaseSymbol + concrete declaration symbols
├── Symbols/Interfaces/           # which symbol kinds may live in which scopes
├── BuiltIn/                      # hardcoded builtins + tyhpdef load/parse pipeline
└── TyhpBuiltIn/Tyhpdef.cs        # embedded keyed tyhpdef content (loaded by BuiltIn.Tyhpdef)
```

### `TyhpBinder` partial split

| File | Responsibility |
|------|----------------|
| `TyhpBinder.cs` | Entry, builtins, file scope, `SetOwningFileRecursive`; calls `ResolvePendingFileUseExtensions` after Pass 1 |
| `TyhpBinder.TopStatements.cs` | Top-level statement dispatch; namespaces; object/function/struct decls; imports; declare; generics/modifiers helpers; **declares** `partial void` hooks |
| `TyhpBinder.ObjectBody.cs` | Implements `BindObjectBody`; members; promoted ctor params; magic method typing; traits |
| `TyhpBinder.CodeBlocks.cs` | Implements `BindFunctionBody` / `BindStatementBlock`; nested decls; using/global/static |
| `TyhpBinder.Extensions.cs` | Standalone `extension` decls, file-level `use extension` pending resolve, synthetic inline extension class |
| `TyhpBinder.Tyhpdef.cs` | Tyhpdef-specific declaration binding and overload merging |
| `TyhpBinder.Extern.cs` | `extern` real-wins merge for types / functions / consts, kind-mismatch, illegal-form, inheritance diagnostics |
| `TyhpBinder.Overlays.cs` | Overlay last-wins, `omit`, `partial`, stamp compatibility |
| `TyhpBinder.Resolution.cs` | Pass 2 resolution orchestration |

Three **partial methods** connect declaration walk pieces without circular file dependencies:

- `BindObjectBody` — declared in TopStatements, implemented in ObjectBody
- `BindFunctionBody` — declared in TopStatements, implemented in CodeBlocks
- `BindStatementBlock` — declared in TopStatements, implemented in CodeBlocks

### Scope types (`Scopes/`)

| Type | Declaration symbol | Role |
|------|-------------------|------|
| `GlobalScope` | `NoSymbol` | Root; indexes file + namespace scopes; hosts builtins |
| `FileScope` | `FileSymbol` | One per source file; un-namespaced decls; file `declare`s; `use` |
| `NamespaceScope` | `NamespaceSymbol` | Shared namespace container across files |
| `NamespaceBlockScope` | `NamespaceBlockSymbol` | Per-file contribution under a namespace (isolates `use`) |
| `ObjectDeclarationScope` | `ObjectDeclarationSymbol` | Class/interface/trait/enum/struct/extension body |
| `FunctionDeclarationScope` | `FunctionDeclarationSymbol` | Free function |
| `InstanceMethodDeclarationScope` / `StaticMethodDeclarationScope` | method symbols | Method bodies |
| `CodeBlockScope` | `CodeBlockSymbol` | `{ }`, if/loop/try/match/using, nested blocks |
| `DeclareBlockScope` | `DeclareBlockSymbol` | Block-form `declare(...) { }` / `enddeclare`; stores `php` constraint stack + inactive flag |
| `AnonymousFunctionScope` | `AnonymousFunctionSymbol` | Closures |
| `AnonymousObjectDeclarationScope` | (anonymous class) | Anonymous class body parentage |
| `LabelScope` | `LabelSymbol` | `goto` label targets |

### Symbol types (`Symbols/`)

Major families:

- **Containers:** `FileSymbol`, `NamespaceSymbol`, `NamespaceBlockSymbol`, `CodeBlockSymbol`, `DeclareBlockSymbol`, `NoSymbol`
- **Types:** `ObjectDeclarationSymbol`, `AnonymousObjectDeclarationSymbol`, `TypeAliasSymbol`, `ObjectTypeAliasSymbol`, `BuiltInTypeSymbol`, `BuiltInUtilityTypeSymbol`, `GenericTypeParameterSymbol`
- **Callables:** `FunctionDeclarationSymbol`, `AnonymousFunctionSymbol`, `ObjectMethodSymbol` (+ constructor/destructor/magic/operator/accessor subclasses), `BuiltInFunctionSymbol`
- **Members/locals:** `ObjectPropertySymbol`, `ObjectConstantSymbol`, `VariableSymbol`, `ConstantSymbol`, `ParameterInfo` (record, not a scope child by itself), `LabelSymbol`
- **Imports / misc:** `UseIncludeSymbol`, `MagicConstantSymbol`, `SuperGlobalSymbol`

Magic methods are **separate symbol classes** (e.g. `ObjectMagicGetMethodSymbol`) selected by `DetermineMethodSymbolType` from the method name (`__get`, `__construct`, …).

---

## 4. Core data structures

### `BaseSymbol`

Shared fields: `Name`, `FullyQualifiedName`, `DeclaringAstNode`, `ContainingScope`, `SymbolType`, visibility/deprecation/doc/source location (`Line`, `Column`, `EndLine`, `EndColumn`). `IsDeprecated` is true when the tyhpdef `deprecated` keyword is on the declaration **or** when `#[\Deprecated]` / `#[Deprecated]` is on the declaring AST (name match; the engine class may be gated out below PHP 8.4). A string-literal `$message` on that attribute is stored as `DeprecatedMessage` for TYHP4500. `IsInternal` is set when the declaration used Tyhp `internal` (`ApplyInternal` / `HasInternalModifier`); it is a generation boundary (compiled-library tyhpdef omits like `private` via `SymbolExportVisibility.OmitFromPublicTyhpdef`), not a PHP visibility. Combining `internal` with `public`/`protected`/`private` is existing TYHP4002. `IBaseSymbol` exposes `DeclaringAstNode` as well as the identity and location fields. `EndLine`/`EndColumn` are copied from the declaring AST node (`IBase2Ast.EndLine` / exclusive `EndColumn`); they are `0` when no declaring node is provided and `-1` when the AST node itself has no end span.

On construction, if a declaring AST node is provided, **`declaringNode.BoundSymbol = this`**.

`FullyQualifiedName` and `ContainingScope` are filled when the symbol is added to a scope (`BaseScope.AddChildSymbol` / `TryAddChildSymbol` / `AddChildScope`). Only `NamespaceSymbol` segments contribute to the namespace path; `FileScope` and code blocks are transparent for FQN computation (PHP-like).

### Scope tree + three PHP name indexes

`BaseScope<...>` maintains:

1. Typed `_childScopes` list
2. `_additionalChildScopes` — children that cannot be stored in the typed list because of **C# generic invariance** (see §8)
3. Child symbols + **three name indexes** mirroring PHP symbol tables:

| Index | Comparer | Symbol kinds |
|-------|----------|--------------|
| `_constantSymbolIndex` | ordinal (case-sensitive) | `Constant`, `MagicConstant`, `ObjectConstant` |
| `_functionSymbolIndex` | ordinal ignore-case | `FunctionDeclaration`, `BuiltInFunction` |
| `_childSymbolIndex` | ordinal ignore-case | class-likes, methods, properties, variables, `use`, etc. |

`FindChildSymbolByName` checks constants → class-likes → functions. That prevents `HASH_HMAC` colliding with `hash_hmac`, and prefers class `Decimal` over function `decimal` when both exist.

**Operator overloads** (`SymbolType.ObjectOperatorOverload`) bypass by-name uniqueness: multiple overloads share a name and are found by enumeration + signature matching later.

### `ObjectDeclarationSymbol` member maps

Separate from the scope child list:

- `Members` — methods, properties, object type aliases (case-insensitive; properties keep `$` prefix)
- `Constants` — class constants / enum cases (case-sensitive)

`RegisterObjectMember` keeps these maps in sync after successful `AddChildSymbol`. Operator overloads are intentionally **not** registered in `Members`.

Also stores inheritance/trait/extension metadata: `ExtendsType`, `BackingType` (enum scalar only; not `extends`), `ImplementsTypes` (interfaces **and** used traits — trait `use` / `implements` name-list items are `IClassName`/`PhpNameAst`, wrapped as `PhpNamedTypeAst` via `AsTypeExpression` so they fit the `ITypeExpression` list), `TraitMethodPrecedence` / `TraitMethodAliases`, extension auto-activation lists, synthetic inline extension pointer, etc.

### `SymbolTree`

Post-bind wrapper used by the checker (and optional convenience APIs):

- Holds `GlobalScope` + optional `SymbolIdentifier`
- Lazily builds `ExtensionMethodIndex` (method name → list of extension `ObjectMethodSymbol`s from `IsExtension` classes)
- Factory methods create ephemeral or reusable `NameResolver` instances

### `NameResolver`

Stateful resolver with `_resolvedSymbols` (`AST → symbol`) and writes through to `BoundSymbol`.

Key operations:

| Method | Behavior |
|--------|----------|
| `ResolveSymbol` | Lexical walk up scopes; expands `use`; special-cases FileScope for namespace blocks; stops variable lookup at function/method/closure boundaries. Optional `accept` predicate skips a hit and keeps walking (type-position uses `IsTypePositionSymbol` so `Type::bool()` does not bind as the `bool` type) |
| `ResolveQualifiedName` | Absolute `\A\B\C` via `NamespaceScope` + block child search; single-segment uses `SearchGlobalNamespace` |
| `ResolveRelativeName` | Class-`use` prefix expansion, then current namespace, then global |
| `ResolveType` | Builtins, named types, unions/intersections (resolves all components; **returns first**), type guards, template-string types → `string` builtin. Applied type arguments (`array<T>`, `Foo<T>`, the `typeName` addon) are resolved in the same scope, so a nested `extends<T>` group's parameters and method generics bind inside the instantiation. A name already bound to a `GenericTypeParameterSymbol` keeps that binding |
| `ResolveMember` / `ResolveStaticMember` / `ResolveConstant` | Inheritance + traits + interfaces with adaptation rules. Enumerations also search engine `\UnitEnum` (and `\BackedEnum` when backed) so `cases` / `from` / `tryFrom` resolve without a written `implements`. See **Inherited member lookup** below. |
| `ResolveSelfStaticParent` | `self`/`static`/`parent` → enclosing object symbol (binder identity). Checker keeps bare `static` as a distinct late-bound type and bans `static<…>` (TYHP4168); see Checker technical guide / `tyhp_0150_newTypes.md`. |
| `ResolveGenericTypeParameter` | Walks enclosing function/method/object `GenericParameters` lists (method shadows class). While resolving a type-alias body, `GenericAliasParameters` supplies that alias's parameters (`type Alias<T> = …` at file or class level) |
| `ResolveExtensionMethod` | Auto-activated tyhpdef extensions, else indexed/scan search; matches first parameter type. Optional `callSiteScope` supplies the calling file so file-local `use extension` `hide` / `insteadof` / method `as` apply on built-in receivers (`string`, `array`, …) whose declaring scope is global. When several activated extensions all match, prefer this file's declared extensions, then file-local `use extension`, then `global use` (ties keep index order). `FindCallSiteScope` locates that file from the AST or a lexical scope. Receiver matching canonicalizes FQNs (`\int` vs `int`) and builtin names so checker singletons and literal underlying types match `extends int` / `extends string`. The index re-resolves that first parameter from `GlobalScope`. A block-target method's synthesized `$this` reuses the header or group target AST, which a namespaced relative name (`namespace App; extends Box`) does not resolve from global. When that re-resolution misses and the first parameter is that synthesized `$this`, the match uses the block's `ExtensionBlockTargetSymbol` instead. |
| `FindCallSiteScope` | Nested lexical scope (function / method / class) when provided. `FileScope` and `GlobalScope` are not the current namespace — namespace blocks are siblings of the file under global — so an optional current-namespace name selects this file's `NamespaceBlockScope`. That is what makes unqualified `twice()` in a `namespace App` top-level statement resolve `App\twice` the same way a method body already did. Falls back to the file, then the namespace, then global. |
| `ResolveAttributeClassName` | Soft resolve for attributes (missing `\Override` etc. allowed) |

#### Inherited member lookup

`ResolveInheritedMember` / `ResolveInheritedConstant` walk parent, traits, and interfaces.
A tyhpdef interface's `extends` list is a `PhpClassNameListAst` on `Extends` (source
interfaces store that list in `Implements`, which the binder copies into
`ImplementsTypes`). Method lookup and constant lookup both resolve those base
interfaces through `CollectResolvedImplementsAndUsedTraits` and search them with
the same walk, so a method or constant declared on an extended interface is
visible on the child.

A used trait's method wins over an inherited method of the same name. The parent
method is kept and used only when no trait defines that name, so a parent method
the trait does not declare stays visible. Properties and constants still resolve
on the parent before traits.

`insteadof` and method `as` look up the chosen trait with a new `visited` set
that contains only the class being resolved. The parent walk's set is unchanged,
so a trait already entered while resolving the parent can still supply the
child's adaptation. The ordinary trait search keeps the shared set.

Inherited `__call` / `__callStatic` are deferred until after this class's used
traits and interfaces are searched. A concrete trait or interface method wins.
A magic symbol counts as that fallback only when the looked-up name is not
`__call` / `__callStatic` itself. Magic dispatch order when nothing concrete
matches: this class's own hook, then a used trait's hook, then the ancestor hook.

- Each public `ResolveMember` / `ResolveStaticMember` / `ResolveConstant` / `TryResolveObjectTypeAlias` entry allocates a fresh per-walk `visited` set so diamond inheritance does not loop.
- Nested re-entry while resolving a parent or implements type shares an in-flight `(object, member, kind)` stack on the `NameResolver` instance. PHP allows a class `\Foo\Bar` and a namespace `\Foo\Bar` at once, so a type name `\Foo\Bar\Baz` is first tried as class-level alias `Baz` on `\Foo\Bar`. If that walk is already in flight for the same object, member, and kind, the lookup returns null. `ResolveNamedType` then uses `ResolveQualifiedName` / `ResolveRelativeName` for the nested type.
- Member names on the in-flight key compare `OrdinalIgnoreCase` (`InheritedLookupKeyComparer`), matching PHP method/property lookup and `ObjectDeclarationSymbol.Members`. Object identity and kind stay exact; constants use a distinct kind from instance and static members.
- A true `A extends B extends A` heritage cycle still terminates (unresolved member; circular inheritance is `BinderCircularInheritance`). Linear chains are capped at `MaxInheritanceDepth` (100).

### Generics on symbols (not always scope children)

`PopulateGenericParameters` / `PopulateGenericParametersFromGrammarAddon` append `GenericTypeParameterSymbol` instances to the owning symbol’s `GenericParameters` list (class/function/method/type alias). Resolution of those names goes through `NameResolver.ResolveGenericTypeParameter`, **not** primarily through `FindChildSymbolByName` on the object/function scope. Extension members use the same addon (`AstGrammarAddons["identifier"]`): standalone `extension { function foo<T>(…) }` via `BindExtensionFunctionDecl` / `BindExtensionMethodDecl`, and class-body tyhpdef `extension fn foo<T>(…)` via `BindTyhpdefInlineExtensionFunction`.

Constraint/default AST nodes are resolved in Pass 2 via `ResolveGenericParameterConstraints`. The checker later fills `GenericTypeParameterSymbol.ResolvedConstraint`.

### `ParameterInfo`

Immutable-ish record attached to function/method symbols for signature metadata. Parameters are **also** registered as `VariableSymbol` children of the method/function scope (`IsParameter = true`).

---

## 5. Binding flow walkthrough

### High-level sequence

```
Bind(parsedFiles)
  PopulateBuiltIns(global)
  LoadTyhpdefSymbols()          // BindTyhpdefSourceFile → BindFile for each tyhpdef
  foreach user file:
    BindFile(srcFile)
      TryAddFileScope
      SetOwningFileRecursive     // every AST node.OwningFile = this SrcFileAst
      BindTopStatementList
  ReportReservedPhp86Names()    // TYHP4372 on Tyhp-facing names that remain after overlays
  ResolvePendingFileUseExtensions()  // file-level / global `use extension` after all decls exist
  RunResolutionPass()
    NameResolver(global)
    ResolveInScope(each FileScope / NamespaceBlockScope subtree)
```

### Pass 1 — declaration

#### File and top statements

`BindTopStatementList` maintains a **current scope**. Statement-form `namespace Foo;` (no braces) switches `currentScope` for subsequent siblings until another namespace (PHP semantics). Block namespaces bind their body into a new `NamespaceBlockScope`.

`BindTopStatement` dispatches: namespaces, object types, functions, imports, constants, declare (`BindDeclare` — file-level vs block `php` gates), type aliases, typed vars, tyhpdef import ASTs, extension decls, structs, nested statement lists, and otherwise `BindStatementBlock` for executable top-level statements. When `FileSymbol.IsPhpVersionGateInactive`, non-declare top statements are skipped.

#### Namespaces

`BindNamespaceDeclCore`:

1. `GlobalScope.AddNamespaceScope(name)` — reuses existing namespace by normalized name
2. Creates a **new** `NamespaceBlockScope` under that namespace (one block per declaration occurrence / file contribution)
3. Binds nested top statements into the block (if present)

Cross-file uniqueness for functions/classes/constants/type aliases is enforced in `NamespaceBlockScope.TryAddChildSymbol` / `FileScope.TryAddChildSymbol` by scanning sibling blocks/files in the same PHP symbol namespace. A rejected add returns the sibling's existing symbol so the duplicate diagnostic can label that other file.

#### Object declarations

`BindObjectTypeDecl`:

1. Builds `ObjectDeclarationSymbol` (kind from `class`/`interface`/`trait`/`enum`)
2. Captures `extends` / `implements` AST references (unresolved until Pass 2). For enums, also copies `BackingType` (`enum Status: int`) — a distinct AST slot from `Extends`.
3. Populates class generics from grammar addon `"identifier"` → `TyhpGenericsTypeArgumentListAst`. A repeated parameter name is `BinderDuplicateGenericParameter` (TYHP3011) with a `declared here` label on the first parameter; the duplicate is not registered.
4. Adds symbol + `ObjectDeclarationScope` under File or NamespaceBlock
5. Calls `BindObjectBody`

Anonymous classes use `IObjectDeclarationScopeParent.AddObjectDeclarationChildScope` (may land in `_additionalChildScopes` under a code block).

`BindObjectBody`:

- Injects `self`/`static`/`parent` builtins into the object scope (`Types.PopulateObject`)
- Skips class method **overload signatures** (`OverloadSignatureHelper`) — only implementations bind
- Methods → instance or static method scope + parameter vars + body
- Properties, object consts, enum cases, operators, object type aliases, nested named structs (`type Name = struct { }`), trait uses

`BindEnumCase` marks `ObjectConstantSymbol.IsEnumCase` and stores the case's `= value` expression on `ValueExpression` (null when the case is unbacked). Compiled-library `package.tyhpdef` generation spells that expression as the case's `BackingValue`.

Constructor parameter promotion creates both a parameter `VariableSymbol` and an `ObjectPropertySymbol` keyed with `$` prefix. Promoted `PropertyHooks` get the same hook flags as class-body properties.

Property hook flags on `ObjectPropertySymbol` (Tyhp and tyhpdef, via `ApplyPropertyHookFlags`):

- `HasAccessor` is `hooks != null` (a hook list is present; invalid names stay a checker problem, existing 4006).
- Walk the list: `HasGetHook` / `HasSetHook` from hook names `get` / `set`; `GetHookReturnsRef` when that `get` has `ReturnsRef` (`&get`).
- `AccessorKind` is `Get` or `Set` when exactly one of those hooks is present; both hooks leave it unset (no `Both` enum value).
- Tyhpdef hooks are bodyless — do not bind `ObjectAccessorMethodSymbol` for them.

Traits: trait type expressions are appended to `ImplementsTypes`; adaptations fill `TraitMethodPrecedence` / `TraitMethodAliases`.

#### Functions

Skips erasable overload signatures. Creates `FunctionDeclarationSymbol` + `FunctionDeclarationScope`, then `BindFunctionBody` (parameters as variables + return type AST + body). After the body is bound, `IsGenerator` is set when the body contains a `yield` / `yield from` that belongs to this callable (`BodyContainsYield` — skips nested functions/closures/methods). Methods get the same flag after their body bind in `TyhpBinder.ObjectBody`.

Nested named functions inside statement blocks are handled both in `BindStatementBlock` (`PhpFunctionDeclAst` arm) and `BindCodeBlockChildren` (declaration-before-`IStatement` order) — historically a bug when nested decls were walked as plain statements (`FOUND_BUGS` #36; comments in CodeBlocks/TopStatements).

#### Type aliases

File-level `type Alias = …` creates a `TypeAliasSymbol` in the class-like name index (so it still collides with a class of the same name). Source aliases also occupy the PHP **function** namespace (`TryOccupyFunctionNamespace`); `type Foo` + `function Foo()` in the same namespace is `BinderTypeAliasConflictsWithFunction` (TYHP3028). Tyhpdef file-level aliases stay type-only (no function occupancy). Names that cannot be PHP functions (`echo`, `list`, `empty`, …) are `BinderTypeAliasReservedFunctionName` (TYHP3030) at file level; class-level aliases emit as methods and may use those names. A class-kind `use App\Types\UserId` binds the alias for type position; call-site `UserId()` resolves through that same import (no second `use function` in Tyhp source).

Class-level `public type Row<T> = …` copies modifiers onto `ObjectTypeAliasSymbol` and `PopulateGenericParameters` the same way as file-level aliases — except `type Name = struct { }`, which binds as a nested named struct (`ObjectDeclarationSymbol { IsStruct = true }`) instead. `self\Alias` / `ClassName\Alias` resolve to that member in `NameResolver.TryResolveObjectTypeAlias` (type annotations via `ResolveNamedType`, `new`, and `is` / `instanceof` RHS names via the checker calling the same helper). When that helper returns null — no such alias or nested struct, or the in-flight inherited-lookup stack declined re-entry — `ResolveNamedType` continues with the qualified or relative type name, so a nested class under the same prefix as a class FQN still binds. Unqualified type names never bind to a method/property/constant: `ResolveNamedType` walks with `IsTypePositionSymbol` (builtins, class-likes, `TypeAliasSymbol`, `ObjectTypeAliasSymbol`, generic parameters). A class that declares `static fn bool(): self` does not turn a `bool $flag` annotation into `\Ns\bool`. Pass 2 sets `GenericAliasParameters` while resolving an alias body so `T` inside `type Row<T> = array<string, T>` binds. After resolution, `DetectCircularTypeAliases` reports `BinderCircularTypeAlias` (TYHP3029) when **source** `.tyhp` alias bodies form a cycle, except when every participant's RHS resolves to an object shape — directly (`type Node = object { public function parent(): ?Node; }`), nested in a union/intersection, or reached through a bare rename or generic instantiation of another alias that does (`type Foo = Box;` / `type Foo = Container<Foo>;`, where `Box`/`Container<T>` are shape aliases). A cycle where some participant never bottoms out at a shape (e.g. mixed with a plain nominal alias) still errors. Embedded `<tyhpdef:…>` helpers and `.tyhpdef` aliases are not diagnosed (the checker still guards expansion with a visiting set).

An object-shape alias (`type Clock = object { … }` or `type Log = Logger & object { … }`) keeps a `TyhpObjectShapeAst` as the alias target (or as an intersection item). There is no `ObjectDeclarationSymbol` / PHP FQN for the shape. Bind walks member signatures so recursive names resolve. Empty `object {}` is TYHP4349; `private`/`protected`/`internal` members are TYHP4350. `new` / `::` / `extends` / `implements` / `::class` treat the alias as not a class (checker TYHP4347), including a rename or generic wrapper of a shape used as `new`. Any other `type` alias used as `new` is TYHP4069 — the alias name is not a class even when its RHS is.

`DetectCircularTypeAliases` (post name-resolution) reports `BinderCircularTypeAlias` (TYHP3029) for any dependency cycle among file-/class-level aliases — except a cycle where every participant ultimately resolves to an object shape (`type Node = object { public function parent(): ?Node; }`, or a chain reaching a shape through a bare rename / generic instantiation). `ObjectShapeSupport.AliasResolvesToObjectShape` is the single source of truth for "does this alias resolve to a shape" — both this cycle exemption and the checker's `new`/`::` shape-used-as-class diagnostics call it, so a chain that satisfies one satisfies the other. A cycle that never bottoms out at a shape (`type A = B; type B = A`, or a union like `type B = A|int`) still errors. Callable-shape aliases (`type Mapper = callable(int $i): string`, including a rename of another callable alias) are detected by `ObjectShapeSupport.AliasResolvesToCallableShape` for LSP hover; they stay `TypeAliasSymbol` / `ObjectTypeAliasSymbol` and are not constructable. Struct-shape aliases (`type Point = struct { … }`) bind as `ObjectDeclarationSymbol { IsStruct = true }` and remain constructable (`new` + `with`).

#### Imports (`use`)

Become `UseIncludeSymbol` on File or NamespaceBlock with `UseType` Class/Const/Function and pre-split `ImportedNameSegments`. A second `use` of the same alias reports `BinderDuplicateUseAlias` (TYHP3008) with a `declared here` label on the first import. Colliding with a real type/function/const of that name is still `BinderDuplicateSymbolDeclaration` (TYHP3002).

#### Code blocks / control flow

`BindStatementBlock` creates nested `CodeBlockScope`s for statement blocks, if/loop/try/match, declare blocks, labels, anonymous functions, typed vars, using blocks, `global`/`static`, and recursively walks other statement trees.

Variable lookup during later resolution **stops** at function/method/anonymous-function scopes for `$names` (then only global-scope superglobals).

#### Extensions

- Standalone `TyhpExtensionDeclAst` and tyhpdef `TyhpdefStandaloneExtensionDeclAst` → `ObjectDeclarationSymbol` with `IsExtension = true`, static methods and operators. Empty `extension Name {}` is a checker error (TYHP4172). A Tyhp name ending with `GeneratedNames.ExtensionBackerSuffix` (`__tyhpExtensionBacker`) is TYHP4171.
- Tyhpdef `partial class` / `enum` / `interface` / `trait` (include load): additive members on an existing type; duplicate member → TYHP8002; missing target → TYHP8014.
- Overlay tyhpdefs (`extra.tyhp.package.overlay` / `tyhp.json` `"overlay"` globs): loaded after includes, in array order (lexicographic within a glob). Last wins by **Tyhp name**. `omit` removes a symbol (TYHP8017 if used in include; TYHP8020 if the name is missing). Overlay `partial` merges members with last-wins replace (TYHP8019 if the type is missing; TYHP8030 and skip if the target `IsExtern`). The first overlay declaration of a member name replaces the harvested member; later same-name **methods** in that overlay body append to `ObjectMethodSymbol.Overloads` (same as free-function overlay overloads), so `bind` / `bindTo` two-signature overlays do not report `BinderDuplicateSymbolDeclaration`. Overlay `partial class Foo<T>;` (header-only semicolon form) is overlay-only (TYHP8034 in include): written generics / `extends` / `implements` / enum `BackingType` / `as` replace those clauses; omitted clauses and unlisted members stay; attributes merge; a bare `partial class Foo;` with no keep-for-alias warns TYHP8035. Overlay `partial function` (free function or method) is name-only attribute merge onto the current overload set (TYHP8031 if the name is missing; TYHP8032 if used in include; TYHP8033 if a method sits outside an overlay `partial` type). Match is the current Tyhp name. Optional `as` registers a new Tyhp name with the same `OriginalPhpName`; the old Tyhp name is removed at end of overlay file unless that file also kept it (`partial function X;`). Overlay replace/`omit` also evicts the Layer 1 `_phpVersionGatedDeclarations` record so a replacement with an overlapping `#[\Tyhp\Php]` gate is not 4303 (same as ungated last-wins). An unsatisfied `#[\Tyhp\Php]` on the overlay declaration itself skips stamp / replace / partial / omit (`ShouldSkipPhpVersionGatedOverlay`) so TYHP8021 / TYHP8031 are not reported when Layer 1 was also gated off. `// @overlay-against:` mismatches are TYHP8021 (error under `--strict`). Comparison looks up Layer 1 by the **PHP original name** (`ResolveLayer1StampKey`): overlay `function php_name as tyhpName` is registered under `tyhpName`, which is not in Layer 1, so the stamp must match `php_name`'s compact signature. Layer 1 overload sets (several `__construct` signatures, several free-function overloads) are captured under one key; 8021 succeeds when the authored stamp matches **any** compact Layer 1 overload, not only the primary `Members` / first-function spell. An unqualified overlay identifier (`partial interface LoggerInterface` inside `namespace Psr\Log`) is resolved to the bound Layer 1 FQN (`\Psr\Log\LoggerInterface`) when `existing` is that same PHP short name, matching `CaptureStamps` keys. A name that already contains `\` (`function \call_user_func as …`) is used as-is and is never replaced by a Tyhp `as` alias FQN. Member stamps are keyed `Type::kind:member` (`TyhpdefOverlayStamp.MemberKey`, kind `method` / `property` / `object-const`) so `Logger::debug()` and `Logger::DEBUG` do not last-wins overwrite. Top-level stamps are `kind:FQN` (`function` / `type` / `const` / `variable`) so `class Phar` and `const PHAR` stay distinct. `TyhpdefOverlayStamp.Normalize` strips generic arguments and variadic `...` so `mixed ...$args` matches Layer 1 `mixed $args`. Compact method stamps omit visibility. Name-only stamps (`function foo`) on `partial function` skip 8021 when the target was found. Unstamped incompatible replaces are TYHP8022 (`TyhpdefOverlayStamp.IsCompatibleReplace` / `IsCompatibleParamType`: the live symbol **or** any captured Layer 1 overload stamp may satisfy the check, so a prior Layer 2 stub replace does not make a Layer 1-compatible hand overlay warn. Layer 1 empty or `mixed` parameters accept any overlay type; an exact spelled match — generics stripped — or a static-value literal overlay also pass. When the overlay parameter type is a function/method generic with an `extends` constraint, that constraint is substituted and compared with the same rules, so `TArray extends array<…>` is a compatible rewrite of Layer 1 `array`, while `T extends string` against `array` is not. Unconstrained generics are not auto-accepted. By-ref uses the same type path; return types and compile-only attributes (`#[\Tyhp\NativeTypeTest]`, other `NoEmit`) are not part of this check). TYHP8022 applies only to the first overlay declaration of a Tyhp name in that overlay file (free function) or overlay type body (method) — that declaration is the replace. Later same-name overlay functions/methods append as overloads (`_overlayReplacedFunctionNames` / `_overlayReplacedMemberNames` already contain the name) and skip 8022 even when unstamped; an authored `@overlay-against:` on a later overload still runs the 8021 stamp compare. Unstamped `partial function` does not run 8022. `partial` / `omit` / `deprecated` / `obsolete` cannot be combined on the same declaration (TYHP8018). If a visitor path still sets `extern` together with any of those four, the same TYHP8018 fires (`extern` is not counted in that four-flag combination). Overlay `omit` of an `extern` type uses the existing omit path. Overlay full replace of a real type onto an `extern` placeholder is real-wins (clears `IsExtern`); overlay `extern` onto a real type is a no-op. `@overlay-against:` on an **`extern` declaration** is a no-op (no Layer 1 member list; do not treat the placeholder as a hollow class that must match members). Layer 1 still records a compact **type-header** stamp for an include `extern` so a later full overlay replace of a real type can match `@overlay-against:` against that header without a false TYHP8021 (`TryConsumeTyhpdefExternMerge` keeps `existing` on real-wins for that lookup). `tyhp overlay stamp` skips `extern` declarations (`TyhpdefOverlayStampRewriter`) and writes name-only `function {name}` stamps for `partial function`. The rewriter re-parses the overlay file without binding, so it cannot resolve a generic overlay parameter (e.g. `DatePeriod::__construct(TDate $start, ...)`) back to the Layer 1 overload it matches; when a member/function key holds several Layer 1 overloads, it writes the primary (first) overload's stamp only when the declaration is unstamped or its current stamp does not already match one of the recorded overloads — an already-correct per-overload stamp (`DatePeriod`'s three `__construct` overloads) is left untouched rather than clobbered with the primary on every rerun. `tyhp overlay create` refuses an `extern` target (TYHP7904) and does not copy the placeholder into a hollow `class { }` overlay.
- Overlay / include `partial` (and overlay last-wins / `omit`) locate the target with `FindExistingObjectType` / `FindExistingTyhpdefSymbol`. Those helpers search the current contribution **and** sibling scopes in the same PHP symbol namespace: sibling `FileScope`s for un-namespaced types (`partial class PDO`), and sibling `NamespaceBlockScope`s under the same `NamespaceScope` for `namespace Foo { partial class Bar }`. Each tyhpdef file still gets its own block via `BindNamespaceDeclCore`. A `#[\Tyhp\Php]`-gated type that is inactive for `output.phpVersion` is not registered. Overlay `partial` of that name is skip-before-bind without TYHP8019 (`HasInactiveGatedDeclaration`) — the target exists in Layer 1, it is just inactive for this compile. A misspelled overlay still warns TYHP8019. When the type is visible, namespaced `partial` applies.
- `global use` / `global use function` / `global use const` → `GlobalScope.GlobalImports` (and still the file/namespace `use` table). `global use extension` → `GloballyActivatedExtensions`. Local unmutated `use` of a globally included symbol is a checker warning (TYHP4169).
- File-level `use extension` / `global use extension` (`BindFileUseExtension`) **stashes** the import AST during Pass 1 and resolves in `ResolvePendingFileUseExtensions` after every tyhpdef and user file has been declared. `FindExtensionSymbol` then sees forward-declared `extension { }` blocks in the same file (or a later file). A name that still does not resolve reports `TyhpdefExtensionNotFound` (TYHP8011). Hide / `insteadof` / `as` bookkeeping (`ProcessExtensionUseAdaptations`) runs in that same deferred step so `hide` of an unknown member (TYHP4173) is validated against the resolved extension, not an empty list.
- `use extension` adaptations: postfix `hide` fills `ExtensionUseHiddenMembers`; `insteadof` fills precedence; `as` on an operator reference is TYHP4170. Hide of an unknown member is TYHP4173. File-local adaptations are stored on `FileScope` (or `GlobalScope` for `global use`); `ResolveExtensionMethod` consults them via the call-site scope, not the receiver type's declaring scope.
- Block target (`extensionTargetType` on the header, or on each nested `extends Type { }` group with `IsTargetGroup`). Pass 1 stores that AST on `PendingExtensionBlockTarget`. `extension Name<T>` and `extends<T>` fill that symbol's `GenericParameters`. A group is a compiler-generated `ObjectDeclarationSymbol` (`IsExtensionTargetGroup`) nested under the extension scope so its type parameters are in scope for the group's target and member signatures (`array<TKey, TValue>`, `callable(…): T`, and the same names in a tyhpdef `extends<T> Type { }` group). A method generic with the same name is a separate symbol on the method; generic lookup finds the method parameter first and still keeps the block parameter. Members are published on the enclosing extension (`Members`, and `ObjectMethodSymbol.Overloads` when that name is already taken) so every group is visible to `ResolveExtensionMethod`. When a header target and nested groups are both present, header members use the header target and each group uses its own.
- Pass 2 resolves the block target to an `ObjectDeclarationSymbol` or a `BuiltInTypeSymbol` and stores it on `ExtensionBlockTargetSymbol`. A `TypeAliasSymbol` or `ObjectTypeAliasSymbol` is followed through `ObjectShapeSupport.GetAliasedType` (same alias body other alias sites read) and that body is resolved again, so `type Str = string` and `type Maybe = ?string` both store the `string` builtin. The written target node stays the alias; `$this`'s declared type is that node, and the checker expands it (`?string` stays nullable). A union target, including an alias of a union, is a legal checker target. Pass 2 still stores that union's first resolved member on `ExtensionBlockTargetSymbol`; method applicability uses the whole union. An alias whose body is not a class or builtin is not TYHP3016 — the name was found — and is left for the checker. Each operator in that header or group is appended to the target's `ExtensionContributedOperators`; `ExtensionTargetSymbol` is the same symbol. `self` in a member signature or operand type resolves to that target. A targeted method synthesizes a `$this` receiver (first `ParameterInfo`, plus a `$this` variable in the method scope) whose type is the block target. `ResolveExtensionMethod` matches that first parameter against the receiver, and when the synthesized type does not re-resolve from `GlobalScope` it matches `ExtensionBlockTargetSymbol` on the header or nested group. `static` and `parent` still follow the enclosing object (`FindEnclosingObjectScope` skips target-group scopes).
- An unresolvable block target reports `ExtensionOperatorTargetNotFound` (TYHP3016) on that type and is not contributed. A non-instantiable builtin (`void`, `never`, `null`, `mixed`, `resource`, `true`, `false`) that is the target of an operator in the same header or group reports `ExtensionOperatorTargetNotInstantiable` (TYHP3025) on that type; those operators are not contributed. A block target on a symbol that is not an extension or a target group, or a per-operator target type outside an extension, reports `ExtensionOperatorTargetNotAllowed` (TYHP3015) on that type.
- Inline tyhpdef class-body `extension fn` / `extension operator` → bind-only synthetic class `__TyhpInlineExt_{OwnerName}` via `GetOrCreateSyntheticInlineExtensionScope`, linked with `SyntheticInlineExtension` / `InlineExtensionReceiverClass`. The scope is auto-activated (`IsCompilerGenerated`) and **never emitted as PHP**. Class-body `extension fn` prepends an implicit `$this` parameter typed as the enclosing tyhpdef type. Those members do not use a block target.

#### Structs

File-, namespace-, and class-member `type Name = struct { … };` (and `type Name<T> = struct extends Parent { … };`) bind as an `ObjectDeclarationSymbol` with `IsStruct = true` — the same model as anonymous `new struct { … }` (`TyhpStructDeclAst` via `BindStructDecl`). Generics come from the alias's `GenericArguments` child (not a grammar addon). The alias does **not** occupy the PHP function namespace and does not emit a `\Tyhp\Type` factory. Object-shape and callable-shape aliases remain `TypeAliasSymbol` / `ObjectTypeAliasSymbol` and cannot be constructed with `new`.

A class-member struct is registered on the owning class (`Members` + child symbol) with FQN `Owner\Name` so `C\Point` / `self\Point` resolve through `NameResolver.TryResolveObjectTypeAlias` (which also still returns `ObjectTypeAliasSymbol` members). Its `ObjectDeclarationScope` is nested under the owner via `IObjectDeclarationScopeParent.AddObjectDeclarationChildScope`. Unqualified `Point` does not bind to that nested struct.

Anonymous expression structs still use `BindStructDecl` → `ObjectDeclarationSymbol` with a synthetic `anonStruct@…` name.

`BindNamedStruct` creates the symbol, populates `GenericParameters`, and registers each `TyhpStructPropertyAst` as a public `ObjectPropertySymbol`. `extends` is a raw `IClassName` (`PhpNameAst`); `ExtendsType` is usually null, so parent lookup reads `StructExtends.FromDeclaringNode` (the alias AST or `TyhpStructShapeAst`).

#### Tyhpdefs (before user code)

`LoadTyhpdefSymbols` → `TyhpdefSymbolRegistrar.RegisterAll(Tyhpdef.GetSourceFiles(...))` → include files first (`BindTyhpdefSourceFile` → `BindFile`), then a Layer 1 stamp snapshot, then overlay files in `OverlaySequence` order.

There is no separate symbol-dump / AST-index path that registers package APIs for compilation. `TyhpdefApiIndex` / `TyhpdefAstApiReader` are for `--verify` comparison of generated stubs, not binder registration. Because package files share `BindFile`, file-level `declare(php=…)` and declaration-level `#[\Tyhp\Php]` use the same `PhpVersionConstraint.Evaluate` + constraint stack as user sources. A single Composer package (`tyhpdef/php`, plus `tyhpdef/php-ext-*`) can therefore ship version-disjoint stubs; the compiler filters them against `CompilationOptions.PhpVersion` (`output.phpVersion`) instead of installing mutually exclusive `tyhpdef/php-8.x` packages. Vendor discovery loads `vendor/<vendor>/<package>/composer.json` when `extra.tyhp.package` is a JSON object, from both Composer `require` and `require-dev`. Directories named `tyhpdef/php-{major}.{minor}` are the only version-skipped layout; `tyhpdef/php-ext-*` is always eligible. TYHP8027 runtime-package presence prefers `composer.json` `"name"` (`tyhp/core`) so a path-repo physical directory such as `dist/tyhp-core/805.0.1` still counts. require-dev-only package `.tyhp` sources are excluded from the consumer lint/build file set (`Project.GetProjectSourceFiles`).

Special behaviors:

- Duplicate functions in the same package may merge into `FunctionDeclarationSymbol.Overloads`
- Tyhpdef `const int NAME ?? N` stores the coalesce start expression on
  `ConstantSymbol.ValueExpression` / `ObjectConstantSymbol.ValueExpression`
  (declaring node is the const AST) so the checker can treat a known `N` as
  integer literal `N` at overload selection
- Overlay same-name methods after last-wins replace of the harvested member merge into `ObjectMethodSymbol.Overloads`
- Cross-package same FQN → `TyhpdefDuplicateFqnAcrossPackages` via registrar tracking. The error is recorded when the later include fails to register. A later overlay `omit` retracts that TYHP8025 when its fully-qualified name matches the omitted symbol and its `declared here` label is that declaration (constants match the name ordinally; classes and functions match it case-insensitively). An unlabeled TYHP8025 stays. TYHP8025 for any other name stays, as does every diagnostic with another code. A class, function, and const that share an FQCN stay separate, so omit of one leaves the others' TYHP8025. Overlay last-wins replace untracks the name but does not retract the diagnostic.
- Tyhpdef `extern class` / `interface` / `enum` / kind-unspecified `extern \Name;` (`TyhpdefImportObjectDeclAst.IsExtern`) register as the same `ObjectDeclarationSymbol` class-like path with an empty body. Name-only `extern function \bcadd;` / `extern const \FOO;` register `FunctionDeclarationSymbol` / `ConstantSymbol` the same way. `IsExtern` and `ProvidedBy` live on `BaseSymbol` (copied from the AST). The bare type form sets `ObjectKind` to `PhpTypeDeclType.Unspecified`. Occupancy is **per PHP name space**: a class, a function, and a const may share an FQCN. The name is **bound**: parameter/return/property/generic types that resolve to an extern type do not report TYHP3019 / TYHP3020 / TYHP3021 / TYHP3022; tyhpdef signatures and mapping bodies may name an extern function or const.
- **Real-wins merge** (`TryConsumeTyhpdefExternMerge` / `TryConsumeTyhpdefExternFunctionMerge` / `TryConsumeTyhpdefExternConstMerge`, before `TryRegisterTyhpdefTopLevelSymbol` / overlay last-wins): same FQCN + compatible kind — existing extern + incoming real **replaces** the placeholder (`IsExtern` cleared, no TYHP8002 / TYHP8025); existing real + incoming extern is a **no-op** (keep real); both extern keep the first symbol (first `@provided-by` if they differ; a later specific type-kind claim upgrades an unspecified surviving type); both real keep the existing duplicate diagnostics (TYHP8002 same package, TYHP8025 cross-package). Specified **type** kinds that disagree (and either side is extern) → TYHP8029. Authors of a specified-kind `extern` must use the originating kind (`interface` → `extern interface`, `class` → `extern class`, `enum` → `extern enum`). `Unspecified` is compatible with class / interface / enum, not trait. Function vs class/const of the same FQCN is **not** 8029 (separate name spaces). Replacement is order-independent: include packages may load the real tyhpdef before or after the `extern` and the surviving symbol is the real declaration. Overlay last-wins of a full real type/function/const onto an extern placeholder uses the same replace path; overlay `extern` onto a real is the no-op. An incoming extern function does not append to `Overloads`.

  Include + overlay + cross-package merge for `extern`:

  | Already in the table | Incoming | Result |
  |----------------------|----------|--------|
  | nothing | `extern class Foo` / `extern Foo` | placeholder |
  | `extern class Foo` | `extern class Foo` | same symbol |
  | `extern Foo` | `extern class Foo` | same symbol, kind becomes class |
  | `extern class Foo` | `extern Foo` | same symbol, keep class |
  | `extern Foo` | real `class` / `interface` / `enum Foo` | **real wins**, silent |
  | `extern class Foo` | real `class Foo` | **real wins**, silent |
  | real `class Foo` | `extern class Foo` / `extern Foo` | **no-op** |
  | `extern class Foo` | real `interface Foo` | **error** TYHP8029 (kind mismatch) |
  | `extern class Foo` | `extern interface Foo` | **error** TYHP8029 (kind mismatch) |
  | real `class Foo` | real `class Foo` | existing TYHP8002 / TYHP8025 |
  | `extern class Foo` / `extern Foo` | overlay `partial class Foo` | **error** TYHP8030; skip partial |
  | `extern class Foo` / `extern Foo` | overlay `omit class Foo` | name removed |
  | `extern class Foo` / `extern Foo` | overlay full `class Foo { … }` | **real wins** (full replace) |
  | nothing | `extern function Foo` / `extern const FOO` | placeholder |
  | `extern function Foo` | real `function Foo(...)` | **real wins**, silent |
  | real `function Foo(...)` | `extern function Foo` | **no-op** |
  | `extern const FOO` | real `const int FOO` | **real wins**, silent |
  | `extern function Foo` | `class Foo` / `interface Foo` | **coexist** (not 8029) |
  | `extern function Foo` | overlay `partial function Foo` | **error** TYHP8030; skip partial |
  | `extern function Foo` / `extern const FOO` | overlay `omit` | name removed |

  Hand-written include files (or overlay) may **add** extra `extern` names. That is a new placeholder, not a hollow `class \Foreign\Type {}`. Regen must not overwrite those include files.
- Include or overlay `partial` whose target `IsExtern` → TYHP8030 and the partial is skipped. Illegal `extern` forms that still reach the binder (body members, `extends` / `implements`, `as` alias, generics, modifiers, enum backing type, or a signature/body on `extern function` / `const`) → TYHP8028. Combining `extern` with `partial` / `omit` / `deprecated` / `obsolete` on the same declaration → TYHP8018.
- Tyhpdef objects can carry pending class-body `use extension` namespace paths resolved in Pass 2 onto `TyhpdefAutoActivatedExtensions`. File-level / `global use extension` uses a binder-side pending list resolved after Pass 1 (`ResolvePendingFileUseExtensions`) onto `FileScope.ImportedExtensions` / `GlobalScope.GloballyActivatedExtensions`.
- **Free-function `as` aliases** (`function php_name as tyhpName(...)`): the
  `FunctionDeclarationSymbol` is registered under the **Tyhp-facing alias** (file scope), with
  `OriginalPhpName` set to the PHP name for emit erasure. Generic parameter declarations on the
  name (`function foo<T> as bar`) populate `GenericParameters`. This matches method aliases
  (`ObjectMethodSymbol.OriginalPhpName`) so `\tyhpName(...)` resolves for checking and emit.
  A tyhpdef name whose only `\` is a leading global prefix (`\array_map`) binds in the parent
  (file) scope; `ResolveNamespacedScope` does not treat the empty prefix as a namespace.
- **Class / interface / trait / enum `as` aliases** (`class php_name as tyhpName`): the
  `ObjectDeclarationSymbol` is registered under the **Tyhp-facing alias** (current file /
  namespace scope), with `OriginalPhpName` set to the PHP name for emit erasure. The PHP name is
  not also registered as a class symbol, so a standalone `extension php_name { }` may share that
  short name (compiled-library backers: `class Name as Name__tyhpExtensionBacker`). Do not also emit a
  `UseIncludeSymbol` under the alias — that would collide with the primary object symbol.
  Const/variable declaration aliases still use `UseIncludeSymbol` via `CreateTyhpdefAlias`.

Built-in / tyhpdef load order is documented in `BuiltIn/README.md` and `TYHPDEF_DISTRIBUTION.md` (embedded → vendor `composer.json` with `extra.tyhp.package` + explicit includes → user tyhpdef globs → overlay globs last; excludes applied after collection). There is no silent scan of `runtime/packages`. `extra.tyhp.impl` on a public metapackage is not a load path. `extra.tyhp.tyhpdef` names a sibling whose own `extra.tyhp.package` loads once that sibling is installed. When a PHP package and `tyhpdef/<vendor>-<name>-impl` both declare `extra.tyhp.package`, the PHP package is loaded and the impl is not (TYHP7523).

`fallback function` is not registered during that walk. Include-layer fallbacks resolve after every include binds and before overlays, and overlay fallbacks resolve after overlays bind (`TyhpBinder.ResolveFallbackFunctions`). One package's fallback fills the name. Several packages: the upstream Composer package that appears first in `vendor/composer/autoload_files.php` wins (`ComposerInstalledInventory.TryReadFilesAutoloadPackageOrder`, mapped from the tyhpdef package's non-`tyhpdef/*` `require`). `tyhpdef/php` beats every fallback. A loaded package whose `require` or `require-dev` contains `ext-X` also beats fallbacks for that compilation (`declare(ext="X")` / `declare(ext="!X")` uses the same set). `fallback const` uses the same resolution as `fallback function`, with case-sensitive names. A missing autoload list, or a competing package with no files entry, is TYHP8036 and registers nothing. A later fallback with a different signature warns TYHP8037. An ordinary function loaded after a fallback is TYHP8038. Alphabetical tyhpdef load order does not choose the winner.

### Pass 2 — resolution

`RunResolutionPass` constructs `NameResolver` from the bound global scope (or an injected `SymbolTree`).

`ResolveInScope`:

1. For each child **symbol**, resolve declared types (properties, variables, constants, type aliases, …). Methods are deferred to method scopes so method generics are in scope. **Top-level / namespace `ConstantSymbol`s** also run `ResolveDeclarationAttributes` on their `DeclaringAstNode` (`PhpConstDeclListAst`) so PHP 8.5 const attributes bind for `AttributeRule` / `TARGET_CONSTANT`. `ResolveDeclarationAttributes` also walks nested **parameter-list** `AstAttributes` on methods, functions, tyhpdef imports, closures, operator overloads, and property-hook parameters, plus nested **property-hook** `AstAttributes` (and promoted-parameter hooks) so those attribute class names bind for emit FQNs / checking.
2. Scope-kind switch: object `extends`/`implements`/generics/attributes/`use extension` and extension block targets; function/method return + params + generics (+ extension operator contribution onto the resolved block target); **anonymous function / closure** return + parameter types (so free type parameters like `fn(): T` bind to `GenericTypeParameterSymbol` and the emitter can erase them — without this, PHP sees a bare `T` class name). A simple non-nullable type expression (`Money`, `self`, `T`) records the resolved symbol on that wrapper, not only on the inner name.
3. Recurse with **`GetAllChildScopes()`** (typed + additional) — critical for anonymous classes / nested functions parked in `_additionalChildScopes`

Unresolved names emit specific `MessageCode`s (`BinderUnresolvedExtendsType`, `BinderUnresolvedReturnType`, `BinderUnresolvedParameterType`, `BinderUnresolvedGenericConstraintType`, `BinderUnresolvedGenericDefaultType`, …) with optional `DidYouMean` attachments from `InScopeNameCandidates.CollectTypeNames`. PHP source (`LanguageMode` `php`) is the exception for `implements` and trait `use`: when the short name's leading segment is a class `use` import in scope (`NameResolver.HasClassUseImport`), TYHP3018 is not reported if that import's target is outside the compilation. The import resolves the short name. `.tyhp` and tyhpdef still report TYHP3018 when the target symbol is missing. When `extends` / `implements` **resolves** to an `ObjectDeclarationSymbol` with `IsExtern`, the binder reports `BinderExternExtendsType` (TYHP3026) or `BinderExternImplementsType` (TYHP3027) instead of the unresolved codes — primary span on the clause, secondary label `declared here` on the `extern` declaration, and `Help` set to `@provided-by` as-is when present. This applies to tyhpdef and `.tyhp` inheritance.

Nesting depth caps: bind depth and resolution depth both use **500**.

### What the Binder does *not* resolve

Many expression-level name references (variables in expressions, method calls, etc.) are left for the **Checker**, which continues to use `SymbolTree` / `NameResolver`. The binder focuses on **declaration structure** and **type annotation** binding (plus attributes on declarations).

**Exception (Story 14.5):** keyword call forms `exit(...)` / `die(...)` / `clone(...)` — recognized when a `PhpUnaryOpAst` operand is a `PhpArgumentListAst` — attach the ExtCore tyhpdef `FunctionDeclarationSymbol` on `BoundSymbol` during `BindStatementBlock`. Bare `exit;` / unary `clone $x` (including parenthesized `clone($x)`) are not call forms and stay unbound; the checker keeps the unary clone object-type rule for those.

#### `declare(php=…)` gates (Story 20.5)

The binder evaluates Composer constraints against `CompilationOptions.PhpVersion` (`output.phpVersion`). If that value is null/empty, the target is `"8.2"` locally — the missing-config warning (4306) is not emitted here.

`PhpVersionConstraint.Evaluate` distinguishes invalid syntax from an unsatisfied gate. Invalid strings do not throw; the region is treated as inactive (symbols are not registered). Diagnostic 4300 is checker-owned.

**File-level** `declare(php="…");` (body is empty/`;`): non-block declares in the file's top-level / namespace statement lists are collected first and **AND**ed. If any is invalid or unsatisfied, `FileSymbol.IsPhpVersionGateInactive` is set and the file's declarations are not bound (parse still ran; declare directives are still recorded). No error is reported for a simply-unsatisfied file gate.

**Block** `declare(php="…") { … }` / `declare: … enddeclare`: creates a `DeclareBlockScope` / `DeclareBlockSymbol` storing the local constraint, the effective AND stack (`EffectivePhpVersionConstraints`), and `IsPhpVersionGateInactive`. Inactive bodies do not register symbols. Active bodies at file/namespace scope **hoist** inner declarations into the enclosing file/namespace (the declare wrapper is compile-time only, not a PHP runtime scope). Nested blocks AND with enclosing file-level and block constraints.

**Alone rule:** `php` plus any other directive in the same `declare` list reports `CheckerPhpVersionDeclareNotAlone` (4301). The `php` gate is not applied (no skip / no inactive region from that statement). Other directives in that list are still recorded. Sequential `declare(strict_types=1); declare(php=">=8.4");` is fine.

`strict_types`, `ticks`, `encoding`, `output_file`, and `include_tag` are unchanged aside from the alone rule when mixed with `php`.

#### `#[\Tyhp\Php]` gates (Story 20.5)

When registering a named declaration, the binder looks for `#[\Tyhp\Php]` on the AST (`AstAttributes`). The attribute is identified by resolving the name to FQCN `\Tyhp\Php` (loaded from `tyhp/core` `package.tyhpdef`) or by the written name `\Tyhp\Php` / `Tyhp\Php`. It is **not** a binder builtin allowlist entry like `Override` / `AllowUnset`.

The `version` argument is read as a string literal, positional (`#[\Tyhp\Php(">=8.4")]`) or named (`#[\Tyhp\Php(version: ">=8.4")]`). Missing or non-string arguments report `CheckerPhpVersionAttributeInvalidArgument` (4305) and the attribute is ignored as a gate (enclosing `declare(php=…)` still applies). Invalid Composer strings omit the symbol without throwing; diagnostic 4300 is checker-owned.

The attribute constraint is **AND**ed with the current `_phpVersionConstraintStack` (file-level + enclosing block `declare(php=…)`). If the outer declare is inactive, Phase 3 already skipped the region — attributes inside are not bound twice. Unsatisfied effective constraints omit the symbol.

Constraint collection is `EvaluatePhpVersionGate` (`TyhpBinder.PhpVersionAttributes.cs`) — the same `PhpVersionConstraint.Evaluate` used for `declare(php=…)`. `ShouldRegisterPhpVersionGatedDeclaration` uses it when registering a named declaration. Overlay bind uses it via `ShouldSkipPhpVersionGatedOverlay` **before** stamp / replace / partial / omit: an unsatisfied `#[\Tyhp\Php]` on an overlay declaration is skip-before-bind, identical to wrapping that declaration in an inactive `declare(php=…) { }` block. TYHP8021 (`@overlay-against` mismatch) and TYHP8031 (`partial function` target not found) are therefore not reported for a gated overlay member whose Layer 1 counterpart is also gated off for the compile target. The stamp spelling keeps nullability: a nullable type that is not a union or intersection is `?string`, a nullable union is spelled with `|null`, and a nullable intersection keeps `&` before `|null`. A type named like `NullLogger` still takes the `?` prefix. Ungated overlay replace of a gated-off Layer 1 member still binds (and still evicts the lingering `_phpVersionGatedDeclarations` record).

Version-**disjoint** same-name declarations (functions, types, members) are allowed: the binder keeps variants whose effective constraint set matches the target. **Overlapping** constraint sets report `CheckerPhpVersionDuplicateDeclaration` (4303) when the declarations would also collide as ordinary overloads: the same parameter-list shape, or a static method and an instance method of the same name (PHP forbids that pairing regardless of parameter shape). `NormalizeGatedKind` still keys instance and static methods together as `"method"` so that comparison happens; `PhpVersionGatedCallableSignature.AreCoexistingOverloads` then requires both a distinct parameter digest **and** matching static/instance-ness before treating the pair as compatible overloads. Distinct function/method parameter lists under the same or overlapping gates are overloads, not 4303, when they share staticness. Overlap is decided by `PhpVersionConstraint.AnyOverlap`, an exact interval check over the literal version bounds referenced by either side (plus their adjacent patches and the version floor) rather than a fixed set of minor probes, so patch-level overlaps are caught too. Two ungated same-name declarations still use the ordinary duplicate path (3019 / 8002), not 4303. Overlay last-wins replace and `omit` call `EvictPhpVersionGatedSymbol` before rebinding so a gated Layer 1 member can be overlay-replaced with the same (or overlapping) gate.

`#[\Tyhp\Php]` on a **struct** or **extension** declaration (Tyhp `extension { }` and tyhpdef `extension { }`) reports `CheckerPhpVersionAttributeInvalidTarget` (4304). The attribute is not applied as a gate; wrap those declarations in `declare(php=…) { }` instead. Grammar accepts the attribute on those targets so the binder can diagnose them.

Effective constraints are stored on `BaseSymbol.EffectivePhpVersionConstraints`. The enclosing `declare(ext="…")` specs (file-level and block, outermost first) are stored on `BaseSymbol.EffectiveExtGates`; both are stamped by `StampPhpVersionConstraints` from `_phpVersionConstraintStack` and `_extGateStack`. The emitter turns them into runtime checks (Emitter guide §6g).

`TryEvaluateExtGate` decides whether a `declare(ext="…")` block binds. A positive gate binds only when a loaded tyhpdef package provides the extension. A negative gate binds when none does, or always when the block is inside a function body (parent scope is not a `FileScope` / `NamespaceBlockScope`): there it is the runtime fallback branch, so it must compile even though a package is loaded. `DeclareBlockSymbol.ExtGate` keeps the block's own spec for the emitter and for the checker's `if`/`else` pairing (`PhpVersionRule.AreComplementaryExtBlocks`).

`MarkDeclarationsWithUncompiledVersionVariants` runs after the declaration pass and sets `BaseSymbol.HasUncompiledVersionVariants` on a registered function/type whose name is also declared under a php gate that is unsatisfied at `output.phpVersion` but not `Never` at or above it. Sources are the unbound records in `_phpVersionGatedDeclarations` and the names in unbound `declare(php)` blocks (`_uncompiledVersionVariants`, filled by `RecordUncompiledDeclareBlockDeclarations`; kept apart so they do not feed TYHP4303 overlap checks).

---

## 6. Coding conventions and patterns

### Partial classes + partial methods

Large binder logic is split by concern. Use `partial void` for hooks whose implementation lives in another file; call sites stay in TopStatements while ObjectBody/CodeBlocks own the bodies.

### Marker interfaces for parent/child scope typing

Scopes implement interfaces like `ICodeBlockScopeParent`, `IObjectDeclarationScopeParent`, `IFunctionDeclarationScopeParent`, `IFileScopeChild`, etc. Adding a child often goes through:

```csharp
void ICodeBlockScopeParent.AddCodeBlockChildScope(ICodeBlockScopeChild child)
    => this.AddChildScopeFromMarkerInterface(child);
```

This avoids illegal generic conversions when a parent’s `TChildScopes` cannot accept the child’s concrete type.

### Symbol interface tagging

Symbols implement scope-specific symbol interfaces (`INamespaceBlockScopeSymbol`, `IObjectDeclarationScopeSymbol`, …) so `AddChildSymbol` is typed. `SymbolTypeHelper.GetAllowedChildren` further validates parent/child `SymbolType` pairs (debug rejection if mismatched).

### Naming

- Bind methods: `BindX` for declaration walk; `ResolveX` for Pass 2
- Scopes: `*Scope`; symbols: `*Symbol`
- Synthetic names: `block@line:col`, `closure@line:col`, `$__using_N`, `__TyhpInlineExt_ClassName`

### Diagnostics

Prefer `DiagnosticBag.AddError` / `AddErrorFromAst` with `MessageCode` binder/tyhpdef codes. Duplicate-name errors (`BinderDuplicateSymbolDeclaration` TYHP3002, `BinderDuplicateUseAlias` TYHP3008, `BinderDuplicateGenericParameter` TYHP3011, include-layer `TyhpdefDuplicateDeclaration` TYHP8002, cross-package `TyhpdefDuplicateFqnAcrossPackages` TYHP8025) go through `DiagnosticBag.AddDuplicateFromAst`, which keeps the primary span on the second declaration and attaches a `declared here` label on the first when `DeclaringAstNode` is present (including a different `SourceFile`). Callers recover that original via `TryAddChildSymbol(..., out existing)` rather than copying `WithLabels`. A missing original still emits the error with no label. Overlay last-wins replace is not a duplicate and must not grow a 8002. 8025 keeps package names in the short message and still labels the first package's declaration. Overlay `omit` retracts the 8025 for that declaration; `omit` of any other name, and any other diagnostic code, stays. A function or const that shares the FQCN keeps its own 8025. Unexpected AST kinds often produce warnings rather than hard failures so partial trees still bind.

### Overload signatures

Tyhp function/method overload signatures without bodies are compile-time-only: binder skips them via `OverloadSignatureHelper`; implementations bind normally. Short methods/functions already desugared to bodies bind as normal declarations. Extension members use the same skip (`IsExtensionFunctionOverloadSignature`); signatures are then attached to the implementation's `ObjectMethodSymbol.Overloads` so call-site selection can see them.

### AST grammar addons

Generics, `async`, and `internal` often arrive via `AstGrammarAddons` (`"identifier"` generics list, `"isAsync"`, `"isInternal"`, `"modifiers"`), not only classic PHP AST fields — see `PopulateGenericParametersFromGrammarAddon` and `HasAsyncModifier` / `HasInternalModifier` / `ConvertModifiers`. `PhpModifier.Internal` / `MemberModifier.Internal` are also produced when the token is in a PHP modifier list (`classModifierGrammarAddon` / `memberModifierGrammarAddon`).

---

## 7. Important helpers and utilities

| Helper | Where | When |
|--------|-------|------|
| `NameResolver` | `Resolution/` | Pass 2 and checker/emitter lookups |
| `InScopeNameCandidates` | `Resolution/` | DidYouMean for unresolved types/properties/params |
| `SymbolTree` | Binder root | Checker entry; extension index; batch resolver |
| `TyhpdefSymbolRegistrar` | Binder root | Ordered tyhpdef bind + FQN→package map |
| `BuiltIn.Tyhpdef.GetSourceFiles` | `BuiltIn/` | Discover/parse tyhpdefs (cached ASTs) |
| `BuiltIn.Types/Constants/Variables/StructUtilityTypes/Functions/...` | `BuiltIn/` | Hardcoded global symbols |
| `GenericParameterRequirements` | `Symbols/` | Metadata for builtin generic arity (checker) |
| `ObjectDeclarationMemberNamePolicy` | `Symbols/` | Comparers for Members vs Constants maps |
| `SymbolTypeHelper` | `Enum/SymbolType.cs` | Allowed children; static vs instance method scope |
| `SymbolExportVisibility.OmitFromPublicTyhpdef` | `Symbols/SymbolExportVisibility.cs` | Public compiled-library skip: `private` **or** `IsInternal` (call from the generator in place of a private-only skip) |
| `OverloadSignatureHelper` | (shared TyhpLang helper) | Skip overload-only decls |
| `EvaluatePhpVersionGate` | `TyhpBinder.PhpVersionAttributes.cs` | Shared `#[\Tyhp\Php]` + declare-stack constraint evaluation (`PhpVersionConstraint.Evaluate`) |
| `ShouldSkipPhpVersionGatedOverlay` | `TyhpBinder.PhpVersionAttributes.cs` | Overlay skip-before-bind when that evaluation is unsatisfied |
| `ShouldRegisterPhpVersionGatedDeclaration` | `TyhpBinder.PhpVersionAttributes.cs` | `#[\Tyhp\Php]` omit / 4303 overlap / 4304 struct-extension / 4305 args |
| `StaticValueTypeHelper` | (shared) | Literal types in annotations → underlying builtin |
| `DidYouMean` | Domain diagnostics | Attach suggestions to unresolved diagnostics |
| `AstCacheService` | Domain | Tyhpdef AST cache; fresh deserialize per bind so `BoundSymbol`/`OwningFile` do not leak |

### Built-in registration order (`PopulateBuiltIns`)

1. `Types` (scalars + `Decimal`/`struct` aliases)
2. `Constants` (magic constants)
3. `Variables` (superglobals)
4. `SymbolNameTypes`, `StructUtilityTypes` (including `__New`), `MagicUtilityTypes`, `TypeNameAlgebraTypes`
5. `Functions` (compile-time `nameof` / `typeof` / …)

Then tyhpdefs load separately (not inside `PopulateBuiltIns`). Aliases in the embedded `__tyhp_types` tyhpdef are ordinary type symbols. Unqualified lookup finds a same-named global built-in first; an alias with no built-in still binds.

---

## 8. Weirdness / non-obvious design choices

### PHP 8.6 reserved Tyhp names

`ReportReservedPhp86Names` walks the finished tree (after tyhpdef overlays and user files). It rejects a remaining Tyhp-facing name that PHP 8.6 reserves: `let` or `is` on a class, interface, trait, enum, function, or constant; `namespace` on a constant; `readonly` on a function; `_` on a constant or a class/const `use` alias. Methods, properties, enum cases, and `function _()` are not reserved. A Layer 1 `class Is` disappears when an overlay `partial class Is as SomeAlias` renames it, so only `SomeAlias` is checked. `FlushOverlayPartialTypeRename` records the old FQN on `OriginalPhpName` so emit still spells the PHP class.

### `_additionalChildScopes` + always use `GetAllChildScopes`

C# generics are invariant. A `CodeBlockScope` cannot store an `ObjectDeclarationScope` in its typed child list when the type parameters do not line up. Those children go into `_additionalChildScopes`.

**If you recurse only `ChildScopes`, you miss anonymous classes and nested functions.** Pass 2 documents this explicitly; use `IBaseScope.GetAllChildScopes()`.

### Three symbol namespaces (PHP fidelity)

Constants, functions, and class-likes are indexed separately. Duplicate detection for cross-file uniqueness uses `TryGetChildInPhpSymbolNamespace` so a class does not “block” a same-named function incorrectly, and vice versa.

### NamespaceBlock vs Namespace

One shared `NamespaceScope` per namespace name; **many** `NamespaceBlockScope`s (typically per file/contribution) so `use` aliases stay file-local while FQNs still collide across siblings.

### Traits stored in `ImplementsTypes`

Trait `use` adds trait type expressions to the same list as interfaces. Member resolution distinguishes traits via `ObjectKind == Trait` when applying adaptations.

### Statement-form namespaces mutate “current scope”

Unlike block namespaces, `namespace Foo;` returns a block scope used as the parent for following top-level siblings so FQNs/PSR-4 paths include the namespace segment.

### Synthetic inline extensions

Class-body tyhpdef `extension fn` / `extension operator` need a symbol home for lookup and splice, but they must not become PHP. The binder invents `__TyhpInlineExt_*` classes marked `IsCompilerGenerated` / `IsExtension` (auto-activated; no `use extension`). Method-level generics from the name addon are registered on the synthetic method symbol the same way as standalone extension members. `ResolveSelfStaticParent` remaps `self`/`static` inside them to the receiver class. The emitter splices call sites and never emits the synthetic class or a static forwarding method.

### Operator overloads share a name

They are excluded from uniqueness indexes and from `Members` so multiple `+` overloads coexist; discovery is by enumeration.

### Union/intersection `ResolveType` return value

All components are resolved and recorded, but the method returns only the **first** non-null component. Consumers needing the full composite must use `ResolvedSymbols` or checker type models — documented on `NameResolver.ResolveType`. `TyhpObjectShapeAst` is not a symbol: `ResolveType` walks member signatures and returns null so the alias (not a synthetic class) stays the type-position symbol. Inline `TyhpCallableShapeAst` (`callable(…): R`) walks parameter and return types, then binds to the `callable` builtin so Pass 2 does not report unresolved-parameter/constraint errors on the shape itself.

### Template-string and type-guard annotations

Binder binds template-string types to the `string` builtin and type-guard returns to the guarded type expression so Pass 2 does not emit spurious unresolved-type errors; precise checking is elsewhere.

### `using` disposable validation deferred

`BindUsingResource` contains TODOs: verifying `IsDisposable` / async disposable and `:=` restrictions need type information → checker (comments in `TyhpBinder.CodeBlocks.cs`).

### Old `readme.md` vs reality

The folder `readme.md` sketches passes (scopes / declarations / references) and many scope kinds. The implemented binder folds scope creation into the declaration walk and concentrates “references” on **type** (and attribute) resolution in Pass 2, not a full expression-reference pass.

---

## 9. Interactions with other TyhpLang components

### Parser / AST

- Input: `SrcFileAst` trees from ANTLR visitor.
- Binder sets `OwningFile` on every node and `BoundSymbol` on declaring nodes (+ resolved type/attribute names).
- Tyhpdef content is parsed with `ParseMode.Tyhpdef` inside `BuiltIn.Tyhpdef.ParseContent`. A failed AST cache read or write there is a warning: the cache file is removed, the source is parsed, and one write is attempted after a read failure. The parsed AST is returned either way.

### Checker

- Receives `GlobalScope` via `SymbolTree`.
- Reads `BoundSymbol` on names/types; creates `NameResolver` for members, generics, extensions.
- Fills checker-only fields on binder symbols (e.g. `GenericTypeParameterSymbol.ResolvedConstraint`, `ObjectDeclarationSymbol.InheritedPropertiesInitializedByConstruction`).
- Performs assignability, control-flow, and expression binding the binder intentionally skips.

### Emitter

- Uses `BoundSymbol` on names for FQN spelling / use-alias expansion (`TyhpEmitter.Expressions`, `TypeSpellingHelper`).
- Relies on extension symbols produced at bind time for emit of Tyhp `extension { }` backers. Compiler-generated `__TyhpInlineExt_*` scopes are bind/check/splice only and must not appear in PHP.

### Tyhpdefs / packages

- Binder is the **registration** point for PHP extension stubs and runtime package APIs.
- Wrong/missing tyhpdef signatures surface as bind/check errors; per project rules, prefer fixing tyhpdefs/toolchain over hacking package `tyhp_src`.

### CLI / lint / build

`CompilationService.ParseFiles` is shared by build and lint. Options (`PhpVersion`, `tyhpdefInclude`/`overlay`/`Exclude`, `ProjectPath`, `SkipChecking`, `StrictMode`, `ApplyTyhpdefOverlays`) flow into tyhpdef discovery and whether check runs after bind.

---

## 10. Common pitfalls for contributors

1. **Recursing only typed `ChildScopes`** — miss `_additionalChildScopes`; use `GetAllChildScopes()`.
2. **Adding nested class/function via generic `AddChildScope` only** — prefer parent marker `AddObjectDeclarationChildScope` / `AddFunctionDeclarationChildScope`.
3. **Putting class constants into `Members`** — use `Constants` / `RegisterObjectMember` paths; keep `$` on property keys.
4. **Expecting generic type parameters to appear as ordinary scope children** — they live on `GenericParameters` lists; resolve via `ResolveGenericTypeParameter`.
5. **Treating operator overloads like unique-named methods** — they bypass name indexes on purpose.
6. **Resolving unqualified names during Pass 1** — unsafe until all files/namespaces/`use`s are registered (see Story 31 plan); Pass 2 exists for this. File-level `use extension` follows that rule: do not call `FindExtensionSymbol` while walking the statement itself.
7. **Mutating cached tyhpdef ASTs across compiles** — cache returns fresh trees; still avoid storing binder state on shared static ASTs.
8. **Forgetting declaration-before-`IStatement` in AST walks** — nested function/class decls implement `IStatement` and can be mis-handled as bare statements.
9. **Duplicate symbol checks across files** — uniqueness is sibling FileScopes / NamespaceBlockScopes, not only the current scope’s index. Overlay / include `partial` target lookup uses the same sibling walk (`FindExistingObjectType`); searching only the current `NamespaceBlockScope` misses Layer 1 types declared in another file.
10. **Assuming binder resolves all expression names** — variables/calls in expressions are largely a checker concern.
11. **Breaking PHP case rules** — constants case-sensitive; functions/classes case-insensitive. `FindExistingTyhpdefSymbol`'s child matcher must honor this too: comparing every candidate case-insensitively lets `omit const foo_bar;` match a differently-cased `FOO_BAR` and remove the wrong constant. The inherited-lookup in-flight key compares **member** names `OrdinalIgnoreCase` for the same reason; do not switch it to ordinal.
12. **Synthetic extension naming collisions** — `__TyhpInlineExt_{Name}` must remain unique in the owning file/namespace scope.
13. **Assuming per-walk `visited` sees nested type-resolution lookups** — public member/alias entries allocate a new `visited` set. Cross-walk re-entry (class `\Foo\Bar` whose `extends` is `\Foo\Bar\Baz`) is the in-flight `(object, member, kind)` stack on the resolver; do not reset it at public entry.

---

## 11. Open Questions / Needs Clarification

These remain unclear after reading the Binder sources, tests, and clarifying docs (`INCOMPLETE.md`, Story 02/31 plans, `BuiltIn` docs). Do **not** treat the following as settled behavior.

1. **Are `GenericTypeParameterSymbol` instances ever registered as scope child symbols** in any remaining code path, or is the list-on-owner + `ResolveGenericTypeParameter` walk the sole intended model? Interfaces allow them as object/function/method scope symbols, but the declaration walk observed here only appends to `GenericParameters` lists.

2. **`INCOMPLETE.md` Story 02** still lists finishing a post-registration scan for duplicate FQNs across tyhpdef sources (aligning with code **8025**). `TyhpdefSymbolRegistrar` tracks FQNs for *failed duplicate adds*; whether a proactive full-tree duplicate scan is still required is not closed in code comments.

3. **`CodeBlockScope` TODOs** still ask whether object/function child scopes are allowed only under certain ancestor chains up to a namespace block. Current code accepts nested decls via marker interfaces; the stricter structural rule is not enforced in the binder.

4. **`using` resource type validation and `:=` prohibition** are explicitly TODO’d in the binder and deferred; exact checker ownership/status was not verified end-to-end for this guide.

5. **Story 31 deferred-resolution optimization** (eager resolve of stable refs + work list instead of full Pass 2 tree walk) is planned in docs but **not** present in `TyhpBinder.Bind()` / `RunResolutionPass` as of this writing — treat Pass 2 full walk as current truth.

6. **Expression-level reference binding completeness:** the early `readme.md` envisioned a broad “symbol references” pass attaching links for all name uses. How much of that remains intentionally unfinished versus permanently owned by the checker is a product/architecture question; the implementation clearly centers Pass 2 on types/attributes.

7. **`AnonymousObjectDeclarationSymbol` vs using `ObjectDeclarationSymbol` for anonymous classes:** TopStatements builds an `ObjectDeclarationSymbol` for anonymous classes; a separate `AnonymousObjectDeclarationSymbol` type exists — when (if ever) the dedicated type is constructed in production bind paths was not exhaustively confirmed across all call sites.

8. **Label scoping vs `goto` resolution:** binder creates `LabelScope`s; whether goto target resolution is binder or checker responsibility was not fully traced beyond label registration.

---

## Appendix A — Quick mental model

```
GlobalScope
├── BuiltInType / MagicConstant / SuperGlobal / Utility / BuiltInFunction …
├── FileScope (each .tyhp / tyhpdef file)
│   ├── UseInclude, TypeAlias, Constant, Function, Object, …
│   ├── ObjectDeclarationScope → methods/properties/…
│   └── CodeBlockScope… (top-level statements)
└── NamespaceScope ("App\\Models")
    ├── NamespaceBlockScope (file A contribution)
    └── NamespaceBlockScope (file B contribution)
```

Pass 1 fills this tree. Pass 2 walks it with `NameResolver` and stamps `BoundSymbol` on type-related AST nodes. Checker and emitter consume the tree + annotations.

## Appendix B — Key source anchors

| Concern | Primary file |
|---------|----------------|
| `Bind()` two-pass entry | `TyhpBinder.cs` |
| Top-level dispatch | `TyhpBinder.TopStatements.cs` |
| Class bodies | `TyhpBinder.ObjectBody.cs` |
| Nested scopes / closures / using | `TyhpBinder.CodeBlocks.cs` |
| Extensions | `TyhpBinder.Extensions.cs` |
| Tyhpdef binding | `TyhpBinder.Tyhpdef.cs`, `TyhpBinder.Extern.cs`, `TyhpBinder.Overlays.cs`, `TyhpdefSymbolRegistrar.cs`, `BuiltIn/Tyhpdef*.cs` |
| Resolution pass | `TyhpBinder.Resolution.cs` |
| Name/type/member resolution | `Resolution/NameResolver.cs` |
| Scope storage / PHP indexes | `Scopes/BaseScope.cs` |
| Pipeline hook | `Domain/Services/CompilationService.cs` |

---

*Generated from source review of `Tyhp/TyhpLang/Binder/**` and related call sites/tests/docs. Prefer the `.cs` files if this guide and older sketches disagree.*
