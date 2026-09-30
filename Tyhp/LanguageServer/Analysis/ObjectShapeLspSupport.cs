namespace Tyhp.LanguageServer.Analysis
{
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Resolution;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using Tyhp.TyhpLang.Checker;
    using Tyhp.TyhpLang.Enum;

    /// <summary>
    /// LSP helpers for object-shape aliases: they are types, not classes. Hover/definition
    /// use the alias and its <c>object { … }</c> members; instance completion lists public
    /// instance members and never <c>__construct</c>.
    /// </summary>
    internal static class ObjectShapeLspSupport
    {
        public static bool IsObjectShapeTypeSymbol(IBaseSymbol? symbol) =>
            ObjectShapeSupport.AliasResolvesToObjectShape(symbol);

        public static (TyhpObjectShapeAst Shape, IBaseSymbol? Alias)? FromSymbol(
            IBaseSymbol? symbol,
            NameResolver? resolver = null,
            IBaseScope? fromScope = null)
        {
            if (symbol is VariableSymbol variable)
            {
                (TyhpObjectShapeAst, IBaseSymbol?)? fromDeclared = FromTypeExpression(
                    variable.DeclaredType,
                    resolver,
                    fromScope);
                if (fromDeclared is not null)
                {
                    return fromDeclared;
                }
            }

            TyhpObjectShapeAst? shape = TryGetShape(symbol);
            if (shape is null)
            {
                return null;
            }

            return (shape, symbol);
        }

        public static (TyhpObjectShapeAst Shape, IBaseSymbol? Alias)? FromTypeExpression(
            ITypeExpression? type,
            NameResolver? resolver = null,
            IBaseScope? fromScope = null)
        {
            (TyhpObjectShapeAst Shape, IBaseSymbol? Alias)? direct = TryGetShapeWithAliasFromTypeExpression(
                type,
                []);
            if (direct is not null)
            {
                return direct;
            }

            if (type is null || resolver is null || fromScope is null)
            {
                return null;
            }

            IBaseSymbol? resolved = resolver.ResolveType(type, fromScope);
            TyhpObjectShapeAst? shape = TryGetShape(resolved);
            if (shape is not null)
            {
                return (shape, resolved);
            }

            string? written = ObjectShapeSupport.GetWrittenTypeName(type);
            if (string.IsNullOrEmpty(written))
            {
                return null;
            }

            resolved = resolver.ResolveSymbol(written.TrimStart('\\'), fromScope, NameResolver.IsTypePositionSymbol);
            shape = TryGetShape(resolved);
            return shape is null ? null : (shape, resolved);
        }

        public static TyhpObjectShapeAst? TryGetShape(IBaseSymbol? symbol) =>
            TryGetShapeFromTypeExpression(ObjectShapeSupport.GetAliasedType(symbol));

        public static TyhpObjectShapeAst? TryGetShape(ICheckedType? type)
        {
            if (type is null)
            {
                return null;
            }

            return TypeComparer.TryGetObjectShapeFromType(type, out ObjectShapeCheckedType shape)
                ? shape.Shape
                : null;
        }

        public static TyhpObjectShapeAst? TryGetShapeFromTypeExpression(ITypeExpression? type) =>
            TryGetShapeWithAliasFromTypeExpression(type, [])?.Shape;

        public static IEnumerable<BaseSymbol> EnumerateDeclaredMembers(
            TyhpObjectShapeAst shape,
            string sourceFile)
        {
            ArgumentNullException.ThrowIfNull(shape);
            return EnumerateMembers(shape, sourceFile, instanceOnly: false, includeConstruct: true);
        }

        public static IEnumerable<BaseSymbol> EnumerateInstanceMembers(
            TyhpObjectShapeAst shape,
            string sourceFile)
        {
            ArgumentNullException.ThrowIfNull(shape);
            return EnumerateMembers(shape, sourceFile, instanceOnly: true, includeConstruct: false);
        }

        public static BaseSymbol? FindInstanceMember(
            TyhpObjectShapeAst shape,
            string memberName,
            string sourceFile)
        {
            ArgumentNullException.ThrowIfNull(shape);
            string normalized = ObjectShapeMemberBuilder.NormalizePropertyName(memberName);
            if (string.IsNullOrEmpty(normalized)
                || ObjectShapeMemberBuilder.IsConstructabilityName(normalized))
            {
                return null;
            }

            foreach (BaseSymbol member in EnumerateInstanceMembers(shape, sourceFile))
            {
                string name = member is ObjectPropertySymbol
                    ? ObjectShapeMemberBuilder.NormalizePropertyName(member.Name)
                    : member.Name;
                if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return member;
                }
            }

            return null;
        }

        public static IEnumerable<ObjectDeclarationSymbol> EnumerateNominalConjuncts(
            IBaseSymbol? alias,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            ITypeExpression? aliased = ObjectShapeSupport.GetAliasedType(alias);
            if (aliased is not PhpTypeExpressionAst { TypeKind: PhpTypeKind.Intersection, Types: { } items }
                || resolver is null
                || fromScope is null)
            {
                yield break;
            }

            foreach (ITypeExpression item in items.GetAllNotNull())
            {
                if (item is TyhpObjectShapeAst)
                {
                    continue;
                }

                if (resolver.ResolveType(item, fromScope) is ObjectDeclarationSymbol obj)
                {
                    yield return obj;
                }
            }
        }

        /// <summary>
        /// Finds the object shape reachable from <paramref name="type"/> along with the alias
        /// symbol written at that position (the immediate bound name, even when its RHS is a
        /// rename or another shape alias several hops away).
        /// </summary>
        private static (TyhpObjectShapeAst Shape, IBaseSymbol? Alias)? TryGetShapeWithAliasFromTypeExpression(
            ITypeExpression? type,
            HashSet<IBaseSymbol> seen)
        {
            if (type is null)
            {
                return null;
            }

            TyhpObjectShapeAst? found = TyhpObjectShapeAst.Find(type);
            if (found is not null)
            {
                IBaseSymbol? directAlias = type.BoundSymbol
                    ?? (type as PhpNamedTypeAst)?.Name?.BoundSymbol;
                return (found, directAlias);
            }

            // A single named type (including a generic instantiation like `Box<int>`) is parsed
            // as a `PhpTypeExpressionAst` wrapper around the `PhpNamedTypeAst`; the bound symbol
            // lives on that inner node, not on the wrapper itself. Recurse into every wrapped
            // item so those cases resolve the same way plain union/intersection members do.
            if (type is PhpTypeExpressionAst { Types: { } items })
            {
                foreach (ITypeExpression item in items.GetAllNotNull())
                {
                    (TyhpObjectShapeAst, IBaseSymbol?)? fromItem = TryGetShapeWithAliasFromTypeExpression(item, seen);
                    if (fromItem is not null)
                    {
                        return fromItem;
                    }
                }

                return null;
            }

            IBaseSymbol? bound = type.BoundSymbol
                ?? (type as PhpNamedTypeAst)?.Name?.BoundSymbol;
            if (bound is null || !seen.Add(bound))
            {
                return null;
            }

            (TyhpObjectShapeAst, IBaseSymbol?)? nested = TryGetShapeWithAliasFromTypeExpression(
                ObjectShapeSupport.GetAliasedType(bound),
                seen);
            return nested is null ? null : (nested.Value.Item1, bound);
        }

        private static IEnumerable<BaseSymbol> EnumerateMembers(
            TyhpObjectShapeAst shape,
            string sourceFile,
            bool instanceOnly,
            bool includeConstruct)
        {
            IEnumerable<IClassMember> members = shape.Members?.GetAllNotNull() ?? [];
            foreach (IClassMember member in members)
            {
                switch (member)
                {
                    case PhpMethodDeclAst method:
                        if (ShouldSkipMethod(method, instanceOnly, includeConstruct))
                        {
                            break;
                        }

                        yield return CreateMethodSymbol(method, sourceFile);
                        break;
                    case PhpPropertyDeclAst property:
                        foreach (ObjectPropertySymbol propSymbol in CreatePropertySymbols(property, sourceFile))
                        {
                            if (instanceOnly
                                && (propSymbol.SymbolType == SymbolType.StaticObjectProperty
                                    || propSymbol.Visibility.HasFlag(MemberModifier.Static)))
                            {
                                continue;
                            }

                            yield return propSymbol;
                        }

                        break;
                    case PhpConstDeclListAst constList when !instanceOnly:
                        foreach (PhpConstDeclAst constant in constList.GetAllNotNull())
                        {
                            yield return CreateConstantSymbol(constant, sourceFile);
                        }

                        break;
                }
            }
        }

        private static bool ShouldSkipMethod(PhpMethodDeclAst method, bool instanceOnly, bool includeConstruct)
        {
            string name = method.Identifier ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            if (!includeConstruct && ObjectShapeMemberBuilder.IsConstructabilityName(name))
            {
                return true;
            }

            return instanceOnly && HasModifier(method.Modifiers, PhpModifier.Static);
        }

        private static ObjectMethodSymbol CreateMethodSymbol(PhpMethodDeclAst method, string sourceFile)
        {
            MemberModifier visibility = ConvertModifiers(method.Modifiers);
            if (visibility == MemberModifier.None)
            {
                visibility = MemberModifier.Public;
            }

            bool isStatic = visibility.HasFlag(MemberModifier.Static);
            bool isConstruct = ObjectShapeMemberBuilder.IsConstructabilityName(method.Identifier ?? string.Empty);
            var symbol = new ObjectMethodSymbol(
                method.Identifier ?? string.Empty,
                method,
                sourceFile,
                visibility,
                isConstruct
                    ? SymbolType.ObjectConstructor
                    : isStatic
                        ? SymbolType.StaticObjectMethod
                        : SymbolType.InstanceObjectMethod)
            {
                ReturnType = method.ReturnType,
                IsStatic = isStatic,
                DocComment = method.DocComment,
            };

            foreach (PhpParameterAst parameter in method.Parameters?.GetAllNotNull() ?? [])
            {
                symbol.Parameters.Add(new ParameterInfo(
                    parameter.ValueString ?? parameter.Name,
                    parameter.Type,
                    parameter.DefaultValue,
                    parameter.IsVariadic,
                    parameter.IsRef,
                    MemberModifier.None));
            }

            return symbol;
        }

        private static IEnumerable<ObjectPropertySymbol> CreatePropertySymbols(
            PhpPropertyDeclAst property,
            string sourceFile)
        {
            MemberModifier visibility = ConvertModifiers(property.Modifiers);
            if (visibility == MemberModifier.None)
            {
                visibility = MemberModifier.Public;
            }

            bool isStatic = visibility.HasFlag(MemberModifier.Static);
            foreach (PhpPropertyAst item in property.Properties?.GetAllNotNull() ?? [])
            {
                string name = item.Identifier ?? string.Empty;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                yield return new ObjectPropertySymbol(
                    name,
                    sourceFile,
                    item,
                    isStatic ? SymbolType.StaticObjectProperty : SymbolType.InstanceObjectProperty,
                    visibility)
                {
                    DeclaredType = property.Type,
                    DefaultValue = item.DefaultValue,
                    DocComment = item.DocComment ?? property.DocComment,
                };
            }
        }

        private static ObjectConstantSymbol CreateConstantSymbol(PhpConstDeclAst constant, string sourceFile)
        {
            MemberModifier visibility = ConvertModifiers(constant.Modifiers);
            if (visibility == MemberModifier.None)
            {
                visibility = MemberModifier.Public;
            }

            return new ObjectConstantSymbol(
                constant.Identifier ?? string.Empty,
                sourceFile,
                constant,
                visibility)
            {
                DeclaredType = constant.Type,
                ValueExpression = constant.Value,
                DocComment = constant.DocComment,
            };
        }

        private static MemberModifier ConvertModifiers(PhpModifierListAst? modifiers)
        {
            if (modifiers is null)
            {
                return MemberModifier.None;
            }

            var result = MemberModifier.None;
            foreach (PhpModifier mod in modifiers.Modifiers)
            {
                result |= mod switch
                {
                    PhpModifier.Public => MemberModifier.Public,
                    PhpModifier.Protected => MemberModifier.Protected,
                    PhpModifier.Private => MemberModifier.Private,
                    PhpModifier.Static => MemberModifier.Static,
                    PhpModifier.Abstract => MemberModifier.Abstract,
                    PhpModifier.Final => MemberModifier.Final,
                    PhpModifier.Readonly => MemberModifier.Readonly,
                    PhpModifier.Var => MemberModifier.Var,
                    PhpModifier.Internal => MemberModifier.Internal,
                    _ => MemberModifier.None,
                };
            }

            return result;
        }

        private static bool HasModifier(PhpModifierListAst? modifiers, PhpModifier wanted)
        {
            if (modifiers is null)
            {
                return false;
            }

            foreach (PhpModifier modifier in modifiers.Modifiers)
            {
                if (modifier == wanted)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
