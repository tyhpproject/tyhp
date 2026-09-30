# Tyhp — Design Open Questions & Candidate Future Features

> **What this is:** a holding pen for language ideas that are **not committed** to the roadmap yet — either
> because they still have unresolved design questions, or because they are "nice to have someday" candidates.
> Nothing here is a promise. When an idea's questions are answered and it is scheduled, it graduates into a
> numbered story in `ROADMAP.md` / an `IMPLEMENTATION_PLAN_TODO_STORY_NN.md`.
>
> For ideas that were **considered and explicitly rejected**, see `DECISIONS.md`.
> For the committed sequence of work, see `ROADMAP.md`.
> Language-assessment leftovers that still need a product lock are §16’s PHP-boundary questions.
> Syntax trim, tagline, `with`, ctor `: void`, `isa`/`isan`, and the emitter pipeline are decided
> (`DECISIONS.md` / Story 21.12). Async HTTP/DB/FS is Story 31 Idea 17.

---

## PHP builtin typing ceilings (revisit later)

Logged from the Story 21.8 Layer 3 overlay audit (2026-09-02). Tyhpdef cannot express these.
Story 21.8 still ships the **default / union** overlay contract noted below. A later story could
add checker special-cases; none of that is scheduled.

### 1. SplPriorityQueue extract flags

`SplPriorityQueue::setExtractFlags` changes what `current()` / `extract()` / `top()` / `foreach`
yield: `EXTR_DATA` → value, `EXTR_PRIORITY` → priority, `EXTR_BOTH` → `array{priority, data}`.

**21.8 overlay:** default flag is `EXTR_DATA`, so those members are `TValue`. Flag-precise typing
would need the checker to track the last `setExtractFlags` argument on that instance (or a
generic phantom parameter), which tyhpdef cannot do.

### 2. FilesystemIterator / RecursiveDirectoryIterator CURRENT_AS_* / KEY_AS_*

Constructor `$flags` selects `current()` / `key()` as `\SplFileInfo`, `string` (pathname), or
`self` (the iterator), and independently `KEY_AS_PATHNAME` vs `KEY_AS_FILENAME`.

**21.8 overlay:** union of the documented modes (`SeekableIterator<string, \SplFileInfo|string|self>`
and the matching RecursiveDirectoryIterator harvest). Per-flag precision is the same class of
runtime-flag problem as SplPriorityQueue extract flags.

### 3. Format-string typing for `sprintf` / `sscanf` / `pack` / `unpack`

Return and argument types depend on the format string (`%s` vs `%d`, `a*` vs `N`, `sscanf`
out-params). Needs a compiler format parser (and version-aware PHP format tables), not a
tyhpdef overload set. Revisit as its own spike; do not fold into a Layer 3 overlay pass.

### 4. Scope-mutating PHP builtins — manual review list

Leave the harvested signatures. Overlay will not pretend to understand caller scope. Review
each later and decide whether a checker special-case is worth it:

| Function | Why tyhpdef is a ceiling | What a later checker might do |
|---|---|---|
| `compact` | Builds an array from **variable names** in the caller. Return keys/values are the named locals. | If every argument is a string literal, type the result as a struct/shape of those locals. |
| `extract` | Writes **locals** from an array (by-ref import). Flags (`EXTR_OVERWRITE`, …) change which names are created. | If the input is a known shape and flags are literals, bind those names in the current scope. High foot-gun; likely stays untyped. |
| `settype` | Mutates the **variable’s type** by a string (`"int"`, `"array"`, …) passed by reference. | If the type name is a literal, retype the variable after the call. |
| `get_defined_vars` | Snapshot of **all** current locals. | Could be a struct of in-scope symbols; noisy and changes at every assignment. |

Do not schedule these until that manual review happens. Story 21.8 leaves L1/L2 as-is.

---

## Open design questions (feature wanted, design unresolved)

### 5. Refined types — `type` with a body

- UNDECIDED: should it be called "Branded Types" or "Refined Types"?

> **Supersedes two earlier entries.** This merges "opaque (nominal) type aliases" and the "unified
> refinement mechanism" into one feature. They turned out to be two points on a single axis — *how is
> membership in this type determined?* — either by a computable property of the value (`Positive`) or by
> **provenance**, meaning how the value was produced (`Safe`, `UserId`). Same declaration form, different
> content.

A `type` declaration **with a body** is opaque (nominal) and carries conversion rules. A `type` declaration
**without** a body remains a transparent alias exactly as today.

**Having a body is what makes a type opaque.** No `opaque` keyword, no `#[\Tyhp\Opaque]` attribute, no
`newtype` — which dissolves what was previously the main open syntax question, and makes the merged feature
*cheaper* in grammar than either half was separately. The body is shaped like a property hook
(semicolon-separated members, `=>` shorthand or a braced body), so it reads as existing Tyhp syntax.

Everything **erases**: a refined type has the runtime representation of its base and no wrapper, exactly as
generics do.

#### Syntax

```
type <Name> = <base types> {
    <members>
};
```

The body is property-hook shaped: semicolon-separated members, each either `=> <expr>;`, `{ … }`, or (for `guard` only) a bodyless `guard;` / `guard(<base>);`. Placeholders:

- `<base>` — one type from `<base types>`, or a union of a subset of them
- `<target>` — outbound type for `widen`
- `<php-hint>` — a PHP type-hint spelling (`string`, `string|int`, `mixed`, `?\Foo`, …)

**Shared omission rules** (`guard` / `narrow` / `widen`):

- `()` may be dropped when there is no parameter and no return type. `guard => …` and `guard() => …` are the same.
- A return type requires `()`. `guard: bool => …` / `widen: string => …` are invalid.
- When the parameter is omitted, the body sees `$value` typed as the full `<base types>` union (`widen` residual: that union minus bases already covered by explicit arms for the same `<target>`).
- A written parameter may rename `$value` and may restrict the type to `<base>`. No default values.
- Arrow members end with `;`. Brace members do not take a semicolon after `}`.

**`guard`** — required; overloadable. Must cover every base (one full-union guard, or overloads that partition the bases). Return type is always `bool` and may be spelled or inferred. Parameter type, when present, selects which base(s) this overload tests.

A bodyless `guard;` is short for `guard => true;` — every value of the (selected) base may be branded, which is the usual spelling for a pure brand. The same sugar applies with a parameter list: `guard(<base>);` and `guard(<base> $value);` are both `guard(<base> $value) => true`. The `$value` name is optional here because the implied body does not mention it. `guard();` is the same as `guard;`.

```
// bodyless — same as `=> true`
guard;
guard();
guard(<base>);
guard(<base> $value);

// arrow
guard => <bool-expr>;
guard() => <bool-expr>;
guard(): bool => <bool-expr>;
guard(<base> $value) => <bool-expr>;
guard(<base> $value): bool => <bool-expr>;

// brace
guard {
    return <bool-expr>;
}
guard() {
    return <bool-expr>;
}
guard(): bool {
    return <bool-expr>;
}
guard(<base> $value) {
    return <bool-expr>;
}
guard(<base> $value): bool {
    return <bool-expr>;
}
```

**`narrow`** — optional; overloadable. No return type is written (inferred as this refined type; the value must be compatible with the resolved bases). `()` is optional when not targeting a specific `<base>`.

```
// arrow
narrow => <expr>;
narrow() => <expr>;
narrow(<base> $value) => <expr>;

// brace
narrow {
    return <expr>;
}
narrow() {
    return <expr>;
}
narrow(<base> $value) {
    return <expr>;
}
```

**`widen`** — optional; overloadable. The return type is the outbound `<target>`: inferred from the body, or written explicitly. Three parameter shapes (see the widen-overload notes under unresolved questions):

- none — residual fallback for that `<target>`
- `<base> $value` — explicit arm for a held base
- `never $value` — open residual when explicit arms already cover every current base; `: <target>` is required

`=> unset` kills conversion to that exact `<target>` (all-or-nothing). Arrow only — it is not `return unset`.

```
// arrow — residual
widen => <expr>;
widen() => <expr>;
widen(): <target> => <expr>;
widen(): <target> => unset;

// arrow — explicit arm
widen(<base> $value) => <expr>;
widen(<base> $value): <target> => <expr>;
widen(never $value): <target> => <expr>;

// brace — residual
widen {
    return <expr>;
}
widen() {
    return <expr>;
}
widen(): <target> {
    return <expr>;
}

// brace — explicit arm
widen(<base> $value) {
    return <expr>;
}
widen(<base> $value): <target> {
    return <expr>;
}
widen(never $value): <target> {
    return <expr>;
}
```

**`emit`** — at most one; optional. A PHP type-hint spelling for generated hints, not a conversion. No parameter list, no `: <type>` colon, no brace body.

```
emit => <php-hint>;
```

Worked examples of those forms together:

```tyhp
// Pure brands — no predicate is possible; these exist only to be distinct from other strings.
// Auto-widen: identity to the full base union (here just `string`).
type UnsafeHtml            = string { guard; };
type DangerousPreserveHtml = string { guard; };

// Value-determined — the `guard` is the whole definition, and `narrow` is derived from it.
// Auto-widen to `int` keeps the short guard-only form.
type Positive = int { guard => $value > 0; };

// Multiple bases, per-base guards and narrowing, and an explicit widening beyond the auto union.
type Safe = UnsafeHtml|DangerousPreserveHtml {
    guard => !\str_contains($value, '<');          // heuristic, not the definition

    narrow(UnsafeHtml $value)            { return \htmlspecialchars($value, \ENT_QUOTES); }
    narrow(DangerousPreserveHtml $value) { return $value; }

    // Auto: Safe → UnsafeHtml|DangerousPreserveHtml (identity).
    widen(): string => $value;                     // opt in to a proper subset / other target
};

// Overloaded guards and widens across several bases.
// Auto: Numeric → int|float|string. Subset targets below are opt-in.
type Numeric = int|float|string {
    guard(string $value) => \is_numeric($value);
    guard(int|float);

    widen(): string => \strval($value);
    widen(): int    => \intval($value);
    widen(): float  => \floatval($value);
};

// Recovery brand must be opaque — a transparent `type UnsecureSecret = string` would collide
// with `widen(): string` (same target).
type UnsecureSecret = string {
    guard;
    widen(): string => $value;                     // recover later
};

// Redacted at the `string` boundary; recoverable via UnsecureSecret.
// Auto union widen `string|int` would leak the payload to `string|int` parameters, so unset it.
type Secret = string|int {
    guard;
    widen(): string|int => unset;                  // kill auto union widen
    widen(): string => "****";                     // or throw
    widen(): UnsecureSecret => \strval($value);
};

// First-party decimal — library brand in `tyhp/decimal`, not a compiler builtin.
type decimal = \Tyhp\Decimal { guard; };
```

