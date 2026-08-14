# Implementation Plan: Story 16 — Parsable Lambdas (Expression Trees)

> **Roadmap position:** Story 16 — **Tier 1 — Usable** · **FLAGSHIP: query-builder / expression-tree wedge — explicit showcase**
> **Direct dependencies (new numbering):** 08 (mature checker), 11 (emitter feature expansion), 04 (tyhp/lambda runtime)
> **Renumbered from:** legacy Story 16
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `Syntax_TODO.md` item 20
> **Branch:** TBD
> **Generated:** 2026-02-17
> **Prerequisites:** This is the query-builder / expression-tree **wedge showcase**, lifted forward to Tier 1. Its real prerequisites are a mature checker (Story 08), the emitter feature transformers (Story 11), and the `tyhp/lambda` runtime (Story 04, Phase 11). It does **not** require the optimizer (Stories 23–24) or any Tier 2/3 story — those only enhance expression trees, they do not enable them.

---

## Architecture Overview

### What Parsable Lambdas Are

Parsable lambdas (expression trees) allow Tyhp code to pass a short lambda (`fn`) expression to a function and have the compiler emit a **data structure describing the lambda's AST** instead of (or in addition to) a compiled closure. The receiving function can then inspect the structure of the lambda at runtime — walking property accesses, method calls, operators, and constants — to translate the expression into another domain (SQL queries, validation rules, serialization mappings, etc.).

This is the same concept as C#'s `Expression<Func<T, TResult>>`, which powers Entity Framework's LINQ-to-SQL. In C#, the compiler detects that the target parameter type is `Expression<>` and emits code that constructs an expression tree instead of IL. Tyhp does the same: the compiler detects `Expression<>` parameter types and emits PHP code that constructs `\Tyhp\Expression` tree objects.

### The `\Closure` Problem

PHP's `\Closure` class is `final` — it cannot be extended. This means `Expression` cannot inherit from `\Closure`, and an `Expression` instance cannot be passed directly where a `\Closure` type hint is expected.

**Workaround strategy:**

1. `Expression` implements `__invoke()`, making it callable. This satisfies `callable` type hints.
2. `Expression` stores the original compiled closure internally via `$expression->callable`, which can be extracted when a `\Closure` is needed.
3. The Tyhp type system treats `Expression<T, R>` as assignable to `callable<T, R>` but NOT to `\Closure`. When the target type is `\Closure`, the emitter automatically extracts the stored closure: `$expression->callable`.
4. Functions that want to accept EITHER a regular callable OR a parsable expression should type their parameter as `Expression<T, R>` — the compiler will convert inline `fn` expressions to `Expression` automatically, and the `__invoke()` method allows the Expression to be called if the function just wants to execute it.

### Position in the Pipeline

```
Wedge prerequisites satisfied
(checker = Story 08, emitter feature transformers = Story 11, tyhp/lambda runtime = Story 04)
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 16: Parsable Lambdas (Expression Trees)           │
│  ◄── THIS PLAN                                           │
│                                                          │
│  Touches: Grammar, Binder, Checker, Emitter, TyhpLib,   │
│           TyhpSpec, Testing, LSP                         │
│                                                          │
│  Phase 1: PropertyPath<T, R> (simple property chains)    │
│  Phase 2: Expression<T, R> (full expression trees)       │
│  Phase 3: Advanced features and ecosystem integration    │
└──────────────────────────────────────────────────────────┘
```

### Design Principles

1. **Only `fn` (arrow function) syntax is parsable.** Statement-body closures (`function() { ... }`) are too complex and cannot be meaningfully represented as expression trees. Only single-expression `fn` lambdas are converted.

2. **The parameter type drives the behavior.** If a parameter is typed `Expression<T, R>`, the compiler emits an expression tree. If typed `callable` or `\Closure`, the compiler emits a normal closure. This matches C#'s approach. `Expression` uses regular generic parameters following the `callable<TArgs..., TReturn>` convention: the last generic parameter is always the return type, and everything before it represents the parameter types. With a single type argument, it is the return type (zero parameters). For example, `Expression<User, string>` represents a single-parameter lambda taking `User` and returning `string`; `Expression<T, T, int>` represents a two-parameter lambda returning `int`.

**Single type parameter:** `Expression<R>` with a single type parameter is a zero-parameter expression returning `R`. The minimum arity is 1 (at least the return type must be specified). Examples:
- `Expression<string>` — zero-parameter expression returning `string`
- `Expression<User, string>` — one-parameter expression `(User) → string`
- `Expression<User, Address, string>` — two-parameter expression `(User, Address) → string`

3. **Every Expression carries its compiled callable.** The `Expression` object always includes the original closure so it can be executed as a fallback. This means code that receives an `Expression` can either inspect the tree OR just call it.

4. **Captured variables become `ConstantExpression` nodes.** Variables from the enclosing scope that are referenced in the lambda body become constant nodes in the expression tree. Their values are captured at the time the expression tree is constructed (runtime values, not compile-time constants). This enables patterns like `fn ($u) => $u->age > $minAge` where `$minAge` is a runtime variable.

5. **Phase 1 is deliberately minimal.** `PropertyPath<T, R>` covers 80% of the ORM/mapping use case with 10% of the complexity. Full expression trees come in Phase 2.

6. **Backward-compatible upgrade path.** `PropertyPath<T, R>` will be a subtype of `Expression<T, R>` in Phase 2, so code written against `PropertyPath` continues to work.

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup using the canonical naming: `<filename>.bak.<YYYYMMDD_HHMMSS>`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: `PropertyPath<T, R>` — Simple Property Chain Extraction

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the simplest and most useful form of parsable lambdas: extracting property access chains from `fn` expressions. When a parameter is typed `PropertyPath<T, R>`, the compiler converts `fn (T $x) => $x->propA->propB` into a `PropertyPath` object containing the chain `['propA', 'propB']` plus type information.

This covers the primary use case: type-safe ORM column references, object mapping, validation rules, and serialization configuration — all without string-based property names that break silently during refactoring.

### Deliverables

