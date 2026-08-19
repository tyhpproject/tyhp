---
title: 'Extensions in Tyhpdef'
status:
  tier: 0
  story: '03'
  state: complete
---

Tyhpdef can attach Tyhp extension methods and operator overloads to PHP types you do not own. That lets Tyhp callers write `$money->formatCurrency()` or `$a + $b` while the compiler rewrites those sites to the underlying PHP methods.

Standalone `extension Name { … }` blocks belong in `.tyhp` files. Tyhpdef cannot declare them. Instead, tyhpdef offers two forms:

1. **Inline members** on a tyhpdef class — `extension function`, `extension fn`, and `extension operator` with mapping bodies
2. **`use extension`** on a tyhpdef class — attach a standalone Tyhp extension so callers do not need their own import

For the Tyhp-side `extension { }` syntax, `use extension` in `.tyhp` files, and emit details, see [Extensions](tyhp_2100_extensions.md).

## Inline Extension Members

Inside a tyhpdef class body you can declare `extension function`, `extension fn` (expression body), and `extension operator` members. The compiler treats them as a synthetic extension for that class. These members are **auto-active**: Tyhp code that uses the type does not need a separate `use extension` import.

`extension` replaces a visibility modifier — members are always public. `abstract` and `final` are not allowed. `$this` in an extension function and `self` in an inline extension operator resolve to the enclosing tyhpdef class.

These members are the exception to tyhpdef's declaration-only rule: they **require** a brace body or `=>` expression. The body is mapping code the compiler uses when rewriting Tyhp call sites. The `.tyhpdef` file itself is never emitted as PHP.

```tyhp
<?tyhpdef

class Money {
    public function plus(Money $other): Money;
    public function isEqualTo(Money $other): bool;
    public function __toString(): string;

    extension function formatCurrency(string $locale = 'en_US'): string {
        return $locale . ' ' . $this->__toString();
    }

    extension fn shortLabel(): string => $this->formatCurrency();

    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }

    extension operator ==(self $left, self $right): bool => $left->isEqualTo($right);
}
```

Tyhp callers then write:

```tyhp
<?tyhp

Money $total = $a + $b;
string $label = $total->formatCurrency();
```

The compiler rewrites those sites using the mapping bodies (for example `$a->plus($b)` and a static call for `formatCurrency`). Inline `extension operator` call sites target methods on the owning PHP class (such as `\Money::__add`).

### Full vs short function form

| Form | Syntax | Body |
|------|--------|------|
| Full | `extension function name(…): T { … }` | Brace `methodBody` |
| Short | `extension fn name(…): T => expr;` | Expression plus required semicolon |

Inline `extension function` does **not** use `extends` on the first parameter. The receiver is implicit `$this`. Do not write `extension function format(extends Money $this)`.

Inline `extension operator` does **not** use `<Type>` after the operator token. The target is the enclosing class. Do not write `extension operator +<Money>(…)`.

## Native vs Mapped Operators

Tyhpdef has two operator members. Mixing them up is a common source of `TYHP8013`.

| Form | Meaning | Emitter |
|------|---------|---------|
| `operator +(…): T;` (no `extension`) | Native PHP operator — the type already supports it | **No rewrite** — leave `$a + $b` |
| `extension operator +(…): T { … }` / `=> …` | Mapped overload (**body required**) | Rewrite to `__add` / the body target |
| `extension operator +(…): T;` (bodyless) | **Illegal** | Diagnostic `TYHP8013` |

