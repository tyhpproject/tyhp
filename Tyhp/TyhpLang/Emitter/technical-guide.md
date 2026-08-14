# Tyhp Emitter — Developer Technical Guide

This guide explains how the Tyhp emitter turns a bound, checked AST into PHP source. It is grounded in the code under `Tyhp/TyhpLang/Emitter/` and the emitter tests under `tests/Tyhp.Tests/Emitter/`. Prefer this document over the older `readme.md` in the same folder when they disagree; `readme.md` still describes removed designs (e.g. `@tyhpEmitterStart` templates) and outdated Story 11 placeholders.

---

## 1. Overview / role in the pipeline

### Compilation pipeline position

The Tyhp compile pipeline is:

**Parse → Bind → Check → Optimize (Story 23, currently a no-op) → Emit → Write files**

Emission is step 8 in `Tyhp/CLI/BuildAction.cs`. The CLI builds an `EmitContext` from the compilation result (global scope, diagnostics, project config, and checker-produced flag sets), constructs `TyhpEmitter`, and calls `Emit(parsedFiles)`. Disk writing is a separate step (`OutputWriterService`); the emitter only fills `PHPOutputFile.GeneratedContent` and related metadata.

### What the emitter is responsible for

- Splitting one or more `SrcFileAst` trees into one PHP file per PSR-4 object, namespace functions file, `declare(output_file=…)`, or entry-point script.
- AST-level rewriting of Tyhp-only constructs into PHP-compatible forms (`AliasConverter` and helpers).
- Walking the rewritten AST and building an `EmitItem` tree of PHP text fragments.
- Rendering that tree into PSR-12-oriented PHP strings (opening tag, declares, namespace, imports, body).
- Recording runtime Composer packages required by emitted code (`EmitContext.RequiredPackages`, e.g. `tyhp/core`, `tyhp/async`).

### What the emitter is *not*

- It is not the binder or checker. It **consumes** `BoundSymbol` on AST nodes and checker flag sets; it does not re-typecheck.
- It does not own authoritative disk I/O in the build path (`OutputFileWriter` is a legacy/minimal writer; Story 10’s `OutputWriterService` writes in the CLI).
- There is **no** `IEmitTransformer` / `Transformers/` dispatch layer. Story 11’s accepted ADR (see `IMPLEMENTATION_PLAN_TODO_STORY_11.md`) is an **inline emitter**: `EmitNode` switches to `EmitX` methods on `TyhpEmitter` partials, calling focused helpers as needed.

### Design stance

Features compose on a single node (e.g. `clone $obj with [prop => new Struct() with […]]`). A one-transformer-owns-the-node model fought that composition; the inline walk can call struct, `with`, generics, and operator helpers from the same site.

---

## 2. Entry points and emission orchestration

### Public entry

```csharp
public TyhpEmitter(EmitContext context)
public IReadOnlyList<PHPOutputFile> Emit(IEnumerable<SrcFileAst> parsedFiles)
```

Defined in `TyhpEmitter.cs`.

### CLI wiring

`BuildAction` creates context via `EmitContext.Create(...)`, passing:

| Argument | Source |
| --- | --- |
| `GlobalScope` | Binder |
| `DiagnosticBag` | Shared bag |
| `Project` | Config (`output.*`, `build.*`, PHP version) |
| `RequiresRuntimeGenericTracking` | Checker (Mechanism C classes) |
| `RequiresWeakReferenceCapture` | Checker (closures capturing `$this` into properties) |
| `InferredClosureSignatures` | Checker (contextual param/return types for closures that omitted authored annotations) |
| `RequiresDisposableTryFinally` | Checker (circular disposable fallback) |
| `AsyncForeachKinds` | Checker (`PromiseIterable` / async-iterator kinds) |
| `RequiresGenericVariant` | Checker (Mechanism D callables) |
| `GenericCallTargets` | Checker (`PhpCallAst` → callee for binder routing) |

### `Emit()` phase sequence (actual code)

Order in `TyhpEmitter.Emit()`:

1. **`SplitSourceFile`** — `PHPOutputFile.FromAstTree` → `PHPOutputFileSplitter.Split`
2. **`ConvertAliasesForAll`** — per-file `AliasConverter.Convert`
3. **`MergeOutputFiles`** — merge non-PSR4 files that share the same output path
4. **`BuildEmitTrees`** — build `EmitItem` trees via `EmitNode`
5. **`PruneImportsForAll`** — drop unused / erased / FQ-static-call imports; fold `AdditionalImports`
6. **`GenerateAll`** — `PHPOutputFile.Generate` → `GeneratedContent`

> Note: `IMPLEMENTATION_PLAN_TODO_STORY_11.md` ADR text lists Merge after Build/Prune. The **implemented** order merges **before** building emit trees so merged statement lists are what the walk sees.

### Per-file emit tree build

`BuildEmitTree`:

1. Creates root `EmitItem` (`EmitType.FileHeader`).
2. Emits file-level declares into the root.
3. Emits statement-style or block-style namespace (sets `CurrentNamespace` / `CurrentSourceNamespace`).
4. Emits import lists under the content parent.
5. Emits statements. For entry points that contain `await`, may wrap **executable** top-level statements in `\Tyhp\Promise::run(function () { … })` while leaving declarations outside (`ShouldWrapEntryPointInPromiseRun` in `TyhpEmitter.Async.cs`).

### `EmitNode` dispatch

`EmitNode` is a two-stage switch:

1. Declarations / structural nodes (namespace, import, object, extension, function, const, declare, method, property, trait use, enum case). Struct and type-alias declarations emit **empty** items (erased at file level; usages rewritten earlier).
2. Else `IStatement` → `EmitStatement`.
3. Else diagnostic `EmitterUnsupportedAstNode` and a `/* TYHP: unsupported construct */` comment line.

Expressions are usually **string-built** (`BuildExpression`) and attached as statement lines, not always as separate `EmitItem` nodes—except where a statement wrapper is needed.

---

## 3. Folder / file map

### Orchestration and primitives

| File | Role |
| --- | --- |
| `TyhpEmitter.cs` | Constructor, `Emit()` pipeline, `EmitNode`, merge, entry-point Promise wrapping |
| `EmitContext.cs` / `EmitConfig` | Shared emit state, alias maps, checker flags, package requirements, disposable/async helpers |
| `EmitItem.cs` | Tree of PHP text segments; `emit(indent)` renders; children sorted by `EmitType` then source order |
| `PHPOutputFile.cs` | One output file’s AST slices + generate/prune/merge |
| `PHPOutputFileSplitter.cs` | Split source AST into output-file units |
| `OutputPathResolver.cs` | PSR-4 object paths, `_functions.php`, entry-point path, `output_file` paths |
| `AstWalker.cs` | Pre-order walk; `TransformTree` (children first, optional pre-transform) |
| `OutputFileWriter.cs` | Legacy disk writer (CLI uses `OutputWriterService`) |

### `TyhpEmitter.*` partials

| Partial | Responsibility |
| --- | --- |
| `TyhpEmitter.Declarations.cs` | Namespaces, imports, classes/interfaces/traits/enums, extensions, functions/methods/properties, overload-signature erasure |
| `TyhpEmitter.Expressions.cs` | Expression spelling: calls, `new`, member access, `nameof`/`typeof`/`default`/`variable_exists`, typed vars, etc. |
| `TyhpEmitter.Statements.cs` | Control flow, `using` blocks/calls, typed-var statements, switches/match |
| `TyhpEmitter.Types.cs` | Thin wrapper over `TypeSpellingHelper.Spell` |
| `TyhpEmitter.Helpers.cs` | Doc comments, modifiers, namespace prefix, async detection, diagnostics helpers |
| `TyhpEmitter.Generics.cs` | Mechanism C setup: GenericObject trait use, ctor prologue, runtime type expressions, property type registration helpers |
| `TyhpEmitter.GenericClasses.cs` | Mechanism C chain: `__initGenerics__tyhpGeneric`, parent chain args, generic factory `new_<MangledFqn>__tyhpGeneric` |
| `TyhpEmitter.GenericVariants.cs` | Mechanism D: wrapper + `__tyhpGeneric` Closure binder; call-site `binder(types…)(values…)` |
| `TyhpEmitter.PropertyAccessors.cs` | PHP &lt; 8.4 property-hook polyfill (UsesPropertyAccessors / PropertyAccessor) |
| `TyhpEmitter.OperatorOverloads.cs` | Collapse operator forms into static methods (+ convert to/from) |
| `TyhpEmitter.Async.cs` | Async foreach desugaring, async method wrapping, entry-point `Promise::run` |
| `TyhpEmitter.Disposables.cs` | `:=` / DisposableScope emission, WeakReference `$this` capture, try/finally fallback |

