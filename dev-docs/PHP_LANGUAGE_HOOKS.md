# PHP Language Hooks Contract

Living audit of PHP (and Tyhp) constructs whose meaning depends on a **type**, **magic method**,
or **engine special case**. Checker and emit must implement each row; later optimizer passes
may rewrite them but must preserve the same surface semantics.

**Not a user manual.** User-facing pages describe the supported contract only. This file is the
checklist so we do not drop a hook, and so a later audit has one place to tick.

**Related:** Story 21.6 (`IMPLEMENTATION_PLAN_TODO_STORY_21.6.md`) owns Closure / Fiber /
Generator inference, homogeneous ArrayAccess indexing, `ArrayAccessShape` per-key maps,
`#[\Tyhp\PhpType]`, and the Traversable `implements` rule. Story 21.7
(`IMPLEMENTATION_PLAN_TODO_STORY_21.7.md`) owns ArrayAccess `list()` / named destructuring and
Stringable auto-implement from `__toString`. Story 21.8
(`IMPLEMENTATION_PLAN_TODO_STORY_21.8.md`) owns remaining `tyhpdef/php` Layer 3 overlays
(generics, structs, DateTime operators) plus checker companions (Closure alias bounds,
`__CallableParametersTuple` index, WeakMap append, const-int overload selection). Story 21.11
(`IMPLEMENTATION_PLAN_TODO_STORY_21.11.md`) Workstreams B–E own UnitEnum / BackedEnum
auto-implement, remaining engine-attribute hooks (`Deprecated` / `Override` / `NoDiscard` /
`DelayedTargetValidation`), the `\stdClass` named gate plus `(object)` → `\stdClass`, and
XDebug proxy redaction of `#[\SensitiveParameter]`.
Capability tracker: `CHECKER_GAPS.md`.
Diagnostic codes: `CONVENTIONS.md` / `MessageCode.cs`.

**How to use**

1. Adding checker or emit behavior for a hook — update the row’s **Checker** / **Emit** / **Status**.
2. Discovering a missed hook — add a row here first, then a story or `CHECKER_GAPS` item.
3. Status: **Done** (intended behavior, tested) · **Partial** · **21.6** / **21.7** / **21.8** /
   **21.11** (specified there) · **Open** (specified here, not scheduled) · **N/A**.

---

## 1. Types that dispatch syntax

These Zend engine types (or built-in pseudo-types) change what a construct **does**. Implementing
or being the type is what matters, not calling a method by name (methods are listed when they are
the lowering of the syntax).

