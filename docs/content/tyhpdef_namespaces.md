---
title: 'Namespaces in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Namespaces in tyhpdef work just like in Tyhp and PHP, allowing you to organize declarations into logical groups. When you declare items inside a namespace block, the compiler registers them under that namespace. You can also import items from one PHP namespace and place them into a different namespace for use in Tyhp code, which is useful for reorganizing third-party library APIs.

## Basic Namespace Declarations

Use the namespace keyword followed by the namespace name and a block of declarations. All items inside the block belong to that namespace.

```tyhp
<?tyhpdef

namespace App\Models {
    class User {
        public int $id;
        public string $name;
        public string $email;

        public function __construct(
            string $name,
            string $email
        ): void;
    }

    class Post {
        public int $id;
        public string $title;
        public int $authorId;

        public function author(): User;
    }
}
```

## Semicolon Namespace Syntax

You can also use the semicolon namespace syntax, where all declarations following the namespace statement until the end of the file (or the next namespace statement) belong to that namespace.

```tyhp
<?tyhpdef

namespace App\Services;

class UserService {
    public function findById(int $id): ?\App\Models\User;
    public function findByEmail(string $email): ?\App\Models\User;
    public function create(string $name, string $email): \App\Models\User;
}
```

## Multiple Namespaces in One File

A single tyhpdef file can contain multiple namespace blocks. This is common when describing a PHP extension or library that uses several namespaces.

```tyhp
<?tyhpdef

namespace Psr\Log {
    interface LoggerInterface {
        public function emergency(string|\Stringable $message, array $context = []): void;
        public function alert(string|\Stringable $message, array $context = []): void;
        public function critical(string|\Stringable $message, array $context = []): void;
        public function error(string|\Stringable $message, array $context = []): void;
        public function warning(string|\Stringable $message, array $context = []): void;
        public function info(string|\Stringable $message, array $context = []): void;
        public function debug(string|\Stringable $message, array $context = []): void;
        public function log(mixed $level, string|\Stringable $message, array $context = []): void;
    }

    abstract class AbstractLogger implements LoggerInterface {
        public function emergency(string|\Stringable $message, array $context = []): void;
        public function alert(string|\Stringable $message, array $context = []): void;
    }
}

namespace Psr\Log\LogLevel {
    const string EMERGENCY;
    const string ALERT;
    const string CRITICAL;
    const string ERROR;
    const string WARNING;
    const string NOTICE;
    const string INFO;
    const string DEBUG;
}
```

## Importing Into a Different Namespace

You can import a PHP item from its original namespace and expose it under a different namespace in Tyhp. When a fully-qualified name with a leading backslash is used on a declaration, the item is imported from that PHP namespace but registered in the tyhpdef's declared namespace.

```php
<?php

namespace EmailerLib {
    class Emailer {
        public function __construct($subjectLine) { }
        public function connect($opt) { return true; }
    }
}

function testEmail($emailAddress) { return true; }
```

```tyhp
<?tyhpdef

namespace EL\Options {
    struct Connect {
        bool $verbose;
    }

    struct Statistics {
        bool $opened;
        ?bool $unopened;
        ?bool $bounced;
    }
}

namespace EL {
    class \EmailerLib\Emailer {
        public function __construct(string $subjectLine): void;
        public function connect(Options\Connect $opt): bool;
        public function getStatistics(Options\Statistics $opt): array<string, int>;
    }
}

namespace EL\Helpers {
    function \testEmail(string $emailAddress): bool;
}
```

:::note
When Tyhp compiles back to PHP, it uses the original PHP names and namespaces — not the tyhpdef namespace aliases. The namespace reorganization exists only at compile time in Tyhp.
:::

## Declaring PHP Extension Types

When writing tyhpdef files for PHP extensions, place declarations in the correct namespace. Most PHP extension classes and functions live in the global namespace, but some (like PDO, Reflection, and Random) use their own namespaces.

```tyhp
<?tyhpdef

// Global-namespace extension functions (no namespace block needed)
function \json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false;
function \json_decode(string $json, ?bool $associative = null, int $depth = 512, int $flags = 0): mixed;

// Classes in the global namespace
class JsonException extends \Exception {
    public function __construct(
        string $message = "",
        int $code = 0,
        ?\Throwable $previous = null
    ): void;
}

// Extension with its own namespace
namespace Random {
    class Randomizer {
        public function __construct(?\Random\Engine $engine = null): void;
        public function getInt(int $min, int $max): int;
        public function getBytes(int $length): string;
        public function shuffleArray(array $array): array;
    }

    interface Engine {
        public function generate(): string;
    }
}
```

:::tip
DO: Use braced namespace blocks when organizing multiple namespaces in a single tyhpdef file. This makes the file structure clear and avoids ambiguity about which namespace a declaration belongs to.
:::

:::tip
DO: Use the leading backslash on class names and function names when importing items from a different PHP namespace than the declared tyhpdef namespace.
:::

:::danger
DON'T: Mix semicolon-style and brace-style namespace declarations in the same file. Pick one style per file for consistency.
:::

:::danger
DON'T: Start namespace names with a leading backslash. Namespace declarations never use a leading backslash — only references to items from other namespaces do.
:::

## Summary

- Namespace declarations do not use a leading backslash
- Items imported from a different PHP namespace use a fully-qualified name with leading backslash
- Only the base name of the imported item is added to the declared namespace
- Both braced and semicolon-style namespace syntax are supported
- Multiple namespaces can coexist in one tyhpdef file using braced blocks
- Global functions imported into a namespace use a leading backslash on the original name
- Tyhp compiles back to the original PHP namespaces and names at emit time