**Why this is worth having** (the motivating cases, in rough order of value):

- **Escaped vs. unescaped strings.** The case most worth having in a PHP-targeted language — it converts a
  class of XSS and injection bug from a code-review problem into a compile error, at zero runtime cost. The
  same shape covers raw vs. quoted SQL identifiers and any other "has this been sanitized yet" distinction.
- **Identifier mix-ups.** `UserId` / `OrderId` / `TenantId` are all `int` and interchange silently today.
  The most common case by far.
- **Units.** `Cents` vs. `Dollars`, `Seconds` vs. `Milliseconds` — all `int`, catastrophic to confuse.
- **"Parse, don't validate."** If the only way to obtain an `Email` is through its `narrow`, then *holding*
  one is proof it was validated.
- **It subsumes range types and regex-constrained strings.** Both were previously written up as their own
  entries in this file, and both were **dropped in favour of this mechanism** — do not re-propose either.
  See the next subsection for what that trade buys and what it costs.
- **First-party `decimal`.** A library pure brand over `\Tyhp\Decimal` is how most of the compiler's
  hardcoded `decimal` type goes away. `T_DECIMAL_CAST` is only a bridge until named casts land. See
  [`decimal` as a library brand](#decimal-as-a-library-brand).

#### Subsuming range types and regex-constrained strings

Both former entries collapse into ordinary library declarations, with no new syntax and no compiler
involvement beyond the refined-type machinery itself:

```
type Email   = string { guard => \preg_match('/^[^@\s]+@[^@\s]+$/', $value) === 1; };
type Percent = int    { guard => $value >= 0 && $value <= 100; };
type UnitF   = float  { guard => $value >= 0.0 && $value <= 1.0; };
```

Writing the constraint as a guard dissolves nearly every hard question those entries raised, because the
author writes the actual check instead of the compiler inferring one from special syntax:

- **Regex.** The pattern is a plain runtime `preg_match` call rather than something the checker must
  understand, so there is no .NET/PCRE dialect split, no anchoring or delimiter question, no
  value-dependent narrowing guard to invent, and no regular-language-inclusion problem — assignability is
  nominal, exactly like every other refined type.
- **Ranges.** No const-generics system is needed, because there are no generic parameters at all.
  Inclusive vs. exclusive is whatever the author types (`>=` or `>`). Open-ended is just a one-sided
  comparison. And the "what would `string<"a", "z">` even mean" question disappears, because the author
  spells out the comparison they meant instead of the compiler guessing.

**One thing does not carry over, and was accepted as a loss:**

- **Bound propagation through arithmetic.** `int<0, 100> + int<0, 100>` inferring `int<0, 200>` has no
  equivalent here — `Percent + Percent` is typed as `int` (operators see the base union). This is
  dependent-typing territory and is a deliberate **non-goal**; the range entry already listed
  widening-back as an acceptable answer.

**`decimal` ranges are expressible.** `decimal` itself is a library refined type (see the next subsection),
so `type UnitD = decimal { guard => …; }` is a refinement of a refinement — already allowed, no compiler
scalar. Do **not** write `type UnitD = \Tyhp\Decimal { … }` when the author means the decimal type.
`(?decimal)` tracks named refined-type casts, not `(?int)`: until `(Name)` casts exist, `(decimal)` is a
lexer token and `(?decimal)` is rejected like `(?bool)`; once named casts land, `(?decimal)$x` is the
same as `(?Safe)$x` (yields null) and the token goes away.

#### `decimal` as a library brand

**Intent:** stop treating `decimal` as a compiler builtin type. The runtime class `\Tyhp\Decimal` already
owns arithmetic, comparison, construction, JSON, and `operator convert`. A pure brand in `tyhp/decimal`
is the author-facing type; erasure is the class. Most of today's special-casing (builtin symbol, hint
spelling `decimal` → `\Tyhp\Decimal`, AliasConverter name unification, tyhpdef builtin spelling,
`typeof`/`default` via `T_DECIMAL_CAST`, `IsDecimalType` arithmetic promotion) exists only because the
name is a hardcoded scalar. Delete that. Keep the pieces PHP or the toolchain still cannot infer.

```tyhp
namespace Tyhp;

type decimal = Decimal { guard; };
```

No custom `narrow` / `widen`, no `emit` (erasure is already `\Tyhp\Decimal`), and **not** a union with
`int|float|string`. Putting coercible scalars on the base would make operators see that union and pick
PHP `+` instead of Decimal overloads.

**Composition at the boundary (decided).** One-hop forbids chaining *refined* types (`string → UnsafeHtml
→ Safe`). It does **not** forbid composing class `operator convert` with an identity brand over that
class at a **single** inbound or outbound site:

- Inbound `decimal $x = 1` is `int → Decimal` (existing convert) then identity-narrow into `decimal`.
- Outbound `int $n = $d` is identity-widen to `Decimal` then class convert to `int`.

The brand must stay a pure brand (`guard;` only). Overlapping `narrow` / `widen` on the brand would
collide with class convert; `decimal` does not need that, and it stays out of scope here. General
object bases that *do* declare their own convert-like members remain an unresolved question below.

**Mixed arithmetic** belongs on the class, not in the checker. `$d + 1` already matches
`operator +(self, DecimalCoercible)`. `1 + $d` does not (overloads are left-`self`). Add the symmetric
library overloads (`operator +(DecimalCoercible $left, self $right)`, and the same for the other ops)
and delete `TypeInferrer.Operators.IsDecimalType`. Keep that checker special-case only if the overloads
prove insufficient.

**Bare name `decimal`.** A type in `namespace Tyhp` is `\Tyhp\decimal` unless the compiler still injects
it into global scope the way `int` is. Keep a **prelude**: when `tyhp/decimal` is loaded, `decimal` is
visible globally so `decimal $x` does not require `use`. That is a small remaining hardcode, not a
builtin type.

**What the brand / class / compiler each own**

| Owner | Responsibility |
|---|---|
| Library brand | Nominal type `decimal`; identity narrow/widen to `Decimal`; `type Money = decimal { … }` |
| `\Tyhp\Decimal` | Operators, converts, factory `\Tyhp\decimal()`, backends, JSON, `ZERO` |
| Compiler (keep) | Global prelude so bare `decimal` works; `build:decimalBacking` / scale / rounding; Composer auto-require when output mentions `\Tyhp\Decimal` or `\decimal(`; xdebug unwrap of `Decimal.$value` |
| Compiler (until named casts) | `T_DECIMAL_CAST`; infer `(decimal)$x` as the brand; lower to `\Tyhp\decimal($x)`; `DecimalConvertible` row in the lexer-cast → contract table. Drop all four when `(Name)` casts exist — `(decimal)$x` / `(?decimal)$x` are then the same four spellings as `Safe` |
| Compiler (delete) | `BuiltInTypeSymbol("Decimal")`; `TypeSpellingHelper` rewrite; AliasConverter `decimal` ≡ `\Tyhp\Decimal`; tyhpdef builtin spelling of `decimal`; `typeof`/`default` accepting `T_DECIMAL_CAST` as a type name (`decimal` is a normal identifier); `IsDecimalType` once left-coercible overloads exist |

