# Domain Services — tyhpdef generation

This guide covers PHP-source harvest (`NativeTyhpdefGenerator`) catalog lookup and
`extern` emit, plus how that sits next to Reflection harvest (`--ext-name`) and
compiled-library `package.tyhpdef` (`tyhp build`). Current behavior only.

## Harvest paths

| Path | Entry | Input | `extern` |
|------|--------|--------|----------|
| Reflection (Layer 1 `--ext-name`) | `PhpDelegationTyhpdefGenerator` | PHP extension Reflection | Does not emit `extern` |
| PHP source (Layer 1 `--package-path` / `--source`) | `NativeTyhpdefGenerator` | Composer autoload or `--source` PHP | Catalog-proven `suggest` / `require-dev` types → `_tyhpdef/externs.tyhpdef` |
| Compiled Tyhp (`tyhp build`) | `TyhpCodeTyhpdefGenerator` | Bound Tyhp public API | Author-only `require-dev` owners → name-only `extern` + `@provided-by` in `package.tyhpdef`. Ambient `extra.tyhp.require` and runtime `require` stay FQNs. |

Reflection harvest (`PhpReflectionMapper`) and PHP-source harvest fill a missing return type as `void` for `__construct` / `__destruct` and `mixed` otherwise. Missing parameter types are `mixed`. Neither invents parameters Reflection or the PHP AST did not report, except `DateExtensionReflectionFixes` after Reflection mapping: DateInterval public handler properties (`$y` / `$m` / `$d` / …) that `ReflectionClass::getProperties()` omits; DatePeriod's three constructors in place of the collapsed mixed 4-arg `__construct`; and `DateTimeZone::getTransitions` / `timezone_transitions_get` `$timestampEnd` spelled `2147483647` rather than arginfo `PHP_INT_MAX` on 64-bit PHP &lt; 8.5.

Reflection attribute arguments keep `ReflectionAttribute::getArguments()` names (`argName` on each dumped value). `#[\Deprecated]` is always spelled with named `message:` / `since:` matching the PHP ctor `(?string $message = null, ?string $since = null)`, so PHP stub `since:`-first named arguments are not flattened into swapped positionals. Unnamed dump args whose first literal looks like a PHP minor (`'8.1'`) are treated as `$since`.

A single `--package-path` run generates one PHP package. It does not walk the
Composer `require` graph or generate prerequisite wrappers. Generate those
wrappers first (the same order `dev-docs/PROPOSED_TYHPDEF_PACKAGES.md` uses).
`runtime/packages/new-composer-lib.sh` scaffolds and generates one package at
a time. When a target `require` names a PHP package / `ext-*` that no catalog
wrapper maps to, PHP-source harvest warns (`CLI_TyhpdefRequiredPhpPackageMissingWrapper`)
and leaves those types unresolved — it does not `extern` a required dependency.

## Catalog (`TyhpdefExternCatalog`)

Built **before** classifying foreign names in this package’s generated IR.

**Roots.** `TyhpdefGenerationOptions.CatalogRoots`:

- `null` — discover the runtime packages root (`TYHP_RUNTIME_SRC`, then a sibling
  `../tyhp-runtime-src/packages`; no in-tree `runtime/packages` requirement) via
  `ComposerJsonService.TryResolveRuntimePackagesRoot`. Cached until that
  environment value or the working directory changes.
- empty list — no catalog (every foreign name is unknown origin).
- one or more directories — only those trees (tests).

Each `composer.json` under a root that has `extra.tyhp.package` as an object
(skipping `vendor` / `node_modules` /
`.git` / `dist`) is a wrapper. Include **and** overlay globs are scanned.

**Index.** Real `class` / `interface` / `enum`, plus top-level `function` /
`const` for compiled-library owner lookup. Lines that declare `extern` are skipped so a
consumer’s own placeholders cannot make it look like the provider. PHP-source harvest
still classifies **types** only (`TryGet`). Brace depth for `namespace { }` uses
code-only text: `{` and `}` inside `/** */`, `/* */`, `//`, and quoted strings are
not namespace delimiters.

**Unique harvest shorts.** `UniqueGlobalAndPsrShortNames` is the last-segment map
PHP-source harvest uses for unqualified names with no `use` import (`KnownShortNames`).
Entries are last-segments from `php` / `ext-*` wrappers (global types such as
`\DateTime`) plus unique `\Psr\*` types (`CacheItemPoolInterface` →
`\Psr\Cache\CacheItemPoolInterface`). A short that maps to more than one FQCN is
omitted (`Builder`). Composer-library class names are not unique globals.

