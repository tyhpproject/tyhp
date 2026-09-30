<?php
declare(strict_types=1);

/**
 * Tyhp Reflection dumper. Emits one schema-version-1 JSON document on stdout.
 * Invoked as a file (never `php -r`). Extension name: argv[1] or TYHP_REFLECT_EXT.
 */

$extName = $argv[1] ?? \getenv('TYHP_REFLECT_EXT') ?: '';
$extName = \is_string($extName) ? \trim($extName) : '';
if ($extName === '') {
    \fwrite(\STDERR, "missing extension name\n");
    \exit(2);
}

if (!\extension_loaded($extName)) {
    \fwrite(\STDERR, "extension not loaded: {$extName}\n");
    \exit(2);
}

$ext = new \ReflectionExtension($extName);

$dump = [
    'schemaVersion' => 1,
    'phpVersion' => \PHP_VERSION,
    'extension' => $ext->getName(),
    'extensionVersion' => $ext->getVersion(),
    'constants' => [],
    'functions' => [],
    'classes' => [],
];

foreach ($ext->getConstants() as $name => $value) {
    $dump['constants'][] = dump_constant((string) $name, $value, [], false, null);
}

$functions = $ext->getFunctions();
\uksort($functions, 'strcasecmp');
foreach ($functions as $function) {
    $dump['functions'][] = dump_function($function, []);
}

$classes = $ext->getClasses();
\uksort($classes, 'strcasecmp');
$dumpedClassNames = [];
foreach ($classes as $class) {
    if ($class->isAnonymous()) {
        continue;
    }
    $dump['classes'][] = dump_class($class);
    $dumpedClassNames[\strtolower($class->getName())] = true;
}

$hostPdo = dump_pdo_driver_host_class($extName, $dumpedClassNames);
if ($hostPdo !== null) {
    $dump['classes'][] = $hostPdo;
}

$flags = \JSON_UNESCAPED_UNICODE | \JSON_INVALID_UTF8_SUBSTITUTE;
if (\defined('JSON_THROW_ON_ERROR')) {
    $flags |= \JSON_THROW_ON_ERROR;
}

echo \json_encode($dump, $flags);
exit(0);

/**
 * @param list<string> $modifiers
 * @return array<string, mixed>
 */
function dump_constant(string $name, mixed $value, array $modifiers, bool $deprecated, ?string $docComment, mixed $declaredType = null): array
{
    $encoded = encode_value($value);
    $type = $declaredType instanceof \ReflectionType
        ? encode_type($declaredType)
        : infer_type_from_value($encoded);

    return [
        'name' => $name,
        'value' => $encoded,
        'type' => $type,
        'modifiers' => $modifiers,
        'deprecated' => $deprecated,
        'docComment' => normalize_doc($docComment),
    ];
}

/**
 * @param list<string> $modifiers
 * @return array<string, mixed>
 */
function dump_function(\ReflectionFunctionAbstract $function, array $modifiers): array
{
    $returnType = $function->getReturnType();
    $tentative = false;
    if ($returnType === null && \method_exists($function, 'getTentativeReturnType')) {
        $tentativeType = $function->getTentativeReturnType();
        if ($tentativeType !== null) {
            $returnType = $tentativeType;
            $tentative = true;
        }
    }

    return [
        'name' => $function->getName(),
        'params' => \array_map('dump_parameter', $function->getParameters()),
        'returnType' => encode_type($returnType),
        // Missing return types are filled in C# (constructors/destructors -> void, else mixed).
        'returnByRef' => $function->returnsReference(),
        'tentativeReturn' => $tentative,
        'deprecated' => is_deprecated($function),
        'docComment' => normalize_doc($function->getDocComment() ?: null),
        'attributes' => dump_attributes($function),
        'modifiers' => $modifiers,
    ];
}

/**
 * @return array<string, mixed>
 */
