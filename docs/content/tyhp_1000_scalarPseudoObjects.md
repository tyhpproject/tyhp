---
title: 'Scalar Pseudo-Objects'
status:
  tier: 0
  story: '03'
  state: complete
---

Tyhp allows you to call methods on scalar types (string, int, float, bool, array) as if they were objects. These methods map directly to PHP's built-in functions and are rewritten by the compiler at compile time. There is zero runtime overhead — no objects are created, no wrapper classes are instantiated. The compiler simply rewrites $value->method($args) into the corresponding PHP function call like \func($value, $args).

:::note
Scalar pseudo-object methods are syntactic sugar only. They compile to direct PHP function calls with no performance penalty.
:::

## How It Works

Each scalar type has a set of methods derived from PHP's built-in functions. The naming convention removes common prefixes to create shorter, more natural method names:

- string methods: Functions starting with str have the str prefix removed (e.g., strlen() becomes len(), strtolower() becomes tolower()). Functions starting with str_ have the str_ prefix removed (e.g., str_contains() becomes contains()).
- array methods: Functions starting with array_ have the array_ prefix removed (e.g., array_map() becomes map(), array_filter() becomes filter()).
- float methods: number_format() becomes format() (the number_ prefix is removed).
- int and bool methods: Use original function names directly (e.g., abs(), chr()).

For functions where the scalar value is not the first parameter in PHP (such as explode() or implode()), the method is adapted so the scalar becomes the calling object and the arguments are rearranged accordingly.

## String Methods

String methods cover PHP's extensive string function library. Functions prefixed with str or str_ have those prefixes removed.

```tyhp
<?tyhp

string $name = "Hello World";

// str_ prefix removed
$has = $name->contains("World");     // \str_contains($name, "World")
$starts = $name->starts_with("He");  // \str_starts_with($name, "He")
$ends = $name->ends_with("ld");      // \str_ends_with($name, "ld")
$padded = $name->pad(20);            // \str_pad($name, 20)
$repeated = $name->repeat(3);        // \str_repeat($name, 3)
$replaced = $name->replace("World", "Tyhp"); // \str_replace("World", "Tyhp", $name)
$csv = $name->getcsv(",");           // \str_getcsv($name, ",")
$shuffled = $name->shuffle();        // \str_shuffle($name)
$split = $name->split(2);            // \str_split($name, 2)
$rot = $name->rot13();               // \str_rot13($name)
$words = $name->word_count();        // \str_word_count($name)

// str prefix removed (no underscore)
$length = $name->len();              // \strlen($name)
$upper = $name->toupper();           // \strtoupper($name)
$lower = $name->tolower();           // \strtolower($name)
$rev = $name->rev();                 // \strrev($name)
$pos = $name->pos("World");          // \strpos($name, "World")
$cmp = $name->cmp("other");          // \strcmp($name, "other")
$tok = $name->tok(" ");              // \strtok($name, " ")

// Standard function names (no prefix change)
$sub = $name->substr(0, 5);          // \substr($name, 0, 5)
$trimmed = $name->trim();            // \trim($name)
$ltrimmed = $name->ltrim();          // \ltrim($name)
$rtrimmed = $name->rtrim();          // \rtrim($name)
$ucfirst = $name->ucfirst();         // \ucfirst($name)
$lcfirst = $name->lcfirst();         // \lcfirst($name)
$ucwords = $name->ucwords();         // \ucwords($name)
$ord = $name->ord();                 // \ord($name)
$md5 = $name->md5();                 // \md5($name)
$sha1 = $name->sha1();               // \sha1($name)

// Adapted parameter order: string is not the first PHP parameter
$parts = $name->explode(" ");        // \explode(" ", $name)

// Encoding/decoding
$encoded = $name->urlencode();       // \urlencode($name)
$decoded = $name->urldecode();       // \urldecode($name)
$b64 = $name->base64_encode();       // \base64_encode($name)
$json = $name->json_decode();        // \json_decode($name)
$html = $name->htmlspecialchars();   // \htmlspecialchars($name)

// Utility
$isEmpty = $name->empty();           // empty($name)
$isNum = $name->is_numeric();        // \is_numeric($name)
```

## Compiled PHP Output — String Methods

```php
<?php
// PHP output — all pseudo-object calls become function calls

$name = "Hello World";

$has = \str_contains($name, "World");
$starts = \str_starts_with($name, "He");
$length = \strlen($name);
$upper = \strtoupper($name);
$lower = \strtolower($name);
$rev = \strrev($name);
$pos = \strpos($name, "World");
$sub = \substr($name, 0, 5);
$trimmed = \trim($name);
$parts = \explode(" ", $name);
$encoded = \urlencode($name);
$isEmpty = empty($name);
```

## Integer Methods

