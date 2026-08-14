---
title: 'Enums in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef lets you describe existing PHP enums to the Tyhp compiler. PHP supports two kinds of enums: unit enums (no backing value) and backed enums (each case has a string or int value). Both are fully supported in Tyhpdef, along with methods, interface implementations, and constants.

## Unit Enums

A unit enum has no backing type. Each case is a standalone value with no associated scalar.

```tyhp
<?tyhpdef

enum Status {
    case Active;
    case Pending;
    case Deleted;
}
```

## Backed Enums

A backed enum associates each case with a scalar value of a specific type — either `string` or `int`. When importing a backed enum in Tyhpdef, specifying case values is optional. The checker validates values that **are** present; omitted values are not treated as “matches any int/string.” Prefer listing known values when you have them.

```tyhp
<?tyhpdef

// String-backed enum
enum HttpMethod: string {
    case GET = "GET";
    case POST = "POST";
    case PUT = "PUT";
    case DELETE = "DELETE";
    case PATCH = "PATCH";
}

// Int-backed enum — values optional
enum LogLevel: int {
    case Debug = 0;
    case Info;
    case Warning;
    case Error = 3;
    case Critical;
}
```

:::note
Omitted backed-case values are allowed in the grammar. This alpha only checks values you write; it does not infer “any int” / “any string” from a missing `=`.
:::

## Enums with Methods

PHP enums can have methods. In Tyhpdef, you declare the method signatures just like class methods — signatures only, ending with semicolons.

```tyhp
<?tyhpdef

enum Color: string {
    case Red = "#FF0000";
    case Green = "#00FF00";
    case Blue = "#0000FF";

    public function toRgb(): array<int>;
    public function darken(float $amount): string;
    public static function fromRgb(int $r, int $g, int $b): self;
}
```

## Enums with Interface Implementation

Enums can implement interfaces. The implements clause follows the backing type (if any), and the interface methods are declared within the enum body.

```tyhp
<?tyhpdef

enum Permission: int implements \Stringable {
    case None = 0;
    case Read = 1;
    case Write = 2;
    case Admin = 4;

    public function __toString(): string;
    public function includes(self $other): bool;
}
```

## Enum Constants

Enums can declare constants, just like classes.

```tyhp
<?tyhpdef

enum Suit: string {
    case Hearts = "H";
    case Diamonds = "D";
    case Clubs = "C";
    case Spades = "S";

    public const int TOTAL_SUITS;
    public const array<string> RED_SUITS;
}
```

## Enum Aliasing

The same PHP enum can be imported multiple times with different aliases. This is useful when you need to import an enum under a different name or re-import it with different backing type information.

```tyhp
<?tyhpdef

// Import as unit enum (no backing type)
enum BadgeType {
    case GUEST;
    case EMPLOYEE;
    case MANAGER;
}

// Same enum imported with alias and backing type
enum BadgeType as BackedBadgeType: string {
    case GUEST;
    case EMPLOYEE = "emp";
    case MANAGER;
}
```

## Deprecated and Obsolete

The enum itself can be marked `deprecated` or `obsolete` (top-level). Markers on cases and methods parse but are not enforced in this alpha.

```tyhp
<?tyhpdef

deprecated enum LegacyPriority: int {
    case Low = 0;
    case Medium = 1;
    case High = 2;
    case Urgent = 3;
}
```

## Best Practices

:::tip
DO specify case values for backed enums when you know them. Present values are checked; omitted values are not a wildcard.
:::

:::tip
DO import only the enum cases you use in your Tyhp code. You don't need to declare every case from the PHP enum.
:::

:::danger
DON'T include method bodies on enum methods. All methods end with a semicolon — no curly-brace blocks.
:::

:::danger
DON'T declare a backing type that doesn't match the actual PHP enum. If the PHP enum is string-backed, the Tyhpdef declaration must also be string-backed.
:::
