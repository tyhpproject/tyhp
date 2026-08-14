# Implementation Plan: Story 26 — Null-Conditional Chaining with Assignment

> **Roadmap position:** Story 26 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** all earlier stories (01–25)
> **Renumbered from:** legacy Story 19
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `Syntax_TODO.md` item "Null-Conditional Chaining with Assignment"
> **Branch:** TBD
> **Generated:** 2026-02-17
> **Prerequisites:** All earlier stories (01–25) must be complete — the parser, binder, checker, emitter, Tyhp runtime packages, LSP, and testing infrastructure must be fully functional.

---

## Architecture Overview

### What Null-Conditional Assignment Is

PHP 8.0 introduced the null-safe operator `?->` for reading properties and calling methods on nullable objects:

```php
// PHP — reading is allowed:
$city = $user?->address?->city; // returns null if $user or $address is null

// PHP — assignment is NOT allowed:
$user?->address?->city = 'Berlin'; // Fatal error: Can't use nullsafe operator in write context
```

Tyhp extends PHP by allowing assignment through `?->`. When any `?->` in the chain encounters `null`, the entire assignment becomes a **no-op** and the expression evaluates to `null`. When no nulls are encountered, the assignment proceeds normally and the expression evaluates to the assigned value.

This eliminates verbose null-checking boilerplate:

```php
// Before (PHP):
if ($user !== null) {
    if ($user->address !== null) {
        $user->address->city = 'Berlin';
    }
}

// After (Tyhp):
$user?->address?->city = 'Berlin';
```

### Design Decisions

1. **All assignment operators are supported.** Any assignment operator that works without `?->` also works with it. This is the set of **14 standard PHP assignment operators** defined by `phpExprAssignmentOps` in `PhpParser.g4`: `=`, `+=`, `-=`, `.=`, `??=`, `*=`, `/=`, `%=`, `**=`, `&=`, `|=`, `^=`, `<<=`, `>>=`. In addition, the Tyhp-specific using-equal operator `:=` is supported, but it is not part of the standard 14 — it is expanded by the using-equal transformer first and the null-safe wrapper is then applied to the result (see Phase 4.4 and Phase 4.9).

2. **Chaining works.** `$obj?->child?->prop = 'value'` — if ANY `?->` in the chain evaluates to null, the entire expression becomes a no-op and evaluates to `null`.

3. **Expression value.** The expression returns `null` when the object is null, and the assigned value when the object is non-null. So `$result = ($obj?->prop = 'value')` gives `$result` the value `'value'` or `null`.

4. **PHP emission uses inline ternary** for maximum composability:
   - Simple case: `$obj?->prop = 'value'` → `(($obj !== null) ? ($obj->prop = 'value') : null)`
   - Chained case: `$obj?->child?->prop = 'value'` → `(($obj !== null) ? (($obj->child !== null) ? ($obj->child->prop = 'value') : null) : null)`
   - With null coalesce: `$obj?->prop ??= 'default'` → `(($obj !== null) ? ($obj->prop ??= 'default') : null)`

5. **Side-effect safety.** When the object expression has side effects (e.g., a function call), it must only be evaluated once. The emitter introduces a temporary variable in these cases.

### Position in the Pipeline

```
ALL earlier stories complete
(Stories 01–25)
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 26: Null-Conditional Chaining with Assignment      │
│  ◄── THIS PLAN                                           │
│                                                          │
│  Touches: Grammar, Binder, Checker, Emitter, Testing     │
│                                                          │
│  Phase 1: Grammar changes                                │
│  Phase 2: Binder tracking                                │
│  Phase 3: Checker validation                             │
│  Phase 4: Emitter transformation                         │
│  Phase 5: Testing                                        │
└──────────────────────────────────────────────────────────┘
```

### How the Grammar Currently Works

The existing grammar handles assignment and null-safe access as follows:

**Assignment** — In `PhpParser.g4`, assignment is a binary expression rule:

```
phpExprPrec
    : ...
    | <assoc=right> L=phpExprPrec Op=phpExprAssignmentOps R=phpExprPrec   #phpExprAssignment
    | ...
    ;
```

The left-hand side `L=phpExprPrec` can be any expression, and `phpExprPrec` can resolve to `fullyDereferenceable` (which includes `?->` chains via `dereferenceableMemberAccessSuffix`). However, **a Tyhp-mode grammar addition IS required** for this story: there is currently no dedicated rule that marks a null-safe assignment as a distinct construct. `PhpParser.g4` exposes an (as-yet-unused) extension hook `phpExprPrecBaseGrammarAddon` (its default body is the no-op `T_NO_GRAMMAR_ADDON_0000`), and `TyhpParser.g4` does **not** currently override it. Phase 1 adds that override so the visitor/binder can reliably and unambiguously detect null-safe assignment. The bulk of the remaining work is in the **binder**, **checker**, and **emitter**, but the parser change is a prerequisite, not optional.

**Null-safe member access** — In `PhpParser.g4`, the `dereferenceableMemberAccessSuffix` rule already accepts both `T_OBJECT_OPERATOR` (`->`) and `T_NULLSAFE_OBJECT_OPERATOR` (`?->`):

```
dereferenceableMemberAccessSuffix
    : (TokenValue=T_OBJECT_OPERATOR | TokenValue=T_NULLSAFE_OBJECT_OPERATOR)
        MemberName=memberName
    ;
```

**Variable rule** — `variable` simply delegates to `fullyDereferenceable`, which is a left-recursive chain of suffixes (member access, array access, method calls, etc.).

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup using the canonical naming: `<filename>.bak.<YYYYMMDD_HHMMSS>`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Grammar — Ensure Parser Acceptance of Null-Safe Assignment LHS

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Extend the ANTLR4 grammar so that Tyhp's parser recognizes assignment expressions whose left-hand side uses `?->` (null-safe property access) as a **distinct Tyhp construct**. The base `#phpExprAssignment` rule (`L=phpExprPrec Op=phpExprAssignmentOps R=phpExprPrec`) accepts the raw token sequence because `phpExprPrec` permits `?->` chains, but it does not distinguish null-safe assignment from an ordinary assignment. **A Tyhp-mode grammar addition is required** so the downstream visitor/binder, checker, and emitter can identify and transform these expressions reliably. This phase makes that change (overriding the `phpExprPrecBaseGrammarAddon` hook in `TyhpParser.g4`) and verifies the regenerated parser.

### Deliverables

- **Modified:** `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — Assignment target rule extended for null-safe chains
- **Not modified:** `Tyhp/TyhpLang/Grammar/PhpParser.g4` — Base grammar is untouched

### Implementation Details

**Grammar verification result:** `?->` (`T_NULLSAFE_OBJECT_OPERATOR`, defined in `PhpParser.g4`) is already parsed as a dereference suffix in expression context. The assignment rule itself is `#phpExprAssignment` (`L=phpExprPrec Op=phpExprAssignmentOps R=phpExprPrec`) — there is **no** `assignmentTarget`/`assignableExpression` rule; those names do not exist in the grammar. Because `L=phpExprPrec` can already contain `?->` chains, the base grammar accepts the raw token sequence, but it does not tag the result as a null-safe assignment. Therefore:

- **Grammar change required:** In `TyhpParser.g4` (NOT `PhpParser.g4`), override the `phpExprPrecBaseGrammarAddon` extension hook (currently the no-op `T_NO_GRAMMAR_ADDON_0000` in `PhpParser.g4`) to add a Tyhp-mode alternative that matches a null-safe assignment and labels it `#tyhpNullSafeAssignment`.
- The checker validates that the `?->` assignment is semantically valid (the chain must be an object property chain, not a method call chain).
- The emitter transforms the `?->` assignment into nested null-check ternary expressions (specified in Phase 4).

#### 1.1 Verify Current Parser Behavior

Test whether the existing parser accepts the following Tyhp source patterns without syntax errors:

```php
// Simple null-safe assignment
$obj?->prop = 'value';

// Chained null-safe assignment
$obj?->child?->prop = 'value';

// Mixed chain (regular -> and null-safe ?->)
$obj->child?->prop = 'value';
$obj?->child->prop = 'value';

// All assignment operators
$obj?->prop += 1;
$obj?->prop -= 1;
$obj?->prop .= 'suffix';
$obj?->prop ??= 'default';
$obj?->prop *= 2;
$obj?->prop /= 2;
$obj?->prop %= 2;
$obj?->prop **= 2;
$obj?->prop &= 0xFF;
$obj?->prop |= 0x01;
$obj?->prop ^= 0xFF;
$obj?->prop <<= 2;
$obj?->prop >>= 2;

// Using equal (:= for disposable assignment, Tyhp-specific)
$obj?->prop := new Resource();

// Null-safe assignment in expression context
$result = ($obj?->prop = 'value');

// Function call as object (side-effect case)
getObj()?->prop = 'value';

// Array access after null-safe
$obj?->items[0] = 'value';

// Nested null-safe in array destructuring (parses successfully but rejected by checker — see Phase 3, §3.7)
[$obj?->prop] = $values;
```

Use the `DebugAction` or a custom test harness to parse these. Record which patterns succeed and which fail.

#### 1.2 Grammar Changes

The `#phpExprAssignment` alternative uses `L=phpExprPrec` on the LHS, and `phpExprPrec` resolves through `phpExprBaseHandler` → `phpExprBase` → `variable` → `fullyDereferenceable` → chains with `dereferenceableMemberAccessSuffix` (which includes `T_NULLSAFE_OBJECT_OPERATOR`). The base grammar therefore already parses `$obj?->prop = 'value'` as a `#phpExprAssignment` whose `L` is a `fullyDereferenceable` chain containing a null-safe suffix.

To make the parser explicitly recognize null-safe assignment as a Tyhp-specific construct (and to enable the visitor to produce the correct AST flag), override the `phpExprPrecBaseGrammarAddon` hook in `TyhpParser.g4`:

```antlr
// In TyhpParser.g4 (overrides the no-op hook from PhpParser.g4):
phpExprPrecBaseGrammarAddon
    : NullSafeTarget=fullyDereferenceable Op=phpExprAssignmentOps
        Value=phpExprPrec {this.isLanguageMode("tyhp")}?       #tyhpNullSafeAssignment
    ;
```

This alternative is intended to match when the `fullyDereferenceable` chain contains at least one `T_NULLSAFE_OBJECT_OPERATOR` suffix. The Tyhp language mode guard ensures PHP-mode parsing is unaffected.

<!-- REVIEW: ANTLR alternative-ordering risk. `#phpExprAssignment` is a direct alternative of the left-recursive `phpExprPrec` rule and will generally match `$obj?->prop = X` before the parser descends into `phpExprBase` → `phpExprPrecBaseGrammarAddon`. As a result, the `#tyhpNullSafeAssignment` label above may never fire for a top-level assignment. Implementers must validate during Phase 1 whether the override actually intercepts these expressions; if it does not, the robust path is to detect null-safe assignment in the visitor by inspecting the LHS of every `#phpExprAssignment` for `?->` suffixes (exactly the approach already specified in Phase 2 §2.1) and skip the grammar override. Either way, the parser/visitor must end up with a reliable signal — do not rely on the grammar override alone without verifying it triggers. -->

#### 1.3 Regenerate Parser

After modifying the grammar:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone. Then verify existing test files still parse correctly.

### Acceptance Criteria

- [ ] All target syntax patterns listed in 1.1 parse without errors
- [ ] Grammar changes in `TyhpParser.g4` are guarded by `{this.isLanguageMode("tyhp")}?` so PHP-mode parsing is unaffected
- [ ] The generated parser compiles without errors
- [ ] Existing parser test data files (`TestData/ValidTyhp/**/*.tyhp`) continue to parse without regressions

### Dependencies

- **Requires:** All earlier stories complete (01–25 — full compiler pipeline working)
- **Provides:** Parser acceptance of null-safe assignment syntax for Phase 2

---

## Phase 2: Binder — Track Null-Conditional Assignment Expressions

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Extend the binder to recognize and annotate assignment expressions where the left-hand side contains one or more null-safe property accesses (`?->`). These expressions need to be flagged so the checker can validate them and the emitter can transform them into ternary-wrapped PHP output.

### Deliverables

**Modified compiler files:**
- `Tyhp/TyhpLang/Visitor/` — Visitor changes to detect null-safe LHS in assignments and produce the correct AST node or flag
- Potentially a new AST node or flag type to represent null-conditional assignments

### Implementation Details

#### 2.1 Detect Null-Safe Assignment in the Visitor/Binder

When the visitor processes a `phpExprAssignment` parse tree node, it must check whether the LHS expression (`L`) contains any null-safe member access operators (`T_NULLSAFE_OBJECT_OPERATOR`). This detection can happen at any of several points:

**Approach: AST flag on the existing assignment AST node.**

Add a boolean property `HasNullSafeChain` (or equivalent) to the existing assignment expression AST node. When the visitor creates the assignment AST, it walks the LHS to check for null-safe operators and sets this flag.

```csharp
// In the assignment AST class:
public bool HasNullSafeChain { get; set; } = false;
```

This approach is simpler and sufficient — the emitter can walk the LHS AST to extract chain segments at emit time. A dedicated AST node is not warranted since null-safe assignment does not require fundamentally different processing throughout the pipeline.

#### 2.2 Extract Null-Safe Chain Segments

Regardless of which AST representation is used, the binder (or a helper utility) must be able to extract the **null-safe chain segments** from an assignment LHS. Given:

```php
$obj?->child?->grandchild->prop = 'value';
```

The chain segments are:

| Segment | Expression | Is Null-Safe | Needs Null Check |
|---------|-----------|--------------|------------------|
| 1 | `$obj` | Yes (`?->child`) | Yes |
| 2 | `$obj->child` | Yes (`?->grandchild`) | Yes |
| 3 | `$obj->child->grandchild` | No (`->prop`) | No |

The null checks wrap **outside-in**: the outermost ternary checks `$obj !== null`, the next ternary checks `$obj->child !== null`, and so on.

**Extraction algorithm:**