function dump_parameter(\ReflectionParameter $parameter): array
{
    $default = null;
    if ($parameter->isDefaultValueAvailable()) {
        try {
            if ($parameter->isDefaultValueConstant()) {
                $default = [
                    'kind' => 'const',
                    'value' => null,
                    'constName' => $parameter->getDefaultValueConstantName(),
                ];
            } else {
                $default = encode_value($parameter->getDefaultValue());
            }
        } catch (\Throwable) {
            $default = ['kind' => 'unavailable', 'value' => null, 'constName' => null];
        }
    }

    return [
        'name' => $parameter->getName(),
        'type' => encode_type($parameter->getType()),
        'optional' => $parameter->isOptional(),
        'default' => $default,
        'variadic' => $parameter->isVariadic(),
        'byRef' => $parameter->isPassedByReference(),
        'promoted' => $parameter->isPromoted(),
        'attributes' => dump_attributes($parameter),
    ];
}

/**
 * PDO drivers register MYSQL_*, SQLITE_*, … constants on class PDO, which
 * ReflectionExtension::getClasses() does not return (PDO belongs to ext pdo).
 * Harvest those constants so Layer 1 for pdo_* can emit `partial class PDO`.
 * Prefix filtering (which driver owns which constant) happens in C#.
 *
 * @param array<string, true> $alreadyDumped lowercased PHP class names
 * @return ?array<string, mixed>
 */
function dump_pdo_driver_host_class(string $extName, array $alreadyDumped): ?array
{
    if (!\str_starts_with(\strtolower($extName), 'pdo_')) {
        return null;
    }
    if (isset($alreadyDumped['pdo'])) {
        return null;
    }
    if (!\class_exists(\PDO::class, false)) {
        return null;
    }

    $pdo = dump_class(new \ReflectionClass(\PDO::class));
    $pdo['methods'] = [];
    $pdo['properties'] = [];
    $pdo['enumCases'] = [];
    $pdo['implements'] = [];
    $pdo['uses'] = [];
    $pdo['extends'] = null;
    if (!\is_array($pdo['constants'] ?? null) || $pdo['constants'] === []) {
        return null;
    }

    return $pdo;
}

/**
 * @return array<string, mixed>
 */
function dump_class(\ReflectionClass $class): array
{
    $kind = 'class';
    $backingType = null;
    $enumCases = [];
    if ($class->isEnum()) {
        $kind = 'enum';
        $enum = new \ReflectionEnum($class->getName());
        if ($enum->isBacked()) {
            $backing = $enum->getBackingType();
            $backingType = $backing instanceof \ReflectionNamedType ? $backing->getName() : null;
        }
        foreach ($enum->getCases() as $case) {
            $backingValue = null;
            if ($case instanceof \ReflectionEnumBackedCase) {
                $backingValue = encode_value($case->getBackingValue());
            }
            $enumCases[] = [
                'name' => $case->getName(),
                'backing' => $backingValue,
                'docComment' => normalize_doc($case->getDocComment() ?: null),
            ];
        }
    } elseif ($class->isInterface()) {
        $kind = 'interface';
    } elseif ($class->isTrait()) {
        $kind = 'trait';
    }

    $parent = $class->getParentClass();
    $implements = [];
    foreach ($class->getInterfaceNames() as $interface) {
        $implements[] = fqn($interface);
    }
    $uses = [];
    foreach ($class->getTraits() as $traitName => $trait) {
        $uses[] = fqn(\is_string($traitName) ? $traitName : $trait->getName());
    }

    $constants = [];
    foreach ($class->getReflectionConstants() as $constant) {
        if ($constant->getDeclaringClass()->getName() !== $class->getName()) {
            continue;
        }
        // Enum cases are internally represented as class constants, so `getReflectionConstants()`
        // on an enum also yields one entry per `case` — as a plain `ReflectionClassConstant`,
        // not a `ReflectionEnumUnitCase` (that subclass is only returned by
        // `ReflectionEnum::getCases()`, used above). Cases are already captured via
        // `$enum->getCases()`; including them here too would emit each case as both a `const`
        // and a `case` declaration in the same tyhpdef type.
        if (\method_exists($constant, 'isEnumCase') && $constant->isEnumCase()) {
            continue;
        }
        $declaredType = \method_exists($constant, 'getType') ? $constant->getType() : null;
        $constants[] = dump_constant(
            $constant->getName(),
            $constant->getValue(),
            visibility_modifiers($constant->getModifiers(), includeAbstractFinal: false),
            is_deprecated($constant),
            $constant->getDocComment() ?: null,
            $declaredType
        );
    }

    $properties = [];
    foreach ($class->getProperties() as $property) {
        if ($property->getDeclaringClass()->getName() !== $class->getName()) {
            continue;
        }
        $properties[] = dump_property($property);
    }

    $methods = [];
    foreach ($class->getMethods() as $method) {
        if ($method->getDeclaringClass()->getName() !== $class->getName()) {
            continue;
        }
        $methods[] = dump_function($method, visibility_modifiers($method->getModifiers(), includeAbstractFinal: true));
    }

    $modifiers = [];
    if ($class->isAbstract() && !$class->isInterface()) {
        $modifiers[] = 'abstract';
    }
    if ($class->isFinal() && !$class->isEnum()) {
        $modifiers[] = 'final';
    }
    if (\method_exists($class, 'isReadOnly') && $class->isReadOnly()) {
        $modifiers[] = 'readonly';
    }

    return [
        'kind' => $kind,
        'name' => $class->getShortName(),
        'fqn' => fqn($class->getName()),
        'modifiers' => $modifiers,
        'extends' => $parent ? fqn($parent->getName()) : null,
        'implements' => $implements,
        'uses' => $uses,
        'isAnonymous' => false,
        'backingType' => $backingType,
        'docComment' => normalize_doc($class->getDocComment() ?: null),
        'deprecated' => is_deprecated($class),
        'attributes' => dump_attributes($class),
        'constants' => $constants,
        'properties' => $properties,
        'methods' => $methods,
        'enumCases' => $enumCases,
    ];
}