**Wrapper → PHP package.** From that wrapper’s `composer.json` `require`:

- `ext-*` keys if any
- else `vendor/name` keys that are not `tyhp/*` or `tyhpdef/*`
- else `php` (the `tyhpdef/php` builtins package)

`tyhp/*` and `tyhpdef/*` require entries are ignored for this mapping.

When that list is only `php`, each always-present name in the same file’s `extra.tyhp.extensions` (`json`, `hash`, `libxml`, and the rest of `VendorTyhpdefLayout.AlwaysPresentPhpExtensions`) is also registered as `ext-<name>` → that wrapper. `ext-json` therefore resolves to `tyhpdef/php`. Those aliases are lookup-only: type classification still uses the require-derived list (`php`), and optional extensions such as `ext-curl` stay on `tyhpdef/php-ext-*`.

## Classification (`TyhpdefTrackBExternPass`)

Runs after IR extract and stub hole-fill, only for PHP-source harvest.

1. **Target composer.json.** `--package-path` uses that directory’s
   `composer.json`. `--source` classifies only when the tree is package-shaped
   (`composer.json` with `autoload`). That file is the **PHP** package being
   wrapped, not the wrapper’s own `composer.json`.
2. **Omit cascade.** Seed omitted FQCNs from catalog `extern` bases and excluded
   `@internal` types, then cascade through `extends` / `implements`. Native
   `extends` / `implements` and native hints are already FQCN-qualified before
   this pass; `use` imports are already FQCN'd at harvest. Those match as FQCNs.
   Unqualified names (a bare PHPDoc name with no import, left as written) match
   only `\currentNs\Name` in the omitted set — not last-segment equality against
   the whole omitted set. Members / trait `use` / functions / aliases that
   still name an omitted type are dropped (`CLI_TyhpdefOmittedMissingReference`).
   `--include-internal` keeps `@internal` types and their references.
3. **Intra-package qualify** (after omit). Unqualified names that this package
   still declares are rewritten to FQCN (`array<LogRecord>` in a package that
   declares `LogRecord` → `array<\Ns\LogRecord>`). The same rewrite runs on
   generic parameter constraints and defaults (`T extends ClassMetadata<object>`
   in another namespace becomes `T extends \Ns\ClassMetadata<object>`). Lookup:
   `\currentNs\Name` first, else the unique short name in the **surviving**
   generated set (omitted types are already gone, so a PHPDoc `Widget` in
   another namespace is not rewritten to an omitted `\OtherNs\Widget`).
   Names that stay unqualified are **never** auto-`extern`.
4. **Each remaining signature name** (params, returns, properties, `extends`,
   `implements`):

| Condition | Action |
|-----------|--------|
| Unqualified | Leave as written. Never auto-`extern`. |
| Catalog miss | Leave as written. No namespace / `suggest` guess. |
| Catalog hit, providing PHP package / `ext-*` in target **`require`** | No `extern`. Add/keep that `tyhpdef/*` on the **wrapper** as `require-dev` + `extra.tyhp.require` `@dev` (`ComposerJsonService.EnsureTyhpdefRequireDevEntries`; drops a stale `require` pin such as `tyhpdef/php` `0.0.1`; path-repos the local package when the resolved runtime packages root contains it). |
| Catalog hit, providing package in target `suggest` or `require-dev` (not `require`) | Emit `extern` with `// @provided-by: tyhpdef/…`. Do not add a wrapper `require`. |
| Catalog hit, providing package in none of those three | Leave unresolved. |
| Generated type `extends` / `implements` a name that would be `extern` | Do not emit that generated type; warn (`CLI_TyhpdefOmittedExternBase`). Descendants of an omitted type are omitted too (`CLI_TyhpdefOmittedMissingBase`). Members, trait `use`, functions, and aliases that still name an omitted type (including excluded `@internal` types) are dropped (`CLI_TyhpdefOmittedMissingReference`). |

Default file set stays Composer `autoload` only. `--include-dev` is not turned
on for published wrappers.

No Packagist calls. `@provided-by` is only a catalog `tyhpdef/*` name.

## Compiled-library classification (`TyhpCodeTyhpdefExternPass`)

Runs after the public IR is built, from `TyhpCodeTyhpdefGenerator`. Looks up
this library’s `composer.json` next to `package.tyhpdef` (publish directory, or
its parent).

1. Collect qualified names from the IR: signatures, `extends` / `implements`,
   thin-mapping bodies, surviving attributes.
