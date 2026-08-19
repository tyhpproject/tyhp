namespace Tyhp.LanguageServer.Analysis
{
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Services;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Resolution;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
    using Tyhp.TyhpLang.Enum;

    /// <summary>
    /// Locates AST nodes and bound symbols at a source position.
    /// </summary>
    public sealed class SymbolFinder
    {
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        /// <summary>
        /// Walks <paramref name="ast"/> and returns the most specific (deepest) node
        /// whose source range contains the ANTLR coordinates
        /// (<paramref name="line"/> 1-based, <paramref name="column"/> 0-based).
        /// </summary>
        public IBase2Ast? FindNodeAtPosition(SrcFileAst ast, int line, int column)
        {
            ArgumentNullException.ThrowIfNull(ast);
            CollectAtPosition(ast, line, column, out IBase2Ast? node, out _);
            return node;
        }

        /// <summary>
        /// AST ancestor path from the file root to the deepest node that contains
        /// the ANTLR coordinates, or null when nothing matches.
        /// </summary>
        public IReadOnlyList<IBase2Ast>? FindPathAtPosition(SrcFileAst ast, int line, int column)
        {
            ArgumentNullException.ThrowIfNull(ast);
            CollectAtPosition(ast, line, column, out _, out List<IBase2Ast>? path);
            return path;
        }

        /// <summary>
        /// Finds the node at the given position and returns its bound or resolved symbol.
        /// </summary>
        public BaseSymbol? FindSymbolAtPosition(SrcFileAst ast, GlobalScope? scope, int line, int column)
            => this.LookupAtPosition(ast, scope, tree: null, line, column)?.Symbol;

        /// <summary>
        /// Innermost binder scope covering the ANTLR coordinates in <paramref name="ast"/>.
        /// </summary>
        public IBaseScope? FindScopeAtPosition(SrcFileAst ast, GlobalScope? scope, int line, int column)
        {
            ArgumentNullException.ThrowIfNull(ast);
            if (scope is null)
            {
                return null;
            }

            return FindInnermostScope(scope, ast, line, column);
        }

        /// <summary>
        /// Finds an untyped local variable's declaration (and, if the declaration is a plain
        /// <c>$name = expr;</c> assignment, the RHS node to feed to type inference) by name,
        /// anchored at any position within its enclosing function/method — independent of
        /// whether that exact position resolves to a usable AST node. Cursor positions next
        /// to incomplete syntax (e.g. a dangling <c>-&gt;</c> with no member yet) often do not
        /// resolve to the variable reference itself (see <see cref="CollectAtPosition"/>/
        /// <see cref="ContainsPosition"/> limitations for nodes with unrecorded end positions),
        /// so this does not depend on <paramref name="line"/>/<paramref name="column"/>
        /// pointing at the variable — only at some position inside its enclosing callable.
        /// </summary>
        public BaseSymbol? FindLocalVariableByName(
            SrcFileAst ast,
            string variableName,
            int line,
            int column,
            out IBase2Ast? inferredTypeNode)
        {
            ArgumentNullException.ThrowIfNull(ast);
            inferredTypeNode = null;
            string bare = StripDollar(variableName);
            if (string.IsNullOrEmpty(bare) || string.Equals(bare, "this", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            CollectAtPosition(ast, line, column, out _, out List<IBase2Ast>? path);
            if (path is null || path.Count == 0)
            {
                return null;
            }

            BaseSymbol? resolved = FindLocalVariableDeclarationCore(
                bare,
                path,
                out IBase2Ast? declaringNode,
                out List<IBase2Ast>? declaringPath);
            if (declaringNode is not null)
            {
                inferredTypeNode = declaringPath is not null
                    ? FindInferredTypeNode(declaringNode, declaringPath)
                    : declaringNode;
            }

            return resolved;
        }

        /// <summary>
        /// Resolves the symbol (and declaration target) under the cursor, including usage
        /// sites the binder does not record on <see cref="IBase2Ast.BoundSymbol"/>.
        /// </summary>
        public SymbolLookupResult? LookupAtPosition(
            SrcFileAst ast,
            GlobalScope? scope,
            SymbolTree? tree,
            int line,
            int column)
        {
            ArgumentNullException.ThrowIfNull(ast);
            CollectAtPosition(ast, line, column, out IBase2Ast? node, out List<IBase2Ast>? path);
            if (node is null || path is null)
            {
                return null;
            }

            node = PromoteTokenToParent(node, path);
            if (IsNonSemanticNode(node))
            {
                return null;
            }

            IBaseScope? fromScope = scope is null
                ? null
                : FindInnermostScope(scope, ast, line, column);
            NameResolver? resolver = CreateResolver(scope, tree);
            BaseSymbol? resolved = this.TryResolveReference(node, path, fromScope, resolver, scope);

            if (resolved is null)
            {
                resolved = node is PhpVariableAst
                    ? AsVariableSymbol(node.BoundSymbol)
                    : AsUsefulSymbol(node.BoundSymbol) ?? FindBoundSymbolOnAncestors(path);
            }

            if (resolved is null)
            {
                resolved = this.FindLocalVariableDeclaration(node, path, out IBase2Ast? localDecl, out List<IBase2Ast>? declPath);
                if (resolved is null && localDecl is not null)
                {
                    string localFile = FirstNonEmpty(ast.FileName, ast.Identifier);

                    // Untyped `$x = expr` declarations redirect to the RHS (InferAssignment
                    // never types the left-hand variable). Foreach/catch bindings have no RHS,
                    // so keep the cursor node — usage-site inference and the loop/catch
                    // variable's own recorded type both live there.
                    IBase2Ast inferredTypeNode = SelectInferredTypeNode(node, path, localDecl, declPath);

                    return new SymbolLookupResult(
                        node,
                        symbol: null,
                        localDecl,
                        localFile,
                        inferredTypeNode);
                }
            }

            if (resolved is UseIncludeSymbol use && resolver is not null && fromScope is not null)
            {
                BaseSymbol? imported = ResolveImportedSymbol(use, resolver, fromScope);
                if (imported is not null)
                {
                    resolved = imported;
                }
            }

            if (resolved is null)
            {
                return null;
            }

            return new SymbolLookupResult(
                node,
                resolved,
                resolved.DeclaringAstNode,
                resolved.SourceFile,
                FindInferredTypeNode(node, path));
        }

        /// <summary>
        /// Resolves <paramref name="node"/> (already located on <paramref name="path"/>)
        /// without re-walking the AST for a cursor position. Used by semantic tokens
        /// so token identity comes from binder resolution, not text heuristics.
        /// </summary>
        internal BaseSymbol? ResolveNode(
            SrcFileAst ast,
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            GlobalScope? scope,
            SymbolTree? tree)
        {
            ArgumentNullException.ThrowIfNull(ast);
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(path);

            IBaseScope? fromScope = scope is null
                ? null
                : FindInnermostScope(scope, ast, Math.Max(1, node.Line), Math.Max(0, node.Column));
            NameResolver? resolver = CreateResolver(scope, tree);
            BaseSymbol? resolved = this.TryResolveReference(node, path, fromScope, resolver, scope);
            if (resolved is not null)
            {
                return resolved;
            }

            resolved = node is PhpVariableAst
                ? AsVariableSymbol(node.BoundSymbol)
                : AsUsefulSymbol(node.BoundSymbol);
            if (resolved is not null)
            {
                return resolved;
            }

            if (node is PhpVariableAst)
            {
                resolved = this.FindLocalVariableDeclaration(node, path, out _, out _);
            }

            return resolved;
        }

        /// <summary>
        /// The checker only calls <c>InferExpressionType</c> on the right-hand side of a plain
        /// <c>=</c> assignment, not on the assignment target itself (see
        /// <c>TypeInferrer.InferAssignment</c>). Hovering the left-hand variable of
        /// <c>$user = new User();</c> must therefore read the right-hand side's inferred type,
        /// or it shows a stale/unresolved entry (if any) instead of the assigned type.
        /// </summary>
        private static IBase2Ast FindInferredTypeNode(IBase2Ast node, IReadOnlyList<IBase2Ast> path)
        {
            if (node is not PhpVariableAst)
            {
                return node;
            }

            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(path[i], node))
                {
                    continue;
                }

                if (i > 0
                    && path[i - 1] is PhpBinaryOpAst assignment
                    && ReferenceEquals(assignment.Left, node)
                    && string.Equals(assignment.Operator?.ValueString, "=", StringComparison.Ordinal)
                    && assignment.Right is IBase2Ast rightNode)
                {
                    return rightNode;
                }

                break;
            }

            return node;
        }

        /// <summary>
        /// Chooses the AST node whose checker-inferred type should back hover/completion
        /// for an untyped local.
        /// </summary>
        /// <remarks>
        /// Plain <c>$x = expr</c> never records a type on the left-hand variable
        /// (<c>TypeInferrer.InferAssignment</c>), so the declaring assignment's RHS is
        /// used. Foreach/catch bindings have no RHS; the cursor node is kept so usage-site
        /// inference and the binding's own recorded type apply.
        /// </remarks>
        private static IBase2Ast SelectInferredTypeNode(
            IBase2Ast cursorNode,
            IReadOnlyList<IBase2Ast> cursorPath,
            IBase2Ast declaringNode,
            IReadOnlyList<IBase2Ast>? declaringPath)
        {
            IBase2Ast fromCursor = FindInferredTypeNode(cursorNode, cursorPath);
            if (!ReferenceEquals(fromCursor, cursorNode))
            {
                return fromCursor;
            }

            if (declaringPath is not null)
            {
                IBase2Ast fromDecl = FindInferredTypeNode(declaringNode, declaringPath);
                if (!ReferenceEquals(fromDecl, declaringNode))
                {
                    return fromDecl;
                }
            }

            return cursorNode;
        }

        /// <summary>
        /// Returns the AST node that declared <paramref name="symbol"/>.
        /// </summary>
        public IBase2Ast? FindDeclaringNode(BaseSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            return symbol.DeclaringAstNode;
        }

        /// <summary>
        /// Finds AST nodes that refer to <paramref name="symbol"/> across
        /// <paramref name="allAsts"/>. Resolves name, variable, member, and type
        /// usages — not only nodes that already carry <see cref="IBase2Ast.BoundSymbol"/>.
        /// </summary>
        public IReadOnlyList<IBase2Ast> FindReferences(
            BaseSymbol symbol,
            IEnumerable<SrcFileAst> allAsts,
            GlobalScope? scope)
            => this.FindReferences(symbol, allAsts, scope, tree: null)
                .Select(occurrence => occurrence.Node)
                .ToList();

        /// <summary>
        /// Project-wide occurrences of <paramref name="symbol"/>, including read/write
        /// classification and declaration flags.
        /// </summary>
        public IReadOnlyList<SymbolReference> FindReferences(
            BaseSymbol symbol,
            IEnumerable<SrcFileAst> allAsts,
            GlobalScope? scope,
            SymbolTree? tree)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            ArgumentNullException.ThrowIfNull(allAsts);
            return this.CollectSymbolReferences(
                symbol,
                symbol.DeclaringAstNode,
                allAsts,
                scope,
                tree);
        }

        /// <summary>
        /// Occurrences for the symbol (or untyped local) under the cursor.
        /// <c>$this</c> is treated as the implicit receiver, not as the enclosing class.
        /// </summary>
        public IReadOnlyList<SymbolReference> FindReferences(
            SymbolLookupResult lookup,
            IEnumerable<SrcFileAst> allAsts,
            GlobalScope? scope,
            SymbolTree? tree)
        {
            ArgumentNullException.ThrowIfNull(lookup);
            ArgumentNullException.ThrowIfNull(allAsts);

            if (IsThisVariable(lookup.Node))
            {
                return CollectThisReferences(lookup, allAsts);
            }

            if (lookup.Symbol is BaseSymbol symbol)
            {
                return this.CollectSymbolReferences(
                    symbol,
                    lookup.DeclaringNode ?? symbol.DeclaringAstNode,
                    allAsts,
                    scope,
                    tree);
            }

            return CollectLocalNameReferences(lookup, allAsts);
        }

        private static void CollectAtPosition(
            IBase2Ast root,
            int line,
            int column,
            out IBase2Ast? node,
            out List<IBase2Ast>? path)
        {
            IBase2Ast? bestNode = null;
            List<IBase2Ast>? bestPath = null;
            var currentPath = new List<IBase2Ast>();
            Walk(root);
            node = bestNode;
            path = bestPath;

            void Walk(IBase2Ast current)
            {
                if (!ContainsPosition(current, line, column))
                {
                    return;
                }

                currentPath.Add(current);
                bestNode = current;
                bestPath = [.. currentPath];
                foreach (IBase2Ast child in EnumerateChildren(current))
                {
                    Walk(child);
                }

                currentPath.RemoveAt(currentPath.Count - 1);
            }
        }

        private BaseSymbol? TryResolveReference(
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver,
            GlobalScope? globalScope)
        {
            if (node is PhpVariableAst variable)
            {
                return this.ResolveVariable(variable, path, fromScope, resolver);
            }

            if (node is PhpBuiltinTypeAst builtin)
            {
                return ResolveName(
                    FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                    node,
                    path,
                    fromScope,
                    resolver,
                    preferType: true);
            }

            if (node is PhpNameAst or TyhpGenericIdentifierAst or PhpNamedTypeAst)
            {
                string name = GetNameText(node);
                bool preferType = node is PhpNamedTypeAst || IsTypeContext(path);
                return ResolveName(name, node, path, fromScope, resolver, preferType);
            }

            if (node is PhpNewAst newExpr)
            {
                string className = GetNameText(newExpr.ClassName as IBase2Ast);
                return ResolveName(className, node, path, fromScope, resolver, preferType: true);
            }

            if (node is PhpInstanceMemberAccessAst instanceAccess)
            {
                return this.ResolveInstanceMember(
                    instanceAccess,
                    path,
                    fromScope,
                    resolver,
                    GetNameText(instanceAccess.MemberName as IBase2Ast));
            }

            if (node is PhpStaticMemberAccessAst staticAccess)
            {
                return this.ResolveStaticMember(
                    staticAccess,
                    path,
                    fromScope,
                    resolver,
                    GetNameText(staticAccess.Member as IBase2Ast));
            }

            if (node is PhpClassConstantAccessAst constantAccess)
            {
                return this.ResolveStaticMember(
                    constantAccess,
                    path,
                    fromScope,
                    resolver,
                    GetNameText(constantAccess.Member as IBase2Ast));
            }

            if (node is ITypeExpression typeExpr && resolver is not null && fromScope is not null)
            {
                return resolver.ResolveType(typeExpr, fromScope) as BaseSymbol;
            }

            if (node is PhpNamespaceDeclAst nsDecl && globalScope is not null)
            {
                string nsName = FirstNonEmpty(nsDecl.Identifier, nsDecl.ValueString);
                if (!string.IsNullOrEmpty(nsName)
                    && globalScope.TryGetNamespaceScope(nsName, out NamespaceScope? nsScope)
                    && nsScope is not null)
                {
                    return nsScope.DeclarationSymbol as BaseSymbol;
                }

                return AsUsefulSymbol(nsDecl.BoundSymbol);
            }

            _ = globalScope;
            return null;
        }

        private BaseSymbol? ResolveVariable(
            PhpVariableAst variable,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver)
        {
            string? raw = variable.VariableToken?.ValueString
                ?? variable.Identifier
                ?? variable.ValueString;
            if (string.IsNullOrEmpty(raw)
                && variable.VariableExpression is TokenValueAst token)
            {
                raw = token.ValueString;
            }

            if (string.IsNullOrEmpty(raw))
            {
                return AsVariableSymbol(variable.BoundSymbol);
            }

            string bare = StripDollar(raw);
            if (string.Equals(bare, "this", StringComparison.OrdinalIgnoreCase)
                || string.Equals(bare, "self", StringComparison.OrdinalIgnoreCase))
            {
                return FindEnclosingObject(path, fromScope, resolver);
            }

            if (fromScope is not null)
            {
                BaseSymbol? found = LookupVariableInScope(fromScope, raw, bare);
                if (found is not null)
                {
                    return found;
                }
            }

            if (resolver is not null && fromScope is not null)
            {
                if (AsVariableSymbol(resolver.ResolveSymbol(raw, fromScope)) is BaseSymbol byRaw)
                {
                    return byRaw;
                }

                if (AsVariableSymbol(resolver.ResolveSymbol("$" + bare, fromScope)) is BaseSymbol byDollar)
                {
                    return byDollar;
                }
            }

            return AsVariableSymbol(variable.BoundSymbol);
        }

        private BaseSymbol? ResolveName(
            string name,
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver,
            bool preferType)
        {
            if (string.IsNullOrEmpty(name))
            {
                return AsUsefulSymbol(node.BoundSymbol);
            }

            if (IsSelfStaticParent(name))
            {
                if (resolver is not null && fromScope is not null)
                {
                    return resolver.ResolveSelfStaticParent(name, fromScope);
                }

                return FindEnclosingObject(path, fromScope, resolver);
            }

            BaseSymbol? member = this.TryResolveAsMember(name, node, path, fromScope, resolver);
            if (member is not null)
            {
                return member;
            }

            if (node.BoundSymbol is BaseSymbol bound && IsUsefulSymbol(bound))
            {
                return bound;
            }

            if (resolver is null || fromScope is null)
            {
                return null;
            }

            if (preferType)
            {
                BaseSymbol? type = ResolveTypeName(name, fromScope, resolver);
                if (type is not null)
                {
                    return type;
                }
            }

            if (IsCallContext(path, node))
            {
                BaseSymbol? function = ResolveFunctionName(name, fromScope, resolver);
                if (function is not null)
                {
                    return function;
                }
            }

            IBaseSymbol? simple = resolver.ResolveSymbol(name, fromScope);
            if (simple is BaseSymbol simpleSymbol && IsUsefulSymbol(simpleSymbol))
            {
                return simpleSymbol;
            }

            string trimmed = name.TrimStart('\\');
            if (trimmed.Contains('\\', StringComparison.Ordinal))
            {
                string[] segments = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (name.StartsWith('\\'))
                {
                    if (resolver.ResolveQualifiedName(segments) is BaseSymbol qualified)
                    {
                        return qualified;
                    }
                }
                else if (resolver.ResolveRelativeName(segments, fromScope) is BaseSymbol relative)
                {
                    return relative;
                }
            }
            else if (resolver.ResolveRelativeName([trimmed], fromScope) is BaseSymbol sameNs)
            {
                return sameNs;
            }

            if (!preferType)
            {
                BaseSymbol? type = ResolveTypeName(name, fromScope, resolver);
                if (type is not null)
                {
                    return type;
                }

                BaseSymbol? function = ResolveFunctionName(name, fromScope, resolver);
                if (function is not null)
                {
                    return function;
                }
            }

            return resolver.ResolveGenericTypeParameter(StripDollar(name), fromScope);
        }

        private BaseSymbol? TryResolveAsMember(
            string name,
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                IBase2Ast ancestor = path[i];
                if (ancestor is PhpInstanceMemberAccessAst instance
                    && IsSameOrChild(instance.MemberName as IBase2Ast, node))
                {
                    return this.ResolveInstanceMember(instance, path, fromScope, resolver, name);
                }

                if (ancestor is PhpStaticMemberAccessAst staticAccess
                    && IsSameOrChild(staticAccess.Member as IBase2Ast, node))
                {
                    return this.ResolveStaticMember(staticAccess, path, fromScope, resolver, name);
                }

                if (ancestor is PhpClassConstantAccessAst constantAccess
                    && IsSameOrChild(constantAccess.Member as IBase2Ast, node))
                {
                    return this.ResolveStaticMember(constantAccess, path, fromScope, resolver, name);
                }
            }

            return null;
        }

        private BaseSymbol? ResolveInstanceMember(
            IBase2Ast accessNode,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver,
            string memberName)
        {
            if (string.IsNullOrEmpty(memberName) || resolver is null)
            {
                return AsUsefulSymbol(accessNode.BoundSymbol);
            }

            ObjectDeclarationSymbol? owner = this.FindMemberReceiver(accessNode, path, fromScope, resolver);
            if (owner is null)
            {
                return AsUsefulSymbol(accessNode.BoundSymbol);
            }

            IBaseSymbol? method = resolver.ResolveMember(memberName, owner);
            if (method is BaseSymbol methodSymbol and not ObjectPropertySymbol)
            {
                return methodSymbol;
            }

            string propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            if (resolver.ResolveMember(propertyKey, owner) is BaseSymbol property)
            {
                return property;
            }

            return method as BaseSymbol ?? AsUsefulSymbol(accessNode.BoundSymbol);
        }

        private BaseSymbol? ResolveStaticMember(
            IBase2Ast accessNode,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver,
            string memberName)
        {
            if (string.IsNullOrEmpty(memberName) || resolver is null)
            {
                return AsUsefulSymbol(accessNode.BoundSymbol);
            }

            ObjectDeclarationSymbol? owner = this.FindStaticReceiver(accessNode, path, fromScope, resolver);
            if (owner is null)
            {
                return AsUsefulSymbol(accessNode.BoundSymbol);
            }

            if (resolver.ResolveConstant(memberName, owner) is BaseSymbol constant)
            {
                return constant;
            }

            if (resolver.ResolveStaticMember(memberName, owner) is BaseSymbol staticMember)
            {
                return staticMember;
            }

            string propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            if (resolver.ResolveStaticMember(propertyKey, owner) is BaseSymbol staticProperty)
            {
                return staticProperty;
            }

            return AsUsefulSymbol(accessNode.BoundSymbol);
        }

        private ObjectDeclarationSymbol? FindMemberReceiver(
            IBase2Ast accessNode,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver)
        {
            PhpDereferenceableAst? deref = FindAncestor<PhpDereferenceableAst>(path, accessNode);
            IBase2Ast? receiver = deref?.Base as IBase2Ast;
            if (receiver is PhpDereferenceableAst nested)
            {
                receiver = nested.Base as IBase2Ast ?? nested;
            }

            if (receiver is PhpVariableAst variable)
            {
                BaseSymbol? resolved = this.ResolveVariable(variable, path, fromScope, resolver);
                ObjectDeclarationSymbol? fromResolved = ObjectFromSymbol(resolved, resolver, fromScope);
                if (fromResolved is not null)
                {
                    return fromResolved;
                }

                return this.InferObjectFromLocalAssignment(variable, path, fromScope, resolver);
            }

            if (receiver is PhpNameAst name)
            {
                BaseSymbol? resolved = ResolveName(
                    GetNameText(name),
                    name,
                    path,
                    fromScope,
                    resolver,
                    preferType: true);
                return ObjectFromSymbol(resolved, resolver, fromScope);
            }

            return FindEnclosingObject(path, fromScope, resolver);
        }

        /// <summary>
        /// When a receiver is an untyped local (<c>$user = new User(); $user->name</c>), the
        /// binder has no <see cref="VariableSymbol"/> to carry a declared type. Infer the
        /// class from the declaring assignment's right-hand side — the same AST redirect
        /// hover uses — not from CompletionEngine's text heuristics.
        /// </summary>
        private ObjectDeclarationSymbol? InferObjectFromLocalAssignment(
            PhpVariableAst variable,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver)
        {
            this.FindLocalVariableDeclaration(variable, path, out IBase2Ast? localDecl, out List<IBase2Ast>? declPath);
            if (localDecl is null || declPath is null)
            {
                return null;
            }

            IBase2Ast typeNode = FindInferredTypeNode(localDecl, declPath);
            if (typeNode is PhpNewAst newExpr)
            {
                BaseSymbol? created = ResolveName(
                    GetNameText(newExpr.ClassName as IBase2Ast),
                    newExpr,
                    declPath,
                    fromScope,
                    resolver,
                    preferType: true);
                return ObjectFromSymbol(created, resolver, fromScope);
            }

            if (typeNode is PhpNameAst or PhpNamedTypeAst or PhpBuiltinTypeAst)
            {
                BaseSymbol? named = ResolveName(
                    GetNameText(typeNode),
                    typeNode,
                    declPath,
                    fromScope,
                    resolver,
                    preferType: true);
                return ObjectFromSymbol(named, resolver, fromScope);
            }

            if (typeNode.BoundSymbol is BaseSymbol bound)
            {
                return ObjectFromSymbol(bound, resolver, fromScope);
            }

            return null;
        }

        private ObjectDeclarationSymbol? FindStaticReceiver(
            IBase2Ast accessNode,
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver)
        {
            PhpDereferenceableAst? deref = FindAncestor<PhpDereferenceableAst>(path, accessNode);
            IBase2Ast? receiver = deref?.Base as IBase2Ast;
            while (receiver is PhpDereferenceableAst nested)
            {
                receiver = nested.Base as IBase2Ast;
            }

            if (receiver is PhpNameAst or PhpBuiltinTypeAst)
            {
                BaseSymbol? resolved = ResolveName(
                    GetNameText(receiver),
                    receiver,
                    path,
                    fromScope,
                    resolver,
                    preferType: true);
                return ObjectFromSymbol(resolved, resolver, fromScope);
            }

            return FindEnclosingObject(path, fromScope, resolver);
        }

        private BaseSymbol? FindLocalVariableDeclaration(
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            out IBase2Ast? declaringNode,
            out List<IBase2Ast>? declaringPath)
        {
            declaringNode = null;
            declaringPath = null;
            if (node is not PhpVariableAst variable)
            {
                return null;
            }

            string bare = StripDollar(GetVariableRawName(variable));
            if (string.IsNullOrEmpty(bare)
                || string.Equals(bare, "this", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return FindLocalVariableDeclarationCore(bare, path, out declaringNode, out declaringPath);
        }

        private static BaseSymbol? FindLocalVariableDeclarationCore(
            string bare,
            IReadOnlyList<IBase2Ast> path,
            out IBase2Ast? declaringNode,
            out List<IBase2Ast>? declaringPath)
        {
            declaringNode = null;
            declaringPath = null;
            if (path.Count == 0)
            {
                return null;
            }

            IBase2Ast? body = FindEnclosingCallableBody(path);
            IBase2Ast searchRoot = body ?? path[0];
            IBase2Ast? best = null;
            List<IBase2Ast>? bestPath = null;
            var currentPath = new List<IBase2Ast>();
            Walk(searchRoot);
            if (best is null)
            {
                return null;
            }

            declaringNode = best;
            declaringPath = bestPath;
            return AsVariableSymbol(best.BoundSymbol);

            void Walk(IBase2Ast current)
            {
                currentPath.Add(current);

                if (current is PhpVariableAst candidate
                    && string.Equals(StripDollar(GetVariableRawName(candidate)), bare, StringComparison.OrdinalIgnoreCase))
                {
                    if (best is null
                        || ComparePosition(candidate.Line, candidate.Column, best.Line, best.Column) < 0)
                    {
                        best = candidate;
                        bestPath = [.. currentPath];
                    }
                }

                foreach (IBase2Ast child in EnumerateChildren(current))
                {
                    Walk(child);
                }

                currentPath.RemoveAt(currentPath.Count - 1);
            }
        }

        private static BaseSymbol? LookupVariableInScope(IBaseScope fromScope, string raw, string bare)
        {
            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                BaseSymbol? found = FindVariableChild(scope, raw, bare);
                if (found is not null)
                {
                    return found;
                }

                scope = scope.ParentScope;
            }

            return FindVariableInChildScopes(fromScope, raw, bare);
        }

        private static BaseSymbol? FindVariableChild(IBaseScope scope, string raw, string bare)
        {
            foreach (string candidate in new[] { raw, bare, "$" + bare })
            {
                if (scope.FindChildSymbolByName(candidate) is BaseSymbol symbol
                    && symbol is VariableSymbol or SuperGlobalSymbol)
                {
                    return symbol;
                }
            }

            return null;
        }

        private static BaseSymbol? FindVariableInChildScopes(IBaseScope fromScope, string raw, string bare)
        {
            foreach (IBaseScope child in fromScope.GetAllChildScopes())
            {
                if (child is FunctionDeclarationScope
                    or InstanceMethodDeclarationScope
                    or StaticMethodDeclarationScope
                    or AnonymousFunctionScope)
                {
                    continue;
                }

                BaseSymbol? found = FindVariableChild(child, raw, bare)
                    ?? FindVariableInChildScopes(child, raw, bare);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        private static BaseSymbol? AsVariableSymbol(IBaseSymbol? symbol)
            => symbol is VariableSymbol or SuperGlobalSymbol ? (BaseSymbol)symbol : null;

        private static ObjectDeclarationSymbol? ObjectFromSymbol(
            BaseSymbol? symbol,
            NameResolver? resolver,
            IBaseScope? fromScope)
        {
            if (symbol is ObjectDeclarationSymbol obj)
            {
                return obj;
            }

            if (symbol is VariableSymbol variable)
            {
                if (variable.DeclaredType is ITypeExpression type && resolver is not null && fromScope is not null)
                {
                    return resolver.ResolveType(type, fromScope) as ObjectDeclarationSymbol;
                }

                if (variable.DeclaredType is PhpNamedTypeAst { BoundSymbol: ObjectDeclarationSymbol named })
                {
                    return named;
                }
            }

            return null;
        }

        private static ObjectDeclarationSymbol? FindEnclosingObject(
            IReadOnlyList<IBase2Ast> path,
            IBaseScope? fromScope,
            NameResolver? resolver)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i].BoundSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }
            }

            IBaseScope? scope = fromScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }

                scope = scope.ParentScope;
            }

            _ = resolver;
            return null;
        }

        /// <summary>
        /// Resolves a type name (qualified, relative, or simple) against <paramref name="fromScope"/>.
        /// Exposed for completion's text-based receiver-type fallbacks, which parse a class
        /// name out of source text rather than an AST node (see <see cref="FindLocalVariableByName"/>
        /// remarks on incomplete-syntax positions not resolving to a usable node).
        /// </summary>
        public static BaseSymbol? ResolveTypeByName(string name, IBaseScope fromScope, NameResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(fromScope);
            ArgumentNullException.ThrowIfNull(resolver);
            return ResolveTypeName(name, fromScope, resolver);
        }

        private static BaseSymbol? ResolveTypeName(string name, IBaseScope fromScope, NameResolver resolver)
        {
            string trimmed = name.TrimStart('\\');
            if (name.StartsWith('\\'))
            {
                string[] segments = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (resolver.ResolveQualifiedName(segments) is BaseSymbol qualified
                    && IsTypeSymbol(qualified))
                {
                    return qualified;
                }
            }

            if (resolver.ResolveRelativeName([trimmed], fromScope) is BaseSymbol relative
                && IsTypeSymbol(relative))
            {
                return relative;
            }

            if (resolver.ResolveSymbol(trimmed, fromScope) is BaseSymbol simple && IsTypeSymbol(simple))
            {
                return simple;
            }

            return resolver.ResolveGenericTypeParameter(trimmed, fromScope);
        }

        private static BaseSymbol? ResolveFunctionName(string name, IBaseScope fromScope, NameResolver resolver)
        {
            string trimmed = name.TrimStart('\\');
            if (resolver.ResolveSymbol(trimmed, fromScope) is BaseSymbol fromScopeHit
                && (fromScopeHit is FunctionDeclarationSymbol || fromScopeHit is BuiltInFunctionSymbol))
            {
                return fromScopeHit;
            }

            if (resolver.ResolveRelativeName([trimmed], fromScope) is BaseSymbol relative
                && (relative is FunctionDeclarationSymbol || relative is BuiltInFunctionSymbol))
            {
                return relative;
            }

            return null;
        }

        private static BaseSymbol? ResolveImportedSymbol(
            UseIncludeSymbol use,
            NameResolver resolver,
            IBaseScope fromScope)
        {
            if (use.ImportedNameSegments.Length == 0)
            {
                return null;
            }

            if (resolver.ResolveQualifiedName(use.ImportedNameSegments) is BaseSymbol qualified)
            {
                return qualified;
            }

            return resolver.ResolveRelativeName(use.ImportedNameSegments, fromScope) as BaseSymbol;
        }

        private static NameResolver? CreateResolver(GlobalScope? scope, SymbolTree? tree)
        {
            var diagnostics = new DiagnosticBag();
            if (tree is not null)
            {
                return tree.CreateNameResolver(diagnostics);
            }

            if (scope is not null)
            {
                return new NameResolver(scope, diagnostics);
            }

            return null;
        }

        private static IBaseScope? FindInnermostScope(GlobalScope global, SrcFileAst ast, int line, int column)
        {
            FileScope? file = FindFileScope(global, ast);
            IBaseScope best = file ?? (IBaseScope)global;
            int bestDepth = 0;
            Walk((IBaseScope)global, 0);
            return best;

            void Walk(IBaseScope scope, int depth)
            {
                if (file is not null && scope is FileScope otherFile && !ReferenceEquals(otherFile, file))
                {
                    return;
                }

                if (file is not null
                    && scope is NamespaceBlockScope
                    && scope.DeclarationSymbol is NamespaceBlockSymbol nsBlock
                    && nsBlock.OwningFileScope is not null
                    && !ReferenceEquals(nsBlock.OwningFileScope, file))
                {
                    return;
                }

                bool consider = scope is not GlobalScope and not NamespaceScope;
                if (consider
                    && ScopeBelongsToFile(scope, file, ast)
                    && (depth == 0 || ScopeContains(scope, line, column))
                    && depth >= bestDepth)
                {
                    best = scope;
                    bestDepth = depth;
                }

                foreach (IBaseScope child in scope.GetAllChildScopes())
                {
                    Walk(child, depth + 1);
                }
            }
        }

        private static FileScope? FindFileScope(GlobalScope global, SrcFileAst ast)
        {
            foreach (IBaseScope child in ((IBaseScope)global).GetAllChildScopes())
            {
                if (child is FileScope file && FileScopeMatches(file, ast))
                {
                    return file;
                }
            }

            return null;
        }

        private static bool ScopeBelongsToFile(IBaseScope scope, FileScope? file, SrcFileAst ast)
        {
            if (scope is FileScope fileScope)
            {
                return file is not null ? ReferenceEquals(fileScope, file) : FileScopeMatches(fileScope, ast);
            }

            if (scope is NamespaceBlockScope && scope.DeclarationSymbol is NamespaceBlockSymbol nsBlock)
            {
                if (file is not null && nsBlock.OwningFileScope is not null)
                {
                    return ReferenceEquals(nsBlock.OwningFileScope, file);
                }

                if (nsBlock.OwningFileScope is not null)
                {
                    return FileScopeMatches(nsBlock.OwningFileScope, ast);
                }
            }

            if (scope.DeclarationSymbol is BaseSymbol symbol && !string.IsNullOrEmpty(symbol.SourceFile))
            {
                return PathsEqual(symbol.SourceFile, ast.FileName)
                    || PathsEqual(symbol.SourceFile, ast.Identifier);
            }

            return file is not null;
        }

        private static bool FileScopeMatches(FileScope file, SrcFileAst ast)
        {
            return PathsEqual(file.FileName, ast.FileName)
                || PathsEqual(file.FileName, ast.Identifier)
                || PathsEqual(file.SourceFile, ast.FileName)
                || PathsEqual(file.SourceFile, ast.Identifier)
                || PathsEqual(file.FileName, AstCacheService.GetRelativePath(ast.Identifier));
        }

        private static bool ScopeContains(IBaseScope scope, int line, int column)
        {
            if (scope.DeclarationSymbol is not BaseSymbol symbol)
            {
                return AggregateScopeContains(scope, line, column);
            }

            if (symbol.DeclaringAstNode is IBase2Ast node)
            {
                return ContainsPosition(node, line, column);
            }

            if (symbol.Line < 1)
            {
                // Symbols such as CodeBlockSymbol never receive a declaring AST node or
                // Line/Column from the binder (see TyhpBinder.CodeBlocks.cs), so there is
                // no position to trust here. Falling back to "always true" would let this
                // scope match every position in the file. Derive a range from the scope's
                // own bound descendants instead.
                return AggregateScopeContains(scope, line, column);
            }

            int endLine = symbol.EndLine >= 1 ? symbol.EndLine : symbol.Line;
            int endColumn = symbol.EndColumn >= 0 ? symbol.EndColumn : symbol.Column + Math.Max(1, symbol.Name.Length);
            if (ComparePosition(line, column, symbol.Line, Math.Max(0, symbol.Column)) < 0)
            {
                return false;
            }

            return ComparePosition(line, column, endLine, endColumn) < 0;
        }

        /// <summary>
        /// Position-based containment for a scope whose own symbol carries no reliable
        /// range (e.g. <c>CodeBlockScope</c>). Aggregates the min-start/max-end range from
        /// bound descendant symbols and child scopes; if none are found (e.g. a block with
        /// only untyped locals, which the binder does not bind into the scope tree at all),
        /// there is nothing to compare against, so the scope is treated as not matching —
        /// callers then fall back to an enclosing scope with a real range instead of
        /// incorrectly matching an unrelated block elsewhere in the file.
        /// </summary>
        private static bool AggregateScopeContains(IBaseScope scope, int line, int column)
        {
            if (!TryGetAggregateRange(scope, out int startLine, out int startColumn, out int endLine, out int endColumn))
            {
                return false;
            }

            if (ComparePosition(line, column, startLine, startColumn) < 0)
            {
                return false;
            }

            return ComparePosition(line, column, endLine, endColumn) < 0;
        }

        private static bool TryGetAggregateRange(
            IBaseScope scope,
            out int startLine,
            out int startColumn,
            out int endLine,
            out int endColumn)
        {
            int bestStartLine = int.MaxValue;
            int bestStartColumn = 0;
            int bestEndLine = -1;
            int bestEndColumn = 0;
            bool found = false;

            foreach (IBaseSymbol child in scope.GetAllChildSymbols())
            {
                if (child is not BaseSymbol baseSymbol || baseSymbol.Line < 1)
                {
                    continue;
                }

                int childEndLine = baseSymbol.EndLine >= 1 ? baseSymbol.EndLine : baseSymbol.Line;
                int childEndColumn = baseSymbol.EndColumn >= 0
                    ? baseSymbol.EndColumn
                    : baseSymbol.Column + Math.Max(1, baseSymbol.Name.Length);
                Expand(baseSymbol.Line, Math.Max(0, baseSymbol.Column), childEndLine, childEndColumn);
            }

            foreach (IBaseScope child in scope.GetAllChildScopes())
            {
                if (TryGetAggregateRange(child, out int childStartLine, out int childStartColumn, out int childEndLine, out int childEndColumn))
                {
                    Expand(childStartLine, childStartColumn, childEndLine, childEndColumn);
                }
            }

            startLine = bestStartLine;
            startColumn = bestStartColumn;
            endLine = bestEndLine;
            endColumn = bestEndColumn;
            return found;

            void Expand(int candidateStartLine, int candidateStartColumn, int candidateEndLine, int candidateEndColumn)
            {
                found = true;
                if (ComparePosition(candidateStartLine, candidateStartColumn, bestStartLine, bestStartColumn) < 0)
                {
                    bestStartLine = candidateStartLine;
                    bestStartColumn = candidateStartColumn;
                }

                if (ComparePosition(candidateEndLine, candidateEndColumn, bestEndLine, bestEndColumn) > 0)
                {
                    bestEndLine = candidateEndLine;
                    bestEndColumn = candidateEndColumn;
                }
            }
        }

        private static bool PathsEqual(string? left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            if (PathComparer.Equals(left, right))
            {
                return true;
            }

            try
            {
                return PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
            }
            catch (Exception ex) when (
                ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException)
            {
                return PathComparer.Equals(Path.GetFileName(left), Path.GetFileName(right));
            }
        }

        private static bool IsTypeContext(IReadOnlyList<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                IBase2Ast node = path[i];
                if (node is ITypeExpression or PhpNamedTypeAst or PhpNewAst)
                {
                    return true;
                }

                if (node is PhpFunctionDeclAst or PhpMethodDeclAst or PhpPropertyDeclAst or PhpParameterAst)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCallContext(IReadOnlyList<IBase2Ast> path, IBase2Ast nameNode)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is PhpDereferenceableAst deref
                    && deref.Suffix is PhpCallAst
                    && IsSameOrChild(deref.Base as IBase2Ast, nameNode))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSelfStaticParent(string name)
            => string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase);

        private static bool IsTypeSymbol(BaseSymbol symbol)
            => symbol is ObjectDeclarationSymbol
            or TypeAliasSymbol
            or ObjectTypeAliasSymbol
            or BuiltInTypeSymbol
            or BuiltInUtilityTypeSymbol
            or GenericTypeParameterSymbol;

        private static bool IsUsefulSymbol(BaseSymbol symbol)
        {
            return symbol.SymbolType is not (
                SymbolType.Root
                or SymbolType.File
                or SymbolType.CodeBlock
                or SymbolType.NamespaceBlock
                or SymbolType.Statement
                or SymbolType.DeclareBlock
                or SymbolType.Label);
        }

        private static IBase2Ast PromoteTokenToParent(IBase2Ast node, List<IBase2Ast> path)
        {
            if (node is not TokenValueAst || node is PhpNameAst || node is PhpMagicConstantAst || path.Count < 2)
            {
                return node;
            }

            IBase2Ast parent = path[^2];
            if (parent is PhpVariableAst
                or PhpParameterAst
                or PhpPropertyAst
                or PhpNameAst
                or PhpNamedTypeAst
                or ITypeExpression
                or PhpInstanceMemberAccessAst
                or PhpStaticMemberAccessAst
                or PhpClassConstantAccessAst
                or PhpNewAst
                or PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpObjectTypeDeclAst
                or PhpConstDeclAst
                or PhpNamespaceDeclAst)
            {
                return parent;
            }

            return node;
        }

        private static bool IsNonSemanticNode(IBase2Ast node)
        {
            if (node is PhpStatementBlockAst or SrcFileAst or PhpTopStatementListAst)
            {
                return true;
            }

            if (node is TokenValueAst token
                && node is not PhpNameAst
                && node is not PhpMagicConstantAst)
            {
                string text = token.ValueString ?? string.Empty;
                if (text.Length > 0 && !char.IsLetterOrDigit(text[0]) && text[0] != '_' && text[0] != '$')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The binder records type resolutions on <see cref="PhpNamedTypeAst"/>, not on the
        /// inner <see cref="PhpNameAst"/> token. Walk wrappers only — skip enclosing
        /// declarations so F12 on a return type does not jump to the function itself.
        /// </summary>
        private static BaseSymbol? FindBoundSymbolOnAncestors(IReadOnlyList<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                IBase2Ast ancestor = path[i];
                if (ancestor is PhpFunctionDeclAst
                    or PhpMethodDeclAst
                    or PhpObjectTypeDeclAst
                    or PhpPropertyDeclAst
                    or PhpParameterAst
                    or PhpConstDeclAst
                    or SrcFileAst
                    or PhpTopStatementListAst
                    or PhpStatementBlockAst)
                {
                    continue;
                }

                BaseSymbol? bound = AsUsefulSymbol(ancestor.BoundSymbol);
                if (bound is not null)
                {
                    return bound;
                }
            }

            return null;
        }

        private static BaseSymbol? AsUsefulSymbol(IBaseSymbol? symbol)
            => symbol is BaseSymbol baseSymbol && IsUsefulSymbol(baseSymbol) ? baseSymbol : null;

        /// <summary>
        /// Display name for an AST node, including <see cref="PhpVariableAst"/> names that
        /// live on <see cref="PhpVariableAst.VariableToken"/> rather than
        /// <see cref="IBase2Ast.ValueString"/>.
        /// </summary>
        internal static string GetDisplayName(IBase2Ast? node) => GetNameText(node);

        private static string GetNameText(IBase2Ast? node)
        {
            return node switch
            {
                PhpNamedTypeAst named => GetNameText(named.Name as IBase2Ast),
                PhpNameAst name => FirstNonEmpty(name.ValueString, name.Identifier),
                PhpBuiltinTypeAst builtin => FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                PhpVariableAst variable => GetVariableRawName(variable),
                PhpParameterAst parameter => FirstNonEmpty(parameter.ValueString, parameter.Identifier, parameter.Name),
                PhpEnumCaseAst enumCase => FirstNonEmpty(
                    GetNameText(enumCase.Name),
                    enumCase.Identifier),
                null => string.Empty,
                _ => FirstNonEmpty(node.ValueString, node.Identifier),
            };
        }

        private static string GetVariableRawName(PhpVariableAst variable)
        {
            string? raw = variable.VariableToken?.ValueString
                ?? variable.Identifier
                ?? variable.ValueString;
            if (string.IsNullOrEmpty(raw) && variable.VariableExpression is PhpVariableAst inner
                && !ReferenceEquals(inner, variable))
            {
                return GetVariableRawName(inner);
            }

            if (string.IsNullOrEmpty(raw) && variable.VariableExpression is TokenValueAst token)
            {
                raw = token.ValueString;
            }

            return raw ?? string.Empty;
        }

        private static string StripDollar(string name)
            => name.StartsWith('$') ? name[1..] : name;

        private static IBase2Ast? FindEnclosingCallableBody(IReadOnlyList<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is PhpFunctionDeclAst function)
                {
                    return function.Body as IBase2Ast ?? function;
                }

                if (path[i] is PhpMethodDeclAst method)
                {
                    return method.Body as IBase2Ast ?? method;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(IReadOnlyList<IBase2Ast> path, IBase2Ast inner)
            where T : class, IBase2Ast
        {
            bool seenInner = false;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(path[i], inner))
                {
                    seenInner = true;
                    continue;
                }

                if (seenInner && path[i] is T match)
                {
                    return match;
                }
            }

            return null;
        }

        private static bool IsSameOrChild(IBase2Ast? root, IBase2Ast node)
        {
            if (root is null)
            {
                return false;
            }

            if (ReferenceEquals(root, node))
            {
                return true;
            }

            foreach (IBase2Ast child in EnumerateChildren(root))
            {
                if (IsSameOrChild(child, node))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private IReadOnlyList<SymbolReference> CollectSymbolReferences(
            BaseSymbol symbol,
            IBase2Ast? declaringNode,
            IEnumerable<SrcFileAst> allAsts,
            GlobalScope? scope,
            SymbolTree? tree)
        {
            NameResolver? resolver = CreateResolver(scope, tree);
            var matches = new List<SymbolReference>();
            var seen = new HashSet<(string File, int Line, int Column)>();

            foreach (SrcFileAst fileAst in allAsts)
            {
                if (fileAst is null)
                {
                    continue;
                }

                var path = new List<IBase2Ast>();
                Walk(fileAst);

                void Walk(IBase2Ast current)
                {
                    path.Add(current);
                    if (this.TryMatchReference(current, path, symbol, fileAst, scope, resolver, declaringNode, out SymbolReference? match)
                        && match is not null
                        && seen.Add(OccurrenceKey(match)))
                    {
                        matches.Add(match);
                    }

                    foreach (IBase2Ast child in EnumerateChildren(current))
                    {
                        Walk(child);
                    }

                    path.RemoveAt(path.Count - 1);
                }
            }

            return matches;
        }

        private bool TryMatchReference(
            IBase2Ast node,
            List<IBase2Ast> path,
            BaseSymbol symbol,
            SrcFileAst fileAst,
            GlobalScope? globalScope,
            NameResolver? resolver,
            IBase2Ast? declaringNode,
            out SymbolReference? match)
        {
            match = null;
            IBase2Ast occurrence = PreferIdentifierNode(node);
            if (IsSkippedClassKeyword(occurrence, symbol))
            {
                return false;
            }

            bool boundHit = node.BoundSymbol is BaseSymbol bound && RefersTo(bound, symbol);
            bool resolvedHit = false;
            if (!boundHit && IsReferenceCandidate(node))
            {
                IBaseScope? fromScope = globalScope is null
                    ? null
                    : FindInnermostScope(globalScope, fileAst, Math.Max(1, node.Line), Math.Max(0, node.Column));
                BaseSymbol? resolved = this.TryResolveReference(node, path, fromScope, resolver, globalScope);
                resolvedHit = resolved is not null && RefersTo(resolved, symbol);
            }

            if (!boundHit && !resolvedHit)
            {
                return false;
            }

            bool isDeclaration = IsDeclarationOccurrence(occurrence, node, declaringNode);
            match = new SymbolReference(
                occurrence,
                fileAst,
                ClassifyKind(occurrence, path, isDeclaration),
                isDeclaration);
            return true;
        }

        /// <summary>
        /// True when the callable that contains <paramref name="anchor"/> already has a
        /// variable named <paramref name="bareName"/> (other than <paramref name="exceptNode"/>).
        /// </summary>
        internal static bool EnclosingCallableHasVariableName(
            SrcFileAst ast,
            IBase2Ast anchor,
            string bareName,
            IBase2Ast? exceptNode)
        {
            ArgumentNullException.ThrowIfNull(ast);
            ArgumentNullException.ThrowIfNull(anchor);
            if (string.IsNullOrEmpty(bareName))
            {
                return false;
            }

            IBase2Ast searchRoot = FindEnclosingCallableNode(ast, anchor) ?? ast;
            return ContainsVariableName(searchRoot, bareName, exceptNode);
        }

        private static bool ContainsVariableName(IBase2Ast root, string bareName, IBase2Ast? exceptNode)
        {
            if (root is PhpVariableAst variable
                && !ReferenceEquals(variable, exceptNode)
                && !ReferenceEquals(variable.VariableToken, exceptNode)
                && string.Equals(StripDollar(GetVariableRawName(variable)), bareName, StringComparison.OrdinalIgnoreCase)
                && !IdentifierSyntax.IsThisName(bareName))
            {
                return true;
            }

            foreach (IBase2Ast child in EnumerateChildren(root))
            {
                if (ContainsVariableName(child, bareName, exceptNode))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<SymbolReference> CollectLocalNameReferences(
            SymbolLookupResult lookup,
            IEnumerable<SrcFileAst> allAsts)
        {
            string bare = StripDollar(GetNameText(lookup.DeclaringNode ?? lookup.Node));
            if (string.IsNullOrEmpty(bare) || IdentifierSyntax.IsThisName(bare))
            {
                return [];
            }

            SrcFileAst? home = FindOwningFile(lookup.DeclaringNode ?? lookup.Node, allAsts);
            if (home is null)
            {
                return [];
            }

            IBase2Ast? callable = FindEnclosingCallableNode(home, lookup.DeclaringNode ?? lookup.Node);
            IBase2Ast searchRoot = callable ?? (IBase2Ast)home;
            var matches = new List<SymbolReference>();
            var path = new List<IBase2Ast>();
            Walk(searchRoot);
            return matches;

            void Walk(IBase2Ast current)
            {
                path.Add(current);
                if (current is PhpVariableAst variable
                    && string.Equals(StripDollar(GetVariableRawName(variable)), bare, StringComparison.OrdinalIgnoreCase)
                    && !IdentifierSyntax.IsThisName(GetVariableRawName(variable)))
                {
                    bool isDeclaration = lookup.DeclaringNode is not null
                        && (ReferenceEquals(current, lookup.DeclaringNode)
                            || (current.Line == lookup.DeclaringNode.Line
                                && current.Column == lookup.DeclaringNode.Column));
                    matches.Add(new SymbolReference(
                        PreferIdentifierNode(current),
                        home,
                        ClassifyKind(current, path, isDeclaration),
                        isDeclaration));
                }

                foreach (IBase2Ast child in EnumerateChildren(current))
                {
                    Walk(child);
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        private static IReadOnlyList<SymbolReference> CollectThisReferences(
            SymbolLookupResult lookup,
            IEnumerable<SrcFileAst> allAsts)
        {
            SrcFileAst? home = FindOwningFile(lookup.Node, allAsts);
            if (home is null)
            {
                return [];
            }

            IBase2Ast? typeDecl = FindEnclosingObjectNode(home, lookup.Node);
            IBase2Ast searchRoot = typeDecl ?? (IBase2Ast)home;
            var matches = new List<SymbolReference>();
            var path = new List<IBase2Ast>();
            Walk(searchRoot);
            return matches;

            void Walk(IBase2Ast current)
            {
                path.Add(current);
                if (IsThisVariable(current))
                {
                    matches.Add(new SymbolReference(
                        PreferIdentifierNode(current),
                        home,
                        ClassifyKind(current, path, isDeclaration: false),
                        isDeclaration: false));
                }

                foreach (IBase2Ast child in EnumerateChildren(current))
                {
                    Walk(child);
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        /// <summary>
        /// The identifier-bearing node to highlight or rename, rather than a whole
        /// declaration/call AST.
        /// </summary>
        internal static IBase2Ast PreferIdentifierNode(IBase2Ast node)
        {
            return node switch
            {
                PhpInstanceMemberAccessAst instance when instance.MemberName is IBase2Ast member => member,
                PhpStaticMemberAccessAst staticAccess when staticAccess.Member is IBase2Ast member => member,
                PhpClassConstantAccessAst constant when constant.Member is IBase2Ast member => member,
                PhpNamedTypeAst named when named.Name is IBase2Ast name => name,
                PhpNewAst created when created.ClassName is IBase2Ast className => className,
                PhpVariableAst variable when variable.VariableToken is IBase2Ast token => token,
                PhpBuiltinTypeAst builtin => builtin,
                _ => node,
            };
        }

        private static bool IsReferenceCandidate(IBase2Ast node)
        {
            return node is PhpVariableAst
                or PhpNameAst
                or PhpNamedTypeAst
                or PhpBuiltinTypeAst
                or PhpNewAst
                or PhpInstanceMemberAccessAst
                or PhpStaticMemberAccessAst
                or PhpClassConstantAccessAst
                or PhpFunctionDeclAst
                or PhpMethodDeclAst
                or PhpObjectTypeDeclAst
                or PhpPropertyAst
                or PhpParameterAst
                or PhpConstDeclAst
                or PhpNamespaceDeclAst
                or TyhpGenericIdentifierAst;
        }

        private static bool IsSkippedClassKeyword(IBase2Ast node, BaseSymbol symbol)
        {
            if (symbol is not ObjectDeclarationSymbol)
            {
                return false;
            }

            string text = GetNameText(node);
            return IdentifierSyntax.IsSelfStaticParent(text) || IdentifierSyntax.IsThisName(text);
        }

        private static bool IsDeclarationOccurrence(IBase2Ast occurrence, IBase2Ast walked, IBase2Ast? declaringNode)
        {
            if (declaringNode is null)
            {
                return walked is PhpFunctionDeclAst
                    or PhpMethodDeclAst
                    or PhpObjectTypeDeclAst
                    or PhpPropertyAst
                    or PhpParameterAst
                    or PhpConstDeclAst
                    or PhpNamespaceDeclAst;
            }

            if (ReferenceEquals(walked, declaringNode) || ReferenceEquals(occurrence, declaringNode))
            {
                return true;
            }

            IBase2Ast declaringId = PreferIdentifierNode(declaringNode);
            return ReferenceEquals(occurrence, declaringId)
                || (occurrence.Line == declaringNode.Line
                    && occurrence.Column == declaringNode.Column
                    && walked is PhpFunctionDeclAst
                        or PhpMethodDeclAst
                        or PhpObjectTypeDeclAst
                        or PhpPropertyAst
                        or PhpParameterAst
                        or PhpConstDeclAst);
        }

        private static SymbolReferenceKind ClassifyKind(
            IBase2Ast node,
            IReadOnlyList<IBase2Ast> path,
            bool isDeclaration)
        {
            if (IsAssignmentTarget(node, path) || IsIncrementTarget(node, path))
            {
                return SymbolReferenceKind.Write;
            }

            if (isDeclaration)
            {
                return DeclarationHasInitializer(node, path)
                    ? SymbolReferenceKind.Write
                    : SymbolReferenceKind.Text;
            }

            return SymbolReferenceKind.Read;
        }

        private static bool IsAssignmentTarget(IBase2Ast node, IReadOnlyList<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is PhpBinaryOpAst assignment
                    && IsAssignmentOperator(assignment.Operator?.ValueString)
                    && assignment.Left is IBase2Ast left
                    && IsSameOrChild(left, node))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIncrementTarget(IBase2Ast node, IReadOnlyList<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is PhpUnaryOpAst unary
                    && IsIncrementOperator(unary.Operator?.ValueString)
                    && unary.Operand is IBase2Ast operand
                    && IsSameOrChild(operand, node))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAssignmentOperator(string? op)
            => op is "=" or "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**="
                or "&=" or "|=" or "^=" or "<<=" or ">>=" or "??=";

        private static bool IsIncrementOperator(string? op)
            => op is "++" or "--";

        private static bool DeclarationHasInitializer(IBase2Ast node, IReadOnlyList<IBase2Ast> path)
        {
            if (node is PhpParameterAst parameter)
            {
                return parameter.DefaultValue is not null;
            }

            if (node is PhpPropertyAst property)
            {
                return property.DefaultValue is not null;
            }

            if (node is PhpConstDeclAst)
            {
                return true;
            }

            return IsAssignmentTarget(node, path);
        }

        private static bool IsThisVariable(IBase2Ast node)
        {
            if (node is PhpVariableAst variable)
            {
                return IdentifierSyntax.IsThisName(GetVariableRawName(variable));
            }

            string text = GetNameText(node);
            return text.StartsWith('$') && IdentifierSyntax.IsThisName(text);
        }

        private static SrcFileAst? FindOwningFile(IBase2Ast node, IEnumerable<SrcFileAst> allAsts)
        {
            if (node.OwningFile is SrcFileAst owned)
            {
                return owned;
            }

            foreach (SrcFileAst file in allAsts)
            {
                if (file is not null && IsSameOrChild(file, node))
                {
                    return file;
                }
            }

            return allAsts.FirstOrDefault();
        }

        private static IBase2Ast? FindEnclosingCallableNode(SrcFileAst ast, IBase2Ast target)
        {
            IBase2Ast? found = null;
            var path = new List<IBase2Ast>();
            Walk(ast);
            return found;

            void Walk(IBase2Ast current)
            {
                path.Add(current);
                if (ReferenceEquals(current, target))
                {
                    found = FindEnclosingCallableBody(path) ?? FindEnclosingCallableDeclaration(path);
                    return;
                }

                foreach (IBase2Ast child in EnumerateChildren(current))
                {
                    Walk(child);
                    if (found is not null)
                    {
                        return;
                    }
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        private static IBase2Ast? FindEnclosingCallableDeclaration(IReadOnlyList<IBase2Ast> path)
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] is PhpFunctionDeclAst or PhpMethodDeclAst)
                {
                    return path[i];
                }
            }

            return null;
        }

        private static IBase2Ast? FindEnclosingObjectNode(SrcFileAst ast, IBase2Ast target)
        {
            IBase2Ast? found = null;
            var path = new List<IBase2Ast>();
            Walk(ast);
            return found;

            void Walk(IBase2Ast current)
            {
                path.Add(current);
                if (ReferenceEquals(current, target))
                {
                    for (int i = path.Count - 1; i >= 0; i--)
                    {
                        if (path[i] is PhpObjectTypeDeclAst)
                        {
                            found = path[i];
                            return;
                        }
                    }

                    return;
                }

                foreach (IBase2Ast child in EnumerateChildren(current))
                {
                    Walk(child);
                    if (found is not null)
                    {
                        return;
                    }
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        private static (string File, int Line, int Column) OccurrenceKey(SymbolReference occurrence)
        {
            string file = FirstNonEmpty(occurrence.File.Identifier, occurrence.File.FileName);
            IBase2Ast node = occurrence.Node;
            return (file, node.Line, node.Column);
        }

        private static bool RefersTo(BaseSymbol bound, BaseSymbol symbol)
        {
            if (ReferenceEquals(bound, symbol))
            {
                return true;
            }

            return bound.SymbolType == symbol.SymbolType
                && string.Equals(bound.FullyQualifiedName, symbol.FullyQualifiedName, StringComparison.Ordinal)
                && string.Equals(bound.SourceFile, symbol.SourceFile, StringComparison.Ordinal)
                && bound.Line == symbol.Line
                && bound.Column == symbol.Column;
        }

        private static IEnumerable<IBase2Ast> EnumerateChildren(IBase2Ast node)
        {
            foreach (IBase2Ast? child in node.AstChildren)
            {
                if (child is not null)
                {
                    yield return child;
                }
            }

            foreach (IBase2Ast attribute in node.AstAttributes)
            {
                yield return attribute;
            }
        }

        /// <summary>
        /// True when the ANTLR position falls inside <paramref name="node"/>'s source range.
        /// File roots and nodes without a start line are treated as covering the whole file.
        /// When the end is unknown, the end is estimated from children; if that also fails,
        /// the node matches when its start is at or before the cursor (deepest such node wins).
        /// </summary>
        internal static bool ContainsPosition(IBase2Ast node, int line, int column)
        {
            if (node.Line < 1)
            {
                return true;
            }

            if (ComparePosition(line, column, node.Line, Math.Max(0, node.Column)) < 0)
            {
                return false;
            }

            var (endLine, endColumn) = PositionUtilities.ResolveEnd(node);
            if (endLine < 1)
            {
                return true;
            }

            return ComparePosition(line, column, endLine, endColumn) < 0;
        }

        private static int ComparePosition(int line1, int column1, int line2, int column2)
        {
            int lineCompare = line1.CompareTo(line2);
            return lineCompare != 0 ? lineCompare : column1.CompareTo(column2);
        }
    }
}