/**
 * @return array<string, mixed>
 */
function dump_property(\ReflectionProperty $property): array
{
    $hasDefault = $property->hasDefaultValue();
    $default = null;
    if ($hasDefault) {
        try {
            $default = encode_value($property->getDefaultValue());
        } catch (\Throwable) {
            $default = ['kind' => 'unavailable', 'value' => null, 'constName' => null];
        }
    }

    return [
        'name' => $property->getName(),
        'type' => encode_type($property->getType()),
        'modifiers' => visibility_modifiers($property->getModifiers(), includeAbstractFinal: false),
        'hasDefault' => $hasDefault,
        'default' => $default,
        'deprecated' => is_deprecated($property),
        'docComment' => normalize_doc($property->getDocComment() ?: null),
        'attributes' => dump_attributes($property),
        'hooks' => dump_property_hooks($property),
    ];
}

/**
 * @return list<array<string, mixed>>
 */
function dump_property_hooks(\ReflectionProperty $property): array
{
    if (!\method_exists($property, 'getHooks')) {
        return [];
    }

    try {
        $reflected = $property->getHooks();
    } catch (\Throwable) {
        return [];
    }

    if (!\is_array($reflected) || $reflected === []) {
        return [];
    }

    $hooks = [];
    foreach ($reflected as $name => $hook) {
        if (!$hook instanceof \ReflectionMethod) {
            continue;
        }

        $hookName = \is_string($name) && $name !== '' ? $name : $hook->getName();
        $hooks[] = [
            'name' => $hookName,
            'returnsRef' => $hook->returnsReference(),
            'modifiers' => hook_modifiers($hook),
            'attributes' => dump_attributes($hook),
        ];
    }

    return $hooks;
}

/**
 * Compact hook modifiers: omit public (the tyhpdef default) so writer emits `{ get; set; }`.
 *
 * @return list<string>
 */
function hook_modifiers(\ReflectionMethod $hook): array
{
    $result = [];
    if ($hook->isFinal()) {
        $result[] = 'final';
    }
    if ($hook->isPrivate()) {
        $result[] = 'private';
    } elseif ($hook->isProtected()) {
        $result[] = 'protected';
    }

    return $result;
}

/**
 * @return list<array<string, mixed>>
 */
