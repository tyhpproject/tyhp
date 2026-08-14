---
title: 'Traits in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef lets you describe existing PHP traits to the Tyhp compiler. Traits in PHP provide a mechanism for code reuse across classes. When importing a trait, you declare the typed signatures of its public and protected members — methods, properties, and constants — so that Tyhp can type-check code that uses the trait.

## Basic Trait Declaration

Consider a PHP trait you want to use from Tyhp:

```php
<?php

trait TimestampedEntity {
    protected $createdAt;
    protected $updatedAt;

    public function touch(): void {
        $this->updatedAt = new DateTime();
    }

    abstract public function save(): void;
}
```

The Tyhpdef declaration adds full type information. Methods end with semicolons — no bodies:

```tyhp
<?tyhpdef

trait TimestampedEntity {
    protected \DateTime $createdAt;
    protected \DateTime $updatedAt;

    public function touch(): void;
    abstract public function save(): void;
}
```

## Properties

Trait properties are declared with visibility modifiers and types, just like class properties.

```tyhp
<?tyhpdef

trait HasSlug {
    public string $slug;
    protected ?string $slugSource;

    public function generateSlug(): string;
    public function getSlugAttribute(): string;
}
```

## Constants

Traits can declare typed constants.

```tyhp
<?tyhpdef

trait Configurable {
    public const string CONFIG_KEY;
    public const int DEFAULT_TIMEOUT;

    public function getConfig(string $key): mixed;
    public function setConfig(string $key, mixed $value): void;
}
```

## Abstract Methods

Traits can require using classes to implement specific methods by declaring them as abstract.

```tyhp
<?tyhpdef

trait Validatable {
    abstract public function validate(): bool;
    abstract protected function getRules(): array<string, string>;

    public function isValid(): bool;
    public function getErrors(): array<string>;
}
```

## Generic Traits

Traits support generic type parameters. When a class uses a generic trait, it must provide concrete type arguments.

```tyhp
<?tyhpdef

trait Collection<T> {
    public function add(T $item): void;
    public function remove(T $item): bool;
    public function contains(T $item): bool;
    public function getItems(): array<T>;
    public function count(): int;
}

trait KeyValueStore<TKey extends string|int, TValue> {
    public function get(TKey $key): ?TValue;
    public function set(TKey $key, TValue $value): void;
    public function has(TKey $key): bool;
}
```

## Using Imported Traits in Tyhp

Once a trait is declared in Tyhpdef, Tyhp classes can use it with full type checking. Generic traits require concrete type arguments at the use site.

```tyhp
<?tyhp

class BlogPost {
    use TimestampedEntity;
    use HasSlug;

    public function save(): void {
        $this->touch();
        // ... save implementation
    }
}

class UserCollection {
    use Collection<User>;

    public function getActiveUsers(): array<User> {
        return \array_filter(
            $this->getItems(),
            fn(User $u): bool => $u->isActive()
        );
    }
}
```

## Trait Conflict Resolution

When a class uses multiple traits that have methods with the same name, you can use PHP's conflict resolution syntax (`insteadof` and `as`) in your Tyhp code. The imported traits will carry their method signatures so Tyhp can validate the resolution.

```tyhp
<?tyhp

class AuditableUser {
    use TimestampedEntity, SoftDeletes {
        SoftDeletes::delete insteadof TimestampedEntity;
        TimestampedEntity::delete as hardDelete;
    }
}
```

## Deprecated and Obsolete

Both the trait itself and individual members can be marked as `deprecated` or `obsolete`.

```tyhp
<?tyhpdef

deprecated trait LegacyCacheable {
    public function cacheFor(int $seconds): void;
}

trait Cacheable {
    deprecated public function cacheForever(): void;
    public function cache(int $ttl): void;
    public function invalidateCache(): void;
}
```

## Best Practices

:::tip
DO declare only public and protected members. Private trait members are not accessible from outside and should be omitted.
:::

:::tip
DO mark abstract methods as abstract in your Tyhpdef declaration. This ensures Tyhp enforces that using classes implement them.
:::

:::danger
DON'T include method bodies in trait declarations. All methods must end with a semicolon.
:::

:::danger
DON'T include property default values. Properties are declared as type and name only.
:::
