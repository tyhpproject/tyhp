using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Builds symbol-keyed generic parameter bindings for a receiver type by walking its
    /// <c>extends</c> chain, and resolves <c>Iterator</c>/<c>IteratorAggregate</c>/
    /// <c>Traversable</c> foreach key/value and <c>ArrayAccess</c> <c>TKey</c>/<c>TValue</c>
    /// contracts from <c>implements</c>, plus <c>ArrayAccessShape&lt;TStruct&gt;</c>. Shared by
    /// member substitution (<see cref="TypeInferrer"/>), struct shape materialization, foreach
    /// typing, and homogeneous / shape <c>$obj[$k]</c>.
    /// </summary>
    internal static class GenericInheritanceBindings
    {
        /// <summary>
        /// Binds every generic parameter reachable from the receiver — its own and each generic
        /// ancestor's — to a concrete type argument, keyed by parameter symbol so that same-named
        /// parameters at different levels stay distinct.
        /// </summary>
        public static bool TryBuild(
            ICheckedType receiverType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            bindings = null!;
            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            ObjectDeclarationSymbol? level;
            IReadOnlyList<ICheckedType> arguments;
            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                && generic.BaseType is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol spelled })
            {
                level = spelled;
                arguments = generic.TypeArguments;
            }
            else if (TryGetObjectDeclaration(type, out var bare))
            {
                // A receiver written without type arguments still inherits whatever its ancestors bind
                // concretely (`class Derived extends Base<string>`).
                level = bare;
                arguments = Array.Empty<ICheckedType>();
            }
            else
            {
                return false;
            }

            bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            var visited = new HashSet<ObjectDeclarationSymbol>();

            while (level is not null && visited.Add(level))
            {
                BindLevelParameters(level, arguments, bindings, state, symbolTree, globalScope, resolveType);

                var parent = TypeComparer.TryGetParentDeclaration(level, symbolTree, globalScope);
                if (parent is null || parent.GenericParameters.Count == 0)
                {
                    break;
                }

                arguments = ResolveExtendsArguments(
                    level, bindings, state, symbolTree, globalScope, resolveType);
                level = parent;
            }

            return bindings.Count > 0;
        }

        /// <summary>
        /// Type arguments on an <c>extends</c> clause. Classes and structs both spell it as a
        /// <c>className</c>, which parks the arguments on the name's <c>"identifier"</c> grammar
        /// addon. The <see cref="TyhpGenericIdentifierAst"/> fallbacks cover names built with their
        /// arguments attached directly instead (tyhpdef import declarations).
        /// </summary>
        public static IReadOnlyList<ITypeExpression>? GetExtendsTypeArguments(ObjectDeclarationSymbol level)
        {
            var extends = StructExtends.ExtendsAstNode(level.DeclaringAstNode);

            return extends is null ? null : GetNameNodeTypeArguments(extends);
        }

        /// <summary>
        /// Type arguments hanging off an <c>implements</c>/<c>extends</c> class-name node (grammar
        /// addon or <see cref="TyhpGenericIdentifierAst"/>), same shapes as
        /// <see cref="GetExtendsTypeArguments"/>.
        /// </summary>
        public static IReadOnlyList<ITypeExpression>? GetNameNodeTypeArguments(IBase2Ast nameNode)
        {
            if (nameNode.AstGrammarAddons.TryGetValue("identifier", out var addon)
                && addon is PhpTypeExpressionListAst list
                && list.GetAllNotNull().Any())
            {
                return list.GetAllNotNull().ToList();
            }

            if (nameNode is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst genArgs }
                && genArgs.GetAllNotNull().Any())
            {
                return genArgs.GetAllNotNull().ToList();
            }

            if (nameNode is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst nested } }
                && nested.GetAllNotNull().Any())
            {
                return nested.GetAllNotNull().ToList();
            }

            return null;
        }

        /// <summary>
        /// Foreach key/value types from the receiver's <c>Iterator</c> /
        /// <c>IteratorAggregate</c> / <c>Traversable</c> contract (own type args or a substituted
        /// <c>implements</c>/<c>extends</c> clause), not from the receiver's own generic parameter
        /// positions. Needed for types like <c>Generator&lt;TKey,TValue,TSend,TReturn&gt;</c> and
        /// <c>SplPriorityQueue&lt;TValue,TPriority&gt;</c> whose own parameter order/count differs
        /// from <c>&lt;TKey, TValue&gt;</c>.
        /// </summary>
        public static bool TryGetTraversableIterationTypes(
            ICheckedType receiverType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Int;
            valueType = CheckedTypes.Mixed;

            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            // The type itself is Iterator / IteratorAggregate / Traversable — read its own args.
            if (TryReadTraversableFamilyArgs(type, out keyType, out valueType))
            {
                return true;
            }

            if (!TryGetObjectDeclaration(type, out var level))
            {
                return false;
            }

            IReadOnlyList<ICheckedType> arguments =
                type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                    ? generic.TypeArguments
                    : Array.Empty<ICheckedType>();

            var bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            var visited = new HashSet<ObjectDeclarationSymbol>();
            // Prefer Iterator over IteratorAggregate over Traversable when several match.
            ICheckedType? bestKey = null;
            ICheckedType? bestValue = null;
            var bestRank = int.MaxValue;

            while (level is not null && visited.Add(level))
            {
                BindLevelParameters(level, arguments, bindings, state, symbolTree, globalScope, resolveType);

                foreach (var className in EnumerateImplementsClassNames(level))
                {
                    if (!TryResolveTraversableClause(
                            className,
                            level,
                            bindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out var clauseKey,
                            out var clauseValue,
                            out var rank))
                    {
                        continue;
                    }

                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        bestKey = clauseKey;
                        bestValue = clauseValue;
                    }
                }

                if (bestKey is not null && bestValue is not null)
                {
                    keyType = bestKey;
                    valueType = bestValue;
                    return true;
                }

                var parent = TypeComparer.TryGetParentDeclaration(level, symbolTree, globalScope);
                if (parent is null)
                {
                    break;
                }

                arguments = parent.GenericParameters.Count == 0
                    ? Array.Empty<ICheckedType>()
                    : ResolveExtendsArguments(
                        level, bindings, state, symbolTree, globalScope, resolveType);
                level = parent;
            }

            return false;
        }

        /// <summary>
        /// Homogeneous <c>$obj[$k]</c> key/value types from the receiver's <c>ArrayAccess&lt;TKey,
        /// TValue&gt;</c> contract (own type args or a substituted <c>implements</c>/<c>extends</c>
        /// clause). Foreach does not use this path.
        /// </summary>
        public static bool TryGetArrayAccessTypes(
            ICheckedType receiverType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Mixed;
            valueType = CheckedTypes.Mixed;

            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TryReadArrayAccessArgs(type, out keyType, out valueType))
            {
                return true;
            }

            if (!TryGetObjectDeclaration(type, out var level))
            {
                return false;
            }

            IReadOnlyList<ICheckedType> arguments =
                type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                    ? generic.TypeArguments
                    : Array.Empty<ICheckedType>();

            var bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            var visited = new HashSet<ObjectDeclarationSymbol>();

            while (level is not null && visited.Add(level))
            {
                BindLevelParameters(level, arguments, bindings, state, symbolTree, globalScope, resolveType);

                foreach (var className in EnumerateImplementsClassNames(level))
                {
                    if (TryResolveArrayAccessClause(
                            className,
                            level,
                            bindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out keyType,
                            out valueType))
                    {
                        return true;
                    }
                }

                var parent = TypeComparer.TryGetParentDeclaration(level, symbolTree, globalScope);
                if (parent is null)
                {
                    break;
                }

                arguments = parent.GenericParameters.Count == 0
                    ? Array.Empty<ICheckedType>()
                    : ResolveExtendsArguments(
                        level, bindings, state, symbolTree, globalScope, resolveType);
                level = parent;
            }

            return false;
        }

        /// <summary>
        /// <c>TStruct</c> from a receiver that is (or implements/extends)
        /// <c>\Tyhp\Contracts\ArrayAccessShape&lt;TStruct&gt;</c>. Does not treat
        /// <c>ArrayAccess&lt;SomeStruct&gt;</c> as a shape.
        /// </summary>
        public static bool TryGetArrayAccessShapeStruct(
            ICheckedType receiverType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType structType)
        {
            structType = CheckedTypes.Unresolved;

            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TryReadArrayAccessShapeArg(type, out structType))
            {
                return true;
            }

            if (!TryGetObjectDeclaration(type, out var level))
            {
                return false;
            }

            IReadOnlyList<ICheckedType> arguments =
                type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                    ? generic.TypeArguments
                    : Array.Empty<ICheckedType>();

            var bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            var visited = new HashSet<ObjectDeclarationSymbol>();

            while (level is not null && visited.Add(level))
            {
                BindLevelParameters(level, arguments, bindings, state, symbolTree, globalScope, resolveType);

                if (ArrayAccessShapeSupport.IsArrayAccessShapeNominal(CheckedTypes.FromSymbol(level)))
                {
                    if (level.GenericParameters.Count > 0
                        && bindings.TryGetValue(level.GenericParameters[0], out var bound)
                        && bound is not null)
                    {
                        structType = bound;
                    }
                    else if (arguments.Count > 0)
                    {
                        structType = arguments[0];
                    }

                    return true;
                }

                foreach (var className in EnumerateImplementsClassNames(level))
                {
                    if (TryResolveArrayAccessShapeClause(
                            className,
                            level,
                            bindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out structType))
                    {
                        return true;
                    }
                }

                var parent = TypeComparer.TryGetParentDeclaration(level, symbolTree, globalScope);
                if (parent is null)
                {
                    break;
                }

                arguments = parent.GenericParameters.Count == 0
                    ? Array.Empty<ICheckedType>()
                    : ResolveExtendsArguments(
                        level, bindings, state, symbolTree, globalScope, resolveType);
                level = parent;
            }

            return false;
        }

        /// <summary>
        /// PHP cannot use a struct (erases to <c>array</c>) or an <c>array</c> as an
        /// <c>ArrayAccess</c> offset. Object keys (<c>WeakMap</c>, <c>SplObjectStorage</c>) stay
        /// legal.
        /// </summary>
        public static bool IsIllegalArrayAccessKeyType(ICheckedType type)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (current is UnionCheckedType union)
            {
                return union.Members.Count > 0 && union.Members.Any(IsIllegalArrayAccessKeyType);
            }

            if (current is GenericCheckedType generic)
            {
                current = generic.BaseType;
            }

            if (current is StructCheckedType)
            {
                return true;
            }

            if (Rules.CheckerHelpers.IsBuiltInName(current, "array"))
            {
                return true;
            }

            return Rules.CheckerHelpers.TryGetObjectDeclaration(current) is { IsStruct: true };
        }

        /// <summary>
        /// True when <paramref name="type"/> is itself an <c>ArrayAccess&lt;…&gt;</c>
        /// instantiation (including a nullable wrapper), not a class that implements it.
        /// An illegal <c>TKey</c> on that spelling is already reported by
        /// <see cref="GenericTypeArgumentValidator"/> at the type-expression site, so
        /// indexing / destructure must not emit TYHP4330 again on a different span.
        /// </summary>
        public static bool IsDirectArrayAccessInstantiation(ICheckedType type)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            return TryReadArrayAccessArgs(current, out _, out _);
        }

        private static IEnumerable<IClassName> EnumerateImplementsClassNames(ObjectDeclarationSymbol level)
        {
            // Overlay merge writes remapped <c>implements</c> onto <see cref="ObjectDeclarationSymbol.ImplementsTypes"/>
            // without rewriting the harvested AST. Prefer those nodes so
            // <c>Generator implements Iterator&lt;TKey, TValue&gt;</c> is visible to foreach.
            foreach (var typeExpr in level.ImplementsTypes)
            {
                if (TryUnwrapClassName(typeExpr, out var fromSymbol))
                {
                    yield return fromSymbol;
                }
            }

            foreach (var className in TypeComparer.GetAstImplementsClassNames(level))
            {
                yield return className;
            }
        }

        private static bool TryUnwrapClassName(ITypeExpression typeExpr, out IClassName className)
        {
            switch (typeExpr)
            {
                case IClassName direct:
                    className = direct;
                    return true;
                case PhpNamedTypeAst { Name: IClassName named }:
                    className = named;
                    return true;
                default:
                    className = null!;
                    return false;
            }
        }

        private static bool TryReadArrayAccessArgs(
            ICheckedType type,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Mixed;
            valueType = CheckedTypes.Mixed;

            ICheckedType baseType = type;
            IReadOnlyList<ICheckedType> args = Array.Empty<ICheckedType>();
            if (type is GenericCheckedType generic)
            {
                baseType = generic.BaseType;
                args = generic.TypeArguments;
            }

            if (!IsArrayAccessNominal(baseType))
            {
                return false;
            }

            if (args.Count >= 1)
            {
                keyType = args[0];
            }

            if (args.Count >= 2)
            {
                valueType = args[1];
            }

            return true;
        }

        private static bool TryResolveArrayAccessClause(
            IClassName className,
            ObjectDeclarationSymbol owner,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Mixed;
            valueType = CheckedTypes.Mixed;

            var scope = owner.ContainingScope ?? globalScope;
            var implemented = TypeComparer.ResolveClassNameSymbol(className, scope, symbolTree);
            if (implemented is null)
            {
                return false;
            }

            if (IsArrayAccessNominal(CheckedTypes.FromSymbol(implemented)))
            {
                var typeArgs = GetNameNodeTypeArguments(className);
                if (typeArgs is null || typeArgs.Count == 0)
                {
                    if (TryFindArrayAccessInInterfaceExtends(
                            implemented,
                            bindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out keyType,
                            out valueType))
                    {
                        return true;
                    }

                    keyType = CheckedTypes.Mixed;
                    valueType = CheckedTypes.Mixed;
                    return true;
                }

                var levelState = ForLevel(state, owner);
                var resolved = new List<ICheckedType>(typeArgs.Count);
                foreach (var argument in typeArgs)
                {
                    var argumentType = resolveType(argument, levelState, false, true);
                    resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                        argumentType, bindings, symbolTree, globalScope));
                }

                keyType = resolved[0];
                if (resolved.Count >= 2)
                {
                    valueType = resolved[1];
                }

                return true;
            }

            var interfaceBindings = BindClauseTypeArguments(
                className, implemented, owner, bindings, state, symbolTree, globalScope, resolveType);

            return TryFindArrayAccessInInterfaceExtends(
                implemented,
                interfaceBindings,
                state,
                symbolTree,
                globalScope,
                resolveType,
                out keyType,
                out valueType);
        }

        private static bool TryFindArrayAccessInInterfaceExtends(
            ObjectDeclarationSymbol iface,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> outerBindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Mixed;
            valueType = CheckedTypes.Mixed;

            var ifaceBindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>(outerBindings);
            var visited = new HashSet<ObjectDeclarationSymbol> { iface };

            foreach (var className in EnumerateImplementsClassNames(iface))
            {
                var scope = iface.ContainingScope ?? globalScope;
                var parent = TypeComparer.ResolveClassNameSymbol(className, scope, symbolTree);
                if (parent is null || !visited.Add(parent))
                {
                    continue;
                }

                if (!IsArrayAccessNominal(CheckedTypes.FromSymbol(parent)))
                {
                    var parentBindings = BindClauseTypeArguments(
                        className, parent, iface, ifaceBindings, state, symbolTree, globalScope, resolveType);
                    if (TryFindArrayAccessInInterfaceExtends(
                            parent,
                            parentBindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out keyType,
                            out valueType))
                    {
                        return true;
                    }

                    continue;
                }

                var typeArgs = GetNameNodeTypeArguments(className);
                if (typeArgs is null || typeArgs.Count == 0)
                {
                    return true;
                }

                var levelState = ForLevel(state, iface);
                var resolved = new List<ICheckedType>(typeArgs.Count);
                foreach (var argument in typeArgs)
                {
                    var argumentType = resolveType(argument, levelState, false, true);
                    resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                        argumentType, ifaceBindings, symbolTree, globalScope));
                }

                keyType = resolved[0];
                if (resolved.Count >= 2)
                {
                    valueType = resolved[1];
                }

                return true;
            }

            return false;
        }

        private static bool TryReadArrayAccessShapeArg(ICheckedType type, out ICheckedType structType)
        {
            structType = CheckedTypes.Unresolved;

            ICheckedType baseType = type;
            IReadOnlyList<ICheckedType> args = Array.Empty<ICheckedType>();
            if (type is GenericCheckedType generic)
            {
                baseType = generic.BaseType;
                args = generic.TypeArguments;
            }

            if (!ArrayAccessShapeSupport.IsArrayAccessShapeNominal(baseType))
            {
                return false;
            }

            if (args.Count >= 1)
            {
                structType = args[0];
            }

            return true;
        }

        private static bool TryResolveArrayAccessShapeClause(
            IClassName className,
            ObjectDeclarationSymbol owner,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType structType)
        {
            structType = CheckedTypes.Unresolved;

            var scope = owner.ContainingScope ?? globalScope;
            var implemented = TypeComparer.ResolveClassNameSymbol(className, scope, symbolTree);
            if (implemented is null)
            {
                return false;
            }

            if (ArrayAccessShapeSupport.IsArrayAccessShapeNominal(CheckedTypes.FromSymbol(implemented)))
            {
                var typeArgs = GetNameNodeTypeArguments(className);
                if (typeArgs is null || typeArgs.Count == 0)
                {
                    if (TryFindArrayAccessShapeInInterfaceExtends(
                            implemented,
                            bindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out structType))
                    {
                        return true;
                    }

                    return true;
                }

                var levelState = ForLevel(state, owner);
                var argumentType = resolveType(typeArgs[0], levelState, false, true);
                structType = TypeComparer.ResolveGenericTypeBySymbol(
                    argumentType, bindings, symbolTree, globalScope);
                return true;
            }

            var interfaceBindings = BindClauseTypeArguments(
                className, implemented, owner, bindings, state, symbolTree, globalScope, resolveType);

            return TryFindArrayAccessShapeInInterfaceExtends(
                implemented,
                interfaceBindings,
                state,
                symbolTree,
                globalScope,
                resolveType,
                out structType);
        }

        private static bool TryFindArrayAccessShapeInInterfaceExtends(
            ObjectDeclarationSymbol iface,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> outerBindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType structType)
        {
            structType = CheckedTypes.Unresolved;

            var ifaceBindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>(outerBindings);
            var visited = new HashSet<ObjectDeclarationSymbol> { iface };

            foreach (var className in EnumerateImplementsClassNames(iface))
            {
                var scope = iface.ContainingScope ?? globalScope;
                var parent = TypeComparer.ResolveClassNameSymbol(className, scope, symbolTree);
                if (parent is null || !visited.Add(parent))
                {
                    continue;
                }

                if (!ArrayAccessShapeSupport.IsArrayAccessShapeNominal(CheckedTypes.FromSymbol(parent)))
                {
                    var parentBindings = BindClauseTypeArguments(
                        className, parent, iface, ifaceBindings, state, symbolTree, globalScope, resolveType);
                    if (TryFindArrayAccessShapeInInterfaceExtends(
                            parent,
                            parentBindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out structType))
                    {
                        return true;
                    }

                    continue;
                }

                var typeArgs = GetNameNodeTypeArguments(className);
                if (typeArgs is null || typeArgs.Count == 0)
                {
                    if (parent.GenericParameters.Count > 0
                        && ifaceBindings.TryGetValue(parent.GenericParameters[0], out var bound)
                        && bound is not null)
                    {
                        structType = bound;
                    }

                    return true;
                }

                var levelState = ForLevel(state, iface);
                var argumentType = resolveType(typeArgs[0], levelState, false, true);
                structType = TypeComparer.ResolveGenericTypeBySymbol(
                    argumentType, ifaceBindings, symbolTree, globalScope);
                return true;
            }

            return false;
        }

        private static bool IsArrayAccessNominal(ICheckedType type)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (current is GenericCheckedType generic)
            {
                current = generic.BaseType;
            }

            if (current is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                var fqn = obj.FullyQualifiedName.TrimStart('\\');
                return string.Equals(fqn, "ArrayAccess", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(NominalTypeName(type), "ArrayAccess", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadTraversableFamilyArgs(
            ICheckedType type,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Int;
            valueType = CheckedTypes.Mixed;

            ICheckedType baseType = type;
            IReadOnlyList<ICheckedType> args = Array.Empty<ICheckedType>();
            if (type is GenericCheckedType generic)
            {
                baseType = generic.BaseType;
                args = generic.TypeArguments;
            }

            if (!TryTraversableFamilyRank(baseType, out _))
            {
                return false;
            }

            if (args.Count >= 1)
            {
                valueType = args[^1];
            }

            if (args.Count >= 2)
            {
                keyType = args[0];
            }

            return true;
        }

        private static bool TryResolveTraversableClause(
            IClassName className,
            ObjectDeclarationSymbol owner,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType keyType,
            out ICheckedType valueType,
            out int rank)
        {
            keyType = CheckedTypes.Int;
            valueType = CheckedTypes.Mixed;
            rank = int.MaxValue;

            var scope = owner.ContainingScope ?? globalScope;
            var implemented = TypeComparer.ResolveClassNameSymbol(className, scope, symbolTree);
            if (implemented is null)
            {
                return false;
            }

            if (TryTraversableFamilyRank(CheckedTypes.FromSymbol(implemented), out rank))
            {
                var typeArgs = GetNameNodeTypeArguments(className);
                if (typeArgs is null || typeArgs.Count == 0)
                {
                    // e.g. `implements MyIter` where MyIter extends Iterator<…> — walk the
                    // interface's own extends for a typed Traversable-family ancestor.
                    return TryFindTraversableInInterfaceExtends(
                        implemented,
                        bindings,
                        state,
                        symbolTree,
                        globalScope,
                        resolveType,
                        out keyType,
                        out valueType,
                        out rank);
                }

                var levelState = ForLevel(state, owner);
                var resolved = new List<ICheckedType>(typeArgs.Count);
                foreach (var argument in typeArgs)
                {
                    var argumentType = resolveType(argument, levelState, false, true);
                    resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                        argumentType, bindings, symbolTree, globalScope));
                }

                valueType = resolved[^1];
                if (resolved.Count >= 2)
                {
                    keyType = resolved[0];
                }

                return true;
            }

            // Non-family interface that may extend Iterator / Traversable with concrete args. Bind
            // its own generic parameters from this clause's type arguments (resolved against
            // owner's scope/bindings) before descending — a remapped interface such as
            // `MyIter<A, B> extends Iterator<B, A>` must resolve A/B to owner's concrete arguments,
            // not leak MyIter's own unbound parameter symbols into the result.
            var interfaceBindings = BindClauseTypeArguments(
                className, implemented, owner, bindings, state, symbolTree, globalScope, resolveType);

            return TryFindTraversableInInterfaceExtends(
                implemented,
                interfaceBindings,
                state,
                symbolTree,
                globalScope,
                resolveType,
                out keyType,
                out valueType,
                out rank);
        }

        /// <summary>
        /// Binds <paramref name="target"/>'s own generic parameters from the type arguments
        /// attached to <paramref name="className"/> (an <c>implements</c>/<c>extends</c> clause item
        /// on <paramref name="owner"/>), resolved against <paramref name="owner"/>'s scope and
        /// existing bindings — e.g. for `class Foo implements MyIter&lt;int, string&gt;`, binds
        /// MyIter's `A`/`B` to `int`/`string` so a subsequent walk into MyIter's own
        /// <c>extends</c> clause (which may spell them in a different order, or not at all) resolves
        /// to owner's concrete arguments instead of MyIter's raw parameter symbols. Returns a new
        /// dictionary; parameters with no supplied argument fall back to their declared default
        /// (mirrors <see cref="BindLevelParameters"/>, which this delegates to).
        /// </summary>
        private static Dictionary<GenericTypeParameterSymbol, ICheckedType> BindClauseTypeArguments(
            IClassName className,
            ObjectDeclarationSymbol target,
            ObjectDeclarationSymbol owner,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> ownerBindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var merged = new Dictionary<GenericTypeParameterSymbol, ICheckedType>(ownerBindings);
            if (target.GenericParameters.Count == 0)
            {
                return merged;
            }

            var typeArgs = GetNameNodeTypeArguments(className);
            IReadOnlyList<ICheckedType> resolvedArguments = Array.Empty<ICheckedType>();
            if (typeArgs is not null && typeArgs.Count > 0)
            {
                var ownerState = ForLevel(state, owner);
                var resolved = new List<ICheckedType>(typeArgs.Count);
                foreach (var argument in typeArgs)
                {
                    var argumentType = resolveType(argument, ownerState, false, true);
                    resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                        argumentType, ownerBindings, symbolTree, globalScope));
                }

                resolvedArguments = resolved;
            }

            BindLevelParameters(target, resolvedArguments, merged, state, symbolTree, globalScope, resolveType);
            return merged;
        }

        private static bool TryFindTraversableInInterfaceExtends(
            ObjectDeclarationSymbol iface,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> outerBindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType keyType,
            out ICheckedType valueType,
            out int rank)
        {
            keyType = CheckedTypes.Int;
            valueType = CheckedTypes.Mixed;
            rank = int.MaxValue;

            // Interface type parameters stay unbound unless the implements clause supplied args
            // (handled by the caller). Concrete extends args like Iterator<int, string> still resolve.
            var ifaceBindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>(outerBindings);

            ICheckedType? bestKey = null;
            ICheckedType? bestValue = null;
            var bestRank = int.MaxValue;
            var visited = new HashSet<ObjectDeclarationSymbol> { iface };

            foreach (var className in TypeComparer.GetAstImplementsClassNames(iface))
            {
                var scope = iface.ContainingScope ?? globalScope;
                var parent = TypeComparer.ResolveClassNameSymbol(className, scope, symbolTree);
                if (parent is null || !visited.Add(parent))
                {
                    continue;
                }

                if (!TryTraversableFamilyRank(CheckedTypes.FromSymbol(parent), out var parentRank))
                {
                    var parentBindings = BindClauseTypeArguments(
                        className, parent, iface, ifaceBindings, state, symbolTree, globalScope, resolveType);
                    if (TryFindTraversableInInterfaceExtends(
                            parent,
                            parentBindings,
                            state,
                            symbolTree,
                            globalScope,
                            resolveType,
                            out var nestedKey,
                            out var nestedValue,
                            out var nestedRank)
                        && nestedRank < bestRank)
                    {
                        bestRank = nestedRank;
                        bestKey = nestedKey;
                        bestValue = nestedValue;
                    }

                    continue;
                }

                var typeArgs = GetNameNodeTypeArguments(className);
                if (typeArgs is null || typeArgs.Count == 0)
                {
                    continue;
                }

                var levelState = ForLevel(state, iface);
                var resolved = new List<ICheckedType>(typeArgs.Count);
                foreach (var argument in typeArgs)
                {
                    var argumentType = resolveType(argument, levelState, false, true);
                    resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                        argumentType, ifaceBindings, symbolTree, globalScope));
                }

                var clauseValue = resolved[^1];
                var clauseKey = resolved.Count >= 2 ? resolved[0] : CheckedTypes.Int;
                if (parentRank < bestRank)
                {
                    bestRank = parentRank;
                    bestKey = clauseKey;
                    bestValue = clauseValue;
                }
            }

            if (bestKey is null || bestValue is null)
            {
                return false;
            }

            keyType = bestKey;
            valueType = bestValue;
            rank = bestRank;
            return true;
        }

        private static bool TryTraversableFamilyRank(ICheckedType type, out int rank)
        {
            rank = int.MaxValue;
            var name = NominalTypeName(type);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // Prefer the most specific foreach contract when several are listed.
            if (string.Equals(name, "Iterator", StringComparison.OrdinalIgnoreCase))
            {
                rank = 0;
                return true;
            }

            if (string.Equals(name, "IteratorAggregate", StringComparison.OrdinalIgnoreCase))
            {
                rank = 1;
                return true;
            }

            if (string.Equals(name, "Traversable", StringComparison.OrdinalIgnoreCase))
            {
                rank = 2;
                return true;
            }

            return false;
        }

        private static string? NominalTypeName(ICheckedType type)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (current is GenericCheckedType generic)
            {
                current = generic.BaseType;
            }

            if (current is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return obj.Name;
            }

            var display = current.DisplayName.TrimStart('\\');
            var angle = display.IndexOf('<');
            if (angle >= 0)
            {
                display = display[..angle];
            }

            var slash = display.LastIndexOf('\\');
            return slash >= 0 ? display[(slash + 1)..] : display;
        }

        public static StructCheckedType SubstituteShape(
            StructCheckedType shape,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            (StructCheckedType)TypeComparer.ResolveGenericTypeBySymbol(
                shape, bindings, symbolTree, globalScope);

        private static void BindLevelParameters(
            ObjectDeclarationSymbol level,
            IReadOnlyList<ICheckedType> arguments,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            // Inside the open generic itself (`$this` in `Promise<TReturn>`), empty type-argument
            // lists must leave parameters unbound so ObjectGenerics keep meaning. Filling defaults
            // here would rewrite `TReturn` to `void` and break every member that mentions it.
            // Defaults still apply for *foreign* bare receivers (`$f instanceof \Fiber` →
            // `Fiber<…=mixed>` so `resume(?TResume)` becomes `resume(?mixed)`).
            var applyDefaults = !ReferenceEquals(state.EnclosingObject, level);

            for (var i = 0; i < level.GenericParameters.Count; i++)
            {
                var param = level.GenericParameters[i];
                if (i < arguments.Count
                    && arguments[i] is not null
                    && !TypeComparer.IsUnresolvedType(arguments[i]))
                {
                    bindings[param] = arguments[i];
                    continue;
                }

                if (!applyDefaults || param.DefaultType is null || bindings.ContainsKey(param))
                {
                    continue;
                }

                var defaultState = ForLevel(state, level);
                var defaultType = resolveType(param.DefaultType, defaultState, false, true);
                if (!TypeComparer.IsUnresolvedType(defaultType))
                {
                    bindings[param] = TypeComparer.ResolveGenericTypeBySymbol(
                        defaultType, bindings, symbolTree, globalScope);
                }
            }
        }

        private static IReadOnlyList<ICheckedType> ResolveExtendsArguments(
            ObjectDeclarationSymbol level,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var extendsArguments = GetExtendsTypeArguments(level);
            if (extendsArguments is null || extendsArguments.Count == 0)
            {
                return Array.Empty<ICheckedType>();
            }

            var levelState = ForLevel(state, level);
            var resolved = new List<ICheckedType>(extendsArguments.Count);
            foreach (var argument in extendsArguments)
            {
                var argumentType = resolveType(argument, levelState, false, true);
                resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                    argumentType, bindings, symbolTree, globalScope));
            }

            return resolved;
        }

        /// <summary>
        /// State for resolving a declaration written in <paramref name="level"/>'s own generic scope
        /// (its <c>extends</c> arguments, its parameter defaults). Must stay mutable — the resolver
        /// it is handed to may snapshot it.
        /// </summary>
        private static CheckerState ForLevel(CheckerState state, ObjectDeclarationSymbol level)
        {
            var levelState = state.Fork();
            levelState.EnclosingObject = level;
            levelState.EnclosingObjectType = CheckedTypes.FromSymbol(level);
            levelState.ObjectGenerics = level.GenericParameters;
            return levelState;
        }

        private static bool TryGetObjectDeclaration(
            ICheckedType receiverType,
            out ObjectDeclarationSymbol objectDecl)
        {
            objectDecl = null!;
            var unwrapped = receiverType;
            while (unwrapped is NullableCheckedType or GenericCheckedType)
            {
                unwrapped = unwrapped is NullableCheckedType nullable
                    ? nullable.InnerType
                    : ((GenericCheckedType)unwrapped).BaseType;
            }

            if (unwrapped is not SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return false;
            }

            objectDecl = obj;
            return true;
        }
    }
}