function dump_attributes(object $reflector): array
{
    if (!\method_exists($reflector, 'getAttributes')) {
        return [];
    }

    $result = [];
    foreach ($reflector->getAttributes() as $attribute) {
        $args = [];
        try {
            foreach ($attribute->getArguments() as $key => $argument) {
                $encoded = encode_value($argument);
                // PHP stubs often write #[\Deprecated(since: '8.1', message: 'use …')].
                // Flattening named keys to positionals swaps those vs the ctor
                // (message, since) and TYHP4500 then shows the version string.
                if (\is_string($key) && $key !== '') {
                    $encoded['argName'] = $key;
                }
                $args[] = $encoded;
            }
        } catch (\Throwable) {
            $args[] = ['kind' => 'unavailable', 'value' => null, 'constName' => null];
        }
        $result[] = [
            'name' => fqn($attribute->getName()),
            'args' => $args,
        ];
    }
    return $result;
}

/**
 * @return array<string, mixed>
 */
function encode_type(?\ReflectionType $type): array
{
    if ($type === null) {
        return [
            'kind' => 'none',
            'text' => '',
            'name' => null,
            'builtin' => false,
            'nullable' => false,
            'types' => [],
        ];
    }

    if ($type instanceof \ReflectionUnionType) {
        $parts = [];
        foreach ($type->getTypes() as $inner) {
            $parts[] = encode_type($inner);
        }
        return [
            'kind' => 'union',
            'text' => join_encoded_types($parts, '|'),
            'name' => null,
            'builtin' => false,
            'nullable' => $type->allowsNull(),
            'types' => $parts,
        ];
    }

    if ($type instanceof \ReflectionIntersectionType) {
        $parts = [];
        foreach ($type->getTypes() as $inner) {
            $parts[] = encode_type($inner);
        }
        return [
            'kind' => 'intersection',
            'text' => join_encoded_types($parts, '&'),
            'name' => null,
            'builtin' => false,
            'nullable' => false,
            'types' => $parts,
        ];
    }

    if ($type instanceof \ReflectionNamedType) {
        $name = $type->getName();
        $builtin = $type->isBuiltin();
        $rendered = $builtin ? $name : fqn($name);
        $nullable = $type->allowsNull() && $name !== 'mixed' && $name !== 'null';
        if ($nullable) {
            return [
                'kind' => 'nullable',
                'text' => '?' . $rendered,
                'name' => $rendered,
                'builtin' => $builtin,
                'nullable' => true,
                'types' => [[
                    'kind' => 'named',
                    'text' => $rendered,
                    'name' => $rendered,
                    'builtin' => $builtin,
                    'nullable' => false,
                    'types' => [],
                ]],
            ];
        }
        return [
            'kind' => 'named',
            'text' => $rendered,
            'name' => $rendered,
            'builtin' => $builtin,
            'nullable' => false,
            'types' => [],
        ];
    }

    return [
        'kind' => 'named',
        'text' => (string) $type,
        'name' => (string) $type,
        'builtin' => false,
        'nullable' => $type->allowsNull(),
        'types' => [],
    ];
}

/**
 * @param list<array<string, mixed>> $parts
 */
function join_encoded_types(array $parts, string $sep): string
{
    $texts = [];
    foreach ($parts as $part) {
        $text = (string) ($part['text'] ?? '');
        $kind = (string) ($part['kind'] ?? '');
        if ($kind === 'intersection' && $sep === '|') {
            $text = '(' . $text . ')';
        }
        $texts[] = $text;
    }
    return \implode($sep, $texts);
}

/**
 * @return array<string, mixed>
 */
