using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>Resolves Story 08.5 Phase 7 type-name string algebra utility types.</summary>
    internal static class TypeNameAlgebraResolver
    {
        private static readonly string[] BaseBuiltinNames =
        [
            "int", "float", "bool", "array", "string", "null", "mixed", "self", "parent", "static",
            "callable", "iterable", "object", "struct",
        ];

        public static bool IsTypeNameAlgebraBehavior(UtilityBehavior behavior) =>
            behavior is UtilityBehavior.BaseTypeName
                or UtilityBehavior.NullableBaseTypeName
                or UtilityBehavior.BaseUnionTypeName
                or UtilityBehavior.UnionTypeName
                or UtilityBehavior.BaseIntersectTypeName
                or UtilityBehavior.IntersectTypeName
                or UtilityBehavior.NotNullableUnionTypeName
                or UtilityBehavior.NotNullableIntersectTypeName
                or UtilityBehavior.NotNullableTypeName
                or UtilityBehavior.TypeName
                or UtilityBehavior.NonMatchingStringType
                or UtilityBehavior.AsNotNullableTypeName
                or UtilityBehavior.AsNullableTypeName
                or UtilityBehavior.AsTypeName
                or UtilityBehavior.AsType;

        public static ICheckedType Resolve(
            UtilityBehavior behavior,
            IReadOnlyList<ICheckedType> typeArguments,
            GlobalScope globalScope)
        {
            return behavior switch
            {
                UtilityBehavior.BaseTypeName => ResolveBaseTypeName(globalScope),
                UtilityBehavior.NullableBaseTypeName => ResolveNullableBaseTypeName(globalScope),
                UtilityBehavior.BaseUnionTypeName => ResolveBaseUnionTypeName(globalScope),
                UtilityBehavior.UnionTypeName => ResolveUnionTypeName(globalScope),
                UtilityBehavior.BaseIntersectTypeName => ResolveBaseIntersectTypeName(globalScope),
                UtilityBehavior.IntersectTypeName => ResolveIntersectTypeName(globalScope),
                UtilityBehavior.NotNullableUnionTypeName => ResolveUnionTypeName(globalScope),
                UtilityBehavior.NotNullableIntersectTypeName => ResolveIntersectTypeName(globalScope),
                UtilityBehavior.NotNullableTypeName => ResolveNotNullableTypeName(globalScope),
                UtilityBehavior.TypeName => ResolveTypeName(globalScope),
                UtilityBehavior.NonMatchingStringType => MakeUtilityType(UtilityBehavior.NonMatchingStringType, globalScope),
                UtilityBehavior.AsNotNullableTypeName => ResolveAsNotNullableTypeName(typeArguments, globalScope),
                UtilityBehavior.AsNullableTypeName => ResolveAsNullableTypeName(typeArguments, globalScope),
                UtilityBehavior.AsTypeName => ResolveAsTypeName(typeArguments, globalScope),
                UtilityBehavior.AsType => ResolveAsType(typeArguments, globalScope),
                _ => CheckedTypes.Unresolved,
            };
        }

        private static ICheckedType ResolveBaseTypeName(GlobalScope globalScope)
        {
            var members = BaseBuiltinNames
                .Select(name => new LiteralCheckedType(name, new SimpleCheckedType(new BuiltInTypeSymbol("string"))))
                .Cast<ICheckedType>()
                .ToList();

            members.Add(SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.ClassName, globalScope));
            members.Add(SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.EnumName, globalScope));
            members.Add(SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.InterfaceName, globalScope));
            return CheckedTypes.UnionTypes(members);
        }

        private static ICheckedType ResolveNullableBaseTypeName(GlobalScope globalScope) =>
            MakeTemplate("?", ResolveBaseTypeName(globalScope), "\"?${__BaseTypeName}\"");

        private static ICheckedType ResolveNullableBaseTypeNameDirect(GlobalScope globalScope) =>
            ResolveNullableBaseTypeName(globalScope);

        private static ICheckedType ResolveBaseUnionTypeName(GlobalScope globalScope) =>
            MakeTemplate("|", ResolveBaseTypeName(globalScope), "\"|${__BaseTypeName}\"");

        private static ICheckedType ResolveUnionTypeName(GlobalScope globalScope)
        {
            var baseType = ResolveBaseTypeName(globalScope);
            var segment = ResolveBaseUnionTypeName(globalScope);
            return MakeTemplate(
                segments:
                [
                    new TemplateStringSegment.HoleSegment(baseType, TemplateStringQuantifier.ExactlyOnce),
                    new TemplateStringSegment.HoleSegment(segment, TemplateStringQuantifier.OneOrMore),
                ],
                display: "\"${__BaseTypeName}${__BaseUnionTypeName}+\"");
        }

        private static ICheckedType ResolveBaseIntersectTypeName(GlobalScope globalScope)
        {
            var holeType = CheckedTypes.UnionTypes(
            [
                ResolveBaseTypeName(globalScope),
                ResolveUnionTypeName(globalScope),
            ]);
            return MakeTemplate("&", holeType, "\"&${__BaseTypeName|__UnionTypeName}\"");
        }

        private static ICheckedType ResolveIntersectTypeName(GlobalScope globalScope)
        {
            var head = CheckedTypes.UnionTypes(
            [
                ResolveBaseTypeName(globalScope),
                ResolveUnionTypeName(globalScope),
            ]);
            var segment = ResolveBaseIntersectTypeName(globalScope);
            return MakeTemplate(
                segments:
                [
                    new TemplateStringSegment.HoleSegment(head, TemplateStringQuantifier.ExactlyOnce),
                    new TemplateStringSegment.HoleSegment(segment, TemplateStringQuantifier.OneOrMore),
                ],
                display: "\"${__BaseTypeName|__UnionTypeName}${__BaseIntersectTypeName}+\"");
        }

        private static ICheckedType ResolveNotNullableTypeName(GlobalScope globalScope) =>
            CheckedTypes.UnionTypes(
            [
                ResolveBaseTypeNameWithoutNull(globalScope),
                ResolveUnionTypeName(globalScope),
                ResolveIntersectTypeName(globalScope),
            ]);

        private static ICheckedType ResolveBaseTypeNameWithoutNull(GlobalScope globalScope)
        {
            var members = BaseBuiltinNames
                .Where(name => !string.Equals(name, "null", StringComparison.Ordinal))
                .Select(name => new LiteralCheckedType(name, new SimpleCheckedType(new BuiltInTypeSymbol("string"))))
                .Cast<ICheckedType>()
                .ToList();

            members.Add(SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.ClassName, globalScope));
            members.Add(SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.EnumName, globalScope));
            members.Add(SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.InterfaceName, globalScope));
            return CheckedTypes.UnionTypes(members);
        }

        private static ICheckedType ResolveTypeName(GlobalScope globalScope) =>
            CheckedTypes.UnionTypes(
            [
                ResolveBaseTypeName(globalScope),
                ResolveNullableBaseTypeNameDirect(globalScope),
                ResolveUnionTypeName(globalScope),
                ResolveIntersectTypeName(globalScope),
                ResolveNotNullableTypeName(globalScope),
            ]);

        private static ICheckedType ResolveAsNotNullableTypeName(
            IReadOnlyList<ICheckedType> args,
            GlobalScope globalScope)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (args[0] is LiteralCheckedType { Value: "null" })
            {
                return CheckedTypes.Void;
            }

            return CheckedTypes.UnionTypes(
            [
                ResolveNotNullableTypeName(globalScope),
                MakeUtilityType(UtilityBehavior.NonMatchingStringType, globalScope),
            ]);
        }

        private static ICheckedType ResolveAsNullableTypeName(
            IReadOnlyList<ICheckedType> args,
            GlobalScope globalScope)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            return CheckedTypes.UnionTypes(
            [
                ResolveTypeName(globalScope),
                MakeUtilityType(UtilityBehavior.NonMatchingStringType, globalScope),
            ]);
        }

        private static ICheckedType ResolveAsTypeName(
            IReadOnlyList<ICheckedType> args,
            GlobalScope globalScope)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var input = args[0];
            if (input is LiteralCheckedType { Value: "null" })
            {
                return new LiteralCheckedType("null", new SimpleCheckedType(new BuiltInTypeSymbol("string")));
            }

            if (input is LiteralCheckedType { Value: string literalName })
            {
                return new LiteralCheckedType(literalName, new SimpleCheckedType(new BuiltInTypeSymbol("string")));
            }

            if (TryGetBuiltinName(input, out var builtinName))
            {
                return new LiteralCheckedType(builtinName, new SimpleCheckedType(new BuiltInTypeSymbol("string")));
            }

            if (TryGetObjectTypeName(input, out var objectName))
            {
                return CheckedTypes.UnionTypes(
                [
                    new LiteralCheckedType(objectName, new SimpleCheckedType(new BuiltInTypeSymbol("string"))),
                    SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.StructName, globalScope),
                    MakeUtilityType(UtilityBehavior.NonMatchingStringType, globalScope),
                ]);
            }

            return CheckedTypes.UnionTypes(
            [
                ResolveTypeName(globalScope),
                SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.StructName, globalScope),
                MakeUtilityType(UtilityBehavior.NonMatchingStringType, globalScope),
            ]);
        }

        private static ICheckedType ResolveAsType(
            IReadOnlyList<ICheckedType> args,
            GlobalScope globalScope)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var input = args[0];
            if (input is LiteralCheckedType { Value: string literal }
                && TryResolveAsTypeName(literal, out var resolvedFromLiteral))
            {
                return resolvedFromLiteral;
            }

            // Bare (unquoted) builtin type argument, e.g. `__AsType<int>` rather than
            // `__AsType<'int'>`: resolve the written type's own name the same way so both
            // spellings agree instead of the unquoted form silently falling to the wide union.
            if (TryGetBuiltinName(input, out var bareName) && TryResolveAsTypeName(bareName, out var resolvedFromBareName))
            {
                return resolvedFromBareName;
            }

            return CheckedTypes.UnionTypes(
            [
                CheckedTypes.Mixed,
                StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>()),
                CheckedTypes.Void,
            ]);
        }

        private static bool TryResolveAsTypeName(string name, out ICheckedType resolved)
        {
            if (string.Equals(name, "void", StringComparison.OrdinalIgnoreCase))
            {
                resolved = CheckedTypes.Void;
                return true;
            }

            if (string.Equals(name, "null", StringComparison.OrdinalIgnoreCase))
            {
                resolved = CheckedTypes.Null;
                return true;
            }

            if (string.Equals(name, "struct", StringComparison.OrdinalIgnoreCase))
            {
                resolved = StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>());
                return true;
            }

            return TryMapBuiltinLiteral(name, out resolved);
        }

        private static TemplateStringCheckedType MakeTemplate(
            string literalPrefix,
            ICheckedType holeType,
            string display) =>
            MakeTemplate(
                segments:
                [
                    new TemplateStringSegment.LiteralSegment(literalPrefix),
                    new TemplateStringSegment.HoleSegment(holeType, TemplateStringQuantifier.ExactlyOnce),
                ],
                display: display);

        private static TemplateStringCheckedType MakeTemplate(
            IReadOnlyList<TemplateStringSegment> segments,
            string display) =>
            new(TemplateStringPatternReader.CreateFromSegments(segments, display));

        private static ICheckedType MakeUtilityType(UtilityBehavior behavior, GlobalScope globalScope)
        {
            var name = behavior switch
            {
                UtilityBehavior.NonMatchingStringType => "__NonMatchingStringType",
                _ => null,
            };

            if (name is null)
            {
                return CheckedTypes.Unresolved;
            }

            var symbol = ((Binder.Scopes.Interfaces.IBaseScope)globalScope).FindChildSymbolByName(name)
                as BuiltInUtilityTypeSymbol;
            return symbol is null ? CheckedTypes.Unresolved : CheckedTypes.FromSymbol(symbol);
        }

        private static bool TryGetBuiltinName(ICheckedType type, out string name)
        {
            name = string.Empty;
            if (type is not SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol builtIn })
            {
                return false;
            }

            name = builtIn.Name;
            return !string.IsNullOrEmpty(name);
        }

        private static bool TryGetObjectTypeName(ICheckedType type, out string name)
        {
            name = string.Empty;
            if (type is not SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return false;
            }

            name = obj.FullyQualifiedName.TrimStart('\\');
            return !string.IsNullOrEmpty(name);
        }

        private static bool TryMapBuiltinLiteral(string literal, out ICheckedType type)
        {
            type = CheckedTypes.Unresolved;
            switch (literal.ToLowerInvariant())
            {
                case "int": type = CheckedTypes.Int; return true;
                case "float": type = CheckedTypes.Float; return true;
                case "bool": type = CheckedTypes.Bool; return true;
                case "string": type = CheckedTypes.String; return true;
                case "mixed": type = CheckedTypes.Mixed; return true;
                case "array": type = CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")); return true;
                case "object": type = CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object")); return true;
                case "callable": type = CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable")); return true;
                case "iterable": type = CheckedTypes.FromSymbol(new BuiltInTypeSymbol("iterable")); return true;
                default: return false;
            }
        }
    }
}