### Feature helpers (non-partial)

| File | Responsibility |
| --- | --- |
| `AliasConverter.cs` | Pre-emit AST rewrite: aliases, structs, extension calls, operators, `with`, magic constants, types, PropertyPath / Expression |
| `PropertyPathEmissionHelper.cs` | Story 16 Phase 1: `fn` → `new \Tyhp\PropertyPath(...)`; `\Closure` targets extract `->callable` |
| `ExpressionTreeEmissionHelper.cs` | Story 16 Phase 2: `fn` → `new \Tyhp\Expression(...)` with nested `\Tyhp\Expression\*` nodes |
| `StructEmissionHelper.cs` | Struct → array (or custom backing) construction/access/`with`/`clone` |
| `WithKeywordHelper.cs` | Object-form `with` → ObjectHelper / PHP 8.5 `clone()` / assignments / experimental readonly wrappers |
| `TypeSpellingHelper.cs` | PHP type-hint spelling: erase generics, expand aliases, structs → `array`, etc. |
| `EmitHelpers.cs` | Unique vars, `EmitPhpTypeHint`, `IsStructType`, `IsExtensionMethodCall` |
| `OperatorOverloadResolver.cs` | Pick matching operator form at call sites (emit) and for checker return-type inference |
| `OperatorMethodNameGenerator.cs` | Deterministic `__add`, `__toString`, … names |
| `NameGeneration/TypeNameFormatter.cs` | Type segments for convert-to method names (`Foo|Bar` → `FooOrBar`, generics → `Of…`) |
| `EmittedFqnHelper.cs` | Binder FQN → emitted FQN with `output.namespacePrefix` (skip tyhpdef / runtime packages) |
| `DeclarationExistenceGateHelper.cs` | `if (!function_exists(…)) { function … }` gates: move with declaration, rewrite gate args |
| `GeneratedNames` (`Tyhp/TyhpLang/GeneratedNames.cs`) | Shared compiler-generated PHP names (`__tyhpGeneric`, `__initGenerics__tyhpGeneric`, factories) — reserved by the checker too |

### `PHP8.3/`

Placeholder files (`ClassFile.cs`, `FunctionFile.cs`, `ObjectDefinition.cs`, members) are **empty**. Version-specific behavior lives in `EmitContext.IsPhpVersionAtLeast` and feature modules (e.g. property accessors), not in this subdirectory yet.

### Tests map (high signal)

| Test area | File |
| --- | --- |
| Pipeline / basics | `EmitterTests.cs`, `EmitterEndToEndTests.cs` |
| Mechanism C | `MechanismCEmitterTests.cs`, `GenericObjectEmitterTests.cs` |
| Mechanism D | `GenericVariantEmitterTests.cs` |
| Property hooks | `PropertyHookEmitterTests.cs` (native 8.4+ vs polyfill 8.2) |
| Pipe `\|\>` | `PipeOperatorEmitterTests.cs` (native 8.5 vs nested-call lowering 8.2/8.4) |
| Operators / extensions | `OperatorOverloadEmitterTests.cs`, `ExtensionMethodEmitterTests.cs`, `CallSiteRewriteEmitterTests.cs` |
| Structs / with | `StructEmitterTests.cs`, `WithKeywordEmitterTests.cs` |
| Async / disposables | `AsyncAwaitEmitterTests.cs`, `UsingBlockEmitterTests.cs`, `Disposable*EmitterTests.cs` |
| Naming / imports | `TypeNameFormatterTests.cs`, `ImportConsolidationTests.cs`, `RelativeQualifiedNameEmitterTests.cs` |

---

## 4. How AST / bound info becomes PHP

### Data flow

```
SrcFileAst (bound)
  → PHPOutputFileSplitter  → List<PHPOutputFile>  (path + statement slices)
  → AliasConverter         → mutated AST on each PHPOutputFile
  → Merge                  → combined statement lists for shared paths
  → EmitNode walk          → EmitItem tree (StartContent / Children / EndContent)
  → Prune imports          → FileImports + emit-item use lines cleaned
  → PHPOutputFile.Generate → GeneratedContent string
```

### Bound symbols

- `EmitContext.GetSymbolForAst(node)` → `node.BoundSymbol`.
- Alias conversion and expression builders consult bound symbols for extension methods, operator overloads, structs, object types, and FQN emission.
- Some constructs are **intentionally unbound** by the binder/checker for emit-specific reasons (notably many `typeof` arguments — see `BuildTypeofExpression` comments referencing `CompileTimeRule.CheckTypeof`). The emitter then resolves names against `GlobalScope` or treats them as generic parameters.

### `EmitItem` rendering model

- Factories: `Line`, `Block`, `BlockBraceNextLine` (PSR-12 brace-on-next-line for named types/methods), `MultiLine`, `Empty`, `AttachDocComment`.
- `SortedChildren()` orders children by `EmitType` numeric value, then original index — so trait uses, constants, properties, constructor, methods land in a stable class-member order even if collected interleaved.
- `emit(indentLevel)` joins non-empty segments with newlines and indents embedded multiline content (closures, switches). Sibling children must never be concatenated without a separator — that previously glued statements into `$a = 1;    $b = 2;` (indent looked like mid-line spacing) and adjacent braces into `}function …`.
- `PHPOutputFile.AppendBodyChildren` joins top-level declarations with a blank line (`\n\n`) so multiple functions/classes in one output file stay PSR-12 separated.

### `EmitType` (member ordering)

Defined in `Tyhp/TyhpLang/Enum/EmitType.cs`. Important class-body values (ascending):

`ObjectTraitUse` → constants → static props → instance props → constructor → destructor → static methods → instance methods → function statements…

`EmitGenericInitHook` uses `ObjectInstanceMethods`; the factory uses a static method emit type. Trait injection uses `ObjectTraitUse` so PSR-12 blank-line-after-trait-use can fire in `EmitItem.emit`.

### Type hints vs runtime types

- **Signatures / hints:** `TypeSpellingHelper.Spell` / `BuildTypeExpression` — erase generics to constraints or `mixed`, expand aliases, structs → `array` (or rewritten backing FQN). Union and intersection separators are spaced per PSR-12 §6.2 (`int | string`, `A & B`). Closure and arrow return/parameter types are included: the binder resolves those annotations onto `GenericTypeParameterSymbol`, and spelling erases them the same way as method signatures (bare `fn(): T` must never reach PHP). When a classic / arrow closure omits an authored param or return type but call-site / annotation contextual typing recovered one, `EmitContext.InferredClosureSignatures` (from the checker) supplies that `ICheckedType` and `TypeSpellingHelper.SpellCheckedType` spells it with the same erasure rules — so `_async<array<…>>(function () { … })` emits `: array` even though Tyhp source left the return blank. Pure `mixed` inferences are omitted (no stronger PHP surface than an untyped param/return). **PHPDoc / SA surface:** `TypeSpellingHelper.SpellForPhpDoc` / `BuildPhpDocTypeExpression` keep bare type-parameter names and type-argument lists (used by polyfill `@property*` tags on generic hosts).
- **Runtime reflection values:** `BuildRuntimeTypeExpression` (in generics partials) produces `\Tyhp\Type` / `\Tyhp\NamedType` expressions for `typeof`, property registration, Mechanism D binders, and optional runtime checks. Class names inside `Type::generic` / `fromClassName` use the bound object's FQCN (`ResolveRuntimeClassName`) so same-namespace unqualified spellings (e.g. `Deferred` in `namespace Tyhp`) emit `\Tyhp\Deferred::class`, not `\Deferred::class`. A free type parameter never becomes `Type::fromClassName(T::class)` — it uses the Mechanism D capture, the class GenericObject lookup, or erased `Type::mixed()` when the binding is unavailable in the current emit context. While emitting a method or function, `_currentCallableGenericParamNames` records that callable's own type-parameter names so unbound call-site type args (e.g. `other<T>(…)` inside `wrap<T>`) still erase instead of spelling a fake class.

### File splitting rules (summary)

`PHPOutputFileSplitter`:

- **Named object types** → one PSR-4 file via `OutputPathResolver.ResolveObjectPath`.
- **Functions / consts / ungated root code in a namespace** → `…/Namespace/_functions.php` (existence-gated functions sorted last).
- **`declare(output_file="…")`** → force single output path for subsequent content.
- **Entry points** (top-level executable code without `output_file`) → path mirroring source under output (`ResolveEntryPointPath`).
- **`TyhpStructDeclAst` / `TyhpTypeAliasAst`** → no output file of their own (erased).
- **Existence gates** (`if (!function_exists('…')) { function … }`) travel with the gated declaration.

---

## 5. Generics emission strategies

Tyhp has no PHP generics. Emission uses three related strategies documented in `FOUND_BUGS.md` and implemented in the generics partials.

