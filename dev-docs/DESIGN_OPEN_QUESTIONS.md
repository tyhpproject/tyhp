# Tyhp — Design Open Questions & Candidate Future Features

> **What this is:** a holding pen for language ideas that are **not committed** to the roadmap yet — either
> because they still have unresolved design questions, or because they are "nice to have someday" candidates.
> Nothing here is a promise. When an idea's questions are answered and it is scheduled, it graduates into a
> numbered story in `ROADMAP.md` / an `IMPLEMENTATION_PLAN_TODO_STORY_NN.md`.
>
> For ideas that were **considered and explicitly rejected**, see `DECISIONS.md`.
> For the committed sequence of work, see `ROADMAP.md`.

---

## Open design questions (feature wanted, design unresolved)

### Refined types — `type` with a body

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

```tyhp
// Pure brands — no predicate is possible; these exist only to be distinct from other strings.
// Auto-widen: identity to the full base union (here just `string`).
type UnsafeHtml            = string { guard => true; };
type DangerousPreserveHtml = string { guard => true; };

// Value-determined — the guard is the whole definition, and coerce is derived from it.
// Auto-widen to `int` keeps the short guard-only form.
type Positive = int { guard => $value > 0; };

// Multiple bases, per-base guards and coercions, and an explicit widening beyond the auto union.
type Safe = UnsafeHtml|DangerousPreserveHtml {
    guard => !\str_contains($value, '<');          // heuristic, not the definition

    coerce (UnsafeHtml $value)            { return \htmlspecialchars($value, \ENT_QUOTES); }
    coerce (DangerousPreserveHtml $value) { return $value; }

    // Auto: Safe → UnsafeHtml|DangerousPreserveHtml (identity).
    widen(): string => $value;                     // opt in to a proper subset / other target
};

// Overloaded guards and widens across several bases.
// Auto: Numeric → int|float|string. Subset targets below are opt-in.
type Numeric = int|float|string {
    guard (string $value)    => \is_numeric($value);
    guard (int|float $value) => true;

    widen(): string => \strval($value);
    widen(): int    => \intval($value);
    widen(): float  => \floatval($value);
};

// Recovery brand must be opaque — a transparent `type UnsecureSecret = string` would collide
// with `widen(): string` (same target).
type UnsecureSecret = string {
    guard => true;
    widen(): string => $value;                     // recover later
};

// Redacted at the `string` boundary; recoverable via UnsecureSecret.
// Auto union widen `string|int` would leak the payload to `string|int` parameters, so unset it.
type Secret = string|int {
    guard => true;
    widen(): string|int => unset;                  // kill auto union widen
    widen(): string => "****";                     // or throw
    widen(): UnsecureSecret => \strval($value);
};
```

**Why this is worth having** (the motivating cases, in rough order of value):

- **Escaped vs. unescaped strings.** The case most worth having in a PHP-targeted language — it converts a
  class of XSS and injection bug from a code-review problem into a compile error, at zero runtime cost. The
  same shape covers raw vs. quoted SQL identifiers and any other "has this been sanitized yet" distinction.
- **Identifier mix-ups.** `UserId` / `OrderId` / `TenantId` are all `int` and interchange silently today.
  The most common case by far.
- **Units.** `Cents` vs. `Dollars`, `Seconds` vs. `Milliseconds` — all `int`, catastrophic to confuse.
- **"Parse, don't validate."** If the only way to obtain an `Email` is through its `coerce`, then *holding*
  one is proof it was validated.
- **It subsumes range types and regex-constrained strings.** Both were previously written up as their own
  entries in this file, and both were **dropped in favour of this mechanism** — do not re-propose either.
  See the next subsection for what that trade buys and what it costs.

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

