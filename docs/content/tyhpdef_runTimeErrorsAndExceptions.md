---
title: 'Runtime Errors and Exceptions in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

While Tyhp performs extensive compile-time type checking, tyhpdef declarations describe external PHP code that the compiler cannot verify at compile time. If a tyhpdef declaration does not accurately reflect the actual PHP implementation, runtime errors can occur. This page covers how to declare exception classes, document throwable functions, and understand the runtime implications of tyhpdef mismatches.

## Declaring Exception Classes

Exception and error classes from PHP libraries can be declared in tyhpdef just like any other class. Declaring them provides the compiler with the exception hierarchy for type checking in catch blocks.

```tyhp
<?tyhpdef

class \InvalidArgumentException extends \LogicException {
    public function __construct(
        string $message = "",
        int $code = 0,
        ?\Throwable $previous = null
    ): void;
    public function getMessage(): string;
    public function getCode(): int;
    public function getFile(): string;
    public function getLine(): int;
    public function getTrace(): array;
    public function getPrevious(): ?\Throwable;
    public function getTraceAsString(): string;
}

class \RuntimeException extends \Exception {
    public function __construct(
        string $message = "",
        int $code = 0,
        ?\Throwable $previous = null
    ): void;
}
```

## Declaring Custom Exception Hierarchies

When a PHP library defines its own exception classes, declare them with their full inheritance chain so the compiler can validate catch blocks and exception type narrowing.

```tyhp
<?tyhpdef

namespace Guzzle\Exception {
    interface GuzzleException extends \Throwable {}

    class TransferException extends \RuntimeException implements GuzzleException {
        public function __construct(
            string $message = "",
            int $code = 0,
            ?\Throwable $previous = null
        ): void;
    }

    class RequestException extends TransferException {
        public function getRequest(): \Psr\Http\Message\RequestInterface;
        public function getResponse(): ?\Psr\Http\Message\ResponseInterface;
        public function hasResponse(): bool;
    }

    class ConnectException extends TransferException {
        public function getRequest(): \Psr\Http\Message\RequestInterface;
    }

    class ClientException extends RequestException {}
    class ServerException extends RequestException {}
}
```

## Documenting Throwable Functions with @throws

Use the @throws doc comment annotation on function and method declarations to document which exceptions a function can throw. Tyhp records these annotations for humans and for future tooling. This alpha does **not** require callers to catch or declare them — there is no checked-exception / throws-effect analysis yet.

```tyhp
<?tyhpdef

/**
 * @throws \JsonException When JSON encoding fails with JSON_THROW_ON_ERROR
 */
function \json_encode(
    mixed $value,
    int $flags = 0,
    int $depth = 512
): string|false;

/**
 * @throws \InvalidArgumentException When the date format is invalid
 * @throws \RuntimeException When timezone data is unavailable
 */
function \date_create_from_format(
    string $format,
    string $datetime,
    ?\DateTimeZone $timezone = null
): \DateTime|false;

class DatabaseConnection {
    /**
     * @throws \PDOException When the connection fails
     */
    public function __construct(
        string $dsn,
        ?string $username = null,
        ?string $password = null,
        ?array $options = null
    ): void;

    /**
     * @throws \PDOException When the query is invalid
     */
    public function query(string $sql): \PDOStatement;
}
```

## Variable and Constant Imports

Tyhpdef global imports are type information only. This alpha does **not** emit runtime `ErrorException` checks or apply tyhpdef `??` defaults. Handle missing PHP globals at the use site.

## Class and Interface Mismatch Errors

If a tyhpdef declaration says a class has certain methods, properties, or constants that the actual PHP class does not have, you may get runtime errors when the code attempts to access them. The compiler trusts tyhpdef declarations at compile time, so mismatches are only caught at runtime.

```php
<?php

class User {
    public string $name;
    // Missing: public function getEmail(): string
}
```

```tyhp
<?tyhpdef

class User {
    public string $name;
    public function getEmail(): string;
}
```

In this example, Tyhp code calling $user->getEmail() will compile without errors because the tyhpdef says the method exists. At runtime, PHP will throw: Fatal error: Call to undefined method User::getEmail().

## Type Mismatch Errors

If a tyhpdef declares a function's return type differently from what the PHP function actually returns, the Tyhp compiler will accept the tyhpdef's type at compile time, but runtime behavior may differ.

```tyhp
<?tyhpdef

// Declared as always returning string, but the actual PHP function
// can return false on failure
function \file_get_contents(string $filename): string;
```

If the tyhpdef omits the false return type, the compiler will not require null/false checks at call sites, potentially leading to unhandled runtime failures.

## Prevention Strategies

1. Keep tyhpdef declarations synchronized with the actual PHP code they describe (by hand in this alpha — `generate_tyhpdef` is not shipped)
2. Run `tyhp lint` so tyhpdef files parse and bind
3. Use automated testing and CI/CD pipelines to catch runtime mismatches early
4. Handle missing PHP globals at the Tyhp use site (`isset`, `??` in **code**, nullable types)
5. Declare complete exception hierarchies so catch blocks have accurate type information
6. Do not omit failure return types (like false or null) from function declarations — include them even if they make the types more complex

:::tip
DO: Include failure return types (false, null) in your tyhpdef function declarations even when they make the type more complex. Accurate types prevent unhandled runtime failures.
:::

:::tip
DO: Declare the full exception class hierarchy for libraries you depend on. This allows the compiler to validate catch block type narrowing and prevents catching overly broad exception types.
:::

:::danger
DON'T: Narrow return types in tyhpdef to be more specific than the actual PHP function. If a PHP function returns string|false, do not declare it as returning just string — this hides potential runtime failures.
:::

:::danger
DON'T: Declare methods or properties in tyhpdef that do not exist on the actual PHP class. The compiler will trust your declarations, and runtime errors will result when the missing members are accessed.
:::

## Summary

- Exception classes are declared in tyhpdef like any other class, with their full inheritance chain
- Use `@throws` doc comment annotations to document which exceptions functions and methods can throw (documentation only in this alpha — the compiler does not enforce that callers handle them)
- Tyhpdef global imports are type information only — tyhpdef `??` is not applied in this alpha
- Tyhpdef declarations are trusted at compile time; mismatches between tyhpdef and PHP cause runtime errors
- Always declare accurate return types including failure types (`false`, `null`)
- Keep declarations synchronized with PHP by hand in this alpha (`generate_tyhpdef` is Story 20 and is not shipped)