### Type erasure (always)

In type-hint positions, generic parameters become their constraint or `mixed` (`TypeSpellingHelper`). Generic argument lists are stripped from class/function names (`StripGenericsFromName`). Union/intersection spellings collapse when `mixed` appears. Intersection constraints are normalized to PHP-legal forms: class/interface conjuncts are kept (`Foo & Bar`); illegal members (`object`, `array` from structs, `callable`, scalars) are dropped, with fallback to a single useful builtin so `object&TProperties` (→ `object&array`) erases to `object` rather than `mixed` (e.g. `ObjectHelper::with`).

### Mechanism C — class / instance generics (`TyhpEmitter.Generics.cs` + `GenericClasses.cs`)

Used when the checker marks an `ObjectDeclarationSymbol` in `RequiresRuntimeGenericTracking` (e.g. the class uses `typeof(T)` / `default(T)`, or participates in a tracked generic inheritance chain).

**Shape:**

1. Apply `\Tyhp\Concerns\HasGenerics` **once**, at the **topmost** generic level in the chain (`ShouldApplyGenericObjectTrait`). The trait hosts a public `\Tyhp\GenericObject $__tyhpGeneric` bag created in `__bootTrait_Tyhp_Concerns_HasGenerics` and composes `BootsTraits` for `tyhpBootTraits()`. Lower levels must not re-apply the trait.
2. Each level in the chain emits `protected function __initGenerics__tyhpGeneric(?\Tyhp\Type ...$generics): void` (`GeneratedNames.GenericInitHook`). The hook starts with `$this->tyhpBootTraits();` so factories that use `newInstanceWithoutConstructor()` still create the bag.
3. The hook records this level’s bindings keyed by declaring class FQN, registers generic-typed property types, then either `parent::__initGenerics__tyhpGeneric(...)` or `$this->__tyhpGeneric->markBound()`.
4. Every constructor (author or synthesized) gets a prologue:
   ```php
   $this->tyhpBootTraits();
   if ($this->__tyhpGeneric->needsInit()) {
       self::__initGenerics__tyhpGeneric(/* nulls for own arity */);
   }
   ```
   Uses `self::` (not `$this->`) so a derived override is not dispatched with the wrong argument list.