**`decimal` ranges are expressible.** For this feature, the lowercase keyword `decimal` is a **scalar**
base — same tier as `int` / `float` / `string` / `bool` — so `type UnitD = decimal { guard => …; }` is an
ordinary refined type. The class `\Tyhp\Decimal` / `Decimal` is a separate **object** type and is *not*
what authors write as a refined base when they mean the scalar. Internal `decimal` → `\Tyhp\Decimal`
rewriting still runs as today, but **after** refined-type lowering so refinement rewrites see the scalar
form. Nullable cast `(?decimal)` is rejected for the same reason as `(?bool)`: lexer-level scalar casts
do not take the nullable form. That is intentional consistency, not a wart unique to `decimal`.

#### The three members

All three are overloadable, all are statically dispatched, and none of them exist at runtime as members.

| Member | Direction | Dispatches on | Default when omitted | May fail |
|---|---|---|---|---|
| `guard` | narrowing test | operand's static type | **none — always required** | no |
| `coerce` | in: base → this | operand's static type | `guard ? $value : throw` | yes — throws |
| `widen` | out: this → target | target type at the use site; optional stored-base arms | identity to the **full union of direct bases** only | no |

`guard` must cover **all** base types; with a union base, either one guard handles the whole union or
overloads cover it exhaustively.

#### Semantics (decided)

- **`guard` is a narrowing test, not an invariant.** It answers "may I brand *this base value* as this
  type?" It does **not** promise that every value of the type satisfies it — which is exactly what lets
  `Safe`'s second `coerce` overload deliberately produce a `Safe` containing `<`. Consequences: nothing may
  optimize on the assumption that a guard holds, and the three forms read as `true` = any base value may be
  branded (a pure brand), `false` = no base value may be branded and `coerce` is the only way in, and a
  real predicate = narrowable when it passes.
- **`is` has three cases.** Operand already the type (or narrower) → folds to `true`, the guard never runs.
  Operand is a base → evaluate the guard. Operand unrelated → **compile error**. Without the first and
  third rules the design contradicts itself: `$safe is Safe` would return `false`, and `$userId is OrderId`
  would return `true` for two types the checker considers unrelated.
- **One hop only, in both directions. No path finding.** `string → Safe` is two hops and rejected;
  `(DangerousPreserveHtml)"…"` followed by an implicit hop to `Safe` is two *single* hops and accepted.
  This is the entire safety mechanism, and it is why no `allowForce` flag is needed — the escape hatch is a
  marker type with its own `coerce` overload, which is greppable and opt-in per call site rather than a
  blanket per-type boolean.
- **Auto-widen is identity to the full union of direct bases only.** `Positive` (base `int`)
  auto-widens to `int`; `Safe` auto-widens to `UnsafeHtml|DangerousPreserveHtml`; `Numeric`
  auto-widens to `int|float|string`. That default is always total (identity on the erased payload) and
  is what keeps the short guard-only form. Any **proper subset** or other target (`string`, `int`,
  `UnsecureSecret`, …) is **opt-in** via an explicit `widen(): T`. Authors may override the auto union
  target with `widen(): BaseUnion => unset` (or a throwing / redacting body) — needed for types like
  `Secret`, where leaving `string|int` open would bypass a redacting `widen(): string`. `unset` is
  **all-or-nothing for that exact return type** and must be a **compile** error at the use site, not a
  runtime throw. `unset` is already a lexer token (`T_UNSET`), so this needs no new keyword. There is
  no per-arm automatic unwrap to each direct base individually — that was unsound for multi-erasure
  unions (holding `int` does not make the value usable as `string`).
- **Dispatch is static; overload overlap is a compile error.** Overlap is judged on **static
  assignability** between parameter types, not erased representation — `coerce(UnsafeHtml)` and
  `coerce(DangerousPreserveHtml)` coexist happily, while `coerce(UnsafeHtml)` and `coerce(string)` collide.
  The same rule applies to explicit `widen(Base $value): T` arms (see unresolved widen overload notes).