| Type | Syntax / construct | PHP rule | Checker today | Emit | Status |
|---|---|---|---|---|---|
| `\Generator` | `yield`, `yield from`; function is a generator; `$g->send` / `getReturn` | Function containing `yield` returns a Generator. Declared return is the Generator, not the body’s `return`. | Yield location 4086/4088/4089; illegal declared return 4087; body infers `<K,V,S,R>` (`GeneratorBodyInference`); `TSend` intersection **4328**. | Native `yield` | **Done** |
| `\Fiber` | `Fiber::suspend`, `start`, `resume`, `throw` | `suspend` is static (running fiber). | Overlay `Fiber<TResume, TCallableShape>`; `TResume` on `resume` argument only; suspend/start/throw returns `mixed\|null`; `getCurrent(): ?\Fiber<mixed, callable>` | Native | **Done** |
| `\Closure`, `callable`, `__invoke` | `$fn()`, `$obj()`, FCC `foo(...)` | Closures are objects; `__invoke` makes an object callable. | Callable facets; `\Closure<TCallableShape, TThis, TScope>` from overlay (`__invoke`); not return-last. Bind **4334**–**4336**. | Native; FCC → Closure | **Done** |
| `iterable` | `foreach`, `yield from`, `...$x` | `array\|Traversable` | `IsIterableType`; spread 4096 | Native | **Partial** (feeds from Traversable generics) |
| `\Traversable` | `foreach`; **not** directly implementable by user classes | Only `\Iterator` and `\IteratorAggregate` extend it in `tyhpdef/php`. User classes must implement one of those two. See [Traversable implements](#traversable-implements). | Foreach via `GenericInheritanceBindings`. User `.tyhp` `implements`/`extends` of Traversable is TYHP4326 (`DeclarationRule`); `.tyhpdef` harvest shells are skipped. | Native `foreach` | **Partial** (foreach) + **Done** (implements rule) |
| `\Iterator` | `foreach`; `current` / `key` / `next` / `rewind` / `valid` | User-land foreach contract | `current(): TValue`, `key(): TKey` in Layer 3 | Native | **Partial** (foreach OK; method bodies are ordinary) |
| `\IteratorAggregate` | `foreach` via `getIterator()` | Must return Traversable | `getIterator(): Traversable<K,V>` in Layer 3 | Native | **Partial** |
| `\ArrayAccess` | `$o[$k]`, `$o[$k]=`, `$o[] =`, `isset`/`empty`/`unset` on offsets; `list()`/`[]` destructuring (positional **and** named keys) | Not foreach (needs Traversable). `empty` may call `offsetGet` after `offsetExists`. Append uses `offsetSet(null, $v)`. PHP `ArrayAccess` params/return are `mixed` (contravariance). Destructuring is `offsetGet` per bound key (**no** `offsetExists`). | Overlay `ArrayAccess<K,V>`; `$o[$k]` is `TValue` / `$k` assignable to `TKey` (`InferArrayAccess` / `InferIndexValue` + `GenericInheritanceBindings.TryGetArrayAccessTypes`). **4093** when not array/string/ArrayAccess. Struct/array `TKey` is **4330**. Shape: `ArrayAccessShape` per-key (`__IndexValueType`), wide key **4331**, append **4332**, unhandled key **4333**. Destructuring uses the same `offsetGet` types; **4094** when the source is not array, string, or ArrayAccess. | Native `[]`; emit `offset*` as PHP `mixed` | **Done** — [ArrayAccess](#arrayaccess) |
| `array` | `$a[$i]`, unpack, foreach, destructuring | Keys `int\|string` | Generic `array<K,V>` reads | Native | **Done** (list this as the baseline for ArrayAccess) |
| `string` | `$s[$i]` (byte/char offset) | Always yields `string` | String-index → `string` | Native | **Done** |
| `\Stringable`, `__toString` | `(string)`, concat, interpolation, `echo`/`print` | PHP auto-implements Stringable if `__toString` exists (classes **and** interfaces) | Assignability / `instanceof` / 4120 treat `__toString` as Stringable. `__toString` must return `string`. `(string)` cast is not a dedicated 4120 site | Native | **Done** (auto-implement) |
| `\Throwable` | `throw`, `catch` | Only throwables | 4039 / 4040 | Native | **Done** |
| `\UnitEnum`, `\BackedEnum` | `Enum::Case`; `cases()`; backed `from` / `tryFrom`; `match`; `\UnitEnum` / `\BackedEnum` type positions | Engine applies `\UnitEnum` to **all** enums and `\BackedEnum` (extends `\UnitEnum`) to **backed** enums. Userland types cannot implement either. Methods are engine-provided (not overridable). Type-check only. See [UnitEnum / BackedEnum](#unitenum--backedenum). | Cases typed as the enum. User `.tyhp` enums auto-implement `\UnitEnum` (`ImplementsOrExtends`); backed also `\BackedEnum`. Overlay `from`/`tryFrom` → `static` / `?static`. User listing → TYHP4337; enum method override → TYHP4338; `.tyhpdef` harvest skipped. | Native | **Done** (auto-implement + listing/override reject). Exhaustiveness not compile-checked (not 21.11). |
| `\WeakReference<T>` | `::create` / `->get()` | Not syntax; typed handle | Layer 2 header + Layer 3 `create`/`get` | Native | **Partial** (Layer 3 header generics out of 21.6) |
| `\WeakMap` | ArrayAccess with **object** keys + foreach | Object keys; `$map[] =` is illegal (no null key). Foreach via IteratorAggregate | Overlay `offsetGet(TKey): TValue`; indexing uses ArrayAccess `TKey`/`TValue` when the class binds them. **21.8:** reject append when `offsetSet`’s key is not `null\|TKey` | Native | **Partial** (21.8 append reject; Layer 3 header generics stay Layer 2) |
| `\stdClass` | `$o->undeclared = …`; `new \stdClass()`; `(object)` cast | Empty instantiable class; not a universal base; no methods / default properties; engine allows undeclared props **without** `#[\AllowDynamicProperties]`; PHP subclasses inherit that. `(object)` is identity on an already-object operand; array/scalar/null convert to stdClass | Named gate: undeclared **writes** only when the receiver is exactly the global engine class `\stdClass` (TYHP4134 otherwise, including `class Bag extends \stdClass`). `#[\AllowDynamicProperties]` omitted — not an opt-in (4126 + 4134). `(object)` is identity on an already-object operand (concrete class, builtin `object`, all-object union); array/scalar/null/`mixed` convert to engine `\stdClass`, not builtin `object`. Undeclared reads Unresolved without a declared property / `__get`. See [stdClass](#stdclass). | Native | **Done** (writes + `(object)`) · **Partial** (undeclared reads) |

**Internal classes with custom handlers** (SimpleXMLElement, ArrayObject, GMP operator objects, …)
are not extra syntax families: they implement the rows above (or extension operator tables). Type
them through tyhpdefs; do not add a second checker path unless the engine hook is not one of these.

---

## 2. Magic methods (syntax without a required interface)

Declaring the method changes `$obj->…` / `clone` / serialize. PHP does not require an interface
except `__toString` ↔ Stringable (engine auto-implements).

| Method | Syntax | Checker today | Emit | Status |
|---|---|---|---|---|
| `__invoke` | `$obj()` | Callable facet; Closure via overlay `__invoke` | Native call | **Done** |
| `__get` / `__set` / `__isset` / `__unset` | `$obj->prop` when undeclared | Arity; missing props may resolve via `__get`. Undeclared **writes** without `__set` are TYHP4134 except `\stdClass` ([stdClass](#stdclass)) | Native | **Partial** (no full property map) |
| `__call` / `__callStatic` | missing method | Arity; `__callStatic` must be static | Native | **Partial** |
| `__clone` | `clone $obj`, `clone($obj)` | Clone-non-object; `__clone` arity 0 | Native | **Partial** |
| `__toString` | string context | See Stringable | Native | **Done** |
| `__construct` / `__destruct` | `new`, GC | Ordinary methods; ctor `return` 4153 | Native | **Done** |
| `__sleep` / `__wakeup` / `__serialize` / `__unserialize` | `serialize` | Arity | Native | **Partial** |
| `__set_state` | `var_export` eval | Arity; must be static | Native | **Partial** |
| `__debugInfo` | `var_dump` | Arity | Native | **Partial** |

Tyhp **property hooks** (Story 20.7) are a separate, typed replacement for a subset of `__get`/`__set`.
They stay on that story; this table is the PHP engine surface.

---

## 3. Function / extension contracts (no new syntax)

Engine or extension special-cases a **call**, not `$x[…]` / `foreach` / `yield`.

| Type | Hook | Status |
|---|---|---|
| `\Countable` | `count($x)` | Function stub; not `$x` syntax. Leave unless we later bind `count()` to Countable. |
| `\JsonSerializable` | `json_encode` | Extension stub. |
| `\Serializable` | legacy `serialize` | Prefer `__serialize`. |
| Session handler interfaces | `session_*` | Stubs only. |

---

## 4. Tyhp-only hooks

| Mechanism | Syntax | Status |
|---|---|---|
| `await` / `Promise` | `await`, `foreach (await …)` | Existing async stories |
| `\Tyhp\Expression` / `PropertyPath` | FCC / `fn` at those parameters | **Done** (`TCallableShape`) |
| `#[\Tyhp\PhpType]` | emit PHP type on a parameter, return, property, or typed constant | **Done** (hint / inherit / reject illegal sites; TYHP4327 / 4329). Class + usages omitted because the class is `#[\Tyhp\NoEmit]`. |
| `#[\Tyhp\NoEmit]` | omit tagged type declarations from PHP; strip usages of those types as attributes | **Done** (`NoEmitAttributeSupport`; `\Tyhp\Php` / `\Tyhp\PhpType` / `\Tyhp\NoEmit` are tagged) |
| `\Tyhp\Contracts\ArrayAccessShape` | `$o[$k]` per struct field | **Done** (listing in `package.tyhpdef`; wide key **4331**; append **4332**; unhandled key **4333**) |
| Operator overloads / convert-to | operators, implicit convert; string via Stringable | Story 09 / 31 Idea 2 |

---

## 5. PHP-engine attributes

Engine attributes that change compile-time validation, how use of a declaration is diagnosed,
or how values appear in stack traces. The Layer 1 class may already exist in `tyhpdef/php`; the
row is the **hook**, not the stub.

`\AllowDynamicProperties` is **not** a row here. Layer 3 Core **`omit`s** the class so other
types cannot opt into undeclared properties that way. `\stdClass` is the exception; see
[stdClass](#stdclass).

| Type | Syntax / construct | PHP rule | Checker today | Emit | Status |
|---|---|---|---|---|---|
| `\DelayedTargetValidation` | `#[\DelayedTargetValidation]` on a declaration. PHP 8.5+; tyhpdef `#[\Tyhp\Php(">=8.5")]`. Global Core class. | Delays **target** validation errors for **internal** attributes on that same declaration from compile time to `ReflectionAttribute::newInstance()`. Does **not** suppress functional validation (e.g. `#[Override]` still errors if the method does not override). Forward compatibility when attributes gain extra valid targets in later PHP versions. | When `output.phpVersion` is ≥ 8.5 and this attribute is on the declaration, skip TYHP4127 TARGET mismatches for Core attributes on that declaration (`AttributeRule.ShouldSkipCoreAttributeTargetMismatch`). Functional checks still run (4129, 4165, 4339, 4500). Below 8.5 the skip is ignored. Userland attributes still target-checked. | Native empty class | **Done** |
| `\Deprecated` | `#[\Deprecated($message, $since)]`. Optional `?string $message`, `?string $since`. PHP 8.4+; tyhpdef `#[\Tyhp\Php(">=8.4")]`. | PHP emits `E_USER_DEPRECATED` when deprecated functionality is used. | Binder sets `IsDeprecated` from `#[\Deprecated]` (in addition to the tyhpdef `deprecated` keyword). `DeprecationRule` TYHP4500 at use sites; literal `$message` in `{1}` (`: ` + text). `$since` is not in the short message. Still warns when `output.phpVersion` is < 8.4. | Native (`message` / `since` ctor) | **Done** |
| `\NoDiscard` | `#[\NoDiscard($message)]`. Optional `?string $message`. PHP 8.5+; tyhpdef `#[\Tyhp\Php(">=8.5")]`. Global Core class. `#[Attribute(6)]` = function + method. | Unused return → warning; optional `$message`. Suppress with `(void)` (PHP 8.5+). On older PHP `(void)` may not exist — `$_` assignment is the portable suppress. Attribute is a runtime no-op on PHP < 8.5. Warning is based on the **called** declaration: interface/abstract `#[\NoDiscard]` does not warn (the invoked method is the implementor/override); an override does not inherit the warning unless it is itself marked; a trait-imported method keeps the attribute (copied as if declared on the using class). | Discarded call / non-final for-list item → TYHP4165 (`CheckerHelpers.ReportNoDiscardIfDiscarded`). `(void)` suppresses. Used return (including `$_ = …`) does not warn. Literal `$message` in `{1}`. Interface and abstract callees do not warn. Overrides do not inherit unless marked. Trait-imported methods keep the attribute. Not gated on `output.phpVersion`. | Native (`message` ctor). `(void)` native ≥ 8.5; stripped below. | **Done** |
| `\Override` | `#[\Override]` on a method or property. PHP 8.3+ methods; PHP 8.5 also properties. tyhpdef `#[\Tyhp\Php(">=8.3")]`. Global Core class. `#[Attribute(12)]` = method + property. | Marks a method or property as intended to override a parent/interface member. Compile-time error if no same-name method/property exists on a parent class or implemented interface. Cannot be used on `__construct()` (exempt from signature checks). | Methods: TYHP4129 when the method does not override a non-private ancestor or interface method (`AttributeRule.ValidateOverride`; parent + interface walk). Trait bodies skipped. Properties legal when `output.phpVersion` is ≥ 8.5 (same-name parent/interface property); below 8.5, TYHP4127 target mismatch. `__construct` always TYHP4339. | Native (empty ctor) | **Done** |
| `\SensitiveParameter`, `\SensitiveParameterValue` | `#[\SensitiveParameter]` on a **parameter**. PHP 8.2+; ungated Core (no `#[\Tyhp\Php]`). `#[Attribute(32)]` = parameter. Companion class `\SensitiveParameterValue` (not itself an attribute). | Marked args are redacted in stack traces. PHP wraps those values in `\SensitiveParameterValue` in traces. That class hides `$value` from `__debugInfo` / accidental exposure; `getValue()` retrieves it. | Attribute class + `TARGET_PARAMETER` exist (`AttributeRule`). No use-site diagnostic (informational). Overlay `SensitiveParameterValue<TValue>` ctor / `getValue()` inference is **Done** (21.8). Compile-time Tyhp does not emit stack traces. XDebug proxy (`StackFrameTranslator` / `VariableTranslator` / `EvalTranslator`) does **not** unwrap `\SensitiveParameterValue` (unlike Decimal display); wrappers are preserved on property trees / `stack_get` / eval. Reconstructed args (Story 23, not built) must use `SensitiveParameterRedaction`. | Native (empty attribute ctor; wrapper ctor / `getValue` / `__debugInfo`) | **Done** |

Compile-time Tyhp does not emit stack traces. Native PHP wraps marked args in `\SensitiveParameterValue`
in `debug_backtrace` / exception traces. Debugger-side redaction is shipped (Workstream E of Story 21.11):
the XDebug proxy does not unwrap `\SensitiveParameterValue` (unlike Decimal display); wrappers are
preserved on property trees / `stack_get` / eval. Reconstructed args (Story 23, not built) must use
`SensitiveParameterRedaction`.

---

## stdClass

PHP: `\stdClass` is a generic empty class. It is instantiable (`new \stdClass()`). `(object)` on
an already-object operand is identity (same instance and class). `(object)` on an array, scalar,
or `null` produces a new stdClass. Several builtins also return it (`json_decode()`,
`mysqli_fetch_object()`, `PDOStatement::fetchObject()`, …). It has no methods and no default
properties. It is **not** a universal base class (PHP has none). Despite not implementing
`__get` / `__set`, the engine allows undeclared properties on it without
`#[\AllowDynamicProperties]`. A user class that `extends \stdClass` inherits that PHP behavior.

Layer 1 (`Ext.Core.tyhpdef`) is `#[\AllowDynamicProperties] class stdClass {}`. There is **no**
Layer 3 `stdClass` overlay. The named write gate does not walk that attribute.

### Locked (Tyhp)

1. **`\stdClass` is the only object type that allows undeclared (dynamic) properties.** Other
   classes — including `class Bag extends \stdClass` — must declare properties or use
   `__get` / `__set`. They cannot opt in with `#[\AllowDynamicProperties]`.
2. **Mechanism:** Layer 3 Core overlay (`runtime/packages/php/_tyhpdef/overlays/Ext.Core.tyhpdef`)
   **`omit`s** `final class AllowDynamicProperties`. The PHP opt-in attribute is not a Tyhp
   symbol (TYHP4126). Magic `__get` / `__set` remain the typed bag path for everyone else.
   `RestrictedFeatureRule` allows undeclared writes only when the receiver type is **exactly**
   the global engine class `\stdClass`.
3. Instantiable. Empty: no methods, no default properties. Not a universal base —
   `extends \stdClass` is ordinary inheritance, not “everything is stdClass”.
4. **`(object)`:** identity when the operand is already an object (concrete class, builtin
   `object`, all-object union) — same type, not re-wrapped as `\stdClass`, and not a license
   for undeclared writes on other types. Array / scalar / `null` / `mixed` (not already an
   object) convert to the resolved engine class `\stdClass`, not builtin `object`. Emit is
   native `(object)`.

### Checker today

**Done** for undeclared writes and `(object)` typing. Undeclared **reads** stay **Partial**
(no property map).

| Piece | PHP | Tyhp today | Status |
|---|---|---|---|
| Undeclared writes on `\stdClass` | Allowed (engine; attribute not required) | Allowed when the receiver type is exactly the global engine class `\stdClass` | **Done** |
| `class Foo extends \stdClass` | Subclasses inherit dynamic properties | TYHP4134; declare properties or use `__get` / `__set` | **Done** (stricter than PHP) |
| `#[\AllowDynamicProperties]` on a user class | Opt-in; effect is inherited by children | Class omitted → TYHP4126. Named gate does not treat the stamp as an opt-in (4126 + 4134) | **Done** |
| Other harvest stamps (`__PHP_Incomplete_Class`, …) | Engine incomplete-object bag | Not exact `\stdClass` → TYHP4134 | **Done** |
| `(object)` already-object operand | Identity (same instance / class) | Identity: operand’s own type (`InferObjectCastType` / `IsDefinitelyObject`) | **Done** |
| `(object)` array / scalar / `null` / `mixed` | Produces `\stdClass` | Resolved engine `\stdClass` (`ObjectDeclarationSymbol`), not builtin `object`. `(object)[]` accepts undeclared writes | **Done** |
| Undeclared **reads** | Dynamic property / notice | No property and no `__get` → `Unresolved` (same as other classes). No property map | **Partial** |
| Instantiable; empty; not a universal base | Yes | Layer 1 empty class; `new \stdClass()`; no overlay | **Done** |

Emit is native `(object)`. Optimizer must not assume every object is stdClass, or that every
`(object)` expression has type `\stdClass`.

---

## Traversable implements

PHP: Traversable is an **engine** interface. Userland classes cannot implement it except **as part of**
`\Iterator` or `\IteratorAggregate`. PHP 8 also rejects listing an interface that is already implied
(`implements \Iterator, \Traversable` → “cannot implement previously implemented interface”).

Reflection harvest still writes `implements \Iterator, \Traversable` on many Core/SPL classes
(`Generator`, `InternalIterator`, SPL iterators). That is stub noise, not a userland pattern.

### Locked (Tyhp)

1. **Only** `\Iterator` and `\IteratorAggregate` (the `tyhpdef/php` interface declarations) may
   **`extends \Traversable`**. A user `interface Foo extends \Traversable` is a **compile error**.
   (PHP allows that form; every class implementor would still need Iterator or IteratorAggregate.
   Tyhp requires the foreach contract to be named explicitly.)
2. **Classes and enums** (user `.tyhp`) must not list `\Traversable` in `implements`. They
   `implements \Iterator` or `implements \IteratorAggregate` (or a subinterface of those). Traversable
   is inherited. Listing Traversable as well is a compile error (matches PHP 8 duplicate-implements).
3. A class that implements a user interface which — illegally under (1) — extended Traversable is
   already rejected at the interface. No third path.
4. **`.tyhpdef` harvest** may keep redundant `implements …, \Traversable` so Layer 1/2 dumps stay
   honest. Harvest output is always a `.tyhpdef` shell (a different AST from a real `.tyhp`
   declaration), so the checker **does not** flag those stubs. `#[\Tyhp\Php]` is an ordinary
   version-gating attribute available on any `.tyhp` declaration — it does not, by itself, exempt a
   real user class/enum/interface/trait from this rule. Overlay comments may still drop Traversable
   from a class header when Iterator is present; not required for correctness.
5. **Traits:** a trait `implements` requirement of Traversable is an error; require Iterator or
   IteratorAggregate instead.

This check is small (`DeclarationRule`) and is implemented (Phase 0).

---

## UnitEnum / BackedEnum

PHP: `\UnitEnum` and `\BackedEnum` are **engine** interfaces. The engine applies `\UnitEnum` to
**every** enumeration and `\BackedEnum` (which `extends \UnitEnum`) to **backed** enumerations
(`string` / `int` backing). They exist for type checks (`instanceof`, parameter / return / property
types). Userland classes, interfaces, and traits cannot implement or extend them. Enumerations
cannot override `cases`, `from`, or `tryFrom` — the engine supplies those implementations.

Reflection harvest writes `implements \UnitEnum` (and `\BackedEnum` when backed) on Core/extension
enums. That is stub noise, same as Traversable on iterators — not a userland pattern.

### Locked (Tyhp)

1. **Every** enum is a `\UnitEnum` without a written `implements`. A backed enum is also a
   `\BackedEnum`. Assignability, `instanceof`, and method lookup (`cases`; backed `from` /
   `tryFrom`) follow from that, not from a listing on the declaration.
2. **Classes, interfaces, and traits** (user `.tyhp`) must not `implements` / `extends`
   `\UnitEnum` or `\BackedEnum`. Only the engine `\BackedEnum` declaration may extend `\UnitEnum`.
3. **Enums** (user `.tyhp`) must not list `\UnitEnum` or `\BackedEnum` in `implements` (PHP 8
   already-implied / duplicate). They must not redeclare `cases` / `from` / `tryFrom`.
4. **`.tyhpdef` harvest** may keep `implements \UnitEnum` / `\BackedEnum` so Layer 1 dumps stay
   honest. Harvest shells are a different AST from a real `.tyhp` declaration, so the checker
   **does not** flag those stubs (same exemption as Traversable). Overlay remaps
   `BackedEnum::from` / `tryFrom` to `static` / `?static` (Layer 3) and `UnitEnum::cases` to
   `array<int, static>` (Layer 2 stub).
5. These interfaces are type-check surface only. Emit is native PHP enums; do not emit a written
   `implements \UnitEnum` / `\BackedEnum`.

### Checker today

**Done** for auto-implement and listing/override reject. Enum cases are typed as the enum. Every
user `.tyhp` enum is `\UnitEnum` via `ImplementsOrExtends` without a written `implements`; a backed
enum is also `\BackedEnum`. Assignability, `instanceof`, and method lookup (`cases`; backed `from` /
`tryFrom`) follow from that. Overlay `from` / `tryFrom` return `static` / `?static`. User `.tyhp`
classes, interfaces, and traits that list `\UnitEnum` / `\BackedEnum`, and user enums that list them,
are TYHP4337; user enums that redeclare `cases` / `from` / `tryFrom` are TYHP4338; `.tyhpdef`
harvest shells are skipped. Exhaustiveness of `match` on enums is not compile-checked.

---

## ArrayAccess

Two modes, both **locked** in Story 21.6. Canonical surface: `IMPLEMENTATION_PLAN_TODO_STORY_21.6.md`.

### PHP syntax this type owns

| Construct | Lowers to |
|---|---|
| `$o[$k]` | `offsetGet($k)` |
| `$o[$k] = $v` | `offsetSet($k, $v)` |
| `$o[] = $v` | `offsetSet(null, $v)` (append; key is `null`) |
| `isset($o[$k])` / `empty($o[$k])` | `offsetExists`; `empty` may also call `offsetGet` |
| `unset($o[$k])` | `offsetUnset($k)` |
| `[$a, $b] = $o` / `list()` / `['k' => $v] = $o` | `offsetGet` per bound key (PHP 7.1+). **No** `offsetExists`. Named keys work. Homogeneous: `TValue` when the key is assignable to `TKey`. Shape: per-key `__IndexValueType`. Else **4094**. |
| `foreach ($o as …)` | **Not ArrayAccess.** Needs `Traversable` / `Iterator` / `IteratorAggregate`. |

By-ref `$r =& $o[$k]` requires `offsetGet` by-ref. Treat as existing magic-method / ref rules; do
not invent a third ArrayAccess mode for it in v1.

### Homogeneous — `ArrayAccess<TKey, TValue>` (21.6)

Layer 2 harvest already has all four `offset*` methods with `TKey`/`TValue` and
`offsetSet(null|TKey, …)` for append. Layer 3 only adds generic defaults and overlays
**`offsetGet(): TValue`** (harvest is `null|TValue`). Do not duplicate the other three methods.

- `$o[$k]` has type `TValue`; `$k` must be assignable to `TKey`.
- `$o[] = $v` requires `null` to be allowed as the offset (already `null\|TKey` on `offsetSet`).
- Receiver is not `array`, `string`, or ArrayAccess → **4093**.
- `TKey` that is a **struct** or **array** is an error in this mode (PHP cannot use arrays as
  offsets; structs erase to arrays). Object keys (`WeakMap`, `SplObjectStorage`) stay `TKey extends object`.
- Implementors’ `offset*` signatures must satisfy the interface after generic substitution
  (ordinary implements checking). Native `offset*` still emit as PHP `mixed`.

Call sites use **interface generics + InferArrayAccess**, not “always mixed”.

**PHP `ArrayAccess` is `mixed`.** Implementing `offsetGet(string $offset): User` fatals against
`offsetGet(mixed $offset)` (contravariant params). Emit native `\ArrayAccess` `offset*` as PHP
`mixed`. `ArrayAccessShape` stamps `#[\Tyhp\PhpType('mixed')]`; implementors inherit it.

### Shape — `ArrayAccessShape<TStruct extends struct>` (Done)

Homogeneous `TValue` cannot express `$o['host']` is `string` and `$o['port']` is `int`. A struct
already is that key→type map. Do **not** spell this as `ArrayAccess<MyStruct>` (that is `TKey` +
default `TValue`).

`.tyhp` interfaces cannot overload. Per-key typing is the method generic
`TKey extends __IndexKeys<T>` → `__IndexValueType<T, TKey>`. Canonical source:
`runtime/packages/core/tyhp_src/Contracts/ArrayAccessShape.tyhp`, listed in `package.tyhpdef`.

```tyhp
interface ArrayAccessShape<TStruct extends struct>
    extends \ArrayAccess<__IndexKeys<TStruct>, __IndexValueTypes<TStruct>>
{
    public function offsetExists(
        #[\Tyhp\PhpType('mixed')] __IndexKeys<TStruct> $offset,
    ): bool;

    #[\Tyhp\PhpType('mixed')]
    public function offsetGet<TKey extends __IndexKeys<TStruct>>(
        #[\Tyhp\PhpType('mixed')] TKey $offset,
    ): __IndexValueType<TStruct, TKey>;

    public function offsetSet<TKey extends __IndexKeys<TStruct>>(
        #[\Tyhp\PhpType('mixed')] TKey $offset,
        #[\Tyhp\PhpType('mixed')] __IndexValueType<TStruct, TKey> $value,
    ): void;

    public function offsetUnset(
        #[\Tyhp\PhpType('mixed')] __IndexKeys<TStruct> $offset,
    ): void;
}
```

`$o['host']` is `offsetGet` with `TKey = 'host'`. Wide `string` / `int` / `mixed` keys at the
call site → dedicated diagnostic (narrow or `as`). `$o[] =` → error. `offsetExists` on a schema
key is **`bool`** (sparse), not `true`. `throw` / `never` does **not** cover a key.

Checker obligations:

| Piece | Rule |
|---|---|
| **Keys** | `__IndexKeys<T>` = union of struct array keys as **literal types** (names + `as` aliases). |
| **Value lookup** | `__IndexValueType<T, K>` distributes over `K`. `__IndexValueTypes<T>` = union of all field types (envelope `TValue`). |
| **Use-site `[]`** | Same as `offsetGet` / `offsetSet`. Wide keys → dedicated diagnostic. |
| **Append** | Error in shape mode. |
| **Implementor body** | Finite `TKey`: instantiate once per inhabitant `K`; `$offset` assumed `K`; reachable **value** returns must be `__IndexValueType<T, K>`. Coverage = a reachable path returns a value of that type. `match` optional. Infinite constraints: one envelope check. |
| **`$o as ArrayAccess<…>`** | Envelope only (`Keys`, `ValueUnion`). Precision requires the `ArrayAccessShape` type. |
| **Assignability** | `TStruct` invariant. Envelope via `extends`. No extra widening to `ArrayAccess<string, mixed>`. |
| **Emit** | Inherit `PhpType('mixed')` onto implementations. |

Dynamic bags stay homogeneous `ArrayAccess<string, mixed>`. Shape is a **closed** key set.
`list()` / named destructuring on ArrayAccess uses the same `offsetGet` types as `$o[$k]` (Story 21.7).

---

## Emit / optimize

Unless a row says otherwise, emit is **PHP-native** (no rewrite). Optimizer later may:

- Devirtualize `$o[$k]` to `offsetGet` only when it can prove the same semantics (including
  `empty`/`isset` and append).
- Skip ArrayAccess for values proven to be arrays.

Do not optimize by assuming ArrayAccess is foreachable or that `offsetGet` is pure.

---

## Audit checklist (tick when implementing or reviewing)

- [x] Generator body inference + declared-return shapes (21.6)
- [x] Fiber `TResume` only on `resume` (21.6)
- [x] Closure not return-last; `__invoke` facet (21.6)
- [x] Traversable `extends`/`implements` rule (21.6 Phase 0)
- [x] Enum types auto-implement `\UnitEnum`; backed enums also `\BackedEnum` (assignability, `instanceof`, `cases` / `from` / `tryFrom`) — **21.11 B**
- [x] User `.tyhp` classes/interfaces/traits cannot implement or extend `\UnitEnum` / `\BackedEnum`; enums cannot list or override them — **21.11 B**
- [x] Homogeneous ArrayAccess `$o[$k]` / assign / append / 4093 (21.6)
- [x] `#[\Tyhp\PhpType]` on PHP type-declaration sites (hint, inherit, reject) (21.6 Phase 6d)
- [x] `#[\Tyhp\NoEmit]` declaration + attribute-usage erasure (generalizes Php / PhpType emit strip)
- [x] ArrayAccessShape per-key maps + inherit mixed emit (21.6)
- [x] `list()` / named `[]` destructuring on ArrayAccess (21.7)
- [x] Stringable auto-implement from `__toString` (21.7)
- [ ] SplPriorityQueue `<TPriority, TValue>` + foreach `Iterator<int, TValue>` (21.8)
- [ ] DateTimeInterface native comparisons; DateTime `+`/`-` mapped to `add`/`sub`/`diff` (21.8)
- [ ] Closure alias-bound expansion; `__CallableParametersTuple` index; WeakMap `$map[] =` reject; const-int overload selection (21.8)
- [x] `implements Traversable` tests include tyhpdef stub exemption
- [x] `#[\DelayedTargetValidation]` (≥ 8.5) skips Core-attribute TARGET (TYHP4127) on that declaration; functional checks still run — **21.11 C**
- [x] `#[\Deprecated]` use-site warning TYHP4500 (compile-time analogue of `E_USER_DEPRECATED`); literal `$message` in `{1}`; still warns when phpVersion < 8.4 — **21.11 C**
- [x] `#[\NoDiscard]` unused-return warning TYHP4165; `(void)` suppresses
- [x] `#[\NoDiscard]` warning follows called-declaration PHP rules (interface/abstract do not warn; overrides do not inherit unless marked; trait import keeps the attribute); optional `$message` in the diagnostic — **21.11 C**
- [x] `#[\Override]` on a method that does not override a parent/interface method → TYHP4129
- [x] `#[\Override]` on properties (PHP 8.5) and reject on `__construct()` (TYHP4339) — **21.11 C**
- [x] `#[\SensitiveParameter]` / `\SensitiveParameterValue` Core stubs; `SensitiveParameterValue<TValue>` overlay
- [x] XDebug proxy redacts `#[\SensitiveParameter]` args in traces (wrap as `\SensitiveParameterValue` the same way PHP does) — **21.11 E**
- [x] `\stdClass` is the only type that allows undeclared properties; `#[\AllowDynamicProperties]` stays omitted — **21.11 D**
- [x] `class Foo extends \stdClass` undeclared writes are TYHP4134 (exact `\stdClass` only) — **21.11 D**
- [x] `(object)` is identity on already-object operands; array / scalar / `null` / `mixed` convert to engine `\stdClass` — **21.11 D**
