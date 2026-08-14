# TyhpLang — Developer Technical Guide

This is the top-level map of `Tyhp/TyhpLang/`: what the language front-end is, how a source file moves through the compiler, how the pieces hand data off, what lives at the TyhpLang root, and where to read next.

Deep behavior belongs in the per-area guides linked below. This document stays high-level and current — how the system works today.

---

## 1. What TyhpLang is

**TyhpLang** is the compiler front-end for Tyhp: a typed language that is a superset of PHP. It turns `.tyhp` / `.php` / `.tyhpdef` source into:

1. An **AST** (`SrcFileAst` trees)
2. A **symbol/scope tree** (`GlobalScope`)
3. **Diagnostics** and checker **side-channels** (flags the emitter needs)
4. Eventually **PHP text** (`PHPOutputFile`s), when a CLI action runs the emitter

The C# types under `Tyhp/TyhpLang/` own syntax, AST, binding, checking, emission helpers, and shared language enums. Orchestration of multi-file parse → bind → check lives **outside** this folder in `Tyhp/Domain/Services/CompilationService.cs`. The build CLI wires emission and disk write after that service returns.

TyhpLang is not the CLI, project config, or output writer. Those sit under `Tyhp/CLI/`, `Tyhp/Config/`, and `Tyhp/Domain/`.

---

## 2. End-to-end pipeline

```text
Source bytes (.tyhp / .php / .tyhpdef)
  → Grammar (.g4) ──compile_grammar.sh──► Parser (TyhpLexer / TyhpParser)
  → Visitor (TyhpParserAstVisitor) → SrcFileAst
  → [optional] AstCacheService (Attributes registry for binary node IDs)
  → Binder (TyhpBinder) → GlobalScope + BoundSymbol on AST
  → Checker (TyhpChecker) → diagnostics + emitter contracts on CompilationResult
  → [CLI only] Emit (TyhpEmitter) → PHPOutputFile
  → [CLI only] OutputWriterService → disk
```

| Phase | Owns | Primary types | Why it exists |
|-------|------|---------------|---------------|
| **Grammar** | Syntax definition | `.g4` under `Grammar/` | Single source of truth for PHP + Tyhp syntax; regenerated into C# |
| **Parse** | Lex + parse | `TyhpLexer`, `TyhpParser` | Tokens → ANTLR parse tree |
| **Visit** | Parse tree → IR | `TyhpParserAstVisitor` | Stable AST independent of ANTLR contexts |
| **Bind** | Names & declarations | `TyhpBinder`, `GlobalScope` | Symbol table + link AST ↔ symbols |
| **Check** | Semantics & types | `TyhpChecker`, rules | Validate; classify emit-time needs |
| **Emit** | Tyhp → PHP text | `TyhpEmitter` | Erase/transform Tyhp features into runnable PHP |
| **Lint fixes** | Auto-fix text | `Lint/` | Optional source rewrites keyed by `MessageCode` (not emit) |

**Optimizer** is a planned phase (Story 23). `BuildAction` currently records `OptimizeDuration = TimeSpan.Zero` and does not call an optimizer.

### Error gating

Inside `CompilationService.ParseFiles`:

1. **Parse** always runs (parallel when `MaxThreads != 1`).
2. **Bind** runs only if parse produced **no** errors.
3. **Check** runs only if bind produced a non-null `GlobalScope` and `SkipChecking` is false.
4. AST cache flush runs after bind (including tyhpdefs loaded during bind).

Emission is **not** part of `CompilationService`. `BuildAction` decides whether to emit (`ShouldContinueToEmit`), builds `EmitContext` from the result, calls `TyhpEmitter.Emit`, then `OutputWriterService.WriteAll`. Lint runs parse/bind/check (and optional auto-fix loops) but does not emit PHP.

---

## 3. How the pieces work together

### 3.1 Orchestration: `CompilationService`

**File:** `Tyhp/Domain/Services/CompilationService.cs`

Production entry point for parse → bind → check. Callers today:

| Caller | Role |
|--------|------|
| `Tyhp/CLI/BuildAction.cs` | Full build: service → emit → write |
| `Tyhp/CLI/LintAction.cs` | Diagnostics (+ optional `Lint/` auto-fix), no emit |
| `Tyhp/CLI/DebugAction.cs` | Debug / dump / tokenize-style workflows |
| `tests/Tyhp.Tests/**` | Fixture pipelines via `new CompilationService().ParseFiles(...)` |

