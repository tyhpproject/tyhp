# Changelog

Notable changes to the Tyhp compiler, CLI, and language server. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- `lazy new Class(args)` and `lazy new Class<T>(args)` defer the constructor when `output.phpVersion` is 8.4 or newer and the class has a stored instance property. A generic class that Tyhp tracks at runtime also stores its type-argument bag, so it stays lazy, and its constructor still sees the type arguments. Arguments run immediately, in order, in the surrounding scope (locals, `$this`, `static`). On older targets, and when the class has no stored instance property, PHP would skip the constructor entirely, so the constructor runs immediately as an ordinary `new` (TYHP4403). An internal class other than `\stdClass`, a class that extends one, and a type that is not a class are TYHP4402. `lazy` is an identifier except immediately before `new`.

### Changed

- `nameof(...)` is a constant expression, so it can be an attribute argument. Attribute arguments are checked against the constructor, including `#[Name<T>(...)]`. `nameof(Type::$prop)` has type `__PropertyName<Type>` and is rejected when that member is not declared (TYHP4101) or belongs to a different type (TYHP4010); a string literal satisfies a `__PropertyName<Type>` parameter only when it names a property of `Type` (TYHP4101). A lambda in an attribute is still TYHP4090. `T::class` on a generic parameter is `__ClassName<T>` and emits that parameter's class name.
- `using`, `using await`, and a `:=` local disposed from `try`/`finally` call `dispose()` or `disposeAsync()` only when the value is not an uninitialized lazy object, so it is not initialized just to be disposed. The emitted guard calls `ReflectionClass::isUninitializedLazyObject` (behind `\PHP_VERSION_ID >= 80400` when `output.phpVersion` is below 8.4) and needs no package beyond the disposable contracts.
- Emit and checking are much faster in projects that load large tyhpdef packages. A PHP 8.5 build of `tyhp/rhetoric` emits in about 8 seconds instead of about 150 and checks in about 4 seconds instead of about 13, with identical output and diagnostics.

### Fixed

- A class used as a parameter, return, or property type is no longer emitted as `array` when a struct with the same short name is declared in another namespace.

- An extension method call inside an expression-tree lambda stays a method call (`$title->startsWith($prefix)` is a `MethodCallExpression`). The extension body is not substituted into the tree. `compile()` still runs that body.
- A `mixed` type argument in a `.tyhpdef` satisfies an `extends` bound, so a signature that spells `Builder<mixed>` from a PHPDoc `Builder<*>` can be called. The same argument written in a `.tyhp` file is still checked.
- A value passed to a parameter typed as an extern unioned with a real type (`\SortDirection|string`) is not a use of the extern when the value fits the real arm. A value that needs the extern arm still reports TYHP4307.
- Attributes on a class or function in a namespaced file resolve the names in their arguments through that file's namespace and imports, so `#[Index<Post>([nameof(Post::$title)])] class Post {}` type-checks `Post` instead of reporting TYHP4090 or skipping the check.
- `array<'get'|'set', T>` and `array<0|1, T>` type-check. String and int literal types, including unions of them, satisfy the array key constraint.

## [805.1.2-beta.4] - 2026-11-04

### Added

