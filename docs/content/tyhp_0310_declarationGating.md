---
title: 'Declaration Gating'
status:
  tier: 1
  story: '11'
  state: complete
---

PHP developers often wrap declarations in existence checks so a file can be loaded more than once without fatal redeclaration errors — for example `if (!\function_exists(...)) { function ... }` or `if (!\class_exists(...)) { class ... }`. Tyhp supports the same pattern. What Tyhp adds is compile-time validation: when you use that idiom to gate a declaration, the gate argument must name that exact declaration, using a fully-qualified name.

:::note
This page is about **declaration gates** — wrapping a class, function, interface, trait, or enum so it is only declared when it does not already exist. That is separate from using `\class_exists($name)` / `\function_exists($name)` as **type guards** on string variables (see Type Narrowing and Guards).
:::

## The Pattern

A declaration gate is a top-level `if` with a negated existence call and a single matching declaration in the then-branch (no `else`):

```tyhp
<?tyhp

namespace App\Payments;

if (!\function_exists('\App\Payments\formatMoney')) {
    function formatMoney(int $cents): string {
        return '$' . number_format($cents / 100, 2);
    }
}

if (!\class_exists('\App\Payments\Money')) {
    class Money {
        public function __construct(public readonly int $cents): void {}
    }
}
```

The matching helpers are `\function_exists`, `\class_exists`, `\enum_exists`, `\interface_exists`, and `\trait_exists`. The helper must match the kind of declaration inside the block.

## The Name Must Be Exact

Inside a namespace, an unqualified or wrong name is not enough. The gate must check for the same symbol the block declares. Tyhp accepts:

- A string literal with the fully-qualified name (leading `\` optional), e.g. `'\App\Payments\formatMoney'`
- `__NAMESPACE__ . '\formatMoney'` — concatenating the current namespace with `'\Name'`

In the global namespace only, a short name such as `'Foo'` or `'\Foo'` is also accepted.

These are rejected (compiler error TYHP4213):

- `'formatMoney'` — not namespaced when the declaration lives in a namespace
- `'\formatMoney'` — wrong namespace (global instead of `App\Payments`)
- `''` — empty
- `'someOtherName'` — a different symbol than the one being declared

```tyhp
<?tyhp

namespace App\Payments;

// ERROR TYHP4213 — unqualified
if (!\function_exists('formatMoney')) {
    function formatMoney(int $cents): string { return ''; }
}

// ERROR TYHP4213 — wrong namespace
if (!\function_exists('\formatMoney')) {
    function formatMoney(int $cents): string { return ''; }
}

// OK — explicit FQN
if (!\function_exists('\App\Payments\formatMoney')) {
    function formatMoney(int $cents): string { return ''; }
}

// OK — __NAMESPACE__ concat
if (!\function_exists(__NAMESPACE__ . '\formatMoney')) {
    function formatMoney(int $cents): string { return ''; }
}
```

:::note
`nameof(...)` inside a declaration gate is not validated yet and is not treated as a movable gate. Prefer an FQN string or `__NAMESPACE__ . '\Name'` for now.
:::

## When the Gate Is Correct

If the gate is valid (matching helper, single matching declaration, and a correct FQN / `__NAMESPACE__` argument):

- The checker accepts it — no TYHP4213
- The emitter moves the entire `if` together with the declaration to the normal destination for that kind of symbol (classes/interfaces/traits/enums to their PSR-4 file; namespace functions to `_functions.php`)
- The gate argument is always rewritten to `__NAMESPACE__ . '\Name'` in the PHP output (even if the Tyhp source used an explicit FQN), so an output `namespacePrefix` cannot break the runtime check
- Gated functions are placed at the end of `_functions.php`, after ungated function declarations, so they are evaluated last
- The source file is not treated as an entry point solely because of the gate

For example, Tyhp source that checks `'\App\Payments\formatMoney'` emits:

```php
namespace App\Payments;

if (!\function_exists(__NAMESPACE__ . '\formatMoney')) {
    function formatMoney(int $cents): string {
        return '$' . number_format($cents / 100, 2);
    }
}
```

:::tip
This is why declaration gates are useful for shared helpers that may be loaded from more than one path: Tyhp still emits them as normal declarations, wrapped in the same existence check you wrote — rewritten to `__NAMESPACE__` so it tracks the emitted namespace.
:::

## When the Gate Is Incorrect

If the shape looks like a declaration gate (negated `*_exists` wrapping a single declaration of the matching kind) but the argument does not name that declaration:

- The checker reports **TYHP4213** — Declaration existence gate must check for the expected fully-qualified name
- The build fails (the error blocks emit)
- The splitter does not treat the `if` as a movable declaration gate

Other `if` wrappers that are not this idiom (wrong helper kind, `else` branch, multiple statements in the then-body, or a non-existence condition) are ordinary control flow. They stay as entry-point / root code and are not rewritten as declaration gates.

:::warning
A mismatched gate is not “close enough.” Checking `'formatMoney'` while declaring `App\Payments\formatMoney` is a compile error in Tyhp, even though PHP might appear to work at runtime in some setups.
:::

## Common Mistakes

:::danger
Don't use the short name inside a namespace (`'demo'`). Use `'\Current\Namespace\demo'` or `__NAMESPACE__ . '\demo'`.
:::

:::danger
Don't gate one symbol while declaring another (`function_exists('foo')` wrapping `function bar()`). The names must match.
:::

:::danger
Don't mix helpers and declaration kinds (`class_exists` around a `function`, or `function_exists` around a `class`). Those are not treated as declaration gates.
:::
