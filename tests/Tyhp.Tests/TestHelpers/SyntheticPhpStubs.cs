namespace Tyhp.Tests.TestHelpers;

/// <summary>
/// Tiny tyhpdef fixtures for compiler-mechanism tests. These are not the live
/// <c>tyhpdef/php</c> overlays — overlay contracts live in package tests.
/// </summary>
public static class SyntheticPhpStubs
{
    public const string DateTimeOperators = """
        <?tyhpdef
        interface DateTimeInterface {
            public function diff(\DateTimeInterface $targetObject, bool $absolute = false): \DateInterval;
            operator <=>(self $left, \DateTimeInterface $right): int;
            operator <(self $left, \DateTimeInterface $right): bool;
        }

        class DateInterval {
        }

        class DateTime implements \DateTimeInterface {
            public function add(\DateInterval $interval): \DateTime;
            public function sub(\DateInterval $interval): \DateTime;
            public function diff(\DateTimeInterface $targetObject, bool $absolute = false): \DateInterval;
            extension operator +(self $left, \DateInterval $right): \DateTime => $left->add($right);
            extension operator -(self $left, \DateInterval $right): \DateTime => $left->sub($right);
            extension operator -(self $left, \DateTimeInterface $right): \DateInterval => $left->diff($right);
        }

        class DateTimeImmutable implements \DateTimeInterface {
            public function add(\DateInterval $interval): \DateTimeImmutable;
            public function sub(\DateInterval $interval): \DateTimeImmutable;
            public function diff(\DateTimeInterface $targetObject, bool $absolute = false): \DateInterval;
            extension operator +(self $left, \DateInterval $right): \DateTimeImmutable => $left->add($right);
            extension operator -(self $left, \DateInterval $right): \DateTimeImmutable => $left->sub($right);
            extension operator -(self $left, \DateTimeInterface $right): \DateInterval => $left->diff($right);
        }
        """;

    public const string Fiber = """
        <?tyhpdef
        interface Throwable {
        }

        class Exception implements \Throwable {
            public function __construct(string $message = ""): void;
        }

        final class Fiber<TResume = mixed, TCallableShape extends callable = callable> {
            static public function getCurrent(): ?\Fiber<mixed, callable>;
            static public function suspend(null|mixed $value = null): null|mixed;
            public function __construct(TCallableShape $callback): void;
            public function getReturn(): __CallableReturnType<TCallableShape>;
            public function resume(null|TResume $value = null): null|mixed;
            public function start(__CallableParametersRest<TCallableShape> ...$args): null|mixed;
            public function throw(\Throwable $exception): null|mixed;
        }
        """;

    /// <summary>
    /// Layer-1 harvest used with <see cref="ClosureFiberOverlay"/> to exercise overlay merge.
    /// </summary>
    public const string ClosureFiberHarvest = """
        <?tyhpdef
        interface Throwable {
        }

        class Exception implements \Throwable {
            public function __construct(string $message = ""): void;
        }

        class stdClass {
        }

        final class Closure {
            static public function bind(\Closure $closure, ?object $newThis, object|string|null $newScope = 'static'): ?\Closure;
            static public function fromCallable(callable $callback): \Closure;
            #[\Tyhp\Php(">=8.5")]
            static public function getCurrent(): \Closure;
            private function __construct(): void;
            public function __invoke(): mixed;
            public function bindTo(?object $newThis, object|string|null $newScope = 'static'): ?\Closure;
            public function call(object $newThis, mixed ...$args): mixed;
        }

        final class Fiber {
            static public function getCurrent(): ?\Fiber;
            static public function suspend(mixed $value = null): mixed;
            public function __construct(callable $callback): void;
            public function getReturn(): mixed;
            public function resume(mixed $value = null): mixed;
            public function start(mixed ...$args): mixed;
            public function throw(\Throwable $exception): mixed;
        }
        """;