**Package tyhpdef dependency:**
- `tyhp/lambda` package — provides `\Tyhp\PropertyPath` runtime class and its auto-generated `package.tyhp.json` containing type definitions for `PropertyPath<T, R>` (created in Story 04, Phase 11; tyhpdef generated by Story 20, Track C)

**Note:** No manual `tyhpExpression.tyhpdef` file is created in `Tyhp/TyhpSpec/`. The type definitions for `PropertyPath<T, R>` and `Expression<T, R>` are provided by the `tyhp/lambda` package's auto-generated `package.tyhp.json`, which the binder discovers and loads from `vendor/tyhp/lambda/package.tyhp.json`.

**Modified compiler files:**
- `Tyhp/TyhpLang/Checker/PropertyPathSupport.cs` — PropertyPath type detection + property-chain walk
- `Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.Calls.cs` — `CheckPropertyPathArgument` at call sites
- `Tyhp/TyhpLang/Checker/Rules/ClosureParameterInference.cs` — contextual `PropertyPath<T,R>` → `callable<T,R>`
- `Tyhp/TyhpLang/Emitter/PropertyPathEmissionHelper.cs` — build `new \Tyhp\PropertyPath(...)` / `->callable` extraction
- `Tyhp/TyhpLang/Emitter/AliasConverter.cs` — call-arg rewrite via `RewriteArgumentListConverts`
- **Note:** Story 11 removed the planned `Transformers/` layer and `TyhpChecker.TyhpFeatures.cs`; Phase 1 follows the live AliasConverter + `ICheckerRule` / `TypeCompatibilityRule` patterns instead.

**Modified runtime files (`tyhp/lambda`):**
- `runtime/packages/lambda/tyhp_src/PropertyPath.tyhp` — optional `array<bool> $nullSafeFlags` ctor parameter; body built via `FluentSupport::buildPathNodes` so `?->` segments become `NullSafeAccessExpression`
- `runtime/packages/lambda/tyhp_src/PropertyPathBuilder.tyhp` — forwards its own flags to the parent ctor
- `runtime/packages/lambda/package.tyhpdef` — ctor signature updated; `PropertyPath` is not `final` (`PropertyPathBuilder` extends it)
- **Grammar: no changes expected.** `PropertyPath<T, R>` uses the existing generic type-argument syntax (`tyhpGenericsTypeArguments` / `tyhpGenericsTypeArgumentList` in `Tyhp/TyhpLang/Grammar/TyhpParser.g4`), which already parses generic type references. This deliverable is conditional and should only be touched if a concrete parse failure for `PropertyPath<>`/`Expression<>` is observed.

**New test files:**
- PropertyPath checker tests
- PropertyPath emitter tests
- PropertyPath end-to-end tests

### Implementation Details

#### 1.1 Verify `PropertyPath<TSource, TResult>` Type (Provided by Package Tyhpdef)

The `PropertyPath<TSource, TResult>` class is defined in the `tyhp/lambda` package's Tyhp source (`runtime/packages/lambda/tyhp_src/PropertyPath.tyhp`) and is included in the auto-generated `package.tyhp.json`. The following documents the expected type shape for reference:

```
<?tyhpdef

class PropertyPath<TSource, TResult> {
    /**
     * The source type that the property chain starts from.
     * For `fn (User $u) => $u->address->city`, this is `User`.
     */
    public readonly string $sourceType;

    /**
     * The result type at the end of the property chain.
     * For `fn (User $u) => $u->address->city`, this is `string`.
     */
    public readonly string $resultType;

    /**
     * The property names in the access chain, in order.
     * For `fn (User $u) => $u->address->city`, this is `['address', 'city']`.
     */
    public readonly array $path;

    /**
     * The compiled callable that can execute the property access.
     * This allows PropertyPath to be used as a callable fallback.
     */
    public readonly \Closure $callable;

    /**
     * Get the final property name (last element of the path).
     * For `fn (User $u) => $u->address->city`, this is `'city'`.
     */
    public function getPropertyName(): string;

    /**
     * Get the full dot-notation path string.
     * For `fn (User $u) => $u->address->city`, this is `'address.city'`.
     */
    public function getPath(): string;

    /**
     * Get the path as an array of property names.
     */
    public function getSegments(): array;

    /**
     * Execute the property access chain on an object instance.
     * Equivalent to calling the compiled callable.
     */
    public function getValue(TSource $source): TResult;

    /**
     * Invoke the property path as a callable (enables __invoke compatibility).
     */
    public function __invoke(TSource $source): TResult;
}
```

#### 1.2 Runtime `PropertyPath` Class

The `\Tyhp\PropertyPath` PHP runtime class is provided by the `tyhp/lambda` Composer package (implemented in Story 04, Phase 11). This phase does NOT create the runtime class — it only implements the checker and emitter that produce `new \Tyhp\PropertyPath(...)` calls.

The emitter emits calls to the `tyhp/lambda` runtime class at:
- `runtime/packages/lambda/src/PropertyPath.php` — `\Tyhp\PropertyPath`

#### 1.3 Checker: Validate PropertyPath Usage

**File: `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs`**

Add a new check method `CheckPropertyPathArgument()` that is called when the checker encounters a function call where a parameter has type `PropertyPath<T, R>`:

1. **Verify the argument is an inline `fn` expression.** If the argument is anything other than an inline arrow function (`fn ($x) => ...`), report an error:
   - `CheckerPropertyPathRequiresInlineFn = 4320` — "Parameter of type 'PropertyPath<{0}, {1}>' requires an inline fn expression (e.g., fn ($x) => $x->property)"

2. **Verify the fn body is a property access chain.** Walk the expression body of the `fn`:
   - The body must be a chain of property accesses starting from the lambda parameter
   - Valid: `fn ($x) => $x->prop`, `fn ($x) => $x->a->b->c`
   - Invalid: `fn ($x) => $x->method()` (method call, not property access)
   - Invalid: `fn ($x) => $x->a + $x->b` (binary expression, not a chain)
   - Invalid: `fn ($x) => strtolower($x->name)` (function call wrapping)
   - If the body is not a valid property chain, report: `CheckerPropertyPathInvalidBody = 4321` — "PropertyPath expression must be a simple property access chain (e.g., fn ($x) => $x->prop->subProp)"

