# Tyhp Binder — Developer Technical Guide

This guide explains how the Tyhp **Binder** works: its place in the compilation pipeline, data structures, two-pass binding flow, conventions, and how it interacts with the rest of TyhpLang. Everything here is grounded in the source under `Tyhp/TyhpLang/Binder/` and the call sites that invoke it.

> Related local notes: the older `readme.md` in this folder is an early design sketch (scope taxonomy, open questions). Prefer this guide + the current `.cs` sources for behavior. Tyhpdef loading details live in `BuiltIn/README.md` and `BuiltIn/TYHPDEF_DISTRIBUTION.md`.

---

## 1. Overview / purpose

The Binder turns a list of parsed ASTs (`SrcFileAst`) into a **scope/symbol tree** rooted at `GlobalScope`. It does **not** type-check assignability or emit PHP. Its jobs are:

1. **Register declarations** — files, namespaces, classes/interfaces/traits/enums/structs/extensions, functions, members, `use` imports, variables, labels, declare blocks, etc.
2. **Link declarations to AST** — `BaseSymbol` constructors set `IBase2Ast.BoundSymbol`; the resolution pass also sets `BoundSymbol` on type-name / attribute-name nodes via `NameResolver.RecordResolution`.
3. **Resolve type-name references** — after all declarations exist, walk scopes and bind type expressions (`extends`/`implements`, parameter/return/property types, generic constraints/defaults, attributes, extension operator targets, tyhpdef `use extension` paths) to declaring symbols.
4. **Load external type information** — built-in scalars/utilities plus `.tyhpdef` packages, before user files are bound.

Pipeline position (from `CompilationService.ParseFiles`):

```
Parse (parallel) → Bind (single-threaded) → Check → (later) Emit
```

Binding is skipped if parse produced errors. Checking is skipped if bind produced errors (or `SkipChecking` is set). The checker wraps the bound `GlobalScope` in a `SymbolTree` and continues to use `NameResolver` for member/extension lookups.

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
├── TyhpBinder.Extensions.cs      # extension decls + synthetic __TyhpInlineExt_* scopes
├── TyhpBinder.Tyhpdef.cs         # tyhpdef AST binding + package tracking hooks
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
| `TyhpBinder.cs` | Entry, builtins, file scope, `SetOwningFileRecursive` |
| `TyhpBinder.TopStatements.cs` | Top-level statement dispatch; namespaces; object/function/struct decls; imports; declare; generics/modifiers helpers; **declares** `partial void` hooks |
| `TyhpBinder.ObjectBody.cs` | Implements `BindObjectBody`; members; promoted ctor params; magic method typing; traits |
| `TyhpBinder.CodeBlocks.cs` | Implements `BindFunctionBody` / `BindStatementBlock`; nested decls; using/global/static |
| `TyhpBinder.Extensions.cs` | Standalone `extension` decls + synthetic inline extension class |
| `TyhpBinder.Tyhpdef.cs` | Tyhpdef-specific declaration binding and overload merging |
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
| `DeclareBlockScope` | `DeclareBlockSymbol` | Block-form `declare(...) { }` |
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

Shared fields: `Name`, `FullyQualifiedName`, `DeclaringAstNode`, `ContainingScope`, `SymbolType`, visibility/deprecation/doc/source location (`Line`, `Column`, `EndLine`, `EndColumn`). `EndLine`/`EndColumn` are copied from the declaring AST node (`IBase2Ast.EndLine` / exclusive `EndColumn`); they are `0` when no declaring node is provided and `-1` when the AST node itself has no end span.

On construction, if a declaring AST node is provided, **`declaringNode.BoundSymbol = this`**.

`FullyQualifiedName` and `ContainingScope` are filled when the symbol is added to a scope (`BaseScope.AddChildSymbol` / `AddChildScope`). Only `NamespaceSymbol` segments contribute to the namespace path; `FileScope` and code blocks are transparent for FQN computation (PHP-like).

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

