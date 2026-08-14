---
title: 'Type Aliases in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef supports declaring type aliases using the type keyword. Type aliases give a name to a complex type expression, making declarations more readable and reusable. In tyhpdef, type aliases are used to describe common PHP type patterns that lack a formal definition — such as union types representing valid option sets, array shapes, or callback signatures. Type aliases can be declared at the root level or within a namespace block.

## Basic Type Aliases

A type alias is declared with the type keyword, followed by the alias name, an equals sign, the underlying type expression, and a semicolon.

```tyhp
<?tyhpdef

type UserId = int;

type Email = string;

type Scalar = int|float|string|bool;

type OptionalString = ?string;

type StatusCode = 200|301|302|404|500;
```

## Union and Intersection Type Aliases

Type aliases can represent union types (using the pipe operator) and intersection types (using the ampersand operator). These are especially useful for describing PHP functions that accept or return multiple types.

```tyhp
<?tyhpdef

type Stringable = string|\Stringable;

type ArrayKey = int|string;

type JsonValue = string|int|float|bool|null|array<JsonValue>;

type Countable = \Countable&\Traversable;

type TagLine = string|callable<string>|CheesyTagLineGeneratorInterface;
```

## Generic Type Aliases

Type aliases can have generic type parameters, making them reusable across different concrete types. Generic parameters are declared in angle brackets after the alias name and can include constraints.

```tyhp
<?tyhpdef

type Collection<T> = array<T>;

type Dictionary<TValue> = array<string, TValue>;

type Pair<TFirst, TSecond> = array{TFirst, TSecond};

type Result<T, TError extends \Throwable> = T|TError;

type Predicate<T> = callable<T, bool>;

type Mapper<TIn, TOut> = callable<TIn, TOut>;

type Callback<TReturn extends void|never|mixed> = callable<string, TReturn>;
```

:::note
When a generic type alias wraps a callable, the constraint extends void|never|mixed on the return type parameter allows the alias to accept void and never as return types. Without this constraint, restricted types like void and never would be rejected as generic arguments.
:::

## Type Aliases in Namespaces

Type aliases can be declared inside namespace blocks to organize them logically.

```tyhp
<?tyhpdef

namespace App\Types {
    type Collection<T> = array<T>;
    type UserMap = Dictionary<User>;

    function first<T>(Collection<T> $items): ?T;
    function last<T>(Collection<T> $items): ?T;
}

namespace App\Http {
    type StatusCode = 200|301|302|404|500;
    type Headers = array<string, string|array<string>>;
    type ResponseBody = string|null;
}
```

## Using Type Aliases in Declarations

Once declared, type aliases can be used anywhere a type is expected — in function signatures, class member declarations, and other type aliases.

```tyhp
<?tyhpdef

type UserId = int;
type UserMap = array<UserId, User>;

function findUser(UserId $id): ?User;

function getAllUsers(): UserMap;

class UserService {
    public function getActive(): UserMap;
    public function findById(UserId $id): ?User;
}
```

:::tip
DO: Use type aliases for complex union or intersection types that are repeated across multiple function declarations. This keeps your tyhpdef files readable and consistent.
:::

:::tip
DO: Use generic type aliases with constraints to create reusable callable and collection type patterns.
:::

:::danger
DON'T: Declare type aliases inside class, interface, or trait blocks within tyhpdef. Type aliases in tyhpdef can only appear at the root level or within namespace blocks. Class-level type aliases are a Tyhp code feature, not a tyhpdef feature.
:::

:::danger
DON'T: Create circular type aliases where alias A references alias B and alias B references alias A. The compiler will detect and reject circular definitions.
:::

## Summary

- Type aliases use the syntax: `type Name = TypeExpr;`
- Generic type parameters are supported: `type Name<T> = TypeExpr;`
- Generic parameters can have constraints: `type Name<T extends SomeType> = TypeExpr;`
- Union and intersection types are fully supported in type alias definitions
- Type aliases in tyhpdef can only appear at root level or inside namespace blocks
- Type aliases are a compile-time construct — they are erased during PHP emission
- Type aliases are interchangeable with their underlying type at compile time
- Type aliases can reference other type aliases (but not circularly)