3. **Verify type compatibility.** The source type `T` in `PropertyPath<T, R>` must match the lambda parameter type, and the result type `R` must match the type of the final property in the chain.

4. **Verify null safety.** If any property in the chain is nullable, handle accordingly:
   - `fn ($x) => $x?->address?->city` — nullable chain is allowed; result type should be `?R`
   - The PropertyPath should track which segments are nullable

#### 1.4 Emitter: Emit PropertyPath Construction

**New file: `Tyhp/TyhpLang/Emitter/Transformers/PropertyPathTransformer.cs`**

When the emitter encounters a function call argument that the checker has identified as a PropertyPath conversion:

1. Extract the property names from the `fn` body's AST (chain of property access nodes)
2. Emit a `new \Tyhp\PropertyPath(...)` construction instead of the closure

**Example:**

```php
// Tyhp input:
$query->select(fn ($u) => $u->address->city);

// Emitted PHP:
$query->select(new \Tyhp\PropertyPath(
    \App\Models\User::class,
    'string',
    ['address', 'city'],
    fn (\App\Models\User $u) => $u->address->city
));
```

Note: the original `fn` is included as the `callable` parameter so the PropertyPath can be executed.

**Nested/chained property access extraction algorithm:**

```
Given fn body AST node:
1. If node is PropertyAccessAst:
   a. Recursively extract from node.Object
   b. Append node.PropertyName to the path
   c. Track property type for each segment
2. If node is ParameterReferenceAst:
   a. This is the chain root — verify it matches the fn parameter
   b. Return empty path (start of chain)
3. If node is NullSafePropertyAccessAst:
   a. Same as PropertyAccessAst but mark segment as nullable
4. Anything else: error (not a valid property chain)
```

#### 1.5 Type System Integration

The Tyhp checker must handle `PropertyPath<T, R>` in the type system:

- `PropertyPath<T, R>` is assignable to `callable<T, R>` (because it implements `__invoke`)
- `PropertyPath<T, R>` is NOT assignable to `\Closure` (because \Closure is final)
- When a `PropertyPath` is passed where `\Closure` is expected, the emitter automatically extracts `$propertyPath->callable` at the call site
- When a `PropertyPath` is passed where `callable` is expected and the parameter is NOT typed as `PropertyPath`, emit the `PropertyPath` object directly (it is callable via `__invoke`)

#### 1.6 LSP Support (conditional enhancement — builds on Story 19)

These are **optional polish** items layered on top of Story 19's existing handlers; they are not required for the core PropertyPath feature and may be deferred. When implemented, they modify the corresponding Story 19 handler files (under `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/`):

- `CompletionHandler.cs` — provide autocomplete for property names inside `fn` expressions that target `PropertyPath<T, R>` parameters (the LSP knows `T` and can suggest its properties)
- `HoverHandler.cs` — show hover information on `PropertyPath` parameters explaining that an inline `fn` expression is required
- `CodeActionHandler.cs` — provide a diagnostic quick-fix (surfacing `CheckerPropertyPathRequiresInlineFn`) when a non-`fn` expression is passed to a `PropertyPath` parameter

### Acceptance Criteria

- [x] `PropertyPath<T, R>` type is provided by the `tyhp/lambda` package's `package.tyhp.json` and loadable by the binder via Composer dependency scanning
- [x] `PropertyPath` runtime class is available from `tyhp/lambda` package (provided by Story 04)
- [x] `fn ($u) => $u->firstName` passed to a `PropertyPath<User, string>` parameter emits a `new \Tyhp\PropertyPath(...)` construction
- [x] Multi-segment chains work: `fn ($u) => $u->address->city` produces `['address', 'city']`
- [x] Nullable chains work: `fn ($u) => $u?->address?->city` tracks nullable segments (emitted as `nullSafeFlags: [...]`)
- [x] Non-`fn` arguments to `PropertyPath` parameters produce `CheckerPropertyPathRequiresInlineFn` error (4320)
- [x] Non-property-chain `fn` bodies produce `CheckerPropertyPathInvalidBody` error (4321)
- [x] `PropertyPath` is callable via `__invoke()` — `$path($userInstance)` returns the property value
- [x] `PropertyPath->getPropertyName()` returns the last segment
- [x] `PropertyPath->getPath()` returns the dot-notation string
- [x] Source and result types are correctly resolved and stored
- [x] When `PropertyPath` is passed where `\Closure` is expected, emitter extracts `->callable`
- [x] When `PropertyPath` is passed where `callable` is expected, emitter passes the object directly
- [ ] All example files compile without errors
- [x] PSR-4 autoloading resolves `\Tyhp\PropertyPath`

### Dependencies

- **Requires:** Story 08 (mature checker), Story 11 (emitter feature transformers), and Story 04 Phase 11 (`tyhp/lambda` package with PropertyPath/Expression runtime classes)
- **Provides:** Type-safe property references for ORM, mapping, validation, serialization use cases; foundation for Phase 2

---

## Phase 2: `Expression<T, R>` — Full Expression Trees

> **[Phase Runner] Runtime/Model:** `claude/opus` | `cursor/opus`
> **[Phase Runner] Review Level:** `High`

### Phase Overview

Expand from simple property chains to full expression trees. When a parameter is typed `Expression<T, R>`, the compiler converts any inline `fn` expression into a tree of expression nodes that the receiving function can walk, inspect, and translate into another domain (SQL, validation rules, etc.).

`PropertyPath<T, R>` becomes a subtype of `Expression<T, R>`, ensuring backward compatibility.

### Deliverables

**Package tyhpdef dependency:**
- `tyhp/lambda` package — provides `\Tyhp\Expression`, `\Tyhp\Expression\ExpressionNode`, all concrete node types, and `\Tyhp\Expression\ExpressionVisitor` (created in Story 04, Phase 11). The auto-generated `package.tyhp.json` (Story 20, Track C) contains type definitions for `Expression<T, R>` and all expression node types.

