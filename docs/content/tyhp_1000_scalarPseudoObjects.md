---
title: 'Scalar Pseudo-Objects'
status:
  tier: 0
  story: '03'
  state: complete
---

Tyhp lets you call methods on scalar values (`string`, `int`, `float`, `bool`, `array`) using object syntax when you define **extension** methods. The compiler rewrites `$value->method($args)` to a static call on the extension class. There is zero runtime overhead — no wrapper objects are created.

```status
tier: 2
story: '21'
state: planned
```

:::warning Not in this alpha
A built-in catalog of scalar methods (`toUpper`, `contains`, `len`, `map`, …) is **planned** for Story 21 (`tyhp/php` support extensions). It is **not included** in Tyhp 805.0.0-alpha.1. User-defined `extension` methods work today. Until the catalog ships, call PHP functions such as `\strtoupper($name)` or write (and import) your own extensions.
:::

## How to Write an Extension

Declare an `extension` with methods whose first parameter is marked `extends Type $this` (the receiver). The method body is ordinary Tyhp; `$this` has the extended type.

```tyhp
<?tyhp

extension StringHelpers {
    function toCamelCase(extends string $this): string {
        return $this;
    }

    function trimLower(extends string $this): string {
        return \strtolower(\trim($this));
    }
}

string $name = 'hello_world';
string $camel = $name->toCamelCase();
```

Compiles to:

```php
<?php
declare(strict_types=1);

$name = 'hello_world';
$camel = \StringHelpers::toCamelCase($name);
```

When the extension lives in another file or namespace, import it with `use extension`:

```tyhp
<?tyhp

use extension App\Extensions\StringHelpers;

string $name = 'hello_world';
string $camel = $name->toCamelCase();
```

See Use Statements for adaptation syntax (`as` / hide) on `use extension`.

## Method Chaining (User Extensions)

If your extensions return the extended type (or another type that also has extensions in scope), you can chain calls. Each call is rewritten independently.

```tyhp
<?tyhp

extension StringHelpers {
    function trimmed(extends string $this): string {
        return \trim($this);
    }

    function lower(extends string $this): string {
        return \strtolower($this);
    }

    function dashed(extends string $this): string {
        return \str_replace(' ', '-', $this);
    }
}

string $input = "  Hello World  ";
$result = $input->trimmed()->lower()->dashed();
```

```php
<?php

$input = "  Hello World  ";
$result = \StringHelpers::dashed(\StringHelpers::lower(\StringHelpers::trimmed($input)));
```

## Calling on Literal Values

User extension methods can be called on literals. Wrap integer and float literals in parentheses.

```tyhp
<?tyhp

extension IntHelpers {
    function asChar(extends int $this): string {
        return \chr($this);
    }
}

extension StringHelpers {
    function parts(extends string $this, string $sep): array {
        return \explode($sep, $this);
    }
}

$char = (65)->asChar();
$parts = "a,b,c"->parts(",");
```

## Best Practices

:::tip
Write small extension methods that wrap `\str_*` / `\array_*` (or your own helpers) when the method-call style helps readability. Import them with `use extension` in each file that needs them.
:::

:::tip
Chain only extensions you defined (or imported). A built-in `$name->trim()->tolower()` catalog is planned (Story 21) and is not in this alpha — use `\strtolower(\trim($name))` or your own wrappers until then.
:::

:::tip
Keep the receiver as `extends string $this` (or `extends array $this`, …) so call sites read as instance methods while emit stays a static call.
:::

## Common Mistakes

:::danger
Don't assume PHP string/array functions are already methods on scalars in this alpha. `$name->toUpper()` and `$numbers->map(...)` are **planned** (Story 21). Until that catalog ships, use `\strtoupper($name)` / `\array_map(...)` or your own extensions.
:::

:::danger
Don't expect actual object instances — these are compile-time rewrites. Calling a method that is not an imported extension produces a compile error.
:::

:::danger
Don't import an extension class with a plain `use` and expect methods to attach. Use `use extension` to activate extension methods on the target type (same-file extensions are already in scope).
:::

:::danger
Don't try to assign scalar methods to variables — `$fn = $name->trimmed` is not valid. These are method calls, not callable properties.
:::
