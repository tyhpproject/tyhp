using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        /// <summary>
        /// Logical (source, target-shape) pairs currently being width-checked. Recursive member
        /// types allocate new <see cref="ICheckedType"/> wrappers, so the assignability
        /// <c>visited</c> set (reference equality) does not see them as the same pair.
        /// </summary>
        [ThreadStatic]
        private static HashSet<(string Source, string Target)>? _objectShapeWidthVisits;

        /// <summary>
        /// Width subtyping: every public instance member of <paramref name="targetShape"/>
        /// (except <c>__construct</c>) must exist on <paramref name="source"/> with a compatible
        /// signature. Extra source members are allowed. Magic methods match only themselves.
        /// </summary>
        private static bool SourceSatisfiesObjectShape(
            ICheckedType source,
            ObjectShapeCheckedType targetShape,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var sourceKey = ObjectShapeWidthSourceKey(source);
            var targetKey = ObjectShapeWidthTargetKey(targetShape);
            if (sourceKey is not null)
            {
                _objectShapeWidthVisits ??= new HashSet<(string Source, string Target)>(
                    StringPairComparer.Instance);
                if (!_objectShapeWidthVisits.Add((sourceKey, targetKey)))
                {
                    return true;
                }
            }

            try
            {
                if (TryAsObjectShape(source) is { } sourceShape)
                {
                    return ObjectShapeSatisfiesObjectShape(
                        sourceShape, targetShape, symbolTree, globalScope, visited);
                }

                if (TryGetObjectDeclaration(source) is { } objectDecl)
                {
                    return ObjectSatisfiesObjectShape(
                        source, objectDecl, targetShape, symbolTree, globalScope, visited);
                }

                return false;
            }
            finally
            {
                if (sourceKey is not null)
                {
                    _objectShapeWidthVisits?.Remove((sourceKey, targetKey));
                }
            }
        }

        private static string? ObjectShapeWidthSourceKey(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TryAsObjectShape(type) is { } shape)
            {
                return ObjectShapeWidthTargetKey(shape);
            }

            if (type is GenericCheckedType generic
                && TryGetObjectDeclaration(generic) is { } genericDecl)
            {
                return ObjectShapeWidthDeclarationKey(genericDecl, generic.TypeArguments);
            }

            if (TryGetObjectDeclaration(type) is { } objectDecl)
            {
                return ObjectShapeWidthDeclarationKey(objectDecl, []);
            }

            return null;
        }

        private static string ObjectShapeWidthTargetKey(ObjectShapeCheckedType shape)
        {
            if (shape.DeclaringAlias is { } alias)
            {
                return ObjectShapeWidthDeclarationKey(alias, shape.TypeArguments);
            }

            return ObjectShapeWidthDeclarationKey(shape.Shape, shape.TypeArguments);
        }

        private static string ObjectShapeWidthDeclarationKey(
            object identity,
            IReadOnlyList<ICheckedType> typeArguments)
        {
            var name = identity switch
            {
                IBaseSymbol symbol => NormalizeFqn(symbol),
                _ => identity.GetType().Name + "@" + identity.GetHashCode(),
            };
            if (typeArguments.Count == 0)
            {
                return name;
            }

            return $"{name}<{string.Join(",", typeArguments.Select(arg => arg.DisplayName))}>";
        }

        private sealed class StringPairComparer : IEqualityComparer<(string Source, string Target)>
        {
            public static readonly StringPairComparer Instance = new();

            public bool Equals((string Source, string Target) x, (string Source, string Target) y) =>
                string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string Source, string Target) obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target));
        }

        private static bool ObjectShapeSatisfiesObjectShape(
            ObjectShapeCheckedType source,
            ObjectShapeCheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (AreObjectShapeIdentitiesEqual(source, target, visited))
            {
                return true;
            }

            var targetMembers = target.Members;
            var sourceMembers = source.Members;
            if (targetMembers is null || sourceMembers is null)
            {
                return false;
            }

            foreach (var (name, targetMethod) in targetMembers.Methods)
            {
                if (ObjectShapeMemberBuilder.IsConstructabilityName(name))
                {
                    continue;
                }

                if (!sourceMembers.Methods.TryGetValue(name, out var sourceMethod)
                    || sourceMethod.IsStatic != targetMethod.IsStatic
                    || !IsAssignableToCore(
                        sourceMethod.CallableType,
                        targetMethod.CallableType,
                        symbolTree,
                        globalScope,
                        visited))
                {
                    return false;
                }
            }

            foreach (var (name, targetProperty) in targetMembers.Properties)
            {
                if (!sourceMembers.Properties.TryGetValue(name, out var sourceProperty)
                    || !PropertySatisfiesShape(sourceProperty, targetProperty, symbolTree, globalScope, visited))
                {
                    return false;
                }
            }

            foreach (var (name, targetConstant) in targetMembers.Constants)
            {
                if (!sourceMembers.Constants.TryGetValue(name, out var sourceConstant)
                    || !IsAssignableToCore(
                        sourceConstant.Type,
                        targetConstant.Type,
                        symbolTree,
                        globalScope,
                        visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ObjectSatisfiesObjectShape(
            ICheckedType source,
            ObjectDeclarationSymbol objectDecl,
            ObjectShapeCheckedType targetShape,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var targetMembers = targetShape.Members;
            if (targetMembers is null)
            {
                return false;
            }

            foreach (var (name, targetMethod) in targetMembers.Methods)
            {
                if (ObjectShapeMemberBuilder.IsConstructabilityName(name))
                {
                    continue;
                }

                if (!TryFindExactPublicMethod(
                        objectDecl,
                        name,
                        targetMethod.IsStatic,
                        symbolTree,
                        globalScope,
                        out var method)
                    || !MethodSatisfiesShape(
                        source, method, targetMethod.CallableType, symbolTree, globalScope, visited))
                {
                    return false;
                }
            }

            foreach (var (name, targetProperty) in targetMembers.Properties)
            {
                if (!TryFindExactPublicProperty(
                        objectDecl, name, symbolTree, globalScope, out var property)
                    || !ClassPropertySatisfiesShape(
                        source, property, targetProperty, symbolTree, globalScope, visited))
                {
                    return false;
                }
            }

            foreach (var (name, targetConstant) in targetMembers.Constants)
            {
                if (!TryFindExactPublicConstant(
                        objectDecl, name, symbolTree, globalScope, out var constant)
                    || !ConstantSatisfiesShape(
                        source, constant, targetConstant.Type, symbolTree, globalScope, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MethodSatisfiesShape(
            ICheckedType source,
            ObjectMethodSymbol method,
            ICheckedType targetCallable,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            foreach (var candidate in EnumerateMethodOverloads(method))
            {
                var callable = BuildMethodCallableType(
                    source, candidate, symbolTree, globalScope);
                if (IsAssignableToCore(callable, targetCallable, symbolTree, globalScope, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<ObjectMethodSymbol> EnumerateMethodOverloads(ObjectMethodSymbol method)
        {
            yield return method;
            foreach (var overload in method.Overloads)
            {
                yield return overload;
            }
        }

        private static ICheckedType BuildMethodCallableType(
            ICheckedType source,
            ObjectMethodSymbol method,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var sourceDecl = TryGetObjectDeclaration(source);
            var declaringClass = sourceDecl is null
                ? method.ContainingScope?.DeclarationSymbol as ObjectDeclarationSymbol
                : FindDeclaringClassForMember(sourceDecl, method, symbolTree, globalScope)
                    ?? sourceDecl;

            var state = new CheckerState
            {
                EnclosingObject = declaringClass,
                EnclosingObjectType = declaringClass is null
                    ? null
                    : CheckedTypes.FromSymbol(declaringClass),
                NameResolutionScope = declaringClass?.ContainingScope,
                ObjectGenerics = declaringClass is { GenericParameters.Count: > 0 } genericClass
                    ? genericClass.GenericParameters
                    : [],
            };

            ICheckedType SilentResolve(
                ITypeExpression typeAst,
                CheckerState st,
                bool _isRet,
                bool _isUser) =>
                ResolveTypeAstSilently(typeAst, st, symbolTree, globalScope);

            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (GenericInheritanceBindings.TryBuild(
                    source, state, symbolTree, globalScope, SilentResolve, out var built)
                && built.Count > 0)
            {
                bindings = built;
            }

            ICheckedType Substitute(ICheckedType type) =>
                bindings is null
                    ? type
                    : ResolveGenericTypeBySymbol(type, bindings, symbolTree, globalScope);

            var paramTypes = new List<ICheckedType>(method.Parameters.Count);
            foreach (var param in method.Parameters)
            {
                var resolved = param.DeclaredType is null
                    ? CheckedTypes.Mixed
                    : SilentResolve(param.DeclaredType, state, false, true);
                paramTypes.Add(Substitute(resolved));
            }

            ICheckedType returnType = method.ReturnType switch
            {
                null => CheckedTypes.Mixed,
                TyhpReturnTypeGuardAst => CheckedTypes.Bool,
                var returnAst => Substitute(SilentResolve(returnAst, state, true, true)),
            };

            return CallableArityFacetBuilder.BuildFromParameterInfos(
                method.Parameters, paramTypes, returnType);
        }

        private static bool ClassPropertySatisfiesShape(
            ICheckedType source,
            ObjectPropertySymbol property,
            ObjectShapePropertyMember target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (!IsClassPropertyReadable(property) || (target.IsWritable && !IsClassPropertyWritable(property)))
            {
                return false;
            }

            var propertyType = ResolveClassMemberType(
                source, property.DeclaredType, property, symbolTree, globalScope);
            if (!IsAssignableToCore(propertyType, target.Type, symbolTree, globalScope, visited))
            {
                return false;
            }

            return !target.IsWritable
                || IsAssignableToCore(target.Type, propertyType, symbolTree, globalScope, visited);
        }

        private static bool PropertySatisfiesShape(
            ObjectShapePropertyMember source,
            ObjectShapePropertyMember target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (!source.IsReadable || (target.IsWritable && !source.IsWritable))
            {
                return false;
            }

            if (!IsAssignableToCore(source.Type, target.Type, symbolTree, globalScope, visited))
            {
                return false;
            }

            return !target.IsWritable
                || IsAssignableToCore(target.Type, source.Type, symbolTree, globalScope, visited);
        }

        private static bool ConstantSatisfiesShape(
            ICheckedType source,
            ObjectConstantSymbol constant,
            ICheckedType targetType,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var constantType = ResolveClassMemberType(
                source, constant.DeclaredType, constant, symbolTree, globalScope);
            return IsAssignableToCore(constantType, targetType, symbolTree, globalScope, visited);
        }

        private static ICheckedType ResolveClassMemberType(
            ICheckedType source,
            ITypeExpression? declaredType,
            IBaseSymbol member,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (declaredType is null)
            {
                return CheckedTypes.Mixed;
            }

            var declaringClass = member.ContainingScope?.DeclarationSymbol as ObjectDeclarationSymbol
                ?? TryGetObjectDeclaration(source);
            var state = new CheckerState
            {
                EnclosingObject = declaringClass,
                EnclosingObjectType = declaringClass is null
                    ? null
                    : CheckedTypes.FromSymbol(declaringClass),
                NameResolutionScope = declaringClass?.ContainingScope ?? member.ContainingScope,
                ObjectGenerics = declaringClass is { GenericParameters.Count: > 0 } genericClass
                    ? genericClass.GenericParameters
                    : [],
            };

            ICheckedType SilentResolve(
                ITypeExpression typeAst,
                CheckerState st,
                bool _isRet,
                bool _isUser) =>
                ResolveTypeAstSilently(typeAst, st, symbolTree, globalScope);

            var resolved = SilentResolve(declaredType, state, false, true);
            if (GenericInheritanceBindings.TryBuild(
                    source, state, symbolTree, globalScope, SilentResolve, out var bindings)
                && bindings.Count > 0)
            {
                return ResolveGenericTypeBySymbol(resolved, bindings, symbolTree, globalScope);
            }

            return resolved;
        }

        private static bool TryFindExactPublicMethod(
            ObjectDeclarationSymbol start,
            string name,
            bool isStatic,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectMethodSymbol method)
        {
            method = null!;
            foreach (var type in EnumerateObjectHeritage(start, symbolTree, globalScope))
            {
                if (!type.Members.TryGetValue(name, out var member)
                    || member is not ObjectMethodSymbol candidate
                    || candidate.IsStatic != isStatic
                    || !IsPublicApi(candidate))
                {
                    continue;
                }

                method = candidate;
                return true;
            }

            return false;
        }

        private static bool TryFindExactPublicProperty(
            ObjectDeclarationSymbol start,
            string name,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectPropertySymbol property)
        {
            property = null!;
            var key = name.StartsWith('$') ? name : "$" + name;
            foreach (var type in EnumerateObjectHeritage(start, symbolTree, globalScope))
            {
                if (!type.Members.TryGetValue(key, out var member)
                    || member is not ObjectPropertySymbol candidate
                    || !IsPublicApi(candidate))
                {
                    continue;
                }

                property = candidate;
                return true;
            }

            return false;
        }

        private static bool TryFindExactPublicConstant(
            ObjectDeclarationSymbol start,
            string name,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectConstantSymbol constant)
        {
            constant = null!;
            foreach (var type in EnumerateObjectHeritage(start, symbolTree, globalScope))
            {
                if (!type.TryGetConstant(name, out var member)
                    || member is not ObjectConstantSymbol candidate
                    || !IsPublicApi(candidate))
                {
                    continue;
                }

                constant = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Heritage walk that does <em>not</em> fall back to <c>__call</c> / <c>__get</c>.
        /// </summary>
        private static IEnumerable<ObjectDeclarationSymbol> EnumerateObjectHeritage(
            ObjectDeclarationSymbol start,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            var stack = new Stack<ObjectDeclarationSymbol>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                yield return current;

                foreach (var ancestor in EnumerateDirectAncestors(current, symbolTree, globalScope))
                {
                    stack.Push(ancestor);
                }

                foreach (var trait in ResolveUsedTraits(current, symbolTree, globalScope, out _))
                {
                    stack.Push(trait);
                }
            }
        }

        private static bool IsPublicApi(IBaseSymbol symbol)
        {
            if (symbol is BaseSymbol { IsInternal: true })
            {
                return false;
            }

            var visibility = symbol is BaseSymbol baseSymbol
                ? baseSymbol.Visibility
                : MemberModifier.None;
            return (visibility
                & (MemberModifier.Private | MemberModifier.Protected | MemberModifier.Internal)) == 0;
        }

        private static bool IsClassPropertyWritable(ObjectPropertySymbol property) =>
            (property.Visibility & MemberModifier.Readonly) == 0
            && (!property.HasGetHook || property.HasSetHook);

        private static bool IsClassPropertyReadable(ObjectPropertySymbol property) =>
            !property.HasAccessor || property.HasGetHook;

        internal static ObjectShapeCheckedType? TryAsObjectShape(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is ObjectShapeCheckedType shape)
            {
                return shape;
            }

            if (type is SimpleCheckedType { ResolvedSymbol: { } alias }
                && ObjectShapeSupport.TryGetObjectShape(alias) is { } simpleShape)
            {
                return new ObjectShapeCheckedType(simpleShape, alias);
            }

            if (type is GenericCheckedType generic
                && generic.BaseType is SimpleCheckedType { ResolvedSymbol: { } genericAlias }
                && ObjectShapeSupport.TryGetObjectShape(genericAlias) is { } genericShape)
            {
                return new ObjectShapeCheckedType(genericShape, genericAlias, generic.TypeArguments);
            }

            return null;
        }

        internal static bool IsObjectShapeType(ICheckedType type) =>
            TryAsObjectShape(type) is not null;

        private static bool AreObjectShapeIdentitiesEqual(
            ObjectShapeCheckedType left,
            ObjectShapeCheckedType right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (left.DeclaringAlias is not null
                && right.DeclaringAlias is not null
                && SymbolsMatch(left.DeclaringAlias, right.DeclaringAlias)
                && AreTypeArgumentListsEqual(left.TypeArguments, right.TypeArguments, visited))
            {
                return true;
            }

            return ReferenceEquals(left.Shape, right.Shape)
                && AreTypeArgumentListsEqual(left.TypeArguments, right.TypeArguments, visited);
        }

        private static bool AreObjectShapesStructurallyEqual(
            ObjectShapeCheckedType left,
            ObjectShapeCheckedType right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (AreObjectShapeIdentitiesEqual(left, right, visited))
            {
                return true;
            }

            var leftMembers = left.Members;
            var rightMembers = right.Members;
            if (leftMembers is null || rightMembers is null)
            {
                return false;
            }

            return AreMethodMapsEqual(leftMembers.Methods, rightMembers.Methods, visited)
                && ArePropertyMapsEqual(leftMembers.Properties, rightMembers.Properties, visited)
                && AreConstantMapsEqual(leftMembers.Constants, rightMembers.Constants, visited);
        }

        /// <summary>
        /// Instance-member lookup on a shape-typed receiver (including <c>__New&lt;Shape&gt;</c>
        /// and intersections that contain a shape). <c>__construct</c> is present in the map
        /// for constructability but is not an instance method.
        /// </summary>
        internal static bool TryFindShapeInstanceMethod(
            ICheckedType type,
            string methodName,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectShapeCheckedType shape,
            out ObjectShapeMethodMember method)
        {
            method = null!;
            if (!TryGetObjectShapeFromType(type, out shape))
            {
                return false;
            }

            var members = GetShapeMembers(shape, symbolTree, globalScope);
            if (!members.Methods.TryGetValue(methodName, out var found) || found.IsStatic)
            {
                return false;
            }

            method = found;
            return true;
        }

        internal static bool TryFindShapeInstanceProperty(
            ICheckedType type,
            string propertyName,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectShapeCheckedType shape,
            out ObjectShapePropertyMember property)
        {
            property = null!;
            if (!TryGetObjectShapeFromType(type, out shape))
            {
                return false;
            }

            var members = GetShapeMembers(shape, symbolTree, globalScope);
            var normalized = ObjectShapeMemberBuilder.NormalizePropertyName(propertyName);
            if (!members.Properties.TryGetValue(normalized, out var found))
            {
                return false;
            }

            property = found;
            return true;
        }

        internal static bool IsObjectShapeReceiver(ICheckedType type) =>
            TryGetObjectShapeFromType(type, out _);

        private static bool AreTypeArgumentListsEqual(
            IReadOnlyList<ICheckedType> left,
            IReadOnlyList<ICheckedType> right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!AreTypesEqualCore(left[i], right[i], visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreMethodMapsEqual(
            IReadOnlyDictionary<string, ObjectShapeMethodMember> left,
            IReadOnlyDictionary<string, ObjectShapeMethodMember> right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var (name, leftMethod) in left)
            {
                if (!right.TryGetValue(name, out var rightMethod)
                    || leftMethod.IsStatic != rightMethod.IsStatic
                    || !AreTypesEqualCore(leftMethod.CallableType, rightMethod.CallableType, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ArePropertyMapsEqual(
            IReadOnlyDictionary<string, ObjectShapePropertyMember> left,
            IReadOnlyDictionary<string, ObjectShapePropertyMember> right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var (name, leftProperty) in left)
            {
                if (!right.TryGetValue(name, out var rightProperty)
                    || leftProperty.IsReadonly != rightProperty.IsReadonly
                    || leftProperty.HasGetHook != rightProperty.HasGetHook
                    || leftProperty.HasSetHook != rightProperty.HasSetHook
                    || !AreTypesEqualCore(leftProperty.Type, rightProperty.Type, visited))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreConstantMapsEqual(
            IReadOnlyDictionary<string, ObjectShapeConstantMember> left,
            IReadOnlyDictionary<string, ObjectShapeConstantMember> right,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var (name, leftConstant) in left)
            {
                if (!right.TryGetValue(name, out var rightConstant)
                    || !AreTypesEqualCore(leftConstant.Type, rightConstant.Type, visited))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
