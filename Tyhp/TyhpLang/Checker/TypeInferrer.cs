using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Resolves AST type expressions and infers expression types for the checker.
    /// </summary>
    public sealed partial class TypeInferrer : INarrowingResolution
    {
        private readonly SymbolTree _symbolTree;
        private readonly GlobalScope _globalScope;
        private readonly DiagnosticBag _diagnostics;
        private readonly TyhpChecker _checker;

        public TypeInferrer(
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            TyhpChecker checker)
        {
            _symbolTree = symbolTree;
            _globalScope = globalScope;
            _diagnostics = diagnostics;
            _checker = checker;
        }

        public ICheckedType InferExpressionType(IBase2Ast expression, CheckerState state)
        {
            if (expression is null or ErrorAst)
            {
                return CheckedTypes.Unresolved;
            }

            if (_checker.TryGetExpressionType(expression, out var cached) && cached is not null)
            {
                return cached;
            }

            var inferred = InferExpressionTypeCore(expression, state);
            _checker.SetExpressionType(expression, inferred);
            return inferred;
        }

        public ICheckedType ResolveTypeExpression(
            ITypeExpression typeAst,
            CheckerState state,
            bool isReturnTypePosition = false,
            bool isUserTypeDeclaration = true)
        {
            if (typeAst is ErrorAst)
            {
                return CheckedTypes.Unresolved;
            }

            // Type annotations must resolve in the file that wrote them (namespace + `use`
            // imports), not at the access site. Otherwise a struct/class property typed as a
            // short name imported only in its declaring file is mis-resolved when read from
            // another file (e.g. PathNode::$body as ExpressionNode → `\Tyhp\ExpressionNode`).
            var resolveState = state;
            if (state.NameResolutionScope is null
                && TryGetDeclaringFileResolutionScope(typeAst, out var declaringScope)
                && !SharesFileScope(GetResolutionScope(state), declaringScope))
            {
                resolveState = state.Fork();
                resolveState.NameResolutionScope = declaringScope;
            }

            var resolved = ResolveTypeExpressionCore(
                typeAst, resolveState, isReturnTypePosition, isUserTypeDeclaration);
            return TypeComparer.ExpandTypeAliases(
                resolved,
                _symbolTree,
                _globalScope,
                ast => ResolveTypeExpressionCore(
                    ast, resolveState, isReturnTypePosition, isUserTypeDeclaration));
        }

        ICheckedType INarrowingResolution.ResolveExpressionType(IBase2Ast expression, CheckerState state) =>
            InferExpressionType(expression, state);

        ICheckedType INarrowingResolution.ResolveTypeAnnotation(
            ITypeExpression typeAst,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration) =>
            ResolveTypeExpression(typeAst, state, isReturnTypePosition, isUserTypeDeclaration);

        internal IBaseScope GetResolutionScope(CheckerState state)
        {
            if (state.NameResolutionScope is { } overrideScope)
            {
                return overrideScope;
            }

            if (state.EnclosingFunction?.ContainingScope is IBaseScope functionScope)
            {
                return functionScope;
            }

            if (state.EnclosingObject?.ContainingScope is IBaseScope objectScope)
            {
                return objectScope;
            }

            return _globalScope;
        }

        /// <summary>
        /// Scope that should resolve names for a type annotation: the namespace block (preferred)
        /// or file scope of the AST node's <see cref="IBase2Ast.OwningFile"/>.
        /// Namespace blocks are siblings of <see cref="FileScope"/> under global (not children),
        /// linked via <see cref="Binder.Symbols.NamespaceBlockSymbol.OwningFileScope"/>.
        /// </summary>
        private bool TryGetDeclaringFileResolutionScope(IBase2Ast ast, out IBaseScope declaringScope)
        {
            declaringScope = null!;
            var owningFile = ast.OwningFile;
            if (owningFile is null)
            {
                return false;
            }

            var fileName = owningFile.FileName;
            var absolutePath = owningFile.Identifier;
            if (string.IsNullOrEmpty(fileName) && string.IsNullOrEmpty(absolutePath))
            {
                return false;
            }

            FileScope? matchingFile = null;
            foreach (var child in ((IBaseScope)_globalScope).GetAllChildScopes())
            {
                if (child is FileScope fileScope
                    && FileScopeMatchesOwningFile(fileScope, fileName, absolutePath))
                {
                    matchingFile = fileScope;
                    break;
                }
            }

            if (matchingFile is null)
            {
                return false;
            }

            // Prefer the namespace block for this file — that is where per-file `use` imports live.
            foreach (var child in ((IBaseScope)_globalScope).GetAllChildScopes())
            {
                if (child is not NamespaceScope nsScope)
                {
                    continue;
                }

                foreach (var block in nsScope.ChildScopes)
                {
                    if (block is NamespaceBlockScope nsBlock
                        && ReferenceEquals(nsBlock.DeclarationSymbol.OwningFileScope, matchingFile))
                    {
                        declaringScope = nsBlock;
                        return true;
                    }
                }
            }

            declaringScope = matchingFile;
            return true;
        }

        private static bool FileScopeMatchesOwningFile(
            FileScope fileScope,
            string? relativeFileName,
            string? absolutePath)
        {
            if (!string.IsNullOrEmpty(relativeFileName)
                && (string.Equals(fileScope.FileName, relativeFileName, StringComparison.Ordinal)
                    || string.Equals(fileScope.SourceFile, relativeFileName, StringComparison.Ordinal)))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(absolutePath)
                && (string.Equals(fileScope.SourceFile, absolutePath, StringComparison.Ordinal)
                    || string.Equals(fileScope.FileName, absolutePath, StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
        }

        private static bool SharesFileScope(IBaseScope left, IBaseScope right)
        {
            var leftFile = FindEnclosingFileScope(left);
            var rightFile = FindEnclosingFileScope(right);
            if (leftFile is null || rightFile is null)
            {
                return ReferenceEquals(left, right);
            }

            return ReferenceEquals(leftFile, rightFile)
                || string.Equals(leftFile.FileName, rightFile.FileName, StringComparison.Ordinal)
                || string.Equals(leftFile.SourceFile, rightFile.SourceFile, StringComparison.Ordinal);
        }

        /// <summary>
        /// Walks parents for a <see cref="FileScope"/>, or uses
        /// <see cref="Binder.Symbols.NamespaceBlockSymbol.OwningFileScope"/> when under a namespace
        /// (namespace blocks are not parented by their file scope).
        /// </summary>
        private static FileScope? FindEnclosingFileScope(IBaseScope scope)
        {
            for (var current = scope; current is not null; current = current.ParentScope)
            {
                if (current is FileScope fileScope)
                {
                    return fileScope;
                }

                if (current is NamespaceBlockScope
                    && current.DeclarationSymbol is Binder.Symbols.NamespaceBlockSymbol
                    {
                        OwningFileScope: { } owning
                    })
                {
                    return owning;
                }
            }

            return null;
        }

        private static int GetTokenType(TokenValueAst? token) =>
            token?.TokenValue ?? -1;
    }
}