- Emitted PHP includes PHPStan/Psalm docblock tags (`@param`, `@return`, `@var`, `@template`, `@extends`, `@implements`, `@internal`) where the PHP declaration drops type information. `emit.docblocks` defaults to `lossy-only` in every build profile; `all` emits a tag for every signature type and `off` emits none. `emit.docblockDialect` defaults to `both`. Author docblocks and emitter directives are kept; `output.comments: false` drops prose and keeps analysis tags.
- In a `.tyhp` file, `<?=` is a Tyhp echo. The expression list is parsed and checked as Tyhp (`is`, `typeof`, `with`, and `nameof` are keywords) and emitted as `echo`, with the same stringable rules as `echo` (TYHP4120). The output is not escaped. A `.php` file still treats `<?=` as a PHP echo.
- Expression trees accept nested arrow `fn` bodies (as `LambdaExpression` nodes), list and keyed array literals, and `?->` method calls. Array spread is rejected with TYHP4322.
- `PropertyPath<callable(T): R>` parameters infer a generic `R` from an inline `fn`, also in a union with `callable(T): R`; a `?->` chain makes `R` nullable.
- Emitted properties and promoted constructor parameters whose PHP hint loses type information (type arguments, struct types, source type aliases) carry `#[\Tyhp\PropertyType('…')]`, which `\Tyhp\Type::fromProperty()` reads.
- Expression-tree parameter nodes carry the full declared type (`App\Table<App\Blog>`, `struct App\Address`) instead of the erased class name.
- More narrowing forms: `\assert($cond);`, loose `!= null` / `<>` / `== null` (non-null side only), nested property paths (`$a->b->c`), static properties of any class, and `switch (true)` / `match (true)` arms, which see earlier conditions as false.
- Template string holes can name type aliases (imported, namespace-qualified, class-level, generic, or from a tyhpdef), so template aliases compose. An unknown hole name reports TYHP3003 and a self-referencing alias TYHP3029.
- A double-quoted interpolation whose text and hole types fit a template string type is assignable to it, so `"api/{$resource}/items"` can be returned as `"api/${string}/items"`.
- Type parameters constrained to stringable types (`T extends string`, `T extends 'a'|'b'`) can be interpolated, concatenated, echoed, and used as template holes.

### Changed

- New error TYHP4198 for reading or writing a property, calling a method, or indexing through a possibly null receiver without narrowing it first. Code that compiled before may now fail; narrow the value or use `?->`.
- A value read through `?->` is nullable (`$a?->b` is `?B`), so using it where a non-null value is required now reports an error. Later links in the same chain short-circuit as in PHP.
- Expression trees reject a variable method name (`$x->$m()`), a class held in a variable (`new $c()`, `$c::make()`, `$x::class`), named arguments, and argument unpacking with TYHP4322. These were accepted before and emitted incorrectly.
- New error TYHP4199 for a template string hole with no string form (`${bool}`, `${array}`) or written as an expression (`${$x}`, an inline nested template).
- `.` and `.=` report TYHP4120 for operands with no string form, such as arrays and unconstrained type parameters. Nullable stringable operands are allowed.
- String interpolation, heredoc holes, `echo`, and `print` accept nullable stringable values (`?string`, `?int`, a nullable class with `__toString`), like `.` does; `null` becomes `''`. In a template string type, a nullable hole fits only a hole that also allows empty text, so `"item-{$id}"` with `?int $id` fits `"item-${''|int}"` but not `"item-${int}"`.
- `${int}` and `${float}` template holes match only the text PHP's `(string)` cast produces: `${int}` accepts `'3'` but not `'03'` or `'+3'`.
- `break` inside a closure that is nested in a loop is now rejected.
- New error TYHP4378 for a default value on a readonly property (or any instance property of a `readonly class`) when `output.phpVersion` is below 8.6, which PHP rejects when the class loads. Assign the value in the constructor or use a promoted parameter default.
- TYHP4024 now reports every magic method PHP rejects on an enum (`__toString`, `__get`, `__set`, `__isset`, `__unset`, `__destruct`, `__clone`, `__serialize`, `__unserialize`, `__sleep`, `__wakeup`, `__set_state`), not just `__construct`, and names the method.
- New error TYHP4379 for `__debugInfo()` on an enum when `output.phpVersion` is below 8.6.
- With `build.updateComposer`, a target PHP version that has no published `tyhp/*` runtime release (such as `8.0` or `8.1`) reports TYHP7108 and leaves `composer.json` unchanged, instead of pinning `804.X.Y` packages that need PHP 8.4.
- `scripts/release.sh` takes `prerelease`, `alpha`, `beta`, `rc`, `release`, `patch`, `minor`, `major`, or an explicit `X.Y.Z`. It publishes the GitHub Release only after each asset matches `dist/checksums.txt`, and it checks the bundled runtime versions against the runtime checkout.
- Every instance property and promoted constructor parameter of a `readonly class` is now readonly, as in PHP, so all readonly checks apply to it: writing it through another object or from outside the class (TYHP4021), a hook on it (TYHP4155), and `with` on it (TYHP4139–TYHP4141, TYHP4400, TYHP4401).
- TYHP4021 now also reports writes to a readonly property from a function, the file scope, or a class other than the declaring class or a subclass. Properties with an explicit set visibility such as `public(set)` are not reported.
- New error TYHP4109 for a static property in a `readonly class`, which PHP rejects when the class loads.

