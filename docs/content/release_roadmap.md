---
title: Roadmap
status:
  tier: 3
  story: '30'
  state: complete
---

Tyhp is built in a deliberately ordered sequence of stories, grouped into four tiers. The guiding principle is a thin vertical slice that compiles and runs end-to-end as early as possible.

**This alpha (805.0.0-alpha.1) is Tier 1 complete** — stories **01 through 16.5**. Stories 17+ are not shipped.

Stories are numbered contiguously from 01 to 30 with additive sub-stories (`08.5`, `10.5`, `14.5`, `16.5`, `19.5`, `20.5`). Each documentation page carries a Tier/Story badge.

:::note
The two flagships are **Story 08 — the type checker** (the correctness engine on the spine) and **Story 16 — parsable lambdas / expression trees** (the marquee feature that enables LINQ-style query builders and ORMs).
:::

## Tier 0 — Spine (a real program compiles and runs) — done

1. **Story 01 — Foundation:** diagnostic system, compilation pipeline, and build endpoint.
2. **Story 02 — Binder:** name resolution and scope building.
3. **Story 03 — Extension operator overloads & tyhpdef inline extensions.**
4. **Story 04 — Tyhp runtime library modules:** tyhp/core, tyhp/decimal, tyhp/async, tyhp/lambda.
5. **Story 05 — Bind symbols to AST nodes.**
6. **Story 06 — Built-in types, grammar fixes, and compiler infrastructure.**
7. **Story 07 — Testing infrastructure & conformance harness.**
8. **Story 08 — Checker** (type checking & validation) — <i>flagship</i>.
9. **Story 08.5 — Symbol-name types** (`__ClassName`, template string types, …).
10. **Story 09 — Emitter** (basic PHP output).
11. **Story 10 — Build action** (wires everything together).
12. **Story 10.5 — Deferred correctness & quality fixes.**

## Tier 1 — Usable — done (this alpha)

1. **Story 11 — Emitter feature expansion:** structs, generics erasure, extension methods, operator overloads, type aliases, with, disposables, async/await, compile-time constructs, short function syntax, overloads, type guards, trait requirements, and import lowering.
2. **Story 12 — Lint action.**
3. **Story 13 — CLI polish:** help, init, version, composer integration.
4. **Story 14 — Error-message quality** (diagnostics as a product feature).
5. **Story 14.5 — PHP 8.5 syntax surface + lowering** (`805.0.0`).
6. **Story 15 — The Tyhp ↔ PHP interop contract** (written down).
7. **Story 16 — Parsable lambdas (expression trees)** — <i>flagship wedge showcase</i>.
8. **Story 16.5 — Callable signature utilities** (`__CallableParametersStruct` / `__CallableParametersTuple` / `__CallableReturnType`).
9. **Story 28 — Generic type parameter defaults** (originally sequenced in Tier 3; already in this alpha).

## Tier 2 — Developer Experience & Ecosystem — not in this alpha

1. **Story 17 — Sourcemap generation.**
2. **Story 18 — XDebug proxy.**
3. **Story 19 — Language Server (LSP).**
4. **Story 19.5 — VS Code extension (`vscode-tyhp`).**
5. **Story 20 — Tyhpdef generator** (C# CLI integration).
6. **Story 20.5 — PHP version gating** (`declare(php=…)` / `#[\Tyhp\Php]`).
7. **Story 21 — PHP extension Composer packages** (`tyhp/php` + `tyhp/php-ext-*`; this alpha ships a thin PHP 8.2-baseline `tyhp/php` only). Includes the planned built-in scalar method catalog (`$name->toUpper()`, …).
8. **Story 22 — Web playground** (live .tyhp → PHP).

## Tier 3 — Advanced — not in this alpha

1. **Story 23 — Compiler optimizer (MVP).**
2. **Story 24 — Advanced optimizations.**
3. **Story 25 — `internal` visibility modifier.**
4. **Story 26 — Null-conditional chaining with assignment.**
5. **Story 27 — `new<TArgs...>` constructable object type.**
6. **Story 29 — Tyhp reflection API** (sourcemap-backed).
7. **Story 30 — Documentation & polish** (final capstone).

> Story 28 (generic defaults) shipped with this alpha; see Tier 1 above.

## Runtime Library Packages

Compiled Tyhp code depends on Composer packages under the `tyhp/` vendor (GitHub org `tyhpproject`):

- `tyhp/core` — type-system support, property accessors, strongly-typed variable wrappers, and disposable interfaces
- `tyhp/async` — Promise&lt;T&gt;, event loop, cancellation tokens, deferred execution, and async iterators
- `tyhp/decimal` — the decimal type with operator-overload support for precise arithmetic
- `tyhp/lambda` — expression-tree nodes, visitors, and serialization for parsable lambdas
- `tyhp/php` — PHP builtin tyhpdefs (8.2 baseline in this alpha)

## Long-Term Vision

Tyhp's long-term goal is to be for PHP what TypeScript is for JavaScript: a mature, widely-adopted typed superset that makes PHP development safer, more productive, and more enjoyable — while always producing standard PHP that runs anywhere PHP runs.
