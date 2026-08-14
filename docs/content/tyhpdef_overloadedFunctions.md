---
title: 'Overloaded Functions and Methods in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef allows you to declare multiple signatures for the same function or method name. This is essential for describing PHP functions that accept different parameter types or counts and return different types depending on the arguments. In PHP, a single function like substr or json_decode handles multiple calling patterns internally. In tyhpdef, you describe each pattern as a separate overload signature so the Tyhp compiler can select the correct return type at each call site.

## Basic Function Overloads

To declare overloads, simply declare the same function name multiple times with different parameter signatures. Each declaration must have a unique combination of parameter types. The compiler internally constructs a combined encompassing signature from all declared overloads.

```tyhp
<?tyhpdef

function \substr(string $string, int $offset): string;
function \substr(string $string, int $offset, int $length): string;

function \parse(int $value): IntegerResult;
function \parse(string $value): StringResult;
function \parse(array $value): ArrayResult;
```

:::note
In tyhpdef, you do not need to specify a final encompassing signature that accepts all possible parameter types. The compiler constructs one internally by combining all declared overloads.
:::

## Method Overloads in Classes

Methods within class, interface, and trait declarations can also be overloaded. Each overload must have the same visibility modifier.

```tyhp
<?tyhpdef

class Repository {
    public function find(int $id): ?User;
    public function find(string $uuid): ?User;
    public function find(array<string, mixed> $criteria): array<User>;

    public function save(User $user): User;
    public function save(array<User> $users): array<User>;
}
```

## Overloads with Static Value Types

Literal values (static value types) can be used as parameter types in overloads. This allows the compiler to select different return types based on the literal value passed at a call site.

```tyhp
<?tyhpdef

function \getConfig(true $asArray): array<string, mixed>;
function \getConfig(false $asArray): string;

function \json_decode(string $json, true $associative): array<string, mixed>;
function \json_decode(string $json, false $associative): object;
function \json_decode(string $json, null $associative = null): mixed;
```

## Generic Overloads

Overloaded functions can use generic type parameters. Each overload can have its own generic parameter set.

```tyhp
<?tyhpdef

function \array_map<T, U>(
    callable<T, U> $callback,
    array<T> $array
): array<U>;

function \array_map<T1, T2, U>(
    callable<T1, T2, U> $callback,
    array<T1> $array1,
    array<T2> $array2
): array<U>;

function \array_map<T1, T2, T3, U>(
    callable<T1, T2, T3, U> $callback,
    array<T1> $array1,
    array<T2> $array2,
    array<T3> $array3
): array<U>;
```

## Async Overloads

When a function has async overloads, all overloads for that name must consistently use the async keyword.

```tyhp
<?tyhpdef

class HttpClient {
    async public function request(string $url): Response;
    async public function request(string $url, RequestOptions $options): Response;

    async public function get(string $url): Response;
    async public function post(string $url, mixed $body): Response;
}
```

## Overload Resolution

The compiler resolves overloads at compile time using the following priority order:

1. Exact type match — a parameter whose type exactly matches the argument type
2. Static value type match — a literal value type (like true, false, 0) that matches the argument's compile-time value
3. Compatible type match — a parameter whose type is a supertype of the argument type
4. Generic type inference — a generic overload whose type parameters can be inferred from the arguments

If no single overload matches or if multiple overloads match equally, the compiler reports an ambiguity error.

## Implementing Overloaded Interfaces in Tyhp

When implementing a class in Tyhp that has overloaded interface methods from tyhpdef, you only need to implement the single encompassing method that handles all overload variants.

```tyhp
<?tyhp

class UserRepository implements Repository {
    public function find(int|string|array<string, mixed> $idOrUuidOrCriteria): null|User|array<User> {
        if ($idOrUuidOrCriteria is int) {
            return $this->findById($idOrUuidOrCriteria);
        }
        if ($idOrUuidOrCriteria is string) {
            return $this->findByUuid($idOrUuidOrCriteria);
        }
        return $this->findByCriteria($idOrUuidOrCriteria);
    }
}

Repository $repo = new UserRepository();
?User $user1 = $repo->find(123);
?User $user2 = $repo->find("abc-123");
array<User> $users = $repo->find(['status' => 'active']);
```

## Deprecating Specific Overloads

Individual overloads can be marked as deprecated or obsolete without affecting the other overloads of the same function.

```tyhp
<?tyhpdef

deprecated function \connectDb(string $connectionString): DbConnection;
function \connectDb(DbConfig $config): DbConnection;
```

:::tip
DO: Use overloads to describe PHP functions that behave differently based on parameter types or counts. This gives the Tyhp compiler precise return type information for each call pattern.
:::

:::tip
DO: Use static value types (like true, false, literal integers) in overloads when a PHP function returns different types based on a boolean or flag argument.
:::

:::danger
DON'T: Declare overloads with identical parameter type lists. Each overload must have a unique parameter signature — the compiler cannot distinguish between overloads that accept the same types.
:::

:::danger
DON'T: Mix async and non-async overloads for the same function name. If one overload is async, all overloads of that name must be async.
:::

## Summary

- Declare overloads by repeating the same function or method name with different parameter signatures
- Each overload must have a unique parameter signature
- The compiler internally constructs an encompassing signature from all overloads
- Overload resolution happens at compile time based on argument types
- Static value types (literal values) can be used as parameter types for precise overload matching
- Generic overloads are supported — each overload can have its own type parameters
- Async overloads must consistently use the `async` keyword across all overloads
- In Tyhp code, implementing an overloaded interface requires only the encompassing method
- Individual overloads can be independently marked as `deprecated` or `obsolete`
