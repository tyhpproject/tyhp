---
title: 'Parsable Lambdas and Expression Trees'
status:
  tier: 1
  story: '16'
  state: complete
  note: 'Phases 1–3 landed: PropertyPath, Expression trees, multi-parameter arity, nameof(fn), instanceof/is, serialization, and structural equality'
---

Tyhp supports parsable lambdas — a feature that allows the compiler to capture the abstract syntax tree (AST) of an inline fn expression instead of just compiling it to a closure. When a function parameter is typed as PropertyPath<T, R> or Expression<T, R>, passing an inline fn expression causes the compiler to emit a data structure describing the expression tree. The receiving function can then inspect the structure of the lambda at runtime — walking property accesses, method calls, operators, and constants — to translate the expression into another domain such as SQL queries, validation rules, or serialization mappings.

:::note
This is the same concept as C#'s Expression<Func<T, TResult>>, which powers Entity Framework's LINQ-to-SQL. The parameter type drives the behavior: if a parameter is typed Expression<T, R>, the compiler emits an expression tree; if typed callable or \Closure, the compiler emits a normal closure.
:::

## PropertyPath<T, R> — Type-Safe Property References

PropertyPath<T, R> is the simplest form of parsable lambda. It captures a chain of property accesses from an fn expression. When a parameter is typed PropertyPath<T, R>, the compiler converts fn (T $x) => $x->propA->propB into a PropertyPath object containing the chain ['propA', 'propB'] plus type information.

```tyhp
<?tyhp

class User {
    public string $firstName;
    public string $lastName;
    public Address $address;
}

class Address {
    public string $city;
    public string $state;
}

class QueryBuilder<T> {
    public function select(PropertyPath<T, string> $prop): static {
        string $column = $prop->getPropertyName();
        string $path = $prop->getPath();
        return $this;
    }
}

$query = new QueryBuilder<User>();

// Single property
$query->select(fn ($u) => $u->firstName);
// path: ['firstName'], getPropertyName(): 'firstName', getPath(): 'firstName'

// Chained property access
$query->select(fn ($u) => $u->address->city);
// path: ['address', 'city'], getPropertyName(): 'city', getPath(): 'address.city'
```

## PropertyPath Members

:::member[readonly string $sourceType]
The fully qualified source type that the property chain starts from. For fn (User $u) => $u->address->city, this is 'User'.
:::

:::member[readonly string $resultType]
The resolved type at the end of the property chain. For fn (User $u) => $u->address->city, this is 'string'.
:::

:::member[readonly array $path]
The property names in the access chain, in order. For fn (User $u) => $u->address->city, this is ['address', 'city'].
:::

:::member[readonly \Closure $callable]
The compiled closure that can execute the property access. This allows PropertyPath to be used as a callable fallback.
:::

:::member[getPropertyName(): string]
Returns the final property name (last element of the path). For fn (User $u) => $u->address->city, returns 'city'.
:::

:::member[getPath(): string]
Returns the full dot-notation path string. For fn (User $u) => $u->address->city, returns 'address.city'.
:::

:::member[getSegments(): array]
Returns the path as an array of property names.
:::

:::member[getValue(T $source): R]
Executes the property access chain on an object instance. Equivalent to calling the compiled callable.
:::

## PropertyPath Compiled PHP Output

When you pass an fn expression to a PropertyPath parameter, the compiler emits a new \Tyhp\PropertyPath(...) construction. The original fn is included as the callable parameter so the PropertyPath can be executed directly.

```tyhp
<?tyhp

$query->select(fn ($u) => $u->address->city);
```

```php
<?php

$query->select(new \Tyhp\PropertyPath(
    \App\Models\User::class,
    'string',
    ['address', 'city'],
    fn (\App\Models\User $u) => $u->address->city
));
```

## Nullable Property Chains

PropertyPath supports null-safe property access. Nullable chains are allowed and the PropertyPath tracks which segments are nullable. The result type is adjusted accordingly.

```tyhp
<?tyhp

class User {
    public ?Address $address;
}

// Nullable chain — result type is ?string
$query->select(fn ($u) => $u?->address?->city);
```

The emitted construction records which segments used `?->`, so the expression tree contains
`NullSafeAccessExpression` nodes for them rather than plain property access:

```php
<?php

$query->select(new \Tyhp\PropertyPath(
    \App\Models\User::class,
    '?string',
    ['address', 'city'],
    fn (\App\Models\User $u): ?string => $u?->address?->city,
    nullSafeFlags: [true, true]
));
```

## Expression<T, R> — Full Expression Trees

`PropertyPath<T, R>` extends `Expression<T, R>`. `Expression` supports full expression trees: any supported inline `fn` body — not just property chains — can be captured as a tree of expression nodes. This enables runtime inspection of comparison operators, method calls, binary expressions, captured variables, and more.

Expression uses the same generic parameter convention as callable: the last type argument is the return type, and everything before represents parameter types. Expression<User, string> is a single-parameter lambda taking User and returning string. Expression<T, T, int> is a two-parameter lambda returning int.

```tyhp
<?tyhp

class QueryBuilder<T> {
    public function where(Expression<T, bool> $predicate): static {
        // Walk the expression tree to build SQL WHERE clause
        return $this;
    }

    public function select<R>(Expression<T, R> $selector): QueryBuilder<R> {
        // Walk the expression tree to build SQL SELECT clause
        return new QueryBuilder<R>();
    }
}

int $minAge = 18;

$query = new QueryBuilder<User>()
    ->where(fn ($u) => $u->age > $minAge && $u->isActive)
    ->select(fn ($u) => $u->firstName);

// The fn expressions are automatically converted to Expression trees
// because the parameter types are Expression<T, R>
```

## Expression Members

:::member[readonly ExpressionNode $body]
The expression tree root node representing the fn body.
:::

:::member[readonly array $parameters]
The lambda parameters as ParameterExpression nodes.
:::

:::member[readonly \Closure $callable]
The compiled closure for execution. Every Expression carries its original compiled closure so it can be executed as a fallback.
:::

:::member[readonly string $returnType]
The resolved return type of the expression.
:::

:::member[__invoke(mixed ...$args): mixed]
Execute the expression by delegating to the stored callable. This makes Expression callable via $expr($arg).
:::

:::member[compile(): \Closure]
Returns the stored compiled closure.
:::

## Expression Node Types

Expression trees are built from a hierarchy of node types. Every node extends ExpressionNode and carries a resolved type string and a nodeType identifier. The following table lists all supported node types.

:::member[ParameterExpression]
Represents the lambda parameter ($x). Properties: string $name, string $paramType, int $index.
:::

:::member[PropertyAccessExpression]
Represents a property access ($x->firstName). Properties: ExpressionNode $object, string $property.
:::

:::member[NullSafeAccessExpression]
Represents a null-safe property access ($x?->address). Properties: ExpressionNode $object, string $property.
:::

:::member[MethodCallExpression]
Represents a method call ($x->getFullName()). Properties: ExpressionNode $object, string $method, array $arguments.
:::

:::member[StaticMethodCallExpression]
Represents a static method call (Str::lower(...)). Properties: string $class, string $method, array $arguments.
:::

:::member[BinaryExpression]
Represents a binary operation ($x->age > 18). Properties: ExpressionNode $left, string $operator, ExpressionNode $right.
:::

:::member[UnaryExpression]
Represents a unary operation (!$x->isDeleted). Properties: string $operator, ExpressionNode $operand, bool $isPrefix.
:::

:::member[ConstantExpression]
Represents a literal value (18, 'hello') or a captured variable from the enclosing scope. Properties: mixed $value.
:::

:::member[TernaryExpression]
Represents a ternary expression ($x->a ? $x->b : $x->c). Properties: ExpressionNode $condition, ?ExpressionNode $ifTrue, ExpressionNode $ifFalse.
:::

:::member[CoalesceExpression]
Represents a null coalescing expression ($x->nickname ?? $x->name). Properties: ExpressionNode $left, ExpressionNode $right.
:::

:::member[ArrayAccessExpression]
Represents an array access ($x->tags[0]). Properties: ExpressionNode $array, ExpressionNode $index.
:::

:::member[CastExpression]
Represents a type cast ((int) $x->value). Properties: string $targetType, ExpressionNode $operand.
:::