`(decimal)` does **not** have to stay a lexer token. It is only there because PHP has no such cast and Tyhp cannot yet parse `(Safe)$x`. Once named casts land, drop `T_DECIMAL_CAST`: `(decimal)$x` is a cast to the library brand, emit still lowers to `\Tyhp\decimal($x)` (or convert-then-identity-narrow) because PHP still has no `(decimal)`, and `(?decimal)$x` starts working like `(?Safe)$x`. The `DecimalConvertible` *interface* stays on the class; it no longer needs a token-keyed compiler table. Until that grammar ships, keep the token and bind it to the brand, not to a `BuiltInTypeSymbol`. See [Grammar impact](#grammar-impact--named-casts) for the bind-time rule. Named casts are an implementation prerequisite for dropping the token, not a forever decimal special case.

Class methods are part of the decimal API (`$d->add(...)`) because the payload *is* `\Tyhp\Decimal`. That
is representation leak as a feature; it does not decide the scalar-pseudo-object question for `Email`.

#### The members

`guard`, `narrow`, and `widen` are overloadable, statically dispatched, and do not exist at runtime as members.
`emit` is optional, not overloadable, and exists only for generated PHP type hints.

| Member | Direction | Dispatches on | Default when omitted | May fail |
|---|---|---|---|---|
| `guard` | narrowing test | operand's static type | **none — always required** | no |
| `narrow` | in: base → this | operand's static type | `guard ? $value : throw` | yes — throws |
| `widen` | out: this → target | target type at the use site; optional stored-base arms | identity to the **full union of direct bases** only | no |
| `emit` | PHP type hint of this type | n/a | erased **base** (same as today) | no |

`guard` must cover **all** base types; with a union base, either one guard handles the whole union or
overloads cover it exhaustively.

`emit => mixed` (or `string`, `string|int`, `?\Foo`, …) is a **PHP type-hint spelling**, same idea as
`#[\Tyhp\PhpType]` (Story 21.6), but on the refined type so every parameter/return/property of that
type emits that hint. The checker still uses the refined type. Example:

```tyhp
type Email = string { guard => \preg_match('/^[^@\s]+@[^@\s]+$/', $value) === 1; emit => string; };
type Token = string { guard; emit => mixed; };
```

Omitted `emit` keeps current erasure (Email parameters would emit as `string` if the base is `string`).
`#[\Tyhp\PhpType]` stays the per-declaration override until this ships. If both exist, the declaration
attribute wins (candidate; confirm when scheduling).

#### Semantics (decided)

- **`guard` is a narrowing test, not an invariant.** It answers "may I brand *this base value* as this
  type?" It does **not** promise that every value of the type satisfies it — which is exactly what lets
  `Safe`'s second `narrow` overload deliberately produce a `Safe` containing `<`. Consequences: nothing may
  optimize on the assumption that a guard holds, and the three forms read as `true` (`guard;` / `guard => true`)
  = any base value may be branded (a pure brand), `false` = no base value may be branded and `narrow` is the
  only way in, and a real predicate = narrowable when it passes.
- **`is` has three cases.** Operand already the type (or narrower) → folds to `true`, the guard never runs.
  Operand is a base → evaluate the guard. Operand unrelated → **compile error**. Without the first and
  third rules the design contradicts itself: `$safe is Safe` would return `false`, and `$userId is OrderId`
  would return `true` for two types the checker considers unrelated.
- **One hop only, in both directions. No path finding among refined types.** `string → Safe` is two hops
  and rejected; `(DangerousPreserveHtml)"…"` followed by an implicit hop to `Safe` is two *single* hops
  and accepted. This is the entire safety mechanism, and it is why no `allowForce` flag is needed — the
  escape hatch is a marker type with its own `narrow` overload, which is greppable and opt-in per call
  site rather than a blanket per-type boolean. **Object-base pure brands are different:** composing that
  class's `operator convert` with an identity brand at one inbound or outbound site is allowed (see
  [`decimal` as a library brand](#decimal-as-a-library-brand)). That is not path finding through a second
  refined type.
- **Auto-widen is identity to the full union of direct bases only.** `Positive` (base `int`)
  auto-widens to `int`; `Safe` auto-widens to `UnsafeHtml|DangerousPreserveHtml`; `Numeric`
  auto-widens to `int|float|string`; `decimal` auto-widens to `\Tyhp\Decimal`. That default is always
  total (identity on the erased payload) and is what keeps the short guard-only form. Any **proper
  subset** or other target (`string`, `int`, `UnsecureSecret`, …) is **opt-in** via an explicit
  `widen(): T`. Authors may override the auto union target with `widen(): BaseUnion => unset` (or a
  throwing / redacting body) — needed for types like `Secret`, where leaving `string|int` open would
  bypass a redacting `widen(): string`. `unset` is **all-or-nothing for that exact return type** and
  must be a **compile** error at the use site, not a runtime throw. `unset` is already a lexer token
  (`T_UNSET`), so this needs no new keyword. There is no per-arm automatic unwrap to each direct base
  individually — that was unsound for multi-erasure unions (holding `int` does not make the value
  usable as `string`). Outbound `decimal → int` is not auto-widen; it is identity-widen to `Decimal`
  then class convert (one site).
- **Dispatch is static; overload overlap is a compile error.** Overlap is judged on **static
  assignability** between parameter types, not erased representation — `narrow(UnsafeHtml)` and
  `narrow(DangerousPreserveHtml)` coexist happily, while `narrow(UnsafeHtml)` and `narrow(string)` collide.
  The same rule applies to explicit `widen(Base $value): T` arms (see unresolved widen overload notes).
- **Erased brands are not runtime-discriminable.** Two brands over the same base are indistinguishable in
  emitted PHP, so nothing may require testing *which* brand a value carries. This is why `narrow` is
  overloaded and statically dispatched rather than taking a union and branching on `is` internally — that
  form silently always takes the first branch. It follows that invoking a conversion whose operand type is
  a union selecting between two non-erased-distinguishable overloads must be an error. Discriminating on
  the *erased* types is fine (`guard (int|float)` vs. `guard (string)` works, because PHP can tell those
  apart).
- **Conversion failure throws; it does not return null.** This matches PHP, where casts never yield null
  and the one failing cast — `(string)` on an object without `__toString` — throws `\Error`.
- **Four conversion spellings, two behaviours.** `(Safe)$x` and `Safe::from($x)` throw;
  `(?Safe)$x` and `Safe::tryFrom($x)` yield null. PHP itself ships both a cast and a function form for
  every scalar conversion (`(int)` / `intval()`, `(string)` / `strval()`), so offering both is idiomatic
  rather than redundant. `from` / `tryFrom` carry the meaning PHP developers already know from backed
  enums. The same four spellings apply to `decimal` once named casts exist; until then only
  `T_DECIMAL_CAST` plus `\Tyhp\decimal()` / `decimal::from` (the last still a pseudo-call). Named
  `(Name)` casts: see [Grammar impact](#grammar-impact--named-casts) (bind-time choice; use-site error
  only when a type and a const share the name on the `+/-` shape).
- **`from` / `tryFrom` are rewritten pseudo-calls, not real methods.** They are compile-time syntax wearing
  method clothing, so they cannot be used as callables or reached by reflection. The checker must reject
  both.
- **Base types are scalars, arrays, other refined types, and (for a pure brand) an object class.**
  Scalars are `int` / `float` / `string` / `bool` (and `decimal` only as the library brand, not as a
  compiler builtin). The first-party object-base case is `type decimal = \Tyhp\Decimal { guard; }`:
  operators see the class, converts stay on the class, the brand is identity. Custom `narrow` / `widen`
  on an object-base brand that would overlap `operator convert` is still unresolved (below). Do not
  paper over inbound convert by adding `int|float|string` to an object brand's base list.
- **Conjunction down a chain.** A refinement of a refinement conjoins guards. With a union base the
  effective guard is the disjunction across bases, but because narrowing is only legal from a base type,
  the operand's static type always selects one branch and **the disjunction is never emitted**.
- **Rename from `condition` to `guard`.** More accurate given that it is a narrowing test rather than an
  invariant, and it matches the project's existing vocabulary for exactly this concept
  (`docs/content/tyhp_0200_typeNarrowingAndGuards.md`, `AIDevGuide/guide/16-type-guards.md`,
  `TypeNarrowingRule.BuiltInTypeGuards`, and the tyhpdef return-type guard form
  `function is_string(mixed $value): $value instanceof string;`).

#### Mutation and write-back (decided)

A refined type describes a **value**, but a variable is a mutable box. The narrowing holds at the
assignment, and every later mutation is a fresh chance to break it:

```tyhp
Positive $v = 1;
$v -= 5;            // must not silently leave -4 sitting in a Positive
```

**Operators and mutations see the base union — not `widen`.** Because refined types erase, the payload
already *is* the base representation. Eligibility for an operator / compound assignment / `++` / `--`
is checked as if the operands had the **full union of direct bases**. The **result type** of the
operation is likewise whatever that operator yields on the base union. `widen` is a boundary mechanism
(arguments, assignments, casts to a *specific* target); it is **not** consulted to unwrap for
computation. To force a particular widen path before operating, the author casts explicitly
(e.g. `(string)$secret . $suffix` runs the redacting `widen(): string`).

**Every write back into refined-typed storage is a re-narrowing point.** Compound assignment, `++` /
`--`, property assignment, array element assignment and append, and destructuring all desugar to a
write whose right-hand side must **narrow** back into the declared type. Where that narrowing is fallible
the write is a **compile error** and the author spells the conversion out:

```tyhp
$v = (Positive)($v - 5);                // throws if the result is not positive
?Positive $r = (?Positive)($v - 5);     // null if the result is not positive
```

Casting an *operand* is not a substitute for write-back: `$v -= (Positive)5;` fails for the same reason —
the failure is on storing into `Positive`, not on reading. A no-op cast on a value already at the base
also emits nothing useful when the guard folds and types erase.

**Refined types do not ride through operators.** `$cents1 + $cents2` is typed as `int` (base), not
`Cents`. Re-entering `Cents` is a narrow at the assignment / write-back site (identity and infallible for
a pure brand; fallible for `Positive`). Sibling brands meeting at the shared base without an explicit
cast still launder units if assigned loosely — `Cents $c = $cents + $dollars` is a narrow from `int`,
which a pure brand accepts; catching that remains a lint / discipline problem unless the author keeps
units behind opt-in subset widens and avoids treating the base union as interchangeable at call sites.
Prefer APIs that take `Cents` / `Dollars` nominally.

| Expression | Result type | Write-back to refined storage |
|---|---|---|
| `Cents + Cents`, `Cents + 5` | `int` | OK into `Cents` — pure brand, identity narrow |
| `Positive - Positive` | `int` | **error** into `Positive` — narrow fallible |
| `$safe .= $userInput` | `string` (op on base) | **error** into `Safe` — narrow fallible |
| `(string)$secret . $x` | `string` | uses redacting widen first, then concat |

`Positive - Positive` remains the worked counterexample against “same refined type propagates”: the
operation is legal on the base (`int`), but storing the result back into `Positive` is not automatically
sound.

**By-ref binding requires exact type identity.** A by-ref argument is a read *and* a write, and the write
happens inside a callee that knows only the parameter type — possibly hand-written PHP — so there is no
site at which to re-narrow. Neither widening nor narrowing applies at a by-ref argument position:

```tyhp
function reset(int &$x): void { $x = -4; }

Positive $v = 1;
reset($v);          // error: by-ref needs Positive, not an int widen
```

A `Positive` variable binds to `Positive &$x` and to nothing else. The same rule covers
`foreach ($a as &$item)` and PHP builtins with out-parameters such as `preg_match($p, $s, $matches)`.

**A total `narrow` is the opt-in normalizer.** Where a type has a sensible correction for out-of-range
values, writing a `narrow` that never throws makes write-back infallible, which makes mutation both legal
and self-normalizing:

```tyhp
type Percent = int {
    guard  => $value >= 0 && $value <= 100;
    narrow (int $value) { return \max(0, \min(100, $value)); }   // never throws
};

Percent $p = 90;
$p += 50;           // op as int -> 140 -> narrow clamps -> 100
```

`Positive` has no sensible total narrowing, so it does not get one and stays strict. The author selects the
behaviour by how they write `narrow`, and no separate mechanism is needed.

**Considered and rejected — a `conform` member.** A normalizer implicitly invoked after every mutation,
returning a corrected value. It contradicts the decided semantics directly: `guard` is a narrowing test
and deliberately **not** an invariant, so there is nothing for a mutation to break and nothing to restore.
The incoherence shows up immediately in the obvious example — a `conform` on `Positive` returning `0`
produces a value failing `$value > 0`, so its output would itself need checking, and then the failure of
*that* check needs an answer. It also silently substitutes a wrong-but-tidy number for a wrong-and-visible
one, and it adds runtime cost to a feature whose selling point is erasing completely. Every use case it
served is covered by a total `narrow`, written explicitly by the author.

**Considered and rejected — mutation de-narrows the variable.** Letting `$v -= 5;` succeed while
flow-typing `$v` as `int` from that point, requiring an `is` check to get back to `Positive`. Tempting,
because expressions already yield base-union results and Tyhp has flow-sensitive narrowing to reuse.
Two problems: flow typing is only sound for locals, so properties and array elements would need the strict
write-back rule anyway and the language would ship two rules for one concept; and it loses the case the
feature exists for:

```tyhp
Safe $s = getSafeHtml();
$s .= $userInput;   // must not succeed and leave $s typed in a way echo can XSS
echo $s;
```

The mistake is at the write-back, so the error belongs at the write-back (fallible narrow into `Safe`).

**Considered and rejected — auto identity widen to each direct base separately.** Unsound for
multi-erasure unions: a value that might be `int` must not type-check where `string` is required just
because `string` is one of the bases. Replaced by auto-widen to the **full base union** only.

**Deferred ergonomics — per-operator closure analysis.** The strict write-back rule stings only for
numeric predicate types, where `$v++` on a `Positive` is rejected despite being obviously safe. For
guards built from comparisons against constants the checker could settle this by interval reasoning:
`Positive` is `[1, ∞)`, so `++` lands in `[2, ∞)` which is inside and provably safe, while `- 5` lands in
`[-4, ∞)` which is not. This is much smaller than the bound propagation ruled out above, because it
answers a yes/no safety question and never has to name an intermediate type such as `int<0, 200>`. It is
also aimed exactly at the guards that hurt, since numeric ranges are both the ones authors mutate and the
ones that are analyzable. It must be per **operator** rather than per type — `Positive + Positive` is
closed and `Positive - Positive` is not.

**The guarantee is Tyhp-land only.** Guards exist at narrowing sites and nowhere else, so hand-written PHP
can store `-4` in something Tyhp types as `Positive`. This has the same standing as `internal` and
`sealed`, and is inherent to erasure rather than specific to mutation.

#### Emission

Inline `guard` and `narrow` bodies where they are short enough; hoist larger bodies into generated static
helper classes. Those helpers exist only to support the emitted PHP, so they may use mangled names in a
dedicated namespace and are **not** part of the PHP-facing surface. They must be reserved names the checker
forbids user code from declaring (case-insensitively, since PHP class and method names are), should be
`internal` once Story 25 lands, and are ideal candidates for Story 31's lowering and relocation since
nothing references them by name.

A force-style conversion that skips `narrow` emits **nothing at all** — the runtime representation is
identical, so only `guard`, `narrow`, and non-trivial `widen` ever produce code.

#### Grammar impact — named casts

Casts are currently **lexer tokens**, not parser constructs: `T_INT_CAST` matches `'(' 'int' ')'` as a
single atom against a closed list, and Tyhp added `T_DECIMAL_CAST` the same way. `(Safe)$x` cannot be
made to work by adding a token — cast recognition has to move into the **parser**. That revives the C
"typedef name" ambiguity, because `'(' Name ')'` is already a parenthesized value in PHP:

```tyhp
(FOO) - $x      // cast of -$x to FOO, or constant FOO minus $x?
```

Unresolvable at parse time: Tyhp parses the whole file before it binds. `(int)` / `(float)` / `(string)` /
`(bool)` stay lexer tokens (they are PHP casts). `T_DECIMAL_CAST` is a **bridge** until named casts ship;
then drop it — `(decimal)$x` is `(Name)` with `Name = decimal`.

**Decided: parse a cast candidate, bind chooses, error only when both readings exist.** Do not take the
lookahead rule “`(FOO) $x` casts and `(FOO) - $x` is always subtraction” — that makes `(Safe)-$x` unlike
`(int)-$x` and is a rule that needs explaining forever. Do not forbid a namespace const and a type from
sharing a name: PHP has three symbol tables (class-like, function, constant) and Tyhp already matches
that. Source type aliases already collide with functions (they emit as factories); they do **not** collide
with constants. A declaration-site ban would need a tyhpdef/PHP exception. Skip it.

**Syntactic filter** — `(Name)` / `(?Name)` / `(Name<…>)` is a *cast candidate* only when the next token
would be juxtaposition in PHP (illegal unless this is a cast): `$`, literals, `!`, `~`, `@`, and unary
`+` / `-`. Never a candidate before postfix `[`, `(`, `->`, `::`, `++`, `--` — those stay subscript, call,
and member access. `(?Safe)$x` and `(Safe<T>)$x` are type spellings, not values, so they are always casts.

**Binder** on a cast candidate:

| `Name` resolves to | `(Name)$x` / `!$x` / literals | `(Name)-$x` / `(Name) - $x` / `(Name)+$x` |
|---|---|---|
| Type only | Cast | Cast of unary `+/-` (same as `(int)-$x`) |
| Const only | Error (not a type) | PHP reading: binary `+/-` |
| Neither | Error | Error |
| Type **and** const | Cast (`$x` cannot follow a parenthesized const) | **Compile error** — do not guess |

The error is **use-site only**, and only on the `+` / `-` shape. `class Foo` + `const Foo` (and
case-insensitive `class Foo` + `const FOO`) may coexist, including in tyhpdefs. `(Foo)$x` is still a
cast. `Foo - $x` is still subtraction. Only `(Foo)-$x` is rejected.

Do **not** tell authors to write `(Foo)(-$x)` to force a cast. That is PHP’s call spelling
(`(callable)($arg)`), and source aliases occupy the function namespace, so it is a second ambiguity.
The diagnostic points at spellings that are already unambiguous:

```tyhp
Foo - $x              // subtraction: drop the parens around the name
Foo::from(-$x)        // cast of a negated value
$n = -$x; (Foo)$n     // same, if they want the cast token
```

**Unresolved questions (must be answered before this can be scheduled):**
- **How does the checker know an author-written `narrow` can fail?** The write-back rule is that only
  infallible narrowings may be implicit on mutation / assignment into refined storage, but with
  throw-not-null semantics the return type no longer signals fallibility. The derived default is
  decidable (`guard;` / `guard => true` cannot fail; a real guard can), but a hand-written body needs either
  exception-effect tracking (see the `throws` entry above) or an explicit marker. **This is the
  highest-priority question in this entry.** Infallibility is what gates write-back after base-union
  operations, so whether `$p += 50;` compiles depends on the answer — it is a prerequisite rather than
  a refinement.
- **What is the opacity boundary?** ML and Haskell make a newtype transparent inside its defining module
  and opaque outside, which is what lets the module construct values at all. Tyhp's natural equivalent is
  `internal` (Story 25) — transparent within the declaring file/package, opaque beyond it. Confirm that
  coupling, or define an independent boundary.
- **Do scalar pseudo-objects apply?** Does `Email` get `->trim()` from its underlying `string`? Convenient,
  but it leaks the representation and partially defeats opacity. Separate from `decimal`: that brand
  erases to a real class, so `$d->add(...)` is ordinary method call on `\Tyhp\Decimal` and is intended.
- **Can extensions target a refined type?** `extension function domain(extends Email $this): string` would
  be a strong pairing, giving a brand real behaviour. Confirm the extension machinery can target an erased
  type rather than a real class.
- **Guard purity.** A guard must be side-effect-free and deterministic or the type means nothing, and
  inlining it would duplicate any side effect. Enforced how — reusing the `#[\Tyhp\Optimize\Pure]`
  analysis? What exactly is rejected?
- **Compile-time folding on literals.** `Positive $x = 5;` should be checkable at compile time by folding
  the guard. Which guard forms are foldable, and what happens when one is not?
- **May object types be bases?** **Partial lock:** a **pure brand** (`guard;` only) over a class is
  allowed, even when that class defines `operator convert`. That is the `decimal` case: converts stay on
  `\Tyhp\Decimal` and compose with identity narrow/widen at one site. Still open for everyone else:
  1. Object bases with **custom** `narrow` / `widen` that overlap class convert — error, or brand wins
     only at refined-type sites? (The old "allow objects only if they define **no** convert" candidate
     was the wrong split for `decimal`; it may still be the right default for user classes.)
  2. When other resolved bases exist beside that object, every outbound target that is not the auto
     full-union identity must be accounted for (explicit `widen`, residual, `*Convertible` /
     `\Stringable` fallback, or whole-target `unset`). `\Stringable` and `*Convertible` sit **lowest**
     on the fallback chain: they may satisfy a required arm by default, but an author-written
     `widen(MyObject $value): int` on the refined type overrides them **only** at refined-type widen
     sites.
- **Can `widen` overload on the stored base as well as the target?** Decided direction (candidate locked
  in for scheduling — still listed here until the story is cut):
  1. **`widen(): T`** — residual fallback. `$value` is typed as the full base union **minus** bases
     covered by explicit arms for `T`. Body must return `T`. If that residual type is empty (`never`),
     this form is a **declaration error** (cannot type-check `$value`).
  2. **`widen(Base $value): T`** — explicit arm, selected when the held erased type matches `Base`.
     Parameter must be a resolved base (or union of bases); a non-base is an error. Explicit arms must
     not overlap (same static-assignability rule as `narrow`). Same-erasure brands cannot be split.
     Mixing with `widen(): T` is allowed: explicit arms win; residual covers the rest; residual
     `$value` self-narrows accordingly.
  3. **`widen(never $value): T`** — open residual for future bases. Allowed only when the residual after
     explicit arms is empty. Body must not read `$value` (constant / throw). Using `widen(): T` in that
     situation remains an error; the `never` spelling is the explicit open-world opt-in.
  4. **`unset`** is all-or-nothing per return type (no partial unset across arms). Primary remaining use:
     `widen(): BaseUnion => unset` to kill the auto union widen (see `Secret`).
  5. **Resolution order** for held `H` → target `T`: explicit arm > residual `widen(): T` > identity
     auto (only when `T` is exactly the full base union and not unset) > `*Convertible` /
     `\Stringable` auto-call > missing (error). `*Convertible` counts toward exhaustiveness only when
     neither an explicit arm for that held type nor a residual `widen(): T` would take it.
- **Interaction with generics.** Is `array<Safe>` related to `array<string>` at all? Presumably invariant,
  but confirm — and decide whether a refined type may be a generic constraint.
- **`emit` vs `#[\Tyhp\PhpType]`.** Declaration-site `PhpType` (21.6) is v1. Type-body `emit` is the
  refined-type version. Confirm declaration `PhpType` overrides `emit`, and whether `emit` must be a
  legal PHP hint for `output.phpVersion` (same validation as `PhpType`).
- **Precedence across all conversion mechanisms.** This is the third time the "two conversion mechanisms
  both apply" question has arisen (Story 31 Idea 2 already flagged it between the `*Convertible` contracts
  and `operator convert`, and deferred it). For a **pure brand** over a class (`decimal`), the order at
  a boundary site is: identity brand stamp/unstamp, then the class's `operator convert` / `*Convertible`
  (one site, not two refined hops). Non-object bases never see this. Custom `narrow` / `widen` on an
  object-base brand that would overlap convert is still the open collision (`explicit widen` >
  `residual widen(): T` > auto union identity > `*Convertible` / `\Stringable`, genuine ties as errors).

### 6.Declaration-site generic variance (`in` / `out`)

Let a generic type parameter declare how its type argument may vary, so that `Producer<Dog>` can be used
where `Producer<Animal>` is expected:

```tyhp
interface Producer<out T> { function produce(): T; }            // covariant   — T only comes out
interface Consumer<in T>  { function consume(T $value): void; }  // contravariant — T only goes in
interface Transform<in TIn, out TOut> { function apply(TIn $x): TOut; }
```

```tyhp
Producer<Animal> $p = getDogProducer();   // covariance:     Producer<Dog>    → Producer<Animal>
Consumer<Dog>    $c = getAnimalConsumer(); // contravariance: Consumer<Animal> → Consumer<Dog>
```

**Why this is unusually cheap for Tyhp:** generics are erased, so variance changes **nothing** in the
emitted PHP — it is purely a checker rule. More to the point, **most of the plumbing already exists and is
currently dead**:

- `Tyhp/TyhpLang/Enum/TypeVariance.cs` defines `Invariant` / `Covariant` / `Contravariant`.
- `GenericTypeParameterSymbol.Variance` holds it.
- `TypeComparer.Subtyping.cs` **already reads** it when comparing type arguments and branches correctly
  (covariant → compare in order, contravariant → compare reversed, invariant → require equality).
- But **nothing in the compiler ever assigns `Variance`**, so it is always `Invariant`. There is no syntax
  to set it.

So the missing pieces are the surface syntax, the binder wiring, and — the actual work — the
variance-safety check.

**Unresolved questions (must be answered before this can be scheduled):**

- **Variance safety checking — the real cost.** Declared variance is unsound without a position check:
  `out T` may only appear in *output* positions (return types, readonly property types) and `in T` only in
  *input* positions (parameter types). Enforcing that needs a new pass over every member signature of a
  variant generic, plus diagnostics. Do we implement full safety checking, or trust the author (cheap, but
  unsound and un-Tyhp-like)?
- **Constructors and `readonly` are the classic exception.** A constructor takes `T` as a parameter (input
  position) even on a covariant `out T`, and `readonly T $x` is input-at-construction but output
  thereafter. C# exempts constructors from the check. Confirm the same rule, and decide how `readonly`
  properties are classified.
- **Relationship to the existing hardcoded array covariance.** `TypeComparer.Subtyping.cs` has an
  `arrayLikeCovariant` special case that forces covariance for array-like types regardless of declared
  variance. Does declared variance subsume that special case, or do they coexist (and which wins)?
- **Declaration-site only, or use-site too?** C# is declaration-site only; Kotlin has both (`out T` on the
  declaration plus `Box<out Animal>` at the use site); Java is use-site only (wildcards). Declaration-site
  alone is the smaller feature and probably the right first cut — confirm we are not leaving a gap.