2. Resolve the owning package from a **real** bound declaration (skip
   `IsExtern`) via that file’s `composer.json` `name`, else the catalog.
   Another package’s `extern` does not own the name.
3. Classify against **this** library’s composer graph:

| Owner | Emit |
|-------|------|
| This package | Already a compiled-library declaration |
| In `require` | FQN; do not copy; do not extern |
| In `extra.tyhp.require` | FQN; do not extern |
| In `require-dev` only | Name-only `extern` of the right kind + `// @provided-by: <owner>` in `package.tyhpdef` |
| Unknown | Leave as written |

Compiled-library tyhpdef does not write `_tyhpdef/externs.tyhpdef`. A public type must still
not `extends` / `implements` an extern type (`TYHP3026` / `TYHP3027`).

Owned-type operators emit one compiled PHP backer per operator (`Money::__add`)
plus one `extension operator` mapping per source form. Include-layer occupancy
is name-only (`TYHP8002`), so a second `operator +` overload does not write a
second `__add` method.

`operator convert` is the exception, matching PHP emit (`EmitConvertGroup`):
first-parameter `self`/`static` is convert-to (one instance `__toInt` /
`__toFloat` / `__toString` / … per distinct target, mapping
`$value->__toInt()` with no backer parameter); otherwise convert-from (one
static `__from` whose parameter type unions every from-form, mapping
`self::__from($value)`). Library tyhpdef generation never writes a `convert(...)` method.

Standalone `extension E { operator convert }` is a separate path
(`AddStandaloneExtension` / `MapStandaloneOperatorMapping`). Convert-to maps
to `E__tyhpExtensionBacker::__toInt($value)` (static on the backer, operand
kept) and convert-from to `E__tyhpExtensionBacker::__from($value)` returning
the block target. Brace-bodied forms emit those backer methods. On the backer
class, a signature `self` is the extension, so `MapBackerMethod` spells that
atom as the block target (`\Lib\Money`, `string`) before the stub is written.
The extension mapping itself still says `self`, which means the block target.
Call-site rewrite uses `E::__toInt($expr)` / `E::__from($expr)`, not an
instance method on the target. Class-owned convert stays on
`MapOwnedConvertOperator`.

