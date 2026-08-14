using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public static partial class TypeComparer
    {
        private const int MaxInheritanceDepth = 100;

        private static bool IsSubtypeOfCore(
            ICheckedType child,
            ICheckedType parent,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited)
        {
            if (!visited.Add((child, parent)))
            {
                return true;
            }

            if (AreTypesEqualCore(child, parent, new HashSet<(ICheckedType, ICheckedType)>()))
            {
                return true;
            }

            // Constrained type parameters are subtypes of their upper bound.
            if (child is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint }
                }
                && IsSubtypeOfCore(constraint, parent, symbolTree, globalScope, visited))
            {
                return true;
            }

            if (IsUnresolvedType(child) || IsUnresolvedType(parent))
            {
                return true;
            }

            if (IsMixedType(parent))
            {
                return true;
            }

            if (IsNeverType(child))
            {
                return true;
            }

            if (child is LiteralCheckedType literalChild && literalChild.Value is bool)
            {
                if (IsBuiltInName(parent, "bool"))
                {
                    return true;
                }

                if (literalChild.Value is true
                    && (IsBuiltInName(parent, "true") || IsBoolLiteralValue(parent, true)))
                {
                    return true;
                }

                if (literalChild.Value is false
                    && (IsBuiltInName(parent, "false") || IsBoolLiteralValue(parent, false)))
                {
                    return true;
                }
            }

            if (IsBuiltInName(parent, "object") && TryGetObjectDeclaration(child) is not null)
            {
                return true;
            }

            // `object&StructShape` (and similar intersections) are subtypes of `object`.
            if (IsBuiltInName(parent, "object") && child is IntersectionCheckedType objectIntersection)
            {
                if (objectIntersection.Members.Any(member =>
                        IsBuiltInName(member, "object")
                        || TryGetObjectDeclaration(member) is not null
                        || IsSubtypeOfCore(member, parent, symbolTree, globalScope, visited)))
                {
                    return true;
                }
            }

            if (IsBuiltInName(parent, "bool"))
            {
                if (IsBuiltInName(child, "true") || IsBuiltInName(child, "false")
                    || IsBoolLiteralValue(child, true) || IsBoolLiteralValue(child, false))
                {
                    return true;
                }
            }

            if (TryGetObjectDeclaration(child) is { } childDecl &&
                TryGetObjectDeclaration(parent) is { } parentDecl)
            {
                return ImplementsOrExtends(
                    childDecl,
                    parentDecl,
                    symbolTree,
                    globalScope,
                    new HashSet<ObjectDeclarationSymbol>());
            }

            if (child is UnionCheckedType unionChild)
            {
                return unionChild.Members.All(member =>
                    IsSubtypeOfCore(member, parent, symbolTree, globalScope, visited));
            }

            if (parent is UnionCheckedType unionParent)
            {
                return unionParent.Members.Any(member =>
                    IsSubtypeOfCore(child, member, symbolTree, globalScope, visited));
            }

            if (TryCheckTemplateStringSubtyping(
                    child, parent, symbolTree, globalScope, visited, out var templateSubtype))
            {
                return templateSubtype;
            }

            if (SymbolNameTypeHelper.IsSymbolNameType(child))
            {
                if (SymbolNameTypeHelper.IsErasureAssignable(child, parent, globalScope))
                {
                    return true;
                }

                if (SymbolNameTypeHelper.IsCompatibleBrandAssignable(
                        child, parent, symbolTree, globalScope))
                {
                    return true;
                }

                var erasure = SymbolNameTypeHelper.GetFullErasure(child, globalScope);
                if (IsSubtypeOfCore(erasure, parent, symbolTree, globalScope, visited))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ImplementsOrExtends(
            ObjectDeclarationSymbol child,
            ObjectDeclarationSymbol parent,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<ObjectDeclarationSymbol> visited,
            int depth = 0)
        {
            if (depth > MaxInheritanceDepth || !visited.Add(child))
            {
                return false;
            }

            if (SymbolsMatch(child, parent))
            {
                return true;
            }

            if (TryResolveParentClass(child, symbolTree, globalScope) is { } resolvedParent &&
                ImplementsOrExtends(resolvedParent, parent, symbolTree, globalScope, visited, depth + 1))
            {
                return true;
            }

            foreach (var implemented in ResolveImplementedInterfaces(child, symbolTree, globalScope))
            {
                if (implemented.ObjectKind == PhpTypeDeclType.Trait)
                {
                    continue;
                }

                if (ImplementsOrExtends(implemented, parent, symbolTree, globalScope, visited, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Yields the interfaces a type implements (and, for an interface, the interfaces it extends),
        /// resolving both the bound <see cref="ITypeExpression"/> list and the raw
        /// <see cref="IClassName"/> nodes on the declaring AST. The latter is required because
        /// <c>implements</c>/<c>extends</c> clauses are parsed as <see cref="IClassName"/> (not
        /// <see cref="ITypeExpression"/>), so the symbol's <c>ImplementsTypes</c> list is typically empty.
        /// Callers that want contracts only should filter out <see cref="PhpTypeDeclType.Trait"/>
        /// entries — trait <c>use</c> names can land in <c>ImplementsTypes</c> when they happen to be
        /// <see cref="ITypeExpression"/>.
        /// </summary>
        internal static IEnumerable<ObjectDeclarationSymbol> ResolveImplementedInterfaces(
            ObjectDeclarationSymbol child,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var scope = child.ContainingScope ?? globalScope;

            foreach (var implementsAst in child.ImplementsTypes)
            {
                if (symbolTree.ResolveType(implementsAst, scope, SilentDiagnostics) is ObjectDeclarationSymbol implemented)
                {
                    yield return implemented;
                }
            }

            foreach (var className in GetAstImplementsClassNames(child))
            {
                if (ResolveClassNameSymbol(className, scope, symbolTree) is { } implemented)
                {
                    yield return implemented;
                }
            }
        }

        // The `implements` clause (and an interface's `extends` clause, which lists base interfaces)
        // is parsed as raw <see cref="IClassName"/> nodes. Both regular (.tyhp) and imported (.tyhpdef)
        // declarations expose these on their declaring AST node.
        private static IEnumerable<IClassName> GetAstImplementsClassNames(ObjectDeclarationSymbol child)
        {
            switch (child.DeclaringAstNode)
            {
                case PhpObjectTypeDeclAst { Implements: { } implementsList }:
                    foreach (var className in implementsList.GetAllNotNull())
                    {
                        yield return className;
                    }

                    break;

                case TyhpdefImportObjectDeclAst tyhpdef:
                    if (tyhpdef.Implements is { } tyhpdefImplements)
                    {
                        foreach (var className in tyhpdefImplements.GetAllNotNull())
                        {
                            yield return className;
                        }
                    }

                    // An interface's base interfaces appear in the `extends` clause as a name list.
                    if (tyhpdef.Extends is PhpClassNameListAst extendsList)
                    {
                        foreach (var className in extendsList.GetAllNotNull())
                        {
                            yield return className;
                        }
                    }

                    break;
            }
        }

        /// <summary>
        /// The traits a declaration pulls in with <c>use</c>, transitively — a trait may itself use
        /// traits. Trait members are never copied onto the using declaration's symbol, so a caller
        /// looking for an inherited member has to consult these separately.
        /// <paramref name="hasUnresolvedTrait"/> is set when a named trait could not be resolved, so a
        /// caller can avoid concluding that a member is absent when it may simply be out of reach.
        /// </summary>
        internal static IReadOnlyCollection<ObjectDeclarationSymbol> ResolveUsedTraits(
            ObjectDeclarationSymbol declaration,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out bool hasUnresolvedTrait)
        {
            var resolved = new HashSet<ObjectDeclarationSymbol>();
            var unresolved = false;
            Collect(declaration);
            hasUnresolvedTrait = unresolved;
            return resolved;

            void Collect(ObjectDeclarationSymbol current)
            {
                var scope = current.ContainingScope ?? globalScope;
                foreach (var traitName in GetAstUsedTraitClassNames(current))
                {
                    if (ResolveClassNameSymbol(traitName, scope, symbolTree) is not { } trait)
                    {
                        unresolved = true;
                        continue;
                    }

                    if (resolved.Add(trait))
                    {
                        Collect(trait);
                    }
                }
            }
        }

        // Trait names in a `use` clause are raw IClassName nodes, and BindTraitUseBlock only records
        // the ones that also happen to be ITypeExpression — so the AST is the reliable source.
        private static IEnumerable<IClassName> GetAstUsedTraitClassNames(ObjectDeclarationSymbol declaration)
        {
            var body = declaration.DeclaringAstNode switch
            {
                PhpObjectTypeDeclAst { Body: { } classBody } => classBody,
                TyhpdefImportObjectDeclAst { Body: { } tyhpdefBody } => tyhpdefBody,
                _ => null,
            };

            if (body is null)
            {
                yield break;
            }

            foreach (var member in body.GetAllNotNull())
            {
                if (member is not PhpTraitUseAst { TraitNames: { } traitNames })
                {
                    continue;
                }

                foreach (var className in traitNames.GetAllNotNull())
                {
                    yield return className;
                }
            }
        }

        private static ObjectDeclarationSymbol? ResolveClassNameSymbol(
            IClassName className,
            IBaseScope scope,
            SymbolTree symbolTree)
        {
            var name = GetClassNameText(className);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Fully-qualified (`\Foo\Bar`) resolves from the global root. A relative qualified name
            // (`Exceptions\Base`, no leading `\`) must resolve against the enclosing namespace / a
            // leading `use` alias (Prop-init #17) — treating it as absolute (the prior behavior)
            // looks for a top-level `Exceptions` namespace that does not exist and spuriously
            // reports 3017/3018 "unresolved extends/implements type".
            if (name.StartsWith("\\", StringComparison.Ordinal))
            {
                return symbolTree.ResolveQualifiedName(name.TrimStart('\\').Split('\\'), scope, SilentDiagnostics)
                    as ObjectDeclarationSymbol;
            }

            if (name.Contains('\\'))
            {
                return symbolTree.ResolveRelativeName(name.Split('\\'), scope, SilentDiagnostics)
                    as ObjectDeclarationSymbol;
            }

            // A bare `extends`/`implements` name refers to a type in the current namespace. Plain
            // scope-chain resolution only sees the declaring file's own namespace block, so a
            // same-namespace type declared in a different file (the common case across a package)
            // is missed. Fall back to namespace-relative resolution, which searches every file that
            // contributes to the enclosing namespace.
            if (symbolTree.ResolveSymbol(name, scope, SilentDiagnostics) is ObjectDeclarationSymbol direct)
            {
                return direct;
            }

            var resolver = new Binder.Resolution.NameResolver(symbolTree, SilentDiagnostics);
            return resolver.ResolveRelativeName(name.Split('\\'), scope) as ObjectDeclarationSymbol;
        }

        internal static ObjectDeclarationSymbol? TryGetParentDeclaration(
            ObjectDeclarationSymbol child,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            TryResolveParentClass(child, symbolTree, globalScope);

        /// <summary>
        /// The base class and every implemented (or, for an interface, extended) interface declared
        /// directly on <paramref name="child"/>. A type it cannot resolve is skipped rather than
        /// reported: <c>DeclarationRule.CheckInheritanceTargets</c> diagnoses unresolvable bases.
        /// </summary>
        internal static IEnumerable<ObjectDeclarationSymbol> EnumerateDirectAncestors(
            ObjectDeclarationSymbol child,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (TryResolveParentClass(child, symbolTree, globalScope) is { } parent)
            {
                yield return parent;
            }

            foreach (var implemented in ResolveImplementedInterfaces(child, symbolTree, globalScope))
            {
                yield return implemented;
            }
        }

        /// <summary>
        /// Resolves a raw <see cref="IClassName"/> (as written in <c>extends</c>/<c>implements</c>)
        /// against <paramref name="context"/>'s scope — the same path
        /// <see cref="TryGetParentDeclaration"/> uses for AST fallbacks.
        /// </summary>
        internal static ObjectDeclarationSymbol? TryResolveClassName(
            IClassName className,
            ObjectDeclarationSymbol context,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var scope = context.ContainingScope ?? globalScope;
            return ResolveClassNameSymbol(className, scope, symbolTree);
        }

        private static ObjectDeclarationSymbol? TryResolveParentClass(
            ObjectDeclarationSymbol child,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var scope = child.ContainingScope ?? globalScope;

            if (child.ExtendsType is { } extendsAst &&
                symbolTree.ResolveType(extendsAst, scope, SilentDiagnostics) is ObjectDeclarationSymbol resolvedFromType)
            {
                return resolvedFromType;
            }

            // `extends` is parsed as a raw IClassName (not an ITypeExpression), so the symbol's
            // ExtendsType is usually null. Fall back to the declaring AST for both regular and
            // imported (.tyhpdef) declarations.
            return GetAstExtendsClassName(child) is { } parentClassName
                ? ResolveClassNameSymbol(parentClassName, scope, symbolTree)
                : null;
        }

        private static IClassName? GetAstExtendsClassName(ObjectDeclarationSymbol child) =>
            child.DeclaringAstNode switch
            {
                PhpObjectTypeDeclAst { Extends: { } className } => className,
                TyhpStructDeclAst { Extends: { } className } => className,
                TyhpdefImportObjectDeclAst { Extends: IClassName className } => className,
                _ => null,
            };

        internal static string? GetClassNameText(IClassName className) =>
            className switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => className.Identifier,
            };

        private static bool AreGenericBasesEqual(GenericCheckedType left, GenericCheckedType right) =>
            AreTypesEqualCore(left.BaseType, right.BaseType, new HashSet<(ICheckedType, ICheckedType)>())
            || AreEquivalentCallableReturnUtilities(left.BaseType, right.BaseType);

        private static bool AreGenericArgumentsCompatible(
            GenericCheckedType source,
            GenericCheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<(ICheckedType, ICheckedType)> visited,
            bool forAssignability)
        {
            if (source.TypeArguments.Count != target.TypeArguments.Count)
            {
                return false;
            }

            var baseDecl = TryGetObjectDeclaration(source.BaseType);
            var genericParams = baseDecl?.GenericParameters;

            // Built-in `array`/`iterable` carry no declared variance, but they are value types
            // (copied on assignment), so their key/value arguments are sound to treat covariantly
            // for assignability. This lets, e.g., `array<int, T>` (a variadic) satisfy
            // `array<int|string, T>`.
            var arrayLikeCovariant = forAssignability
                && (IsArrayLikeType(source.BaseType) || IsIterableType(source.BaseType));

            for (var i = 0; i < source.TypeArguments.Count; i++)
            {
                var sourceArg = source.TypeArguments[i];
                var targetArg = target.TypeArguments[i];
                var variance = arrayLikeCovariant
                    ? TypeVariance.Covariant
                    : genericParams is not null && i < genericParams.Count
                        ? genericParams[i].Variance
                        : TypeVariance.Invariant;

                var compatible = variance switch
                {
                    TypeVariance.Covariant => IsAssignableToCore(sourceArg, targetArg, symbolTree, globalScope, visited),
                    TypeVariance.Contravariant => IsAssignableToCore(targetArg, sourceArg, symbolTree, globalScope, visited),
                    _ => AreTypesEqualCore(sourceArg, targetArg, new HashSet<(ICheckedType, ICheckedType)>()) ||
                         (forAssignability &&
                          ((IsAssignableToCore(sourceArg, targetArg, symbolTree, globalScope, visited) &&
                            IsAssignableToCore(targetArg, sourceArg, symbolTree, globalScope, visited)) ||
                           IsCovariantToMixedGenericArg(sourceArg, targetArg))),
                };

                if (!compatible)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Carve-out for user-generic invariance: <c>G&lt;T&gt;</c> may assign to <c>G&lt;mixed&gt;</c>
        /// when <c>T</c> is neither <c>void</c> nor <c>never</c>. Heterogeneous bags such as
        /// <c>array&lt;string, PropertyAccessor&lt;mixed&gt;&gt;</c> rely on this; <c>G&lt;string&gt;</c>
        /// still does not assign to <c>G&lt;int&gt;</c>. Explicit <c>in</c>/<c>out</c> variance remains
        /// unimplemented — this is not general covariance.
        /// </summary>
        private static bool IsCovariantToMixedGenericArg(ICheckedType sourceArg, ICheckedType targetArg) =>
            IsMixedType(targetArg) && !IsVoidType(sourceArg) && !IsNeverType(sourceArg);
    }
}