Bare `operator` is documented in [Classes in Tyhpdef](tyhpdef_classes.md#operators). The rest of this section is the mapped form.

```tyhp
<?tyhpdef

class Money {
    public function plus(Money $other): Money;

    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }
}
```

Unary and conversion overloads use the same mapped form. A single-parameter `extension operator` is unary; `extension operator convert` maps conversions:

```tyhp
<?tyhpdef

namespace Tyhp;

final class Decimal {
    extension operator +(self $value): int|float {
        return \Tyhp\Decimal::__asNumeric($value);
    }

    extension operator convert(int $value): self {
        return \Tyhp\Decimal::__from($value);
    }

    extension operator convert(self $value): string {
        return $value->__toString();
    }
}
```

## use extension in Tyhpdef Class Bodies

`use extension` on a tyhpdef class is the extension analogue of `use SomeTrait;` in a Tyhp or PHP class: it attaches an existing standalone `extension { }` to this type. Matching methods become part of the type's surface. Callers that use the type do **not** need their own `use extension`.

The import supports the same adaptation syntax as traits: `as` aliases and `insteadof` precedence.

```tyhp
<?tyhpdef

class Money {
    public function plus(Money $other): Money;
    public function format(): string;

    use extension MoneyFormatting {
        MoneyFormatting::format as formatExtended;
    };
}
```

Only methods whose first parameter `extends` this class are attached. A method in the same extension that `extends` a different type is skipped for this class — it is not an error. If nothing in the extension targets the enclosing class, the `use` adds no methods (a no-op for this type, unless the name itself is missing).

The referenced extension must be a real `extension Name { … }` declared in Tyhp (a `.tyhp` file). If the name does not resolve to an extension, the compiler reports `TYHP8011`.

File-level `use extension` is also valid in tyhpdef (same syntax as Tyhp) when the tyhpdef file needs the extension name in scope.

## What Tyhpdef Cannot Declare

```tyhp
<?tyhpdef

// ERROR: standalone `extension { }` blocks are Tyhp-only (`.tyhp` files)
// extension StringExtensions {
//     function toCamelCase(extends string $this): string;
// }
```

Declare the extension in Tyhp, then attach it with `use extension` on the tyhpdef class, or write inline `extension function` / `extension operator` members with mapping bodies.

## Access and Conflicts

Inline extension members can only use the **public** API of the enclosing type. They cannot access private or protected members — extensions are external to the class.

An inline extension member must not collide with a member already declared on the same class (`TYHP8010`).

## Best Practices

:::tip
DO use inline `extension operator` when a PHP type has methods such as `plus()` / `isEqualTo()` and you want Tyhp to accept `$a + $b`. Keep the body a thin mapping onto those methods.
:::

:::tip
DO use bodyless `operator …;` (no `extension`) when the PHP type already implements the operator natively (engine magic, PECL, `DateTime` arithmetic).
:::

:::tip
DO use `use extension` on a tyhpdef class to auto-activate a shared Tyhp extension for every consumer of that type.
:::

:::tip
DO keep mapping bodies small. They exist so the consumer compiler can rewrite call sites; they are not a place to reimplement the PHP library.
:::

## Common Mistakes

:::danger
DON'T write a bodyless `extension operator +(…): T;`. That is `TYHP8013`. Use bodyless `operator` for native ops, or give `extension operator` a brace or `=>` body.
:::

:::danger
DON'T put `extends Type $this` on an inline `extension function`, and DON'T put `<Type>` on an inline `extension operator`. Those forms belong to standalone Tyhp `extension { }` blocks.
:::

:::danger
DON'T use `abstract` or `final` on `extension function`, `extension fn`, or `extension operator`.
:::

:::danger
DON'T declare a standalone `extension Name { }` in a `.tyhpdef` file. That construct is valid only in `.tyhp` source.
:::

```tyhp
<?tyhpdef

class Money {
    public function plus(Money $other): Money;

    // ERROR TYHP8013: mapped overload missing a body
    // extension operator +(self $left, self $right): self;

    // OK: native passthrough
    // operator +(self $left, self $right): self;

    // OK: mapped with body
    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }
}
```

## Compiler Errors

- `TYHP8010` — an inline extension member conflicts with a declared member on the same class
- `TYHP8011` — a `use extension` reference in tyhpdef does not resolve to an extension declaration
- `TYHP8012` — a member marked `extension` is not a valid extension member (`function` / `fn` / `operator` only)
- `TYHP8013` — `extension operator` is missing a body; use bodyless `operator` for native PHP operators

## Summary

- Tyhpdef cannot declare standalone `extension { }` blocks — use inline members or `use extension`
- Inline `extension function` / `extension fn` / `extension operator` require mapping bodies and are auto-active for consumers
- `$this` / `self` in inline members refer to the enclosing tyhpdef class
- Bodyless `operator …;` means native PHP passthrough; `extension operator` always needs a body
- `use extension` on a tyhpdef class is like `use` for a trait: it attaches a Tyhp extension (optional `as` / `insteadof`); only methods whose `extends` type is this class apply; callers do not need their own import