```tyhp
<?tyhp

int $myNegativeInt = -15;
int $myPositiveInt = $myNegativeInt->abs();  // \abs($myNegativeInt)

// Math operations
$bin = $myPositiveInt->decbin();              // \decbin($myPositiveInt)
$hex = $myPositiveInt->dechex();              // \dechex($myPositiveInt)
$oct = $myPositiveInt->decoct();              // \decoct($myPositiveInt)
$ceil = $myPositiveInt->ceil();               // \ceil($myPositiveInt)
$floor = $myPositiveInt->floor();             // \floor($myPositiveInt)
$div = $myPositiveInt->intdiv(3);             // \intdiv($myPositiveInt, 3)
$max = $myPositiveInt->max(10, 20);           // \max($myPositiveInt, 10, 20)
$min = $myPositiveInt->min(10, 20);           // \min($myPositiveInt, 10, 20)

// Conversion
$chr = (65)->chr();                           // \chr(65)
$float = $myPositiveInt->__toFloat();         // (float)$myPositiveInt
$str = $myPositiveInt->__toString();          // (string)$myPositiveInt

// Utility
$isEmpty = $myPositiveInt->empty();           // empty($myPositiveInt)
```

## Compiled PHP Output — Integer Methods

```php
<?php

$myNegativeInt = -15;
$myPositiveInt = \abs($myNegativeInt);

$bin = \decbin($myPositiveInt);
$hex = \dechex($myPositiveInt);
$ceil = \ceil($myPositiveInt);
$div = \intdiv($myPositiveInt, 3);
$chr = \chr(65);
$float = (float)$myPositiveInt;
$str = (string)$myPositiveInt;
```

## Float Methods

```tyhp
<?tyhp

float $price = 19.99;

// Rounding
$rounded = $price->round(1);         // \round($price, 1)
$ceil = $price->ceil();               // \ceil($price)
$floor = $price->floor();             // \floor($price)

// Formatting (number_format with number_ removed)
$formatted = $price->format(2);                  // \number_format($price, 2)
$formatted2 = $price->format(2, ",", ".");       // \number_format($price, 2, ",", ".")

// Math
$sqrt = $price->sqrt();               // \sqrt($price)
$abs = $price->abs();                 // \abs($price)
$log = $price->log();                 // \log($price)
$exp = $price->exp();                 // \exp($price)
$sin = $price->sin();                 // \sin($price)
$cos = $price->cos();                 // \cos($price)

// Checks
$finite = $price->is_finite();        // \is_finite($price)
$nan = $price->is_nan();              // \is_nan($price)

// Division
$divided = $price->fdiv(3.0);         // \fdiv($price, 3.0)
$mod = $price->fmod(3.0);             // \fmod($price, 3.0)

// Conversion
$int = $price->__toInt();             // (int)$price
$str = $price->__toString();          // (string)$price
```

## Compiled PHP Output — Float Methods

```php
<?php

$price = 19.99;

$rounded = \round($price, 1);
$ceil = \ceil($price);
$floor = \floor($price);
$formatted = \number_format($price, 2);
$sqrt = \sqrt($price);
$abs = \abs($price);
$finite = \is_finite($price);
$int = (int)$price;
$str = (string)$price;
```

## Boolean Methods

```tyhp
<?tyhp

bool $flag = true;
$str = $flag->__toString();           // (string)$flag
$isEmpty = $flag->empty();            // empty($flag)
```

## Array Methods

Array methods are derived from PHP's array_* functions with the array_ prefix removed. Functions that don't start with array_ (like sort(), count(), implode()) keep their original names.

```tyhp
<?tyhp

array<int> $numbers = [3, 1, 4, 1, 5];

// array_ prefix removed
$mapped = $numbers->map(fn($n) => $n * 2);     // \array_map(fn($n) => $n * 2, $numbers)
$filtered = $numbers->filter(fn($n) => $n > 2); // \array_filter($numbers, fn($n) => $n > 2)
$keys = $numbers->keys();                       // \array_keys($numbers)
$values = $numbers->values();                   // \array_values($numbers)
$reversed = $numbers->reverse();                // \array_reverse($numbers)
$unique = $numbers->unique();                   // \array_unique($numbers)
$merged = $numbers->merge([6, 7]);              // \array_merge($numbers, [6, 7])
$sliced = $numbers->slice(1, 3);                // \array_slice($numbers, 1, 3)
$searched = $numbers->search(4);                // \array_search(4, $numbers)
$popped = $numbers->pop();                      // \array_pop($numbers)
$shifted = $numbers->shift();                   // \array_shift($numbers)
$sum = $numbers->sum();                         // \array_sum($numbers)
$product = $numbers->product();                 // \array_product($numbers)
$isList = $numbers->is_list();                  // \array_is_list($numbers)
$flipped = $numbers->flip();                    // \array_flip($numbers)
$chunked = $numbers->chunk(2);                  // \array_chunk($numbers, 2)
$reduced = $numbers->reduce(fn($c, $n) => $c + $n, 0); // \array_reduce($numbers, fn($c, $n) => $c + $n, 0)

// No prefix change (these don't start with array_)
$count = $numbers->count();                     // \count($numbers)
$numbers->sort();                               // \sort($numbers)
$numbers->rsort();                              // \rsort($numbers)
$numbers->shuffle();                            // \shuffle($numbers)
$current = $numbers->current();                 // \current($numbers)

// Adapted parameter order: array is not the first PHP parameter
$joined = $numbers->implode(", ");              // \implode(", ", $numbers)

// Encoding
$json = $numbers->json_encode();                // \json_encode($numbers)

// Utility
$isEmpty = $numbers->empty();                   // empty($numbers)
```

