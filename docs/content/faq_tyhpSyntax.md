---
title: 'FAQ: Tyhp Syntax'
---

## Do I have to type every variable?

Not explicitly. Tyhp supports type inference on first assignment. When you write `$x = 42;` without a type annotation, Tyhp infers that `$x` is `int`. You only need explicit type annotations when the type cannot be inferred (no initializer) or when you want a wider type than inference would give. Variables without both a type annotation and an inferable initializer always produce an error.

## What happens to types at runtime?

Tyhp's type system is entirely compile-time. Types, generics, type aliases, and type guards are all erased during compilation. The compiled PHP output contains only the type hints that PHP itself supports (parameter types, return types, property types). Tyhp-only types like generics and type aliases leave no trace in the output.

## Can I mix Tyhp and PHP files in the same project?

Yes. A Tyhp project can contain both `.tyhp` files (strongly typed, processed by the compiler) and `.php` files (passed through unchanged). You can even mix Tyhp and PHP within a single file using the `<?tyhp` and `<?php` open tags. Code inside `<?tyhp` blocks is type-checked and compiled; code inside `<?php` blocks is passed through as-is.

## How do generics work without runtime support?

Generics are enforced entirely at compile time through type erasure. When you write a generic class or function, the Tyhp compiler verifies that all type parameters are used correctly, then removes the generic type information from the compiled output. The resulting PHP code operates on unparameterized types. This is similar to how Java handles generics.

```tyhp
<?tyhp
function first<T>(array<T> $items): T {
    return $items[0];
}

int $n = first<int>([1, 2, 3]);
```

The compiled PHP has no trace of the generic type parameter `T` — it is used only for compile-time type checking.

## What is the difference between fn (Tyhp arrow functions) and fn (PHP arrow functions)?

In PHP, `fn` creates a short closure (arrow function) with a single expression body. Tyhp extends this: `fn` in Tyhp can also have a block body and supports full type annotations on parameters and return types. Tyhp arrow functions are compiled to PHP closures. The key difference is that Tyhp's `fn` is type-checked and supports generics, while PHP's `fn` has only basic type hints.

## How do I handle nullable types?

All types in Tyhp are non-nullable by default. To allow null, prefix the type with <code>?</code> or use a union type with <code>null</code>:

```tyhp
<?tyhp
?string $name = null;       // nullable via ? prefix
string|null $name2 = null;   // nullable via union
```

Before using a nullable value where a non-nullable type is expected, you must narrow the type with a null check. The Tyhp checker automatically tracks this through control flow analysis:

```tyhp
<?tyhp
function greet(?string $name): string {
    if ($name !== null) {
        // $name is automatically narrowed to string here
        return "Hello, " . $name;
    }
    return "Hello, stranger";
}
```

## Can I use eval() in Tyhp?

By default, `eval()` is disabled in Tyhp for security and type-safety reasons — the compiler cannot verify types inside dynamically evaluated strings. If you absolutely need it, you can re-enable it with `build.allowEval: true` in `tyhp.json`. However, code inside `eval()` is not type-checked. A better alternative is to write the dynamic code in a PHP file and import it via tyhpdef.

## How do extensions differ from traits?

Extensions and traits are both mechanisms for adding functionality to classes, but they work differently. Traits are a PHP feature where methods are copied into a class at the source level. Extensions are a Tyhp feature that lets you add methods to existing classes (even third-party classes) without modifying their source code. Extension methods are compiled to standalone functions that take the target object as the first parameter.

```tyhp
<?tyhp
extension StringExtensions {
    function toTitleCase(extends string $this): string {
        return \ucwords(\strtolower($this));
    }
}

use extension StringExtensions;
string $title = "hello world"->toTitleCase();
```

## What are structs used for?

Structs provide a typed alternative to raw associative arrays. They define a fixed set of named, typed properties and are compiled to PHP arrays by default. Structs are value types — they are copied on assignment and compared by value, not by reference — and use structural (schema-based) typing, meaning two structs with compatible shapes are interchangeable.

```tyhp
<?tyhp
struct Point {
    float $x;
    float $y;
}

Point $p = new Point() with { x => 1.0, y => 2.5 };
float $distance = \sqrt($p->x ** 2 + $p->y ** 2);
```

## How do I use async/await?

Tyhp provides async/await syntax for asynchronous programming. Async functions return a `Promise<T>` and can use `await` to suspend execution until a promise resolves. At compile time, async/await is transformed into promise-based PHP code using the `tyhp/async` runtime package.

```tyhp
<?tyhp
async function fetchUser(int $id): Promise<User> {
    Response $response = await httpClient->get("/users/{$id}");
    return User::fromJson($response->body());
}
```