**Note:** No manual TyhpSpec files are updated. All expression tree type definitions are provided by the `tyhp/lambda` package's `package.tyhp.json`.

**Modified compiler files:**
- `Tyhp/TyhpLang/Checker/ExpressionTreeSupport.cs` — Expression type detection, body validation, capture checks
- `Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.Calls.cs` — `CheckExpressionInlineFn` at call sites (TYHP4322–4324)
- `Tyhp/TyhpLang/Checker/Rules/ClosureParameterInference.cs` — contextual `Expression<…>` → `callable<…>`
- `Tyhp/TyhpLang/Emitter/ExpressionTreeEmissionHelper.cs` — build `new \Tyhp\Expression(...)` + nested `\Tyhp\Expression\*` nodes
- `Tyhp/TyhpLang/Emitter/AliasConverter.cs` — call-arg rewrite via `RewriteArgumentListConverts` (after PropertyPath)
- `EmitContext` / `CompilationResult` / `CompilationService` / `BuildAction` — plumb `ExpressionTypes` for per-node type strings
- **Note:** Same as Phase 1 — no `Transformers/` layer or `TyhpChecker.TyhpFeatures.cs`. PropertyPath emission stays on `PropertyPathEmissionHelper`; Expression uses the parallel helper (not a single merged transformer).

### Implementation Details

#### 2.1 Define Expression Node Hierarchy

> **Runtime files provided by `tyhp/lambda` (Story 04, Phase 11):** All expression node classes live in `runtime/packages/lambda/src/Expression/`.

All expression nodes extend a common base:

```php
abstract class ExpressionNode
{
    /** The resolved type of this expression (fully qualified class name or scalar type) */
    public readonly string $type;

    /** The kind of expression node */
    public readonly string $nodeType;

    /** Accept a visitor */
    abstract public function accept(ExpressionVisitor $visitor): mixed;
}
```

**Node types and their properties:**

| Node Class | Properties | Example |
|------------|-----------|---------|
| `ParameterExpression` | `string $name`, `string $paramType`, `int $index` | `$x` (the lambda parameter) |
| `PropertyAccessExpression` | `ExpressionNode $object`, `string $property` | `$x->firstName` |
| `NullSafeAccessExpression` | `ExpressionNode $object`, `string $property` | `$x?->address` |
| `MethodCallExpression` | `ExpressionNode $object`, `string $method`, `array $arguments` | `$x->getFullName()` |
| `StaticMethodCallExpression` | `string $class`, `string $method`, `array $arguments` | `Str::lower(...)` |
| `BinaryExpression` | `ExpressionNode $left`, `string $operator`, `ExpressionNode $right` | `$x->age > 18` |
| `UnaryExpression` | `string $operator`, `ExpressionNode $operand`, `bool $isPrefix` | `!$x->isDeleted` |
| `ConstantExpression` | `mixed $value` | `18`, `'hello'`, captured `$minAge` |
| `TernaryExpression` | `ExpressionNode $condition`, `?ExpressionNode $ifTrue`, `ExpressionNode $ifFalse` | `$x->a ? $x->b : $x->c` |
| `CoalesceExpression` | `ExpressionNode $left`, `ExpressionNode $right` | `$x->nickname ?? $x->name` |
| `ArrayAccessExpression` | `ExpressionNode $array`, `ExpressionNode $index` | `$x->tags[0]` |
| `CastExpression` | `string $targetType`, `ExpressionNode $operand` | `(int) $x->value` |
| `NewExpression` | `string $class`, `array $arguments` | `new Money($x->amount)` |

#### 2.2 Define `Expression<T, R>` Class

> **Runtime file provided by `tyhp/lambda` (Story 04, Phase 11):** `runtime/packages/lambda/src/Expression.php`

```php
class Expression
{
    /** The expression tree root node */
    public readonly ExpressionNode $body;

    /** The lambda parameters as ParameterExpression nodes */
    public readonly array $parameters;

    /** The compiled closure for execution */
    public readonly \Closure $callable;

    /** The return type of the expression */
    public readonly string $returnType;

    public function __construct(
        ExpressionNode $body,
        array $parameters,
        \Closure $callable,
        string $returnType
    ) { ... }

    /** Execute the expression (delegates to the compiled closure) */
    public function __invoke(mixed ...$args): mixed
    {
        return ($this->callable)(...$args);
    }

    /** Get the compiled closure */
    public function compile(): \Closure
    {
        return $this->callable;
    }
}
```

#### 2.3 Make `PropertyPath<T, R>` Extend `Expression<T, R>`

> **Runtime file provided by `tyhp/lambda` (Story 04, Phase 11):** `PropertyPath` extending `Expression` is implemented in `runtime/packages/lambda/src/PropertyPath.php`.

Update `PropertyPath` to extend `Expression`:

```php
class PropertyPath extends Expression
{
    public readonly array $path;

    // PropertyPath-specific convenience methods remain:
    // getPropertyName(), getPath(), getSegments(), getValue()

    // The expression tree body is a chain of PropertyAccessExpression nodes
}
```

This ensures backward compatibility: any code accepting `Expression<T, R>` also accepts `PropertyPath<T, R>`.

#### 2.4 Checker: Validate Expression Tree Arguments

Expand the checker validation from Phase 1:

1. **Verify the argument is an inline `fn` expression.** Same as Phase 1, but now for `Expression<T, R>` parameters.

2. **Validate the fn body contains only supported expression types.** Walk the AST and verify every node is a supported expression kind:
   - **Supported:** property access, method calls, static method calls, binary operators, unary operators, constants/literals, ternary, null coalescing, array access, casts, `new`, captured variables
   - **Not supported:** assignments, `await`, `yield`, `match`, `instanceof`/`is` (these may be added later; `instanceof`/`is` is added in Phase 3)
   - **Not supported:** nested `fn`/closure expressions (too complex for v1)
   - Report `CheckerExpressionUnsupportedNode = 4322` for unsupported node types