```
Given the LHS of an assignment (a fullyDereferenceable chain):
1. Start from the outermost (leftmost) expression
2. Walk the suffix chain from left to right
3. For each suffix that is a member access:
   a. If the operator is T_NULLSAFE_OBJECT_OPERATOR (?->):
      - Record this segment as requiring a null check
      - The checked expression is everything LEFT of this ?->
   b. If the operator is T_OBJECT_OPERATOR (->):
      - No null check needed for this segment
4. The final segment (the property being assigned to) uses regular ->
   in the emitted PHP (since ?-> cannot be used in write context in PHP)
```

#### 2.3 Register Symbol Information

The binder should record in its scope/symbol data that a null-safe assignment exists at this location. This enables:

- The checker to validate the expression
- The emitter to find it efficiently
- The LSP to provide hover information ("this assignment is null-conditional")

No new symbols are created — null-safe assignment is a property of an existing assignment expression, not a new declaration.

### Acceptance Criteria

- [ ] The visitor/binder correctly identifies assignment expressions with null-safe LHS
- [ ] The `HasNullSafeChain` flag (or equivalent) is set on the assignment AST node
- [ ] Chain segment extraction correctly identifies which segments need null checks
- [ ] Assignments without null-safe operators are unaffected (flag is false)
- [ ] Mixed chains (`$obj->a?->b->c?->d = val`) correctly identify only the null-safe segments
- [ ] Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (parser accepts null-safe assignment syntax)
- **Provides:** AST annotations for Phase 3 (checker) and Phase 4 (emitter)

---

## Phase 3: Checker — Validate Null-Conditional Assignments

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Extend the checker to validate null-conditional assignment expressions. The checker must verify type compatibility, compute the expression's result type, and ensure type narrowing is handled correctly.

### Deliverables

**Modified compiler files:**
- `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs` (or equivalent checker file) — Add validation logic for null-safe assignments

### Implementation Details

#### 3.1 Type Compatibility of the Assignment

The checker must verify that the right-hand side value is compatible with the target property's type, just as it would for a regular assignment. The null-safe chain does not change what types are valid for assignment — it only changes whether the assignment executes.

```php
// If User::$name is typed as string:
$user?->name = 'Alice';    // OK: 'Alice' is string
$user?->name = 42;         // ERROR: int is not assignable to string
$user?->name .= ' Smith';  // OK: string concat produces string
$user?->name += 1;         // ERROR: += requires numeric, but $name is string
```

The checker calls its existing assignment compatibility logic for the inner assignment (ignoring the null-safe wrapper). The null-safe chain only affects the **result type** of the overall expression.

#### 3.2 Compute the Expression Result Type

The result type of a null-conditional assignment is `T|null`, where `T` is the type that the assignment would produce if the object were non-null.

```php
// $user is ?User, User::$name is string
$result = ($user?->name = 'Alice');
// $result type: string|null
// - string when $user is not null (the assignment returns 'Alice')
// - null when $user is null (the no-op returns null)

// Chained:
$result = ($company?->ceo?->name = 'Alice');
// $result type: string|null
// - string when both $company and $company->ceo are not null
// - null when either is null
```

**Special case — `??=` operator:**

```php
$user?->name ??= 'default';
// The inner assignment ($user->name ??= 'default') has type string
// (because ??= assigns 'default' if null, so the result is always string)
// The outer null-conditional makes it string|null
// $result type: string|null
```

**Special case — already-nullable property:**

```php
// User::$nickname is ?string
$result = ($user?->nickname = 'Bob');
// The assignment `$user->nickname = 'Bob'` evaluates to 'Bob' (string).
// The property type is ?string but the assigned value is string.
// Result type: string|null
// - string when $user is not null
// - null when $user is null
```

**Implementation rule:** The expression type is `union(T, null)` where `T` is the type of the RHS value (for `=`) or the type the compound operator produces (for `+=`, `.=`, etc.). If `T` already includes `null`, the result type is simply `T`.

#### 3.3 Validate All Assignment Operators

Each assignment operator must be validated for compatibility with the target property type:

| Operator | Validation Rule |
|----------|----------------|
| `=` | RHS type is assignable to property type |
| `+=`, `-=`, `*=`, `/=`, `%=`, `**=` | Property type and RHS type must be numeric-compatible |
| `.=` | Property type must be string-compatible |
| `&=`, `\|=`, `^=`, `<<=`, `>>=` | Property type and RHS type must be integer-compatible |
| `??=` | RHS type must be assignable to property type; property must be nullable |

These rules are identical to regular (non-null-safe) assignment validation. The null-safe wrapper does not change the inner validation logic.

#### 3.4 Type Narrowing After Null-Conditional Assignment

After a null-conditional assignment, the object variable is **NOT narrowed**. The object could still be null — the assignment was conditional:

```php
?User $user = getUser();
$user?->name = 'Alice';
// $user is still ?User here — NOT narrowed to User
// The assignment may or may not have executed

// Compare with regular null check:
if ($user !== null) {
    $user->name = 'Alice';
    // $user is narrowed to User inside this block
}
```

The checker must ensure that its type narrowing system does not accidentally narrow the object type after a null-conditional assignment.

#### 3.5 Validate Chain Segment Types

Each segment in the null-safe chain must be validated:

```php
$obj?->child?->prop = 'value';
```

