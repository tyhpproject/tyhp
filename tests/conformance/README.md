# Tyhp conformance fixtures

Golden fixture suites live under `tests/conformance/storyNN/<feature>/` (and `story08_5/` for Story 08.5).
Each suite includes:

- `manifest.json` — machine-readable expectations (required)
- `tyhp.json` — optional suite-level project config
- input `.tyhp` / `.tyhpdef` files
- `expected/` — golden PHP output (Wave B, Story 09+)

Run all conformance cases:

```bash
dotnet test tyhp.sln --filter "Category=Conformance"
```

When adding a suite for a story, keep `README.md` notes in sync with `manifest.json`.

## Story 08.5 (`tests/conformance/story08_5/`)

| Suite | Feature |
|-------|---------|
| `symbol-name-erasure/` | `__ClassName` / `__FunctionName` / `__MethodName` / `__PropertyName` / `__EnumCaseName` / `__ConstName` / `__ObjectConstName` / `__UsedTraitName` → plain `string` |
| `nameof-erasure/` | `nameof()` emit |
| `template-string-erasure/` | template-string types + `__TypeName` / `__UnionTypeName` → `string` |
| `struct-utility-erasure/` | `__StructKey` / `__Properties` / `__StructDef` / `\Tyhp\ReturnType` / `\Tyhp\Parameters` PHP-surface erasure |
| `symbol-name-check/` | literal existence diagnostics |
| `template-string-check/` | template membership diagnostics |

## Story 11 (`tests/conformance/story11/`)

Emitter golden suites (`.tyhp` → `.php`, `errorCount: 0`, namespace `App`, PHP 8.4):

| Suite | Feature |
|-------|---------|
| `structs/` | struct → associative array |
| `generics/` | generic erasure (+ GenericObject when tracked) |
| `operator-overloads/` | static `__add` + call-site rewrite |
| `type-aliases/` | type alias erasure |
| `with/` | `new`/`clone` `with` (ObjectHelper path) |
| `async/` | top-level async → `\Tyhp\Promise` + `await` → `_await` |
| `disposables/` | `:=` → `DisposableScope` |
| `short-functions/` | named `fn` → `function` |
| `type-guards/` | `$a is Dog` → `bool` |
| `imports/` | `use` emission for referenced imports |

Fixtures intentionally avoid remaining Story 11 gaps (async class methods, nullsafe extension calls,
struct returned as `array`). Implicit convert call/return *emit* rewriting is implemented; checker
assignability via `operator convert` is still open (see `FOUND_BUGS.md`), so golden suites that need
`errorCount: 0` should keep using explicit casts until that lands.

## Story 14 (`tests/conformance/story14/`)

Representative diagnostic fixtures for error-message quality (parser / binder / checker codes via
manifest `expect.codes`). Rich span/suggestion rendering is covered by unit tests under
`tests/Tyhp.Tests/Diagnostics/`.

## Story 14.5 (`tests/conformance/story14_5/`)

PHP 8.4/8.5 syntax surface golden emit (`.tyhp` → `.php`, `errorCount: 0`, namespace `App`):

| Suite | Feature |
|-------|---------|
| `interface-abstract-hooks/` | Interface/abstract property hooks with `;` bodies (native PHP 8.5) |
| `attributed-property-hooks/` | Attributes on property hooks preserved (native PHP 8.5) |
| `exit-die-call/` | `exit`/`die` bare, call, named, FCC (native 8.5; lower 8.2 rewrite) |
| `pipe-chains/` | Pipe `|>` FCC/chain/arrow/variable/precedence (native 8.5; lower 8.2 nested calls) |
| `void-cast/` | `(void)` cast statement + for-list forms (native 8.5; lower 8.2 omit cast) |
| `clone-call/` | `clone($o, […])` / clone-with + FCC (native 8.5; lower 8.2 ObjectHelper / unary) |
| `attributed-top-level-const/` | Attributes on top-level `const` (native 8.5 preserve; lower 8.2 strip + TYHP5017) |

## Story 15 (`tests/conformance/story15/`)

Thin interop-contract index goldens (`.tyhp` → `.php`, `errorCount: 0`, namespace `App`, PHP 8.4).
Each suite covers one lowering construct; see also story11 / story14_5 for fuller emit coverage.

| Suite | Feature |
|-------|---------|
| `operators/` | `operator +` → static `__add` + call-site rewrite |
| `extensions/` | `extends string` method → `Extension::method($receiver)` |
| `generics-erasure/` | generic class type-param erasure (`Box<T>`) |
| `structs/` | struct → associative array shape |
| `disposables/` | `:=` → `\Tyhp\DisposableScope` |
| `async/` | `async`/`await` → `\Tyhp\Promise::_async` / `_await` |
| `expression-trees/` | `\Tyhp\PropertyPath` FQN surface (lambda package) |

## Story 16 (`tests/conformance/story16/`)

Parsable lambdas / expression trees (Phase 3). Fuller checker/emitter coverage lives in
`tests/Tyhp.Tests/Checker/ExpressionCheckerTests.cs`, `ExpressionEmitterTests.cs`, and
`tests/Tyhp.Tests/TestData/ValidTyhp/expression_trees/`.

| Suite | Feature |
|-------|---------|
| `expression-trees/` | Multi-parameter `Expression<T, T, int>`, `instanceof`/`is` → `InstanceofExpression`, `nameof(fn)` last-segment fold |

## Story 16.5 (`tests/conformance/story16_5/`)

Callable signature utilities. Checker coverage lives in
`tests/Tyhp.Tests/Checker/CallableSignatureUtilityTests.cs`.

| Suite | Feature |
|-------|---------|
| `callable-builtins/` | `\call_user_func` Rest unpack + `\call_user_func_array` named Struct / positional Tuple bags; utilities erase (PHP builtins remain) |
