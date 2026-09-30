# TyhpLang Versioning — PHP version constraints

Composer-compatible evaluation of PHP platform version constraints against
`output.phpVersion`. Binder and checker (Story 20.5) call this for
`declare(php="…")` and `#[\Tyhp\Php("…")]` gates. This folder does not parse
source, emit PHP, or report diagnostics — it only parses and compares version
strings.

## Types

| Type | Role |
|------|------|
| `PhpVersion` | Numeric `major.minor.patch`. `TryParse` is for **targets** (`output.phpVersion`). |
| `PhpVersionConstraint` | Parsed Composer constraint. `TryParse` / `Evaluate` / `IsSatisfied` / `AnyOverlap`. |
| `PhpVersionConstraintResult` | Non-throwing evaluate result: valid vs unsatisfied vs invalid syntax. |

Public API lives in `Tyhp.TyhpLang.Versioning`. There is no NuGet Composer-semver
dependency; the parser is focused on numeric PHP platform versions.

## Target normalization

`PhpVersionConstraint.TryNormalizeTarget` / `PhpVersion.TryParse`:

- `"8.2"` → `8.2.0` (patch padded)
- `"8.2.0"` / `"8.2.15"` → as written
- Optional `v` prefix (`v8.4`)
- Null, empty, wildcards (`8.2.*`), and operators are **not** valid targets

Gating compares this single concrete version to the constraint. A project that
sets `output.phpVersion` to `8.2` is treated as PHP `8.2.0`.

## Constraint language

Composer’s documented grammar, as it applies to numeric PHP versions:

| Form | Meaning |
|------|---------|
| `>=`, `>`, `<=`, `<`, `!=`, `<>` | Comparison; missing components pad to zero (`>=8.2` → `>=8.2.0`) |
| Space or comma | AND (`>=8.2 <8.4`, `>=8.2,<8.4`) |
| Double pipe or single pipe | OR (AND binds tighter) |
| `^8.2` | `>=8.2.0 <9.0.0` (caret: leftmost non-zero digit; `^0.3` → `<0.4.0`) |
| `~8.2` | `>=8.2.0 <9.0.0`; `~8.2.3` → `>=8.2.3 <8.3.0` |
| `8.2.*` / `8.2.x` | `>=8.2.0 <8.3.0` |
| `*` / `*.*` | Any valid target |
| `8.2 - 8.4` | Hyphen range: `>=8.2.0 <8.5.0` (partial right side is a wildcard). `8.2.0 - 8.4.0` is inclusive on both ends |
| `@dev` / `@stable` / `-stable` | Parsed and ignored for matching (targets are always stable numeric PHP) |

`~>` is invalid (Composer’s error: use `~`).

Branch names (`dev-master`), date versions, and non-numeric tokens are invalid
for this PHP-platform helper.

## Bare minor banding (Tyhp)

Composer treats a bare `8.2` as exact `8.2.0.0`. Tyhp does **not**:

- `"8.2"` / `"=8.2"` / `"==8.2"` → entire minor `>=8.2.0 <8.3.0`
- `"8"` (one component) → entire major `>=8.0.0 <9.0.0`
- `"8.2.0"` / `"=8.2.0"` → exact patch (three components specified)

Comparison operators do **not** use this banding: `>=8.2` is `>=8.2.0` (and
therefore also matches 8.3+). `>8.2` does **not** match target `8.2` (`8.2.0`).

## Invalid input

Every public method is non-throwing.

- Empty/null constraint → `TryParse` false; `Error` set
- Garbage syntax → same; checker maps this to **4300**
  `CheckerPhpVersionInvalidConstraint` (Phase 5 owns the `MessageCode` / resx)
- Empty/null/invalid **target** → `IsSatisfied` / `IsSatisfiedBy` false;
  `Evaluate` sets `TargetIsValid = false` while `ConstraintIsValid` may still
  be true

Callers that must distinguish “bad syntax” from “gate does not match”:

```csharp
var result = PhpVersionConstraint.Evaluate(project.PhpVersion, constraintText);
if (!result.ConstraintIsValid)
{
    // diagnostic 4300 using result.Error
}
else if (result.IsSatisfied)
{
    // bind / keep the declaration
}
```

`IsSatisfied(target, constraint)` is a boolean convenience that is false for
invalid syntax *and* for unsatisfied gates.

## Overlap between constraint sets

`PhpVersionConstraint.AnyOverlap(left, right)` answers whether some PHP version
could satisfy every constraint in `left` **and** every constraint in `right`
simultaneously — each list is AND-ed internally, so this composes an enclosing
`declare(php=…)` stack with a `#[\Tyhp\Php(...)]` gate on either side. It is
an exact interval check: for every literal version referenced by either side's
comparators, membership can only change exactly at that version or the patch
immediately above it, so sampling those candidate points (plus the version
floor) never misses a real overlap, including patch-level ranges. It never
throws; invalid constraint strings contribute no restriction (diagnostic 4300
owns reporting those separately).

The binder (Story 20.5 Phase 4) uses `AnyOverlap` to decide whether two
version-gated same-name declarations (functions, types, members) are allowed
to coexist (disjoint → keep both; overlapping → `CheckerPhpVersionDuplicateDeclaration`,
4303) and also calls `Evaluate` for `declare(php=…)` (Phase 3). Checker,
emitter, attribute gating diagnostics, and the missing-`output.phpVersion`
warning are later phases.