- **Erased brands are not runtime-discriminable.** Two brands over the same base are indistinguishable in
  emitted PHP, so nothing may require testing *which* brand a value carries. This is why `coerce` is
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
  enums.
- **`from` / `tryFrom` are rewritten pseudo-calls, not real methods.** They are compile-time syntax wearing
  method clothing, so they cannot be used as callables or reached by reflection. The checker must reject
  both.
- **Base types are restricted to scalars, arrays, and other refined types.** Scalars include the
  keyword `decimal` (see above). Real **object** bases (`\Tyhp\Decimal`, user classes, etc.) are
  excluded for now, which makes any collision with `operator convert` impossible by construction. This
  also matches the motivation — `coerce` exists precisely to serve types with no class to hang an
  operator off. A candidate relaxation for object bases is under unresolved questions below; it is
  independent of `decimal`, which does not need that relaxation.
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

**Every write back into refined-typed storage is a re-coercion point.** Compound assignment, `++` /
`--`, property assignment, array element assignment and append, and destructuring all desugar to a
write whose right-hand side must **coerce** back into the declared type. Where that coercion is fallible
the write is a **compile error** and the author spells the conversion out:

```tyhp
$v = (Positive)($v - 5);                // throws if the result is not positive
?Positive $r = (?Positive)($v - 5);     // null if the result is not positive
```

Casting an *operand* is not a substitute for write-back: `$v -= (Positive)5;` fails for the same reason —
the failure is on storing into `Positive`, not on reading. A no-op cast on a value already at the base
also emits nothing useful when the guard folds and types erase.

**Refined types do not ride through operators.** `$cents1 + $cents2` is typed as `int` (base), not
`Cents`. Re-entering `Cents` is a coerce at the assignment / write-back site (identity and infallible for
a pure brand; fallible for `Positive`). Sibling brands meeting at the shared base without an explicit
cast still launder units if assigned loosely — `Cents $c = $cents + $dollars` is a coerce from `int`,
which a pure brand accepts; catching that remains a lint / discipline problem unless the author keeps
units behind opt-in subset widens and avoids treating the base union as interchangeable at call sites.
Prefer APIs that take `Cents` / `Dollars` nominally.

| Expression | Result type | Write-back to refined storage |
|---|---|---|
| `Cents + Cents`, `Cents + 5` | `int` | OK into `Cents` — pure brand, identity coerce |
| `Positive - Positive` | `int` | **error** into `Positive` — coerce fallible |
| `$safe .= $userInput` | `string` (op on base) | **error** into `Safe` — coerce fallible |
| `(string)$secret . $x` | `string` | uses redacting widen first, then concat |

`Positive - Positive` remains the worked counterexample against “same refined type propagates”: the
operation is legal on the base (`int`), but storing the result back into `Positive` is not automatically
sound.

**By-ref binding requires exact type identity.** A by-ref argument is a read *and* a write, and the write
happens inside a callee that knows only the parameter type — possibly hand-written PHP — so there is no
site at which to re-coerce. Neither widening nor coercion applies at a by-ref argument position:

```tyhp
function reset(int &$x): void { $x = -4; }

Positive $v = 1;
reset($v);          // error: by-ref needs Positive, not an int widen
```

A `Positive` variable binds to `Positive &$x` and to nothing else. The same rule covers
`foreach ($a as &$item)` and PHP builtins with out-parameters such as `preg_match($p, $s, $matches)`.

**A total `coerce` is the opt-in normalizer.** Where a type has a sensible correction for out-of-range
values, writing a `coerce` that never throws makes write-back infallible, which makes mutation both legal
and self-normalizing:

```tyhp
type Percent = int {
    guard  => $value >= 0 && $value <= 100;
    coerce (int $value) { return \max(0, \min(100, $value)); }   // never throws
};

Percent $p = 90;
$p += 50;           // op as int -> 140 -> coerce clamps -> 100
```

`Positive` has no sensible total coercion, so it does not get one and stays strict. The author selects the
behaviour by how they write `coerce`, and no separate mechanism is needed.