Also stores inheritance/trait/extension metadata: `ExtendsType`, `ImplementsTypes` (interfaces **and** used traits — trait `use` / `implements` name-list items are `IClassName`/`PhpNameAst`, wrapped as `PhpNamedTypeAst` via `AsTypeExpression` so they fit the `ITypeExpression` list), `TraitMethodPrecedence` / `TraitMethodAliases`, extension auto-activation lists, synthetic inline extension pointer, etc.

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
| `ResolveSymbol` | Lexical walk up scopes; expands `use`; special-cases FileScope for namespace blocks; stops variable lookup at function/method/closure boundaries |
| `ResolveQualifiedName` | Absolute `\A\B\C` via `NamespaceScope` + block child search; single-segment uses `SearchGlobalNamespace` |
| `ResolveRelativeName` | Class-`use` prefix expansion, then current namespace, then global |
| `ResolveType` | Builtins, named types, unions/intersections (resolves all components; **returns first**), type guards, template-string types → `string` builtin |
| `ResolveMember` / `ResolveStaticMember` / `ResolveConstant` | Inheritance + traits + interfaces with adaptation rules |
| `ResolveSelfStaticParent` | `self`/`static`/`parent` → enclosing object symbol (binder identity). Checker keeps bare `static` as a distinct late-bound type and bans `static<…>` (TYHP4168); see Checker technical guide / `tyhp_0150_newTypes.md`. |
| `ResolveGenericTypeParameter` | Walks enclosing function/method/object `GenericParameters` lists (method shadows class) |
| `ResolveExtensionMethod` | Auto-activated tyhpdef extensions, else indexed/scan search; matches first parameter type |
| `ResolveAttributeClassName` | Soft resolve for attributes (missing `\Override` etc. allowed) |

### Generics on symbols (not always scope children)

`PopulateGenericParameters` / `PopulateGenericParametersFromGrammarAddon` append `GenericTypeParameterSymbol` instances to the owning symbol’s `GenericParameters` list (class/function/method/type alias). Resolution of those names goes through `NameResolver.ResolveGenericTypeParameter`, **not** primarily through `FindChildSymbolByName` on the object/function scope.

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
  RunResolutionPass()
    NameResolver(global)
    ResolveInScope(each FileScope / NamespaceBlockScope subtree)