3. **Resolve types for every sub-expression.** The checker must compute the type of every node in the expression tree (it already does this via `TypeInferrer` from Story 08). These types are passed to the emitter for inclusion in the expression tree.

4. **Validate captured variables.** Variables from the enclosing scope that appear in the `fn` body become `ConstantExpression` nodes. Verify they are definitely assigned and have known types.

#### 2.5 Emitter: Emit Expression Tree Construction

**Migration step (do this first):** Phase 1's `Tyhp/TyhpLang/Emitter/Transformers/PropertyPathTransformer.cs` is **superseded and deleted** in this phase. Its property-path extraction logic is folded into the new `ExpressionTreeTransformer.cs` (property paths are a strict subset of expression trees). Move/port the PropertyPath extraction code into `ExpressionTreeTransformer`, update all registrations/call sites to the new transformer, then remove `PropertyPathTransformer.cs`.

**New file: `Tyhp/TyhpLang/Emitter/Transformers/ExpressionTreeTransformer.cs`**

When the emitter encounters a function call argument that targets an `Expression<T, R>` parameter:

1. Walk the `fn` body AST
2. For each AST node, emit a `new \Tyhp\Expression\XxxExpression(...)` constructor call
3. Emit the tree bottom-up (leaf nodes first, then their parents)
4. Wrap in a `new \Tyhp\Expression(body, parameters, callable, returnType)`

**Example — simple property access:**

```php
// Tyhp:
$query->where(fn ($u) => $u->age > $minAge);

// Emitted PHP:
$query->where(new \Tyhp\Expression(
    body: new \Tyhp\Expression\BinaryExpression(
        left: new \Tyhp\Expression\PropertyAccessExpression(
            object: new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
            property: 'age',
            type: 'int'
        ),
        operator: '>',
        right: new \Tyhp\Expression\ConstantExpression($minAge, 'int'),
        type: 'bool'
    ),
    parameters: [
        new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
    ],
    callable: fn (\App\Models\User $u) => $u->age > $minAge,
    returnType: 'bool'
));
```

**Example — method call:**

```php
// Tyhp:
$query->select(fn ($u) => $u->getFullName());

// Emitted PHP:
$query->select(new \Tyhp\Expression(
    body: new \Tyhp\Expression\MethodCallExpression(
        object: new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
        method: 'getFullName',
        arguments: [],
        type: 'string'
    ),
    parameters: [
        new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
    ],
    callable: fn (\App\Models\User $u) => $u->getFullName(),
    returnType: 'string'
));
```

#### 2.6 ExpressionVisitor

> **Runtime file provided by `tyhp/lambda` (Story 04, Phase 11):** `runtime/packages/lambda/src/Expression/ExpressionVisitor.php`

Provide a visitor interface so consumers can walk expression trees without type-switching:

```php
abstract class ExpressionVisitor
{
    public function visit(ExpressionNode $node): mixed
    {
        return $node->accept($this);
    }

    public function visitParameter(ParameterExpression $node): mixed { ... }
    public function visitPropertyAccess(PropertyAccessExpression $node): mixed { ... }
    public function visitMethodCall(MethodCallExpression $node): mixed { ... }
    public function visitBinary(BinaryExpression $node): mixed { ... }
    public function visitUnary(UnaryExpression $node): mixed { ... }
    public function visitConstant(ConstantExpression $node): mixed { ... }
    public function visitNullSafeAccess(NullSafeAccessExpression $node): mixed { ... }
    public function visitTernary(TernaryExpression $node): mixed { ... }
    public function visitCoalesce(CoalesceExpression $node): mixed { ... }
    public function visitArrayAccess(ArrayAccessExpression $node): mixed { ... }
    public function visitCast(CastExpression $node): mixed { ... }
    public function visitStaticMethodCall(StaticMethodCallExpression $node): mixed { ... }
    public function visitNew(NewExpression $node): mixed { ... }
}
```

This enables library authors to build SQL translators, serialization mappers, etc. by extending `ExpressionVisitor`.

#### 2.7 Type System Integration

- `Expression<T, R>` is assignable to `callable<T, R>` (via `__invoke`)
- `Expression<T, R>` is NOT assignable to `\Closure`
- `PropertyPath<T, R>` is a subtype of `Expression<T, R>`
- When passing `Expression` where `\Closure` is expected, emitter extracts `->callable` automatically and the checker emits a note (not an error)
- When passing a regular `fn` expression to an `Expression<>` parameter, the compiler converts it
- When passing a regular `fn` expression to a `callable` or `\Closure` parameter, the compiler does NOT convert it (normal closure behavior)

### Acceptance Criteria

- [x] All Phase 1 tests continue to pass (PropertyPath backward compatibility)
- [x] `PropertyPath<T, R>` extends `Expression<T, R>` *(runtime already; Phase 2 does not modify tyhp_src)*
- [x] `Expression` class with `body`, `parameters`, `callable`, `returnType` works correctly *(runtime)*
- [x] All expression node types are implemented with correct properties *(runtime)*
- [x] `ExpressionVisitor` can walk any expression tree *(runtime)*
- [x] `fn ($x) => $x->age > 18` emits correct `BinaryExpression` tree with `PropertyAccessExpression` and `ConstantExpression` children
- [x] `fn ($x) => $x->getFullName()` emits correct `MethodCallExpression` tree
- [x] Captured variables (`$minAge` from outer scope) emit as `ConstantExpression` with runtime values
- [x] Unsupported expression bodies produce `CheckerExpressionUnsupportedNode` error (4322)
- [x] Non-`fn` arguments produce clear error (4323)
- [x] `Expression.__invoke()` executes the stored callable correctly *(runtime)*
- [x] `Expression->compile()` returns the stored `\Closure` *(runtime)*
- [x] Type compatibility: `Expression<T, R>` satisfies `callable<T, R>` *(contextual typing via ExpressionTreeSupport.TryMapToCallable)*
- [x] When Expression is passed where `\Closure` is expected, `->callable` is extracted
- [x] All expression node types carry resolved type information *(via ExpressionTypes → EmitContext)*