A multi-target extension (nested `extends<T> Target { }` groups, or a plain
header target plus nested groups) can have two different targets compile a
convert form to the *same* backer method name — `__from` is always shared,
and a to-form's name depends only on its return type, so two targets
converting to the same type collide on `__to{T}` too. `AddStandaloneExtension`
groups every convert form across the whole extension by compiled name first;
`MapStandaloneConvertToBackerMethod` / `MapStandaloneConvertFromBackerMethod`
then union each colliding name's operand/return type across every
contributing target (`UnionSpelledTypes`) and merge each target's own generic
parameters onto the method (`MergeBackerGenerics`), so a nested group's `T`
that the merged return type still mentions stays declared on that method. A
contributing group's own generic can itself be bounded by `self` (`extends<U
extends self> Target`); `MergeBackerGenerics` re-spells that constraint
against *that form's own* target the same way `RewriteBackerSelfTypes` does
for a single form, since two merged forms can belong to two different
targets and a constraint copied in from the second one otherwise still says
`self`.

## `externs.tyhpdef`

**Path:** `{outputDirectory}/externs.tyhpdef` (same directory as the Layer 1
file; `TyhpdefOutputLayout.ExternsFileName`). `extra.tyhp.package` `"include"`
glob `./_tyhpdef/*.tyhpdef` already loads it. Not listed in `"overlay"`.

**Header:** `TyhpdefOutputWriter` default `AUTO-GENERATED, DO NOT EDIT`, plus
`Extern placeholders for optional Composer / extension peers.`

**Regen** always overwrites this file (or deletes it when the run classified
and produced zero `extern`s). Hand `extern` stays in a **different** include
file (for example `_tyhpdef/backers.extern.tyhpdef`). Two `extern` of the same
kind merge in the binder.

Wrapper `composer.json` is found by walking up from the output directory for a
manifest whose `name` starts with `tyhpdef/`.

## PHP-source signature emit

`PhpAstTypeExtractor` / `TyhpdefOutputWriter` keep harvested text legal for the
tyhpdef parser. This is generate emit, not Tyhp/PHP language.

**Types** (`ToTyhpdefType`, also Layer 2 stub harvest): PHPDoc
`callable(...)` / `closure(...)` collapse to `callable`. Psalm/PHPStan
`empty` in a type position becomes `mixed`. Unmatched `()` `{}` `[]` `<>`
become `mixed`. The PHP `empty()` construct is unchanged (`bool`, emitted
`empty(...)`). PHP-source harvest rewrites a PHP `static` type to `self` in
value positions (parameters, properties, `@param`, `@var`, magic `@method`
parameters, `@property`) via `RewriteStaticTypeInValuePosition`. Those slots
use `typeExprWithoutStatic` (the `static` keyword is lexer `T_STATIC`).
Return positions keep `static` (`: static`, magic `@method` returns,
including generic arguments such as `Builder<static>`).

**Defaults** (`SpellLiteral` → `SanitizeDefaultValue`; `FormatParameter`
sanitizes again at write): keep a single-line quoted string, number
(`0x` / `0b` / `0o`), `true` / `false` / `null` / `[]`, or `Class::CONST`.
Drop nowdocs and heredocs before literal checks (the body is raw text, so a
one-word body such as `hi` is not kept as a constant), `/regex/` bodies,
interpolations, multiline text,
leftover `$this`, unquoted source, and unmatched delimiters. Consts and
properties then omit `??` (`const string NAME;`). Parameters that had a PHP
default keep `= <literal>` when it is emitable, otherwise `= null` so the
checker still treats the argument as optional (`DefaultValue is null` would
mean required). That dummy is the same `= null` already written for harvested
implicit-nullable PHP (`string $timezone = null`). The dummy does not widen
the parameter type.

**Attributes** (`FormatAttribute`): if the whole `#[Name(args)]` is unmatched,
write `#[Name]`.

**Declared names** (`FormatDeclaredTypeName`): a `class` / `interface` /
`trait` / `enum` whose last segment is a PHP or tyhpdef reserved word is
written as an enclosing-namespace FQCN (`class \Hamcrest\Core\Is`) so the
lexer takes `T_NAME_FULLY_QUALIFIED` instead of a keyword token. Alias
headers (`class PhpName as TyhpName`) and `extern function` names in a
`namespace {}` use the same prefix. Ordinary identifiers stay unqualified.

**Copied docs** (`CopyDocComment`): each summary, description, and tag line
replaces `*/` with `* /` so the harvested `/** */` cannot close early.

## Related types

- `TyhpdefExternCatalog` — FQCN → providing `tyhpdef/*` from real declarations
  (types, plus functions/consts for compiled-library owner lookup). Unique harvest
  shorts (`UniqueGlobalAndPsrShortNames`) are `php` / `ext-*` globals plus unique
  `\Psr\*` last-segments.
- `TyhpdefTrackBExternPass` — classify foreign signature names; write or delete
  `externs.tyhpdef`.
- `TyhpCodeTyhpdefExternPass` — compiled-library author-only `extern` into
  `package.tyhpdef`.
- `TyhpCodeTyhpdefGenerator` — bound Tyhp public API → `package.tyhpdef`;
  libraries additive-merge generated `extra.tyhp.package` into publish-directory
  `composer.json` (`include` `./package.tyhpdef`, empty `exclude`/`overlay`,
  `source.tagless` from the project). Applications with `build.generateTyhpdef`
  write `package.tyhpdef` only. The merge adds missing keys and array string
  items; it does not remove or change existing values. Skips `private` and
  `IsInternal` when that flag exists. Skips anonymous `anonClass@` classes (they
  are not PHP types). A `return new class { … }` in a public function or method
  becomes a same-namespace shape alias (`Owner_method_Return`, numeric suffix on
  collision) used as the return type (or unioned with a non-weak declared /
  named-`new` return). If the anonymous class `extends` / `implements` nominal
  types, the alias RHS is those types intersected with `object { … }`. Overlays
  may `omit` the generated alias and last-wins replace the callable return.
  Authored object-shape aliases copy the `object { … }` body (and intersection
  parents). Enums copy
  `ObjectDeclarationSymbol.BackingType` and each case's `ValueExpression`
  into `TyhpdefClassDeclaration.BackingType` / `TyhpdefEnumCase.BackingValue`
  (unbacked enums omit both).
- `NativeTyhpdefGenerator` — PHP-source harvest parse / IR / hole-fill / write. A top-level `if (!function_exists(name)) { function name ... }` is `fallback function`. `if (!defined('NAME')) { define('NAME', ...) }` is `fallback const`. A top-level `if (extension_loaded('name')) return;`, `if (function_exists(...)) return;`, `if (defined(...)) return;`, or `if (PHP_VERSION_ID op N) return;` (including `return require 'file.php'`) sets `declare(ext=…)` / `declare(php=…)` / `fallback` on the declarations that follow, or on the required file. `if (!extension_loaded(...)) return;` drops what follows. Other statements inside `if` are still skipped. Calls the
  extern pass after hole-fill, then fills required type arguments on generic types used
  as `@template` constraint bounds (`ClassMetadata` → `ClassMetadata<object>` from the
  bound parameter's own default, else constraint, else `mixed`; already-written arguments
  such as `ClassMetadata<object>` are kept). `AddClass` keeps the first
  class / interface / trait / enum per FQCN when a later declaration has the
  same body (kind, modifiers, extends/implements, members, flags — not doc
  comments) and warns (`CLI_TyhpdefSkippedIdenticalDuplicate`). Divergent bodies
  are both emitted so include-layer TYHP8002 still fires. The binder duplicate
  check is unchanged. Trait `use` adaptations (`addPaths as private`)
  are copied into the tyhpdef `use` clause. PHPDoc local aliases (`@phpstan-type`,
  `@psalm-type`, `@phan-type`) become tyhpdef structs when the RHS is an array
  or list shape, otherwise `type` aliases. Names scoped to a class / interface /
  trait / enum / function are prefixed with that owner (`Options` on
  `ElasticaHandler` → `ElasticaHandlerOptions`) so reused names do not collide.
  `@phpstan-import-type` / `@psalm-import-type` rewrite local names to the
  source type's emitted name. Class-constant parameter defaults such as
  `Level::Debug` are spelled into the signature. Nowdoc / unquoted /
  multiline / unbalanced const and property defaults omit `??` rather than
  being copied. Parameter defaults that are not a tyhpdef literal still emit
  `= null` so the parameter stays optional (see PHP-source signature emit).
  Functions and methods that `return new class { … }` get a generated object-shape
  alias (`createClock_Return` / `Plugin_parseConstraint_Return`) as the return
  (unioned with other named `new` returns). The anonymous class itself is still
  omitted as a named PHP type (`anonClass@`). Public instance members and
  `__construct` are copied onto the shape; `extends` / `implements` become
  intersection conjuncts. Weak PHP returns (`object` / `mixed` / missing) are
  replaced; a non-weak declared return is kept when every `return` is `new class`.
- `PhpAstTypeExtractor` — PHP AST → tyhpdef types and literals
  (`ToTyhpdefType`, `RewriteStaticTypeInValuePosition`, `SpellLiteral` /
  `SanitizeDefaultValue`, `CopyDocComment`). `PhpTypeNameResolver.ResolveTypeExpression` (used by
  `ExtractTemplates`) qualifies identifiers inside generics / unions /
  intersections (`ClassMetadata<object>` → FQCN from `use` imports, unique
  PHP / `\Psr\*` shorts, or a same-package declared type). Builtins stay
  unqualified. Bare names still use `ResolveName`. Real-AST `extends` /
  `implements` / trait `use` (`ResolveBareName` with `qualifyUnknown: true`)
  prefer a same-package declared FQCN over a unique PHP / `\Psr\*` catalog
  short (`Exception` → `\CurrentNs\Exception`). PHPDoc walks keep catalog-first
  (`qualifyUnknown: false`). A leading-`\` unique global the package does not
  declare stays global. Unknown shorts in a composite type (template parameters
  such as `T`) stay as written.
- `TyhpdefOutputWriter` — `IsExtern` emits `extern class|interface|enum Name;` (that kind must match the originating declaration) or kind-unspecified `extern Name;` when `Kind` is empty / `extern`, with an optional `// @provided-by:` line. `FormatDeclaredTypeName` prefixes a reserved-word short name with the enclosing namespace (`class \Hamcrest\Core\Is`, `class \Hamcrest\Core\Isset as IssetMatcher`, `extern function \Some\Ns\isset` in a `namespace {}`) so the lexer takes `T_NAME_FULLY_QUALIFIED`. `FormatParameter` keeps `= null` when an unsafe default is dropped so the param stays optional; `FormatAttribute` writes `#[Name]` when `#[Name(args)]` is unmatched.
- `ComposerJsonService.TryReadDependencySet` / `EnsureTyhpdefRequireDevEntries` —
  read target require/suggest/require-dev; add missing wrapper `tyhpdef/*`
  keys as `require-dev` + `extra.tyhp.require` `@dev` (not a published pin).
