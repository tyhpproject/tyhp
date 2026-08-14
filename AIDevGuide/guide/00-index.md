# Tyhp for PHP developers — language guide for AI agents

> Read before editing a `.tyhp` codebase. You know PHP 8.x. Tyhp is a **statically-typed PHP superset
> that compiles to plain PHP**. This is the *delta only*; anything unmentioned behaves like PHP.
> `→` means "compiles to". ⚠️ = doesn't compile cleanly yet — use the PHP form (see [§28](28-availability-gotchas.md)).
> (Any `NN|` prefixes you see are your reader's line numbers, not Tyhp syntax.)
> For project setup, the CLI/build workflow, PHP interop, testing, worked examples, and full runtime
> API signatures, see the `../handbook/` files.

## Sections (read only the file you need)

- `01-mental-model.md` — mental model
- `02-files-and-tags.md` — files and tags
- `03-strict-rules.md` — strict rules
- `04-type-system.md` — type system
- `05-type-inference.md` — type inference
- `06-assignability.md` — assignability
- `07-generics.md` — generics
- `08-typed-locals.md` — typed locals
- `09-type-aliases.md` — type aliases
- `10-structs.md` — structs
- `11-operator-overloading.md` — operator overloading
- `12-extensions.md` — extensions
- `13-async-await.md` — async await
- `14-disposal.md` — disposal
- `15-with-expressions.md` — with expressions
- `16-type-guards.md` — type guards
- `17-compile-time-helpers.md` — compile time helpers
- `18-string-as-type.md` — string as type
- `19-utility-types.md` — utility types
- `20-property-accessors.md` — property accessors
- `21-expression-trees.md` — expression trees
- `22-declarations.md` — declarations
- `23-tyhpdef.md` — tyhpdef
- `24-php-interop.md` — php interop
- `25-runtime-packages.md` — runtime packages
- `26-build-cli.md` — build cli
- `27-diagnostics.md` — diagnostics
- `28-availability-gotchas.md` — availability gotchas
- `29-php-mapping.md` — php mapping