**Considered and rejected — a `conform` member.** A normalizer implicitly invoked after every mutation,
returning a corrected value. It contradicts the decided semantics directly: `guard` is a narrowing test
and deliberately **not** an invariant, so there is nothing for a mutation to break and nothing to restore.
The incoherence shows up immediately in the obvious example — a `conform` on `Positive` returning `0`
produces a value failing `$value > 0`, so its output would itself need checking, and then the failure of
*that* check needs an answer. It also silently substitutes a wrong-but-tidy number for a wrong-and-visible
one, and it adds runtime cost to a feature whose selling point is erasing completely. Every use case it
served is covered by a total `coerce`, written explicitly by the author.

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

The mistake is at the write-back, so the error belongs at the write-back (fallible coerce into `Safe`).

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

**The guarantee is Tyhp-land only.** Guards exist at coercion sites and nowhere else, so hand-written PHP
can store `-4` in something Tyhp types as `Positive`. This has the same standing as `internal` and
`sealed`, and is inherent to erasure rather than specific to mutation.

#### Emission

Inline `guard` and `coerce` bodies where they are short enough; hoist larger bodies into generated static
helper classes. Those helpers exist only to support the emitted PHP, so they may use mangled names in a
dedicated namespace and are **not** part of the PHP-facing surface. They must be reserved names the checker
forbids user code from declaring (case-insensitively, since PHP class and method names are), should be
`internal` once Story 25 lands, and are ideal candidates for Story 31's lowering and relocation since
nothing references them by name.

A force-style conversion that skips `coerce` emits **nothing at all** — the runtime representation is
identical, so only `guard`, `coerce`, and non-trivial `widen` ever produce code.

#### Grammar impact — the main implementation risk

Casts are currently **lexer tokens**, not parser constructs: `T_INT_CAST` matches `'(' 'int' ')'` as a
single atom against a closed list, and Tyhp added `T_DECIMAL_CAST` the same way. So `(Safe)$x` cannot be
made to work by adding a token — cast recognition has to move into the **parser**, which revives the C
"typedef name" ambiguity:

```tyhp
(FOO) - $x      // cast of -$x to type FOO, or constant FOO minus $x?
```

Unresolvable at parse time, because Tyhp parses before it binds. The existing scalar cast tokens keep
lexer-level precedence and do not accept the nullable form.

**Unresolved questions (must be answered before this can be scheduled):**

- **How is the cast ambiguity resolved?** Either parse `(Name) expr` into an ambiguous node and let the
  binder decide once it knows whether `Name` is a type — which fits Tyhp's parse-then-bind pipeline — or
  adopt a lookahead rule where `(Name)` is a cast only when the following token cannot continue a binary
  expression, so `(FOO) $x` casts and `(FOO) - $x` stays subtraction. The first is architecturally
  cleaner; the second is simpler but is a rule that needs explaining forever.
- **How does the checker know an author-written `coerce` can fail?** The write-back rule is that only
  infallible coercions may be implicit on mutation / assignment into refined storage, but with
  throw-not-null semantics the return type no longer signals fallibility. The derived default is
  decidable (`guard => true` cannot fail; a real guard can), but a hand-written body needs either
  exception-effect tracking (see the `throws` entry above) or an explicit marker. **This is the
  highest-priority question in this entry.** Infallibility is what gates write-back after base-union
  operations, so whether `$p += 50;` compiles depends on the answer — it is a prerequisite rather than
  a refinement.
- **What is the opacity boundary?** ML and Haskell make a newtype transparent inside its defining module
  and opaque outside, which is what lets the module construct values at all. Tyhp's natural equivalent is
  `internal` (Story 25) — transparent within the declaring file/package, opaque beyond it. Confirm that
  coupling, or define an independent boundary.
- **Do scalar pseudo-objects apply?** Does `Email` get `->trim()` from its underlying `string`? Convenient,
  but it leaks the representation and partially defeats opacity.