- **Interaction with `extends` constraints and generic defaults (Story 28).** Does a variance annotation
  combine freely with `T extends Foo` and `T = Foo`, and in what declaration order (`out T extends Foo = Bar`)?
- **Does it apply to structs?** Struct compatibility is already *structural*, not nominal, so variance may
  be meaningless there. Confirm it is a class/interface/trait-only annotation.
- **Docblock emission.** Maps cleanly to `@template-covariant` / `@template-contravariant`. Story 31
  Idea 4 already anticipates this ("variance where Tyhp models it") — this is what would make that real.
- **Method-level generics.** Do variance annotations make sense on a generic *method*'s own type
  parameters, or only on type parameters of a declaration that can appear as a type (class/interface/trait)?
  Most likely the latter — reject them on methods.

### 7. Exception effect tracking (`throws`)

Track which exceptions a function can throw, so the checker can tell a caller what it is not handling.

```tyhp
function parse(string $s): int throws ParseException { … }

function caller(): void {
    parse('12');            // diagnostic: ParseException is neither caught nor declared
    try { parse('12'); } catch (ParseException $e) { … }   // fine
}
```

**Why this is worth doing here specifically:**

- **Story 31 Idea 4 is explicitly blocked on it.** Its `@throws` emission is deferred with "only if/when
  the checker tracks thrown types — otherwise deferred." This is that prerequisite.