```

### Pass 1 — declaration

#### File and top statements

`BindTopStatementList` maintains a **current scope**. Statement-form `namespace Foo;` (no braces) switches `currentScope` for subsequent siblings until another namespace (PHP semantics). Block namespaces bind their body into a new `NamespaceBlockScope`.

`BindTopStatement` dispatches: namespaces, object types, functions, imports, constants, declare, type aliases, typed vars, tyhpdef import ASTs, extension decls, structs, nested statement lists, and otherwise `BindStatementBlock` for executable top-level statements.

#### Namespaces

`BindNamespaceDeclCore`:

1. `GlobalScope.AddNamespaceScope(name)` — reuses existing namespace by normalized name
2. Creates a **new** `NamespaceBlockScope` under that namespace (one block per declaration occurrence / file contribution)
3. Binds nested top statements into the block (if present)

Cross-file uniqueness for functions/classes/constants/type aliases is enforced in `NamespaceBlockScope.AddChildSymbol` / `FileScope.AddChildSymbol` by scanning sibling blocks/files in the same PHP symbol namespace.

#### Object declarations

`BindObjectTypeDecl`:

1. Builds `ObjectDeclarationSymbol` (kind from `class`/`interface`/`trait`/`enum`)
2. Captures `extends` / `implements` AST references (unresolved until Pass 2)
3. Populates class generics from grammar addon `"identifier"` → `TyhpGenericsTypeArgumentListAst`
4. Adds symbol + `ObjectDeclarationScope` under File or NamespaceBlock
5. Calls `BindObjectBody`

Anonymous classes use `IObjectDeclarationScopeParent.AddObjectDeclarationChildScope` (may land in `_additionalChildScopes` under a code block).

`BindObjectBody`:

- Injects `self`/`static`/`parent` builtins into the object scope (`Types.PopulateObject`)
- Skips class method **overload signatures** (`OverloadSignatureHelper`) — only implementations bind
- Methods → instance or static method scope + parameter vars + body
- Properties, object consts, enum cases, operators, object type aliases, trait uses

Constructor parameter promotion creates both a parameter `VariableSymbol` and an `ObjectPropertySymbol` keyed with `$` prefix.

Traits: trait type expressions are appended to `ImplementsTypes`; adaptations fill `TraitMethodPrecedence` / `TraitMethodAliases`.

#### Functions

Skips erasable overload signatures. Creates `FunctionDeclarationSymbol` + `FunctionDeclarationScope`, then `BindFunctionBody` (parameters as variables + return type AST + body).

Nested named functions inside statement blocks are handled both in `BindStatementBlock` (`PhpFunctionDeclAst` arm) and `BindCodeBlockChildren` (declaration-before-`IStatement` order) — historically a bug when nested decls were walked as plain statements (`FOUND_BUGS` #36; comments in CodeBlocks/TopStatements).

#### Imports (`use`)

Become `UseIncludeSymbol` on File or NamespaceBlock with `UseType` Class/Const/Function and pre-split `ImportedNameSegments`.

#### Code blocks / control flow

`BindStatementBlock` creates nested `CodeBlockScope`s for statement blocks, if/loop/try/match, declare blocks, labels, anonymous functions, typed vars, using blocks, `global`/`static`, and recursively walks other statement trees.

Variable lookup during later resolution **stops** at function/method/anonymous-function scopes for `$names` (then only global-scope superglobals).

#### Extensions

- Standalone `TyhpExtensionDeclAst` → `ObjectDeclarationSymbol` with `IsExtension = true`, static methods + operators
- Inline tyhpdef/class `extension function` / extension operators → synthetic class `__TyhpInlineExt_{OwnerName}` via `GetOrCreateSyntheticInlineExtensionScope`, linked with `SyntheticInlineExtension` / `InlineExtensionReceiverClass`
- Standalone `operator +<Target>(…)` resolution: the `<Target>` AST resolves to an `ObjectDeclarationSymbol` **or** a `BuiltInTypeSymbol` (`string`, `int`, …). The overload is appended to that target's `ExtensionContributedOperators` list (same list shape on both symbol kinds) and `ObjectOperatorOverloadMethodSymbol.ExtensionTargetSymbol` stores the resolved `IBaseSymbol`. Unresolvable targets still report `ExtensionOperatorTargetNotFound` (TYHP3016).

#### Structs

`BindStructDecl` creates an `ObjectDeclarationSymbol` with `IsStruct = true` (still `ObjectKind = Class`) and populates `GenericParameters` from `AstGrammarAddons["identifier"]` via `PopulateGenericParametersFromGrammarAddon` (same path as classes).

#### Tyhpdefs (before user code)

`LoadTyhpdefSymbols` → `TyhpdefSymbolRegistrar.RegisterAll(Tyhpdef.GetSourceFiles(...))` → `BindTyhpdefSourceFile` → same `BindFile` path with package provenance.

Special behaviors:

- Duplicate functions in the same package may merge into `FunctionDeclarationSymbol.Overloads`
- Cross-package same FQN → `TyhpdefDuplicateFqnAcrossPackages` via registrar tracking
- Tyhpdef objects can carry pending `use extension` namespace paths resolved in Pass 2 onto `TyhpdefAutoActivatedExtensions`
- **Free-function `as` aliases** (`function php_name as tyhpName(...)`): the
  `FunctionDeclarationSymbol` is registered under the **Tyhp-facing alias** (file scope), with
  `OriginalPhpName` set to the PHP name for emit erasure. Generic parameter declarations on the
  name (`function foo<T> as bar`) populate `GenericParameters`. This matches method aliases
  (`ObjectMethodSymbol.OriginalPhpName`) so `\tyhpName(...)` resolves for checking and emit.
  Class/const/variable declaration aliases still use `UseIncludeSymbol` via `CreateTyhpdefAlias`.

Built-in / tyhpdef load order is documented in `BuiltIn/README.md` and `TYHPDEF_DISTRIBUTION.md` (embedded → vendor `package.tyhp.json` + explicit includes → user tyhpdef globs; excludes applied after collection). There is no silent scan of `runtime/packages` or `runtime/php-extensions`.

### Pass 2 — resolution

`RunResolutionPass` constructs `NameResolver` from the bound global scope (or an injected `SymbolTree`).

`ResolveInScope`:

1. For each child **symbol**, resolve declared types (properties, variables, constants, type aliases, …). Methods are deferred to method scopes so method generics are in scope. **Top-level / namespace `ConstantSymbol`s** also run `ResolveDeclarationAttributes` on their `DeclaringAstNode` (`PhpConstDeclListAst`) so PHP 8.5 const attributes bind for `AttributeRule` / `TARGET_CONSTANT`. `ResolveDeclarationAttributes` also walks nested **property-hook** `AstAttributes` (and promoted-parameter hooks) so hook attribute class names bind for emit FQNs / checking.
2. Scope-kind switch: object `extends`/`implements`/generics/attributes/`use extension`; function/method return + params + generics (+ extension operator target wiring); **anonymous function / closure** return + parameter types (so free type parameters like `fn(): T` bind to `GenericTypeParameterSymbol` and the emitter can erase them — without this, PHP sees a bare `T` class name)
3. Recurse with **`GetAllChildScopes()`** (typed + additional) — critical for anonymous classes / nested functions parked in `_additionalChildScopes`

Unresolved names emit specific `MessageCode`s (`BinderUnresolvedExtendsType`, `BinderUnresolvedReturnType`, `BinderUnresolvedParameterType`, `BinderUnresolvedGenericConstraintType`, `BinderUnresolvedGenericDefaultType`, …) with optional `DidYouMean` attachments from `InScopeNameCandidates.CollectTypeNames`.

Nesting depth caps: bind depth and resolution depth both use **500**.

### What the Binder does *not* resolve

Many expression-level name references (variables in expressions, method calls, etc.) are left for the **Checker**, which continues to use `SymbolTree` / `NameResolver`. The binder focuses on **declaration structure** and **type annotation** binding (plus attributes on declarations).

**Exception (Story 14.5):** keyword call forms `exit(...)` / `die(...)` / `clone(...)` — recognized when a `PhpUnaryOpAst` operand is a `PhpArgumentListAst` — attach the ExtCore tyhpdef `FunctionDeclarationSymbol` on `BoundSymbol` during `BindStatementBlock`. Bare `exit;` / unary `clone $x` (including parenthesized `clone($x)`) are not call forms and stay unbound; the checker keeps the unary clone object-type rule for those.

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

Prefer `DiagnosticBag.AddError` / `AddErrorFromAst` with `MessageCode` binder/tyhpdef codes. Unexpected AST kinds often produce warnings rather than hard failures so partial trees still bind.

### Overload signatures

Tyhp function/method overload signatures without bodies are compile-time-only: binder skips them via `OverloadSignatureHelper`; implementations bind normally. Short methods/functions already desugared to bodies bind as normal declarations.

### AST grammar addons

Generics and `async` often arrive via `AstGrammarAddons` (`"identifier"` generics list, `"isAsync"`, `"modifiers"`), not only classic PHP AST fields — see `PopulateGenericParametersFromGrammarAddon` and `HasAsyncModifier` / `ConvertModifiers`.

---

## 7. Important helpers and utilities

| Helper | Where | When |
|--------|-------|------|
| `NameResolver` | `Resolution/` | Pass 2 and checker/emitter lookups |
| `InScopeNameCandidates` | `Resolution/` | DidYouMean for unresolved types/properties/params |
| `SymbolTree` | Binder root | Checker entry; extension index; batch resolver |
| `TyhpdefSymbolRegistrar` | Binder root | Ordered tyhpdef bind + FQN→package map |
| `BuiltIn.Tyhpdef.GetSourceFiles` | `BuiltIn/` | Discover/parse tyhpdefs (cached ASTs) |
| `BuiltIn.Types/Constants/Variables/UtilityTypes/Functions/...` | `BuiltIn/` | Hardcoded global symbols |
| `GenericParameterRequirements` | `Symbols/` | Metadata for builtin generic arity (checker) |
| `ObjectDeclarationMemberNamePolicy` | `Symbols/` | Comparers for Members vs Constants maps |
| `SymbolTypeHelper` | `Enum/SymbolType.cs` | Allowed children; static vs instance method scope |
| `OverloadSignatureHelper` | (shared TyhpLang helper) | Skip overload-only decls |
| `StaticValueTypeHelper` | (shared) | Literal types in annotations → underlying builtin |
| `DidYouMean` | Domain diagnostics | Attach suggestions to unresolved diagnostics |
| `AstCacheService` | Domain | Tyhpdef AST cache; fresh deserialize per bind so `BoundSymbol`/`OwningFile` do not leak |

### Built-in registration order (`PopulateBuiltIns`)

1. `Types` (scalars + `Decimal`/`struct` aliases)
2. `Constants` (magic constants)
3. `Variables` (superglobals)
4. `UtilityTypes`, `SymbolNameTypes`, `StructUtilityTypes`, `TypeNameAlgebraTypes`
5. `Functions` (compile-time `nameof` / `typeof` / …)

Then tyhpdefs load separately (not inside `PopulateBuiltIns`).

---

## 8. Weirdness / non-obvious design choices

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

Inline extension members need a place to live as static methods. The binder invents `__TyhpInlineExt_*` classes marked `IsCompilerGenerated` / `IsExtension`, and `ResolveSelfStaticParent` remaps `self`/`static` inside them to the receiver class.

### Operator overloads share a name

They are excluded from uniqueness indexes and from `Members` so multiple `+` overloads coexist; discovery is by enumeration.

### Union/intersection `ResolveType` return value

All components are resolved and recorded, but the method returns only the **first** non-null component. Consumers needing the full composite must use `ResolvedSymbols` or checker type models — documented on `NameResolver.ResolveType`.

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
- Tyhpdef content is parsed with `ParseMode.Tyhpdef` inside `BuiltIn.Tyhpdef.ParseContent`.

### Checker

- Receives `GlobalScope` via `SymbolTree`.
- Reads `BoundSymbol` on names/types; creates `NameResolver` for members, generics, extensions.
- Fills checker-only fields on binder symbols (e.g. `GenericTypeParameterSymbol.ResolvedConstraint`, `ObjectDeclarationSymbol.InheritedPropertiesInitializedByConstruction`).
- Performs assignability, control-flow, and expression binding the binder intentionally skips.

### Emitter

- Uses `BoundSymbol` on names for FQN spelling / use-alias expansion (`TyhpEmitter.Expressions`, `TypeSpellingHelper`).
- Relies on extension/synthetic class symbols produced at bind time for emit of extensions.

### Tyhpdefs / packages

- Binder is the **registration** point for PHP extension stubs and runtime package APIs.
- Wrong/missing tyhpdef signatures surface as bind/check errors; per project rules, prefer fixing tyhpdefs/toolchain over hacking package `tyhp_src`.

### CLI / lint / build

`CompilationService.ParseFiles` is shared by build and lint. Options (`PhpVersion`, `tyhpdefInclude`/`Exclude`, `ProjectPath`, `SkipChecking`) flow into tyhpdef discovery and whether check runs after bind.

---

## 10. Common pitfalls for contributors

1. **Recursing only typed `ChildScopes`** — miss `_additionalChildScopes`; use `GetAllChildScopes()`.
2. **Adding nested class/function via generic `AddChildScope` only** — prefer parent marker `AddObjectDeclarationChildScope` / `AddFunctionDeclarationChildScope`.
3. **Putting class constants into `Members`** — use `Constants` / `RegisterObjectMember` paths; keep `$` on property keys.
4. **Expecting generic type parameters to appear as ordinary scope children** — they live on `GenericParameters` lists; resolve via `ResolveGenericTypeParameter`.
5. **Treating operator overloads like unique-named methods** — they bypass name indexes on purpose.
6. **Resolving unqualified names during Pass 1** — unsafe until all files/namespaces/`use`s are registered (see Story 31 plan); Pass 2 exists for this.
7. **Mutating cached tyhpdef ASTs across compiles** — cache returns fresh trees; still avoid storing binder state on shared static ASTs.
8. **Forgetting declaration-before-`IStatement` in AST walks** — nested function/class decls implement `IStatement` and can be mis-handled as bare statements.
9. **Duplicate symbol checks across files** — uniqueness is sibling FileScopes / NamespaceBlockScopes, not only the current scope’s index.
10. **Assuming binder resolves all expression names** — variables/calls in expressions are largely a checker concern.
11. **Breaking PHP case rules** — constants case-sensitive; functions/classes case-insensitive.
12. **Synthetic extension naming collisions** — `__TyhpInlineExt_{Name}` must remain unique in the owning file/namespace scope.

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
| Tyhpdef binding | `TyhpBinder.Tyhpdef.cs`, `TyhpdefSymbolRegistrar.cs`, `BuiltIn/Tyhpdef*.cs` |
| Resolution pass | `TyhpBinder.Resolution.cs` |
| Name/type/member resolution | `Resolution/NameResolver.cs` |
| Scope storage / PHP indexes | `Scopes/BaseScope.cs` |
| Pipeline hook | `Domain/Services/CompilationService.cs` |

---

*Generated from source review of `Tyhp/TyhpLang/Binder/**` and related call sites/tests/docs. Prefer the `.cs` files if this guide and older sketches disagree.*
