---
title: 'Trait Requirements'
status:
  tier: 0
  story: '08'
  state: complete
---

In PHP, traits cannot specify that the using class must extend a specific base class or implement certain interfaces. Tyhp adds extends and implements support directly on trait declarations as compile-time constraints. These requirements are validated at compile time and completely erased from the PHP output. This is similar to Hack's require extends and require implements.

## Trait extends Requirement

A trait can declare that any class using it must extend a specific base class. This allows the trait to safely use methods and properties from that base class, because the compiler guarantees the using class extends it.

```tyhp
<?tyhp

trait TimestampedEntity extends Entity
{
    protected \DateTime $createdAt;
    protected \DateTime $updatedAt;

    public function touch(): void
    {
        $this->updatedAt = new \DateTime();
    }

    public function getEntityLabel(): string
    {
        // Safe: compiler guarantees Entity::getId() is available
        return static::class . ':' . $this->getId();
    }
}
```

## Trait implements Requirement

A trait can declare that any class using it must implement one or more interfaces. This ensures that the trait's methods can rely on interface methods being present on the using class.

```tyhp
<?tyhp

trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }

    public function toCacheValue(): string
    {
        // Safe: Serializable::serialize() is guaranteed
        return $this->serialize();
    }
}
```

## Using Traits with Requirements

The class using the trait must satisfy all declared requirements. The compiler validates this at compile time and emits an error if any requirement is not met.

```tyhp
<?tyhp

// This is OK: User extends Entity and implements Serializable
class User extends Entity implements Serializable
{
    use Cacheable;
    use TimestampedEntity;

    public function serialize(): string { /* ... */ }
    public function unserialize(string $data): void { /* ... */ }
}

// COMPILE ERROR: Post does not extend Entity
// class Post {
//     use Cacheable;  // Error: Cacheable requires extends Entity
// }

// COMPILE ERROR: Comment extends Entity but doesn't implement Serializable
// class Comment extends Entity {
//     use Cacheable;  // Error: Cacheable requires implements Serializable
// }
```

## Compiled PHP Output

Trait requirements are compile-time only. The extends and implements clauses are completely stripped from the PHP output. The trait is emitted as a standard PHP trait.

```tyhp
<?tyhp

trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }
}
```

```php
<?php

trait Cacheable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }
}
```

## Trait Property Aliasing

Tyhp extends PHP's trait aliasing to support property aliasing. In PHP, you can alias trait methods using as; in Tyhp, you can also alias trait properties using TraitName::$property as $newName. This is useful when two traits define a property with the same name.

```tyhp
<?tyhp

trait HasName
{
    public string $name = '';
}

trait HasLabel
{
    public string $name = '';
}

class Widget
{
    use HasName, HasLabel {
        HasLabel::$name as $label;
    }

    // Now $this->name comes from HasName
    // and $this->label comes from HasLabel (aliased)
}
```

## Compiled PHP Output for Property Aliasing

The compiler resolves the property aliasing and emits the appropriate PHP code. The alias becomes a separate property declaration in the class.

```php
<?php

class Widget
{
    use HasName, HasLabel {
        HasLabel::$name as $label;
    }
}
```

## Multiple Requirements

A trait can require at most one base class via extends (single inheritance), but can require multiple interfaces via implements.

```tyhp
<?tyhp

trait FullyManaged extends Entity implements Serializable, JsonSerializable
{
    public function toManagedArray(): array
    {
        return [
            'id' => $this->getId(),
            'serialized' => $this->serialize(),
            'json' => $this->jsonSerialize(),
        ];
    }
}
```

## Best Practices

:::tip
Use trait requirements to enforce contracts — if a trait relies on methods from a base class or interface, declare those requirements explicitly.
:::

:::tip
Document trait requirements even though they are compile-time only — they serve as documentation for which classes can use the trait.
:::

:::tip
Use trait property aliasing to resolve property name conflicts between traits instead of restructuring your trait hierarchy.
:::

:::tip
Prefer implements requirements over extends requirements when possible — interface requirements are more flexible and don't tie the trait to a specific class hierarchy.
:::

## Common Mistakes

:::danger
Do not assume trait requirements appear in PHP output — they are compile-time only and completely erased. The PHP trait has no extends or implements clause.
:::

:::danger
Do not specify multiple base classes with extends on a trait — PHP has single inheritance, so only one base class is allowed.
:::

:::danger
Do not use a trait on a class that does not satisfy its requirements — the compiler emits an error at compile time.
:::

```tyhp
<?tyhp

// ERROR: Cannot require multiple base classes
// trait Bad extends ClassA, ClassB { }

// ERROR: class does not extend Entity
// class StandaloneWidget {
//     use TimestampedEntity;  // Error: requires extends Entity
// }
```

## Compiler Errors

- Using a trait on a class that does not extend the required base class.
- Using a trait on a class that does not implement the required interfaces.
- Specifying multiple base classes in a trait's extends clause.
- Trait property conflicts without proper aliasing when two traits define the same property name.