The service’s XML docs also mention a language server; LSP (Story 19) is not a live caller of this service in the current tree — treat CLI + tests as the verified consumers.

`ParseFiles(filePaths, options, cancellationToken)` returns a `CompilationResult` (`Tyhp/Domain/Diagnostics/CompilationResult.cs`) carrying:

- `Diagnostics` — shared bag across phases
- `ParsedFiles` — `SrcFileAst` list (ordered by file name)
- `GlobalScope` — after successful bind
- Checker side-channels copied from `TyhpChecker` (`NarrowedTypes`, `RequiresRuntimeGenericTracking`, `RequiresGenericVariant`, `GenericCallTargets`, `RequiresWeakReferenceCapture`, `RequiresDisposableTryFinally`, `AsyncForeachKinds`)
- Timing / cache / error counts per phase
- `OutputFiles` — filled later by the CLI emitter, not by the service

### 3.2 Per-file parse hand-off

For each file, `CompilationService.ParseFile`:

1. Uses **thread-local** `TyhpLexer` / `TyhpParser` (no cross-thread sharing of recognizers).
2. Configures tagless mode from `CompilationOptions.Tagless` for `.tyhp` / `.tyhpdef`.
3. Picks the ANTLR entry rule by extension (**.tyhpdef before .tyhp** because `.tyhpdef` ends with `.tyhp`):
   - `.tyhpdef` → `tyhpdefSrcFile` / `tyhpdefTaglessSrcFile`
   - `.tyhp` → `tyhpSrcFile` / `tyhpTaglessSrcFile`
   - else → `phpSrcFile`
4. Optionally loads a cached AST (`AstCacheService`) keyed by path + content hash (+ tagless bit).
5. On miss: `new TyhpParserAstVisitor(tokenStream, path, hash, diagnostics).Visit(ctx)` → `SrcFileAst`.
6. Caches only when that file produced **no new** diagnostics during lex/parse/visit (recovery trees are not cached).

Secondary same-pattern parse: `Binder/BuiltIn/Tyhpdef.ParseContent` for package/runtime tyhpdefs (cache always on for that path; see [Binder](Binder/technical-guide.md) and [Ast](Ast/technical-guide.md)).

### 3.3 Bind hand-off

`BindParsedFiles` constructs `TyhpBinder(diagnostics, options)` and calls `Bind(parsedFiles)` → `GlobalScope?`.

Binder:

- Seeds builtins + loads tyhpdefs into the same global tree
- Pass 1: declare symbols / attach `BoundSymbol` on declarations
- Pass 2: resolve type names via `NameResolver`

Scope types live under `Binder/Scopes/` (see [Binder](Binder/technical-guide.md)).

### 3.4 Check hand-off

`CheckParsedFiles` wraps `GlobalScope` in `SymbolTree`, constructs `TyhpChecker`, calls `Check(parsedFiles)`, then copies checker outputs onto `CompilationResult`.

Checker consumes bound AST + symbols; it does not re-parse. It may resolve things the binder deliberately leaves open (e.g. free function names at call sites). Emitter contracts are **side dictionaries / sets**, not fields on symbols.

### 3.5 Emit hand-off (CLI)

`BuildAction` after a successful gate:

```csharp
emitContext = EmitContext.Create(
    result.GlobalScope,
    result.Diagnostics,
    project,
    result.RequiresRuntimeGenericTracking,
    result.RequiresWeakReferenceCapture,
    result.RequiresDisposableTryFinally,
    result.AsyncForeachKinds,
    result.RequiresGenericVariant,
    result.GenericCallTargets);
var emitter = new TyhpEmitter(emitContext);
result.OutputFiles = emitter.Emit(result.ParsedFiles);
```

Emitter walks/transforms AST using `BoundSymbol` and those flag sets; it does not re-typecheck. Disk I/O is `OutputWriterService`, not `TyhpEmitter`.

### 3.6 Data that crosses phase boundaries

```text
Parse/Visit ──► SrcFileAst (+ optional binary cache via AstNodeTypeRegistry)
     │
Bind ───────► BoundSymbol / OwningFile on nodes; GlobalScope tree
     │         (BoundSymbol is not part of AST cache serialize format)
     │
Check ──────► Diagnostics + CompilationResult side-channels
     │
Emit ───────► PHPOutputFile.GeneratedContent (+ RequiredPackages on EmitContext)
```

