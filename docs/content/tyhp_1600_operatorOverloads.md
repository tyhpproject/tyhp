---
title: 'Operator Overloads'
status:
  tier: 0
  story: '11'
  state: complete
---

Tyhp allows you to define custom behavior for operators on classes, enums, traits, and interfaces. Operator overloads enable intuitive syntax like $a + $b where $a and $b are object instances. The compiler rewrites operator usage into method calls in the PHP output. Operator overloads can be declared on classes, enums, traits, and interfaces (the same class statement list). Structs cannot declare operators — struct bodies are properties only. Trait methods that use an operator on `$this` emit `static::__add` (and the matching `__*` name) so the composing class's overload late-binds.

:::note
Every operator overload compiles to a STATIC method, with a single exception: convert's to-form __to{T}() is an INSTANCE method so it can satisfy PHP's \Stringable and the \Tyhp\Contracts\*Convertible instance interfaces. Multiple forms of the same operator collapse into ONE method whose operands and return use union types; that method dispatches internally on the runtime operand types and throws \Tyhp\Exceptions\InvalidParametersForOperatorOverloadException for a combination no form accepts. The generated method name is reserved — declaring an operator forbids a hand-written method of the same name.
:::

## Binary Operators

Binary operator overloads take two parameters. At least one side must be self (the defining class type). When both operands are objects with the same operator defined, the left operand's class takes precedence (left-first resolution).

```tyhp
<?tyhp

class Money
{
    public function __construct(private int $cents): void {}

    // self + self
    operator +(self $left, self $right)
    {
        return new static($left->cents + $right->cents);
    }

    // self + int (adding raw cents)
    operator +(self $left, int $right)
    {
        return new static($left->cents + $right);
    }

    // int + self (distinguishable from the form above by operand position)
    operator +(int $left, self $right): int
    {
        return $left + $right->cents;
    }

    // self - self
    operator -(self $left, self $right)
    {
        return new static($left->cents - $right->cents);
    }
}
```

## Compiled PHP Output for Binary Operators

All forms of one operator collapse into a single static method with union-typed operands and return. The method dispatches on the runtime operand types (instanceof for objects, is_int/is_float/is_string/is_bool/is_array for builtins) and throws for an unaccepted combination.

```php
<?php

class Money
{
    public function __construct(private int $cents) {}

    public static function __add(self|int $l, self|int $r): static|int
    {
        if ($l instanceof self && $r instanceof self) {
            $left = $l; $right = $r;
            return new static($left->cents + $right->cents);
        }
        elseif ($l instanceof self && \is_int($r)) {
            $left = $l; $right = $r;
            return new static($left->cents + $right);
        }
        elseif (\is_int($l) && $r instanceof self) {
            $left = $l; $right = $r;
            return $left + $right->cents;
        }
        else {
            throw new \Tyhp\Exceptions\InvalidParametersForOperatorOverloadException(static::class, __FUNCTION__, $l, $r);
        }
    }

    public static function __subtract(self $l, self $r): static
    {
        if ($l instanceof self && $r instanceof self) {
            $left = $l; $right = $r;
            return new static($left->cents - $right->cents);
        }
        else {
            throw new \Tyhp\Exceptions\InvalidParametersForOperatorOverloadException(static::class, __FUNCTION__, $l, $r);
        }
    }
}
```

## Call-Site Rewriting

At call sites, operator usage is rewritten to static method calls. Binary expressions resolve the left operand first; the original operand order is preserved in the arguments.

```tyhp
<?tyhp

$a = new Money(500);
$b = new Money(300);

Money $c = $a + $b;        // left is self: \Money::__add($a, $b)
Money $d = $a + 100;       // left is self: \Money::__add($a, 100)
int $e = 100 + $a;         // right is self: \Money::__add(100, $a)
```

```php
<?php

$a = new Money(500);
$b = new Money(300);

$c = \Money::__add($a, $b);
$d = \Money::__add($a, 100);
$e = \Money::__add(100, $a);
```

