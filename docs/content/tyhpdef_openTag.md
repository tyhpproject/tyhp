---
title: 'The <?tyhpdef Open Tag'
status:
  tier: 0
  story: '02'
  state: complete
---

Every Tyhpdef file begins with the `<?tyhpdef` open tag. This tag tells the Tyhp compiler that the file contains declaration-only code describing external PHP constructs. Unlike `<?tyhp` files, Tyhpdef files are never compiled into PHP output — they exist solely to inform the compiler's type system.

## File Extension

Tyhpdef files use the `.tyhpdef` file extension. The compiler uses this extension, along with the open tag, to identify and parse Tyhpdef files separately from regular Tyhp source files.

## Basic File Structure

A Tyhpdef file starts with the open tag and then contains top-level declarations. There is no closing tag.

```tyhp
<?tyhpdef

const string APP_NAME;

function myPhpFunction(string $input): int;

class MyPhpClass {
    public string $name;
    public function getName(): string;
}
```

## Using Namespaces

Tyhpdef files support namespace declarations, both as a simple file-level declaration and as grouped blocks. Use namespaces to organize your declarations just like you would in PHP.

```tyhp
<?tyhpdef

// Simple namespace declaration (applies to the entire file)
namespace App\Services;

class PaymentGateway {
    public function charge(float $amount): bool;
}
```

You can also use grouped namespace blocks to declare items in multiple namespaces within a single file:

```tyhp
<?tyhpdef

namespace App\Models {
    class User {
        public int $id;
        public string $email;
    }
}

namespace App\Services {
    class UserService {
        public function find(int $id): \App\Models\User;
    }
}
```

## Use Statements

Tyhpdef files support use statements for importing types from other namespaces, just like PHP and Tyhp. This includes grouped use declarations and typed use declarations for functions and constants.

```tyhp
<?tyhpdef

namespace App\Handlers;

use App\Models\User;
use App\Models\{Order, Product};
use function App\Helpers\{formatCurrency, formatDate};
use const App\Config\{MAX_RETRIES, TIMEOUT};

class OrderHandler {
    public function process(User $user, Order $order): Product;
}
```

## Top-Level Declarations

The following declarations are valid at the top level of a Tyhpdef file:

- Namespace declarations
- Use / import statements
- Function declarations (signatures only)
- Class declarations
- Interface declarations
- Trait declarations
- Enum declarations
- Struct declarations
- Type alias declarations
- Constant declarations
- Variable declarations

:::tip
DO keep your Tyhpdef files organized: one file per library or extension, with a clear namespace structure. Name your files descriptively, e.g., MyLibrary.tyhpdef or ExtRedis.tyhpdef.
:::

:::danger
DON'T include implementation code in Tyhpdef files. No function bodies, no property initializers, no executable statements. Tyhpdef is declarations only.
:::