    /// <summary>
    /// Layer-3 overlay applied on top of <see cref="ClosureFiberHarvest"/>.
    /// </summary>
    public const string ClosureFiberOverlay = """
        <?tyhpdef
        type __ClosureThis = object|null;
        type __ClosureScope<TThis extends __ClosureThis> = __SuperType<TThis>|__SuperTypeName<TThis>|null;

        final partial class Closure<
            TCallableShape extends callable,
            TThis extends __ClosureThis = __ClosureThis,
            TScope extends __ClosureScope<TThis> = __ClosureScope<TThis>
        >;
        final partial class Closure {
            static public function bind<
                TCallableShape extends callable,
                TNewThis extends __ClosureThis,
                TNewScope extends __ClosureScope<TNewThis>,
                TOldThis extends __ClosureThis,
                TOldScope extends __ClosureScope<TOldThis>
            >(
                \Closure<TCallableShape, TOldThis, TOldScope> $closure,
                TNewThis $newThis,
                TNewScope $newScope
            ): ?\Closure<TCallableShape, TNewThis, TNewScope>;
            static public function bind<
                TCallableShape extends callable,
                TNewThis extends __ClosureThis,
                TNewScope extends 'static',
                TOldThis extends __ClosureThis,
                TOldScope extends __ClosureScope<TOldThis>
            >(
                \Closure<TCallableShape, TOldThis, TOldScope> $closure,
                TNewThis $newThis,
                TNewScope $newScope = 'static'
            ): ?\Closure<TCallableShape, TNewThis, TOldScope>;
            static public function fromCallable<TCallableShape extends callable>(
                TCallableShape $callback
            ): \Closure<TCallableShape, __CallableThis<TCallableShape>, __CallableScope<TCallableShape>>;
            #[\Tyhp\Php(">=8.5")]
            static public function getCurrent(): \Closure<callable, __ClosureThis, __ClosureScope<__ClosureThis>>;
            public function __invoke(__CallableParametersRest<TCallableShape> ...$args): __CallableReturnType<TCallableShape>;
            public function bindTo<
                TNewThis extends __ClosureThis,
                TNewScope extends __ClosureScope<TNewThis>
            >(
                TNewThis $newThis,
                TNewScope $newScope
            ): ?\Closure<TCallableShape, TNewThis, TNewScope>;
            public function bindTo<
                TNewThis extends __ClosureThis,
                TNewScope extends 'static'
            >(
                TNewThis $newThis,
                TNewScope $newScope = 'static'
            ): ?\Closure<TCallableShape, TNewThis, TScope>;
            public function call<TNewThis extends object>(
                TNewThis $newThis,
                __CallableParametersRest<TCallableShape> ...$args
            ): __CallableReturnType<TCallableShape>;
        }

        partial class Fiber<TResume = mixed, TCallableShape extends callable = callable>;
        partial class Fiber {
            static public function getCurrent(): ?\Fiber<mixed, callable>;
            static public function suspend(null|mixed $value = null): null|mixed;
            public function __construct(TCallableShape $callback): void;
            public function getReturn(): __CallableReturnType<TCallableShape>;
            public function resume(null|TResume $value = null): null|mixed;
            public function start(__CallableParametersRest<TCallableShape> ...$args): null|mixed;
            public function throw(\Throwable $exception): null|mixed;
        }
        """;