- **Can extensions target a refined type?** `extension function domain(extends Email $this): string` would
  be a strong pairing, giving a brand real behaviour. Confirm the extension machinery can target an erased
  type rather than a real class.
- **Guard purity.** A guard must be side-effect-free and deterministic or the type means nothing, and
  inlining it would duplicate any side effect. Enforced how — reusing the `#[\Tyhp\Optimize\Pure]`
  analysis? What exactly is rejected?
- **Compile-time folding on literals.** `Positive $x = 5;` should be checkable at compile time by folding
  the guard. Which guard forms are foldable, and what happens when one is not?
- **May object types be bases?** Candidate direction: allow object bases that define **no**
  `operator convert` overrides, so refined `coerce` / `widen` cannot collide with class convert. Extra
  rule: when other resolved bases exist beside that object, every outbound target that is not the auto
  full-union identity must be accounted for (explicit `widen`, residual, `*Convertible` / `\Stringable`
  fallback, or whole-target `unset`). `\Stringable` and `*Convertible` sit **lowest** on the fallback
  chain: they may satisfy a required arm by default, but an author-written `widen(MyObject $value): int`
  on the refined type overrides them **only** at refined-type widen sites. Independent of scalar
  `decimal`, which is already allowed as a base.
- **Can `widen` overload on the stored base as well as the target?** Decided direction (candidate locked
  in for scheduling — still listed here until the story is cut):
  1. **`widen(): T`** — residual fallback. `$value` is typed as the full base union **minus** bases
     covered by explicit arms for `T`. Body must return `T`. If that residual type is empty (`never`),
     this form is a **declaration error** (cannot type-check `$value`).
  2. **`widen(Base $value): T`** — explicit arm, selected when the held erased type matches `Base`.
     Parameter must be a resolved base (or union of bases); a non-base is an error. Explicit arms must
     not overlap (same static-assignability rule as `coerce`). Same-erasure brands cannot be split.
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
- **Precedence across all conversion mechanisms.** This is the third time the "two conversion mechanisms
  both apply" question has arisen (Story 31 Idea 2 already flagged it between the `*Convertible` contracts
  and `operator convert`, and deferred it). Non-object bases avoid it for the common case; the object-base
  candidate above needs that ordering stated once
  (`explicit widen` > `residual widen(): T` > auto union identity > `*Convertible` / `\Stringable`),
  with genuine ties as errors.

### Declaration-site generic variance (`in` / `out`)

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
  thereafter. C# exempts constructors from the check. Confirm the same rule, and decide how `readonly` /
  `init` properties are classified.
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

### Exception effect tracking (`throws`)

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
  aspirational. That doc needs correcting whether or not this feature is ever built (same class of problem
  as the `init` diagnostics below).
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

### Extension properties, constants, and static members

Extensions today carry **methods and operators only**. Since an extension already emits as a class of
`public static` methods (`$money->format('USD')` → `\MoneyFormatting::format($money, 'USD')`), extension
**constants** and **static properties** have an obvious home: real constants and static properties on that
same emitted class. Extension **instance** properties need a side table.

```tyhp
extension MoneyFormatting {
    const string DEFAULT_CURRENCY = 'USD';                       // → \MoneyFormatting::DEFAULT_CURRENCY

    public string $formatted for Money;                          // instance property (syntax TBD)

    function format(extends Money $this, string $currency): string { … }
}
```

**Decided (from design discussion):**

- **Constants and static properties** emit as constants and static properties on the extension class.
  Straightforward — they are genuinely class-level, so there is no storage problem.
- **Instance properties are backed by a `\WeakMap`** keyed by the receiver instance. This is accepted as a
  reasonable cost. `\WeakMap` is already declared in `runtime/php-extensions/php8.2.9/ExtCore.tyhpdef`
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

