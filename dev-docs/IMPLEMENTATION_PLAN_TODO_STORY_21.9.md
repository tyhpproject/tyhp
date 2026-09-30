# Implementation Plan: Story 21.9 — Type-alias `\Tyhp\Type` factories

> **Roadmap position:** Story 21.9 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.8**, before Story **21.10**). **Implement after 21.8.**
> **Direct dependencies (new numbering):** 08 (checker, alias expansion, `typeof` / `is` / `instanceof`), 09 / **11** (emitter; Story 11 locked “aliases produce no PHP”), 04 (`tyhp/core` `\Tyhp\Type`), 15 (interop contract: aliases become callable Type factories in PHP), **16.5** (callable signature utilities; `callable<..., TReturn>` bounds feed Tuple/Struct)
> **New story:** stop fully erasing type-alias *declarations*. Emit a namespace function (file-level) or static method (class-level) with the alias’s name that returns the `\Tyhp\Type` for the aliased body. PHP type **hints** still expand to the fully resolved underlying type. `typeof`, `is` / `instanceof`, `\Tyhp\Type::*` operands, and `default(Alias)` use the helper. Generic aliases take `\Tyhp\Type` value arguments (not Mechanism D Closures). `typeof` / `default` both take `typeExpr` only.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-08
> **Last design lock:** 2026-09-09 — factory (not Mechanism D); `typeof` → `typeExpr`; no `use type` keyword; `use` of an alias used as a Type value emits `use function`; Tyhp may call the helper with `\Tyhp\Type` values only (no `Optional<int>()`); always emit; tyhpdef aliases stay inlined. **Appended 2026-09-08:** `callable<..., TReturn>` any-arity facet + inferred generic-bound checking. **Appended 2026-09-09:** packs auto-splice inside `callable<>`; postfix `T...` is PHP variadic / homogeneous (exact vs `extends`); `__CallableParametersSlice`; `__Nullable` pack-preserving; `__` utility unification.
> **Status:** **Design locked.** Implement after 21.8. Further tasks may be appended to this story later.
> **Prerequisites:** Story 11 (alias erasure + TypeAliasMap hints); Story 08 (`typeof` / `is` / alias expansion); `tyhp/core` `\Tyhp\Type` factories; Story 16.5 (Rest / Tuple / Struct / `__CallableReturnType`).
> **Consumers:** every Tyhp program that uses named aliases with `\Tyhp\Type` / `typeof` / `is`; PHP interop that needs a named Type for an alias; Story 15 (interop contract); Story 30 (docs); Layer 3 overlays that pin callback returns without pinning arity (`iterator_apply` in `Ext.SPL.tyhpdef`); Layer 3 `array_map` zip overlays; `tyhp/core` `ClosureExtensions.compose` / `then`.

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [What already exists](#what-already-exists)
- [Research notes](#research-notes)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Emit shape](#emit-shape)
- [Imports (`use` → `use function`)](#imports-use--use-function)
- [`typeof` / `default` grammar](#typeof--default-grammar)
- [Binder and checker](#binder-and-checker)
- [Runtime Type construction](#runtime-type-construction)
- [Phases](#phases)
- [Callable any-arity facet (`callable<..., TReturn>`)](#callable-any-arity-facet-callable-treturn)
- [Utility type unification (`__` global)](#utility-type-unification--global)
- [Packs, postfix `T...`, and `__CallableParametersSlice`](#packs-postfix-t-and-__callableparametersslice)
- [Out of scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Type aliases are compile-time names for type expressions. Story 11 erased the *declaration* and expanded every *hint* to the underlying PHP spelling. That is still correct for hints. It is wrong for runtime `\Tyhp\Type` values: `typeof(UserId)` currently falls through to `\Tyhp\Type::mixed()`, and authors must write the aliased body out by hand at every `Type::is` / `typeof` site.

This story emits a **Type factory** per source alias:

| Declaration | PHP artifact |
|---|---|
| `type UserId = int;` (root / namespace) | `function UserId(): \Tyhp\Type` |
| `type Optional<T = mixed> = T\|null;` | `function Optional(?\Tyhp\Type $T = null): \Tyhp\Type` |
| `public type NameType = string;` (class body) | `public static function NameType(): \Tyhp\Type` |

Hints keep using `TypeSpellingHelper` / `TypeAliasMap` expansion. Runtime Type values (`typeof`, `is` / `instanceof` of an alias, `\Tyhp\Type::is` / `isType` / `check` when the Type operand is an alias, `default(Alias)`) call the factory. Nested aliases call inner factories rather than re-expanding the whole tree.

Tyhpdef aliases **do not** emit PHP. `typeof` of a tyhpdef alias inlines `BuildRuntimeTypeExpression` of the body.

**Appended work (same story):**

- `callable<..., TReturn>` any-arity facet and inferred generic `extends` checking. See [Callable any-arity facet](#callable-any-arity-facet-callable-treturn).
- Unify built-in checker utilities onto `__` names in **global** scope; delete the parallel `\Tyhp\Nullable` / `\Tyhp\ReturnType` / … registrations. See [Utility type unification](#utility-type-unification--global).
- Postfix `T...` inside `callable<>` (PHP variadic / homogeneous; packs **auto-splice**), pack-preserving `__Nullable`, and `__CallableParametersSlice`. First overlay consumer: `array_map` zip; also fix `ClosureExtensions.compose` / `then`. See [Packs, postfix `T...`, and Slice](#packs-postfix-t-and-__callableparametersslice).

These are independent of alias factories; they ship in 21.9 because Layer 3 overlays and `tyhp/core` need them now.

---

## Motivation

`\Tyhp\Type::is($value, typeof(T))` and `Json.decode<T>` already work when `T` is a **generic parameter** — Mechanism D / GenericObject stash a `\Tyhp\Type` in a variable. Named aliases have no such value. The only workaround is to spell `\Tyhp\Type::union(...)` (or worse) at every use site, which duplicates the alias body and drifts when the alias changes.

PHP interop currently cannot name an alias at all (`tyhp_0700_typeAliases.md`: “You cannot reference a type alias name in PHP interop code”). After this story, PHP calls `UserId()` / `Optional(\Tyhp\Type::int())` / `UserService::NameType()`.

---

## What already exists

| Surface | Today |
|---|---|
| File-level alias | `TypeAliasSymbol`; bind fills `AliasedType` + `GenericParameters` |
| Class-level alias | `ObjectTypeAliasSymbol`; bind does **not** copy visibility or `GenericParameters`; checker `ExpandTypeAliases` only walks `TypeAliasSymbol` |
| Declaration emit | `EmitItem.Empty`; splitter skips `TyhpTypeAliasAst`; no PHP file |
| Hint emit | `TypeAliasMap` → underlying PHP spelling (Story 10.5: spelled PHP, not `ToString()`). Generic substitution in hints is incomplete vs checker expansion |
| `typeof` grammar | `expr` (`TyhpParser.g4` `internalFunctionsGrammarAddon`). `default` already takes `typeExpr` |
| `typeof` emit | scalars → `Type::int()` etc.; declared class → `fromClassName`; struct → `Type::struct`; generic param → `$__generic_T` / HasGenerics lookup; **alias → `Type::mixed()`** |
| `typeof` checker | Accepts `ITypeExpression`, unbound names that resolve to class/alias/generic, **and** arbitrary expressions whose type resolves (no tests; emit still `mixed()`) |
| Mechanism D | Wrapper + `__tyhpGeneric` binder returning `\Closure` — for callables with a **value** signature that also need type args at runtime |
| `\Tyhp\Type` | `union` / `intersection` / `nullable` / `generic` / `struct` / `fromClassName` / scalar factories. `union`/`intersection` require ≥ 2 members. No `'alias'` kind |
| Imports | `useType` = `function` \| `const` only. Plain `use Foo\Bar` is class-kind. Type aliases live in the class-like symbol index, so `use App\Types\UserId;` already imports an alias for **type** position. Emitter **drops** alias imports as erased (`PHPOutputFile.IsErasedTypeImport`) |
| `use type` | **Does not exist.** `useTypeGrammarAddon` is an empty override hook (`T_NO_GRAMMAR_ADDON_0000`). Never implemented |
| Tests | `TypeAliasEmitterTests` locks “no PHP for the declaration”; Story 11 golden `type_aliases.tyhp` same. No `typeof(Alias)` emit test |

Generic **parameters** on functions/methods are not this story: `typeof(T)` already reads `$__generic_T` / `$this->__tyhpGeneric->resolvedType(...)`.

---

## Research notes

### Why `typeof` is `expr` today — and why it should not be

`nameof` and `variable_exists` take `expr` (they name / test values). `typeof` was added in the same `internalFunctionsGrammarAddon` and copied that shape. The AST comment still says it “returns the type name of a value as a string,” which is `nameof`’s job, not `typeof`’s.

User-facing docs (`tyhp_2700`, `tyhp_0151`) already describe **type names** (`int`, `User`, `T`) and point value inspection at `\Tyhp\Type::of($value)`. There are **no** tests for `typeof($var)`.

`typeof($obj)` in Story 08.5 / 21.6 tables (`__PropertyName<typeof($obj)>`, FCC `TThis` = `typeof($obj)`) is **checker-inference notation**, not user-written syntax. Story 16.5 explicitly deferred user `typeof($x)` in type position.

The `expr` choice actively blocks this story:

- `typeof(int|string)` and `typeof(?int)` are type expressions, not expressions.
- `typeof(Optional<int>)` needs `typeNameGrammarAddon` generic arguments on `typeExpr`. Parsed as `expr`, `<` is a comparison.
- Emit ignores generic-argument addons on `typeof` even for classes (`typeof(Box<int>)` → `fromClassName` only).

**Lock:** `typeof` takes `typeExpr` only, same as `default`. `nameof` / `variable_exists` stay `expr`. `\Tyhp\Type::of($value)` stays the value path. `typeof($x)` becomes a parse error.

### How to import a type alias (no `use type`)

PHP has `use`, `use function`, `use const`. Tyhp adds `use extension` and `global use`. There is no `use type`.

Type aliases are class-likes in the binder (`ChildSymbolIndex`, `PhpUseType.Class` for plain `use`). Importing from another namespace is already:

```tyhp
use App\Types\UserId;
use App\Types\Optional;

function f(UserId $id): void { /* hint expands to int */ }
\Tyhp\Type $t = typeof(UserId);
```

Same-namespace aliases need no import. Class-level aliases are not imported; they are `self\NameType` / `UserService\NameType` in type position and `UserService::NameType()` as a call.

**Do not add `use type`.** The unused `useTypeGrammarAddon` hook is left alone. One Tyhp import syntax (`use`), PHP emit rewrites to `use function` when the factory is referenced by short name (see [Imports](#imports-use--use-function)).

---

## Scope (In / Out)

**In**

- Emit file-level alias factories as functions in the alias’s namespace (`_functions.php` with other namespace functions).
- Emit class-level alias factories as `static function` on the owning class, with the alias’s visibility (`public` / `protected` / `private`).
- Bind class-level alias visibility and generic parameters; expand `ObjectTypeAliasSymbol` in the checker the same way as file-level aliases.
- `typeof` grammar + AST + visitor + checker + emit: `typeExpr` only; walk unions, nullability, generic args, aliases, structs, classes, scalars.
- Lower `typeof(Alias)` / `typeof(Alias<…>)` / `$x is Alias` / `$x instanceof Alias` / `default(Alias)` to factory calls (or `->defaultValue()`).
- Teach `BuildRuntimeTypeExpression` to call alias factories (with substitution) instead of skipping aliases / falling through to `mixed` / `fromClassName(Alias::class)`.
- Import rewrite: alias `use` kept as `use function` when the factory is used; still pruned when the alias is hints-only.
- Binder: file-level alias name collides with a function in the same namespace; keep colliding with a class. Class-level alias already collides with methods (the factory *is* that method).
- Tyhp may call the factory as a function / static method taking `?\Tyhp\Type` arguments.
- Always emit the factory (even if this compilation unit never writes `typeof`).
- Circular-alias diagnostic (docs already claim it; no `MessageCode` today).
- Reverse Story 11 / `TypeAliasEmitterTests` / conformance goldens / `tyhp_0700` “zero runtime cost” / “cannot reference in PHP interop.”
- Story 15 interop note: aliases exist at runtime as functions / static methods returning `\Tyhp\Type`, not as PHP types.
- **`callable<..., TReturn>`** any-arity callable facet (grammar + assignability + generic bounds). First consumer: `iterator_apply` in `runtime/packages/php/_tyhpdef/overlays/Ext.SPL.tyhpdef`.
- After inferring function/method generic arguments, **validate each against its `extends` bound** (today `T extends callable<bool>` is not checked at inferred call sites).
- **Utility unification:** every built-in checker utility is `__Name` in global scope; remove `Tyhp/TyhpLang/Binder/BuiltIn/UtilityTypes.cs` `\Tyhp\…` registrations and the duplicate behaviors they share with `__AsNullable` / `__CallableReturnType` / … . Update tests, emitter erasure, `AIDevGuide/guide/19-utility-types.md`, `docs/content/tyhp_0150_newTypes.md`.
- **Packs and postfix `T...` in `callable<>` only** (Story **27** `new<>` may splice packs and use `T...` later; do not implement `new<>` here). Packs **auto-splice** in `callable<>` type-argument lists. Postfix `T...` on a **non-pack** is PHP variadic (exact type) / homogeneous (`extends`). `__Nullable` preserves packs. `__CallableParametersSlice<TCallable, TStart = 0, TMin = 0>`. Retype `array_map` zip in `Ext.Standard.tyhpdef`; `ClosureExtensions.compose` / `then` are correct once splice lands.

**Out**

- Mechanism D (`__tyhpGeneric` + Closure) for alias factories.
- `use type` / new `PhpUseType` member.
- Tyhpdef alias PHP artifacts.
- An `'alias'` kind on `\Tyhp\Type` (aliases stay structurally transparent).
- Refined / opaque types (`type X = T { guard … }` in `DESIGN_OPEN_QUESTIONS.md`).
- Making `typeof` accept value expressions; `Type::of` already does that.
- Fixing generic substitution in **hint** spelling (`Optional<int> $x` hint quality) except where this story’s checker expansion for object aliases is required for correctness.
- Traits / interfaces declaring type aliases (keep forbidden).
- Callable-signature fidelity on `\Tyhp\Type` (still `Type::callable()` — pre-existing Type API ceiling).

---

## Decisions (locked)

1. **Factory, not Mechanism D.** Mechanism D exists because generic *functions* have a value signature *and* type arguments. An alias factory’s only result is `\Tyhp\Type`. Emit:

   ```php
   function Optional(?\Tyhp\Type $T = null): \Tyhp\Type {
       $T ??= \Tyhp\Type::mixed();
       return \Tyhp\Type::union($T, \Tyhp\Type::null());
   }
   ```

   not `Optional__tyhpGeneric(...)()`. Parameter names match the alias’s generic parameter names. Defaults match the alias’s generic defaults (or `Type::mixed()`). Class-level generic aliases are `public static function Optional(?\Tyhp\Type $T = null): \Tyhp\Type`.

2. **Hints still erase.** `function f(UserId $id)` → `function f(int $id)`. Never use the factory in a PHP type-hint position.

3. **Runtime Type sites use the factory:**
   - `typeof(Alias)` / `typeof(Alias<int>)`
   - `$x is Alias` / `$x instanceof Alias` (including an alias of a class — do not special-case native `instanceof` for the underlying class)
   - `\Tyhp\Type::is` / `isType` / `check` when the Type operand is an alias (via `typeof` lowering)
   - `default(Alias)` → `Alias()->defaultValue()` (generic: `Optional(\Tyhp\Type::int())->defaultValue()`)
   - `$x is User` (a real class) stays native `instanceof`

4. **Tyhp may call the factory. Calls take `\Tyhp\Type` values only — no generic type arguments on `()`.** `<>` is type-position syntax. `typeof(...)` is the bridge from a type to a Type value. A factory call is a PHP function over `\Tyhp\Type` instances (same shape PHP will write).

   | Intent | Write |
   |---|---|
   | The type (hints, annotations) | `UserId`, `Optional<int>` |
   | The Type value, from a type | `typeof(UserId)`, `typeof(Optional<int>)` |
   | The Type value, by calling the factory | `UserId()`, `Optional()`, `Optional(typeof(int))` |

   **`Optional<int>()` is invalid.** It looks like construction / Mechanism D and mixes type-argument syntax into a value call. Checker error: point at `typeof(Optional<int>)` or `Optional(typeof(int))`. Do not accept it as sugar.

5. **Always emit** the factory from `.tyhp` source aliases so PHP can call it without a Tyhp `typeof` in that file. Follow existing function emit for `declare(php=…)` / existence-gating / `#[NoEmit]` if those already apply to the declaration.

6. **Tyhpdef aliases** never emit. `typeof(SomeStubAlias)` inlines `BuildRuntimeTypeExpression` of the body (and of nested tyhpdef aliases). A source alias whose body names a tyhpdef alias inlines that inner Type; it does not call a missing function.

7. **Transparency.** `UserId()` returns the same structure `typeof` would build for the body (`Type::int()`, not a distinct alias identity). `Type::is($n, UserId())` matches ints. Refined types (open design) may later reuse these factories; this story does not add `guard` / `coerce` / an `'alias'` kind.

8. **Name occupancy.** A file-level type alias occupies the Tyhp **function** namespace as well as the type namespace. `type Foo` + `function Foo()` in the same namespace is an error (new binder diagnostic). `type Foo` + `class Foo()` stays an error (existing class-like uniqueness). Class-level: the static method *is* the alias; existing member-map duplicate covers `type NameType` + `function NameType()`.

9. **No `use type` keyword.** Plain `use App\Types\UserId;` stays the import. When emitted PHP references the factory by short name, rewrite that import to `use function App\Types\UserId;`. Hints-only usage still drops the import.

10. **`typeof` / `default` are `typeExpr` only.** See [grammar](#typeof--default-grammar).

11. **Nested aliases call helpers.** `type UserIds = array<UserId>;` → `UserIds()` uses `UserId()` (or FQ `\App\Types\UserId()`) inside `Type::generic('array', ...)`, not a second copy of `Type::int()`.

12. **Class-level `self` / `static`.** A helper on class `C` that aliases `self` / `?self` emits `Type::fromClassName(self::class)` / `Type::nullable(...)`. `typeof(self\NameType)` → `self::NameType()`. `typeof(C\NameType)` → `C::NameType()`. Binder must actually resolve `self\Alias` / `Class\Alias` (today incomplete; this story finishes it enough for emit + check).

---

## Emit shape

### Non-generic file-level

```tyhp
namespace App\Types;
type UserId = int;
type Scalar = int|float|string|bool|null|array;
```

```php
namespace App\Types;

function UserId(): \Tyhp\Type
{
    return \Tyhp\Type::int();
}

function Scalar(): \Tyhp\Type
{
    return \Tyhp\Type::union(
        \Tyhp\Type::int(),
        \Tyhp\Type::float(),
        \Tyhp\Type::string(),
        \Tyhp\Type::bool(),
        \Tyhp\Type::null(),
        \Tyhp\Type::array(),
    );
}
```

Single-member aliases must not call `Type::union` (it throws if `< 2` members). Prefer `Type::nullable($t)` for `T|null` / `?T` when that matches the body.

### Generic file-level

```tyhp
type Optional<T = mixed> = T|null;
type Map<TKey, TValue> = array<TKey, TValue>;
```

```php
function Optional(?\Tyhp\Type $T = null): \Tyhp\Type
{
    $T ??= \Tyhp\Type::mixed();
    return \Tyhp\Type::union($T, \Tyhp\Type::null());
}

function Map(?\Tyhp\Type $TKey = null, ?\Tyhp\Type $TValue = null): \Tyhp\Type
{
    $TKey ??= \Tyhp\Type::mixed();
    $TValue ??= \Tyhp\Type::mixed();
    return \Tyhp\Type::generic('array', new \Tyhp\NamedType('TKey', $TKey), new \Tyhp\NamedType('TValue', $TValue));
}
```

`typeof(Optional<int>)` → `Optional(\Tyhp\Type::int())`. `typeof(Optional)` → `Optional()` (default). `typeof(Map<string, UserId>)` → `Map(\Tyhp\Type::string(), UserId())`.

### Class-level

```tyhp
class UserService {
    public type UserIdType = int;
    private type Row<T> = array<string, T>;
}
```

```php
class UserService {
    public static function UserIdType(): \Tyhp\Type
    {
        return \Tyhp\Type::int();
    }

    private static function Row(?\Tyhp\Type $T = null): \Tyhp\Type
    {
        $T ??= \Tyhp\Type::mixed();
        return \Tyhp\Type::generic(
            'array',
            new \Tyhp\NamedType('TKey', \Tyhp\Type::string()),
            new \Tyhp\NamedType('TValue', $T),
        );
    }
}
```

### Call sites (same file / imported short name)

| Tyhp | PHP |
|---|---|
| `typeof(UserId)` | `UserId()` |
| `typeof(Optional<int>)` | `Optional(\Tyhp\Type::int())` |
| `Optional(typeof(int))` | `Optional(\Tyhp\Type::int())` |
| `$x is UserId` | `\Tyhp\Type::is($x, UserId())` |
| `default(UserId)` | `UserId()->defaultValue()` |
| `UserId()` | `UserId()` |
| `typeof(self\UserIdType)` | `self::UserIdType()` |
| `Optional<int>()` | **error** (not a generic function call) |

Unimported FQ names use `\App\Types\UserId()`. Require `tyhp/core` whenever a factory or `typeof`/`is` lowering needs `\Tyhp\Type` (same as Mechanism D / existing typeof).

Splitter: stop treating `TyhpTypeAliasAst` as “no file”; file-level aliases are namespace functions (`AddNamespaceFunction`). Class-level aliases emit inside the class (not Empty in `EmitClassMember`).

---

## Imports (`use` → `use function`)

Tyhp source keeps `use App\Types\UserId;` (class-kind import of a type alias).

| How the alias is used in this file | PHP header |
|---|---|
| Type hints / annotations only | Drop the import (current prune) |
| Factory referenced by short name (`UserId()`, `typeof(UserId)`, `$x is UserId`, …) | `use function App\Types\UserId;` |
| Only FQ `\App\Types\UserId()` in the body | Drop (FQ call does not need `use`) |

Group `use App\Types\{ User, UserId }`: split on emit — `User` stays `use` (class); `UserId` becomes `use function` if the factory is used. Mixed group already exists for `function` / `const` members; reuse that split.

Class-level helpers are static methods. PHP has no `use function ClassName\method` for that; keep `use App\UserService;` as a class import when `UserService` is referenced.

Calling `UserId()` in Tyhp: function resolution must treat a **class-kind import whose target is `TypeAliasSymbol`** as the factory (same short name). Do not require a second `use function` in Tyhp source.

---

## `typeof` / `default` grammar

Change `internalFunctionsGrammarAddon`:

```
| T_TYHP_TYPEOF T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
```

Visitor: `VisitTypeExpr`, not `VisitExpr`. `TyhpTypeofAst` holds `ITypeExpression` (update the stale “type name of a value as a string” comment). Checker `CompileTimeRule.CheckTypeof`: resolve the type annotation; drop the “arbitrary expression type” fallback. Flag Mechanism D / GenericObject when the typeExpr names those generic parameters (same as today for bare `T`).

`default` is already `typeExpr`; keep it. Align `default(Alias)` emit with `Alias()->defaultValue()` so alias defaults and generic-parameter defaults share `\Tyhp\Type::defaultValue()`.

Regen parser artifacts per `AIDevGuide/REGEN.md`.

---

## Binder and checker

- **File-level:** when adding `TypeAliasSymbol`, also occupy the function index (or equivalent duplicate check against `FunctionDeclarationSymbol`) so `function UserId` cannot coexist. Diagnostic in the 3000s binder band (`MessageCode.cs` + both `.resx`).
- **Class-level:** copy modifiers onto `ObjectTypeAliasSymbol`; `PopulateGenericParameters` like file-level; `NameResolver.GenericAliasContext` must include object aliases so `T` inside `type Row<T> = …` resolves. `TypeComparer.ExpandTypeAliases` handles `ObjectTypeAliasSymbol` with substitution.
- **`self\Alias` / `Class\Alias`:** resolve to the object alias; checker + emit use that symbol.
- **Circular aliases:** error on cycles (today expand returns the alias / spelling uses `mixed`). Do not emit a recursive factory.
- **Constraints:** `type EntityList<T extends Entity>` — checker already rejects bad type args at use sites; the PHP factory does not re-check constraints at runtime.
- **Reserved PHP function names:** if the alias name cannot be a PHP function (`echo`, `list`, `empty`, `int`, …), error at bind/check rather than emitting invalid PHP. Class-level static methods may use some names that free functions cannot; follow PHP’s method vs function rules.
- **Call expression `UserId()` / `Optional(typeof(int))`:** resolve to the alias factory; type is `\Tyhp\Type`. Arguments are `\Tyhp\Type` values; arity = number of alias generic params, all optional when defaulted. **Reject type arguments on the call** (`Optional<int>()`): the factory is not a generic function. Point at `typeof(Optional<int>)` or `Optional(typeof(int))`.

Tyhpdef aliases remain type-only (no function occupancy, no PHP).

---

## Runtime Type construction

Reuse `BuildRuntimeTypeExpression` (`TyhpEmitter.Generics.cs`) for factory bodies and for inlined tyhpdef aliases. Add an alias arm:

- Source alias → factory call (`UserId()`, `Optional($T)`, `self::NameType()`), substituting generic params with the factory’s `\Tyhp\Type` parameters inside the factory body.
- Tyhpdef alias → expand body (no call).
- Inside a factory body, the alias’s own generic params are the `$T` parameters, not `typeof(T)` / HasGenerics.

Callable aliases: `Type::callable()` (current Type API). Template-string aliases: `Type::string()` (they erase to `string`). Struct aliases: `Type::struct(...)`.

---

## Phases

### Phase 1 — Grammar + AST + bind gaps

- `typeof` → `typeExpr`; regen parser; `TyhpTypeofAst` holds `ITypeExpression`.
- Bind object-alias visibility + generics; resolve `self\Alias` / `Class\Alias`.
- Duplicate function-vs-alias; circular alias; reserved names.
- Tests: parse `typeof(int|string)`, `typeof(Optional<int>)`, `typeof(?User)`; reject `typeof($x)`; bind class-level `type Foo<T>`.

### Phase 2 — Emit factories

Done.

- Stop `EmitItem.Empty` / splitter skip for source `TyhpTypeAliasAst`.
- Emit namespace functions + class static methods; `_functions.php` placement; `tyhp/core` require.
- Nested alias calls; `union` ≥ 2; nullable; generic defaults.
- Tests replace `TypeAliasEmitterTests` erasure assertions.

### Phase 3 — `typeof` / `is` / `default` / `BuildRuntimeTypeExpression`

- Alias arms; generic args; class-level `self::`.
- `$x is Alias` → `Type::is($x, Alias())`.
- `default(Alias)` → `Alias()->defaultValue()`.
- Tyhpdef aliases stay inlined.
- Checker: drop expression-typeof fallback; `typeof` of aliases is not `mixed`.

### Phase 4 — Imports + Tyhp-callable factories

- `IsErasedTypeImport` no longer drops an alias import when the factory is used; rewrite to `use function`; split mixed groups.
- Function resolution of `UserId()` / `UserService::NameType()` / `Optional(typeof(int))`.
- Reject `Optional<int>()` (type arguments on a factory call).
- Conformance goldens: Story 11 `type_aliases` plus new `tests/conformance/story21_9/`.

### Phase 5 — Docs + interop + Story 11 reversal

- `docs/content/tyhp_0700_typeAliases.md`, `tyhp_0350_useStatements.md`, `tyhp_2700_compileTimeConstructs.md`, `tyhp_0151_newFunctions.md`, `faq_general.md`, Story 15 interop contract: aliases are Type factories; hints still erase; import is `use` in Tyhp and `use function` in PHP.
- **Call vs type vs `typeof` (Decision 4) must be explicit in user docs** (`tyhp_0700` at minimum; `tyhp_2700` / `tyhp_0151` for `typeof`). State the three rows: type position (`UserId`, `Optional<int>`), `typeof(...)` as the way to write generics when you want a Type value (`typeof(Optional<int>)`), factory calls as `\Tyhp\Type` value arguments only (`UserId()`, `Optional(typeof(int))`). Show `Optional<int>()` as a don't / error, not as an alternative. PHP examples use `Optional(\Tyhp\Type::int())`, matching the Tyhp factory call — not `<int>()`.
- Emitter / binder technical guides.
- Do not mention `use type`.

### Phase 6 — `callable<..., TReturn>` + inferred generic bounds

See [Callable any-arity facet](#callable-any-arity-facet-callable-treturn). Grammar, checker assignability, bound checking on inferred type arguments, drop the 0-parameter-target return-only loophole, docs, tests. The SPL Layer 3 overlay already uses the syntax; this phase makes it parse and check.

### Phase 7 — Utility type unification (`__` global)

See [Utility type unification](#utility-type-unification--global). One registration site (extend `StructUtilityTypes` / existing `__` registrars; delete `UtilityTypes.PopulateGlobal` `\Tyhp` namespace block). Same `UtilityBehavior` where two names already share a resolver. Update checker display names, emitter `TypeSpellingHelper`, tests (`StructUtilityEmitterTests` `\Tyhp\ReturnType` / `\Tyhp\Parameters`), user docs.

### Phase 8 — Packs, postfix `T...`, Slice, `array_map` / `compose`

See [Packs, postfix `T...`, and Slice](#packs-postfix-t-and-__callableparametersslice). Pack kind; auto-splice in `callable<>`; postfix `T...` on non-packs (exact vs `extends`); `__Nullable` maps packs; Slice; inference; Layer 3 `array_map`. Depends on Phase 6 (bare `...` wildcard vs postfix `T...`) and Phase 7 (`__Nullable` is the mapped name).

---

## Callable any-arity facet (`callable<..., TReturn>`)

Layer 3 needs a way to say “this callback may take any parameters, but it must return `bool`” without collapsing that to `callable<bool>` (which is **zero parameters**, return-last).

`iterator_apply` is the first consumer (`runtime/packages/php/_tyhpdef/overlays/Ext.SPL.tyhpdef`). PHP forwards `$args` as extra callback parameters; the callback must return a truthy/`bool` value to continue. Pinning arity on the overlay is wrong; pinning only the return is right.

### Why not `T extends callable<bool>`

| Spelling | Meaning today |
|---|---|
| `callable<bool>` | Zero parameters, returns `bool`. Same as `callable(): bool`. |
| `callable<int, bool>` | One `int` parameter, returns `bool`. |
| `T extends callable<bool>` | Looks like “any args, returns bool.” It is not. It is “subtype of the 0-arg facet.” |

Worse: `AreCallableTypesCompatible` currently treats a **0-parameter target** as return-only — it ignores source parameters. That makes `callable<int, bool>` assignable to `callable<bool>`, which is call-unsafe (PHP 8 `ArgumentCountError` on extra required args). Do **not** document or overlay that loophole as the pin.

Also: inferred `T` is **not** checked against `extends` today (`TryInferGenericBindings` never calls `ValidateUserConstraint`). A typed `callable<int, int>` passed where `T extends callable<bool>` does not error. A **parameter** typed `callable<bool> $cb` does (TYHP4010). Bounds must be real after this phase.

`T & callable<bool>` is illegal (TYHP4057 / TYHP4061). Do not use intersection-with-callable as a workaround.

### Locked spelling

`callable<..., TReturn>` — ellipsis is a **wildcard parameter list**, not a rest pack and not a type argument to other generics.

| Write | Meaning |
|---|---|
| `callable<bool>` | Zero parameters, returns `bool`. Unchanged. |
| `callable<int, bool>` | One `int`, returns `bool`. Unchanged. |
| `callable<..., bool>` | Any parameter list. Return <: `bool` (`true` ok; `int` / `mixed` not). |
| `callable<..., TReturn>` | Same, with an open return. |

**Grammar.** `tyhpGenericTypeArgument` is `typeExpr` today. Add `T_ELLIPSIS` as an alternative (`...` already exists for FCC / variadics). Regen per `AIDevGuide/REGEN.md`.

**Valid only as** the first of exactly two arguments on the built-in `callable` type: `callable<..., TReturn>`. Reject:

- `callable<...>` (missing return)
- `callable<int, ..., bool>` (ellipsis is not a rest slot among known params)
- `callable<..., bool, int>` (ellipsis is not “and then more type args”)
- `array<..., int>`, `\Closure<..., bool>`, user generics with `...`

**AST / checked type.** `CallableCheckedType` needs an any-arity flag (empty `ParameterTypes` already means 0-arg — do not reuse that). Equality includes the flag. Emit / PHP hints still erase to `callable`.

**Assignability.**

- Concrete callable `S` (facet, intersection of arity siblings, function/closure symbol) is assignable to `callable<..., R>` iff every applicable return <: `R`. Parameter lists are ignored.
- `callable<..., R>` is assignable to `callable<..., R2>` iff `R` <: `R2`.
- `callable<..., R>` is **not** assignable to a known-arity facet (`callable<bool>`, `callable<int, bool>`, …). Unknown arity cannot satisfy a known one.
- After this lands, **drop** the 0-parameter-target special case in `AreCallableTypesCompatible`. `callable<int, bool>` is not assignable to `callable<bool>`. A function with all-default parameters still assigns via its 0-arg arity sibling, which is the correct model.

**Not directly invokable.** A value whose type is `callable<..., R>` (or `\Closure<callable<..., R>>` used as a call) cannot be called: arity is unknown. Pass it, store it, or recover a concrete `TCallable` via inference. Pick a checker diagnostic in the 4000s band (`MessageCode.cs` + both `.resx`); do not invent a code in this plan.

**Use as a generic bound only.** `callable<..., R>` is not a value type (unknown arity, not invokable). Write `TCallable extends callable<..., bool>` and keep `TCallable $callback`. A **direct** `callable<..., bool> $callback` parameter is a checker error (same 4000s band as calling an any-arity value, or a dedicated code — pick in `MessageCode.cs`).

```tyhp
function iterator_apply<TKey, TValue, TCallable extends callable<..., bool>>(
    \Traversable<TKey, TValue> $iterator,
    TCallable $callback,
    __CallableParametersTuple<TCallable> $args
): int;
```

`TCallable` stays the inferred concrete callable. `__CallableParametersTuple` / `Struct` (Story 16.5) then see named/positional params.

**Inferred bound checking.** After `TryInferGenericBindings` succeeds, run `ValidateUserConstraint` (or equivalent) on each inferred type argument against its `extends` bound — same check as explicitly written `foo<Bad>()`. Reuse `CheckerGenericConstraintNotSatisfied` (4035) unless a more specific existing code already covers call-site inference. Do not add a second constraint system.

**Utilities.** `__CallableReturnType<callable<..., bool>>` is `bool`. `__CallableParametersTuple` / `Struct` of the **bare** any-arity facet stay empty/unavailable (no names, no arity), same as an untyped `callable`. They become useful when the type argument is a **concrete** `TCallable` that satisfied the bound.

**Docs (user-facing, Phase 5/6).** Teach `callable<..., TReturn>` in `tyhp_0150_newTypes.md` next to the existing return-last `callable<…>` section. Do not mention story numbers. Overlay comments describe the PHP contract, not this plan.

**Not this facet.** Prefix `...T` / `...Pack` in the type-argument list (`callable<int, ...T, bool>`). Mixed known+**wildcard** lists (`callable<int, ..., bool>`). `where __CallableReturnType<T> extends bool` as the overlay spelling. **Postfix `T...` (Phase 8)** is a different token: `typeExpr` followed by `...`, last parameter before return, **non-pack** only. If grammar for **bare** `...` as the first `callable` argument proves intractable, fallback `__CallableReturning<TReturn>` is acceptable — but prefer `callable<..., TReturn>`.

The SPL overlay is already written with `callable<..., bool>` plus Tuple and Struct overloads. Until this phase lands it is a parse error; that is expected.

---

## Utility type unification (`__` global)

Built-in checker utilities exist in two places today:

| Registrar | Namespace | Examples |
|---|---|---|
| `Binder/BuiltIn/UtilityTypes.cs` | `\Tyhp` | `Nullable`, `NonNullable`, `ReturnType`, `Parameters`, `Readonly`, `Partial`, `Required`, `Pick`, `Omit`, `Record`, `Exclude`, `Extract`, `Awaited` |
| `Binder/BuiltIn/StructUtilityTypes.cs` (and symbol-name / type-name-algebra registrars) | **global**, `__` prefix | `__AsNullable`, `__AsNotNullable`, `__AsReadOnly`, `__CallableReturnType`, `__CallableParametersRest` / `Tuple` / `Struct`, `__StructKey`, … |

Several pairs already share a resolver (`UtilityTypeResolver`: `Nullable` ≡ `AsNullable`; `ReturnType` ≡ `CallableReturnType`; `Readonly` ≡ `AsReadOnly`). `\Tyhp\Parameters` is **not** Tuple: it collapses to `array<int, union-of-params>`. That weaker shape is unused in product tyhpdefs and must not survive as a second “parameters” utility.

**Lock:** every built-in *magic* / checker utility is a **global `__Name`**. No `\Tyhp\Utility` type symbols. No two names for one behavior.

### Canonical names

| Keep (global) | Remove | Notes |
|---|---|---|
| `__Nullable` | `\Tyhp\Nullable`, `__AsNullable` | Same as today’s `ResolveNullable` (`T\|null`). Phase 8: **pack-preserving** (see below). |
| `__NonNullable` | `\Tyhp\NonNullable`, `__AsNotNullable` | Keep `__AsNotNullable`’s `null` → `void` behavior, not a weaker strip. |
| `__AsReadOnly` | `\Tyhp\Readonly` | Already documented in `tyhp_0150`. |
| `__CallableReturnType` | `\Tyhp\ReturnType` | Same resolver. |
| `__CallableParametersTuple` | `\Tyhp\Parameters` | Do not keep the union-array expansion. |
| `__Partial` | `\Tyhp\Partial` | TS Partial on object/struct. **Not** `__StructPartial`. |
| `__Required` | `\Tyhp\Required` | |
| `__Pick` | `\Tyhp\Pick` | |
| `__Omit` | `\Tyhp\Omit` | |
| `__Record` | `\Tyhp\Record` | **Not** `__StructRecord`. |
| `__Exclude` | `\Tyhp\Exclude` | |
| `__Extract` | `\Tyhp\Extract` | |
| `__Awaited` | `\Tyhp\Awaited` | |

`__AsType` / `__AsTypeName` / `__AsNullableTypeName` / `__AsNotNullableTypeName` stay (name algebra, not duplicates of `__Nullable`).

`UtilityTypes.cs` either goes away or only documents that utilities are not under `\Tyhp`. Call `PopulateGlobal` from one `__` registrar. `UtilityBehavior` enum members may keep old identifiers internally; **user-facing spelling** is the table above.

**Migration:** tests and docs that write `\Tyhp\ReturnType<…>` / `\Tyhp\Nullable<…>` / `__AsNullable<…>` switch to the keep column. No compatibility aliases (that would be a duplicate).

**Docs:** `AIDevGuide/guide/19-utility-types.md` currently says `\Tyhp\…` **and** `__…`. After this phase it is `__…` only. `tyhp_0150`: rename `__AsNullable` / `__AsNotNullable` to `__Nullable` / `__NonNullable`. Do not mention story numbers in user docs.

---

## Packs, postfix `T...`, and `__CallableParametersSlice`

### Why

`__CallableParametersRest<T>` fans out only as a **value** variadic (`Rest<T> ...$args`). Today `callable<Rest<T>, R>` is **one** parameter whose type is the Rest wrapper (`ValidateCallableArguments` is return-last and flat). `ClosureExtensions.compose` / `then` already write `callable<Rest<T>, R>` meaning “same parameters as `T`”; that is the intended shape once packs splice. Layer 3 `array_map` needs N heterogeneous extra arrays without a 2–6 overload ladder, nullable **callback** params, and non-nullable **array** elements. PHP also has trailing variadics (`string ...$values`); `callable<>` must be able to spell that, distinct from splice.

### Packs

A **pack** is an ordered list of types, not a union and not `array`. Today Rest stays a `GenericCheckedType` wrapper so call-site unpack can see it. Phase 8 promotes that wrapper to a pack kind:

- `__CallableParametersRest<TCallable>` is a pack of `TCallable`’s parameters (still used as `Rest<T> ...$args` on values).
- Mapped utilities that take a pack **return a pack**: `__Nullable<Rest<T>>` is `?P0, ?P1, …`, not `?(Rest wrapper)` and not `?P0\|?P1`.
- Non-pack `__Nullable<int>` stays `int|null`.

`__NonNullable` maps a pack by stripping `null` from each member. Other mapped utilities (`__Exclude`, …) stay single-type unless a later story needs pack distribution.

**Auto-splice (locked).** A pack is not a parameter type. Inside `callable<>` type-argument lists (and later Story **27** `new<>`), a pack argument **splices** into N ordinary parameters. No extra punctuation.

```tyhp
// Pack = [int, string, bool]
callable<Pack, R>              // callable<int, string, bool, R>
callable<string, Pack, R>      // callable<string, int, string, bool, R>
callable<__Nullable<Pack>, R>  // callable<?int, ?string, ?bool, R>
```

Value position is unchanged: `Rest<T> ...$args` unpacks at the **call**; `Rest<T> $x` (non-variadic) stays one wrapper.

Diagnostics / hover should print the **spliced** `callable<int, string, bool, R>`, not `callable<Rest<…>, R>`.

**`Pack...` is an error.** Suffix `...` is not splice and does not mean “variadic of the union of pack members” (`int|bool...`). Splice is automatic. For a homogeneous variadic of a union, write the union (`(int|bool)...`).

Prefix `...Pack` / `...T` in `callable<>` is also an error (Phase 6 already rejects `callable<int, ..., bool>`; do not add `...Pack` as splice).

### Three `...` forms on `callable<>`

| Spelling | Meaning |
|---|---|
| `callable<..., R>` | **Wildcard.** Unknown arity, return `R`. **`extends` only** (Phase 6). Not a value type; not invokable. |
| `callable<P1, …, Pk, T..., R>` | **Trailing non-pack `T...`.** Prefix `P1…Pk` (k ≥ 0), then `T...`, then return. Exact vs `extends` below. |
| A **pack** as a type argument | **Splice** (no `...`). Never “must be a PHP variadic.” |

`T...` is only valid as the **last parameter before return**. Reject `callable<int...>` (missing return). Reject `callable<string..., int, bool>` (`...` not last before return). Reject postfix `...` on `array<>`, `\Closure<>`, user generics, and value parameters (`Rest<T> ...$args` is the existing value unpack).

Bare `callable<..., R>`: `...` is the **whole** first argument. Distinct parse from `int...`.

`callable<mixed..., R>` is **not** the wildcard: every used slot must **accept** `mixed` (so `fn(int $x)` fails). Keep both.

Story **27** may splice packs and use `T...` on `new<>` (no return-last). Do not implement `new<>` here; keep this grammar on `callable` only.

### Exact type vs `extends` for `T...`

**Exact type** = a type annotation (parameter, return, property), not an `extends` bound.

**Exact** `callable<int, bool, string..., int>` is exactly

```tyhp
function (int $num, bool $flag, string ...$values): int
```

One trailing PHP variadic. Set `LastParameterIsVariadic` on the facet. The value **is** invokable: prefix arguments, then 0+ values the variadic element type accepts. A non-variadic `fn(int $n, bool $f, string $a, string $b)` is **not** this type.

**`extends`** `TCallable extends callable<int, bool, string..., TReturn>`:

1. **No Rest/Slice feeding arguments** into `TCallable` in this function (`call_int_func`): `TCallable` **must** be a PHP variadic. After the prefix, the **last** parameter is `U ...$rest` where the element type **accepts** `string` (contravariance). No extra **required** parameters after the prefix (`fn(bool $f, int $a, int ...$r)` does **not** satisfy `callable<bool, int..., R>` here).
2. **Rest or Slice supplies a concrete argument list** (`call_int_user_func`, `array_map` arrays): `TCallable` must be **invocable with that list**. Fixed arity matching N, trailing defaults, or a trailing variadic that absorbs extras are all fine. Same invocability Rest already implements, plus prefix / `T` slot checks.

This rule applies **only** to homogeneous `T...` in **`extends`**. It does **not** apply to pack splice or to `callable<..., R>`.

Examples:

```tyhp
function call_int_func<TReturn extends mixed|void|never, TCallableShape extends callable<bool, int..., TReturn>>(
    TCallableShape $callback
): __CallableReturnType<TCallableShape>;

function call_int_user_func<TReturn extends mixed|void|never, TCallableShape extends callable<int..., TReturn>>(
    TCallableShape $callback,
    __CallableParametersRest<TCallableShape> ...$values
): __CallableReturnType<TCallableShape>;
```

```tyhp
call_int_func(fn(bool $flag, int ...$vals): int => \count($vals));

call_int_user_func(fn(int ...$vals): int => \count($vals), 1, 2, 3, 4, 5);
call_int_user_func(
    fn(int $a, int $b, int $c, int $d, int $e): int => $a + $b + $c + $d + $e,
    1, 2, 3, 4, 5
);
```

### `__CallableParametersSlice`

```text
__CallableParametersSlice<TCallable extends callable, TStart extends int = 0, TMin extends int = 0>
```

`TStart` / `TMin` are non-negative **int literal** types (same family as `__IndexValueType<T, 0>`).

| Use | Meaning |
|---|---|
| Non-variadic `Slice<T, 0> $x` | Exactly parameter `TStart`. Type is `P_TStart`. Callable must have a parameter at that index (or a trailing PHP variadic that covers it). `TMin` **must not** be written. |
| Variadic `Slice<T, 1> ...$xs` | Parameters from `TStart`, length = number of arguments passed, `N ≥ TMin` (default `TMin = 0`). |
| `array<Slice<T, 0>> $a` | PHP argument is an **array of** `P_0` (explicit wrap). |

No `TConstraint` generic. Homogeneity belongs on `TCallable extends callable<int..., TReturn>`. Containers belong at the use site (`array<Slice<…>>`).

Rest is Slice-from-0 as a single `...$args` covering the remainder. Keep `__CallableParametersRest` as the name for `call_user_func` / `__invoke`; implement Slice so Rest can share unpack logic (`TStart = 0`, open-ended).

**Same-function slices** of one `TCallable`: disjoint, ordered by `TStart`, at most one open rest (`...`), no holes in required parameters. Overlap / hole → checker error (4000s band; pick in `MessageCode.cs`).

**Expansion:** Slice of a bound `TCallable` is `P_i` (or a Rest-like wrapper for a variadic slice so unpack can run). Unbound `TCallable` stays deferred, like Rest.

### Nullability is on the callback, not on Slice

Zip padding (`array_map` with ≥2 arrays) invents `null` at the **call**, not in the arrays. Do **not** make Slice imply `?P_i`. Pack splice of `__Nullable<Rest<TZip>>` is the nullable view:

```tyhp
function array_map<TZip extends callable>(
    callable<__Nullable<__CallableParametersRest<TZip>>, __CallableReturnType<TZip>> $callback,
    array<__CallableParametersSlice<TZip, 0>> $array,
    array<__CallableParametersSlice<TZip, 1, 1>> ...$arrays
): array<int, __CallableReturnType<TZip>>;
```

- `TZip` is the **non-null schema** (element types of the arrays). It is **not** `$callback`’s type.
- `__Nullable<Rest<TZip>>` is a pack and **splices** to `?P0, ?P1, …`. No `...` on the pack.
- `array<Slice<TZip, i>>` stays `array<Pi>` (principal). Argument `array<T>` is OK when `T <: Pi` (so `array<int>` and `array<int|null>` both work if `Pi` is `int`).
- 1-array `array_map` (key-preserving, no padding) stays a **separate** overload: `callable<TValue, TResult>` / `array<TKey, TValue>`. This zip overload requires `TMin = 1` on the extra arrays so it does not steal the 1-array call.
- `null` callback zip stays its own overload.

**Inference (no explicit `array_map<…>(…)` at the call site):**

1. From `$array` / `$arrays` + Slice starts/min, fill `TZip`’s parameter list (element types).
2. Expected callback = splice `__Nullable` of that pack; return still open.
3. Check `$callback` against that type (`fn(?int, ?string)` ok; `fn(int, string)` fail; untyped `fn($a, $b)` infers `?int`, `?string`).
4. Unify `__CallableReturnType<TZip>` with the callback return.

**`int_map` (values are the parameters, no padding):**

```tyhp
function int_map<TReturn extends mixed|void|never, TCallableShape extends callable<int..., TReturn>>(
    TCallableShape $callback,
    __CallableParametersSlice<TCallableShape, 0> $val,
    __CallableParametersSlice<TCallableShape, 1> ...$vals
): TReturn;
```

Here `$callback` **is** `TCallableShape`. `int...` is the `extends` bound (invocable with the Slice argument list, slots accept `int`). `$val` requires at least one int; `callable<int..., TReturn>` alone does not.

### `ClosureExtensions`

```tyhp
): \Closure<callable<__CallableParametersRest<TNextCallable>, TThisReturn>>
```

and the same for `then` (`TThisCallable` / `TNextReturn`). Rest is a pack, so it splices; the source already matches. Inner `function (Rest<T> ...$args)` is already correct.

### Today vs after Phase 8

| Spelling | Today | After |
|---|---|---|
| `Rest<T> ...$args` | Fans out at the call | Unchanged |
| `callable<Rest<T>, R>` | One param, Rest wrapper | **Splice** `T`’s parameters, return `R` |
| `callable<Rest<T>..., R>` | Parse error | **Error** (no `...` on a pack) |
| `callable<int..., R>` | Parse error | Exact: `int ...$values`. `extends`: variadic unless Rest/Slice fixes N |
| `callable<int, bool, string..., R>` | Parse error | Prefix + trailing string variadic (exact / `extends` as above) |
| `callable<..., R>` | Phase 6 wildcard | Unchanged; **`extends` only** |

---

## Out of scope

- Refined / opaque `type X = T { … }`.
- `use type` syntax (see Decision 9).
- Mechanism D wrappers for factories.
- Improving `\Tyhp\Type` so callable / template-string / enum-case aliases round-trip richer than `callable()` / `string()` / class.
- Emitting factories from tyhpdef.
- User-written `typeof($value)` (use `Type::of`).
- Generic-call syntax on alias factories (`Optional<int>()`). Type arguments stay in type position and inside `typeof(...)`.
- Prefix `...T` / `...Pack` in `callable<…>` (`callable<int, ...T, bool>`). Packs splice with no `...`; postfix `T...` is last-before-return and **non-pack** only.
- `Pack...` as “variadic of the union of pack members.”
- `new<T...>` / pack splice on `new<>` (Story **27**).
- A `TConstraint` / type-constructor generic on Slice; wrap with `array<Slice<…>>` instead.
- Slice implying callback-parameter nullability (that is `__Nullable<Rest<T>>` spliced into `callable<>`).
- Direct `callable<..., R> $cb` as a value type (wildcard is `extends` only).
- Keeping `\Tyhp\Parameters` as `array<int, union>` or dual names for one utility.

Further work may be appended to this story as additional phases; keep those additive and behind a new “Last design lock” date.

---

## Cross-Story References

| Story | Relation |
|---|---|
| **11** | Locked “erase alias declarations.” This story reverses declaration emit only; hint expansion stays. Update goldens / `TypeAliasEmitterTests` |
| **08** | `typeof` / `is` / alias expansion; object-alias expansion was missing |
| **10.5** | `TypeAliasMap` PHP spelling for hints — keep |
| **04** | `\Tyhp\Type` / `NamedType` factories |
| **15** | Interop: aliases are functions/static methods returning Type |
| **16.5** | `__CallableParametersTuple` / `Struct` / `Rest` / `__CallableReturnType` consume inferred `TCallable`; Slice generalizes Rest; any-arity bound pins return without arity |
| **21.8** | Predecessor in Tier 2; Layer 3 overlays may *use* aliases — they benefit once factories exist; `array_map` zip overlay is retyped in Phase 8 |
| **27** | `new<>` may splice packs and use postfix `T...`; not implemented here |
| **21.10** | Successor; `extra.tyhp.require` / Track C extern / Composer plugin. 21.10 is the last *implemented* Tier 2 story |
| **22** | Deferred |
| Open design (refined types) | Transparent aliases stay structural; factories must not introduce nominal identity |

---

## Golden Fixtures / Tests (Acceptance)

**Parse / binder**

- [ ] `typeof(int\|string)`, `typeof(Optional<int>)`, `typeof(?int)` parse; `typeof($x)` does not.
- [ ] `type Foo = int` + `function Foo()` → binder error; `type Foo` + `class Foo` still errors.
- [ ] Class-level `public type Row<T> = array<string, T>` has visibility + `GenericParameters`.
- [ ] `self\Alias` / `Class\Alias` resolve.
- [ ] Circular `type A = B; type B = A` errors.
- [ ] `use App\Types\UserId;` binds the alias for type position **and** `UserId()` as the factory.
- [ ] `Optional<int>()` is a checker error (factory is not a generic function); `typeof(Optional<int>)` and `Optional(typeof(int))` succeed.

**Emit**

- [x] `type UserId = int` → `function UserId(): \Tyhp\Type { return \Tyhp\Type::int(); }` in `_functions.php`.
- [ ] Generic `Optional<T = mixed>` as specified above; `typeof(Optional<int>)` → `Optional(\Tyhp\Type::int())`.
- [ ] Class-level public/private static methods; `typeof(self\UserIdType)` → `self::UserIdType()`.
- [x] `function f(UserId $id)` still hints `int $id`.
- [ ] `$x is UserId` → `\Tyhp\Type::is($x, UserId())`; `$x is User` (class) stays `instanceof`.
- [ ] `default(UserId)` → `UserId()->defaultValue()`.
- [x] Nested `UserIds` body calls `UserId()`.
- [ ] `use App\Types\UserId` + `typeof(UserId)` → PHP `use function App\Types\UserId;`.
- [ ] Hints-only `use` of an alias still pruned.
- [ ] Tyhpdef alias: no function; `typeof` inlines the body.
- [x] Always-emitted factory even when the declaring file never writes `typeof`.

**Conformance**

- [x] Update `tests/conformance/story11/type-aliases/` expected PHP.
- [ ] New `tests/conformance/story21_9/` covering the table in [Emit shape](#emit-shape) plus import rewrite.

**Docs**

- [x] `tyhp_0700` no longer says aliases have zero runtime cost / cannot be named from PHP.
- [x] `tyhp_0700` (and `tyhp_2700` / `tyhp_0151` where `typeof` is taught) states Decision 4: `<>` is type position; `typeof(Optional<int>)` is how to spell generics as a Type value; `UserId()` / `Optional(typeof(int))` take `\Tyhp\Type` values only; `Optional<int>()` is shown as invalid.
- [x] `tyhp_2700` / `tyhp_0151`: `typeof` takes a type; examples include aliases and unions; `typeof($x)` is not shown.
- [x] `tyhp_0350`: alias imports stay `use`; emitted PHP is `use function` when the factory is used.

**`callable<..., TReturn>` (Phase 6)**

- [ ] `callable<..., bool>` parses; `callable<bool>` and `callable<int, bool>` still mean 0-arg and 1-arg.
- [ ] `callable<...>`, `callable<int, ..., bool>`, `array<..., int>` are errors.
- [ ] `callable<int, bool>` and `callable<string, int, bool>` assign to `callable<..., bool>`; `callable<int, int>` does not (`int` is not <: `bool`).
- [ ] `callable<..., bool>` does not assign to `callable<bool>` or `callable<int, bool>`.
- [ ] After the 0-arg loophole is removed: `callable<int, bool>` does not assign to `callable<bool>`; a function whose parameters are all defaulted still does (0-arg sibling).
- [ ] Calling a `callable<..., bool>` value is a checker error.
- [ ] Direct `callable<..., bool> $cb` (parameter / property / return) is a checker error; `T extends callable<..., bool>` with `T $cb` is the spelling.
- [ ] Inferred `T extends callable<..., bool>` accepts `callable<int, bool>` / a matching function; rejects `callable<int, int>` (4035 or the call-site equivalent).
- [ ] Explicit `foo<callable<int, int>>()` against `T extends callable<..., bool>` still errors (existing constraint path).
- [ ] `iterator_apply` overlay overloads parse; Tuple/Struct `$args` follow inferred `TCallable`.
- [x] `tyhp_0150`: document `callable<..., TReturn>` next to return-last `callable<…>`.

**Utility unification (Phase 7)**

- [x] `\Tyhp\Nullable` / `\Tyhp\ReturnType` / `\Tyhp\Parameters` / `\Tyhp\Readonly` (and the rest of `UtilityTypes.cs`) do not bind; `__Nullable` / `__CallableReturnType` / `__CallableParametersTuple` / `__AsReadOnly` do.
- [x] `__AsNullable` / `__AsNotNullable` do not bind (renamed to `__Nullable` / `__NonNullable`).
- [x] `__Nullable<int>` is `int|null`; `__NonNullable<int|null>` is `int`; `__NonNullable<null>` is `void`.
- [x] Emitter goldens no longer spell `\Tyhp\ReturnType` / `\Tyhp\Parameters`; Tuple erases to `array`; Rest still `mixed`.
- [x] `AIDevGuide/guide/19-utility-types.md` and `tyhp_0150` use `__` global names only.

**Packs / postfix `T...` / Slice (Phase 8)**

- [ ] `callable<__CallableParametersRest<callable<int, string, bool>>, bool>` is the same arity/shape as `callable<int, string, bool>` (auto-splice), not a 1-arg Rest wrapper.
- [ ] `callable<string, __CallableParametersRest<callable<int, bool>>, mixed>` is `callable<string, int, bool, mixed>`.
- [ ] `callable<__Nullable<__CallableParametersRest<callable<int, string, bool>>>, mixed>` splices as `?int`, `?string`.
- [ ] `callable<Rest<T>..., R>` and `callable<...Rest<T>, R>` are errors.
- [ ] `callable<int..., bool>` parses (exact = `int ...$values`); `callable<int, bool, string..., int>` parses (prefix + trailing variadic); `callable<..., bool>` still any-arity.
- [ ] `callable<int...>`, `callable<string..., int, bool>`, `array<int...>`, `function f(int... $x)` (postfix on a value param) are errors.
- [ ] Exact `callable<string..., int> $cb` accepts `fn(string ...$s): int` and rejects `fn(string $a, string $b): int`; `$cb('a', 'b')` is a legal call.
- [ ] `T extends callable<int..., mixed>` **without** Rest/Slice on `T` accepts `fn(int ...$xs)` and rejects `fn(int $a, int $b)`.
- [ ] `T extends callable<int..., mixed>` **with** `Rest<T> ...$values` accepts both `fn(int ...$xs)` and `fn(int $a, int $b)` when two ints are passed; rejects `fn(int $a, string $b)`.
- [ ] `T extends callable<bool, int..., mixed>` without Rest accepts `fn(bool $f, int ...$xs)` and rejects `fn(bool $f, int $a, int ...$xs)` (required `$a`).
- [ ] `callable<mixed..., mixed>` does not accept `fn(int $x)` (int does not accept mixed); `callable<..., mixed>` as a bound does.
- [ ] `Slice<T, 0> $x` with `Slice<T, 0> ...$rest` on the same function is an overlap error; `Slice<T, 2> ...` after `Slice<T, 0>` without covering 1 is a hole error; `TMin` on a non-variadic Slice is an error.
- [ ] `int_map` example: `int_map(fn(int $a, int $b): int => $a + $b, 1, 2)` is `int`; extra arity vs callback required params errors (4142/4143 family).
- [ ] `array_map` zip: `array_map(fn(?int $a, ?string $b): int => 0, [1], ['x'])` ok; `fn(int $a, string $b)` on two arrays errors; 1-array key-preserving overload still used when there is no extra array (`TMin = 1`).
- [ ] `array_map($fn, $ints, $strings)` infers without an explicit type-argument list on `array_map`.
- [ ] `ClosureExtensions.compose` / `then` (`callable<Rest<T>, R>`) type-check as spliced params; calling the result with `TNextCallable`’s arity type-checks.
- [x] `tyhp_0150`: document packs auto-splicing in `callable<>`; postfix `T...` (exact vs `extends`); Slice; wildcard `extends`-only; no story numbers.
- [x] `Ext.Standard.tyhpdef` zip `array_map` uses the locked signature (verbose 2–6 overloads removed or left only as optional extras — prefer Slice).