- **The tyhpdef docs already claim it works.** `docs/content/tyhpdef_runTimeErrorsAndExceptions.md` states:
  "Use the `@throws` doc comment annotation … **The compiler uses this information to validate that callers
  handle the declared exceptions.**" Nothing in `Tyhp/TyhpLang/` reads `@throws` — the claim is
  aspirational. That doc needs correcting whether or not this feature is ever built.
- Those existing tyhpdef `@throws` annotations are, however, a ready-made **seed dataset** for the
  interop boundary — the hardest part of any effect analysis is knowing what third-party PHP throws, and
  the tyhpdef convention for recording it already exists and is already documented.

**Unresolved questions (must be answered before this can be scheduled):**

- **Enforced or advisory?** Java's checked exceptions are the canonical cautionary tale — they push authors
  into `catch (Exception $e) {}` and `throws Exception` noise. The likely-right answer for Tyhp is
  **warning-level with inference**, not a hard error requiring annotation. Decide explicitly, because it
  determines whether the feature is loved or hated.
- **Inferred or declared?** Can the checker infer a function's throws set bottom-up from its call graph
  (making annotations optional documentation), or must the author declare it? Inference is far more
  ergonomic but requires whole-program analysis and degrades at every dynamic call.
- **What is the unchecked baseline?** Without an exemption list, every function transitively "throws
  everything" and the feature is useless noise. Java exempts `RuntimeException` and `Error`. What is
  Tyhp's exempt set — `\Error`? `\RuntimeException`? Author-configurable?
- **The interop boundary.** Any call into untyped PHP, or into a tyhpdef function without `@throws`, has an
  unknown throws set. Is unannotated treated as "throws nothing" (unsound, quiet) or "throws anything"
  (sound, useless)? This single choice probably decides the feature's viability.
- **Higher-order code.** Does a function type carry a throws set (`callable<int, string> throws Foo`)? If
  not, every callback launders exceptions and the analysis is trivially defeated. If so, function-type
  assignability gets meaningfully more complex.
- **Override and interface rules.** An override must presumably only ever *narrow* the throws set. Confirm,
  and decide what happens when an interface declares nothing but an implementer throws.
- **Async.** A rejected `Promise<T>` carries an exception that surfaces at the `await`, not at the call.
  Does `Promise<T>` need to carry a throws set for the analysis to mean anything in async code?
- **Syntax.** A `throws` clause in the signature, or an attribute (`#[Throws(ParseException::class)]`)?
  The clause reads better; the attribute needs no grammar change and matches how tyhpdef already records
  this in docblocks.
- **Erasure.** Presumably the throws set is purely static and emits nothing. Confirm.

### 8. Extension properties, constants, and static members

Extensions today carry **methods and operators only**. Since an extension already emits as a class of
`public static` methods (`$money->format('USD')` → `\MoneyFormatting::format($money, 'USD')`), extension
**constants** and **static properties** have an obvious home: real constants and static properties on that
same emitted class. Extension **instance** properties need a side table.