> **Note:** the `self::`-rewriting requirement is not created by this feature — it is already latent for
> any `#[Inline]` method whose body references `self::`, including ordinary class methods touching a
> private constant. Extension constants would simply make it easy to hit.

**Unresolved questions (must be answered before this can be scheduled):**

- **Declaration syntax for per-type properties.** An extension block can extend *several* target types
  (each method names its own receiver via `extends T $this`), so there is no block-level receiver a
  property can implicitly attach to. Properties therefore need to name their target — a `for Money`
  clause as sketched above, a nested per-type block, or a rule that a property-bearing extension may only
  target one type. Which reads best and costs least grammar?
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
- **`readonly` / `init` on extension properties.** Meaningful, or rejected? `readonly` has no construction
  moment to be written in when the object was constructed by someone else.
- **PHP-side visibility.** A `\WeakMap`-backed property is invisible to a PHP consumer of the emitted code
  — they see a static map, not a property on their object. Story 15 (interop contract) should record what,
  if anything, is guaranteed here.
- **Serialization and cloning.** `var_dump`, `json_encode`, and `clone` will not see `\WeakMap`-backed
  values, and a clone gets a fresh identity and therefore no entry. Document as a known limitation, or
  provide a hook?

### `decimal` value-type semantics

`decimal` is an object-backed type (wrapping GMP or BCMATH) that must *behave* like a scalar value type.
For refined types and other surface typing, the keyword `decimal` is treated as a scalar; the class
`\Tyhp\Decimal` is the object representation after rewrite (see refined-types entry above).

- **Open question:** how do we make an object instance act like a value type — the way C#'s `record` /
  value types do — so comparisons operate on the underlying value rather than object identity?