**Tyhpdef native passthrough:** a bodyless `operator +(…): T;` (no `extension` keyword) on a
tyhpdef class means the PHP type already supports the operator. Call sites keep the PHP operator —
they are **not** rewritten to `__add`. Mapped overloads use `extension operator` with a required
body; see [Tyhpdef classes — Operator Overloads](tyhpdef_classes.md#operator-overloads).

## Unary Operators

Unary operator overloads take a single self parameter and emit as static methods.

Unary operators compile to methods with fixed names:

- + (as-numeric) -> __asNumeric(self $o): int|float
- - (negation) -> __negate(self $o): <return_type>
- ! (boolean not) -> __not(self $o): <return_type>
- ~ (bitwise not) -> __bwNot(self $o)
- ++ (increment) -> __increment(self $o): self
- -- (decrement) -> __decrement(self $o): self

## Comparison Operators

Comparison operators return bool (int for spaceship). They emit as static methods just like other binary operators.

Comparison operators compile to these method names:

- < -> __isLessThan($l, $r): bool
- <= -> __isLessThanOrEqual($l, $r): bool
- > -> __isGreaterThan($l, $r): bool
- >= -> __isGreaterThanOrEqual($l, $r): bool
- == -> __isEqual($l, $r): bool
- != -> __isNotEqual($l, $r): bool
- === -> __isExact($l, $r): bool
- !== -> __isNotExact($l, $r): bool
- <=> -> __compare($l, $r): int

## Conversion Operators

The convert operator enables type conversion. Converting FROM another type returns self (the return type is implied) and emits as a static __from method. Converting TO another type requires an explicit return type and emits as an INSTANCE __to{T}() method. Declaring a convert-to also auto-adds the matching \Tyhp\Contracts\*Convertible interface to the class. A self->self convert is a compile-time error.

```tyhp
<?tyhp

class Celsius
{
    public function __construct(private float $degrees): void {}

    // Convert FROM int/float to self (return type of self is implied)
    operator convert(int $value)   { return new static((float)$value); }
    operator convert(float $value) { return new static($value); }

    // Convert FROM self TO float/int (explicit return type)
    operator convert(self $value): float { return $value->degrees; }
    operator convert(self $value): int   { return (int)\round($value->degrees); }
}
```

## Compiled PHP Output for Conversion Operators

```php
<?php

class Celsius implements \Tyhp\Contracts\FloatConvertible, \Tyhp\Contracts\IntConvertible
{
    public function __construct(private float $degrees) {}

    public static function __from(int|float $from): self
    {
        if (\is_int($from))   { $value = $from; return new static((float)$value); }
        elseif (\is_float($from)) { $value = $from; return new static($value); }
        else { throw new \Tyhp\Exceptions\InvalidParametersForOperatorOverloadException(static::class, __FUNCTION__, $from); }
    }

    public function __toFloat(): float { $value = $this; return $value->degrees; }

    public function __toInt(): int { $value = $this; return (int)\round($value->degrees); }
}
```

:::note
Casts rewrite to the instance convert-to method: (int)$c -> $c->__toInt(), (float)$c -> $c->__toFloat(). Call arguments and `return` expressions that need the same conversion (object where a scalar/named type is expected, or a source type where a class with convert-from is expected) rewrite the same way: `$c` passed to an `int` parameter becomes `$c->__toInt()`, and `42` passed where `Celsius` is expected becomes `\Celsius::__from(42)`. String context works through \Stringable when a convert(self): string is declared (-> __toString).
:::

## Word Operator: empty

The empty word operator defines how an object behaves in emptiness checks. It takes a single self parameter and returns bool. The true, false, and null operators do NOT exist — express truthiness via operator convert(self): bool (-> __toBool).

```tyhp
<?tyhp

class OptionalValue
{
    private mixed $value;

    operator empty(self $value)
    {
        return $value->value === null || $value->value === '';
    }
}
```

empty compiles to a static __isEmpty(self $o): bool. Its call site rewrites so a null/unset receiver short-circuits: empty($o) -> (empty($o) || \Type::__isEmpty($o)).

## Expression Body Shorthand

Operator overloads support the => expr shorthand instead of a full block body.

```tyhp
<?tyhp

class Point
{
    public function __construct(public float $x, public float $y): void {}

    operator +(self $left, self $right) => new static($left->x + $right->x, $left->y + $right->y);
    operator -(self $left, self $right) => new static($left->x - $right->x, $left->y - $right->y);
    operator -(self $value) => new static(-$value->x, -$value->y);
    operator ==(self $left, self $right) => $left->x === $right->x && $left->y === $right->y;
}
```

## Abstract and Final Operators

Operator overloads support abstract and final. An abstract operator introduces an abstract generated method that a subclass's operator satisfies; a final operator cannot be overridden.

```tyhp
<?tyhp

abstract class Shape
{
    abstract operator ==(self $left, self $right);
}

class Circle extends Shape
{
    public float $radius;

    operator ==(self $left, self $right)
    {
        return $left->radius === $right->radius;
    }
}
```

## Compound Assignment Operators

Compound assignment operators (+=, -=, *=, etc.) are derived from their corresponding binary operators. You do not declare them separately.

```tyhp
<?tyhp

Money $wallet = new Money(1000);
Money $cost = new Money(250);

$wallet += $cost;  // compiles to: $wallet = \Money::__add($wallet, $cost)
```

```php
<?php

$wallet = new Money(1000);
$cost = new Money(250);

$wallet = \Money::__add($wallet, $cost);
```

## Supported Operators Reference

The following operators can be overloaded. Each shows the operator, kind (unary/binary), and the generated static PHP method name.

## Arithmetic Operators

:::member[+ (unary)]
As-numeric. Method: __asNumeric(self $o): int|float
:::

:::member[+ (binary)]
Addition. Method: __add($l, $r)
:::

:::member[- (unary)]
Negation. Method: __negate(self $o)
:::

:::member[- (binary)]
Subtraction. Method: __subtract($l, $r)
:::

:::member[* (binary)]
Multiplication. Method: __multiply($l, $r)
:::

:::member[/ (binary)]
Division. Method: __divide($l, $r)
:::

:::member[% (binary)]
Modulus. Method: __mod($l, $r)
:::

:::member[** (binary)]
Power. Method: __pow($l, $r)
:::

:::member[++ (unary)]
Increment. Method: __increment(self $o): self
:::

:::member[-- (unary)]
Decrement. Method: __decrement(self $o): self
:::

## Bitwise Operators

:::member[~ (unary)]
Bitwise not. Method: __bwNot(self $o)
:::

:::member[<< (binary)]
Shift left. Method: __bwSL($l, $r)
:::

:::member[>> (binary)]
Shift right. Method: __bwSR($l, $r)
:::

:::member[& (binary)]
Bitwise AND. Method: __bwAnd($l, $r)
:::

:::member[^ (binary)]
Bitwise XOR. Method: __bwXor($l, $r)
:::

:::member[| (binary)]
Bitwise OR. Method: __bwOr($l, $r)
:::

## String and Comparison Operators

:::member[. (binary)]
String concat. Method: __concat($l, $r)
:::

:::member[< (binary)]
Less than. Method: __isLessThan($l, $r): bool
:::

:::member[<= (binary)]
Less than or equal. Method: __isLessThanOrEqual($l, $r): bool
:::

:::member[> (binary)]
Greater than. Method: __isGreaterThan($l, $r): bool
:::

:::member[>= (binary)]
Greater than or equal. Method: __isGreaterThanOrEqual($l, $r): bool
:::

:::member[== (binary)]
Equal. Method: __isEqual($l, $r): bool
:::

:::member[!= (binary)]
Not equal. Method: __isNotEqual($l, $r): bool
:::

:::member[=== (binary)]
Exact. Method: __isExact($l, $r): bool
:::

:::member[!== (binary)]
Not exact. Method: __isNotExact($l, $r): bool
:::

:::member[<=> (binary)]
Spaceship. Method: __compare($l, $r): int
:::

## Boolean, Word, and Conversion

:::member[! (unary)]
Boolean not. Method: __not(self $o)
:::

:::member[empty (word)]
Empty check. Method: __isEmpty(self $o): bool
:::

:::member[convert (from)]
Convert other type to self. Method: static __from($from): self
:::

:::member[convert (to)]
Convert self to other type. Instance method __to{T}(): T (e.g. __toInt, __toFloat, __toString, __toBool, __toDecimal); auto-adds the matching *Convertible interface
:::

## Reserved Names

Generated operator method names are reserved. If a class declares an operator and also hand-writes a method with the generated name (e.g. operator + and __add), it is a compile-time error. Names are reserved only when the corresponding operator is declared, so a class without a convert(self): string may still hand-write __toString.

## Resolution Order

1. If no operands are objects, standard PHP behavior applies.
2. For a unary operator, the operand's class defines the overload.
3. For a binary operator, the LEFT operand's class is checked first for a form matching (typeof left, typeof right); if it matches, emit \LeftType::__op($left, $right).
4. Otherwise the RIGHT operand's class is checked for a matching form; if it matches, emit \RightType::__op($left, $right) (operand order preserved).
5. If no match is found, emit a compiler error.
6. All forms of a given operator must be mutually distinguishable by operand type, otherwise the collapsed dispatch would be ambiguous (compile-time error).

## Best Practices

:::tip
Use operator overloading to make domain objects intuitive — Money, Vector, Color, Matrix and similar types are great candidates.
:::

:::tip
Implement related operators together. If you implement +, also implement -. If you implement <, also implement == and <=> for consistent behavior.
:::

:::tip
To support your type on either side of a binary operator, add a second form with self as the other operand (e.g. operator +(int $left, self $right)); the forms must be mutually distinguishable.
:::

:::tip
Use the => shorthand for simple one-expression operators to keep class definitions concise.
:::

:::tip
Define convert operators for seamless type integration — convert-to instance methods let your objects satisfy the *Convertible interfaces and cast cleanly.
:::

## Common Mistakes

:::danger
Do not define operators on structs — struct bodies are properties only. Traits and interfaces may declare `operator` like classes.
:::

:::danger
Do not create binary operators where neither operand is self — at least one side must be the defining class type.
:::

:::danger
Do not hand-write a method whose name collides with a declared operator's generated name — the name is reserved.
:::

:::danger
Do not define the convert operator with both parameter AND return type as self — self->self conversion is not allowed.
:::

:::danger
Do not define two forms of the same operator that can both match the same runtime operand combination — forms must be mutually distinguishable.
:::

## Compiler Errors

- Defining operator overloads on structs.
- Binary operator where neither operand is self.
- Unary operator where the operand is not self.
- A hand-written method conflicting with a declared operator's reserved generated name.
- Two forms of the same operator that are not mutually distinguishable.
- The convert operator with both parameter and return type as self.
- A decimal convert target when tyhp/decimal is not a project dependency.
- Missing abstract operator implementation in a concrete child class.