function encode_value(mixed $value): array
{
    if ($value === null) {
        return ['kind' => 'null', 'value' => null, 'constName' => null];
    }
    if (\is_bool($value)) {
        return ['kind' => 'bool', 'value' => $value, 'constName' => null];
    }
    if (\is_int($value)) {
        return ['kind' => 'int', 'value' => $value, 'constName' => null];
    }
    if (\is_float($value)) {
        if (\is_nan($value)) {
            return ['kind' => 'nan', 'value' => null, 'constName' => null];
        }
        if (\is_infinite($value)) {
            return ['kind' => $value > 0 ? 'inf' : 'neginf', 'value' => null, 'constName' => null];
        }
        return ['kind' => 'float', 'value' => $value, 'constName' => null];
    }
    if (\is_string($value)) {
        return ['kind' => 'string', 'value' => $value, 'constName' => null];
    }
    if (\is_array($value)) {
        $isList = \array_is_list($value);
        $encoded = [];
        foreach ($value as $key => $item) {
            if (\is_object($item) || \is_resource($item)) {
                return ['kind' => 'unavailable', 'value' => null, 'constName' => null];
            }
            if ($isList) {
                $encoded[] = encode_value($item);
            } else {
                $encoded[(string) $key] = encode_value($item);
            }
        }
        return ['kind' => 'array', 'value' => $encoded, 'constName' => null];
    }

    return ['kind' => 'unavailable', 'value' => null, 'constName' => null];
}

/**
 * @param array<string, mixed> $encoded
 * @return array<string, mixed>
 */
function infer_type_from_value(array $encoded): array
{
    $kind = (string) ($encoded['kind'] ?? 'none');
    $map = [
        'null' => 'null',
        'bool' => 'bool',
        'int' => 'int',
        'float' => 'float',
        'nan' => 'float',
        'inf' => 'float',
        'neginf' => 'float',
        'string' => 'string',
        'array' => 'array',
        'const' => '',
        'unavailable' => '',
    ];
    $text = $map[$kind] ?? '';
    if ($text === '') {
        return [
            'kind' => 'none',
            'text' => '',
            'name' => null,
            'builtin' => false,
            'nullable' => false,
            'types' => [],
        ];
    }
    return [
        'kind' => 'named',
        'text' => $text,
        'name' => $text,
        'builtin' => true,
        'nullable' => false,
        'types' => [],
    ];
}

/**
 * @return list<string>
 */
function visibility_modifiers(int $modifiers, bool $includeAbstractFinal): array
{
    $result = [];
    if ($modifiers & \ReflectionClass::IS_READONLY || (\defined('ReflectionProperty::IS_READONLY') && ($modifiers & \ReflectionProperty::IS_READONLY))) {
        $result[] = 'readonly';
    }
    if ($modifiers & \ReflectionMethod::IS_ABSTRACT && $includeAbstractFinal) {
        $result[] = 'abstract';
    }
    if ($modifiers & \ReflectionMethod::IS_FINAL && $includeAbstractFinal) {
        $result[] = 'final';
    }
    if ($modifiers & \ReflectionMethod::IS_STATIC) {
        $result[] = 'static';
    }
    if ($modifiers & \ReflectionMethod::IS_PUBLIC) {
        $result[] = 'public';
    } elseif ($modifiers & \ReflectionMethod::IS_PROTECTED) {
        $result[] = 'protected';
    } elseif ($modifiers & \ReflectionMethod::IS_PRIVATE) {
        $result[] = 'private';
    }
    return $result;
}

function is_deprecated(object $reflector): bool
{
    if (\method_exists($reflector, 'isDeprecated') && $reflector->isDeprecated()) {
        return true;
    }
    if (\method_exists($reflector, 'getAttributes')) {
        foreach ($reflector->getAttributes() as $attribute) {
            $name = $attribute->getName();
            if ($name === 'Deprecated' || $name === 'PHPDeprecated' || \str_ends_with($name, '\\Deprecated')) {
                return true;
            }
        }
    }
    if (\method_exists($reflector, 'getDocComment')) {
        $doc = $reflector->getDocComment();
        if (\is_string($doc) && \preg_match('/@deprecated\b/i', $doc) === 1) {
            return true;
        }
    }
    return false;
}

function normalize_doc(string|false|null $doc): ?string
{
    if (!\is_string($doc) || \trim($doc) === '') {
        return null;
    }
    return $doc;
}

function fqn(string $name): string
{
    $name = \ltrim($name, '\\');
    $builtins = [
        'int', 'float', 'string', 'bool', 'array', 'object', 'mixed', 'void', 'never',
        'iterable', 'callable', 'null', 'false', 'true', 'self', 'parent', 'static',
    ];
    if (\in_array(\strtolower($name), $builtins, true)) {
        return $name;
    }
    return '\\' . $name;
}