- **Current direction:** use comparison operator overloads (from Tyhp's operator-overload feature) so that
  `==`, `<=>`, etc. compare the underlying numeric value, not the instance. Whether this is *sufficient* to
  give full value semantics (assignment copy behavior, immutability guarantees) is still open.
- Related committed work: the `decimal` type itself, its operator/cast overloads, backing-library config
  (GMP vs BCMATH), default scale/rounding, and the `decimal::ZERO` constant are all part of the runtime
  library story (Story 04). This entry only tracks the *value-semantics* question, not the type's existence.

---

## Unresolved status conflicts (needs a decision)

### `init` property modifier — status unclear

A write-once-during-construction property modifier (C#-style `init`; settable in the constructor and via
`with`, but not afterward — unlike `readonly`, which also blocks `with`).

- An old planning note (`Syntax_TODO.md`, now deleted) stated it was **REMOVED / cancelled** on the grounds
  that `readonly` is sufficient.
- **However**, the current website docs treat it as a **live feature**: a full page
  (`docs/content/tyhp_3200_initPropertyModifier.md`), references in
  `docs/content/tyhp_1300_newObjectDeclSyntax.md`, and a documented diagnostic list. These docs are newer
  than the "removed" note.
- **But the diagnostics it cites do not exist, and two of them collide.** `tyhp_3200` documents `TYHP4055`
  ("property is declared as `init`") and `TYHP4056` ("`init` cannot be used with property hooks"), but
  `MessageCode.cs` — the single source of truth per `CONVENTIONS.md` §1 — assigns those numbers to
  `CheckerRedundantTypeInUnion` and `CheckerUseBoolInsteadOfTrueFalse`. The other two codes the page cites
  (`TYHP4003` → `CheckerNotAllowedMemberModifier`, `TYHP4005` → `CheckerMemberModifierConflict`) do exist
  but are generic modifier errors, not `init`-specific. There is **no** `init` diagnostic in
  `MessageCode.cs`, so "dedicated diagnostics" is not actually evidence that `init` is live.

**Decision needed:** confirm whether `init` is a current feature or cancelled.
- If **current:** allocate real codes for the two `init`-specific diagnostics in `MessageCode.cs` (with
  matching `.resx` entries) and correct `tyhp_3200` to cite them, then delete this entry.
- If **cancelled:** remove/deprecate the `init` content from the website docs (`tyhp_3200` and the
  references in `tyhp_1300`), remove the TOC entry, and move it to `DECISIONS.md` as a rejected feature.
- **Either way:** fix the `TYHP4055`/`TYHP4056` collision in `tyhp_3200` — it currently documents two real
  codes against the wrong messages.

---

## Candidate future features (wanted someday, no blocking questions)

### Target-typed `new` (C# 9 style)

When the target type of an expression is already known, the class name could be omitted from `new`:

```
MyClass<string> $myObj = new("asdf");
```

Inspired by C# 9 target-typed `new` expressions. No known design blockers — just unscheduled.

### `sealed` classes and interfaces — NICE-TO-HAVE, gated on user interest

> **Status:** wanted, but deliberately parked. This is a **Tyhp-land-only** restriction that cannot be
> enforced in emitted PHP (see the caveat below). Schedule it only if Tyhp users actually ask for it.

A `sealed` type restricts **who may implement or extend it** to a list the author gives:

```tyhp
sealed interface PaymentResult permits Approved, Declined, Failed {}

final class Approved implements PaymentResult { public function __construct(public readonly string $authCode): void {} }
final class Declined implements PaymentResult { public function __construct(public readonly string $reason): void {} }
final class Failed   implements PaymentResult { public function __construct(public readonly \Throwable $error): void {} }
```

`final` says nobody may extend; open says anybody may. `sealed` is the middle: *these* and no others.

> **Naming caution.** In C# `sealed` means what PHP's `final` means. The Java/Kotlin/Scala meaning is the
> one intended here. This repo already uses the C# sense informally —
> `IMPLEMENTATION_PLAN_TODO_STORY_24.md` says "Sealed methods: methods marked `final`" — so the term is
> partly spoken for and may need a different keyword.

**The benefit: exhaustiveness checking.** Today this cannot be diagnosed, because the compiler must assume
some other package may add an implementation tomorrow:

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

**⚠️ It cannot be enforced in PHP — this is the reason it is parked.** `permits` is checker-only and
**erased**; the emitted PHP is an ordinary interface. A PHP developer consuming the generated code can
write `class Refunded implements PaymentResult {}` and PHP will accept it without complaint. At that point
every `match` Tyhp proved exhaustive silently falls through, because Tyhp's proof rested on an assumption
PHP does not hold. So:

- The guarantee is real **inside a Tyhp codebase** and worth having there.
- The guarantee is **advisory only** at the PHP boundary.
- This is the same category as the `internal` visibility modifier (Story 25) — a Tyhp-land restriction
  with no PHP counterpart — so there is precedent, but each such feature widens the gap between what Tyhp
  promises and what the emitted artifact actually enforces.

Partial mitigations if it is ever scheduled: emit `@psalm-sealed` / `@phpstan-sealed` docblocks (Story 31
Idea 4) so PHP-side static analysis catches violations; and consider whether exhaustive `match` should
still emit a `default` arm that throws, so an unexpected PHP-side implementation fails loudly instead of
returning null.

**Open questions if scheduled:** keyword choice given the C# collision; whether `permits` is explicit or
inferred from same-file/same-namespace declarations (Kotlin infers, Java requires the list); whether
`sealed` applies to classes as well as interfaces; and whether a sealed hierarchy must be `final` at the
leaves.

### Struct destructuring and target-typed struct literals

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

### Tyhpdef cross-import forms (unverified — needs grammar confirmation before documenting)

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

### Struct property auto-initialization to defaults

Instead of leaving unset struct properties `undefined`, auto-initialize them to their type's default value.

- **Note:** this may be a non-issue given that Tyhp already enforces that every struct property must either
  be nullable, be "sometimes"/optional, and/or carry a default value at declaration. Revisit only if the
  current nullable/default enforcement proves insufficient in practice.