> **Phase 2 deliverable note:** Implementation follows the Phase 1 pattern
> (`ExpressionTreeSupport` + `ExpressionTreeEmissionHelper` + `AliasConverter.RewriteArgumentListConverts`
> + `TypeCompatibilityRule`), **not** the plan’s stale `Transformers/ExpressionTreeTransformer.cs` path.

### Dependencies

- **Requires:** Phase 1 (PropertyPath foundation), Story 04 Phase 11 (`tyhp/lambda` package with Expression tree runtime classes)
- **Provides:** Full expression tree infrastructure for ORM query building, validation, serialization, and domain translation

---

## Phase 3: Advanced Features and Ecosystem Integration

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Add advanced expression tree features: multi-parameter lambdas, nested expressions, the `nameof`-through-expression pattern, expression tree serialization, and example library integrations demonstrating real-world usage.

### Deliverables

**Updated runtime package files (in tyhp/lambda):**
- Expression tree serialization (JSON export) — `runtime/packages/lambda/src/Expression/ExpressionSerializer.php`
- Expression tree comparison and equality — additions to existing node classes

**New test data files:**
- `tests/Tyhp.Tests/TestData/ValidTyhp/expression_trees/ExpressionTrees.tyhp` — Comprehensive expression tree test inputs
- `tests/Tyhp.Tests/TestData/ValidTyhp/expression_trees/TypeSafeQueryBuilder.tyhp` — Demonstrates ORM-style query building
- Note: The `Examples/` directory is for brainstorming only — all test inputs use `TestData/`

**Documentation:**
- Language guide section on parsable lambdas
- Library author guide for consuming expression trees

### Implementation Details

#### 3.1 Multi-Parameter Expressions

Support `fn ($a, $b) => $a->prop > $b->prop` for comparison-style expressions:

```php
// Tyhp:
$list->sortBy(fn ($a, $b) => $a->lastName <=> $b->lastName);

// The Expression tree has two ParameterExpression nodes
```

The type becomes `Expression<T, T, int>` for comparators.

#### 3.2 `nameof` Integration

Allow `nameof` to work with `PropertyPath` for a lightweight property name extraction:

```php
// These should be equivalent:
string $col1 = nameof(fn (User $u) => $u->firstName);  // 'firstName'
string $col2 = nameof(User::$firstName);                 // 'firstName' (existing)
```

The first form is more refactoring-safe when the property is deeply nested or when you want the full path.

`nameof` always returns the **last segment** of the property chain, following C#'s convention. For example:
- `nameof(fn (User $u) => $u->firstName)` → `'firstName'`
- `nameof(fn (User $u) => $u->address->city)` → `'city'`
- `nameof(fn (User $u) => $u->address)` → `'address'`

For the full property path, use `PropertyPath` instead, which captures the complete chain.

#### 3.3 Expression Serialization

Add `ExpressionSerializer` that converts expression trees to JSON:

```php
$expr = /* Expression from fn (User $u) => $u->age > 18 */;
$json = \Tyhp\Expression\ExpressionSerializer::toJson($expr);
// {
//   "nodeType": "binary",
//   "operator": ">",
//   "left": { "nodeType": "propertyAccess", "property": "age", ... },
//   "right": { "nodeType": "constant", "value": 18, "type": "int" }
// }
```

This enables passing expression trees across API boundaries (e.g., client-side query building sent to a server).

#### 3.4 Expression Equality and Comparison

Implement structural equality for expression trees:

```php
$a = fn (User $u) => $u->firstName;
$b = fn (User $u) => $u->firstName;
// $a and $b produce structurally equal expression trees (even though they're different closures)
```

This is useful for caching (e.g., caching compiled SQL for a given expression shape).

#### 3.5 Example: Type-Safe Query Builder

Create a complete example showing how a library author would use expression trees:

```php
// Library code (consuming expressions):
class QueryBuilder<T> {
    /** @var array<Expression> */
    private array $conditions = [];

    public function where(Expression<T, bool> $predicate): static {
        $this->conditions[] = $predicate;
        return $this;
    }

    public function select<R>(Expression<T, R> $selector): QueryBuilder<R> {
        // Walk the expression tree to build SQL SELECT clause
        $visitor = new SqlSelectVisitor();
        $sql = $visitor->visit($selector->body);
        // ...
    }

    public function toSql(): string {
        // Build complete SQL from accumulated expressions
    }
}

// User code:
int $minAge = 18;
$query = new QueryBuilder<User>()
    ->where(fn ($u) => $u->age > $minAge && $u->isActive)
    ->select(fn ($u) => $u->firstName);

string $sql = $query->toSql();
// SELECT first_name FROM users WHERE age > 18 AND is_active = true
```

#### 3.6 Supported Expression Catalog (Final)

Document the complete list of supported and unsupported expression node types:

**Supported in `fn` expression trees:**
- Property access (`$x->prop`)
- Null-safe property access (`$x?->prop`)
- Method calls (`$x->method()`)
- Static method calls (`Class::method()`)
- Binary operators (`+`, `-`, `*`, `/`, `%`, `.`, `==`, `!=`, `===`, `!==`, `<`, `>`, `<=`, `>=`, `<=>`, `&&`, `||`, `and`, `or`, `??`, `**`)
- Unary operators (`!`, `-`, `+`, `~`)
- Constants and literals (int, float, string, bool, null)
- Captured variables (from enclosing scope)
- Array access (`$x->items[$i]`)
- Ternary (`$x ? $y : $z`)
- Null coalescing (`$x ?? $y`)
- Type casts (`(int) $x->value`)
- `new` expressions (`new Money($x->amount)`)
- `instanceof` / `is` checks (`$x->value is int`) — *newly added in Phase 3*

**Not supported (produce checker error):**
- Assignments (`$x = ...`)
- `await` expressions
- `yield` / `yield from`
- `match` expressions (may be added in future)
- `throw` expressions
- `include` / `require`
- `eval`
- Nested `fn` / `function` expressions
- Statement-body constructs (if/else, for, while, etc.)
- `print` / `echo`

