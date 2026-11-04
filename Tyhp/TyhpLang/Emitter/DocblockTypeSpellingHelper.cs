using System.Globalization;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Renders a checked type as a PHPStan/Psalm type string. Native PHP spellings stay in
    /// <see cref="TypeSpellingHelper"/>; this helper keeps the information those spellings erase
    /// (generics, array shapes, <c>class-string</c>, callable signatures).
    /// Symbol names are fully qualified. A <c>use … as X</c> alias spells as the real FQN.
    /// Refined scalars such as <c>positive-int</c> are emitted only when the checked type is that
    /// refinement; a plain <c>int</c> stays <c>int</c>.
    /// </summary>
    internal static class DocblockTypeSpellingHelper
    {
        public static string Spell(ICheckedType? type, string? namespacePrefix = null)
        {
            if (type is null || type.Kind == CheckedTypeKind.Unresolved)
            {
                return "";
            }

            return type switch
            {
                SpecialCheckedType special when special.IsMixed => "mixed",
                SpecialCheckedType special when special.IsVoid => "void",
                SpecialCheckedType special when special.IsNever => "never",
                NullableCheckedType nullable => FormatNullable(Spell(nullable.InnerType, namespacePrefix)),
                UnionCheckedType union => FormatUnion(union.Members.Select(m => Spell(m, namespacePrefix))),
                IntersectionCheckedType intersection => FormatIntersection(
                    intersection.Members.Select(m => Spell(m, namespacePrefix))),
                LiteralCheckedType literal => SpellLiteral(literal, namespacePrefix),
                TemplateStringCheckedType => "string",
                CallableCheckedType callable => SpellCallable(callable, namespacePrefix),
                StructCheckedType shape => SpellStructShape(shape, namespacePrefix),
                ObjectShapeCheckedType objectShape => SpellObjectShape(objectShape, namespacePrefix),
                StaticCheckedType => "static",
                GenericCheckedType generic => SpellGeneric(generic, namespacePrefix),
                SimpleCheckedType simple => SpellSimple(simple, namespacePrefix),
                CallableArityWildcardCheckedType => "...",
                HomogeneousVariadicCheckedType variadic => Spell(variadic.ElementType, namespacePrefix) + "...",
                ParameterPackCheckedType pack => string.Join(", ", pack.Members.Select(m => Spell(m, namespacePrefix))),
                _ => "",
            };
        }

        /// <summary>
        /// PHPStan/Psalm spelling of a bound declaration type. Uses the same format as
        /// <see cref="Spell(ICheckedType?, string?)"/> for the shapes the declaration AST carries.
        /// </summary>
        public static string SpellTypeExpression(
            ITypeExpression? type,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            var context = new SpellContext(typeAliasMap, scope, namespacePrefix);
            return SpellExpr(type, context);
        }

        /// <summary>
        /// Spells a class, interface, or trait reference, including type arguments
        /// (<c>\App\Box&lt;int&gt;</c>).
        /// </summary>
        public static string SpellClassReference(
            IBase2Ast? name,
            IReadOnlyDictionary<string, string>? typeAliasMap,
            IBaseScope? scope,
            string? namespacePrefix)
        {
            if (name is null)
            {
                return "";
            }

            var context = new SpellContext(typeAliasMap, scope, namespacePrefix);
            if (name is ITypeExpression typeExpression)
            {
                return SpellExpr(typeExpression, context);
            }

            return SpellNameNode(name, context, typeArguments: TypeArgumentsOf(name));
        }

        private static string SpellGeneric(GenericCheckedType generic, string? namespacePrefix)
        {
            if (TrySpellSymbolName(generic, namespacePrefix, out var symbolName))
            {
                return symbolName;
            }

            if (generic.BaseType is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol builtIn }
                && IsCallableName(builtIn.Name)
                && generic.TypeArguments.Count >= 1)
            {
                var parameters = generic.TypeArguments.Take(generic.TypeArguments.Count - 1)
                    .Select(arg => Spell(arg, namespacePrefix));
                var ret = Spell(generic.TypeArguments[^1], namespacePrefix);
                return FormatCallable(parameters, ret);
            }

            var baseSpelling = Spell(generic.BaseType, namespacePrefix);
            if (string.IsNullOrWhiteSpace(baseSpelling) || generic.TypeArguments.Count == 0)
            {
                return baseSpelling;
            }

            if (IsClassStringSpelling(baseSpelling))
            {
                return baseSpelling;
            }

            var args = generic.TypeArguments.Select(arg => Spell(arg, namespacePrefix)).ToList();
            return FormatGeneric(baseSpelling, args);
        }

        private static string SpellSimple(SimpleCheckedType simple, string? namespacePrefix)
        {
            if (TrySpellSymbolName(simple, namespacePrefix, out var symbolName))
            {
                return symbolName;
            }

            if (simple.ResolvedSymbol is GenericTypeParameterSymbol genericParam)
            {
                return genericParam.Name;
            }

            if (simple.ResolvedSymbol is ObjectDeclarationSymbol { IsStruct: true } structObj)
            {
                return SpellStructSymbol(structObj, typeArguments: null, new SpellContext(null, null, namespacePrefix));
            }

            if (simple.ResolvedSymbol is BuiltInTypeSymbol builtIn)
            {
                return SpellBuiltinToken(builtIn.Name);
            }

            if (simple.ResolvedSymbol is IBaseSymbol bound)
            {
                return SpellSymbolFqn(bound, namespacePrefix);
            }

            return simple.DisplayName;
        }

        private static string SpellLiteral(LiteralCheckedType literal, string? namespacePrefix)
        {
            return literal.Value switch
            {
                null => "null",
                bool flag => flag ? "true" : "false",
                string text => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'",
                int or long or short or byte => Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? "",
                float or double or decimal => Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? Spell(literal.UnderlyingType, namespacePrefix),
                _ => Spell(literal.UnderlyingType, namespacePrefix),
            };
        }

        private static string SpellCallable(CallableCheckedType callable, string? namespacePrefix)
        {
            if (callable.IsAnyArity)
            {
                return "callable";
            }

            var parameters = new List<string>(callable.ParameterTypes.Count);
            for (var i = 0; i < callable.ParameterTypes.Count; i++)
            {
                var spelled = Spell(callable.ParameterTypes[i], namespacePrefix);
                if (callable.LastParameterIsVariadic && i == callable.ParameterTypes.Count - 1)
                {
                    spelled += "...";
                }

                parameters.Add(spelled);
            }

            return FormatCallable(parameters, Spell(callable.ReturnType, namespacePrefix));
        }

        private static string SpellStructShape(StructCheckedType shape, string? namespacePrefix)
        {
            var fields = new List<string>();
            foreach (var pair in shape.Properties)
            {
                var key = pair.Value.StringKeyAlias
                    ?? (pair.Value.IntegerKeyAlias is int index
                        ? index.ToString(CultureInfo.InvariantCulture)
                        : pair.Key.TrimStart('$'));
                var spelled = Spell(pair.Value.Type, namespacePrefix);
                if (string.IsNullOrWhiteSpace(spelled))
                {
                    spelled = "mixed";
                }

                fields.Add(FormatShapeField(key, spelled, pair.Value.IsOptional || AllowsNull(spelled)));
            }

            return "array{" + string.Join(", ", fields) + "}";
        }

        private static string SpellObjectShape(ObjectShapeCheckedType shape, string? namespacePrefix)
        {
            if (shape.Members is null || shape.Members.Properties.Count == 0)
            {
                var alias = shape.DeclaringAlias;
                if (alias is not null && !string.IsNullOrWhiteSpace(alias.FullyQualifiedName))
                {
                    var name = SpellSymbolFqn(alias, namespacePrefix);
                    return shape.TypeArguments.Count == 0
                        ? name
                        : FormatGeneric(name, shape.TypeArguments.Select(arg => Spell(arg, namespacePrefix)));
                }

                return "object";
            }

            var fields = new List<string>();
            foreach (var pair in shape.Members.Properties)
            {
                var spelled = Spell(pair.Value.Type, namespacePrefix);
                if (string.IsNullOrWhiteSpace(spelled))
                {
                    spelled = "mixed";
                }

                fields.Add(FormatShapeField(pair.Value.Name.TrimStart('$'), spelled, optional: false));
            }

            return "object{" + string.Join(", ", fields) + "}";
        }

        private static bool TrySpellSymbolName(ICheckedType type, string? namespacePrefix, out string spelling)
        {
            spelling = "";
            if (!SymbolNameTypeHelper.TryGetUtilitySymbol(type, out var utility))
            {
                return false;
            }

            if (utility.Behavior == UtilityBehavior.TyhpInternal)
            {
                var args = SymbolNameTypeHelper.GetTypeArguments(type);
                spelling = args.Count > 0 ? Spell(args[0], namespacePrefix) : "mixed";
                if (string.IsNullOrWhiteSpace(spelling))
                {
                    spelling = "mixed";
                }

                return true;
            }

            if (IsClassNameBrand(utility.Behavior))
            {
                var args = SymbolNameTypeHelper.GetTypeArguments(type);
                var arg = args.Count > 0 ? Spell(args[0], namespacePrefix) : "";
                spelling = string.IsNullOrWhiteSpace(arg) || arg == "object" || arg == "mixed"
                    ? "class-string"
                    : "class-string<" + arg + ">";
                return true;
            }

            if (utility.Behavior == UtilityBehavior.FunctionName)
            {
                spelling = "callable-string";
                return true;
            }

            if (SymbolNameTypeHelper.IsSymbolNameBehavior(utility.Behavior))
            {
                spelling = "string";
                return true;
            }

            return false;
        }

        private sealed class SpellContext
        {
            public SpellContext(
                IReadOnlyDictionary<string, string>? typeAliasMap,
                IBaseScope? scope,
                string? namespacePrefix)
            {
                this.TypeAliasMap = typeAliasMap;
                this.Scope = scope;
                this.NamespacePrefix = namespacePrefix;
            }

            public IReadOnlyDictionary<string, string>? TypeAliasMap { get; }

            public IBaseScope? Scope { get; }

            public string? NamespacePrefix { get; }

            public Dictionary<GenericTypeParameterSymbol, string>? Substitutions { get; set; }

            public HashSet<IBaseSymbol> Resolving { get; } = new(ReferenceEqualityComparer.Instance);
        }

        private static string SpellExpr(ITypeExpression? type, SpellContext context)
        {
            if (type is null)
            {
                return "";
            }

            switch (type)
            {
                case PhpTypeExpressionAst composite:
                    return SpellComposite(composite, context);
                case PhpNamedTypeAst named:
                    return SpellNamed(named, context);
                case PhpBuiltinTypeAst builtin:
                    return SpellBuiltinNode(builtin, context);
                case TyhpCallableShapeAst callable:
                    return SpellCallableShape(callable, context);
                case TyhpStructShapeAst shape:
                    return SpellStructShapeAst(shape, context);
                case TyhpObjectShapeAst:
                    return "object";
                case TyhpReturnTypeGuardAst:
                    return "bool";
                case TyhpTemplateStringTypeAst:
                    return "string";
                default:
                    return "";
            }
        }

        private static string SpellComposite(PhpTypeExpressionAst composite, SpellContext context)
        {
            if (composite.IsStatic)
            {
                return "static";
            }

            var members = composite.Types?.GetAllNotNull().Select(member => SpellExpr(member, context)).Where(part => part.Length > 0).ToList()
                ?? [];
            if (members.Count == 0)
            {
                return "";
            }

            string spelled = composite.TypeKind switch
            {
                PhpTypeKind.Union => FormatUnion(members),
                PhpTypeKind.Intersection => FormatIntersection(members),
                _ => members.Count == 1 ? members[0] : FormatUnion(members),
            };

            if (composite.IsNullable)
            {
                spelled = FormatNullable(spelled);
            }

            return spelled;
        }

        private static string SpellNamed(PhpNamedTypeAst named, SpellContext context)
        {
            var arguments = TypeArgumentsOf(named);
            if (named.Name is PhpNameAst relativeName
                && IsRelativeKeyword(relativeName.ValueString ?? relativeName.Identifier ?? "")
                && named.BoundSymbol is not GenericTypeParameterSymbol)
            {
                // `self` / `parent` / `static` stay relative. Spelling the bound class instead
                // would widen `static` and break for subclasses.
                return SpellNameNode(relativeName, context, arguments);
            }

            if (named.BoundSymbol is IBaseSymbol bound)
            {
                return SpellBound(bound, arguments, context);
            }

            if (named.Name is IBase2Ast nameNode)
            {
                var fromName = SpellNameNode(nameNode, context, arguments);
                if (fromName.Length > 0)
                {
                    return fromName;
                }
            }

            return "";
        }

        private static string SpellBuiltinNode(PhpBuiltinTypeAst builtin, SpellContext context)
        {
            var name = builtin.Identifier ?? "";
            if (IsRelativeKeyword(name))
            {
                return name.ToLowerInvariant();
            }

            if (builtin.BoundSymbol is BuiltInUtilityTypeSymbol utility)
            {
                return SpellUtility(utility, TypeArgumentsOf(builtin), context);
            }

            // Struct (and other) names are erased to a builtin before emit. The erased node
            // keeps the original symbol so the docblock can still spell the shape.
            if (builtin.BoundSymbol is IBaseSymbol bound && bound is not BuiltInTypeSymbol)
            {
                return SpellBound(bound, TypeArgumentsOf(builtin), context);
            }

            var arguments = TypeArgumentsOf(builtin);
            if (arguments.Count > 0 && (IsArrayLikeName(name) || IsCallableName(name)))
            {
                return SpellBuiltinGeneric(name, arguments, context);
            }

            return SpellBuiltinToken(name);
        }

        private static string SpellNameNode(
            IBase2Ast nameNode,
            SpellContext context,
            IReadOnlyList<ITypeExpression> typeArguments)
        {
            if (nameNode is PhpNameAst name)
            {
                var text = name.ValueString ?? name.Identifier ?? "";
                if (IsRelativeKeyword(text) && name.BoundSymbol is not GenericTypeParameterSymbol)
                {
                    var relative = text.ToLowerInvariant();
                    return typeArguments.Count == 0
                        ? relative
                        : FormatGeneric(relative, typeArguments.Select(arg => SpellExpr(arg, context)));
                }
            }

            var symbol = nameNode.BoundSymbol ?? (nameNode as PhpNameAst)?.BoundSymbol;
            if (symbol is null && nameNode is PhpNamedTypeAst named)
            {
                symbol = named.BoundSymbol ?? named.Name?.BoundSymbol;
            }

            if (symbol is null)
            {
                if (nameNode is PhpNameAst unbound)
                {
                    var written = unbound.ValueString ?? unbound.Identifier ?? "";
                    if (written.Length == 0)
                    {
                        return "";
                    }

                    var spelled = SpellBuiltinToken(written);
                    return typeArguments.Count == 0
                        ? spelled
                        : FormatGeneric(spelled, typeArguments.Select(arg => SpellExpr(arg, context)));
                }

                return "";
            }

            return SpellBound(symbol, typeArguments, context);
        }

        private static string SpellBound(
            IBaseSymbol symbol,
            IReadOnlyList<ITypeExpression> typeArguments,
            SpellContext context)
        {
            if (symbol is GenericTypeParameterSymbol genericParam)
            {
                if (context.Substitutions is not null
                    && context.Substitutions.TryGetValue(genericParam, out var substituted))
                {
                    return substituted;
                }

                return genericParam.Name;
            }

            if (symbol is BuiltInUtilityTypeSymbol utility)
            {
                return SpellUtility(utility, typeArguments, context);
            }

            if (symbol is TypeAliasSymbol alias && alias.AliasedType is not null)
            {
                if (!context.Resolving.Add(alias))
                {
                    return "mixed";
                }

                try
                {
                    return SpellExpr(alias.AliasedType, context);
                }
                finally
                {
                    context.Resolving.Remove(alias);
                }
            }

            if (symbol is ObjectTypeAliasSymbol objectAlias && objectAlias.AliasedType is not null)
            {
                if (!context.Resolving.Add(objectAlias))
                {
                    return "mixed";
                }

                try
                {
                    return SpellExpr(objectAlias.AliasedType, context);
                }
                finally
                {
                    context.Resolving.Remove(objectAlias);
                }
            }

            if (symbol is ObjectDeclarationSymbol { IsStruct: true } structObj)
            {
                return SpellStructSymbol(structObj, typeArguments, context);
            }

            if (symbol is BuiltInTypeSymbol builtIn)
            {
                return typeArguments.Count == 0
                    ? SpellBuiltinToken(builtIn.Name)
                    : SpellBuiltinGeneric(builtIn.Name, typeArguments, context);
            }

            var fqn = SpellSymbolFqn(symbol, context.NamespacePrefix);
            if (typeArguments.Count == 0)
            {
                return fqn;
            }

            return FormatGeneric(fqn, typeArguments.Select(arg => SpellExpr(arg, context)));
        }

        private static string SpellUtility(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ITypeExpression> typeArguments,
            SpellContext context)
        {
            if (utility.Behavior == UtilityBehavior.TyhpInternal)
            {
                return typeArguments.Count > 0 ? SpellExpr(typeArguments[0], context) : "mixed";
            }

            if (IsClassNameBrand(utility.Behavior))
            {
                if (typeArguments.Count == 0)
                {
                    return "class-string";
                }

                var arg = SpellExpr(typeArguments[0], context);
                return string.IsNullOrWhiteSpace(arg) || arg == "object" || arg == "mixed"
                    ? "class-string"
                    : "class-string<" + arg + ">";
            }

            if (utility.Behavior == UtilityBehavior.FunctionName)
            {
                return "callable-string";
            }

            if (SymbolNameTypeHelper.IsSymbolNameBehavior(utility.Behavior)
                || TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior(utility.Behavior))
            {
                if (utility.Behavior == UtilityBehavior.AsType && typeArguments.Count > 0)
                {
                    return SpellExpr(typeArguments[0], context);
                }

                return "string";
            }

            var name = utility.Name.Length > 0 ? utility.Name : utility.FullyQualifiedName;
            if (typeArguments.Count == 0)
            {
                return name;
            }

            return FormatGeneric(name, typeArguments.Select(arg => SpellExpr(arg, context)));
        }

        private static string SpellBuiltinGeneric(
            string name,
            IReadOnlyList<ITypeExpression> typeArguments,
            SpellContext context)
        {
            var spelledArgs = typeArguments.Select(arg => SpellExpr(arg, context)).Where(part => part.Length > 0).ToList();
            if (IsCallableName(name) && spelledArgs.Count >= 1)
            {
                return FormatCallable(spelledArgs.Take(spelledArgs.Count - 1), spelledArgs[^1]);
            }

            var baseName = SpellBuiltinToken(name);
            return spelledArgs.Count == 0 ? baseName : FormatGeneric(baseName, spelledArgs);
        }

        private static string SpellStructSymbol(
            ObjectDeclarationSymbol structObj,
            IReadOnlyList<ITypeExpression>? typeArguments,
            SpellContext context)
        {
            if (!context.Resolving.Add(structObj))
            {
                return "array";
            }

            var saved = context.Substitutions;
            try
            {
                if (typeArguments is { Count: > 0 } && structObj.GenericParameters.Count > 0)
                {
                    var substitutions = new Dictionary<GenericTypeParameterSymbol, string>();
                    var count = Math.Min(typeArguments.Count, structObj.GenericParameters.Count);
                    for (var i = 0; i < count; i++)
                    {
                        substitutions[structObj.GenericParameters[i]] = SpellExpr(typeArguments[i], context);
                    }

                    context.Substitutions = substitutions;
                }

                var fields = new List<string>();
                foreach (var member in structObj.Members.Values)
                {
                    if (member is not ObjectPropertySymbol property)
                    {
                        continue;
                    }

                    var key = StructShapeKey(property);
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    var spelled = property.DeclaredType is null
                        ? "mixed"
                        : SpellExpr(property.DeclaredType, context);
                    if (string.IsNullOrWhiteSpace(spelled))
                    {
                        spelled = "mixed";
                    }

                    fields.Add(FormatShapeField(
                        key,
                        spelled,
                        property.DefaultValue is not null || AllowsNull(spelled)));
                }

                return "array{" + string.Join(", ", fields) + "}";
            }
            finally
            {
                context.Substitutions = saved;
                context.Resolving.Remove(structObj);
            }
        }

        private static string SpellStructShapeAst(TyhpStructShapeAst shape, SpellContext context)
        {
            var fields = new List<string>();
            foreach (var property in shape.PropertyList?.GetAllNotNull() ?? [])
            {
                var key = StructShapeKey(property);
                if (key.Length == 0)
                {
                    continue;
                }

                var spelled = property.TypeExpression is null ? "mixed" : SpellExpr(property.TypeExpression, context);
                if (string.IsNullOrWhiteSpace(spelled))
                {
                    spelled = "mixed";
                }

                fields.Add(FormatShapeField(
                    key,
                    spelled,
                    property.Property?.DefaultValue is not null || AllowsNull(spelled)));
            }

            return "array{" + string.Join(", ", fields) + "}";
        }

        private static string SpellCallableShape(TyhpCallableShapeAst callable, SpellContext context)
        {
            if (callable.IsUnknownArity)
            {
                return "callable";
            }

            var parameters = new List<string>();
            foreach (var parameter in callable.Parameters?.GetAllNotNull() ?? [])
            {
                var spelled = parameter.TypeExpression is null
                    ? "mixed"
                    : SpellExpr(parameter.TypeExpression, context);
                if (parameter.IsVariadic)
                {
                    spelled += "...";
                }

                parameters.Add(spelled);
            }

            var ret = callable.ReturnType is null ? "mixed" : SpellExpr(callable.ReturnType, context);
            return FormatCallable(parameters, ret);
        }

        private static string StructShapeKey(ObjectPropertySymbol property)
        {
            if (property.DeclaringAstNode is TyhpStructPropertyAst structProp)
            {
                return StructShapeKey(structProp);
            }

            return (property.Name ?? "").TrimStart('$');
        }

        private static string StructShapeKey(TyhpStructPropertyAst property)
        {
            if (property.IsNumericAlias
                && property.ValueInt64 is long key
                && key is >= int.MinValue and <= int.MaxValue)
            {
                return key.ToString(CultureInfo.InvariantCulture);
            }

            var alias = StructTypeHelper.UnquoteStringAlias(property.AliasOf);
            if (!string.IsNullOrEmpty(alias))
            {
                return alias;
            }

            var name = property.Property?.Identifier ?? property.Identifier ?? "";
            return name.TrimStart('$');
        }

        private static string SpellSymbolFqn(IBaseSymbol symbol, string? namespacePrefix)
        {
            if (symbol is GenericTypeParameterSymbol genericParam)
            {
                return genericParam.Name;
            }

            if (symbol is BuiltInTypeSymbol builtIn)
            {
                return SpellBuiltinToken(builtIn.Name);
            }

            if (!string.IsNullOrWhiteSpace(symbol.FullyQualifiedName))
            {
                return EmittedFqnHelper.Format(symbol.FullyQualifiedName, namespacePrefix, symbol);
            }

            return symbol.Name ?? "";
        }

        private static string SpellBuiltinToken(string name)
        {
            if (string.Equals(name, "decimal", StringComparison.OrdinalIgnoreCase))
            {
                return @"\Tyhp\Decimal";
            }

            if (string.Equals(name, "struct", StringComparison.OrdinalIgnoreCase))
            {
                return "array";
            }

            if (string.Equals(name, "resource", StringComparison.OrdinalIgnoreCase))
            {
                return "resource";
            }

            if (StaticValueTypeHelper.TryGetUnderlyingBuiltinName(name, out var underlying))
            {
                return underlying;
            }

            return name;
        }

        internal static string FormatNullable(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner) || inner == "mixed" || inner == "null")
            {
                return string.IsNullOrWhiteSpace(inner) ? "null" : inner;
            }

            if (inner.StartsWith('?'))
            {
                return inner;
            }

            if (IsCallableSpelling(inner))
            {
                // `callable(int): string|null` would read as a callable returning `string|null`.
                return "(" + inner + ")|null";
            }

            if (SplitTopLevel(inner, '|').Count > 1 || SplitTopLevel(inner, '&').Count > 1)
            {
                return inner + "|null";
            }

            return "?" + inner;
        }

        private static string FormatUnion(IEnumerable<string> parts)
        {
            var list = new List<string>();
            var hasNull = false;
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (part == "mixed")
                {
                    return "mixed";
                }

                // Split only on a top-level `|`. A `|` inside `array<int|string>` or a shape
                // belongs to that member.
                foreach (var rawAtom in SplitTopLevel(part, '|'))
                {
                    var atom = rawAtom;
                    if (atom.StartsWith('?'))
                    {
                        atom = atom[1..];
                        hasNull = true;
                    }

                    if (atom == "null")
                    {
                        hasNull = true;
                        continue;
                    }

                    if (atom.Length > 0 && !list.Contains(atom, StringComparer.Ordinal))
                    {
                        list.Add(atom);
                    }
                }
            }

            if (list.Count == 0)
            {
                return hasNull ? "null" : "";
            }

            if (hasNull)
            {
                if (list.Count == 1)
                {
                    return FormatNullable(list[0]);
                }

                list.Add("null");
            }

            // A callable member must be parenthesized so its return type does not absorb `|`.
            return string.Join("|", list.Select(atom => IsCallableSpelling(atom) ? "(" + atom + ")" : atom));
        }

        private static bool IsCallableSpelling(string spelling) =>
            spelling.StartsWith("callable(", StringComparison.Ordinal)
            || spelling.StartsWith("Closure(", StringComparison.Ordinal);

        /// <summary>
        /// Splits <paramref name="text"/> on <paramref name="separator"/> outside brackets and
        /// quoted literals.
        /// </summary>
        /// <summary>
        /// The members of a union spelling, with <c>?T</c> expanded to <c>T</c> and <c>null</c>,
        /// so <c>int|string</c>, <c>string|int</c>, and <c>?int</c> vs <c>int|null</c> compare equal.
        /// </summary>
        internal static HashSet<string> UnionMembers(string spelling)
        {
            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in SplitTopLevel(spelling.Replace(" ", "", StringComparison.Ordinal), '|'))
            {
                var atom = raw.TrimStart('\\');
                if (atom.StartsWith('?'))
                {
                    members.Add("null");
                    atom = atom[1..].TrimStart('\\');
                }

                if (atom.Length > 0)
                {
                    members.Add(atom);
                }
            }

            return members;
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            var depth = 0;
            var quote = '\0';
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (quote != '\0')
                {
                    if (ch == '\\')
                    {
                        i++;
                    }
                    else if (ch == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (ch is '\'' or '"')
                {
                    quote = ch;
                }
                else if (ch is '<' or '{' or '(' or '[')
                {
                    depth++;
                }
                else if (ch is '>' or '}' or ')' or ']')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (ch == separator && depth == 0)
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                }
            }

            parts.Add(text[start..]);
            return parts;
        }

        private static string FormatIntersection(IEnumerable<string> parts)
        {
            var list = parts.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0)
            {
                return "";
            }

            if (list.Contains("mixed", StringComparer.Ordinal))
            {
                return "mixed";
            }

            return list.Count == 1 ? list[0] : string.Join("&", list.Select(part => ParenthesizeFor('&', part)));
        }

        private static string ParenthesizeFor(char separator, string part)
        {
            if (separator == '&'
                && (SplitTopLevel(part, '|').Count > 1 || part.StartsWith('?') || IsCallableSpelling(part)))
            {
                return "(" + part + ")";
            }

            return part;
        }

        private static string FormatGeneric(string baseName, IEnumerable<string> typeArguments)
        {
            var args = typeArguments.Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();
            if (args.Count == 0 || string.IsNullOrWhiteSpace(baseName))
            {
                return baseName;
            }

            return baseName + "<" + string.Join(", ", args) + ">";
        }

        private static string FormatCallable(IEnumerable<string> parameters, string returnType)
        {
            var ret = string.IsNullOrWhiteSpace(returnType) ? "mixed" : returnType;
            if (SplitTopLevel(ret, '|').Count > 1 || SplitTopLevel(ret, '&').Count > 1 || IsCallableSpelling(ret))
            {
                // A callable's return type is a single atom: `callable(): int|string` is
                // `(callable(): int)|string`.
                ret = "(" + ret + ")";
            }

            return "callable(" + string.Join(", ", parameters) + "): " + ret;
        }

        /// <summary>
        /// A nullable struct field is optional, matching the runtime struct registration
        /// (<c>optional(...)</c> for a nullable or defaulted field).
        /// </summary>
        private static bool AllowsNull(string spelled) =>
            spelled.StartsWith('?') || UnionMembers(spelled).Contains("null");

        private static string FormatShapeField(string key, string type, bool optional)
        {
            var renderedKey = NeedsShapeQuotes(key)
                ? "'" + key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'"
                : key;
            return renderedKey + (optional ? "?: " : ": ") + type;
        }

        private static bool NeedsShapeQuotes(string key)
        {
            if (key.Length == 0)
            {
                return true;
            }

            if (char.IsDigit(key[0]))
            {
                return !key.All(char.IsDigit);
            }

            return !key.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
        }

        private static bool IsClassStringSpelling(string spelling) =>
            spelling == "class-string" || spelling.StartsWith("class-string<", StringComparison.Ordinal);

        /// <summary>
        /// Brands whose string is the name of a class, interface, or enum, so
        /// <c>class-string&lt;T&gt;</c> is true. Trait names, used-trait names, and struct names
        /// are not <c>class-string</c> (a trait or a struct cannot be instantiated or passed to
        /// <c>class-string</c> parameters), and <c>__UsedTraitName&lt;T&gt;</c> names a trait of
        /// <c>T</c>, not a subtype of <c>T</c>. Those spell as plain <c>string</c>.
        /// </summary>
        private static bool IsClassNameBrand(UtilityBehavior behavior) =>
            behavior is UtilityBehavior.ClassName
                or UtilityBehavior.InterfaceName
                or UtilityBehavior.EnumName;

        private static bool IsArrayLikeName(string name) =>
            name.Equals("array", StringComparison.OrdinalIgnoreCase)
            || name.Equals("iterable", StringComparison.OrdinalIgnoreCase);

        private static bool IsCallableName(string name) =>
            name.Equals("callable", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the declared type is, or contains, <c>self</c> / <c>parent</c> / <c>static</c>.
        /// The checker resolves those to the enclosing class, so a docblock must be spelled from
        /// the declaration to stay relative.
        /// </summary>
        internal static bool ContainsRelativeType(ITypeExpression? type)
        {
            switch (type)
            {
                case PhpTypeExpressionAst composite:
                    return composite.IsStatic
                        || (composite.Types?.GetAllNotNull().Any(member => ContainsRelativeType(member)) ?? false);
                case PhpNamedTypeAst named:
                    return named.Name is PhpNameAst name
                        && IsRelativeKeyword(name.ValueString ?? name.Identifier ?? "")
                        && named.BoundSymbol is not GenericTypeParameterSymbol;
                case PhpBuiltinTypeAst builtin:
                    return IsRelativeKeyword(builtin.Identifier ?? "");
                default:
                    return false;
            }
        }

        private static bool IsRelativeKeyword(string text) =>
            text.Equals("self", StringComparison.OrdinalIgnoreCase)
            || text.Equals("parent", StringComparison.OrdinalIgnoreCase)
            || text.Equals("static", StringComparison.OrdinalIgnoreCase);

        private static IReadOnlyList<ITypeExpression> TypeArgumentsOf(IBase2Ast? node)
        {
            if (node is null)
            {
                return [];
            }

            if (TryTypeArgumentList(node, "typeName", out var typeNameArgs))
            {
                return typeNameArgs;
            }

            if (TryTypeArgumentList(node, "identifier", out var identifierArgs))
            {
                return identifierArgs;
            }

            if (node is PhpNamedTypeAst named)
            {
                if (named.Name is TyhpGenericIdentifierAst genericOnNamed
                    && genericOnNamed.GenericArguments is PhpTypeExpressionListAst namedArgs)
                {
                    return namedArgs.GetAllNotNull().ToList();
                }

                var fromName = TypeArgumentsOf(named.Name);
                if (fromName.Count > 0)
                {
                    return fromName;
                }
            }

            if (node is TyhpGenericIdentifierAst generic
                && generic.GenericArguments is PhpTypeExpressionListAst genericArgs)
            {
                return genericArgs.GetAllNotNull().ToList();
            }

            return [];
        }

        private static bool TryTypeArgumentList(
            IBase2Ast node,
            string addonKey,
            out IReadOnlyList<ITypeExpression> arguments)
        {
            arguments = [];
            if (node.AstGrammarAddons.TryGetValue(addonKey, out var addon)
                && addon is PhpTypeExpressionListAst list)
            {
                arguments = list.GetAllNotNull().ToList();
                return arguments.Count > 0;
            }

            return false;
        }
    }
}