:::member[NewExpression]
Represents a new expression (new Money($x->amount)). Properties: string $class, array $arguments.
:::

## Expression Tree Compiled PHP Output

When you pass an fn expression to an Expression<T, R> parameter, the compiler emits a tree of \Tyhp\Expression\*Expression node constructors wrapped in a \Tyhp\Expression. The tree is built bottom-up: leaf nodes first, then their parents.

```tyhp
<?tyhp

int $minAge = 18;
$query->where(fn ($u) => $u->age > $minAge);
```

```php
<?php

$query->where(new \Tyhp\Expression(
    body: new \Tyhp\Expression\BinaryExpression(
        left: new \Tyhp\Expression\PropertyAccessExpression(
            object: new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
            property: 'age',
            type: 'int'
        ),
        operator: '>',
        right: new \Tyhp\Expression\ConstantExpression($minAge, 'int'),
        type: 'bool'
    ),
    parameters: [
        new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
    ],
    callable: fn (\App\Models\User $u) => $u->age > $minAge,
    returnType: 'bool'
));
```

```tyhp
<?tyhp

$query->select(fn ($u) => $u->getFullName());
```

```php
<?php

$query->select(new \Tyhp\Expression(
    body: new \Tyhp\Expression\MethodCallExpression(
        object: new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
        method: 'getFullName',
        arguments: [],
        type: 'string'
    ),
    parameters: [
        new \Tyhp\Expression\ParameterExpression('u', \App\Models\User::class, 0),
    ],
    callable: fn (\App\Models\User $u) => $u->getFullName(),
    returnType: 'string'
));
```

## Captured Variables

Variables from the enclosing scope that are referenced in the lambda body become ConstantExpression nodes in the expression tree. Their values are captured at runtime when the expression tree is constructed, not at compile time. This enables patterns where runtime values are embedded in the tree.

```tyhp
<?tyhp

int $minAge = 18;
string $requiredRole = 'admin';

// $minAge and $requiredRole are captured as ConstantExpression nodes
$query->where(fn ($u) => $u->age > $minAge && $u->role == $requiredRole);

// At runtime, the expression tree contains:
// - BinaryExpression(&&)
//   - BinaryExpression(>)
//     - PropertyAccessExpression('age')
//     - ConstantExpression(18)        ← captured $minAge
//   - BinaryExpression(==)
//     - PropertyAccessExpression('role')
//     - ConstantExpression('admin')   ← captured $requiredRole
```

## ExpressionVisitor

The ExpressionVisitor abstract class provides a visitor pattern for walking expression trees. Library authors extend ExpressionVisitor to build SQL translators, serialization mappers, validation rule generators, and other domain-specific interpreters.

```tyhp
<?tyhp

use \Tyhp\Expression\ExpressionVisitor;
use \Tyhp\Expression\BinaryExpression;
use \Tyhp\Expression\PropertyAccessExpression;
use \Tyhp\Expression\ConstantExpression;

class SqlWhereVisitor extends ExpressionVisitor {
    public function visitBinary(BinaryExpression $node): string {
        string $left = $this->visit($node->left);
        string $right = $this->visit($node->right);
        return "{$left} {$node->operator} {$right}";
    }

    public function visitPropertyAccess(PropertyAccessExpression $node): string {
        return $node->property;
    }

    public function visitConstant(ConstantExpression $node): string {
        if ($node->type === 'string') {
            return "'" . \addslashes((string)$node->value) . "'";
        }
        return (string) $node->value;
    }
}

// Usage
$visitor = new SqlWhereVisitor();
string $sql = $visitor->visit($expression->body);
// Result: "age > 18"
```

## Multi-Parameter Expressions

Expression trees support multi-parameter lambdas for comparison-style expressions. The type becomes Expression<T, T, int> for comparators, where the last type argument is always the return type.

```tyhp
<?tyhp

class SortableList<T> {
    public function sortBy(Expression<T, T, int> $comparator): static {
        // Walk the expression tree to determine sort columns
        return $this;
    }
}

$list->sortBy(fn ($a, $b) => $a->lastName <=> $b->lastName);
// The Expression tree has two ParameterExpression nodes
```

## nameof Integration

