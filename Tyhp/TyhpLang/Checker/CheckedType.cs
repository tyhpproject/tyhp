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

        public CheckedTypeKind Kind => CheckedTypeKind.Generic;

        public string DisplayName =>
            TypeArguments.Count == 0
                ? BaseType.DisplayName
                : $"{BaseType.DisplayName}<{string.Join(", ", TypeArguments.Select(a => a.DisplayName))}>";

        public bool IsNullable => BaseType.IsNullable;

        public bool IsNever => false;

        public bool IsVoid => false;

        public bool IsMixed => BaseType.IsMixed;
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

    public sealed class CallableCheckedType : ICheckedType
    {
        public CallableCheckedType(
            IReadOnlyList<ICheckedType> parameterTypes,
            ICheckedType returnType,
            IReadOnlyList<string?>? parameterNames = null,
            bool lastParameterIsVariadic = false)
        {
            ParameterTypes = parameterTypes;
            ReturnType = returnType;
            ParameterNames = NormalizeParameterNames(parameterTypes.Count, parameterNames);
            LastParameterIsVariadic = lastParameterIsVariadic && parameterTypes.Count > 0;
        }

        public IReadOnlyList<ICheckedType> ParameterTypes { get; }

        public ICheckedType ReturnType { get; }

        /// <summary>
        /// Parameter names when this facet came from a function, method, or closure symbol.
        /// Facets synthesized from <c>callable&lt;…&gt;</c> type arguments have no names.
        /// Length matches <see cref="ParameterTypes"/> when non-null. Equality ignores names.
        /// </summary>
        public IReadOnlyList<string?>? ParameterNames { get; }

        /// <summary>
        /// True when this facet is the single-extra variadic sibling
        /// (<c>f(T $a, U ...$rest)</c> modeled as arity N+1). Equality ignores this flag.
        /// </summary>
        public bool LastParameterIsVariadic { get; }

        /// <summary>
        /// Rebuilds this facet with substituted parameter/return types, keeping names and the
        /// variadic flag.
        /// </summary>
        internal CallableCheckedType MapTypes(Func<ICheckedType, ICheckedType> map) =>
            new(ParameterTypes.Select(map).ToList(), map(ReturnType), ParameterNames, LastParameterIsVariadic);

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

        public string DisplayName =>
            $"callable({string.Join(", ", ParameterTypes.Select(p => p.DisplayName))}): {ReturnType.DisplayName}";

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

        public static ICheckedType Bool { get; } = FromSymbol(new BuiltInTypeSymbol("bool"));
        public static ICheckedType Int { get; } = FromSymbol(new BuiltInTypeSymbol("int"));
        public static ICheckedType Float { get; } = FromSymbol(new BuiltInTypeSymbol("float"));
        public static ICheckedType String { get; } = FromSymbol(new BuiltInTypeSymbol("string"));

        public static ICheckedType FromSymbol(IBaseSymbol symbol) =>
            new SimpleCheckedType(symbol);

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
                CheckedTypeKind.Never or CheckedTypeKind.Void or CheckedTypeKind.Mixed or CheckedTypeKind.Unresolved =>
                    left.DisplayName == right.DisplayName,
                _ => left.DisplayName == right.DisplayName,
            };
        }
    }
}