    /// <summary>
    /// Complete generic Closure (no overlay) for checker/emitter language rules.
    /// </summary>
    public const string ClosureGeneric = """
        <?tyhpdef
        type __ClosureThis = object|null;
        type __ClosureScope<TThis extends __ClosureThis> = __SuperType<TThis>|__SuperTypeName<TThis>|null;

        class stdClass {
        }

        final class Closure<
            TCallableShape extends callable,
            TThis extends __ClosureThis = __ClosureThis,
            TScope extends __ClosureScope<TThis> = __ClosureScope<TThis>
        > {
            static public function bind<
                TCallableShape extends callable,
                TNewThis extends __ClosureThis,
                TNewScope extends __ClosureScope<TNewThis>,
                TOldThis extends __ClosureThis,
                TOldScope extends __ClosureScope<TOldThis>
            >(
                \Closure<TCallableShape, TOldThis, TOldScope> $closure,
                TNewThis $newThis,
                TNewScope $newScope
            ): ?\Closure<TCallableShape, TNewThis, TNewScope>;
            static public function bind<
                TCallableShape extends callable,
                TNewThis extends __ClosureThis,
                TNewScope extends 'static',
                TOldThis extends __ClosureThis,
                TOldScope extends __ClosureScope<TOldThis>
            >(
                \Closure<TCallableShape, TOldThis, TOldScope> $closure,
                TNewThis $newThis,
                TNewScope $newScope = 'static'
            ): ?\Closure<TCallableShape, TNewThis, TOldScope>;
            static public function fromCallable<TCallableShape extends callable>(
                TCallableShape $callback
            ): \Closure<TCallableShape, __CallableThis<TCallableShape>, __CallableScope<TCallableShape>>;
            #[\Tyhp\Php(">=8.5")]
            static public function getCurrent(): \Closure<callable, __ClosureThis, __ClosureScope<__ClosureThis>>;
            private function __construct(): void;
            public function __invoke(__CallableParametersRest<TCallableShape> ...$args): __CallableReturnType<TCallableShape>;
            public function bindTo<
                TNewThis extends __ClosureThis,
                TNewScope extends __ClosureScope<TNewThis>
            >(
                TNewThis $newThis,
                TNewScope $newScope
            ): ?\Closure<TCallableShape, TNewThis, TNewScope>;
            public function bindTo<
                TNewThis extends __ClosureThis,
                TNewScope extends 'static'
            >(
                TNewThis $newThis,
                TNewScope $newScope = 'static'
            ): ?\Closure<TCallableShape, TNewThis, TScope>;
            public function call<TNewThis extends object>(
                TNewThis $newThis,
                __CallableParametersRest<TCallableShape> ...$args
            ): __CallableReturnType<TCallableShape>;
        }
        """;

    public const string WeakMap = """
        <?tyhpdef
        interface ArrayAccess<TKey = mixed, TValue = mixed> {
            public function offsetExists(TKey $offset): bool;
            public function offsetGet(TKey $offset): TValue;
            public function offsetSet(?TKey $offset, TValue $value): void;
            public function offsetUnset(TKey $offset): void;
        }

        final class WeakMap<TKey extends object = object, TValue = mixed> implements \ArrayAccess<TKey, TValue> {
            public function offsetExists(TKey $object): bool;
            public function offsetGet(TKey $object): TValue;
            public function offsetSet(?TKey $object, TValue $value): void;
            public function offsetUnset(TKey $object): void;
        }
        """;

    public const string KeywordCalls = """
        <?tyhpdef
        function exit(string|int $status = 0): never;
        function die(string|int $status = 0): never;
        function clone(object $object, array $withProperties = []): object;
        """;

    /// <summary>
    /// Compile-time <c>#[\Tyhp\Php]</c> / <c>#[\Tyhp\NoEmit]</c> so version gates bind and
    /// the emitter strips those attributes. Tyhpdef does not allow constructor promotion.
    /// </summary>
    public const string TyhpPhpAttribute = """
        <?tyhpdef
        class Attribute {
        }

        namespace Tyhp {
            #[\Attribute]
            #[\Tyhp\NoEmit]
            final class NoEmit {
                public function __construct(): void;
            }

            #[\Attribute]
            #[\Tyhp\NoEmit]
            final class Php {
                public function __construct(string $version): void;
            }

            #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::TARGET_PROPERTY | \Attribute::TARGET_PARAMETER)]
            #[\Tyhp\NoEmit]
            final class EraseGeneric {
                public function __construct(): void;
            }

            #[\Attribute(\Attribute::TARGET_ALL)]
            final class GenericRuntime {
                public function __construct(
                    bool $erased = false,
                    ?string $binder = null,
                    ?string $factory = null,
                    ?string $aliasFactory = null,
                    array $layouts = [1],
                    ?string $compiler = null
                ): void;
            }
        }
        """;