5. Instantiation of a tracked generic class goes through a generated factory `new_<MangledFqn>__tyhpGeneric` (Reflection `newInstanceWithoutConstructor` + init hook + constructor), not bare `new` with type args on the class name.
6. **Free generic property set checks:** properties (and promoted params) typed exactly `T` or `?T` (a class generic parameter — not `Promise<T>`, `array<T>`, etc.) get an always-on runtime set check when not `readonly` and when the author did not already write hooks:
   - **PHP ≥ 8.4:** a synthetic native set hook: `set(mixed $value) { $this->__tyhpGeneric->checkProperty('name', $value); $this->name = $value; }`
   - **PHP &lt; 8.4:** a PropertyAccessor registration with `Type::mixed()` and a set method (`__set_<prop>__tyhpPropertyHook`) that calls `checkProperty` + `__tyhpPropertyHook->setBacking` (PA's own type check is a no-op; the real check is in the method)
   - Checks are gated inside the bag until constructors end with `$this->__tyhpGeneric->enablePropertyChecks()` so promoted / ctor-body writes stay unchecked
7. Instance lookups prefer `$this->__tyhpGeneric->resolvedType(Class::class, 'T')` / `$this->__tyhpGeneric->defaultValue(...)` over the verbose `genericType(…)?->getUnderlyingType() ?? Type::mixed()` form. `genericType` remains when the `NamedType` itself is needed (e.g. `new ($this->__tyhpGeneric->genericType(…)->getUnderlyingType()->getName())`).

**Chain edge cases (from code comments / MechanismCEmitterTests):**

- A non-generic class between two generic ones still gets an init hook so each level only talks to its immediate parent.
- Same parameter name `T` on Leaf and Base coexist because bindings are keyed by declaring class.
- Defaults fill omitted type args when chaining (e.g. `Base::TOther = bool`).

Requires package `tyhp/core`.

### Mechanism D — function / method generics (`TyhpEmitter.GenericVariants.cs`)

Supersedes Mechanism A for callable generics. The **emitted** shape is D
(`__tyhpGeneric` Closure binder + curried call sites). Shared ABI names
(`RequiresGenericVariant`, `__tyhpGeneric`) are intentional legacy — see
`CHECKER_GAPS.md` Mechanism A residual audit.

When the checker marks a callable in `RequiresGenericVariant`:

1. **Wrapper** under the declared name: same value signature; body delegates into the binder. Type args are `null`, or `Type::fromCallableReturn($param)` when a value parameter is typed `callable<…, T>` / `Closure<…, T>` matching a binder generic (so PHP callers that omit `<T>` still bind from the callable’s reflected return type).
2. **Binder** named `Name__tyhpGeneric` taking only `?\Tyhp\Type $__generic_<Param>` parameters and returning `\Closure` with the author’s value signature. **Type-arg defaults / null-coalescing run on the binder itself** (above `return function …`), then the Closure captures the settled `$__generic_*` values; the author’s body lives inside that Closure.
3. Interfaces / abstract methods emit **both** signatures so implementors must declare the binder.
4. Call sites flagged in `GenericCallTargets` emit `callee__tyhpGeneric(typeArgs…)(valueArgs…)` via `TryBuildGenericVariantCall`.

`typeof(T)` / `default(T)` inside a variant read the captured `$__generic_T` parameters (`BuildVariantTypeofLookup` / `BuildVariantDefaultLookup`), not the instance registry — which is why free functions and static methods can use their own generics at runtime.

`new self<T>(…)` / bare `new static(…)` inside a flagged body routes through the current class’s Mechanism C factory when the class is in the generic chain, so method generics bind onto the constructed instance (e.g. `Promise::_async` with `: self<T>`). Parameterized `new static<…>` is rejected by the checker (TYHP4168).

`instanceof T` / `is T` reify the same way (`TryBuildReifiedInstanceofCheck`). **Builtin** targets (`is int`, `instanceof string`, and the other `\Tyhp\Type` scalar factories) reify to `\Tyhp\Type::is($x, \Tyhp\Type::int())` because native PHP `instanceof` requires a class name. **Parameterized** targets (`instanceof self<T>`, `instanceof Box<U>`) reify to `\Tyhp\Type::is($x, Type::generic(…))` via `BuildRuntimeGenericFromClassAndArgs` so type arguments are not erased by native PHP `instanceof`. Bare `instanceof static` / `instanceof Foo` stay as PHP `instanceof`. Parameterized `instanceof static<…>` is forbidden.

### Optional runtime checks

`EmitContext.IsRuntimeGenericChecks()` reads `build.runtimeGenericChecks`. When true, constructors / returns may emit `\Tyhp\Type::check` against runtime type expressions (`_currentMethodGenericReturnCheck`, param checks).

### Naming (`GeneratedNames`)

| Constant / helper | Value / pattern |
| --- | --- |
| `GenericVariantSuffix` | `__tyhpGeneric` |
| `GenericVariantParameterPrefix` | `__generic_` |
| `GenericInitHook` | `__initGenerics__tyhpGeneric` |
| `ReflectedClassField` | `__reflectedClass__tyhpGeneric` |
| `GenericFactory(fqn)` | `new_<MangledFqn>__tyhpGeneric` |
| `PropertyHookGetMethod(prop)` | `__get_<prop>__tyhpPropertyHook` |
| `PropertyHookSetMethod(prop)` | `__set_<prop>__tyhpPropertyHook` |
| `PropertyHookInitHook` | `__initPropertyHooks__tyhpPropertyHook` |
| `ExtensionReceiverThisAlias` | `$this_` (emit-time rename only; not checker-reserved) |

The checker reserves the generic / property-hook names so user declarations cannot collide case-insensitively with generated symbols. `ExtensionReceiverThisAlias` is an emit-only spelling for receivers the author named `$this`.

---

## 6. Property accessors / hooks emission

Owned by `TyhpEmitter.PropertyAccessors.cs`. Gate: `ShouldLowerPropertyAccessors()` ≡ **not** `IsPhpVersionAtLeast(8, 4)`.

### PHP ≥ 8.4 (default `EmitConfig.TargetPhpVersion` is `"8.4"`)

- Native property hook syntax is emitted multiline (PSR-12 / PHPCS): the property line opens `{`, each hook sits on its own indented line, and hook bodies use `BuildMethodBodyInline(..., compact: false)` so statements are not jammed onto one line. Tests assert no trailing `;` after hook blocks and no empty `()` on parameterless `get`. Abstract / interface hooks with a null body (`get;` / `set;` from `VisitPropertyHookBody`) emit a bare semicolon, not `{}`. Hook attributes (`#[…]` before each hook name) emit inline on the hook line. By-ref get (`&get`, ampersand before the hook name) is emitted natively. No `UsesPropertyAccessors` trait. Free object-generic properties without author hooks also receive a synthetic multiline set hook (see Mechanism C §6). Promoted constructor parameters that carry hooks force a multiline parameter list.

### PHP &lt; 8.4 polyfill

Authored `&get` is **not** polyfilled: the checker rejects it with TYHP4167 (`CheckerByRefPropertyGetHookRequiresPhp84`) because magic `__get` cannot return by reference, and lowering to by-value would silently lose aliasing. Raise `output.phpVersion` to ≥ 8.4 for native `&get`, or use a by-value `get` hook.

When the object has hooked properties (including promoted constructor params with hooks, and synthetic free-generic set-check properties) **or** an ancestor lowers hooks (property-hook chain):

1. Inject `use \Tyhp\Concerns\UsesPropertyAccessors;` at the **topmost** hooked level only (descendants inherit the bag). Skip when an ancestor is already in the property-hook chain. Also treats author `HasPropertyAccessors` as already covered.
2. **Do not emit** the hooked property declaration with native hook syntax (`ShouldSkipEmittingHookedProperty` — covers author hooks and synthetic free-generic names). Attributes on those hooks are **stripped** with warning `EmitterAttributeStrippedForPhpVersion` (TYHP5017) — the polyfill cannot preserve ReflectionProperty::getHook attribute semantics.
3. Emit private methods `__get_<prop>__tyhpPropertyHook` / `__set_<prop>__tyhpPropertyHook` for each author (or synthetic) hook body. Registration uses a Mechanism C–style init chain, **not** inline ctor registration for ordinary (non-promoted) properties:
   - Each level in the chain emits `protected function __initPropertyHooks__tyhpPropertyHook(): void` (`GeneratedNames.PropertyHookInitHook`). The hook starts with `$this->tyhpBootTraits();`, early-exits when `isInitialized(self::class)` if this level registers accessors, registers **this level’s** accessors only (`declaringClass: self::class`) via **Mechanism D** on `PropertyAccessorObject::register<TType>` (never `\Tyhp\Generic::bind`), then `parent::__initPropertyHooks__tyhpPropertyHook()` when an ancestor also lowers hooks, or `$this->__tyhpPropertyHook->markBound()` at the topmost level.
   - Ctor prologue: `$this->tyhpBootTraits();` once when not already in a Mechanism C generic chain, then `if ($this->__tyhpPropertyHook->needsInit()) { self::__initPropertyHooks__tyhpPropertyHook(); }` with **`self::`** (not `$this->`). Ancestor ctors that call `parent::__construct` are gated by `needsInit` / `bound`.
   - Promoted hooked params are registered from the constructor **after** the init call (promoted values are not in scope inside the init hook).
   - `EmitSynthesizedPropertyAccessorConstructorIfNeeded` fires for any class in the chain with no author constructor — including a pass-through subclass that adds no hooks of its own but sits below a hooked ancestor. It forwards the inherited constructor's parameter list and calls `parent::__construct(...)` with those forwarded arguments when a constructor is inherited (`TryFindConstructorForEmit` / `BuildForwardingParameterList`, mirroring `EmitSynthesizedGenericConstructor`) — never a bare no-arg `__construct()`, which would silently drop the ancestor constructor's parameters and non-hook body statements instead of PHP's normal "no override ⇒ inherit the constructor" behavior.
   ```php
   private function __get_name__tyhpPropertyHook(): mixed
   {
       …
   }
   private function __set_name__tyhpPropertyHook(string|\Stringable $value): void
   {
       …
   }
   // in __initPropertyHooks__tyhpPropertyHook (non-promoted):
   $this->__tyhpPropertyHook->register__tyhpGeneric(<propertyRuntimeType>)(
       'name',
       $this,
       get: $this->__get_name__tyhpPropertyHook(...),
       set: $this->__set_name__tyhpPropertyHook(...),
       backed: true|false,
       declaringClass: self::class,
       visibility: 'protected'|'private', // when property visibility is not public
       setVisibility: 'protected'|'private', // asymmetric *(set), else same as visibility
       setAcceptType: <setParamRuntimeType>, // only when set declares an explicit parameter type
       finalGet: true, // when authored `final get`
       finalSet: true, // when authored `final set`
       …);
   ```
   `register<TType>` builds `new PropertyAccessor<TType>(…)`, fills `propertyName` from the first argument, and defaults `$shadowInherited` to `true`. Always pass `declaringClass: self::class` from the declaring type — `$host::class` is wrong when a parent constructor runs on a child instance.
   The bag keys accessors by declaring class so parent and child hooks for the same property coexist. Live `__get`/`__set` use the most-derived accessor (first registration wins — child init runs before parent). `parentGet`/`parentSet` invoke the ancestor accessor’s `get()`/`set()` when present (parent hooks), else the live accessor’s backing (plain-parent override), else Reflection.
   Visibility (PHP 8.4 parity for polyfill magic):
   - `visibility` is the property (get) visibility; `PropertyAccessor::get` / `isset` enforce it via `debug_backtrace` (skipping `__get`/`tyhpTry*`/closure/`PropertyAccessorObject`/`Tyhp\Concerns\*` frames). Inaccessible `isset` returns `false` (PHP); inaccessible get throws `\Error`.
   - `setVisibility` is asymmetric `*(set)` when authored, otherwise the same as property visibility. `PropertyAccessor::set` enforces it the same way (message uses `protected(set)` / `private(set)` wording).
   Set-hook parameter typing (PHP 8.4 contravariance):
   - Polyfill `__set_*` methods spell the authored set parameter type when present; untyped set params spell the **property** type (PHP's default), not `mixed`.
   - When the set hook declares an explicit parameter type, registration also passes `setAcceptType:` so `PropertyAccessor::set` type-checks against that accept type (which may be wider than property `TValue`) before invoking the set closure. Property `TValue` is still enforced when the hook writes via `setBacking`.
   - Untyped set hooks omit `setAcceptType`; PA defaults the accept check to `TValue`.
   Final hooks (PHP 8.4 `final get` / `final set`):
   - Registration passes `finalGet: true` / `finalSet: true` when the authored hook has the `final` modifier.
   - `PropertyAccessorObject::register` rejects a descendant that supplies the same hook (child-first init: when a parent with a final flag registers after a live descendant that already has that hook; or when a child registers after an ancestor accessor is already present). Throws `\Error` with PHP's wording: `Cannot override final property hook Class::$prop::get()`.
   - Omitting the other hook is still allowed (e.g. child may add `get` when only parent `set` is `final`).
   Defaults: emit `defaultValue: <expr>` when the default is non-null; emit `defaultValueIsNull: true` for `= null`; for promoted params emit both `defaultValue: $arg` and `defaultValueIsNull: $arg === null` so a runtime null still initializes backing. Do not emit the old `hasDefault` flag.
   Synthetic free-generic set checks register with `Type::mixed()` and a set-only method that calls `$this->__tyhpGeneric->checkProperty` then `__tyhpPropertyHook->setBacking` (no get hook; backed default get).
4. While emitting hook method bodies, `_hookBackingPropertyName` is set so `$this->prop` reads/writes become `$this->__tyhpPropertyHook->getBacking('prop', self::class)` / `setBacking(..., self::class)` **only for that property** (separate fields like `$_name` are left alone). The declaring-class argument selects which level's accessor type-checks / labels the access; backing storage itself is a single shared cell per property name on `PropertyAccessorObject` (PHP 8.4: one physical slot across the whole get/set override chain, including partial get-only / set-only overrides).
5. `backed` matches PHP 8.4: true when there is a default, a self-reference (`$this->prop`) in any hook, arrow `set => expr` (implicit write to storage), a synthetic free-generic set check, **or** the property redeclares a plain (non-hooked) ancestor property (inherited storage — omitted get/set still default-read/write that storage). Omitting `get` or `set` alone does **not** force backed on a virtual property — the omitted operation does not exist (read-only / write-only).
6. Class PHPDoc gets `@property` / `@property-read` / `@property-write` tags for each **public** lowered property so Phan/PHPStan/Psalm see magic properties (always, not gated by `IncludeComments`). Non-public (`protected`/`private`) hooked props are omitted so SA does not advertise them as public magic API. On a **generic** host class, the emitter also inserts `@template` / `@template-covariant` / `@template-contravariant` tags for the class's own type parameters (skipped when the author already wrote one) and spells property types via `TypeSpellingHelper.SpellForPhpDoc` so bare parameters stay as `TValue` (not erased `mixed`) and parameterized types keep their argument list (`\Probe\Box<TValue>`). Runtime PHP typehints still use ordinary `Spell` erasure.
7. **Child override of a plain parent property** (e.g. `PositivePoint::$x` over `Point::$x = 0`): emit Reflection capture of the inherited instance value into `$__tyhp_inherited_<prop>` / `$__tyhp_inherited_<prop>_isNull` **before** `register` (which shadows via `unset`), pass those as `defaultValue` / `defaultValueIsNull`, force `backed: true`, and emit `@property`. Matches `PropertyHookPolyfillSmokeTest` / PHP 8.4. **Child override of a hooked parent** uses the same init chain + declaring-class-keyed bag so `parent::$prop::get/set` invoke the parent level’s hooks. `TryFindInheritedPlainProperty` stops without matching (same as no ancestor property at all) when the nearest same-named ancestor property is `private` — a private property is not inherited storage, so PHP treats the child's declaration as a brand-new, unrelated property rather than an override.
8. `PropertyAccessorObject::register` also auto-captures / forces `backed` when shadowing finds parent storage and the caller omitted an explicit default — defense in depth for hand-written polyfill sites.

Requires `tyhp/core`. Covered by `PropertyHookEmitterTests` (`Emit_Php82_*` vs `Emit_Php84_StillUsesNativeHooks`).

`AliasConverter` explicitly does **not** lower hooks; a comment points at this partial.

---

## 6a. Version-gated rewrite matrix (PHP 8.4 / 8.5)

Story 14.5 gates several PHP surface forms on `EmitContext.IsPhpVersionAtLeast`. When the
target is at or above the introducing minor, emit the native spelling; otherwise rewrite,
omit, or strip as in the table. Detail sections follow (§6 / §6b–§6f).

| Feature | Native when | Lower-target rewrite | Owner |
| --- | --- | --- | --- |
| Property hooks / aviz | ≥ 8.4 | Polyfill via `UsesPropertyAccessors` (§6); hook `#[…]` stripped + TYHP5017 (§6f) | `PropertyAccessors` |
| Pipe `\|\>` | ≥ 8.5 | Nested single-arg call chain, left-to-right (§6b) | `BuildPipeExpression` |
| `(void) expr` | ≥ 8.5 | Omit cast; emit operand alone (§6c) | `BuildVoidCastExpression` |
| `clone(…)` / clone-with / FCC | ≥ 8.5 | Unary pass-through; ObjectHelper / static arrow (§6d) | `BuildCloneExpression` + `WithKeywordHelper` |
| `exit` / `die` named + FCC | ≥ 8.4 | Positional / bare / static arrow (§6e) | `BuildExitDieExpression` |
| Top-level `const` attributes | ≥ 8.5 | Strip `#[…]` + TYHP5017; keep `const` (§6f) | `EmitConstDeclaration` |

Grammar parse shapes for these forms are in `Grammar/technical-guide.md` §7.

---

## 6b. Pipe operator `|>` emission

Owned by `BuildPipeExpression` in `TyhpEmitter.Expressions.cs`. Gate: `IsPhpVersionAtLeast(8, 5)`.

| Source | ≥ 8.5 (native) | &lt; 8.5 (lowered) |
| --- | --- | --- |
| `$a \|\> foo(...)` | `$a \|\> foo(...)` | `foo($a)` (FCC unwrap) |
| `$a \|\> f(...) \|\> g(...)` | `$a \|\> f(...) \|\> g(...)` | `g(f($a))` (left-assoc nest) |
| `$a \|\> (fn($x) => …)` | `$a \|\> (fn($x) => …)` | `(fn($x) => …)($a)` |
| `$a \|\> $callable` | `$a \|\> $callable` | `$callable($a)` |
| `$a \|\> (fn($x) => …) \|\> g(...)` | native chain | `g((fn($x) => …)($a))` |

Nested binary/ternary on the **left** is parenthesized when needed. Arrow / closure RHS is
always parenthesized on the native path — PHP forbids bare `fn`/`function` after `|>`, and
`BuildExpression` would otherwise drop grouping parens around non-binary RHS. On the lower
path, FCC forms unwrap to a direct call on the callee spelling; other callables that are not
valid bare invoke forms (arrows, closures, operator results) are parenthesized before `(…)`.
Matches the PHP manual’s nested-call equivalence for single-arg callables.

Covered by `PipeOperatorEmitterTests`.

---

## 6c. `(void)` cast emission

Owned by `BuildVoidCastExpression` in `TyhpEmitter.Expressions.cs` (via `BuildUnaryExpression`).
Gate: `IsPhpVersionAtLeast(8, 5)`.

`(void)` is not a value-producing cast (see Grammar §7). Statement form and for-list items
share the same unary AST path. Native emit preserves source cast spelling and spaces after
the cast per PSR-12 §6.1. On &lt; 8.5 the cast token is unknown — omit it; the discard has no
runtime effect beyond evaluating the expression.

| Source | ≥ 8.5 (native) | &lt; 8.5 (lowered) |
| --- | --- | --- |
| `(void)$x;` | `(void) $x;` | `$x;` |
| `(void)\strlen($s);` | `(void) \strlen($s);` | `\strlen($s);` |
| `for ((void)$a; …; (void)$i++)` | `for ((void) $a; …; (void) $i++)` | `for ($a; …; $i++)` |
| `for (; (void)side(), $cond; )` | `for (; (void) side(), $cond; )` | `for (; side(), $cond; )` |

Covered by `VoidCastEmitterTests`.

---

## 6d. `clone` call / clone-with emission

Owned by `BuildCloneExpression` in `TyhpEmitter.Expressions.cs` (via `BuildUnaryExpression`),
with lowerings from `WithKeywordHelper.RewriteCloneKeywordCall` /
`BuildCloneFirstClassCallableLowering`. Gate: `IsPhpVersionAtLeast(8, 5)`.

Unary `clone $x` (including parenthesized `clone($x)` — not a call form) always pass-through.
Native call-shaped `clone(...)` uses no space before `(` (same as
`WithKeywordHelper.BuildNativeCloneCall` for Tyhp `with`). Unpack / empty / unknown named
forms that cannot be rewritten keep call spelling.

| Source | ≥ 8.5 (native) | &lt; 8.5 (lowered) |
| --- | --- | --- |
| `clone $o` / `clone($o)` (unary) | unchanged | unchanged |
| `clone($o,)` / `clone(object: $o)` | `clone($o,)` / `clone(object: $o)` | `clone $o` |
| `clone($o, […])` / named withProperties | native call | `\Tyhp\ObjectHelper::with(clone $o, …)` |
| `clone(...)` FCC | `clone(...)` | `(static fn(object $object, array $withProperties = []) => \Tyhp\ObjectHelper::with(clone $object, $withProperties))` |

Readonly IIFE / experimental clone-with remain the Tyhp `with` AliasConverter path when the
class and override keys are known. Bare PHP `clone($o, $props)` uses ObjectHelper (same as
non-readonly Story 11 clone-with). Requires `tyhp/core` for ObjectHelper / FCC arrow paths.

Covered by `CloneCallEmitterTests`.

---

## 6e. `exit` / `die` emission

Owned by `BuildExitDieExpression` in `TyhpEmitter.Expressions.cs` (via `BuildUnaryExpression`).
Gate: `IsPhpVersionAtLeast(8, 4)` (PHP 8.4 made `exit`/`die` proper functions).

Bare `exit;` / `die;` always omit parentheses. Unpack / unknown named args keep call spelling
(checker already diagnoses bad names).

| Source | ≥ 8.4 (native) | &lt; 8.4 (lowered) |
| --- | --- | --- |
| `exit;` / `die;` | unchanged | unchanged |
| `exit($status)` / `die($status)` | unchanged | unchanged (positional) |
| `exit()` / `die()` | `exit()` / `die()` | bare `exit` / `die` (prefer no empty parens) |
| `exit(status: $s)` / `die(status: $s)` | named call spelling | `exit($s)` / `die($s)` |
| `exit(...)` / `die(...)` FCC | `exit(...)` / `die(...)` | `(static fn(string\|int $status = 0) => exit($status))` (same for `die`) |

`\Closure::fromCallable('exit')` is only valid once `exit` is a real function (≥ 8.4), which
is the native FCC path — the static arrow is the documented equivalent for older targets.

Covered by `ExitDieCallEmitterTests` (legacy keyword smoke: `ExitEmitterTests`).

---

## 6f. Const / hook attribute strip

Attributes that the target PHP version cannot represent on a construct are **stripped** (not
emitted) and diagnosed with warning `EmitterAttributeStrippedForPhpVersion` (TYHP5017). The
declaration / hook body still emits; only the `#[…]` text is dropped. Stripping changes
`Reflection*::getAttributes` semantics, so the diagnostic always fires when attributes were
present.

| Construct | Native when | &lt; gate | Owner |
| --- | --- | --- | --- |
| Top-level / namespace `const` `#[…]` | ≥ 8.5 — `AttachAttributes` on `EmitType.RootStatement` | Strip + TYHP5017 (`targetDescription`: `constant`); `const` line unchanged | `EmitConstDeclaration` |
| Class / object `const` `#[…]` | ≥ 8.0 (all Tyhp targets) | N/A — always attach | `EmitConstDeclaration` |
| Property-hook `#[…]` (inline) | ≥ 8.4 — `FormatInlineAttributes` in native hook block | When hooks are polyfilled (&lt; 8.4): property + hooks omitted from native syntax; `ReportStrippedPropertyHookAttributes` → TYHP5017 per hook | `PropertyAccessors` + `ReportStrippedAttributes` |

| Source (target &lt; gate) | Emitted | Diagnostic |
| --- | --- | --- |
| `#[Attr] const X = 1;` (&lt; 8.5) | `const X = 1;` | TYHP5017 on each attribute |
| `{ #[Attr] get { … } }` on hooked prop (&lt; 8.4 polyfill) | polyfill methods / register; no native hook `#[…]` | TYHP5017 per hook attribute |

Helpers: `AttachAttributes`, `FormatInlineAttributes`, `ReportStrippedAttributes`,
`ReportStrippedPropertyHookAttributes` in `TyhpEmitter.Helpers.cs`.

---

## 7. Alias conversion and naming

### When it runs

Phase 2 of `Emit()`: `PHPOutputFile.ConvertAliases` → `new AliasConverter(context).Convert(this)`.

### What `AliasConverter` does (high level)

1. Collect **protected member names** (anything after `->`) so case-insensitive tyhpdef alias maps cannot rewrite `$this->promise` into a class FQN.
2. Collect **struct / object / typed variable** maps per function-like scope (and a global frame) because many `PhpVariableAst` nodes are unbound at use sites. Typed-variable collection also retains the full declared `ITypeExpression` so operator rewriting can recover `array<T>` / `array<K,V>` element types for `$arr[$i]` (the symbol map alone only stores the erased `array` builtin).
3. Expand statement-context object `with` **in place** before the tree walk (assignments become property-assignment sequences). The same expand pass also statement-splits compound-assign / increment temps and overloaded postfix `++`/`--` used as values.
4. `AstWalker.TransformTree` with `PreTransformWith` + `TransformNode` for the rest. The walk maintains `_functionStack` (per function-like frame for typed-var lookups) and `_classStack` (enclosing `ObjectDeclarationSymbol` for each `PhpObjectTypeDeclAst`) so `$this` resolves to the current class during operator-overload matching (`ResolveOperatorExpressionType`) and extension-method receiver resolution (`ResolveReceiverType`) — `$this` is never in the typed-var maps and is unbound by the binder. `_classStack` reflects the enclosing `PhpObjectTypeDeclAst` being walked, which for a **trait** is the trait's own declaration (method bodies are not inlined per user). Property-typed `$this->prop OP …` still resolves via the trait's declared members; a direct-operand `$this OP other` whose overload is declared only on a composing class is recovered by searching classes/enums that `use` the trait (temporarily pushing each user onto `_classStack` so `$this` matches that user's `self` parameters) and emitting `static::__op(...)` for late static binding — except extension operators, which still target the extension/owner FQN.

### Major rewrite categories

| Concern | Behavior |
| --- | --- |
| Type / tyhpdef aliases | Names and type nodes rewritten via `TypeAliasMap` / `TyhpdefAliasMap` built in `EmitContext.Create`. Replacements **copy `AstGrammarAddons`** so `new Foo<T>()` type args survive a tyhpdef `use` rewrite (otherwise Mechanism C factories get `null` type args). |
| Structs | `new` / property access / `with` / `clone` via `StructEmissionHelper` (array or custom backing from `build.structBacking`) |
| Object `with` | `WithKeywordHelper` (ObjectHelper, PHP 8.5 clone, experimental readonly clone-with; also PHP 8.5 keyword `clone(...)` lowering) |
| Extension methods | Instance call → FQ static call on extension class; tracks FQ static imports for pruning. Receiver resolution (`ResolveReceiverType`) special-cases `$this` via `_classStack` so `$this->extensionMethod(...)` called from inside the extended class's own method also rewrites (`$this` has no `BoundSymbol` / typed-var entry otherwise). |
| Extension `$this` receiver | `EmitExtensionMethod` / static operator branches: when the receiver (or operand) is named `$this`, set `EmitContext.ExtensionReceiverThisAlias` via `ResolveCollisionSafeThisAlias` — normally `GeneratedNames.ExtensionReceiverThisAlias` (`$this_`), or `$this__`/`$this___`/… if a sibling parameter/operand is already named that — spell it in the signature / `$this_ = $l` alias line, and rewrite body / nested-closure `$this` via `BuildVariableExpression` (WeakSelf still wins when active). Convert-to (`EmitConvertTo`) is always an instance method, so a self-operand literally named `$this` skips the alias line entirely instead of reassigning `$this` (PHP forbids `$this = $this;`) |
| Operator overloads | Binary/unary/empty/cast → static/`__to*` calls via `OperatorOverloadResolver` + `OperatorMethodNameGenerator`. Implicit **convert** at call arguments, constructor arguments, and `return` jumps: when the formal parameter/return type is a single (nullable-unwrapped) target and the actual expression's type has a matching convert-to / convert-from overload, rewrite to `$expr->__to{T}()` or `\Type::__from($expr)` (`TryRewriteImplicitConvert`, using `_functionStack` return types and resolved callee `ParameterInfo` lists — including post-extension-rewrite static calls and `new Type(...)`'s `__construct` params via `TryRewriteConstructorArgumentConverts`; both share `RewriteArgumentListConverts` for positional/named argument matching). `ResolveStaticOperatorTarget`: standalone `extension E { operator +<T> }` rewrites to `E::__op` (methods emit on E); tyhpdef inline `extension operator` rewrites to the owner/`ExtensionTargetSymbol` (methods emit on the owner). Call-site selection looks up forms on the left operand's type first, then the right: class/interface via `ObjectDeclarationSymbol.ExtensionContributedOperators` (plus class-level overloads), and builtins/scalars via `BuiltInTypeSymbol.ExtensionContributedOperators` (e.g. `'-' * $n` → `\StringOperators::__multiply('-', $n)` when `operator *<string>` is in scope). When an operand resolves to a **trait** with no matching form, composing classes/enums that `use` the trait are probed (with `_classStack` temporarily set to each user) and a hit emits `static::__op(...)` so the shared trait method late-binds; extension forms still use the extension/owner FQN. Convert-to casts and implicit convert-to at call/return/`new` sites on trait-`$this` also accept a composing class's convert-to (`TraitComposingClassHasConvertToOverload`) and keep the instance call (`$this->__to{T}()`). `OperatorOverloadResolver` treats `self` as the owning class **or** owning builtin when matching forms. `ResolveOperatorExpressionType` recovers array-element types from `array<T>` / `array<K,V>` (bound declared types or collected type-expression maps) so `$arr[$i] += …` / `$arr[$i++] += …` can select overloads and extract by-ref temps; it still refuses to fall through to the array receiver itself. `$this` resolves via `_classStack` (pushed for each `PhpObjectTypeDeclAst`) so `$this->prop += …`, `$this->items[$i] += …`, and `$this->prop + …` rewrite the same way as `$o->prop`. Overloaded postfix `++`/`--` used as a value (`$b = $a++`) is statement-split before the tree walk (`$__old = $a; $a = \Type::__increment($a);` … `$__old`) so the expression yields the prior value; short-circuit / ternary-arm / `else if`-condition / loop-condition sites that cannot be split (conditionally or repeatedly evaluated) report TYHP5019. |
| PropertyPath (Story 16 Phase 1) | In `RewriteArgumentListConverts`: when the parameter type is `\Tyhp\PropertyPath` (BoundSymbol / name; generics may already be erased) and the argument is an arrow `fn` whose body is a property/`?->` chain, rewrite to `new \Tyhp\PropertyPath($sourceType, $resultType, [$segments…], $fn)` via `PropertyPathEmissionHelper` and `RequirePackage("tyhp/lambda")`. A chain with `?->` appends `nullSafeFlags: [bool…]` so the runtime builds `NullSafeAccessExpression` nodes. Source/result strings prefer remaining generic args, else authored fn annotations, else `EmitContext.InferredClosureSignatures`; free type parameters erase to `mixed` rather than spelling `T::class`, and nullable types keep their `?`. When the parameter is `\Closure` and the argument is a PropertyPath/Expression value (typed var or `new`), rewrite to `$arg->callable`. |
| Expression trees (Story 16 Phase 2–3) | After the PropertyPath rewrite attempt, when the parameter type is `\Tyhp\Expression` (not PropertyPath) and the argument is an arrow `fn`, `ExpressionTreeEmissionHelper` walks the body bottom-up into nested `new \Tyhp\Expression\XxxExpression(...)` nodes and wraps `new \Tyhp\Expression(body:, parameters:, callable:, returnType:)`. Per-node type strings come from `EmitContext.ExpressionTypes` (checker memo), falling back to `'mixed'`. Captures emit as `ConstantExpression($var, …)`. `is` / `instanceof` emit `InstanceofExpression` (operand + target type string / `Class::class`); the RHS is not rewritten as a value node. Multi-parameter fns emit one `ParameterExpression` per parameter. PropertyPath call sites stay on the Phase 1 helper. `nameof(fn ($x) => $x->a->b)` is folded by `BuildNameofExpression` to the last segment string — not an Expression construction. Free-function callees are resolved in the current output file's namespace (`ResolveNamespacedFreeFunction`) so `namespace App; sortBy(fn …)` still rewrites. |
| Magic constants | Tyhp `__TYHP_*` → PHP equivalents |
| Builtin / named types | Spell through alias maps; erase structs |
| Symbol-name / algebra / Phase 5 utilities | `TypeSpellingHelper.TryEraseSymbolNameType` / `TryEraseTypeNameAlgebraType` / `TryEraseUtilityType` — `__ClassName`/`__MethodName`/… → `string`; `__TypeName`/`__AsType`/… → `string` or resolved type; `__StructKey`/`__Properties` → `string`; `__StructDef`/`\Tyhp\Parameters`/`__CallableParametersStruct`/`__CallableParametersTuple`/… → `array`; `__CallableParametersRest` → `mixed` (variadic element type; `array` would make PHP demand each unpacked argument be an array); `\Tyhp\ReturnType<callable<…,T>>` / `__CallableReturnType<callable<…,T>>` → `T` (via `CallableSignatureReflection`; unbound `TCallable` → `mixed`) |

### Alias maps

`EmitContext.BuildAliasMaps` walks the bound scope tree:

- `TypeAliasSymbol` / `ObjectTypeAliasSymbol` → spelled PHP type strings (also `Class\Alias` and `self\Alias` keys for object aliases).
- `UseIncludeSymbol` → tyhpdef / `use` alias map (`Name` → `ImportedName`). Still used for
  class/const/variable declaration aliases and file-level `use` imports.
- `FunctionDeclarationSymbol.OriginalPhpName` → same free-name tyhpdef alias map for
  `function php_name as tyhpName` (symbol is registered under `tyhpName`; emit erases to the PHP
  name).
- `ObjectMethodSymbol.OriginalPhpName` → `TyhpdefMemberAliasMap` (member `as` aliases). Member
  positions are protected from the free-name map so a class alias like `Promise` cannot rewrite
  `$this->promise`; they still erase through this dedicated map.

`AliasConverter.TransformTyhpdefAliasName` looks up both the raw spelling and a leading-`\`-stripped
form, and re-anchors the replacement when the source name was root-qualified.

### Emitted naming helpers

- **`EmittedFqnHelper`**: apply `output.namespacePrefix` only to project-owned object declarations (not `.tyhpdef`, `<embedded>`, `runtime/packages/`, or vendor `tyhp_src` / `tyhpdef`).
- **`OutputPathResolver`**: PSR-4 path from (possibly prefixed) FQN; functions → `_functions.php`.
- **`TypeNameFormatter`**: stable segments for `__to{Segment}` convert methods.
- **Import pruning**: after emit, drop erased types, unused imports, and imports only referenced via leading-`\` static calls (`TrackFullyQualifiedStaticCallImport`).

### Operator method names (deterministic)

Examples from `OperatorMethodNameGenerator`: `__add`, `__subtract`, `__isEqual`, `__negate`, `__from` (convert-from), `__toString` / `__toInt` / … / `__to{Formatted}`. Convert-to also drives auto-`implements` of `\Tyhp\Contracts\*Convertible` in `EmitObjectDeclaration`.

---

## 8. Coding conventions and patterns

### Partial class layout

- All emission methods are `private` on `partial class TyhpEmitter`.
- Feature-specific state is instance fields on the main partial (`_currentObject*`, `_pendingOperatorOverloads`, `_hookBackingPropertyName`, `_currentVariantGenericParams`, …) with careful save/restore around nested object emission (anonymous classes).

### Prefer helpers over new transformers

New Tyhp surface area should land as:

1. Checker flags / `BoundSymbol` data when emit needs facts it cannot recover safely, **then**
2. Either an `AliasConverter` rewrite (AST shape change) or an emit-time `Build*`/`Emit*` method (string/EmitItem), **then**
3. A focused static helper if the logic is reusable / testable without the full emitter.

### PHP text conventions in emitted code

- Root-anchor runtime calls: `\Tyhp\…`, `\str_contains`, etc. (matches project PHP style).
- PSR-12: brace next line for named declarations; blank line after trait-use groups; one `use` per import; `declare(strict_types=1)` when configured and not already present.
- Doc comments attached via `ApplyDocComment` when `IncludeComments` is true.
- Attributes attached via `AttachAttributes` onto the declaration item **after** the docblock and **before** the signature (PHP/PHPDoc convention: doc → `#[…]` → declaration). Property-hook attributes use `FormatInlineAttributes` on each hook line inside the multiline hook block. Top-level `const` attributes attach only when `IsPhpVersionAtLeast(8, 5)`; otherwise they are stripped with TYHP5017.

### Diagnostics

- Unimplemented Tyhp constructs: warning `EmitterTyhpConstructNotImplemented` (`ReportTyhpConstructNotImplemented`) rather than crashing.
- Unsupported AST nodes: error `EmitterUnsupportedAstNode`.
- Namespace mismatch on merge: `EmitterNamespaceMismatch`.
- Attributes stripped because the target PHP version cannot represent them on that construct: warning `EmitterAttributeStrippedForPhpVersion` (TYHP5017) — top-level `const` &lt; 8.5; property-hook attributes when hooks are lowered (&lt; 8.4).
- Overloaded postfix `++`/`--` in an expression that cannot be statement-split (short-circuit operand, ternary arm, `else if` condition, or loop condition — anything conditionally or repeatedly evaluated): error `EmitterPostfixOperatorOverloadRequiresStatementSplit` (TYHP5019).

### Async / disposable composition

Block emission enters/exits disposable scope depth on `EmitContext`. Prefer checking checker flags (`RequiresDisposableTryFinallyFor`, `RequiresWeakReferenceCaptureFor`) over heuristics alone.

---

## 9. Helpers and utilities

| Helper | Typical use |
| --- | --- |
| `EmitContext.GenerateUniqueVarName` | Temps (`$__tyhp_0`, scopes, weak self) |
| `EmitContext.GenerateAsyncIterVarName` | `$__asyncIter_1`, … |
| `EmitContext.RequirePackage` | Record `tyhp/core` / `tyhp/async` for composer update |
| `EmitContext.TrackUsedImport` / `AdditionalImports` | Import pruning inputs |
| `AstWalker.Walk` / `TransformTree` | Alias conversion and analysis walks |
| `TypeSpellingHelper.Spell` | All PHP type hints |
| `StructEmissionHelper` | Struct lowering |
| `WithKeywordHelper` | Object `with` lowering |
| `OperatorOverloadResolver` | Call-site form selection |
| `DeclarationExistenceGateHelper` | Movable `*_exists` gates + `__NAMESPACE__ . '\\Name'` emission |
| `EmitHelpers.IsExtensionMethodCall` | Bound-symbol extension detection |
| `OverloadSignatureHelper` (Declarations) | Erase overload signature methods; keep implementation |

### `EmitConfig` knobs that change emission

| Config | Effect |
| --- | --- |
| `TargetPhpVersion` / project PHP version | Property-hook polyfill vs native (+ hook-attribute strip on polyfill); top-level `const` attributes native ≥ 8.5 vs strip + TYHP5017; pipe `\|\>` native vs nested-call lowering; `(void)` native vs omit-cast; call-shaped `clone(...)` native vs ObjectHelper / FCC arrow; `exit`/`die` named / FCC native ≥ 8.4 vs positional / bare / FCC arrow; other version gates via `IsPhpVersionAtLeast` |
| `StrictTypes` | Inject `declare(strict_types=1)` |
| `IncludeComments` | File banner + doc comments |
| `NamespacePrefix` | Emitted namespaces and FQNs for project types |
| `EntryPointAutoloader` / map | `require_once` for entry points; `declare(autoload=…)` override |
| `build.structBacking` | `array` vs custom backing class |
| `build.runtimeGenericChecks` | Extra `\Tyhp\Type::check` |
| `build.experimentalReadonlyCloneWith` | Readonly `clone with` anonymous-class wrapper |

---

## 10. Weirdness / non-obvious choices (and why)

These are evidenced by comments and tests in-tree:

1. **Inline emitter, not transformer list** — composition of overlapping rewrites; see Story 11 ADR.
2. **Merge before BuildEmitTrees** — merged files share one statement list before the emit walk (differs from older ADR prose).
3. **Mechanism C separates generic init from `__construct`** — type-arg binding must run for every ancestor before any author constructor statement; constructor signature (`: parent(...)` vs `: void`) stays author semantics. Variadic constructors stay valid because generics never share the ctor parameter list.
4. **`self::__initGenerics__…` not `$this->`** — avoid virtual dispatch sending one level’s args to another level’s parameters.
5. **GenericObject trait only at top of chain** — avoid duplicate private trait state per level.
6. **Factory name embeds mangled FQN** — short `new_Leaf__tyhpGeneric` would collide across namespaces in one inheritance chain (`Cannot override final method`).
7. **Mechanism D Closure binder** — free/static generics have no `$this` registry; type args become Closure captures. Call shape is curried: types then values.
8. **Protect member names from alias maps** — case-insensitive class alias `Promise` must not rewrite `$this->promise`.
9. **Struct typed-var collection before erasure** — property/`with` rewrites need types after hints are gone; maps are function-scoped to avoid cross-function bleed.
10. **Entry-point autoload after namespace** — PHP requires `namespace` before other statements (except `declare`); `Generate` places `require_once` accordingly.
11. **`typeof` parenthesized lookup** — `( $this->… ?? Type::mixed() )` so casts/`??` precedence do not tear the expression apart.
12. **Operator forms collapse to one static method** — runtime `instanceof`/`is_*` dispatch; checker rejects reserved-name conflicts rather than emitting `_N` suffixes.
13. **`@tyhpEmitterStart` templates removed** — `readme.md` still shows them; Story 11 notes they were removed from design. Do not implement new features that way.
14. **GeneratedNames lives outside Emitter** — checker must reserve the same identifiers.

---

## 11. Interactions with Checker, Binder, and runtime packages

### Binder

- Supplies `GlobalScope`, `BoundSymbol` on nodes, FQNs, generic parameter lists, struct/extension flags, operator overload symbols.
- Alias maps are derived from the bound scope tree at `EmitContext.Create` time.

### Checker → emitter contracts

| Checker output | Emitter use |
| --- | --- |
| `RequiresRuntimeGenericTracking` | Mechanism C trait/hook/factory |
| `RequiresGenericVariant` | Mechanism D dual emission |
| `GenericCallTargets` | Call-site binder routing |
| `RequiresWeakReferenceCapture` | `\WeakReference::create($this)` + body rewrite |
| `InferredClosureSignatures` | Recover omitted closure param/return PHP typehints from contextual typing |
| `EmitContext.ExtensionReceiverThisAlias` | Scoped while emitting an extension method / static operator form whose author used `$this`; not a checker flag — set by the emitter |
| `RequiresDisposableTryFinally` | try/finally instead of DisposableScope for some blocks |
| `AsyncForeachKinds` | Promise-iterable foreach vs async-iterator while |

Without these flags (parse-only / incomplete check), the emitter falls back to safer defaults where coded (e.g. async foreach → `PromiseIterable`; missing generic tracking → no GenericObject injection).

### Runtime packages

| Package | Typical emitters |
| --- | --- |
| `tyhp/core` | GenericObject, Type/NamedType/Generic, PropertyAccessor / UsesPropertyAccessors, DisposableScope, ObjectHelper, operator exceptions, Convertible contracts |
| `tyhp/async` | Promise, await, async foreach, entry-point `Promise::run`, async method wrappers |

`RequirePackage` populates `EmitContext.RequiredPackages` for later composer update in the build action.

### Optimizer (Story 23)

Currently a no-op. Design note: if the optimizer later inlines extension/operator calls, the emitter must still accept already-rewritten ASTs (emit the direct call, do not double-rewrite). AliasConverter already no-ops when a rewrite does not apply.

---

## 12. Common pitfalls

1. **Assuming native property hooks always emit** — they do at PHP ≥ 8.4; &lt; 8.4 strips them and requires the polyfill + ctor registration. Tests that only run at 8.4 will miss polyfill bugs.
2. **Forgetting checker flags in tests** — Mechanism C/D behavior needs `CompilationService` (or manually populated `EmitContext`) with the flag sets; bare `EmitContext.Create(scope, diagnostics)` without flags will not inject GenericObject / binders.
3. **Re-applying GenericObject on subclasses** — breaks private storage; use chain detection helpers.
4. **Putting type args on `__construct`** — Mechanism C forbids sharing; use init hook / factory.
5. **Using `$this->__initGenerics__`** — wrong; use `self::`.
6. **Rewriting `->member` through alias maps** — must keep member names protected.
7. **Emitting project `namespacePrefix` onto `\Tyhp\…`** — `EmittedFqnHelper` / external source checks exist specifically to prevent this.
8. **Hand-editing `runtime/packages/*/src`** — PHP is emitted from `tyhp_src`; fix emitter/checker and re-emit (workspace rule).
9. **Merging files with different namespaces** — diagnostics and abort of merge for that pair.
10. **Expecting struct declarations in output** — declarations are empty; only usages become arrays/backing ops.
11. **Callable generic call without currying** — Mechanism D sites need `f__tyhpGeneric(types…)(args…)`, not a single flat call (Mechanism A shape is superseded).
12. **Trusting `readme.md` templates / PLACEHOLDER tables** — many Story 11 items are implemented; verify against the partials listed above.

---

## 13. Open questions / needs clarification

Items that remain ambiguous or lightly specified after reading the emitter sources and primary docs:

1. **`PHP8.3/` placeholders** — Still empty. Unclear whether version-specific emit will ever move here or remain feature-gated inside the main partials.
2. **Story 17 source maps** — `PHPOutputFile.SourceMappings` exists; no active population path was verified in the emitter partials reviewed for this guide.
3. **Optimizer ↔ emitter contract** — Documented as a future concern; no live optimizer interactions to validate today.
4. **Custom struct backing edge cases** — `StructEmissionHelper` + `build.structBacking` paths exist; full matrix of rewrite vs error reporting for misconfigured backing is easier to miss than array-backed structs (covered more heavily in tests).
5. **Optional rename of `RequiresGenericVariant` / related API** — cosmetic only;
   behavior is Mechanism D. `__tyhpGeneric` suffix stays as ABI. See
   `CHECKER_GAPS.md` Mechanism A residual Phase 1.
6. **Whether `MergeOutputFiles` before `BuildEmitTrees` is permanently intended** — matches current code; Story 11 ADR text still describes a different order. Confirm with maintainers before “fixing” the phase list in other docs.

---

## Quick reference: “where do I change X?”

| Goal | Start here |
| --- | --- |
| New declaration shape | `TyhpEmitter.Declarations.cs` |
| New expression sugar | Prefer `AliasConverter` if AST should change; else `TyhpEmitter.Expressions.cs` |
| New statement form | `TyhpEmitter.Statements.cs` |
| Class generics / typeof on `T` | Checker mark + `Generics.cs` / `GenericClasses.cs` |
| Function/method generics | Checker mark + `GenericVariants.cs` |
| Property hooks | `PropertyAccessors.cs` + PHP version gate |
| Pipe / `(void)` / `clone(…)` / `exit`/`die` / attr strip | §6a matrix; `Expressions.cs` + `Helpers.cs` / `WithKeywordHelper` |
| Operators | Declarations → `OperatorOverloads.cs`; call sites → `AliasConverter` + `OperatorOverloadResolver` |
| PropertyPath / Expression / parsable lambdas | Checker → `PropertyPathSupport` / `ExpressionTreeSupport` + `ValidateArgumentTypes`; emit → `PropertyPathEmissionHelper` / `ExpressionTreeEmissionHelper` + `AliasConverter.RewriteArgumentListConverts` |
| Structs | `StructEmissionHelper` + AliasConverter collection maps. Property aliases may be quoted strings (`'Reply-To' as $replyTo`) or decimal integers (`0 as $arg1`); erasure emits string vs integer PHP array keys accordingly via `StructArrayKey`. |
| Object `with` | `WithKeywordHelper` |
| Imports / paths / FQNs | `PHPOutputFile` prune, `OutputPathResolver`, `EmittedFqnHelper` |
| Pipeline phases | `TyhpEmitter.Emit` only |

---

*Generated for developers working on Tyhp → PHP emission. When code and this guide diverge, trust the code and update this file.*
