---
title: Extensions
status:
  tier: 0
  story: '03'
  state: complete
---

Extensions in Tyhp allow you to add new methods and operator overloads to existing types without modifying their source code. This is useful for adding functionality to classes you don't own, imported PHP types, or even scalar types like string, int, and array. Extensions are resolved at compile time and rewritten to static method calls in the PHP output, resulting in zero runtime overhead.

## Declaring an Extension

An extension is declared using the extension keyword with a name. Inside the extension body, functions use the extends keyword on the first parameter to indicate the type being extended. This first parameter becomes $this when calling the method.

```tyhp
<?tyhp

extension StringExtensions {
    function toCamelCase(extends string $this): string {
        $parts = \explode('_', $this);
        return \lcfirst(\implode('', \array_map('\ucfirst', $parts)));
    }

    function toSnakeCase(extends string $this): string {
        return \strtolower(\preg_replace('/[A-Z]/', '_$0', $this));
    }

    function truncate(extends string $this, int $maxLength, string $suffix = '...'): string {
        if (\strlen($this) <= $maxLength) {
            return $this;
        }
        return \substr($this, 0, $maxLength - \strlen($suffix)) . $suffix;
    }
}

// Using extension methods — called as if they were instance methods
string $text = 'hello_world';
echo $text->toCamelCase();   // outputs: helloWorld
echo $text->toSnakeCase();   // outputs: hello_world
```

## Compiled PHP Output

Extension method calls are rewritten to static method calls. The object the method is called on becomes the first argument. The extension class is emitted as a standard PHP class with static methods.

```php
<?php

class StringExtensions
{
    public static function toCamelCase(string $this_): string
    {
        $parts = \explode('_', $this_);
        return \lcfirst(\implode('', \array_map('\ucfirst', $parts)));
    }

    public static function toSnakeCase(string $this_): string
    {
        return \strtolower(\preg_replace('/[A-Z]/', '_$0', $this_));
    }

    public static function truncate(string $this_, int $maxLength, string $suffix = '...'): string
    {
        if (\strlen($this_) <= $maxLength) {
            return $this_;
        }
        return \substr($this_, 0, $maxLength - \strlen($suffix)) . $suffix;
    }
}

$text = 'hello_world';
echo StringExtensions::toCamelCase($text);
echo StringExtensions::toSnakeCase($text);
```

## Extensions on Class Types

Extensions can add methods to any class type, including third-party classes you don't own.

```tyhp
<?tyhp

extension DateTimeExtensions {
    function isWeekend(extends \DateTime $this): bool {
        $day = (int)$this->format('N');
        return $day >= 6;
    }

    function isBusinessHours(extends \DateTime $this): bool {
        $hour = (int)$this->format('G');
        return $hour >= 9 && $hour < 17 && !$this->isWeekend();
    }
}

$now = new \DateTime();
if ($now->isBusinessHours()) {
    echo 'Office is open';
}
```

```php
<?php

$now = new \DateTime();
if (DateTimeExtensions::isBusinessHours($now)) {
    echo 'Office is open';
}
```

## Generic Extensions

Extension methods support generic type parameters, allowing you to write type-safe extension methods for generic types like array<T> or iterable<T>.

```tyhp
<?tyhp

extension IterableExtensions {
    function firstOrNull<T>(extends iterable<T> $this, callable<T, bool> $predicate): ?T {
        foreach ($this as $item) {
            if ($predicate($item)) {
                return $item;
            }
        }
        return null;
    }
}

extension ArrayExtensions {
    function max<T extends int|float>(extends array<T> $this): T {
        if (empty($this)) {
            throw new \RuntimeException('Array is empty');
        }
        return \max($this);
    }

    function mapTo<T, R>(extends array<T> $this, callable<T, R> $fn): array<R> {
        return \array_map($fn, $this);
    }
}

array<int> $numbers = [1, 2, 3];
echo $numbers->max();  // outputs: 3

array<string> $upper = $numbers->mapTo(fn(int $n): string => (string)$n);
```

```php
<?php

$numbers = [1, 2, 3];
echo ArrayExtensions::max($numbers);

$upper = ArrayExtensions::mapTo($numbers, fn(int $n): string => (string)$n);
```

## Extension Operator Overloads

Extension functions still use the extends keyword on the first parameter to name the type being extended. Extension operator overloads use an explicit target type in angle brackets right after the operator token, e.g. operator +<MyType>(...). The left operand type is typically self (the target type). abstract and final are not allowed on extension operators, nor on tyhpdef extension function, extension fn, or extension operator members.

```tyhp
<?tyhp

extension StringOperators {
    operator *<string>(self $left, int $right): string
    {
        return \str_repeat($left, $right);
    }
}

string $line = '-' * 40;  // 40 dashes via StringOperators
```

## Extension Operators on Class Types

The type in angle brackets in operator +<Money>(...) is the class or named type the overload applies to. self in the parameter list refers to that target type.

```tyhp
<?tyhp

extension MoneyOperators {
    operator +<Money>(self $left, self $right): self {
        return $left->plus($right);
    }

    operator ==<Money>(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }
}
```

## Tyhpdef Inline Extensions

For the full tyhpdef reference (inline members, native vs mapped operators, `use extension` on a class, and what tyhpdef cannot declare), see [Extensions in Tyhpdef](tyhpdef_extensions.md).