- `$obj` must be a nullable object type (`?SomeClass` or `SomeClass|null` or union containing null)
- `$obj->child` must be a valid property of `SomeClass` (after stripping null from `$obj`'s type)
- `$obj->child->prop` must be a valid property of the type of `child`
- If `$obj` is non-nullable, using `?->` on it is valid but unnecessary — the checker may emit a hint/info diagnostic suggesting `->` instead (optional, low priority)

#### 3.6 Edge Case: Null-Safe on Non-Nullable Object

```php
User $user = getUser(); // non-nullable
$user?->name = 'Alice'; // ?-> on non-nullable type
```

This is **syntactically valid** and **semantically safe** (it's equivalent to `$user->name = 'Alice'`). The checker should:
- Accept it without error
- Emit an info-level diagnostic `CheckerUnnecessaryNullSafeOperator` (4340): "Null-safe operator '?->' used on non-nullable type '{0}'. Consider using '->' instead."

#### 3.7 Unsupported Null-Safe Patterns

The following patterns are syntactically valid (the parser accepts them) but are not supported in this story. The checker must reject them with specific diagnostics:

**Increment/decrement operators with `?->`:**

```php
$obj?->count++;
$obj?->count--;
++$obj?->count;
--$obj?->count;
```

The checker must reject these with `CheckerNullSafeIncrementNotSupported` (4341). These are unary operators, not assignment operators, and their null-safe wrapping semantics are out of scope for this story.

**Array destructuring with null-safe targets:**

```php
[$obj?->prop] = $values;
```

The checker must reject these with `CheckerNullSafeArrayDestructuringNotSupported` (4342). Array destructuring with null-safe property access targets has complex semantics that are out of scope for this story.

### Acceptance Criteria

- [ ] Regular assignment type validation runs on the inner assignment (ignoring the null-safe wrapper)
- [ ] Expression result type is correctly computed as `T|null`
- [ ] All 14 standard assignment operators (plus the Tyhp-specific `:=`) are validated correctly with null-safe chains
- [ ] Type narrowing does NOT occur on the object variable after null-conditional assignment
- [ ] Chain segment types are validated (each property access resolves correctly)
- [ ] Non-nullable objects with `?->` are accepted without error and emit `CheckerUnnecessaryNullSafeOperator` (4340) info diagnostic
- [ ] `++`/`--` with `?->` produces `CheckerNullSafeIncrementNotSupported` (4341)
- [ ] Array destructuring with null-safe targets produces `CheckerNullSafeArrayDestructuringNotSupported` (4342)
- [ ] Invalid assignments (type mismatch) produce the same checker errors as regular assignments
- [ ] Code compiles without errors

### Dependencies

- **Requires:** Phase 2 (binder tracking of null-safe assignments)
- **Provides:** Validated AST for Phase 4 (emitter)

---

## Phase 4: Emitter — Transform Null-Conditional Assignments to Nested Ternaries

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Transform null-conditional assignment expressions into valid PHP code using nested ternary expressions with null checks. This is the most complex phase — it must handle simple cases, chained cases, all assignment operators, side-effect safety, and edge cases.

### Deliverables

**New compiler file:**
- `Tyhp/TyhpLang/Emitter/Transformers/NullSafeAssignmentTransformer.cs` — The transformation logic

**Modified compiler files:**
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` (or equivalent) — Register and invoke the transformer

### Implementation Details

#### 4.1 Simple Case — Single `?->` Before Assignment

**Tyhp input:**
```php
$obj?->prop = 'value';
```

**Emitted PHP:**
```php
(($obj !== null) ? ($obj->prop = 'value') : null)
```

**Transformation algorithm:**
1. Identify the LHS as a null-safe property access chain with one `?->` segment
2. Extract the object expression (`$obj`) and the property name (`prop`)
3. Emit: `((<object> !== null) ? (<object>->prop <op> <value>) : null)`
4. Note: the `?->` becomes `->` inside the true branch (since we already checked for null)

#### 4.2 Chained Case — Multiple `?->` Segments

**Tyhp input:**
```php
$obj?->child?->prop = 'value';
```

**Emitted PHP:**
```php
(($obj !== null) ? (($obj->child !== null) ? ($obj->child->prop = 'value') : null) : null)
```

**Tyhp input (three levels):**
```php
$a?->b?->c?->d = 'value';
```

**Emitted PHP:**
```php
(($a !== null) ? (($a->b !== null) ? (($a->b->c !== null) ? ($a->b->c->d = 'value') : null) : null) : null)
```

**Transformation algorithm for N null-safe segments:**
```
Given chain segments: [seg1?->, seg2?->, ..., segN?->] and final property assignment:

1. Start with the innermost assignment:
   result = "<full_chain>->finalProp <op> <value>"

2. For each null-safe segment, from innermost to outermost:
   result = "(<segN_object> !== null) ? (" + result + ") : null"

3. Wrap entire expression in parentheses:
   result = "(" + result + ")"
```

#### 4.3 Mixed Chains — Some `->` and Some `?->`

**Tyhp input:**
```php
$obj->child?->grandchild->prop = 'value';
```

Here, only `?->grandchild` is null-safe. `->child` and `->prop` are regular.

**Emitted PHP:**
```php
(($obj->child !== null) ? ($obj->child->grandchild->prop = 'value') : null)
```

Only the segments with `?->` generate null checks. Regular `->` segments are emitted as-is.

#### 4.4 All Assignment Operators

Every assignment operator works identically — the operator is preserved inside the ternary:

**Tyhp → PHP examples:**

```php
// +=
$obj?->count += 1;
// → (($obj !== null) ? ($obj->count += 1) : null)

// .=
$obj?->name .= ' Jr.';
// → (($obj !== null) ? ($obj->name .= ' Jr.') : null)

// ??=
$obj?->prop ??= 'default';
// → (($obj !== null) ? ($obj->prop ??= 'default') : null)

// **=
$obj?->value **= 2;
// → (($obj !== null) ? ($obj->value **= 2) : null)

// <<=
$obj?->flags <<= 2;
// → (($obj !== null) ? ($obj->flags <<= 2) : null)

// :=  (Tyhp using-equal for disposables)
$obj?->resource := new DbConnection();
// → The using-equal transformer runs first, then the null-safe wrapper is applied
```

#### 4.5 Side-Effect Safety — Temp Variable for Non-Simple Objects

When the object expression has side effects (function calls, method calls, or any expression that could produce different results on re-evaluation), the emitter must evaluate it only once using a temporary variable.

**Tyhp input:**
```php
getUser()?->name = 'Alice';
```

**WRONG — evaluates `getUser()` twice:**
```php
((getUser() !== null) ? (getUser()->name = 'Alice') : null) // BUG!
```

**CORRECT — uses temp variable:**
```php
(($__tyhp_nsa_0 = getUser()) !== null ? ($__tyhp_nsa_0->name = 'Alice') : null)
```

**Detection of side-effect expressions:**

An expression needs a temp variable if it is anything other than:
- A simple variable (`$obj`)
- A static property (`ClassName::$prop`)

Specifically, these need temp variables:
- Function calls: `getUser()?->prop = val`
- Method calls: `$factory->create()?->prop = val`
- Array access with side effects: `$arr[getIndex()]?->prop = val`
- Ternary/coalesce: `($a ?? $b)?->prop = val`
- Any expression in parentheses that contains calls: `(new Foo())?->prop = val`

**Temp variable naming:** Use `EmitHelpers.GenerateUniqueVarName("__tyhp_nsa")` (nsa = null-safe assignment) to generate collision-free temp variable names.

**Chained case with temp variables:**

```php
// Tyhp:
getCompany()?->getCeo()?->name = 'Alice';

// Emitted PHP:
(($__tyhp_nsa_0 = getCompany()) !== null
    ? (($__tyhp_nsa_1 = $__tyhp_nsa_0->getCeo()) !== null
        ? ($__tyhp_nsa_1->name = 'Alice')
        : null)
    : null)
```

Note: In the chained case, intermediate property accesses that are method calls also need temp variables. The rule is: any sub-expression that appears in both the null check AND the inner expression must be evaluated once.

**Optimization:** When the object is a simple variable, skip the temp variable:

```php
// Tyhp:
$user?->name = 'Alice';

// Emitted PHP (no temp needed — $user is a simple variable):
(($user !== null) ? ($user->name = 'Alice') : null)
```

#### 4.6 Chained Case — Intermediate Expressions Evaluated Once

For chained null-safe access like `$obj?->child?->prop = 'value'`, the intermediate expression `$obj->child` appears in both the null check and the inner assignment. Since `$obj->child` is a property read (not a method call), it is safe to evaluate it multiple times because property reads are idempotent (assuming no magic `__get`). However, for correctness and consistency, the emitter should consider introducing temp variables for intermediate chain segments when they involve method calls or other side-effecting expressions:

```php
// Tyhp:
$obj?->getChild()?->prop = 'value';

// Emitted PHP (temp for getChild() result):
(($obj !== null)
    ? (($__tyhp_nsa_0 = $obj->getChild()) !== null
        ? ($__tyhp_nsa_0->prop = 'value')
        : null)
    : null)
```

For simple property access chains (`$obj?->child?->prop = val`), the repeated evaluation of `$obj->child` in both the null check and the assignment is acceptable because property access is side-effect-free (barring magic methods). The emitter may optimize by not introducing temp variables for simple property reads.

#### 4.7 Statement Context vs. Expression Context

Null-conditional assignments can appear in two contexts:

**Statement context** (standalone statement):
```php
$obj?->prop = 'value';
// The emitted ternary is a valid PHP expression statement:
(($obj !== null) ? ($obj->prop = 'value') : null);
```

**Expression context** (used as a value):
```php
$result = ($obj?->prop = 'value');
// The emitted ternary is used as a value:
$result = (($obj !== null) ? ($obj->prop = 'value') : null);
```

Both contexts work naturally with the ternary emission — no special handling is needed.

#### 4.8 Interaction with Other Tyhp Features

**Struct property assignment:**
```php
// Tyhp:
?MyStruct $s = getStruct();
$s?->prop = 'value';

// Structs are backed by arrays, so struct property access emits as array key access.
// The null-safe wrapper still applies around the struct assignment:
// → (($s !== null) ? ($s['prop'] = 'value') : null)
```

The struct transformer (Story 11) runs first to convert `$s->prop` to `$s['prop']`, then the null-safe assignment transformer wraps the result.

**Property accessor hooks (PHP 8.4):**
```php
// If $obj->prop has a set hook:
$obj?->prop = 'value';
// → (($obj !== null) ? ($obj->prop = 'value') : null)
// The set hook fires inside the true branch. No special handling needed.
```

**Extension methods:**
Extension method calls appear on the object chain but cannot be on the final (assignment) position, since extensions add methods, not settable properties. The checker rejects `$obj?->extensionMethod() = val`.

**Increment/decrement operators:**
```php
$obj?->count++;
$obj?->count--;
++$obj?->count;
--$obj?->count;
```

These are not assignment operators but unary operators. They are out of scope for this story. The checker rejects these with `CheckerNullSafeIncrementNotSupported` (4341) — see Phase 3, §3.7.

**Array destructuring with null-safe targets:**

```php
[$obj?->prop] = $values;
```

Array destructuring with null-safe property access targets is out of scope for this story. The checker rejects these with `CheckerNullSafeArrayDestructuringNotSupported` (4342) — see Phase 3, §3.7.

#### 4.9 Transformer Registration

Register `NullSafeAssignmentTransformer` in the emitter's transformer pipeline. It should run:

1. **After** the struct transformer (so struct property access is already rewritten to array key access)
2. **After** the using-equal (`:=`) transformer (so disposable assignments are already expanded)
3. **Before** the final PHP code generation

The transformer checks each assignment expression for the `HasNullSafeChain` flag (from Phase 2). If present, it rewrites the assignment. If absent, it passes through unchanged.

### Acceptance Criteria

- [ ] Simple case: `$obj?->prop = 'value'` emits correct ternary wrapper
- [ ] Chained case: `$obj?->a?->b?->c = 'value'` emits correctly nested ternaries
- [ ] Mixed case: `$obj->a?->b->c = 'value'` only wraps the null-safe segment
- [ ] All 14 standard assignment operators emit correctly within the ternary (the Tyhp-specific `:=` is expanded by the using-equal transformer first, then wrapped)
- [ ] Side-effect safety: `getObj()?->prop = val` uses a temp variable
- [ ] Chained side effects: `getObj()?->getChild()?->prop = val` uses temp variables for each side-effecting sub-expression
- [ ] Simple variable objects do not use unnecessary temp variables
- [ ] Statement context and expression context both work
- [ ] Interaction with struct transformer: `$struct?->prop = val` emits `$struct['prop']` inside the ternary
- [ ] Emitted PHP is valid and executes correctly
- [ ] Code compiles without errors

### Dependencies

- **Requires:** Phase 2 (binder tracking), Phase 3 (checker validation)
- **Provides:** Complete null-safe assignment transformation for Phase 5 (testing)

---

## Phase 5: Testing

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create comprehensive tests for null-conditional assignment covering the parser, checker, emitter, and end-to-end behavior. Tests should verify both correct behavior and correct error reporting.

### Deliverables

**New test files:**
- Parser tests for null-safe assignment syntax
- Checker tests for type validation and error cases
- Emitter snapshot tests for PHP output verification
- End-to-end tests (Tyhp input → PHP output → PHP execution)

**New example files:**
- `Examples/NullConditionalAssignment.tyhp` — Comprehensive examples
- `Examples/NullConditionalAssignment.php` — Expected PHP output

> **Documentation note (Story 30 overlap):** The language-documentation content file `docs/content/tyhp_3300_nullConditionalAssignment.json` already exists in the repo. Language docs are owned by **Story 30** (docs & polish); Story 26 should **not** create or duplicate that file. If the existing content needs updates to match the final emission behavior, leave the authoritative doc edit to Story 30 and only note any required corrections here.

### Implementation Details

#### 5.1 Parser Tests

Verify that the parser accepts all valid null-safe assignment patterns:

```php
// All of these should parse without errors:
$obj?->prop = 'value';
$obj?->a?->b = 'value';
$obj?->a?->b?->c = 'value';
$obj->a?->b = 'value';
$obj?->a->b = 'value';
$obj?->prop += 1;
$obj?->prop -= 1;
$obj?->prop .= 'x';
$obj?->prop ??= 'default';
$obj?->prop *= 2;
$obj?->prop /= 2;
$obj?->prop %= 3;
$obj?->prop **= 2;
$obj?->prop &= 0xFF;
$obj?->prop |= 0x01;
$obj?->prop ^= 0xFF;
$obj?->prop <<= 2;
$obj?->prop >>= 2;
$result = ($obj?->prop = 'value');
getObj()?->prop = 'value';
$obj?->items[0]?->prop = 'value';
```

#### 5.2 Checker Tests — Valid Cases

```php
class User {
    public string $name;
    public int $age;
    public ?Address $address;
}

class Address {
    public string $city;
    public ?string $zip;
}

// All should pass checker validation:
?User $user = getUser();
$user?->name = 'Alice';                    // string = string ✓
$user?->age += 1;                          // int += int ✓
$user?->address?->city = 'Berlin';         // string = string ✓
$user?->address?->zip ??= '00000';        // ?string ??= string ✓

// Expression type should be string|null:
string|null $result = ($user?->name = 'Alice');

// Non-nullable object with ?-> should be accepted:
User $definiteUser = new User();
$definiteUser?->name = 'Bob';             // Valid (but unnecessary ?->)
```

#### 5.3 Checker Tests — Error Cases

```php
?User $user = getUser();

// Type mismatch:
$user?->name = 42;                         // ERROR: int not assignable to string
$user?->age = 'not a number';             // ERROR: string not assignable to int
$user?->name += 1;                         // ERROR: += requires numeric, name is string

// Expression type mismatch:
string $result = ($user?->name = 'Alice'); // ERROR: string|null not assignable to string

// Unsupported null-safe patterns:
$user?->age++;                             // ERROR 4341: increment not supported with ?->
[$user?->name] = $values;                  // ERROR 4342: array destructuring with ?-> not supported
```

#### 5.4 Emitter Snapshot Tests

For each transformation pattern, create a snapshot test with Tyhp input and expected PHP output:

**Test: Simple null-safe assignment**
```php
// Input (Tyhp):
$obj?->prop = 'value';

// Expected output (PHP):
(($obj !== null) ? ($obj->prop = 'value') : null);
```

**Test: Chained null-safe assignment**
```php
// Input (Tyhp):
$obj?->child?->prop = 'value';

// Expected output (PHP):
(($obj !== null) ? (($obj->child !== null) ? ($obj->child->prop = 'value') : null) : null);
```

**Test: Side-effect safety with function call**
```php
// Input (Tyhp):
getObj()?->prop = 'value';

// Expected output (PHP):
(($__tyhp_nsa_0 = getObj()) !== null ? ($__tyhp_nsa_0->prop = 'value') : null);
```

**Test: Null coalesce assignment**
```php
// Input (Tyhp):
$obj?->prop ??= 'default';

// Expected output (PHP):
(($obj !== null) ? ($obj->prop ??= 'default') : null);
```

**Test: Expression context**
```php
// Input (Tyhp):
$result = ($obj?->name = 'Alice');

// Expected output (PHP):
$result = (($obj !== null) ? ($obj->name = 'Alice') : null);
```

**Test: Mixed chain with regular and null-safe**
```php
// Input (Tyhp):
$obj->child?->grandchild->prop = 'value';

// Expected output (PHP):
(($obj->child !== null) ? ($obj->child->grandchild->prop = 'value') : null);
```

**Test: Three-level chain**
```php
// Input (Tyhp):
$a?->b?->c?->d = 'value';

// Expected output (PHP):
(($a !== null) ? (($a->b !== null) ? (($a->b->c !== null) ? ($a->b->c->d = 'value') : null) : null) : null);
```

**Test: Chained with method call (temp variable)**
```php
// Input (Tyhp):
$obj?->getChild()?->prop = 'value';

// Expected output (PHP):
(($obj !== null) ? (($__tyhp_nsa_0 = $obj->getChild()) !== null ? ($__tyhp_nsa_0->prop = 'value') : null) : null);
```

**Test: All assignment operators (representative selection)**
```php
// Input (Tyhp):
$obj?->count += 1;
$obj?->name .= ' Smith';
$obj?->flags &= 0xFF;
$obj?->value **= 2;
$obj?->bits <<= 4;

// Expected output (PHP):
(($obj !== null) ? ($obj->count += 1) : null);
(($obj !== null) ? ($obj->name .= ' Smith') : null);
(($obj !== null) ? ($obj->flags &= 0xFF) : null);
(($obj !== null) ? ($obj->value **= 2) : null);
(($obj !== null) ? ($obj->bits <<= 4) : null);
```

#### 5.5 End-to-End Tests

Create a Tyhp file that compiles to PHP and is then executed to verify runtime behavior:

```php
<?tyhp
class Box {
    public string $label = '';
    public int $count = 0;
    public ?Box $inner = null;
}

function testSimpleNullSafe(): void {
    ?Box $box = new Box();
    $box?->label = 'Hello';
    assert($box->label === 'Hello');

    ?Box $nullBox = null;
    $nullBox?->label = 'Should not assign';
    assert($nullBox === null);
}

function testExpressionValue(): void {
    ?Box $box = new Box();
    string|null $result1 = ($box?->label = 'World');
    assert($result1 === 'World');

    ?Box $nullBox = null;
    string|null $result2 = ($nullBox?->label = 'World');
    assert($result2 === null);
}

function testChained(): void {
    Box $outer = new Box();
    $outer->inner = new Box();

    $outer?->inner?->label = 'Nested';
    assert($outer->inner->label === 'Nested');

    $outer->inner = null;
    $outer?->inner?->label = 'Should not assign';
    // inner is still null — assignment was a no-op
    assert($outer->inner === null);
}

function testCompoundOperators(): void {
    ?Box $box = new Box();
    $box->count = 10;

    $box?->count += 5;
    assert($box->count === 15);

    $box?->count -= 3;
    assert($box->count === 12);

    $box?->count *= 2;
    assert($box->count === 24);

    $box?->label = 'Hi';
    $box?->label .= ' World';
    assert($box->label === 'Hi World');
}

function testNullCoalesceAssign(): void {
    ?Box $box = new Box();
    $box->label = '';

    $box?->label ??= 'default';
    // label is '' (not null), so ??= does not overwrite
    assert($box->label === '');
}

function testSideEffectSafety(): void {
    int $callCount = 0;
    function getBox() use (&$callCount): ?Box {
        $callCount++;
        return new Box();
    }

    getBox()?->label = 'test';
    assert($callCount === 1); // getBox() called exactly once
}

testSimpleNullSafe();
testExpressionValue();
testChained();
testCompoundOperators();
testNullCoalesceAssign();
testSideEffectSafety();
```

#### 5.6 Example File

Create `Examples/NullConditionalAssignment.tyhp` and `Examples/NullConditionalAssignment.php` showing idiomatic usage:

**`Examples/NullConditionalAssignment.tyhp`:**
```php
<?tyhp

class Config {
    public string $theme = 'light';
    public int $fontSize = 14;
    public ?Config $override = null;
}

// Simple null-safe assignment
?Config $config = loadConfig();
$config?->theme = 'dark';

// Chained null-safe assignment
$config?->override?->fontSize = 18;

// With null coalesce
$config?->theme ??= 'light';

// Expression context — capture the result
string|null $appliedTheme = ($config?->theme = getUserPreference());

// Side-effect safe
getConfig()?->theme = 'dark';
```

**`Examples/NullConditionalAssignment.php`** (expected output):
```php
<?php

class Config {
    public string $theme = 'light';
    public int $fontSize = 14;
    public ?Config $override = null;
}

$config = loadConfig();
(($config !== null) ? ($config->theme = 'dark') : null);

(($config !== null) ? (($config->override !== null) ? ($config->override->fontSize = 18) : null) : null);

(($config !== null) ? ($config->theme ??= 'light') : null);

$appliedTheme = (($config !== null) ? ($config->theme = getUserPreference()) : null);

(($__tyhp_nsa_0 = getConfig()) !== null ? ($__tyhp_nsa_0->theme = 'dark') : null);
```

### Acceptance Criteria

- [ ] All parser test patterns parse without errors
- [ ] All valid checker test cases pass validation
- [ ] All invalid checker test cases produce the expected errors
- [ ] All emitter snapshot tests match expected PHP output exactly
- [ ] End-to-end tests compile to PHP and execute with all assertions passing
- [ ] Example files (`Examples/NullConditionalAssignment.tyhp` and `.php`) are present and consistent
- [ ] No regressions in existing tests

### Dependencies

- **Requires:** Phases 1–4 (complete pipeline implementation)
- **Provides:** Verified, tested null-conditional assignment feature

---

## New MessageCode Values

Null-conditional assignment reuses existing assignment validation errors (type mismatch, invalid operator, etc.) from the checker. The null-safe wrapper is transparent to error reporting.

The following diagnostic codes are required:

```csharp
#region Checker (continued)

// Null-safe assignment diagnostics (4340-4342) — Story 26
CheckerUnnecessaryNullSafeOperator = 4340,
// "Null-safe operator '?->' used on non-nullable type '{0}'. Consider using '->' instead."
// Severity: Info/Hint

CheckerNullSafeIncrementNotSupported = 4341,
// "Increment/decrement operators ('++', '--') are not supported with the null-safe operator '?->'. Assign explicitly instead."
// Severity: Error

CheckerNullSafeArrayDestructuringNotSupported = 4342,
// "Array destructuring with null-safe property access targets is not supported."
// Severity: Error

#endregion
```

Add the corresponding `.resx` entries:

```xml
<data name="INFO_TYHP4340" xml:space="preserve">
    <value>Null-safe operator '?->' used on non-nullable type '{0}'. Consider using '->' instead.</value>
</data>
<data name="ERROR_TYHP4341" xml:space="preserve">
    <value>Increment/decrement operators ('++', '--') are not supported with the null-safe operator '?->'. Assign explicitly instead.</value>
</data>
<data name="ERROR_TYHP4342" xml:space="preserve">
    <value>Array destructuring with null-safe property access targets is not supported.</value>
</data>
```

---

## Testing Strategy Summary

### Unit Tests

**Parser:**
- All assignment operators with `?->` LHS parse correctly
- Mixed chains (`->` and `?->`) parse correctly
- Expression context (wrapped in parentheses, assigned to variable) parses correctly
- Deeply nested chains (3+ levels) parse correctly

**Checker:**
- Type compatibility validated for all assignment operators
- Expression result type is `T|null` for all cases
- No type narrowing after null-conditional assignment
- Non-nullable object with `?->` accepted (optional hint emitted)
- Invalid assignments produce correct error codes

**Emitter:**
- Simple, chained, and mixed cases emit correct ternary structures
- All 14 standard assignment operators preserved in emitted code (plus the Tyhp-specific `:=`, expanded first)
- Temp variables generated for side-effecting object expressions
- Temp variables NOT generated for simple variable objects
- Proper interaction with struct transformer (array key access inside ternary)

### Snapshot Tests

For each emitter transformation pattern, a Tyhp input and expected PHP output golden file:

| Test Name | Tyhp Pattern | Key Verification |
|-----------|-------------|------------------|
| `simple_nullsafe_assign` | `$obj?->prop = val` | Single ternary wrapper |
| `chained_nullsafe_assign` | `$obj?->a?->b = val` | Nested ternaries |
| `mixed_chain_assign` | `$obj->a?->b = val` | Only null-safe segments wrapped |
| `all_operators` | `$obj?->p += 1` (×14 standard) | Each operator preserved |
| `sideeffect_safety` | `fn()?->p = val` | Temp variable used |
| `chained_sideeffect` | `fn()?->m()?->p = val` | Multiple temp variables |
| `expression_context` | `$r = ($o?->p = v)` | Ternary as RHS of assignment |
| `null_coalesce_assign` | `$o?->p ??= 'def'` | `??=` inside ternary |
| `three_level_chain` | `$a?->b?->c?->d = v` | Three nested ternaries |
| `struct_interaction` | `$s?->prop = val` | Array key access inside ternary |

### End-to-End Tests

- Compile Tyhp files with null-conditional assignment → valid PHP output
- Execute the PHP output → all assertions pass
- Verify no-op behavior when object is null
- Verify assignment behavior when object is non-null
- Verify expression value (`null` or assigned value)
- Verify function call evaluated only once (side-effect safety)

---

## Cross-Phase Dependency Map

```
Phase 1: Grammar
    │
    ▼
Phase 2: Binder
    │
    ├──► Phase 3: Checker
    │        │
    │        ▼
    └──► Phase 4: Emitter
             │
             ▼
         Phase 5: Testing
```

Phases 3 and 4 both depend on Phase 2 but are independent of each other (the checker validates types; the emitter transforms code). However, Phase 4's output must match Phase 3's type expectations (e.g., the emitted ternary returns `null` in the false branch, which the checker expects as part of the `T|null` result type).

---

## Appendix: Complete Tyhp → PHP Emission Examples

### Example 1: Simple Assignment

```php
// Tyhp:
?User $user = getUser();
$user?->name = 'Alice';

// PHP:
$user = getUser();
(($user !== null) ? ($user->name = 'Alice') : null);
```

### Example 2: Chained Assignment

```php
// Tyhp:
?Company $company = getCompany();
$company?->ceo?->name = 'Alice';

// PHP:
$company = getCompany();
(($company !== null) ? (($company->ceo !== null) ? ($company->ceo->name = 'Alice') : null) : null);
```

### Example 3: Side-Effect Safe

```php
// Tyhp:
getUser()?->name = 'Alice';

// PHP:
(($__tyhp_nsa_0 = getUser()) !== null ? ($__tyhp_nsa_0->name = 'Alice') : null);
```

### Example 4: Chained with Method Call

```php
// Tyhp:
$repo?->findUser($id)?->profile?->bio = 'Updated';

// PHP:
(($repo !== null)
    ? (($__tyhp_nsa_0 = $repo->findUser($id)) !== null
        ? (($__tyhp_nsa_0->profile !== null)
            ? ($__tyhp_nsa_0->profile->bio = 'Updated')
            : null)
        : null)
    : null);
```

### Example 5: Expression Value Capture

```php
// Tyhp:
string|null $result = ($user?->name = 'Alice');

// PHP:
$result = (($user !== null) ? ($user->name = 'Alice') : null);
```

### Example 6: Null Coalesce Assignment

```php
// Tyhp:
$config?->theme ??= 'light';

// PHP:
(($config !== null) ? ($config->theme ??= 'light') : null);
```

### Example 7: All Assignment Operators (Representative)

```php
// Tyhp:                           // PHP:
$o?->a = 1;                       // (($o !== null) ? ($o->a = 1) : null);
$o?->a += 1;                      // (($o !== null) ? ($o->a += 1) : null);
$o?->a -= 1;                      // (($o !== null) ? ($o->a -= 1) : null);
$o?->a .= 'x';                    // (($o !== null) ? ($o->a .= 'x') : null);
$o?->a ??= 'd';                   // (($o !== null) ? ($o->a ??= 'd') : null);
$o?->a *= 2;                      // (($o !== null) ? ($o->a *= 2) : null);
$o?->a /= 2;                      // (($o !== null) ? ($o->a /= 2) : null);
$o?->a %= 3;                      // (($o !== null) ? ($o->a %= 3) : null);
$o?->a **= 2;                     // (($o !== null) ? ($o->a **= 2) : null);
$o?->a &= 0xF;                    // (($o !== null) ? ($o->a &= 0xF) : null);
$o?->a |= 0x1;                    // (($o !== null) ? ($o->a |= 0x1) : null);
$o?->a ^= 0xF;                    // (($o !== null) ? ($o->a ^= 0xF) : null);
$o?->a <<= 2;                     // (($o !== null) ? ($o->a <<= 2) : null);
$o?->a >>= 2;                     // (($o !== null) ? ($o->a >>= 2) : null);
```

### Example 8: Three-Level Chain

```php
// Tyhp:
$a?->b?->c?->d = 'deep';

// PHP:
(($a !== null)
    ? (($a->b !== null)
        ? (($a->b->c !== null)
            ? ($a->b->c->d = 'deep')
            : null)
        : null)
    : null);
```

### Example 9: Mixed Regular and Null-Safe

```php
// Tyhp:
$company->department?->manager->email = 'boss@example.com';

// PHP:
(($company->department !== null) ? ($company->department->manager->email = 'boss@example.com') : null);
```

### Example 10: Struct Interaction

```php
// Tyhp:
?MyStruct $s = getStruct();
$s?->name = 'Updated';

// PHP (struct properties emit as array keys):
$s = getStruct();
(($s !== null) ? ($s['name'] = 'Updated') : null);
```

---

*Generated: 2026-02-17*

---

## Human Testing and Verification

> These steps are for a human developer to manually verify the implementation works end-to-end. Steps can be skipped or modified as needed — they are guidelines, not a rigid checklist.

### Step 1: Verify Parser Accepts Null-Safe Assignment Syntax

Create a file `test_nullsafe_assign_parse.tyhp`:

```tyhp
<?tyhp

class Target {
    public string $name = '';
    public int $count = 0;
    public ?Target $child = null;
}

function testParsing(): void {
    ?Target $obj = new Target();

    // Simple null-safe assignment
    $obj?->name = 'hello';

    // Chained null-safe assignment
    $obj?->child?->name = 'nested';

    // Mixed chain
    Target $definite = new Target();
    $definite->child?->name = 'mixed';

    // Compound assignment operators
    $obj?->count += 1;
    $obj?->count -= 1;
    $obj?->name .= ' world';
    $obj?->count *= 2;
    $obj?->count /= 2;
    $obj?->count %= 3;
    $obj?->count **= 2;
    $obj?->count &= 0xFF;
    $obj?->count |= 0x01;
    $obj?->count ^= 0xFF;
    $obj?->count <<= 2;
    $obj?->count >>= 2;

    // Null coalesce assignment
    $obj?->name ??= 'default';

    // Expression context
    string|null $result = ($obj?->name = 'captured');

    // Function call as object (side-effect case)
    getTarget()?->name = 'side-effect-safe';
}

function getTarget(): ?Target {
    return new Target();
}
```

Run the parser (e.g., `tyhp lint test_nullsafe_assign_parse.tyhp`). **Expected:** No parse errors. All patterns are accepted.

### Step 2: Verify Checker Type Validation

Create a file `test_nullsafe_assign_checker.tyhp`:

```tyhp
<?tyhp

class User {
    public string $name = '';
    public int $age = 0;
    public ?User $friend = null;
}

function testCheckerValid(): void {
    ?User $user = null;

    // These should all pass checker validation:
    $user?->name = 'Alice';
    $user?->age += 1;
    $user?->friend?->name = 'Bob';
    $user?->name ??= 'Unknown';

    // Expression type should be string|null:
    string|null $result = ($user?->name = 'test');
}
```

Run `tyhp lint test_nullsafe_assign_checker.tyhp`. **Expected:** No errors. The `$result` variable should be accepted as `string|null`.

### Step 3: Verify Checker Error Cases

Create a file `test_nullsafe_assign_errors.tyhp`:

```tyhp
<?tyhp

class Item {
    public string $label = '';
    public int $count = 0;
}

function testCheckerErrors(): void {
    ?Item $item = null;

    // Type mismatch — should produce an error:
    $item?->label = 42;

    // Wrong operator for type — should produce an error:
    $item?->label += 1;

    // Expression type mismatch — should produce an error:
    string $narrow = ($item?->label = 'test');

    // Increment with ?-> — should produce error 4341:
    $item?->count++;
}
```

Run `tyhp lint test_nullsafe_assign_errors.tyhp`. **Expected:** Errors on the marked lines:
- Line with `$item?->label = 42` — type mismatch (int not assignable to string)
- Line with `$item?->label += 1` — operator incompatible with string
- Line with `string $narrow = ...` — `string|null` not assignable to `string`
- Line with `$item?->count++` — error 4341 (`CheckerNullSafeIncrementNotSupported`)

### Step 4: Verify Emitted PHP Output

Compile `test_nullsafe_assign_parse.tyhp` using `tyhp build`. Open the generated PHP file and verify the transformation patterns:

- `$obj?->name = 'hello'` should emit as:
  ```php
  (($obj !== null) ? ($obj->name = 'hello') : null);
  ```
- `$obj?->child?->name = 'nested'` should emit as:
  ```php
  (($obj !== null) ? (($obj->child !== null) ? ($obj->child->name = 'nested') : null) : null);
  ```
- `getTarget()?->name = 'side-effect-safe'` should use a temp variable:
  ```php
  (($__tyhp_nsa_0 = getTarget()) !== null ? ($__tyhp_nsa_0->name = 'side-effect-safe') : null);
  ```
- `string|null $result = ($obj?->name = 'captured')` should emit as:
  ```php
  $result = (($obj !== null) ? ($obj->name = 'captured') : null);
  ```

### Step 5: Verify Runtime Behavior

Create a file `test_nullsafe_runtime.tyhp`:

```tyhp
<?tyhp

class Box {
    public string $label = '';
    public int $count = 0;
    public ?Box $inner = null;
}

// Test 1: Non-null object — assignment should execute
?Box $box = new Box();
$box?->label = 'Hello';
echo $box->label . "\n";

// Test 2: Null object — assignment should be a no-op
?Box $nullBox = null;
$nullBox?->label = 'Should not assign';
echo ($nullBox === null ? 'still null' : 'ERROR') . "\n";

// Test 3: Expression value
?Box $box2 = new Box();
string|null $val1 = ($box2?->label = 'World');
echo $val1 . "\n";

?Box $nullBox2 = null;
string|null $val2 = ($nullBox2?->label = 'Nope');
echo ($val2 === null ? 'null result' : 'ERROR') . "\n";

// Test 4: Chained
Box $outer = new Box();
$outer->inner = new Box();
$outer?->inner?->label = 'Nested';
echo $outer->inner->label . "\n";

$outer->inner = null;
$outer?->inner?->label = 'Noop';
echo ($outer->inner === null ? 'inner still null' : 'ERROR') . "\n";

// Test 5: Compound operators
?Box $box3 = new Box();
$box3->count = 10;
$box3?->count += 5;
echo $box3->count . "\n";
$box3?->label = 'Hi';
$box3?->label .= ' World';
echo $box3->label . "\n";
```

Compile with `tyhp build`, then run the generated PHP with `php <output>.php`. **Expected output:**

```
Hello
still null
World
null result
Nested
inner still null
15
Hi World
```

### Step 6: Verify Info Diagnostic for Unnecessary `?->`

Create a file `test_nullsafe_unnecessary.tyhp`:

```tyhp
<?tyhp

class Foo {
    public string $bar = '';
}

function test(): void {
    Foo $definite = new Foo();
    $definite?->bar = 'unnecessary nullsafe';
}
```

Run `tyhp lint test_nullsafe_unnecessary.tyhp`. **Expected:** An info-level diagnostic (code 4340) on the `$definite?->bar` line suggesting to use `->` instead of `?->`.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