### Acceptance Criteria

- [x] Multi-parameter expressions work correctly
- [x] `nameof` integration with PropertyPath works
- [x] Expression serialization to JSON produces correct output
- [x] Expression structural equality works
- [x] Example query builder demonstrates end-to-end expression tree consumption
- [x] All supported expression types are documented
- [x] Unsupported expression types produce clear checker errors
- [x] Language guide documentation is complete
- [x] All tests pass

### Dependencies

- **Requires:** Phase 2 (full expression tree infrastructure)
- **Provides:** Complete parsable lambda feature ready for ecosystem adoption

---

## New MessageCode Values

```
// Expression tree errors (4320–4324) — Story 16
CheckerPropertyPathRequiresInlineFn = 4320,    // "Parameter of type 'PropertyPath<{0}, {1}>' requires an inline fn expression"
CheckerPropertyPathInvalidBody = 4321,         // "PropertyPath expression must be a simple property access chain"
CheckerExpressionUnsupportedNode = 4322,       // "Expression trees do not support '{0}' expressions; simplify the fn body"
CheckerExpressionRequiresInlineFn = 4323,      // "Parameter of type 'Expression<{0}, {1}>' requires an inline fn expression"
CheckerExpressionCapturedVarUndefined = 4324,  // "Captured variable '${0}' in expression tree must be definitely assigned"
```

---

## Testing Strategy

### Unit Tests

**PropertyPath tests (Phase 1):**
- `PropertyPath` construction and property access
- Single-segment paths
- Multi-segment paths
- Nullable chains
- Type resolution
- Callable execution via `__invoke`
- Error cases: non-fn argument, non-chain body

**Expression tree tests (Phase 2):**
- Every expression node type: construction, visitor dispatch, type tracking
- Expression tree emission for each supported expression kind
- Captured variable handling
- ExpressionVisitor walks complete trees
- Type compatibility checks
- `\Closure` extraction when needed

**End-to-end tests (Phase 3):**
- Compile Tyhp files with PropertyPath/Expression usage → valid PHP output
- PHP output executes correctly (PropertyPath returns correct values, Expression trees have correct structure)
- Example query builder produces expected SQL strings

### Snapshot Tests

- For each expression type, create a Tyhp input and expected PHP output golden file
- Verify the emitter produces the exact expected expression tree construction code

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the parsable lambdas (expression trees) implementation. Steps can be skipped, reordered, or modified as needed. You need: a built `tyhp` binary, PHP 8.4+ installed, and the `tyhp/lambda` Composer package available.

### Step 1: Verify the Build Compiles

```bash
cd /path/to/tyhp
dotnet build
```

Confirm zero errors. The new checker and emitter transformer files should compile cleanly.

### Step 2: Set Up a Test Project

```bash
mkdir -p /tmp/tyhp-expr-test/src
cd /tmp/tyhp-expr-test
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

Initialize Composer and require the `tyhp/lambda` runtime package:

```bash
composer init --no-interaction --name="test/expr-test"
composer require tyhp/lambda
```

### Step 3: Test PropertyPath — Simple Property Chain

Create `src/PropertyPathTest.tyhp`:

```tyhp
<?tyhp

namespace App;

class User {
    public string $firstName;
    public string $lastName;
    public Address $address;
}

class Address {
    public string $city;
    public string $state;
    public string $zip;
}

class QueryBuilder {
    public function select(PropertyPath<User, string> $path): void {
        echo "Property: " . $path->getPropertyName() . "\n";
        echo "Full path: " . $path->getPath() . "\n";
    }
}

$qb = new QueryBuilder();
$qb->select(fn (User $u) => $u->firstName);
$qb->select(fn (User $u) => $u->address->city);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/PropertyPathTest.php
```

**Expected output:**

```
Property: firstName
Full path: firstName
Property: city
Full path: address.city
```

### Step 4: Verify PropertyPath Emitted PHP

Inspect the generated PHP file `build/PropertyPathTest.php`. The `$qb->select(...)` calls should be emitted as `new \Tyhp\PropertyPath(...)` constructions, NOT as closures:

```php
$qb->select(new \Tyhp\PropertyPath(
    \App\User::class,
    'string',
    ['firstName'],
    fn (\App\User $u) => $u->firstName
));
```

### Step 5: Test PropertyPath Callable Behavior

Create `src/PropertyPathCallable.tyhp`:

```tyhp
<?tyhp

namespace App;

class Product {
    public string $name;
    public float $price;

    public function __construct(string $name, float $price) {
        $this->name = $name;
        $this->price = $price;
    }
}

function extractField(PropertyPath<Product, string> $path): void {
    $product = new Product("Widget", 29.99);

    // PropertyPath is callable via __invoke
    $value = $path($product);
    echo "Extracted: {$value}\n";

    // Also callable via getValue
    $value2 = $path->getValue($product);
    echo "Via getValue: {$value2}\n";
}

extractField(fn (Product $p) => $p->name);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/PropertyPathCallable.php
```

**Expected output:**

```
Extracted: Widget
Via getValue: Widget
```

### Step 6: Test PropertyPath Error Cases

Create `src/PropertyPathErrors.tyhp`:

```tyhp
<?tyhp

namespace App;

class Foo {
    public int $value;
    public function getValue(): int { return $this->value; }
}

function takesPropPath(PropertyPath<Foo, int> $path): void {}

// ERROR: Method call instead of property access
takesPropPath(fn (Foo $f) => $f->getValue());

// ERROR: Binary expression, not a chain
takesPropPath(fn (Foo $f) => $f->value + 1);

// ERROR: Not an inline fn expression
$closure = fn (Foo $f) => $f->value;
takesPropPath($closure);
```

Compile:

```bash
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Three checker errors:
- Error 4321 for the method call body ("PropertyPath expression must be a simple property access chain")
- Error 4321 for the binary expression body
- Error 4320 for passing a variable instead of an inline `fn` ("requires an inline fn expression")

### Step 7: Test Expression Trees — Simple Comparison