```tyhp
extension MoneyFormatting extends Money {
    const string DEFAULT_CURRENCY = 'USD';                       // → \MoneyFormatting::DEFAULT_CURRENCY

    public string $formatted for Money;                          // instance property (syntax TBD)

    function format(string $currency): string { … }
}
```

**Decided (from design discussion):**

- **Constants and static properties** emit as constants and static properties on the extension class.
  Straightforward — they are genuinely class-level, so there is no storage problem.
- **Instance properties are backed by a `\WeakMap`** keyed by the receiver instance. This is accepted as a
  reasonable cost. `\WeakMap` is already declared in `runtime/packages/php/_tyhpdef/Ext.Core.tyhpdef`
  (PHP 8.0+), so the runtime surface exists.
- **Object types only.** Extension instance properties are **not** allowed on scalars or structs — a
  `\WeakMap` requires object identity, and structs emit as plain arrays with none. Extension *methods* on
  scalars/structs are unaffected.
- **Inlining must rewrite `self`.** When `#[\Tyhp\Optimize\Inline]` substitutes an extension body into a
  call site, `self::` no longer refers to the extension class — it refers to whatever class the call site
  sits in, or is a fatal error in a free function. The inliner must canonicalize every `self::` reference
  to the fully-qualified extension class before substituting. This is the Story 31 Phase 3 canonicalization
  invariant applied to the optimizer.
- **`static::` is not allowed in extension bodies.** Late static binding has no meaningful referent in an
  extension and is unresolvable once inlined. Reject it with a diagnostic.
- **Un-inlined `self::` needs no rewrite** — it already resolves to the emitted extension class, which is
  the correct target.
- **Visibility is meaningful and enforced by the checker:** `public` means readable and writable from
  anywhere; `protected` / `private` mean readable and writable **only from within the extension itself**.

> **Note:** Story **27.2** rewrites `self` / `self::` / `new self()` in standalone extension **methods** to the
> **target type**. The “un-inlined `self::` needs no rewrite” bullet above applies to a future extension-constant
> feature only if those constants are referenced by the **extension name**, not by `self::`, once 27.2 has landed.
> Do not keep two meanings of `self`.

**Unresolved questions (must be answered before this can be scheduled):**

- **Declaration syntax for per-type properties.** Story **27.2** gives methods and operators a block-level
  target (`extension Name extends Type` or nested `extends Type { }`). Properties can reuse those groups
  (`extends Money { public string $formatted; }`) once this feature is scheduled. Remaining choice: stored
  vs computed, and whether a property-bearing extension may only target one type. Do not invent a second
  target syntax here.
- **Stored vs. computed.** C# and Kotlin both permit extension properties but **forbid backing fields** —
  theirs are computed only, which sidesteps storage entirely. With `\WeakMap` accepted, Tyhp can support
  both. Should computed (accessor-backed, no `\WeakMap` entry) be a distinct declaration form? It is
  cheaper, has no lifetime concerns, and is exactly the single-expression shape `#[Inline]` wants.
- **`\WeakMap` cost and lifecycle.** One map per extension, or per extension-property? Where is it stored
  (a static on the extension class is the obvious answer)? Confirm that weak keying gives the right
  collection behavior and that nothing accidentally holds instances alive.
- **Interaction with property accessors.** Tyhp already has property accessors / PHP 8.4 hooks. Is an
  extension computed property just "a property hook whose receiver is the first parameter", reusing that
  machinery, or a separate path?
- **`readonly` on extension properties.** Meaningful, or rejected? `readonly` has no construction
  moment to be written in when the object was constructed by someone else.
- **PHP-side visibility.** A `\WeakMap`-backed property is invisible to a PHP consumer of the emitted code
  — they see a static map, not a property on their object. Story 15 (interop contract) should record what,
  if anything, is guaranteed here.
- **Serialization and cloning.** `var_dump`, `json_encode`, and `clone` will not see `\WeakMap`-backed
  values, and a clone gets a fresh identity and therefore no entry. Document as a known limitation, or
  provide a hook?

### 9. `decimal` value-type semantics

