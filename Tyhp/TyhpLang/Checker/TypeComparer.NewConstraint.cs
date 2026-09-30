using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    internal enum NewConstraintFailure
    {
        None,
        NotConstructableKind,
        NonPublicConstructor,
        ConstructorMismatch,
        InstanceMismatch,
    }

    public static partial class TypeComparer
    {
        internal static bool IsNewUtilityType(ICheckedType type) =>
            TryGetNewUtility(type, out _);

        internal static bool TryGetNewUtility(ICheckedType type, out GenericCheckedType generic)
        {
            generic = null!;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is IntersectionCheckedType intersection)
            {
                foreach (var member in intersection.Members)
                {
                    if (TryGetNewUtility(member, out generic))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (type is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint },
                })
            {
                return TryGetNewUtility(constraint, out generic);
            }

            if (type is not GenericCheckedType candidate
                || !SymbolNameTypeHelper.TryGetUtilitySymbol(candidate, out var utility)
                || utility.Behavior != UtilityBehavior.New
                || candidate.TypeArguments.Count == 0)
            {
                return false;
            }

            generic = candidate;
            return true;
        }

        /// <summary>
        /// True when <paramref name="type"/> is an object-shape alias (including a rename,
        /// generic instantiation, or <c>Nominal &amp; object { … }</c> intersection).
        /// </summary>
        internal static bool IsObjectShapeTypeArgument(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (IsUnresolvedType(type))
            {
                return true;
            }

            if (TryAsObjectShape(type) is not null)
            {
                return true;
            }

            if (type is IntersectionCheckedType intersection)
            {
                return intersection.Members.Any(IsObjectShapeTypeArgument);
            }

            if (type is SimpleCheckedType { ResolvedSymbol: { } alias }
                && ObjectShapeSupport.AliasResolvesToObjectShape(alias))
            {
                return true;
            }

            if (type is GenericCheckedType generic
                && generic.BaseType is SimpleCheckedType { ResolvedSymbol: { } genericAlias }
                && ObjectShapeSupport.AliasResolvesToObjectShape(genericAlias))
            {
                return true;
            }

            return false;
        }

        internal static bool TryDescribeNewConstraintFailure(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out NewConstraintFailure failure,
            out string sourceDisplay,
            out string shapeDisplay,
            out string kind)
        {
            failure = NewConstraintFailure.None;
            sourceDisplay = source.DisplayName;
            shapeDisplay = target.DisplayName;
            kind = "";

            if (!TryGetNewUtility(target, out var newUtility))
            {
                return false;
            }

            var inner = newUtility.TypeArguments[0];
            shapeDisplay = inner.DisplayName;
            var visited = new HashSet<(ICheckedType, ICheckedType)>();

            if (TryGetObjectDeclaration(source) is { } objectDecl)
            {
                if (TryGetNotConstructableKind(objectDecl, out kind))
                {
                    failure = NewConstraintFailure.NotConstructableKind;
                    return true;
                }

                if (!InstanceSatisfiesNewInner(source, inner, symbolTree, globalScope, visited))
                {
                    failure = NewConstraintFailure.InstanceMismatch;
                    return true;
                }

                if (!TryGetPublicConstructor(
                        source, objectDecl, symbolTree, globalScope, out _, out var nonPublic))
                {
                    if (nonPublic)
                    {
                        failure = NewConstraintFailure.NonPublicConstructor;
                        return true;
                    }
                }

                if (!ClassConstructorSatisfiesShape(
                        source, objectDecl, inner, symbolTree, globalScope, visited))
                {
                    failure = NewConstraintFailure.ConstructorMismatch;
                    return true;
                }

                return false;
            }

            if (TryGetNewUtility(source, out var sourceNew))
            {
                var sourceInner = sourceNew.TypeArguments[0];
                if (!IsAssignableToCore(sourceInner, inner, symbolTree, globalScope, visited))
                {
                    failure = NewConstraintFailure.InstanceMismatch;
                    return true;
                }

                if (!ShapeConstructorSatisfiesShape(sourceInner, inner, symbolTree, globalScope, visited))
                {
                    failure = NewConstraintFailure.ConstructorMismatch;
                    return true;
                }

                return false;
            }

            if (source is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol,
                }
                && TryGetNewUtility(source, out _))
            {
                return false;
            }

            failure = NewConstraintFailure.InstanceMismatch;
            return true;
        }

        private static bool SourceSatisfiesNewConstraint(
            ICheckedType source,
            GenericCheckedType targetNew,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var inner = targetNew.TypeArguments[0];
            var instanceSource = TryGetNewUtility(source, out var sourceNew)
                ? sourceNew.TypeArguments[0]
                : source;

            if (!InstanceSatisfiesNewInner(instanceSource, inner, symbolTree, globalScope, visited))
            {
                return false;
            }

            if (TryGetObjectDeclaration(source) is { } objectDecl)
            {
                return IsConstructableKind(objectDecl)
                    && TryGetPublicConstructor(
                        source, objectDecl, symbolTree, globalScope, out _, out var nonPublic)
                    && !nonPublic
                    && ClassConstructorSatisfiesShape(
                        source, objectDecl, inner, symbolTree, globalScope, visited);
            }

            if (TryGetNewUtility(source, out sourceNew))
            {
                return ShapeConstructorSatisfiesShape(
                    sourceNew.TypeArguments[0], inner, symbolTree, globalScope, visited);
            }

            if (source is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint },
                })
            {
                return IsAssignableToCore(constraint, targetNew, symbolTree, globalScope, visited);
            }

            return false;
        }

        private static bool InstanceSatisfiesNewInner(
            ICheckedType source,
            ICheckedType inner,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited) =>
            IsAssignableToCore(source, inner, symbolTree, globalScope, visited);

        internal static ICheckedType GetShapeConstructorCallable(
            ICheckedType shapeType,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var expanded = ExpandTypeAliases(
                shapeType,
                symbolTree,
                globalScope,
                (ast, alias) => ResolveTypeAstSilently(
                    ast,
                    Rules.CheckerHelpers.WithAliasBodyContext(
                        new CheckerState { NameResolutionScope = globalScope },
                        alias),
                    symbolTree,
                    globalScope));

            if (!TryGetObjectShapeFromType(expanded, out var shape))
            {
                return ImpliedZeroArgConstructor();
            }

            var members = GetShapeMembers(shape, symbolTree, globalScope);
            foreach (var (name, method) in members.Methods)
            {
                if (ObjectShapeMemberBuilder.IsConstructabilityName(name))
                {
                    return method.CallableType;
                }
            }

            return ImpliedZeroArgConstructor();
        }

        internal static bool TryGetObjectShapeFromType(
            ICheckedType type,
            out ObjectShapeCheckedType shape)
        {
            shape = null!;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TryAsObjectShape(type) is { } direct)
            {
                shape = direct;
                return true;
            }

            if (type is GenericCheckedType
                && TryGetNewUtility(type, out var newUtility)
                && newUtility.TypeArguments.Count > 0)
            {
                return TryGetObjectShapeFromType(newUtility.TypeArguments[0], out shape);
            }

            if (type is IntersectionCheckedType intersection)
            {
                foreach (var member in intersection.Members)
                {
                    if (TryGetObjectShapeFromType(member, out shape))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static ObjectShapeMemberMap GetShapeMembers(
            ObjectShapeCheckedType shape,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (shape.Members is not null)
            {
                return shape.Members;
            }

            var state = new CheckerState
            {
                NameResolutionScope = shape.DeclaringAlias?.ContainingScope ?? globalScope,
            };
            return ObjectShapeMemberBuilder.Build(
                shape.Shape,
                (typeAst, _) => ExpandTypeAliases(
                    ResolveTypeAstSilently(typeAst, state, symbolTree, globalScope),
                    symbolTree,
                    globalScope,
                    (ast, alias) => ResolveTypeAstSilently(
                        ast,
                        Rules.CheckerHelpers.WithAliasBodyContext(state, alias),
                        symbolTree,
                        globalScope)));
        }

        private static ICheckedType ImpliedZeroArgConstructor() =>
            new CallableCheckedType([], CheckedTypes.Void);

        private static bool IsConstructableKind(ObjectDeclarationSymbol objectDecl) =>
            !TryGetNotConstructableKind(objectDecl, out _);

        private static bool TryGetNotConstructableKind(
            ObjectDeclarationSymbol objectDecl,
            out string kind)
        {
            kind = objectDecl.ObjectKind switch
            {
                PhpTypeDeclType.Interface => "interface",
                PhpTypeDeclType.Trait => "trait",
                PhpTypeDeclType.Enum => "enum",
                _ => "",
            };
            if (!string.IsNullOrEmpty(kind))
            {
                return true;
            }

            if ((objectDecl.Visibility & MemberModifier.Abstract) != 0)
            {
                kind = "abstract";
                return true;
            }

            return false;
        }

        private static bool TryGetPublicConstructor(
            ICheckedType source,
            ObjectDeclarationSymbol objectDecl,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectMethodSymbol? constructor,
            out bool nonPublic)
        {
            constructor = null;
            nonPublic = false;
            if (symbolTree.ResolveMember("__construct", objectDecl, SilentDiagnostics)
                is not ObjectMethodSymbol ctor)
            {
                return true;
            }

            constructor = ctor;
            if (!IsPublicApi(ctor))
            {
                nonPublic = true;
                return false;
            }

            _ = source;
            _ = globalScope;
            return true;
        }

        private static bool ClassConstructorSatisfiesShape(
            ICheckedType source,
            ObjectDeclarationSymbol objectDecl,
            ICheckedType shapeType,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var shapeCtor = GetShapeConstructorCallable(shapeType, symbolTree, globalScope);
            var classCtor = GetClassConstructorCallable(
                source, objectDecl, symbolTree, globalScope);
            return ConstructorCallableSatisfiesShape(classCtor, shapeCtor, symbolTree, globalScope, visited);
        }

        private static bool ShapeConstructorSatisfiesShape(
            ICheckedType sourceShape,
            ICheckedType targetShape,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var sourceCtor = GetShapeConstructorCallable(sourceShape, symbolTree, globalScope);
            var targetCtor = GetShapeConstructorCallable(targetShape, symbolTree, globalScope);
            return ConstructorCallableSatisfiesShape(sourceCtor, targetCtor, symbolTree, globalScope, visited);
        }

        private static ICheckedType GetClassConstructorCallable(
            ICheckedType source,
            ObjectDeclarationSymbol objectDecl,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (symbolTree.ResolveMember("__construct", objectDecl, SilentDiagnostics)
                is not ObjectMethodSymbol ctor)
            {
                return ImpliedZeroArgConstructor();
            }

            return BuildMethodCallableType(source, ctor, symbolTree, globalScope);
        }

        /// <summary>
        /// Every call the shape constructor allows must be accepted by
        /// <paramref name="classCtor"/>. Extra optional parameters on the class are allowed
        /// because they appear as additional arity facets the shape does not require.
        /// </summary>
        private static bool ConstructorCallableSatisfiesShape(
            ICheckedType classCtor,
            ICheckedType shapeCtor,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var shapeFacets = CallableArityFacetBuilder.GetCallableFacets(shapeCtor);
            if (shapeFacets.Count == 0)
            {
                shapeFacets = [new CallableCheckedType([], CheckedTypes.Void)];
            }

            var classFacets = CallableArityFacetBuilder.GetCallableFacets(classCtor);
            if (classFacets.Count == 0)
            {
                classFacets = [new CallableCheckedType([], CheckedTypes.Void)];
            }

            foreach (var shapeFacet in shapeFacets)
            {
                if (!classFacets.Any(classFacet =>
                        ClassFacetAcceptsShapeFacet(
                            classFacet, shapeFacet, symbolTree, globalScope, visited)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ClassFacetAcceptsShapeFacet(
            CallableCheckedType classFacet,
            CallableCheckedType shapeFacet,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            var shapeCount = shapeFacet.ParameterTypes.Count;
            if (classFacet.LastParameterIsVariadic)
            {
                var prefix = Math.Max(0, classFacet.ParameterTypes.Count - 1);
                if (shapeCount < prefix)
                {
                    return false;
                }
            }
            else if (classFacet.ParameterTypes.Count != shapeCount)
            {
                return false;
            }

            for (var i = 0; i < shapeCount; i++)
            {
                var classParam = classFacet.LastParameterIsVariadic
                    && i >= classFacet.ParameterTypes.Count - 1
                    ? classFacet.ParameterTypes[^1]
                    : classFacet.ParameterTypes[i];
                if (!IsAssignableToCore(
                        shapeFacet.ParameterTypes[i],
                        classParam,
                        symbolTree,
                        globalScope,
                        visited))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when <paramref name="typeArg"/> is an object shape or <c>__New&lt;Shape&gt;</c>
        /// (a structural class-name brand, not an exact-name nominal class).
        /// </summary>
        internal static bool IsStructuralClassNameBrandArgument(ICheckedType typeArg)
        {
            while (typeArg is NullableCheckedType nullable)
            {
                typeArg = nullable.InnerType;
            }

            if (IsUnresolvedType(typeArg)
                || typeArg is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
                || IsBuiltInName(typeArg, "object"))
            {
                return false;
            }

            return TryGetNewUtility(typeArg, out _) || IsObjectShapeTypeArgument(typeArg);
        }

        /// <summary>
        /// <c>__ClassName&lt;A&gt;</c> assigns to <c>__ClassName&lt;B&gt;</c> when <c>B</c> is a
        /// shape or <c>__New&lt;Shape&gt;</c> and <c>A</c> is assignable to <c>B</c>. Nominal
        /// <c>__ClassName&lt;Foo&gt;</c> stays invariant.
        /// </summary>
        internal static bool IsClassNameStructuralBrandAssignable(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (!SymbolNameTypeHelper.TryGetClassNameBrandArgument(source, out var sourceArg)
                || !SymbolNameTypeHelper.TryGetClassNameBrandArgument(target, out var targetArg)
                || !IsStructuralClassNameBrandArgument(targetArg))
            {
                return false;
            }

            return IsAssignableToCore(sourceArg, targetArg, symbolTree, globalScope, visited);
        }
    }
}