Shared cross-cutting helpers at the TyhpLang root (`GeneratedNames`, `OverloadSignatureHelper`, `StaticValueTypeHelper`, `ArityFacetExpansion`) exist so binder, checker, and emitter agree on the same conventions without duplicating logic inside one phase folder.

---

## 4. Root-level files in `Tyhp/TyhpLang/`

Verified directory listing: four `.cs` files sit directly under `Tyhp/TyhpLang/` (everything else is a subfolder).

### `TyhpCompiler.cs`

**Entire file is commented out.** Historical sketch of a single-file parse → visit path (`lexer` → `parser.main()` → `TyhpParserAstVisitor`). It is **not** compiled as live API and is **not** an entry point.

Production orchestration is `CompilationService`. See also [Visitor](Visitor/technical-guide.md) § historical note.

### `GeneratedNames.cs`

Static constants and helpers for **compiler-generated PHP identifiers** that must stay consistent across checker reservation and emitter output:

| Member | Role |
|--------|------|
| `GenericVariantSuffix` (`__tyhpGeneric`) | Suffix on emitted generic callable variants |
| `GenericVariantParameterPrefix` (`__generic_`) | Hidden params carrying bound type arguments |
| `EndsWithGenericVariantSuffix` | Case-insensitive collision check (PHP name matching) |
| `GenericInitHook` | Uniform `__initGenerics__tyhpGeneric` across inheritance levels |
| `ReflectedClassField` | Cached `ReflectionClass` for generic factories |
| `GenericFactory` / `MangleFullyQualifiedName` | `new_<mangledFqn>__tyhpGeneric` factory naming |
| `PropertyHookInitHook` | Uniform `__initPropertyHooks__tyhpPropertyHook` across hooked inheritance levels |
| `PropertyHookGetMethod` / `PropertyHookSetMethod` | `__get_/__set_<prop>__tyhpPropertyHook` |

**Why it lives at the root:** the checker must reject user declarations that would collide with these names (`DeclarationRule.Callable` uses `EndsWithGenericVariantSuffix`). The emitter emits the same strings (`TyhpEmitter.GenericClasses`, `GenericVariants`, `Generics`). Keeping names outside `Emitter/` avoids checker ↔ emitter drift.

### `OverloadSignatureHelper.cs`

Identifies **compile-time-only overload signatures** that binder skips and emitter erases, leaving a single implementation:

1. **Top-level functions** — bodyless + grammar addon `isOverloadSignature` (`IsErasableFunctionOverloadSignature`). Named short functions get a desugared body at visit time and are **not** overload signatures.
2. **Class methods** — no dedicated grammar; structural detection: bodyless, non-abstract, and a same-named method **with a body** exists in the type (`CollectImplementedMethodNames` + `IsClassMethodOverloadSignature`). Abstract/interface methods stay.

**Used by:** `TyhpBinder.TopStatements` / `TyhpBinder.ObjectBody`, `TyhpEmitter.Declarations`.

### `StaticValueTypeHelper.cs`

Recognizes **literal / static-value type spellings** in type positions (`'red'`, `42`, `3.14`, `0xFF`, digit separators, quoted strings including `b'…'`). Widens them to underlying PHP scalars (`string` / `int` / `float`).

Does **not** cover `true` / `false` / `null` — those are builtin type symbols and stay as PHP type hints.

**Used by:** binder `NameResolver` (underlying builtin for annotations), checker `TypeInferrer.TypeExpressions`, emitter `TypeSpellingHelper`.

**Quirk:** legacy leading-zero integers like `017` are treated as **decimal** 17 (not octal), matching how scalar literal expressions are evaluated elsewhere (`PhpScalarAst.Create`).

### `ArityFacetExpansion.cs`

Shared **optional-parameter arity prefix** math for callable/Closure facets (checker) and Story 27
`new<>` constructable facets (binder). Given ordered `(HasDefault, IsVariadic)` flags, returns
ascending prefix lengths from `requiredCount` to `totalCount` (non-variadic only). Variadic-only
signatures yield a single `0` prefix — never infinite arities.

**Used by:** `CallableArityFacetBuilder` (checker); Story 27 binder facet computation should call the
same API rather than re-implementing the loop.

---

## 5. Area map