    public const string FunctionAliases = """
        <?tyhpdef
        function is_object(mixed $value): $value is object;
        function call_user_func_array as call_user_func_array_unsafe(callable $callback, array $args): mixed;
        function class_exists<T extends object = object> as class_exists_alt(string $class, bool $autoload = true): $class is \__ClassName<T>;
        """;

    /// <summary>
    /// Layer-3-style <c>_unsafe</c> aliases for binder tests of the alias-rename mechanism.
    /// Live call-site contracts are covered by <c>runtime/packages/test-all-tyhpdef.sh</c>.
    /// </summary>
    public const string ForwardStaticCallAliases = """
        <?tyhpdef
        function forward_static_call as forward_static_call_unsafe(callable $callback, mixed ...$args): mixed;
        function forward_static_call_array as forward_static_call_array_unsafe(callable $callback, array $args): mixed;
        """;

    /// <summary>
    /// Shared PHP / core-adjacent stubs for language-rule tests that used to load
    /// live packages. Not overlay contracts.
    /// </summary>
    public const string MinimalPhp = """
        <?tyhpdef
        interface Traversable<TKey = mixed, TValue = mixed> {
        }

        interface Iterator<TKey = mixed, TValue = mixed> extends \Traversable<TKey, TValue> {
            public function current(): TValue;
            public function key(): TKey;
            public function next(): void;
            public function rewind(): void;
            public function valid(): bool;
        }

        interface IteratorAggregate<TKey = mixed, TValue = mixed> extends \Traversable<TKey, TValue> {
            public function getIterator(): \Traversable<TKey, TValue>|\Iterator<TKey, TValue>;
        }

        interface ArrayAccess<TKey = mixed, TValue = mixed> {
            public function offsetExists(TKey $offset): bool;
            public function offsetGet(TKey $offset): TValue;
            public function offsetSet(?TKey $offset, TValue $value): void;
            public function offsetUnset(TKey $offset): void;
        }

        interface Countable {
            public function count(): int;
        }

        interface Stringable {
            public function __toString(): string;
        }

        interface UnitEnum {
            abstract static public function cases(): array<int, static>;
        }

        interface BackedEnum extends \UnitEnum {
            static public function from(string|int $value): static;
            static public function tryFrom(string|int $value): ?static;
        }

        interface Throwable {
            public function getMessage(): string;
        }

        class Exception implements \Throwable {
            public function __construct(string $message = ""): void;
            public function getMessage(): string;
        }

        class Error implements \Throwable {
            public function __construct(string $message = ""): void;
            public function getMessage(): string;
        }

        class stdClass {
        }

        type __ClosureThis = object|null;
        type __ClosureScope<TThis extends __ClosureThis> = __SuperType<TThis>|__SuperTypeName<TThis>|null;

        final class Closure<
            TCallableShape extends callable,
            TThis extends __ClosureThis = __ClosureThis,
            TScope extends __ClosureScope<TThis> = __ClosureScope<TThis>
        > {
            static public function bind(
                \Closure $closure,
                object|null $newThis,
                object|string|null $newScope = 'static'
            ): ?\Closure;
            static public function fromCallable<TCallableShapeIn extends callable>(
                TCallableShapeIn $callback
            ): \Closure<TCallableShapeIn, __CallableThis<TCallableShapeIn>, __CallableScope<TCallableShapeIn>>;
            private function __construct(): void;
            public function __invoke(__CallableParametersRest<TCallableShape> ...$args): __CallableReturnType<TCallableShape>;
            public function bindTo(
                object|null $newThis,
                object|string|null $newScope = 'static'
            ): ?\Closure<TCallableShape, TThis, TScope>;
            public function call(object $newThis, mixed ...$args): __CallableReturnType<TCallableShape>;
        }

        #[\Attribute]
        class Attribute {
            public const int TARGET_CLASS ?? 1;
            public const int TARGET_FUNCTION ?? 2;
            public const int TARGET_METHOD ?? 4;
            public const int TARGET_PROPERTY ?? 8;
            public const int TARGET_CLASS_CONSTANT ?? 16;
            public const int TARGET_PARAMETER ?? 32;
            public const int TARGET_CONSTANT ?? 64;
            public const int TARGET_ALL ?? 63;
            public const int IS_REPEATABLE ?? 64;
            public function __construct(int $flags = 0): void;
        }

        #[\Attribute]
        final class Deprecated {
            public function __construct(?string $message = null, ?string $since = null): void;
        }

        class LogicException extends \Exception {
            public function __construct(string $message = ""): void;
        }

        class InvalidArgumentException extends \LogicException {
            public function __construct(string $message = ""): void;
        }

        class RuntimeException extends \Exception {
            public function __construct(string $message = ""): void;
        }

        interface JsonSerializable {
            public function jsonSerialize(): mixed;
        }

        interface Serializable {
            public function serialize(): string;
            public function unserialize(string $data): void;
        }

        final class Fiber<TResume = mixed, TCallableShape extends callable = callable> {
            public function __construct(TCallableShape $callback): void;
        }

        final class WeakReference<T extends object = object> {
            static public function create<TIn extends object>(TIn $object): \WeakReference<TIn>;
            public function get(): ?T;
        }

        final class Generator<TKey = mixed, TValue = mixed, TSend = mixed, TReturn = mixed> implements \Iterator<TKey, TValue>, \Traversable<TKey, TValue> {
            private function __construct(): void;
            public function current(): TValue;
            public function key(): TKey;
            public function next(): void;
            public function rewind(): void;
            public function valid(): bool;
            public function send(TSend $value): TValue;
            public function throw(\Throwable $exception): TValue;
            public function getReturn(): TReturn;
        }

        function is_string(mixed $value): $value is string;
        function is_int(mixed $value): $value is int;
        function is_integer(mixed $value): $value is int;
        function is_long(mixed $value): $value is int;
        function is_array(mixed $value): $value is array;
        function is_object(mixed $value): $value is object;
        function is_bool(mixed $value): $value is bool;
        function is_float(mixed $value): $value is float;
        function is_double(mixed $value): $value is float;
        function is_null(mixed $value): $value instanceof null;
        function is_numeric(mixed $value): $value is int|float|string;
        function is_scalar(mixed $value): $value is int|float|string|bool;
        function is_resource(mixed $value): $value is resource;
        function is_callable(mixed $value, false $syntax_only = false, mixed &$callable_name = null): $value is callable;
        function is_callable(mixed $value, true $syntax_only, mixed &$callable_name = null): bool;
        function is_callable(mixed $value, bool $syntax_only = false, mixed &$callable_name = null): bool;
        function is_iterable(mixed $value): $value instanceof array|\Traversable;
        function is_countable(mixed $value): $value instanceof array|\Countable;
        function get_parent_class<T extends object>(T|__ClassName<T> $object_or_class): __ClassName<__SuperType<T>>|false;
        function class_exists<T extends object = object>(string $class, bool $autoload = true): $class is \__ClassName<T>;
        function class_exists<T extends object = object> as class_exists_alt(string $class, bool $autoload = true): $class is \__ClassName<T>;
        function is_subclass_of<T1 extends object, T2 extends object>(T1|__ClassName<T1> $object_or_class, __ClassName<T2> $class, true $allow_string = true): $class is __SuperTypeName<T1>;
        function is_subclass_of<T1 extends object, T2 extends object>(T1 $object_or_class, __ClassName<T2> $class, false $allow_string): $class is __SuperTypeName<T1>;
        function interface_exists(string $interface, bool $autoload = true): $interface is \__InterfaceName;
        function function_exists(string $function): $function is \__FunctionName;
        function implode(string $separator, array $array): string;
        function property_exists(object|string $object_or_class, string $property): bool;
        function get_class(object $object): string;
        function htmlentities(string $string, int $flags = 0, ?string $encoding = null, bool $double_encode = true): string;
        function array_pop(array &$array): mixed;
        function array_push(array &$array, mixed ...$values): int;
        function str_repeat(string $string, int $times): string;
        function compact(string|array ...$var_names): array;
        function array_key_exists(mixed $key, array $array): bool;
        function strlen(string $string): int;
        function strval(mixed $value): string;
        function count(\Countable|array $value): int;
        function ltrim(string $string, string $characters = " \n\r\t\v\0"): string;
        function array_map<TValue, TResult extends void|never|mixed>(
            callable(TValue $item): TResult $callback,
            array<TValue> $array
        ): array<TResult>;
        function array_map(?callable $callback, array $array, array $extra, array ...$arrays): array;
        function array_reverse(array $array, bool $preserve_keys = false): array;
        function array_keys(array $array): array;
        function call_user_func as call_user_func_unsafe(callable $callback, mixed ...$args): mixed;
        function call_user_func<TCallable extends callable>(
            TCallable $callback,
            __CallableParametersRest<TCallable> ...$args
        ): __CallableReturnType<TCallable>;
        function call_user_func_array as call_user_func_array_unsafe(callable $callback, array $args): mixed;
        function call_user_func_array<TCallable extends callable>(
            TCallable $callback,
            __CallableParametersStruct<TCallable> $args
        ): __CallableReturnType<TCallable>;
        function call_user_func_array<TCallable extends callable>(
            TCallable $callback,
            __CallableParametersTuple<TCallable> $args
        ): __CallableReturnType<TCallable>;
        function sort(array &$array, int $flags = 0): bool;
        function ksort(array &$array, int $flags = 0): bool;
        function strtolower(string $string): string;
        function strtoupper(string $string): string;
        function trim(string $string, string $characters = " \n\r\t\v\0"): string;
        function intval(mixed $value, int $base = 10): int;
        function floatval(mixed $value): float;
        function mb_strlen(string $string, ?string $encoding = null): int;
        function mb_strtolower(string $string, ?string $encoding = null): string;
        function mb_strtoupper(string $string, ?string $encoding = null): string;
        function exit(string|int $status = 0): never;
        function die(string|int $status = 0): never;
        function clone(object $object, array $withProperties = []): object;
        function var_dump(mixed ...$values): void;

        type CallableArgs1<T1> = struct {
            T1 0 as $_1;
        };

        type CallableArgs2<T1, T2> = struct extends CallableArgs1<T1> {
            T2 1 as $_2;
        };

        final class SplPriorityQueue<TPriority = mixed, TValue = mixed> implements \Iterator<int, TValue>, \Traversable<int, TValue>, \Countable {
            public function current(): TValue;
            public function key(): int;
            public function next(): void;
            public function rewind(): void;
            public function valid(): bool;
            public function count(): int;
        }

        final class WeakMap<TKey extends object = object, TValue = mixed> implements \ArrayAccess<TKey, TValue> {
            public function offsetExists(TKey $object): bool;
            public function offsetGet(TKey $object): TValue;
            public function offsetSet(?TKey $object, TValue $value): void;
            public function offsetUnset(TKey $object): void;
        }

        extension MinimalString extends string {
            fn length(): int => 0;
        }
        global use extension \MinimalString;

        namespace Tyhp {
            #[\Attribute]
            #[\Tyhp\NoEmit]
            final class NoEmit {
                public function __construct(): void;
            }

            #[\Attribute]
            #[\Tyhp\NoEmit]
            final class Php {
                public function __construct(string $version): void;
            }

            #[\Attribute]
            #[\Tyhp\NoEmit]
            final class PhpType {
                public function __construct(string $type): void;
            }

            #[\Attribute(\Attribute::TARGET_METHOD | \Attribute::TARGET_FUNCTION)]
            #[\Tyhp\NoEmit]
            final class NativeTypeTest {
                public function __construct(): void;
            }

            #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::TARGET_PROPERTY | \Attribute::TARGET_PARAMETER)]
            #[\Tyhp\NoEmit]
            final class EraseGeneric {
                public function __construct(): void;
            }

            #[\Attribute(\Attribute::TARGET_ALL)]
            final class GenericRuntime {
                public function __construct(
                    bool $erased = false,
                    ?string $binder = null,
                    ?string $factory = null,
                    ?string $aliasFactory = null,
                    array $layouts = [1],
                    ?string $compiler = null
                ): void;
            }

            class Type implements \Stringable {
                public function __toString(): string;
            }

            extension StringExtensions extends string {
                fn length(): int => \mb_strlen($this);
                fn toLower(): string => \mb_strtolower($this);
                fn toUpper(): string => \mb_strtoupper($this);
                fn toCamelCase(): string => $this;
            }
            global use extension \Tyhp\StringExtensions;

            class Expression<TCallableShape extends callable> {
                public \Tyhp\Expression\ExpressionNode $body;
                public array<\Tyhp\Expression\ParameterExpression> $parameters;
                public function equals(\Tyhp\Expression $other): bool;
            }

            class PropertyPath<TCallableShape extends callable> extends Expression<TCallableShape> {
                public function getPath(): string;
            }

            class Promise<TReturn extends void|mixed = mixed> {
            }
        }

        namespace Tyhp\Concerns {
            interface HasPropertyAccessors {
            }

            interface UsesPropertyAccessors {
            }

            interface HasGenerics {
            }
        }

        namespace Tyhp\Expression {
            abstract class ExpressionNode {
                public string $type;
                public string $nodeType;
                abstract public function accept(ExpressionVisitor $visitor): mixed;
            }

            abstract class ExpressionVisitor {
                public function visit(ExpressionNode $node): mixed;
                public function visitParameter(ParameterExpression $node): mixed;
                public function visitPropertyAccess(PropertyAccessExpression $node): mixed;
                public function visitBinary(BinaryExpression $node): mixed;
                public function visitConstant(ConstantExpression $node): mixed;
                public function visitInstanceof(InstanceofExpression $node): mixed;
            }

            final class ExpressionSerializer {
                public static function toJson(\Tyhp\Expression $expression): string;
                public static function equals(\Tyhp\Expression $a, \Tyhp\Expression $b): bool;
            }

            final class ParameterExpression extends ExpressionNode {
                public string $name;
                public function accept(ExpressionVisitor $visitor): mixed;
            }

            final class PropertyAccessExpression extends ExpressionNode {
                public ExpressionNode $object;
                public string $property;
                public function accept(ExpressionVisitor $visitor): mixed;
            }

            final class BinaryExpression extends ExpressionNode {
                public ExpressionNode $left;
                public string $operator;
                public ExpressionNode $right;
                public function accept(ExpressionVisitor $visitor): mixed;
            }

            final class ConstantExpression extends ExpressionNode {
                public mixed $value;
                public function accept(ExpressionVisitor $visitor): mixed;
            }

            final class InstanceofExpression extends ExpressionNode {
                public ExpressionNode $operand;
                public string $targetType;
                public function accept(ExpressionVisitor $visitor): mixed;
            }
        }

        namespace Tyhp\Contracts {
            interface ArrayAccessShape<TStruct extends struct> extends \ArrayAccess {
            }

            interface IsDisposable {
            }
        }
        """;
}