The nameof compile-time function works with PropertyPath-style arrow functions for lightweight, refactoring-safe property name extraction. nameof always returns the **last** segment of the chain (C# convention). Use PropertyPath when the full chain is needed.

```tyhp
<?tyhp

// These are equivalent:
string $col1 = nameof(fn (User $u) => $u->firstName);   // 'firstName'
string $col2 = nameof(User::$firstName);                 // 'firstName'

// Nested paths still yield the last segment:
string $city = nameof(fn (User $u) => $u->address->city); // 'city'
```

## Expression Serialization

Expression trees can be serialized to JSON using ExpressionSerializer. This enables passing expression trees across API boundaries, such as client-side query building sent to a server.

```tyhp
<?tyhp

use \Tyhp\Expression\ExpressionSerializer;

Expression<User, bool> $expr = fn ($u) => $u->age > 18;
string $json = ExpressionSerializer::toJson($expr);

// Result:
// {
//   "nodeType": "binary",
//   "operator": ">",
//   "left": { "nodeType": "propertyAccess", "property": "age", ... },
//   "right": { "nodeType": "constant", "value": 18, "type": "int" }
// }

// Structural equality ignores the compiled closures — two independently
// constructed trees with the same shape compare equal.
bool $same = ExpressionSerializer::equals($expr, $expr);
bool $alsoSame = $expr->equals($expr);
```

## Type System Integration

Expression trees integrate with the type system as follows:

- Expression<T, R> is assignable to callable<T, R> (via __invoke). You can call an Expression directly: $expr($userInstance).
- Expression<T, R> is NOT assignable to \Closure. PHP's \Closure class is final and cannot be extended.
- PropertyPath<T, R> is a subtype of Expression<T, R>. Code accepting Expression also accepts PropertyPath.
- When passing Expression where \Closure is expected, the emitter automatically extracts $expression->callable.
- When passing a regular fn expression to a callable or \Closure parameter (not Expression), the compiler emits a normal closure — no expression tree.
- The parameter type drives the behavior: only Expression<> and PropertyPath<> parameters trigger expression tree generation.

## Supported Expression Types

The following expression types are supported inside fn expressions that target Expression<T, R> parameters:

- Property access: $x->prop
- Null-safe property access: $x?->prop
- Method calls: $x->method()
- Static method calls: Class::method()
- Binary operators: +, -, *, /, %, ., ==, !=, ===, !==, <, >, <=, >=, <=>, &&, ||, and, or, ??, **
- Unary operators: !, -, +, ~
- Constants and literals: int, float, string, bool, null
- Class constants: Class::CONST
- Captured variables from the enclosing scope
- Array access: $x->items[$i]
- Ternary: $x ? $y : $z
- Null coalescing: $x ?? $y
- Type casts: (int) $x->value
- new expressions: new Money($x->amount)
- instanceof / is checks: $x->value is int, $x->value instanceof Address

## Unsupported Expression Types

The following expression types produce a compiler error (CheckerExpressionUnsupportedNode / TYHP4322) when used inside a parsable fn expression:

- Assignments ($x = ...)
- await expressions
- yield / yield from
- match expressions
- throw expressions
- include / require
- eval
- Nested fn / function expressions
- Null-safe method calls ($x?->method()) — property `?->` is supported; method form is not yet
- Statement-body constructs (if/else, for, while, etc.)
- print / echo

## Library Author Guide: Type-Safe Query Builder

This complete example demonstrates how a library author consumes expression trees to build type-safe SQL queries. The query builder accepts Expression parameters and uses an ExpressionVisitor to translate the expression tree to SQL.

```tyhp
<?tyhp

use \Tyhp\Expression;
use \Tyhp\PropertyPath;

class QueryBuilder<T> {
    private array $conditions = [];
    private ?string $selectColumn = null;

    public function where(Expression<T, bool> $predicate): static {
        $this->conditions[] = $predicate;
        return $this;
    }

    public function select<R>(PropertyPath<T, R> $selector): static {
        $this->selectColumn = $selector->getPropertyName();
        return $this;
    }

    public function orderBy<R>(PropertyPath<T, R> $prop): static {
        // Use $prop->getPropertyName() for the ORDER BY clause
        return $this;
    }
}

// User code — fully type-safe, refactoring-safe
int $minAge = 18;

$users = new QueryBuilder<User>()
    ->where(fn ($u) => $u->age > $minAge && $u->isActive)
    ->select(fn ($u) => $u->firstName)
    ->orderBy(fn ($u) => $u->lastName);

// The query builder can translate these expression trees to:
// SELECT first_name FROM users WHERE age > 18 AND is_active = true ORDER BY last_name
```

## Using Expression as a Callable

Every Expression carries its compiled closure, so it can be called directly. This allows code that receives an Expression to either inspect the tree or just execute it.

```tyhp
<?tyhp

function process(Expression<User, bool> $predicate, User $user): void {
    // Option 1: Inspect the expression tree
    ExpressionNode $body = $predicate->body;

    // Option 2: Just call it (delegates to the stored closure)
    bool $result = $predicate($user);

    // Option 3: Get the raw closure
    \Closure $fn = $predicate->compile();
}

$user = new User('Alice', 25);
process(fn ($u) => $u->age >= 18, $user);
```

## Best Practices

:::tip
Use PropertyPath<T, R> for simple property references in ORM column mappings, validation, and serialization configuration. It is lightweight and covers the most common use case.
:::

:::tip
Use Expression<T, R> when you need to inspect the full structure of a predicate — comparisons, boolean logic, method calls — such as building SQL WHERE clauses or DSL translators.
:::

:::tip
Extend ExpressionVisitor to build domain-specific translators rather than manually type-switching on expression node types.
:::

:::tip
Use nameof with PropertyPath expressions for refactoring-safe property name extraction instead of hardcoded strings.
:::

:::tip
Every Expression carries its compiled callable. Design your API to accept Expression<T, R> even if you sometimes just want to execute it — the caller can pass an fn expression and the library can either inspect or call it.
:::

## Common Mistakes

:::danger
Don't use parsable lambdas for regular callbacks where you only need to execute the function. Expression trees have construction overhead — use callable or \Closure when you don't need to inspect the tree.
:::

:::danger
Don't pass non-fn arguments to PropertyPath or Expression parameters. Only inline fn expressions are convertible to expression trees. Passing a variable, closure, or callable produces a compiler error.
:::

:::danger
Don't use complex statements (if/else, loops, match, await) inside parsable fn expressions. Expression trees only support single-expression bodies with the supported node types.
:::

:::danger
Don't pass method calls or binary expressions to a PropertyPath parameter — PropertyPath only supports simple property access chains (fn ($x) => $x->a->b). Use Expression for more complex expressions.
:::

:::danger
Don't assume Expression<T, R> is assignable to \Closure — PHP's \Closure class is final. The emitter extracts ->callable automatically when needed, but avoid explicit \Closure type hints when you want to accept expression trees.
:::

```tyhp
<?tyhp

function findColumn(PropertyPath<User, string> $prop): string {
    return $prop->getPropertyName();
}

// OK — inline fn expression
findColumn(fn ($u) => $u->firstName);

// ERROR — not an inline fn expression
// $myFn = fn ($u) => $u->firstName;
// findColumn($myFn);  // Compiler error: requires inline fn

// ERROR — not a property chain
// findColumn(fn ($u) => $u->getFullName());  // Compiler error: method call not allowed in PropertyPath

// OK — use Expression for method calls
function findExpr(Expression<User, string> $expr): string {
    return '';
}
findExpr(fn ($u) => $u->getFullName());  // This works
```

## Compiler Errors

- TYHP4320: Parameter of type 'PropertyPath<T, R>' requires an inline fn expression — triggered when a non-fn argument is passed to a PropertyPath parameter.
- TYHP4321: PropertyPath expression must be a simple property access chain — triggered when the fn body contains method calls, operators, or other non-property-access expressions in a PropertyPath context.
- TYHP4322: Expression trees do not support '{0}' expressions — triggered when an unsupported expression type (assignments, await, yield, match, etc.) is used in an Expression fn body.
- TYHP4323: Parameter of type 'Expression<T, R>' requires an inline fn expression — triggered when a non-fn argument is passed to an Expression parameter.
- TYHP4324: Captured variable '${0}' in expression tree must be definitely assigned — triggered when a captured variable may not be initialized at the point of capture.