Each subfolder that ships a `technical-guide.md` (all of the following do):

| Area | What / why | Guide |
|------|------------|-------|
| **Grammar** | ANTLR `.g4` sources (`Php*` imported by `Tyhp*`). Syntax only; regenerate with `./compile_grammar.sh`. | [Grammar/technical-guide.md](Grammar/technical-guide.md) |
| **Parser** | Generated `TyhpLexer` / `TyhpParser` + hand-written partials and error listeners. Lex/parse runtime. | [Parser/technical-guide.md](Parser/technical-guide.md) |
| **Visitor** | `PhpParserAstVisitor` / `TyhpParserAstVisitor` — parse tree → `SrcFileAst`. Desugars some Tyhp sugar early. | [Visitor/technical-guide.md](Visitor/technical-guide.md) |
| **Ast** | `Base2Ast` IR, file roots, lists, interfaces, serialization hooks. Stable middle representation. | [Ast/technical-guide.md](Ast/technical-guide.md) |
| **Attributes** | **Not** PHP/C# attributes — `AstNodeTypeRegistry` for binary AST cache node IDs. | [Attributes/technical-guide.md](Attributes/technical-guide.md) |
| **Binder** | Declarations, scopes/symbols (`Binder/Scopes/`), tyhpdef load, name resolution → `GlobalScope`. | [Binder/technical-guide.md](Binder/technical-guide.md) |
| **Checker** | Semantic rules, assignability, inference, emitter side-channels. | [Checker/technical-guide.md](Checker/technical-guide.md) |
| **Emitter** | Bound/checked AST → PHP text; inline `EmitNode` walk + helpers; no transformer plugin layer. | [Emitter/technical-guide.md](Emitter/technical-guide.md) |
| **Interop** | Story 15 contract version + required `\Tyhp\*` surface (`InteropContract` / `InteropContractSurface`). | [Interop/technical-guide.md](Interop/technical-guide.md) |
| **Enum** | Shared language/compiler enumerations and token→enum helpers used everywhere. | [Enum/technical-guide.md](Enum/technical-guide.md) |
| **Lint** | Auto-fix engine for `tyhp lint --fix` (`ILintFix` / `LintFixEngine`). Not checker rules; not emit. | [Lint/technical-guide.md](Lint/technical-guide.md) |

---

## 6. Suggested reading order

For a new developer getting oriented:

1. **This guide** — pipeline and ownership map.
2. [Grammar](Grammar/technical-guide.md) then [Parser](Parser/technical-guide.md) — how text becomes a parse tree (and how regeneration works).
3. [Visitor](Visitor/technical-guide.md) + [Ast](Ast/technical-guide.md) — IR shape and conventions.
4. [Binder](Binder/technical-guide.md) — symbols, scopes (`Binder/Scopes/`), and resolution.
5. [Checker](Checker/technical-guide.md) — semantics and emitter contracts.
6. [Emitter](Emitter/technical-guide.md) — PHP lowering.
7. Skim [Enum](Enum/technical-guide.md), [Attributes](Attributes/technical-guide.md), and [Lint](Lint/technical-guide.md) when you hit those surfaces.
8. Read `CompilationService.cs` and `BuildAction.cs` once — they are the glue outside TyhpLang.

When changing an area, update that area’s `technical-guide.md` (see `.cursor/rules/tyhplang-technical-guides.mdc`). Cross-cutting pipeline or root-helper changes belong in **this** file.

---

## 7. Outside TyhpLang (quick pointers)

| Concern | Location |
|---------|----------|
| Parse → bind → check orchestration | `Tyhp/Domain/Services/CompilationService.cs` |
| Options / progress | `CompilationOptions`, `CompilationProgress` |
| Result bag | `Tyhp/Domain/Diagnostics/CompilationResult.cs` |
| AST disk/memory cache | `Tyhp/Domain/Services/AstCacheService.cs` |
| Build / lint / debug CLI | `Tyhp/CLI/BuildAction.cs`, `LintAction.cs`, `DebugAction.cs` |
| Emit → filesystem | `OutputWriterService` (Domain/CLI path after `TyhpEmitter`) |
| Grammar regeneration | `./compile_grammar.sh` (repo root) |

---

## Open questions

None after researching the root files, area guides, and `CompilationService` / `BuildAction` call sites. If LSP begins calling `CompilationService` directly, update §3.1 consumers.
