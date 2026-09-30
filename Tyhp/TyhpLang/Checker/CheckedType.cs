using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    public enum CheckedTypeKind
    {
        Simple,
        Union,
        Intersection,
        Nullable,
        Generic,
        Literal,
        Struct,
        Callable,
        Never,
        Void,
        Mixed,
        Unresolved,
        Inferred,
        TemplateString,
        /// <summary>
        /// Late-bound <c>static</c> (LSB). Distinct from the declaring class so assignability
        /// only accepts values verifiably typed as <c>static</c> (<c>$this</c>, other
        /// <c>: static</c> results, <c>instanceof static</c>, etc.).
        /// </summary>
        Static,
        /// <summary>
        /// Transient marker for bare <c>...</c> as a generic type argument. Valid only as
        /// the first of exactly two arguments on built-in <c>callable</c>.
        /// </summary>
        CallableArityWildcard,
        /// <summary>
        /// Transient marker for postfix <c>T...</c> as a generic type argument
        /// (homogeneous PHP variadic). Valid only as the last parameter before return
        /// on built-in <c>callable</c>.
        /// </summary>
        HomogeneousVariadic,
        /// <summary>
        /// Ordered list of types from pack-preserving utilities and Rest splice.
        /// Not a union and not an array.
        /// </summary>
        ParameterPack,
        /// <summary>
        /// Structural object type from a <c>type Alias = object { … }</c> (or an
        /// intersection that includes a shape). Not a PHP class.
        /// </summary>
        ObjectShape,
    }

    public sealed class SimpleCheckedType : ICheckedType
    {
        public SimpleCheckedType(IBaseSymbol resolvedSymbol)
        {
            ResolvedSymbol = resolvedSymbol;
        }

        public IBaseSymbol ResolvedSymbol { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.Simple;

        // Builtins are keywords (bool, true, int, …) — spell them bare, not as `\true`.
        public string DisplayName => ResolvedSymbol is BuiltInTypeSymbol
            ? ResolvedSymbol.Name
            : ResolvedSymbol.FullyQualifiedName;

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    public sealed class UnionCheckedType : ICheckedType
    {
        public UnionCheckedType(IReadOnlyList<ICheckedType> members)
        {
            Members = members;
        }

        public IReadOnlyList<ICheckedType> Members { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.Union;

        public string DisplayName => CheckedTypeDisplay.FormatUnion(Members);

        public bool IsNullable => Members.Any(m => m.IsNullable || m.Kind == CheckedTypeKind.Literal && m is LiteralCheckedType lit && lit.Value is null);

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => Members.Any(m => m.IsMixed);
    }

    public sealed class IntersectionCheckedType : ICheckedType
    {
        public IntersectionCheckedType(IReadOnlyList<ICheckedType> members)
        {
            Members = members;
        }

        public IReadOnlyList<ICheckedType> Members { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.Intersection;

        public string DisplayName => string.Join("&", Members.Select(m => m.DisplayName));

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    public sealed class NullableCheckedType : ICheckedType
    {
        public NullableCheckedType(ICheckedType innerType)
        {
            InnerType = innerType;
        }

        public ICheckedType InnerType { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.Nullable;

        public string DisplayName => CheckedTypeDisplay.FormatNullable(InnerType);

        public bool IsNullable => true;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => InnerType.IsMixed;
    }

    public sealed class GenericCheckedType : ICheckedType
    {
        public GenericCheckedType(ICheckedType baseType, IReadOnlyList<ICheckedType> typeArguments)
        {
            BaseType = baseType;
            TypeArguments = typeArguments;
        }

        public ICheckedType BaseType { get; }

        public IReadOnlyList<ICheckedType> TypeArguments { get; }

        /// <summary>
        /// Story 21.6 Phase 5: inferred Closures from arrow functions, first-class callables,
        /// and <c>fromCallable</c> of a non-Closure cannot be <c>bind</c>/<c>bindTo</c>/<c>call</c>
        /// rebound. Not part of assignability or <see cref="DisplayName"/>.
        /// </summary>
        public bool IsNonRebindableClosure { get; init; }

        public CheckedTypeKind Kind => CheckedTypeKind.Generic;

        public string DisplayName =>
            TypeArguments.Count == 0
                ? BaseType.DisplayName
                : $"{BaseType.DisplayName}<{string.Join(", ", TypeArguments.Select(a => a.DisplayName))}>";

        public bool IsNullable => BaseType.IsNullable;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => BaseType.IsMixed;

        public GenericCheckedType With(
            ICheckedType? baseType = null,
            IReadOnlyList<ICheckedType>? typeArguments = null) =>
            new GenericCheckedType(baseType ?? BaseType, typeArguments ?? TypeArguments)
            {
                IsNonRebindableClosure = IsNonRebindableClosure,
            };
    }

    public sealed class LiteralCheckedType : ICheckedType
    {
        public LiteralCheckedType(object? value, SimpleCheckedType underlyingType)
        {
            Value = value;
            UnderlyingType = underlyingType;
        }

        public object? Value { get; }

        public SimpleCheckedType UnderlyingType { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.Literal;

        public string DisplayName => Value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string s => $"'{s}'",
            _ => Value?.ToString() ?? "null",
        };

        public bool IsNullable => Value is null;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    public sealed class TemplateStringCheckedType : ICheckedType
    {
        public TemplateStringCheckedType(TemplateStringPattern pattern)
        {
            Pattern = pattern;
        }

        public TemplateStringPattern Pattern { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.TemplateString;

        public string DisplayName => Pattern.DisplayName;

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    public sealed record StructPropertyInfo(
        ICheckedType Type,
        bool IsReadonly = false,
        int? IntegerKeyAlias = null,
        bool IsOptional = false)
    {
        public StructPropertyInfo WithType(ICheckedType type) => this with { Type = type };
    }

    public sealed class StructCheckedType : ICheckedType
    {
        public StructCheckedType(Dictionary<string, StructPropertyInfo> properties)
        {
            Properties = properties;
        }

        public Dictionary<string, StructPropertyInfo> Properties { get; }

        /// <summary>
        /// True when at least one property stores a PHP integer array key
        /// (<c>T 0 as $_1</c> / <c>__CallableParametersTuple</c>).
        /// </summary>
        public bool HasIntegerKeyAliases
        {
            get
            {
                foreach (var property in Properties.Values)
                {
                    if (property.IntegerKeyAlias is not null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool TryGetPropertyByIntegerKey(int key, out StructPropertyInfo? property)
        {
            foreach (var info in Properties.Values)
            {
                if (info.IntegerKeyAlias == key)
                {
                    property = info;
                    return true;
                }
            }

            property = null;
            return false;
        }

        public static StructCheckedType FromMutableProperties(Dictionary<string, ICheckedType> properties) =>
            new(properties.ToDictionary(
                pair => pair.Key,
                pair => new StructPropertyInfo(pair.Value)));

        public CheckedTypeKind Kind => CheckedTypeKind.Struct;

        public string DisplayName
        {
            get
            {
                if (Properties.Count == 0)
                {
                    return "struct{}";
                }

                if (Properties.Count > 6)
                {
                    return "struct{...}";
                }

                var keys = string.Join(", ", Properties.Select(pair =>
                {
                    var name = pair.Key.TrimStart('$');
                    return pair.Value.IsOptional ? name + "?" : name;
                }));
                return $"struct{{{keys}}}";
            }
        }

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    /// <summary>
    /// Structural object type from <c>object { … }</c> on a type-alias RHS.
    /// Width subtyping matches public instance members (except <c>__construct</c>).
    /// Identity is the declaring alias plus type arguments, or a structural member map.
    /// </summary>
    public sealed class ObjectShapeCheckedType : ICheckedType
    {
        public ObjectShapeCheckedType(
            TyhpObjectShapeAst shape,
            IBaseSymbol? declaringAlias = null,
            IReadOnlyList<ICheckedType>? typeArguments = null,
            ObjectShapeMemberMap? members = null)
        {
            Shape = shape;
            DeclaringAlias = declaringAlias;
            TypeArguments = typeArguments ?? [];
            Members = members;
        }

        public TyhpObjectShapeAst Shape { get; }

        public IBaseSymbol? DeclaringAlias { get; }

        public IReadOnlyList<ICheckedType> TypeArguments { get; }

        /// <summary>
        /// Resolved members from the shape AST. Null on cycle stubs (identity only).
        /// </summary>
        public ObjectShapeMemberMap? Members { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.ObjectShape;

        public string DisplayName
        {
            get
            {
                var name = !string.IsNullOrEmpty(DeclaringAlias?.Name)
                    ? DeclaringAlias!.Name
                    : "object{…}";
                if (TypeArguments.Count == 0)
                {
                    return name;
                }

                return $"{name}<{string.Join(", ", TypeArguments.Select(arg => arg.DisplayName))}>";
            }
        }

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

        public sealed class CallableCheckedType : ICheckedType
        {
            public CallableCheckedType(
                IReadOnlyList<ICheckedType> parameterTypes,
                ICheckedType returnType,
                IReadOnlyList<string?>? parameterNames = null,
                bool lastParameterIsVariadic = false,
                bool isAnyArity = false)
            {
                IsAnyArity = isAnyArity;
                ParameterTypes = isAnyArity ? [] : parameterTypes;
                ReturnType = returnType;
                ParameterNames = isAnyArity
                    ? null
                    : NormalizeParameterNames(parameterTypes.Count, parameterNames);
                LastParameterIsVariadic = !isAnyArity
                    && lastParameterIsVariadic
                    && parameterTypes.Count > 0;
            }

        public IReadOnlyList<ICheckedType> ParameterTypes { get; }

        public ICheckedType ReturnType { get; }

        /// <summary>
        /// Parameter names when this facet came from a function, method, closure, or
        /// <c>callable(…): R</c> shape. Length matches <see cref="ParameterTypes"/> when
        /// non-null. Equality ignores names.
        /// </summary>
        public IReadOnlyList<string?>? ParameterNames { get; }

        /// <summary>
        /// True when this facet is a trailing PHP variadic
        /// (<c>f(T $a, U ...$rest)</c> or <c>callable(T, U ...): R</c>).
        /// Equality includes this flag.
        /// </summary>
        public bool LastParameterIsVariadic { get; }

        /// <summary>
        /// True when this is the any-arity wildcard <c>callable(...): TReturn</c>.
        /// Distinct from a zero-parameter facet (empty <see cref="ParameterTypes"/> with
        /// <see cref="IsAnyArity"/> false). Equality includes this flag.
        /// </summary>
        public bool IsAnyArity { get; }

        /// <summary>
        /// Rebuilds this facet with substituted parameter/return types, keeping names, the
        /// variadic flag, and the any-arity flag.
        /// </summary>
        internal CallableCheckedType MapTypes(Func<ICheckedType, ICheckedType> map)
        {
            var mappedParams = ParameterTypes.Select(map).ToList();
            var mappedReturn = map(ReturnType);
            if (IsAnyArity)
            {
                return new CallableCheckedType([], mappedReturn, isAnyArity: true);
            }

            var spliced = ParameterPack.SpliceParameterList(mappedParams, out var packVariadic);
            return new CallableCheckedType(
                spliced,
                mappedReturn,
                ParameterNames is null || spliced.Count != ParameterTypes.Count ? null : ParameterNames,
                LastParameterIsVariadic || packVariadic);
        }

        private static IReadOnlyList<string?>? NormalizeParameterNames(
            int arity,
            IReadOnlyList<string?>? parameterNames)
        {
            if (parameterNames is null || parameterNames.Count == 0)
            {
                return null;
            }

            if (parameterNames.Count == arity)
            {
                return parameterNames.All(name => name is null) ? null : parameterNames;
            }

            var normalized = new string?[arity];
            var copy = Math.Min(arity, parameterNames.Count);
            for (var i = 0; i < copy; i++)
            {
                normalized[i] = parameterNames[i];
            }

            return normalized.All(name => name is null) ? null : normalized;
        }

        public CheckedTypeKind Kind => CheckedTypeKind.Callable;

        public string DisplayName
        {
            get
            {
                if (IsAnyArity)
                {
                    return $"callable(...): {ReturnType.DisplayName}";
                }

                var paramNames = new string[ParameterTypes.Count];
                for (var i = 0; i < ParameterTypes.Count; i++)
                {
                    var name = ParameterTypes[i].DisplayName;
                    var isVariadic = LastParameterIsVariadic && i == ParameterTypes.Count - 1;
                    var parameterName = ParameterNames is not null && i < ParameterNames.Count
                        ? ParameterNames[i]
                        : null;

                    if (isVariadic)
                    {
                        // `callable(T ...): R` (unnamed) / `callable(T ...$args): R` (named) —
                        // no space between `...` and a written `$name`.
                        name += string.IsNullOrEmpty(parameterName)
                            ? " ..."
                            : " ...$" + parameterName;
                    }
                    else if (!string.IsNullOrEmpty(parameterName))
                    {
                        name += " $" + parameterName;
                    }

                    paramNames[i] = name;
                }

                return $"callable({string.Join(", ", paramNames)}): {ReturnType.DisplayName}";
            }
        }

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    /// <summary>
    /// Late-bound <c>static</c> relative type. <see cref="DeclaringType"/> is the enclosing
    /// class spelling used for member lookup and call-site expansion (replaced by the receiver /
    /// call-site class reference when a <c>: static</c> method is invoked).
    /// </summary>
    public sealed class StaticCheckedType : ICheckedType
    {
        public StaticCheckedType(ICheckedType declaringType)
        {
            DeclaringType = declaringType;
        }

        /// <summary>Enclosing class type at the spelling site (often an open generic).</summary>
        public ICheckedType DeclaringType { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.Static;

        public string DisplayName => "static";

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    public sealed class SpecialCheckedType : ICheckedType
    {
        private SpecialCheckedType(CheckedTypeKind kind, string displayName)
        {
            Kind = kind;
            DisplayName = displayName;
        }

        public CheckedTypeKind Kind { get; }

        public string DisplayName { get; }

        public bool IsNullable => false;

        public bool IsNever => Kind == CheckedTypeKind.Never;

        public bool IsVoid => Kind == CheckedTypeKind.Void;

        public bool IsMixed => Kind == CheckedTypeKind.Mixed;

        internal static SpecialCheckedType Create(CheckedTypeKind kind, string displayName) =>
            new(kind, displayName);
    }

    /// <summary>
    /// Error-recovery singleton produced when a type cannot be resolved — an unresolved symbol, an
    /// unhandled AST shape, or a not-yet-inferred generic parameter. It is deliberately assignable
    /// to and from everything so a single resolution failure does not cascade into a wave of
    /// secondary diagnostics.
    /// </summary>
    /// <remarks>
    /// This is a compiler-internal marker, not a language type: it is not registered as a built-in
    /// so users cannot write it as an annotation. It is emphatically NOT the strict top type —
    /// <c>mixed</c> plays that role (assignable from anything, assignable to nothing without
    /// narrowing). Seeing <c>unresolved</c> in a diagnostic means the checker gave up somewhere,
    /// so prefer reporting the underlying resolution failure over surfacing this name.
    /// </remarks>
    public sealed class UnresolvedCheckedType : ICheckedType
    {
        private UnresolvedCheckedType()
        {
        }

        public static UnresolvedCheckedType Instance { get; } = new();

        public CheckedTypeKind Kind => CheckedTypeKind.Unresolved;

        public string DisplayName => "unresolved";

        public bool IsNullable => true;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => true;
    }

    /// <summary>
    /// Transient marker for bare <c>...</c> as a generic type argument. Not a
    /// user-written value type. Valid unknown-arity callables use
    /// <c>callable(...): R</c> shapes, not this marker.
    /// </summary>
    public sealed class CallableArityWildcardCheckedType : ICheckedType
    {
        private CallableArityWildcardCheckedType()
        {
        }

        public static CallableArityWildcardCheckedType Instance { get; } = new();

        public CheckedTypeKind Kind => CheckedTypeKind.CallableArityWildcard;

        public string DisplayName => "...";

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    /// <summary>
    /// Transient marker for postfix <c>T...</c> as a generic type argument. Not valid on
    /// user generics / <c>array</c> / <c>\Closure</c>. Homogeneous variadic callable
    /// parameters are spelled <c>callable(T ...$args): R</c>.
    /// </summary>
    public sealed class HomogeneousVariadicCheckedType : ICheckedType
    {
        public HomogeneousVariadicCheckedType(ICheckedType elementType)
        {
            ElementType = elementType;
        }

        public ICheckedType ElementType { get; }

        public CheckedTypeKind Kind => CheckedTypeKind.HomogeneousVariadic;

        public string DisplayName => $"{ElementType.DisplayName}...";

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    /// <summary>
    /// How <see cref="ParameterPackCheckedType"/> maps each member of a deferred or expanded pack.
    /// </summary>
    public enum ParameterPackMap
    {
        Identity,
        Nullable,
        NonNullable,
    }

    /// <summary>
    /// Ordered list of types from pack-preserving utilities. Not a union and not an array.
    /// Expanded packs have <see cref="Members"/>; deferred packs keep
    /// <see cref="SourceCallable"/> until substitution fills <c>TCallable</c>.
    /// </summary>
    public sealed class ParameterPackCheckedType : ICheckedType
    {
        public ParameterPackCheckedType(
            IReadOnlyList<ICheckedType> members,
            ICheckedType? sourceCallable = null,
            ParameterPackMap map = ParameterPackMap.Identity,
            bool lastMemberIsVariadic = false)
        {
            Members = members;
            SourceCallable = sourceCallable;
            Map = map;
            LastMemberIsVariadic = lastMemberIsVariadic && members.Count > 0;
        }

        public IReadOnlyList<ICheckedType> Members { get; }

        public ICheckedType? SourceCallable { get; }

        public ParameterPackMap Map { get; }

        public bool LastMemberIsVariadic { get; }

        public bool IsDeferred => Members.Count == 0 && SourceCallable is not null;

        public CheckedTypeKind Kind => CheckedTypeKind.ParameterPack;

        public string DisplayName
        {
            get
            {
                if (Members.Count > 0)
                {
                    var names = new string[Members.Count];
                    for (var i = 0; i < Members.Count; i++)
                    {
                        var name = Members[i].DisplayName;
                        if (LastMemberIsVariadic && i == Members.Count - 1)
                        {
                            name += "...";
                        }

                        names[i] = name;
                    }

                    return string.Join(", ", names);
                }

                var inner = SourceCallable?.DisplayName ?? "unresolved";
                return Map switch
                {
                    ParameterPackMap.Nullable => $"__Nullable<__CallableParametersRest<{inner}>>",
                    ParameterPackMap.NonNullable => $"__NonNullable<__CallableParametersRest<{inner}>>",
                    _ => $"__CallableParametersRest<{inner}>",
                };
            }
        }

        public bool IsNullable => false;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => false;
    }

    /// <summary>Factory methods and singletons for common checked types.</summary>
    public static class CheckedTypes
    {
        public static ICheckedType Never { get; } =
            SpecialCheckedType.Create(CheckedTypeKind.Never, "never");

        public static ICheckedType Void { get; } =
            SpecialCheckedType.Create(CheckedTypeKind.Void, "void");

        public static ICheckedType Mixed { get; } =
            SpecialCheckedType.Create(CheckedTypeKind.Mixed, "mixed");

        public static ICheckedType Null { get; } =
            new LiteralCheckedType(null, new SimpleCheckedType(new BuiltInTypeSymbol("null")));

        public static ICheckedType Unresolved { get; } = UnresolvedCheckedType.Instance;

        public static ICheckedType CallableArityWildcard { get; } =
            CallableArityWildcardCheckedType.Instance;

        public static ICheckedType Bool { get; } = FromSymbol(new BuiltInTypeSymbol("bool"));
        public static ICheckedType Int { get; } = FromSymbol(new BuiltInTypeSymbol("int"));
        public static ICheckedType Float { get; } = FromSymbol(new BuiltInTypeSymbol("float"));
        public static ICheckedType String { get; } = FromSymbol(new BuiltInTypeSymbol("string"));

        /// <summary>
        /// PHP array keys are <c>int|string</c>. Bare <c>array</c> is
        /// <c>array&lt;int|string, mixed&gt;</c>; one-arg <c>array&lt;V&gt;</c> is
        /// <c>array&lt;int|string, V&gt;</c> (not int-only list keys).
        /// </summary>
        public static ICheckedType PhpArrayKey { get; } = UnionTypes(Int, String);

        /// <summary>
        /// Native PHP <c>&lt;=&gt;</c> always returns <c>-1</c>, <c>0</c>, or <c>1</c>
        /// (a subtype of <c>int</c>). Overloaded <c>&lt;=&gt;</c> uses its declared return instead.
        /// Literal values are <c>long</c> so they match declared <c>-1|0|1</c> annotations.
        /// </summary>
        public static ICheckedType SpaceshipResult { get; } = CreateSpaceshipResult();

        public static ICheckedType FromSymbol(IBaseSymbol symbol) =>
            new SimpleCheckedType(symbol);

        private static ICheckedType CreateSpaceshipResult()
        {
            var underlying = new SimpleCheckedType(new BuiltInTypeSymbol("int"));
            return UnionTypes(
            [
                new LiteralCheckedType(-1L, underlying),
                new LiteralCheckedType(0L, underlying),
                new LiteralCheckedType(1L, underlying),
            ]);
        }

        /// <summary>
        /// Resolves an AST type expression to a checked type.
        /// Full resolution is implemented in Phase 2 (<see cref="TypeInferrer"/>).
        /// </summary>
        public static ICheckedType FromTypeExpression(
            ITypeExpression typeAst,
            IBaseScope scope,
            SymbolTree symbolTree) =>
            Unresolved;

        public static ICheckedType UnionTypes(ICheckedType left, ICheckedType right)
        {
            if (ReferenceEquals(left, right) || AreTypesEqual(left, right))
            {
                return left;
            }

            return UnionTypes([left, right]);
        }

        public static ICheckedType UnionTypes(IReadOnlyList<ICheckedType> members)
        {
            var flattened = new List<ICheckedType>();
            foreach (var member in members)
            {
                if (member is UnionCheckedType union)
                {
                    flattened.AddRange(union.Members);
                }
                else
                {
                    flattened.Add(member);
                }
            }

            // Fold coexisting true/false (literal or nominal) into bool — same algebra as
            // TypeComparer.UnionTypesCore / SimplifyBoolLiterals. Declared `true|false` is then
            // assignability-equivalent to `bool` (FOUND #42); TYHP4056 still flags the spelling.
            flattened = FoldTrueFalseIntoBool(flattened);

            var distinct = new List<ICheckedType>();
            foreach (var member in flattened)
            {
                if (!distinct.Any(existing => AreTypesEqual(existing, member)))
                {
                    distinct.Add(member);
                }
            }

            return distinct.Count switch
            {
                0 => Unresolved,
                1 => distinct[0],
                _ => new UnionCheckedType(distinct),
            };
        }

        private static List<ICheckedType> FoldTrueFalseIntoBool(List<ICheckedType> members)
        {
            static bool IsTrue(ICheckedType t) =>
                t is LiteralCheckedType { Value: true }
                || (t is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol b }
                    && b.Name.Equals("true", StringComparison.OrdinalIgnoreCase));

            static bool IsFalse(ICheckedType t) =>
                t is LiteralCheckedType { Value: false }
                || (t is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol b }
                    && b.Name.Equals("false", StringComparison.OrdinalIgnoreCase));

            static bool IsBoolLiteral(ICheckedType t) => IsTrue(t) || IsFalse(t);

            if (!members.Any(IsTrue) || !members.Any(IsFalse))
            {
                return members;
            }

            var result = new List<ICheckedType>();
            var boolAdded = false;
            foreach (var member in members)
            {
                if (IsBoolLiteral(member))
                {
                    if (!boolAdded)
                    {
                        result.Add(Bool);
                        boolAdded = true;
                    }

                    continue;
                }

                result.Add(member);
            }

            return result;
        }

        public static bool AreTypesEqual(ICheckedType? left, ICheckedType? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            if (left.Kind != right.Kind)
            {
                return false;
            }

            return left.Kind switch
            {
                CheckedTypeKind.Simple =>
                    left is SimpleCheckedType ls &&
                    right is SimpleCheckedType rs &&
                    ls.ResolvedSymbol.FullyQualifiedName == rs.ResolvedSymbol.FullyQualifiedName,
                CheckedTypeKind.Union =>
                    left is UnionCheckedType lu &&
                    right is UnionCheckedType ru &&
                    lu.Members.Count == ru.Members.Count &&
                    lu.Members.Zip(ru.Members).All(pair => AreTypesEqual(pair.First, pair.Second)),
                CheckedTypeKind.Intersection =>
                    left is IntersectionCheckedType li &&
                    right is IntersectionCheckedType ri &&
                    li.Members.Count == ri.Members.Count &&
                    li.Members.Zip(ri.Members).All(pair => AreTypesEqual(pair.First, pair.Second)),
                CheckedTypeKind.Nullable =>
                    left is NullableCheckedType ln &&
                    right is NullableCheckedType rn &&
                    AreTypesEqual(ln.InnerType, rn.InnerType),
                CheckedTypeKind.Generic =>
                    left is GenericCheckedType lg &&
                    right is GenericCheckedType rg &&
                    AreTypesEqual(lg.BaseType, rg.BaseType) &&
                    lg.TypeArguments.Count == rg.TypeArguments.Count &&
                    lg.TypeArguments.Zip(rg.TypeArguments).All(pair => AreTypesEqual(pair.First, pair.Second)),
                CheckedTypeKind.Literal =>
                    left is LiteralCheckedType ll &&
                    right is LiteralCheckedType rl &&
                    Equals(ll.Value, rl.Value),
                CheckedTypeKind.TemplateString =>
                    left is TemplateStringCheckedType lt &&
                    right is TemplateStringCheckedType rt &&
                    lt.Pattern.DisplayName == rt.Pattern.DisplayName,
                CheckedTypeKind.Static =>
                    left is StaticCheckedType ls &&
                    right is StaticCheckedType rs &&
                    AreTypesEqual(ls.DeclaringType, rs.DeclaringType),
                CheckedTypeKind.ObjectShape =>
                    left is ObjectShapeCheckedType lo &&
                    right is ObjectShapeCheckedType ro &&
                    TypeComparer.AreTypesEqual(lo, ro),
                CheckedTypeKind.Never or CheckedTypeKind.Void or CheckedTypeKind.Mixed or CheckedTypeKind.Unresolved =>
                    left.DisplayName == right.DisplayName,
                CheckedTypeKind.HomogeneousVariadic =>
                    left is HomogeneousVariadicCheckedType lh &&
                    right is HomogeneousVariadicCheckedType rh &&
                    AreTypesEqual(lh.ElementType, rh.ElementType),
                CheckedTypeKind.ParameterPack =>
                    left is ParameterPackCheckedType lp &&
                    right is ParameterPackCheckedType rp &&
                    lp.Map == rp.Map &&
                    lp.LastMemberIsVariadic == rp.LastMemberIsVariadic &&
                    lp.Members.Count == rp.Members.Count &&
                    lp.Members.Zip(rp.Members).All(pair => AreTypesEqual(pair.First, pair.Second)) &&
                    AreTypesEqual(lp.SourceCallable, rp.SourceCallable),
                _ => left.DisplayName == right.DisplayName,
            };
        }
    }
}