Create `src/ExpressionTest.tyhp`:

```tyhp
<?tyhp

namespace App;

class User {
    public string $name;
    public int $age;
    public bool $isActive;
}

function testExpression(Expression<User, bool> $expr): void {
    echo "Return type: " . $expr->returnType . "\n";
    echo "Body node type: " . $expr->body->nodeType . "\n";

    // Can still execute it
    $user = new User();
    $user->name = "Alice";
    $user->age = 25;
    $user->isActive = true;

    $result = $expr($user);
    echo "Result: " . ($result ? "true" : "false") . "\n";
}

testExpression(fn (User $u) => $u->age > 18);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/ExpressionTest.php
```

**Expected output:**

```
Return type: bool
Body node type: binary
Result: true
```

### Step 8: Verify Expression Tree Structure

Create `src/ExpressionStructure.tyhp`:

```tyhp
<?tyhp

namespace App;

class Item {
    public string $name;
    public float $price;
    public bool $inStock;
}

function inspectTree(Expression<Item, bool> $expr): void {
    $body = $expr->body;

    // Should be a BinaryExpression with > operator
    if ($body instanceof \Tyhp\Expression\BinaryExpression) {
        echo "Operator: " . $body->operator . "\n";

        // Left side should be PropertyAccessExpression
        if ($body->left instanceof \Tyhp\Expression\PropertyAccessExpression) {
            echo "Left property: " . $body->left->property . "\n";
        }

        // Right side should be ConstantExpression
        if ($body->right instanceof \Tyhp\Expression\ConstantExpression) {
            echo "Right value: " . $body->right->value . "\n";
        }
    }
}

inspectTree(fn (Item $i) => $i->price > 9.99);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/ExpressionStructure.php
```

**Expected output:**

```
Operator: >
Left property: price
Right value: 9.99
```

### Step 9: Test Captured Variables

Create `src/CapturedVars.tyhp`:

```tyhp
<?tyhp

namespace App;

class User {
    public int $age;
}

function whereAge(Expression<User, bool> $expr): void {
    $body = $expr->body;
    if ($body instanceof \Tyhp\Expression\BinaryExpression) {
        if ($body->right instanceof \Tyhp\Expression\ConstantExpression) {
            echo "Captured value: " . $body->right->value . "\n";
        }
    }
}

int $minAge = 21;
whereAge(fn (User $u) => $u->age >= $minAge);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/CapturedVars.php
```

**Expected output:**

```
Captured value: 21
```

The `$minAge` variable should be captured as a `ConstantExpression` with its runtime value (21), not as a variable reference.

### Step 10: Test Expression Visitor

Create `src/VisitorTest.tyhp`:

```tyhp
<?tyhp

namespace App;

use \Tyhp\Expression\ExpressionVisitor;
use \Tyhp\Expression\PropertyAccessExpression;
use \Tyhp\Expression\BinaryExpression;
use \Tyhp\Expression\ConstantExpression;
use \Tyhp\Expression\ParameterExpression;

class Item {
    public float $price;
}

class SqlVisitor extends ExpressionVisitor {
    public function visitPropertyAccess(PropertyAccessExpression $node): mixed {
        return $node->property;
    }

    public function visitBinary(BinaryExpression $node): mixed {
        $left = $this->visit($node->left);
        $right = $this->visit($node->right);
        return "{$left} {$node->operator} {$right}";
    }

    public function visitConstant(ConstantExpression $node): mixed {
        return (string) $node->value;
    }

    public function visitParameter(ParameterExpression $node): mixed {
        return $node->name;
    }
}

function buildSql(Expression<Item, bool> $expr): string {
    $visitor = new SqlVisitor();
    return $visitor->visit($expr->body);
}

$sql = buildSql(fn (Item $i) => $i->price > 100);
echo "SQL fragment: WHERE {$sql}\n";
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/VisitorTest.php
```

**Expected output:**

```
SQL fragment: WHERE price > 100
```

### Step 11: Test Unsupported Expression Errors

Create `src/UnsupportedExpr.tyhp`:

```tyhp
<?tyhp

namespace App;

class Foo {
    public int $x;
}

function takesExpr(Expression<Foo, int> $expr): void {}

// ERROR: Assignment is not supported in expression trees
takesExpr(fn (Foo $f) => $f->x = 5);

// ERROR: Nested closure is not supported
takesExpr(fn (Foo $f) => (fn () => $f->x)());
```

Compile:

```bash
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Checker errors with code 4322 ("Expression trees do not support ... expressions").

### Step 12: Test PropertyPath Extends Expression (Backward Compatibility)

Create `src/BackwardCompat.tyhp`:

```tyhp
<?tyhp

namespace App;

class User {
    public string $name;
}

// Function accepts Expression<User, string>
function acceptsExpression(Expression<User, string> $expr): void {
    echo "Body type: " . $expr->body->nodeType . "\n";
    $user = new User();
    $user->name = "Bob";
    echo "Result: " . $expr($user) . "\n";
}

// Passing a PropertyPath-compatible fn (simple property chain)
// should work since PropertyPath extends Expression
acceptsExpression(fn (User $u) => $u->name);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/BackwardCompat.php
```

**Expected output:**

```
Body type: propertyAccess
Result: Bob
```

### Step 13: Test Expression Serialization

Create `src/SerializeTest.tyhp`:

```tyhp
<?tyhp

namespace App;

use \Tyhp\Expression\ExpressionSerializer;

class Product {
    public float $price;
}

function serializeExpr(Expression<Product, bool> $expr): void {
    $json = ExpressionSerializer::toJson($expr);
    echo $json . "\n";
}

serializeExpr(fn (Product $p) => $p->price > 50.0);
```

Compile and run:

```bash
dotnet run --project /path/to/tyhp -- build
php build/SerializeTest.php | python3 -m json.tool
```

**Expected:** Valid JSON output describing the expression tree structure, including `nodeType`, `operator`, `property`, and `value` fields.

### Step 14: Clean Up

```bash
rm -rf /tmp/tyhp-expr-test
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