Inside a tyhpdef class body you can declare extension function, extension fn (expression body), and extension operator members. The compiler treats them as belonging to a synthetic extension for that class: $this in an extension function and self in an inline extension operator resolve to the enclosing tyhpdef class. These members cannot use abstract or final.

### Operators: native vs mapped

| Form | Meaning | Emitter |
|------|---------|---------|
| `operator +(…): T;` (no `extension`) | Native PHP operator | **No rewrite** — leave `$a + $b` |
| `extension operator +(…): T { … }` / `=> …` | Mapped overload (**body required**) | Rewrite to `__add` / body target |
| `extension operator +(…): T;` (bodyless) | **Illegal** | Diagnostic `TYHP8013` |

```tyhp
<?tyhpdef
namespace Decimal;
final class Decimal {
    // Native on the PECL extension — type-check only; emit keeps `$a + $b`
    operator +(self $left, Decimal|string|int $right): self;
}

class Money {
    public function plus(Money $other): Money;
    public function isEqualTo(Money $other): bool;

    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }

    extension operator ==(self $left, self $right): bool {
        return $left->isEqualTo($right);
    }

    extension operator +(self $left, Money $right): self => $left->plus($right);

    extension function formatCurrency(string $locale = 'en_US'): string {
        return $locale . ' ' . $this->__toString();
    }

    extension fn shortLabel(): string => $this->formatCurrency();
}
```

## use extension in Tyhpdef Class Bodies

A tyhpdef class can pull in standalone extension declarations with `use extension`, including trait-like adaptations (`as` aliases, `insteadof` precedence). Types that declare `use extension` in tyhpdef can auto-activate those extensions for callers without a separate `use extension` in every Tyhp file (see binder / resolution behavior).

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

## Importing Extensions with use extension

Extensions from other namespaces or files are imported with the use extension syntax instead of a regular use statement. This makes it explicit that you are bringing extension methods into scope. The import supports trait-like adaptations using curly braces, allowing you to selectively rename methods.

```tyhp
<?tyhp

// Import an extension and bring all its methods into scope
use extension App\Extensions\StringExtensions;

// Import with adaptations (like traits)
use extension App\Extensions\ArrayExtensions {
    ArrayExtensions::first as firstItem;
};

string $text = 'hello_world';
echo $text->toCamelCase();  // from StringExtensions

array<int> $nums = [1, 2, 3];
$first = $nums->firstItem();  // aliased from ArrayExtensions::first
```

## Chained Extension Method Calls

Extension method calls can be chained. Each link in the chain is independently rewritten to a static method call, with the result of the previous call becoming the first argument to the next.

```tyhp
<?tyhp

string $result = \trim($input)
    ->toSnakeCase()
    ->truncate(50);
```

```php
<?php

$result = StringExtensions::truncate(
    StringExtensions::toSnakeCase(
        \trim($input)
    ),
    50
);
```

## Access Restrictions

Extension methods can only access public members of the extended type. They cannot access private or protected members because extensions are external to the class hierarchy.

## Best Practices

:::tip
Use extensions to add utility methods to types you don't own — they keep your code clean without subclassing or wrapper patterns.
:::

:::tip
Organize extensions logically by the type they extend (e.g., StringExtensions, ArrayExtensions, DateTimeExtensions).
:::

:::tip
Import only the extensions you need with use extension — this avoids polluting the method namespace with unused extension methods.
:::

:::tip
Use generic type parameters on extension methods to preserve type safety through the call chain.
:::

:::tip
Keep extension methods pure when possible — they should compute a result from the extended value without side effects.
:::

## Common Mistakes

:::danger
Do not create overlapping extensions for the same type without clear purpose — if two extensions define the same method name for the same type, the compiler reports a conflict.
:::

:::danger
Do not try to access private or protected members from extension methods — extensions can only use the public API of the extended type.
:::

:::danger
Do not omit the extends keyword on the first parameter — that is a compile error (TYHP4147, CheckerExtensionMissingExtends), not a silent regular static method. The extension declaration itself does not need `extends` (`extension StringExtensions {` is valid).
:::

:::danger
Do not use regular use statements for extensions — always use use extension to bring extension methods into scope.
:::

```tyhp
<?tyhp

// ERROR TYHP4147: first parameter lacks `extends`
// extension Bad {
//     function doSomething(string $value): string { /* ... */ }
// }

// ERROR: conflicting extension methods
// extension StringHelpers1 {
//     function clean(extends string $this): string { /* ... */ }
// }
// extension StringHelpers2 {
//     function clean(extends string $this): string { /* ... */ }
// }
// use extension StringHelpers1;
// use extension StringHelpers2;  // Error: conflicting 'clean' method

// ERROR: accessing private member
// extension UserExtensions {
//     function getPasswordHash(extends User $this): string {
//         return $this->passwordHash;  // Error: private property
//     }
// }
```

## Compiler Errors

- Extension method without `extends` on the first parameter (TYHP4147). The extension declaration itself does not require `extends`.
- Extension operator overload without a <Type> target inside standalone extension bodies (e.g. operator +<Target>(...) is required inside extension Name { ... } blocks; tyhpdef inline extension operator members do not use <Type> because the target is the enclosing class).
- Using abstract or final on extension operators or on tyhpdef extension function / extension fn / extension operator.
- Conflicting extension methods for the same type and method name in scope.
- Accessing private or protected members of the extended type from an extension method.
- Using a regular use statement instead of use extension for importing extensions.