`decimal` is a library refined type over `\Tyhp\Decimal` (wrapping GMP or BCMATH) that must *behave* like
a scalar value type. The brand is the author-facing type; the class is the erased representation. See
refined types, [`decimal` as a library brand](#decimal-as-a-library-brand). This entry is only the
value-semantics question, not whether the type exists or how it is spelled.

- **Open question:** how do we make an object instance act like a value type — the way C#'s `record` /
  value types do — so comparisons operate on the underlying value rather than object identity?
- **Current direction:** use comparison operator overloads on `\Tyhp\Decimal` so that `==`, `<=>`, etc.
  compare the underlying numeric value, not the instance. Whether this is *sufficient* to give full
  value semantics (assignment copy behavior, immutability guarantees) is still open.
- Related committed work: operators, converts, backing-library config (GMP vs BCMATH), default
  scale/rounding, and `Decimal::ZERO` live on the class / package (Story 04). `(decimal)` is a named
  brand cast once that grammar exists (`T_DECIMAL_CAST` only until then). Do not re-open "is `decimal`
  a builtin scalar?" here.

---

## Candidate future features (wanted someday, no blocking questions)

### 10. Target-typed `new` (C# 9 style)

When the target type of an expression is already known, the class name could be omitted from `new`:

```
MyClass<string> $myObj = new("asdf");
```

Inspired by C# 9 target-typed `new` expressions. No known design blockers — just unscheduled.

### 11. `sealed` classes and interfaces — NICE-TO-HAVE, gated on user interest

> **Status:** wanted, but deliberately parked. Schedule it only if Tyhp users actually ask for it.
> Parked because closed-set exhaustiveness on interfaces is extra language surface — **not** because
> Story 25 `internal` already covers it. It does not.

A `sealed` type restricts **who may implement or extend it** to a list the author gives:

```tyhp
sealed interface PaymentResult permits Approved, Declined, Failed {}

final class Approved implements PaymentResult { public function __construct(public readonly string $authCode): void {} }
final class Declined implements PaymentResult { public function __construct(public readonly string $reason): void {} }
final class Failed   implements PaymentResult { public function __construct(public readonly \Throwable $error): void {} }
```

`final` says nobody may extend; open says anybody may. `sealed` is the middle: *these* and no others.
The type and its variants are typically **public API**. Callers in other Tyhp packages are supposed
to receive `PaymentResult` and dispatch on it; they must not invent a fifth case.

> **Naming caution.** In C# `sealed` means what PHP's `final` means. The Java/Kotlin/Scala meaning is the
> one intended here. This repo already uses the C# sense informally —
> `IMPLEMENTATION_PLAN_TODO_STORY_24.md` says "Sealed methods: methods marked `final`" — so the term is
> partly spoken for and may need a different keyword. That collision is independent of `internal`.

**Not `internal`.** Story 25 answers “who may **name and use** this symbol?” (project boundary,
omitted from `package.tyhpdef`). `sealed` answers “who may **extend or implement** this type?”
(variant list). Same **erasure** (checker-only; PHP has no counterpart); different **axis**.

`internal` does not close a public variant set:

- Hide the whole hierarchy and foreign Tyhp cannot implement `PaymentResult` — they also cannot
  write `function describe(PaymentResult $r)`. Library consumers never get exhaustive `match`.
- Inside the defining project, anyone can still add `class Refunded implements PaymentResult`, so
  the compiler still cannot treat the set as finite.
- Public interface + `internal` variant classes is worse than either: consumers see
  `PaymentResult` but not the cases, and other packages can still implement the public interface.

They compose: `sealed interface PaymentResult permits …` as public API, `internal function
parseGatewayPayload(…): PaymentResult` to hide the parser.

**The benefit: exhaustiveness checking.** Today this cannot be diagnosed, because the compiler must assume
some other package — or another file in this project — may add an implementation tomorrow:

```tyhp
function describe(PaymentResult $r): string {
    return match (true) {
        $r instanceof Approved => 'ok',
        $r instanceof Declined => 'declined',
        // Failed is unhandled — no diagnostic is possible against an open interface
    };
}
```

Seal the interface and the implementation set is closed and finite, so the missing arm becomes a compile
error. The real payoff is on change: the day a fifth case is added, **every** non-exhaustive `match` in the
codebase reports an error, turning a bug hunt into a worklist. Story 08 already plans exhaustiveness for
enums and literal unions; this extends the same check to `instanceof` dispatch, which `FOUND_BUGS.md`
item 4 currently identifies as blocked.

**Why this matters here specifically.** Sum types with associated data (Rust-style
`enum Shape { Circle(float), … }`) were considered and judged not worth it: the same modelling is achieved
with a shared interface plus small `final` classes — a `ShapeLike` interface with `Circle` / `Rect`
implementations — using only constructs Tyhp already has. That reasoning holds, and the class-based
approach is complete *except* that the compiler cannot prove a dispatch covered every variant. `sealed` is
precisely that missing piece, with no new runtime concept and no new lowering.

**PHP boundary (erasure, not a reason to conflate with `internal`).** `permits` is checker-only and
**erased**; the emitted PHP is an ordinary interface. A PHP developer can write
`class Refunded implements PaymentResult {}` and PHP will accept it. Tyhp `match` that was proved
exhaustive then silently falls through. The guarantee is real **inside Tyhp** and advisory at the
PHP boundary — the same standing as `internal`’s visibility, not the same feature.

If scheduled, mitigations that belong to **this** feature (not to Story 25): emit `@psalm-sealed` /
`@phpstan-sealed` docblocks (Story 31 Idea 4); consider a throwing `default` on exhaustive `match`
so a PHP-side extra implementer fails loudly.

**Open questions if scheduled:** keyword choice given the C# collision; whether `permits` is explicit or
inferred from same-file/same-namespace declarations (Kotlin infers, Java requires the list); whether
`sealed` applies to classes as well as interfaces; and whether a sealed hierarchy must be `final` at the
leaves.

### 12. Struct destructuring and target-typed struct literals

Two small pieces of sugar that together cover every use case a tuple type would, with better readability
and no new type concept. Considered *instead of* tuple types, which were rejected on the grounds that
structs are the superior spelling.

**Struct destructuring is nearly free**, because structs already emit as string-keyed PHP arrays
(`new Point()` → `['x'=>0,'y'=>0]`, `$p->x` → `$p['x']`) and PHP has native keyed list destructuring:

```tyhp
Point $p = getPoint();
[x => $x, y => $y] = $p;        // → ['x' => $x, 'y' => $y] = $p;   (verbatim PHP)
```

No new lowering at all, and the bare-identifier key form already matches `with`
(`new Point() with [x => 1, y => 2]`), so it reads as existing syntax rather than something novel.

**Target-typed struct literals** let a struct be returned without restating its name — the same idea as the
target-typed `new` candidate above:

```tyhp
struct DivResult { int $quotient; int $remainder; }

function divide(int $a, int $b): DivResult {
    return [quotient => \intdiv($a, $b), remainder => $a % $b];
}

[quotient => $q, remainder => $r] = divide(17, 5);
```

Named rather than positional, self-documenting at both the producing and consuming end, and it needs no
tuple type, no new runtime representation, and no arity rules.

**Open questions:** whether to add a shorthand for the common case where the variable name matches the
field name (`{$quotient, $remainder} = divide(17, 5);`) and whether that earns new grammar; whether
destructuring should be checked exhaustively (must every field be bound?) or allow partial extraction;
whether nested struct destructuring is supported; and how this interacts with the still-uncompiled
anonymous `new struct {…}` form.

### 13. Tyhpdef cross-import forms (unverified — needs grammar confirmation before documenting)

The old `tyhpdef_guide.md` listed several "cross-import" tyhpdef forms only as `// TODO` markers; they were
never fully specified, are not in the website docs (`docs/content/tyhpdef_*.md`), and their grammar support
is unconfirmed (especially the `::` member forms). Preserved here so the intent isn't lost:

- Import a **global PHP function as a class static method**:
  `public static function \strval as strval(mixed $val): string;`
- Import **another class's static method as this class's static method**:
  `public static function \StuffCo\Other\OtherClass::getStrValue as strval(mixed $val): string;`
- Import a **class static method as a global function**:
  `function MyClass::staticMethod as myClassStaticMethod(): void;`
- Import a **global constant as a class constant**:
  `public const string|int \GLOBAL_CONST_VALUE as CONST_VALUE ?? 0;`
- Import **another class's constant as a class constant**:
  `public const string|int \StuffCo\Other\OtherClass::OTHER_CONST_VALUE as CONST_VALUE ?? 0;`
- Import a **class constant as a global constant**:
  `const string|int MyClass::CONST_VALUE as GLOBAL_CONST_VALUE ?? 0;`

**Before documenting these on the website:** confirm which forms the grammar/visitor actually accept. The
qualified-name forms (e.g. `\strval as ...`) look grammar-plausible; the `::`-member forms may not be
supported. Do not write authoritative docs for them until verified.

### 14. Struct property auto-initialization to defaults

Instead of leaving unset struct properties `undefined`, auto-initialize them to their type's default value.

- **Note:** this may be a non-issue given that Tyhp already enforces that every struct property must either
  be nullable, be "sometimes"/optional, and/or carry a default value at declaration. Revisit only if the
  current nullable/default enforcement proves insufficient in practice.

---

## Open design questions (from `LANGUAGE_ASSESSMENT.md`, 2026-09-15)

Most of this section is **locked** into Story 21.12 / 31 / `DECISIONS.md`. What remains here is
the two-world *boundary* question that the tagline does not settle, and the parked emitter
refactor.

### 15. Syntax cohesion — locked

| Item | Decision | Where |
|------|----------|--------|
| **15a** `isa` / `isan` / `is_a` / `is_an` | Remove now (greenfield, no deprecation). Keep `is` + `instanceof`. | Story **21.12 G**, `DECISIONS.md` |
| **15b** constructor `: void` | Omitted return type = `: void`. `: parent(...)` unchanged. No implicit parent call. | Story **21.12 H**, `DECISIONS.md` |
| **15c** `with` | Keep. Same as PHP array-copy vs object-identity. | Story **21.12 E**, `DECISIONS.md` |
| **15d** `Type $var` | Docs lead with inference; explicit form stays. | Story **21.12 E** |

`foreach (… as Type $v)` and file-level typed `const` were grammar holes (RESOLVED #51 / #52), not taste.

### 16. Two-world model — tagline locked; boundary still open

**Locked (Story 21.12 E):** public wording is “typed superset of PHP,” not “strongly typed.”

Structs erase to `array`. Generics erase (or opt into a runtime bag). `internal` (Story 25) is
visibility; `sealed` (this file §11) is a closed implementor set. Both erase in PHP; they are not
the same feature. Refined types (this file §5) will be the same standing (Tyhp-land, advisory at
the PHP boundary).

Mitigations already planned, none of which close the gap fully:

- Story **25** — `internal` in the checker.
- Story **31 Idea 4** — PHPStan/Psalm docblocks (shapes, `@template`, later `@psalm-sealed`).
- Story **31 Idea 15** — opt-in runtime struct asserts at public PHP boundaries.

**Still open (do not invent a fourth mitigation until these are answered):**

- Which guarantees are **Tyhp-land only** and which, if any, must survive a PHP caller? Candidate
  “must survive” list is only what Idea 15 covers (struct shapes at public boundaries). Generic
  bounds and `sealed` stay advisory unless a later idea says otherwise.
- Should exhaustive `match` on a Tyhp-only closed set emit a throwing `default` so a PHP-side extra
  implementer fails loudly? Independent of whether `sealed` is scheduled.

### 17. Async I/O ecosystem — scheduled as Story 31 Idea 17

HTTP / filesystem / database non-blocking I/O for `tyhp/async` is **Story 31 Idea 17**. Loop
ownership (stay on `tyhp/async`’s `stream_select` vs become a Revolt driver) is decided there, not
here. Until that idea is implemented, 21.12 E documents that PDO / `curl_exec` / `file_get_contents`
block the loop.

### 18. Emitter as a staged transformer pipeline — don’t

**Rejected for now** (`DECISIONS.md`). Story **21.12 D** (emit-and-run corpus) is the cheap
substitute. If that corpus keeps finding interaction bugs after 21.12 A–C, *then* consider
splitting struct rewrite / splice / `Generic::bind` into explicit passes. Until D says otherwise,
leave the walk.

### 19. Readonly `with` emit (PHP &lt; 8.5, `final`, `readonly class`) — locked

**Locked 2026-09-18.** Not scheduled. Replaces the Story 11 experimental anonymous-subclass
wrapper. Nested omitted-subject `clone with` / `with` is §20 (also locked).

PHP will not reassign an already-initialized `readonly` property. `ReflectionProperty::setValue`,
`setRawValue` (8.4+), `Closure::bind`, and `unset` all fail the same way. The only userland door is
**first-time initialization** on a constructor-skipped instance
(`ReflectionClass::newInstanceWithoutConstructor()` + copy + `setValue`). That is the Spatie
`Cloneable` / Crell `Evolvable` pattern. It does not `extend` the class, so it works on `final`
and on `readonly class`.

The current extend + `__clone` wrapper is not a viable 8.2–8.4 strategy:

- `__clone` reinit of readonly is **8.3+**. On 8.2 the wrapper throws `Cannot modify readonly`.
- Even on 8.3, reinit is declaring-class scope. Subclass assignment of a parent public readonly
  needs 8.4 `protected(set)`.
- `final` cannot be extended (compile-time fatal).
- A non-readonly anonymous class cannot extend a `readonly class` (compile-time fatal). The
  checker’s `TYHP4140` only mentions `final`; `readonly class` is the same brick wall.
- When the wrapper does work (8.4+), the result is an **anonymous subclass** with a leftover
  `$__tyhp_overrides` property, not `T`.

PHP 8.5 native `clone($obj, […])` is also not a drop-in for Tyhp `with`. Public readonly is
`protected(set)`, so a bare `clone($obj, ['alpha' => 128])` emitted in calling code throws
`Cannot modify protected(set) readonly property … from global scope`. It works from a method of
the declaring class, or if the property is `public(set)`. Binding the call to the declaring class
fixes that: `\Closure::bind(fn ($o) => clone($o, $overrides), null, \T::class)($src)` — including
on `final`. `__clone` then runs on the **result**, then overrides (overrides win). Result class is
`T`.

**Emit (locked):**

| Target | Readonly `new … with` / `clone … with` |
|---|---|
| PHP **8.2–8.4** | `\Tyhp\ObjectHelper::cloneWith($src, $overrides)` — hydrator: `newInstanceWithoutConstructor`, walk the parent chain, `setValue` via the **declaring** class’s `ReflectionProperty` (child-scoped `setValue` cannot initialize inherited readonly on 8.2). Same helper for `new T(…) with` (`cloneWith(new T(…), …)`). |
| PHP **8.5+** | `\Closure::bind(fn ($o) => clone($o, $overrides), null, \T::class)($src)` whenever a readonly override is in the list (or always, for one shape). Do **not** emit bare `clone($src, […])` from outside the declaring class. |

**`__clone` on 8.2–8.4 (locked):** clone first, then hydrator-copy from that clone so nested
deep-clones mostly survive. `__clone` runs on a throwaway; `$this` inside `__clone` is not the
object returned. Document that delta vs 8.5. Do not call `$dst->__clone()` as a method (that is
not the clone opcode; readonly reinit does not apply).

**Also locked:**

- In-place `$obj with […]` on readonly stays illegal.
- Drop the extend/`__clone` anonymous-class wrapper.
- `final` and `readonly class` are allowed for readonly `new`/`clone` `with` on PHP &lt; 8.5 once
  the hydrator is the path. `TYHP4140` goes away for that reason.
- Drop `build.experimentalReadonlyCloneWith` (or keep it one release as a kill switch, then remove).
- No serialize-mutation, no FFI.
- Internal classes (`DateTimeImmutable`, …) are out of scope; the checker may reject readonly
  `with` on them.
- `ObjectHelper::with` stays an in-place mutator for non-readonly. Readonly clone/new goes through
  `cloneWith`, not `with(clone $obj, …)`.

Hydrator copies must skip override keys on the source copy, then initialize those keys on the
destination (so readonly is written once). Type mismatches still `TypeError` from `setValue`.

### 20. Nested omitted-subject `clone with` / `with` — locked

**Locked 2026-09-18.** Not scheduled. Option 1: omit the subject, keep existing `with` vs `??`
precedence, parenthesize only when `with` must apply to a coalesced subject.

Inside a `with` list, the clone/in-place target may be omitted. The implicit subject is **that
same key** on the outer `with`’s left-hand value, **before** any overrides in this list.

```
$app2 = clone $app with [
    name => 'Other',
    config => clone with [
        debug => true,
        env => 'production',
    ],
];
```

That is `clone $app->config with [debug => true, env => 'production']` (or the struct-member
equivalent). Same idea for in-place nested `with […]`.

These forms are **only** legal as the value of a `with` pair (object or struct). `$x = clone with
[…]` / `?? new Config()` as a free expression is an error.

#### Three intents

`with` binds tighter than `??` (already true: `$x ?? new Config() with [d => true]` is
`$x ?? (new Config() with [d => true])`). The shorthand does not invert that. Parentheses choose
whether `with` attaches to the fallback or to the coalesced subject.

**C — error if empty** (no parens):

```
config => clone with [debug => true]
config => with [debug => true]
```

If the implicit subject is missing, uninitialized, or `null`, that is an error (same family as
`clone null`). Empty is **not** PHP-falsey: `0`, `false`, and `''` are values.

**B — `with` only on the fallback** (no extra parens; natural binding):

```
config => ?? new Config() with [debug => true]
```

Same as `$app->config ?? (new Config() with [debug => true])`. If the property is already set, it
is left alone. Prefix `?? expr` as a with-pair value is generally `$implicit ?? expr`, so
`config => ?? $defaultConfig` is allowed too.

**A — always apply `with`** (parens required):

```
config => (clone ?? new Config()) with [debug => true]
config => (?? new Config()) with [debug => true]
```

If the subject is present, clone it (first line) or mutate it (second), then apply the list. If
it is empty, use `new Config()` as the subject and apply the **same** list. Desugar must not
clone a brand-new fallback: empty → `new Config() with […]`.

Unparenthesized `clone ?? new Config() with […]` is an **error**. `with` would bind to `new
Config()`, which looks like A and means B-plus-an-accidental-clone of the existing value. Point
at the two spellings above.

Result-level `clone with […] ?? new Config() with […]` is not a feature: a missing subject does
not yield null, so the right-hand side would not run, and the list is duplicated.

#### Clone vs in-place nesting

| Nested spelling | Meaning |
|---|---|
| `clone with […]` | Always a nested **copy**, then overrides. Legal under outer `clone` / `new` / in-place. |
| `with […]` | Nested **in-place**. Legal **only** when the outer `with` is also in-place (`$obj with`). |

`clone $app with [config => with [debug => true]]` is an error: outer clone is shallow, inner
in-place would mutate the `Config` still shared with `$app`. Under `new App() with`, nested
in-place `with […]` is also an error (same aliasing if the constructor retained the instance).
Write `clone with` or `new Config() with` instead.

`$app with [config => clone with [debug => true]]` is allowed: mutate `$app`, replace `config`
with a copy.

Nested in-place `with` cannot assign `readonly` properties (same rule as top-level in-place
`with`). Use `clone with` (or `new … with`).

Structs are included. Nested `clone with` / `with` on a struct member is `\array_replace` of
that member; the in-place-under-clone ban still applies so the rule does not depend on
object-vs-array.

#### Also locked

- Implicit subject is the outer source’s current property/member, not a value assigned earlier
  in the same list.
- Fallback expressions must be assignable to the property/member type.
- No hole token (`it` / `_`), no `??=` with-pair operator, no `clone else new T()` production.
  Those stay available later if A’s parentheses become a real pain; they are not part of this
  lock.
- Emit is the existing (and §19) `with` lowering of the desugared form
  (`clone $implicit with […]`, `$implicit with […]`, `$implicit ?? expr`, etc.). No separate
  runtime helper for the omit itself.

### 21. `with` on arrays (not structs) — wanted, mostly settled

**Wanted 2026-09-18.** Not scheduled. Structs already use `with` as a closed shape (`\array_replace`,
unknown keys are errors, bare property names). This is `with` on a real `array` / `array<T>` /
`array<TKey, TValue>`: **open** keys (add or replace) and **append** for omitted keys.

Assume:

```
array<string, string|null> $keyItems = ["asdf" => "asdfasdf", "blah" => null];
array<int, float> $listValues = [0.33, 4.62, 3.14159, 12.0];
```

**Quoted string keys** (not bare identifiers — those stay struct/object properties):

```
$keyItems with [
    "asdf" => null,
];
```

**Explicit int keys replace;** omitted keys **append** like `$listValues[] =` for each new value.
The array’s key type must allow `int` for unkeyed entries.

```
$listValues with [
    0 => 0.34,
];
```

**Adds are allowed** (the opposite of struct `with`):

```
$keyItems with [
    "foo" => "bar",
];
// ["asdf" => "asdfasdf", "blah" => null, "foo" => "bar"]

$listValues with [
    0.34, 45.99, 0.449204,
];
// [0.33, 4.62, 3.14159, 12.0, 0.34, 45.99, 0.449204]
```

Key-type gates:

| Type | `"foo" =>` | `0 =>` | unkeyed `0.34` |
|---|---|---|---|
| `array<string, T>` | replace or add | error | error |
| `array<int, T>` | error | replace | append |
| `array<T>` / `array` / `array<int\|string, T>` | yes | yes | append |

`array<T>` is `array<int|string, T>`, so unkeyed is allowed. Values must still fit `TValue`.

**Unkeyed is not a PHP array-literal + `array_replace`.**
`[0 => 0.34, 0.34]` as a PHP literal is `[0 => 0.34, 1 => 0.34]`; `array_replace` would overwrite
index 1. Omitted keys are `$a[] =` instead. When **every** pair is keyed, `\array_replace` is
enough (same as struct `with`).

Append is PHP `[] =` (max existing int key + 1), including holes: `[1 => 10, 5 => 20] with [99.0]`
becomes key `6`, not `0`. Do not use `\array_merge` (it reindexes).

#### Locked: `\ArrayAccess` is not array `with` (2026-09-18)

Do not revisit. `\ArrayAccess`, `\Tyhp\Contracts\ArrayAccessShape`, `ArrayObject`, and any
`Foo & \ArrayAccess<…>` stay on the **object** `with` channel: keys are **properties**, never
`offsetSet` / `$obj[] =`. Offsets stay `$obj[$k] =` and `$obj[] =`.

Reasons:

1. They are not PHP arrays. They do not work in array functions; emit cannot be `\array_replace`.
2. Mixing property `with` and offset `with` on one list is confusing. A class can have both stores.
3. Object/struct `with` already accepts **quoted** keys as property/field names when the name is a
   keyword or builtin (`'class' =>`, `'type' =>`), and authors may quote every key by style.
   `$ao with ["foo" => 1]` is `$ao->foo`. There is no spelling left that would mean `offsetSet`
   without stealing that.

Quoted vs bare is **not** how the checker tells array from object/struct. The receiver’s type is.
Known object/struct: bare or quoted (or a keyword/builtin token) → the same property/field.
Known `array`: quoted string / explicit int / unkeyed only; a bare identifier is an error.
Unknown (`array|struct`, `mixed`, `\ArrayAccess` used as “maybe an array”): today’s “`with` on
this expression” error. Do not guess.

#### Pre-locked

- Left-to-right: replaces and appends run in list order, interleaved. Not “all replaces, then all
  appends.”
- No `new array() with`. Forms are `$arr with […]` and `clone $arr with […]` only (`clone` on
  arrays is already a no-op; this is copy-then-replace/append). Same value-type rebind as struct
  `with` (not object identity).
- No key deletion in v1 (`unset` stays `unset`).
- Bare identifiers are illegal as array `with` keys (must quote strings; ints are numeric
  literals). `$keyItems with [asdf => null]` is therefore an error. `$point with [x => 1]` and
  `$point with ['x' => 1]` are both the struct field `x` (same as object properties).
- Nested omitted-subject `with` (§20) on an array member still requires quoted / explicit-int /
  unkeyed rules on the inner list. Nested in-place `with […]` under outer `clone` / `new` stays
  banned even though arrays rebind — one rule.

#### Open (do not pretend these are decided)

- **Computed keys** (`$k => $v`, maybe `"{$k}"`) — likely yes, typed as `TKey` / `TValue`; structs
  skip dynamic keys, and that is a reason this is not “struct `with` with looser names.”
- **Intish strings** — PHP treats `"0"` and `0` as the same key. Candidate: error on
  integer-string literals (`"0"`, `"12"`) for both `array<string, T>` and `array<int, T>` unless
  written as `0 =>`, so the quoted-vs-int split stays honest.
- **Spread** (`...$more`) — candidate: `\array_replace` for keyed spread, not `\array_merge`;
  unkeyed spread of a list could append elementwise.
- **Expression emit** when any entry is unkeyed — `\Tyhp\ArrayHelper::with($arr, $replace,
  $append)` in `tyhp/core` vs an IIFE of `$a[] =`. Keyed-only can stay `\array_replace`.

---