## Compiled PHP Output — Array Methods

```php
<?php

$numbers = [3, 1, 4, 1, 5];

$mapped = \array_map(fn($n) => $n * 2, $numbers);
$filtered = \array_filter($numbers, fn($n) => $n > 2);
$keys = \array_keys($numbers);
$values = \array_values($numbers);
$count = \count($numbers);
\sort($numbers);
$joined = \implode(", ", $numbers);
$json = \json_encode($numbers);
$isEmpty = empty($numbers);
```

## Method Chaining

Scalar pseudo-object methods can be chained for fluent-style operations. Each method call in the chain is independently rewritten to the corresponding PHP function call.

```tyhp
<?tyhp

string $input = "  Hello World  ";
$result = $input->trim()->tolower()->replace(" ", "-");
// Result: "hello-world"
```

```php
<?php
// PHP output — each chained method is expanded independently

$input = "  Hello World  ";
$result = \str_replace(" ", "-", \strtolower(\trim($input)));
```

## Calling on Literal Values

Pseudo-object methods can be called on literal values. For integer and float literals, wrap the value in parentheses.

```tyhp
<?tyhp

$char = (65)->chr();          // \chr(65) — returns 'A'
$rounded = (3.14159)->round(2); // \round(3.14159, 2) — returns 3.14
$parts = "a,b,c"->explode(","); // \explode(",", "a,b,c")
```

## Complete Method Reference

## int Methods

- `abs(): int` — Absolute value
- `ceil(): int` — Round up
- `floor(): int` — Round down
- `decbin(): string` — Decimal to binary string
- `dechex(): string` — Decimal to hexadecimal string
- `decoct(): string` — Decimal to octal string
- `intdiv(int $num2): int` — Integer division
- `max(int|float ...$values): float|int` — Maximum of values
- `min(int|float ...$values): float|int` — Minimum of values
- `chr(): string` — ASCII character for code point
- `__toFloat(): float` — Convert to float
- `__toString(): string` — Convert to string
- `empty(): bool` — Check if empty (zero)

## float Methods

- `abs(): float` — Absolute value
- `ceil(): int` — Round up
- `floor(): int` — Round down
- `round(int $precision = 0, int $mode = PHP_ROUND_HALF_UP): float` — Round to precision
- `sqrt(): float` — Square root
- `log(float $base = M_E): float` — Natural logarithm
- `log10(): float` — Base-10 logarithm
- `exp(): float` — e raised to the power
- `sin(): float` — Sine
- `cos(): float` — Cosine
- `tan(): float` — Tangent
- `asin(): float` — Arc sine
- `acos(): float` — Arc cosine
- `atan(): float` — Arc tangent
- `deg2rad(): float` — Degrees to radians
- `rad2deg(): float` — Radians to degrees
- `fdiv(float $num2): float` — Float division (IEEE 754)
- `fmod(float $num2): float` — Float modulus
- `is_finite(): bool` — Check if finite
- `is_infinite(): bool` — Check if infinite
- `is_nan(): bool` — Check if NaN
- `max(int|float ...$values): float|int` — Maximum of values
- `min(int|float ...$values): float|int` — Minimum of values
- `format(int $decimals = 0, ?string $decimal_separator = ".", ?string $thousands_separator = ","): string` — Format as string (maps to number_format)
- `__toInt(): int` — Convert to int
- `__toString(): string` — Convert to string
- `empty(): bool` — Check if empty (zero)

## Best Practices

:::tip
Use pseudo-object syntax for readability — $name->trim()->tolower() reads more naturally than \strtolower(\trim($name)).
:::

:::tip
Chain methods for fluent-style operations. Each call in the chain compiles to a nested PHP function call with no overhead.
:::

:::tip
Use pseudo-object methods on literal values by wrapping them in parentheses: (65)->chr() returns 'A', (3.14)->round(1) returns 3.1.
:::

:::tip
Remember that array methods like map() and filter() reorder parameters — the array becomes the calling object and the callback becomes the first argument.
:::

## Common Mistakes

:::danger
Don't expect actual object instances — these are compile-time rewrites. Calling a non-existent pseudo-object method produces a compile error.
:::

:::danger
Don't try to assign scalar methods to variables — $fn = $name->trim is not valid. These are method calls, not callable properties.
:::

:::danger
Don't confuse Tyhp's scalar pseudo-object methods with PHP's arrow functions. fn($x) => $x + 1 is a PHP anonymous arrow function, while $x->abs() is a scalar pseudo-object method call.
:::

:::danger
Don't assume in-place mutation — methods like sort() and shuffle() on arrays may or may not mutate in place depending on the underlying PHP function. Check the PHP documentation for each function's behavior.
:::
