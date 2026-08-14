---
title: 'Tyhpdef Quick Reference'
---

A condensed overview of Tyhpdef syntax — the declaration-only language for describing existing PHP code to the Tyhp compiler. Similar to TypeScript's `.d.ts` files. For full details, follow the link below each feature.

## The Open Tag

Every `.tyhpdef` file starts with the `<?tyhpdef` tag. Supports namespaces (semicolon and braced styles) and `use`/`import` statements. Only declarations are allowed — no function bodies or executable code.

<a href="tyhpdef_openTag.html">See full documentation →</a>

```tyhp
<?tyhpdef

const string APP_NAME;
function myPhpFunction(string $input): int;
```

## Variables and Constants

Declare global PHP variables and constants with types. Use `??` for fallback defaults, `as` for aliasing, and `&` for import-by-reference. Supports `deprecated`/`obsolete` markers.

<a href="tyhpdef_variablesAndConstants.html">See full documentation →</a>

```tyhp
bool $debugMode ?? false;
string $legacy_app_name as $appName ?? "DefaultApp";
int &$sharedCounter;

const string APP_ENV;
const float TAX_RATE ?? 0.08;
const int \MAX_LOOPS_ALLOWED as MAX_LOOPS;
```

## Functions

Declare function signatures with typed parameters (including optional, reference, and variadic), generic type parameters, async functions, function aliases, and extension functions.

<a href="tyhpdef_functions.html">See full documentation →</a>

```tyhp
function \array_map<T, U>(callable<T, U> $callback, array<T> $array): array<U>;
async function fetchUserData(int $userId): UserData;
function \array_key_exists as keyExists(int|string $key, array $array): bool;
function toCamelCase(extends string $str): string;
```

## Interfaces

Declare PHP interfaces with fully typed method signatures, constants, inheritance (`extends`), generic type parameters with constraints, method overloads, and async methods.

<a href="tyhpdef_interfaces.html">See full documentation →</a>

```tyhp
interface Repository<T> {
    public function find(int $id): ?T;
    public function save(T $entity): void;
}

interface Stream extends Readable, Writable {
    public function close(): void;
}
```

## Classes

Declare PHP classes with properties, methods, constructors (with parameter promotion), constants, extends/implements, generics, trait usage, operator overloads, and class aliasing with `as`.

<a href="tyhpdef_classes.html">See full documentation →</a>

```tyhp
class Collection<T> {
    public function add(T $item): void;
    public function map<U>(callable<T, U> $transform): Collection<U>;
}

class \Vendor\LongNamespace\SomeClass as ShortName {
    public function doWork(): void;
}
```

## Enums

Declare unit enums (no backing value) and backed enums (`string` or `int`). Supports case values, methods, interface implementation, constants, aliasing, and deprecated cases.

<a href="tyhpdef_enums.html">See full documentation →</a>

```tyhp
enum HttpMethod: string {
    case GET = "GET";
    case POST = "POST";
    case PUT = "PUT";
}

enum Permission: int implements \Stringable {
    case Read = 1;
    case Write = 2;
    public function __toString(): string;
}
```

## Traits

Declare PHP traits with typed properties, methods, constants, abstract methods, and generic type parameters. Generic traits specify concrete type arguments when used in Tyhp code.

<a href="tyhpdef_traits.html">See full documentation →</a>

```tyhp
trait Collection<T> {
    public function add(T $item): void;
    public function getItems(): array<T>;
}

// in Tyhp code:
use Collection<User>;
```

## Structs

Value types backed by PHP associative arrays. Support required and optional properties, property aliases for non-identifier array keys, defaults, inheritance, nesting, and generics.

<a href="tyhpdef_structs.html">See full documentation →</a>

```tyhp
struct UserProfile {
    string $name;
    ?string $bio;
    string $role = "user";
}

struct ApiResponse<T> {
    bool $success;
    ?T $data;
    ?string 'Reply-To' as $replyTo;
}
```

## Type Aliases

Create named aliases for complex type expressions using the `type` keyword. Supports generic parameters with constraints. Allowed at root level or inside namespace blocks.

<a href="tyhpdef_typeAliases.html">See full documentation →</a>

```tyhp
type Scalar = int|float|string|bool;
type StatusCode = 200|301|302|404|500;
type Result<T, TError extends \Throwable> = T|TError;
type Predicate<T> = callable<T, bool>;
```

## Namespaces

Both braced and semicolon-style namespace declarations. Multiple namespaces per file allowed. PHP items can be imported into a different tyhpdef namespace using leading-backslash fully-qualified names.

<a href="tyhpdef_namespaces.html">See full documentation →</a>

```tyhp
namespace App\Models {
    class User { public int $id; public string $name; }
}

namespace EL {
    class \EmailerLib\Emailer {
        public function __construct(string $subjectLine): void;
    }
}
```

## Import Aliases

Alias nearly anything with `as` — functions, classes, interfaces, traits, enums, properties, methods, constants, variables, and enum cases. Only the aliased name is available in Tyhp.

<a href="tyhpdef_importAliases.html">See full documentation →</a>

```tyhp
class LegacyApi as Api {
    public string $usr_nm as $username;
    public function get_usr as getUser(int $id): ?User;
}
```

## Overloaded Functions and Methods

Declare multiple signatures for the same function/method name to describe PHP functions with varying parameter types and return types. Supports static value type overloads, generic overloads, and async overloads.

<a href="tyhpdef_overloadedFunctions.html">See full documentation →</a>

```tyhp
function \json_decode(string $json, true $assoc): array<string, mixed>;
function \json_decode(string $json, false $assoc): object;
function \json_decode(string $json, null $assoc = null): mixed;

public function find(int $id): ?User;
public function find(string $uuid): ?User;
public function find(array<string, mixed> $criteria): array<User>;
```

## The `deprecated` and `obsolete` Keywords

`deprecated` produces a compiler warning but still compiles. `obsolete` produces a compiler error and blocks compilation. Both must appear before all other modifiers. Can be applied to functions, classes, interfaces, traits, enums, methods, properties, constants, and enum cases.

<a href="tyhpdef_deprecatedKeyword.html">See full documentation →</a>

```tyhp
deprecated function \mysql_connect(string $server, string $user, string $pass): resource|false;
obsolete function \md5(string $string, bool $binary = false): string;
```

## Runtime Errors and Exceptions

When tyhpdef declarations don't match actual PHP code, runtime errors can occur. Declare exception hierarchies, use `@throws` annotations, and provide fallback defaults with `??` to prevent type mismatch errors.

<a href="tyhpdef_runTimeErrorsAndExceptions.html">See full documentation →</a>

```tyhp
class \InvalidArgumentException extends \LogicException {
    public function __construct(
        string $message = "",
        int $code = 0,
        ?\Throwable $previous = null
    ): void;
}

/** @throws \JsonException When JSON encoding fails */
function \json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false;
```