### Fixed

- A runtime-tracked generic `readonly class` emits an instance `\Tyhp\GenericObject $__tyhpGeneric` property with no default, initialized by `__bootGenericBag__tyhpGeneric()`, instead of the `HasGenerics` trait, so the class loads on PHP 8.2 and later and `clone` keeps the tracked types. A generic class that is not readonly still uses the trait.
- Readonly `new … with` below PHP 8.3 reports TYHP4400 instead of emitting a `__clone` wrapper that PHP 8.2 rejects. `clone … with` on a `readonly class` below PHP 8.5 reports TYHP4401. Experimental `clone … with` on PHP 8.3–8.4, for a class that is not readonly, emits a wrapper that loads: the empty constructor has no return type, it does not call the parent constructor with no arguments, and overrides sit on a static stack.
- Inline HTML and `<?=` echoes in a `.tyhp` file stay in source order, including inside a function or control-flow block, and a later `<?tyhp` section is accepted. `<?tyhp ?>` or `<?tyhp?>` opens an empty section. A `<?php` block in a `.tyhp` file reports TYHP2005. In a tagless project, the first six characters choose the file: `<?tyhp` is a tagged template, and any other `.tyhp` file is tagless code with no open tag, so `<?tyhp` there is not accepted.
- Inline HTML in a `.tyhp` file is emitted exactly as written: the emitted PHP no longer indents HTML lines (which printed extra spaces, including inside closures), and a blank line right after `?>` is kept.
- An `fn` the emitter could not turn into an expression tree was silently emitted as a plain closure; it now reports TYHP5024.
- `$a ?: $b` in an expression tree emitted a `null` true branch, and a nested `fn` passed to a `PropertyPath` parameter inside a tree was emitted as a plain closure.
- A `switch` with a `default` case where every case returns or throws no longer reports TYHP4011, and code after it reports TYHP4012.
- `break 2` / `continue 2` from a `switch` inside a loop no longer reports TYHP4026, and a loop's exit now accounts for the state at each `break` and `continue`.
- An `if` arm that returns, throws, breaks, or continues now leaves only the other arm's narrowing after the `if`, and a write to a narrowed path inside a nested block ends that narrowing.
- `<>` was typed `unresolved` (TYHP4043); it now works like `!=`.
- `EventLoop::run` accepts a `Promise<void>`, so an `async` function declared `: void` can be run from synchronous code.
- Extension methods on a union target resolve when called from inside a namespace.
- A `catch` that always throws or returns no longer makes locals assigned in the `try` "possibly undefined" (TYHP4014).
- `\get_object_vars()` on a concrete object no longer reports TYHP4035.
- Generated tyhpdefs keep a template string type's pattern and fully qualify alias names in its holes, so a library's consumers see the same type.
- A property or promoted constructor parameter in a `readonly class` without its own `readonly` no longer reports TYHP4108, which is retired.
- `new … with` on a `readonly class` on PHP 8.3–8.4 emits `new readonly class`, because a non-readonly class cannot extend a readonly class.
- Concatenating a nullable class that has `__toString` (`?Name . 'x'`) no longer reports TYHP4029.
- `tyhp --explain` has long-form text again for about 20 codes (including TYHP3031–TYHP3035 and TYHP4373–TYHP4377), and keeps Markdown list items on separate lines.
- When `build.updateComposer` cannot read a runtime package's own `composer.json`, it pins `tyhp/*` `0.4` (`80N.0.4`) and `tyhpdef/php` `0.0.3` instead of `0.1` and `0.0.1`.
- A `readonly class` that uses a trait with a non-readonly instance property or a static property now reports TYHP4380 when `output.phpVersion` is 8.2 or newer. PHP rejects that class when it loads.

## Baseline - 2026-11-02

Tracking starts at version `805.1.2-beta.3` (commit `7c8a0f47`). Earlier changes are not listed.
