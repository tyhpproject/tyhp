# Tyhp quick reference (for PHP devs / AI agents)

**Tyhp = PHP 8.x plus static typing and a few additions.** You already know most of it — below are
only the deltas, with analogies to languages you know.

**How to use these docs:** this file is the index. Each entry ends with `→` pointing to the file that
explains it in full. **Before writing a feature marked `→`, open that file** — don't reconstruct the
syntax from the analogy alone. For a full listing of what's available, read `guide/00-index.md` and
`handbook/00-index.md` (or just list the `guide/` and `handbook/` folders). `⚠️` = not supported yet;
use the alternative noted.

## Core rules
- **Static typing** — like TypeScript over JS: every parameter, property, and return type is required. `→ guide/01-mental-model.md, guide/05-type-inference.md`
- **Files/tags** — `.tyhp` + `<?tyhp ` (needs trailing space); `.tyhpdef` = declarations only. `→ guide/02-files-and-tags.md`
- **Typed / inferred locals** — `string $s;` or inferred `$s = "hi";` (like C# `var` / TS `let`). `→ guide/05-type-inference.md, guide/08-typed-locals.md`
- **Non-nullable by default** — like C#/TS strict null: write `?string` / `|null` to allow `null`. `→ guide/03-strict-rules.md`
- **Conditions must be `bool`** — no truthiness; write `if ($x !== null)` not `if ($x)`. `→ guide/03-strict-rules.md`
- **Inference limits** — literals infer, but annotate arrays (`array<int> $xs = [...]`); an untyped call result is `mixed`. `→ guide/05-type-inference.md`

## Types
- **`decimal`** — arbitrary-precision decimal, like C# `decimal` / Java `BigDecimal`. Write values with `\Tyhp\decimal('19.99')` (⚠️ avoid the `(decimal)` cast). `→ guide/04-type-system.md`
- **Literal types** — `'GET'|'POST'`, `42` used as types, like TS literal/union types. `→ guide/04-type-system.md`
- **Generics** — `class Box<T extends X = Def>`, like C#/TS generics. Pass all type args explicitly. `→ guide/07-generics.md`
- **Generic collection types** — `array<K,V>`, `iterable<T>`, `callable<A,B,R>` (**return type is last**). `→ guide/07-generics.md`
- **Type aliases** — `type Id = int;` like TS `type`. `→ guide/09-type-aliases.md`
- **Structs** — value type with typed properties only; **structural** compatibility like TS/Go (vs nominal classes). `→ guide/10-structs.md`
- **Utility types** — `\Tyhp\Partial<T> Pick<T,K> Record<K,V> Awaited<T>` …, straight from TS. `→ guide/19-utility-types.md`
- **String-as-type** — symbol-name types (`__ClassName`) and template string types (`"api/${string}"`), like TS template-literal types. `→ guide/18-string-as-type.md`

## Behavior / expressions
- **async / await** — like C#/JS; cancel via `CancellationToken`. `→ guide/13-async-await.md, handbook/07-runtime-api.md`
- **using / disposables / `:=`** — like C# `using` + `IDisposable`; `$x := new R()` = scope-disposed local (C# `using` declaration / Go `defer`-ish). `→ guide/14-disposal.md`
- **`with` expressions** — record-style update like C# `with`: `new Point() with [x => 1]`. (Reliable on structs; for classes assign explicitly.) `→ guide/15-with-expressions.md`
- **Operator overloading** — like C# `operator +`, declared in the class/extension body (incl. `convert`). `→ guide/11-operator-overloading.md`
- **Extension methods** — like C#/Kotlin: `function f(extends Money $this, ...)`; activate with `use extension`. `→ guide/12-extensions.md`
- **Type guards + narrowing** — like TS: `function isX(mixed $v): $v is X`, narrows in `if`. `instanceof` / `is` / `isa` / `isan` are equivalent. `→ guide/16-type-guards.md`
- **Compile-time helpers** — `nameof()`, `typeof()`, `default(T)`, `variable_exists()` like C#. `→ guide/17-compile-time-helpers.md`
- **Expression trees** — inline `fn` captured as an AST for an `Expression<T,R>` param, like C# `Expression<Func<>>` / LINQ. Experimental. `→ guide/21-expression-trees.md`
- **Property accessors/hooks** — same syntax as PHP 8.4+ property hooks (get/set). `→ guide/20-property-accessors.md`

## Declarations (deltas vs PHP)
- **Constructor return type / chaining** — constructors declare `: void`, or `: parent(<args>)` to call the base ctor (like C#/Java `: base(...)`). `→ guide/22-declarations.md`
- **Method overload signatures** — bodiless signature declarations, like TS/C# overloads. `→ guide/22-declarations.md`
- **Trait property alias** — Tyhp adds `use T { $prop as $renamed; }` (PHP only aliases methods). `→ guide/22-declarations.md`
- **`internal` modifier** — ⚠️ not in the language yet (unlike C#/TS `internal`). `→ guide/28-availability-gotchas.md`

## External code / tooling
- **`.tyhpdef`** — declaration stubs for untyped PHP, exactly like TS `.d.ts`. `→ guide/23-tyhpdef.md, handbook/05-php-interop.md`
- **Use existing PHP libraries** — describe them in a `.tyhpdef`, then call as usual. `→ guide/24-php-interop.md, handbook/05-php-interop.md`
- **Runtime helpers** — `\Tyhp\` classes back some features (`decimal`, `Promise`, `CancellationToken`, …). `→ guide/25-runtime-packages.md, handbook/07-runtime-api.md`
- **Tooling** — `tyhp lint` to type-check, `tyhp build` to compile. `→ guide/26-build-cli.md, handbook/03-build-cli-workflow.md`

> Everything not listed behaves like plain PHP 8.x. When in doubt, open the referenced file; use the
> `handbook/` files for setup, interop, and runtime API.
