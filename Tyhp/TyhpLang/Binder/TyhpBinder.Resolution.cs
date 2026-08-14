using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        /// <summary>
        /// Maximum scope nesting depth for resolution traversal. Cycles in scope trees
        /// should not occur, but this cap guards against pathologically deep nesting.
        /// </summary>
        private const int MaxScopeNestingDepth = 500;

        private NameResolver? _nameResolver;

        /// <summary>
        /// The name resolver used during the resolution pass. Available after Bind() completes.
        /// </summary>
        public NameResolver? NameResolver => _nameResolver;

        private NameResolver NameResolverRequired =>
            _nameResolver ?? throw new InvalidOperationException(
                "Resolution pass not yet initialized. Call RunResolutionPass() first.");

        /// <summary>
        /// Produces a human-readable name for a type expression for use in diagnostics.
        /// Walks the type-expression AST (unwrapping the <see cref="PhpTypeExpressionAst"/>
        /// wrapper, reading named-type names from their name child, and reconstructing
        /// nullable/union/intersection syntax) rather than reading the wrapper node's own
        /// (always-empty) identifier.
        /// </summary>
        private static string GetTypeDisplayName(ITypeExpression? type)
        {
            switch (type)
            {
                case null:
                    return "";

                case PhpBuiltinTypeAst builtin:
                    return builtin.Identifier ?? "";

                case PhpNamedTypeAst named:
                    return GetTypeNameText(named.Name);

                case PhpTypeExpressionAst expr:
                {
                    var parts = (expr.Types?.GetAllNotNull() ?? Enumerable.Empty<IBase2Ast>())
                        .OfType<ITypeExpression>()
                        .Select(GetTypeDisplayName)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();

                    var separator = expr.TypeKind switch
                    {
                        PhpTypeKind.Union => "|",
                        PhpTypeKind.Intersection => "&",
                        _ => ""
                    };

                    var body = string.Join(separator, parts);
                    return expr.IsNullable && !string.IsNullOrEmpty(body) ? "?" + body : body;
                }

                default:
                    return !string.IsNullOrEmpty(type.Identifier) ? type.Identifier : (type.ValueString ?? "");
            }
        }

        /// <summary>Reads the textual name from a named-type's name node (Identifier, then ValueString).</summary>
        private static string GetTypeNameText(IBase2Ast? nameNode)
        {
            if (nameNode == null)
            {
                return "";
            }

            return !string.IsNullOrEmpty(nameNode.Identifier) ? nameNode.Identifier : (nameNode.ValueString ?? "");
        }

        /// <summary>
        /// Extracts the source file name from a scope, falling back to _currentFileName.
        /// </summary>
        private string GetScopeSourceFile(IBaseScope scope)
        {
            return scope switch
            {
                FileScope fs => fs.DeclarationSymbol?.SourceFile ?? _currentFileName,
                _ => _currentFileName
            };
        }

        /// <summary>
        /// Performs Pass 2: walks all scopes and resolves type references on symbols
        /// to their declaring symbols using the NameResolver.
        /// </summary>
        private void RunResolutionPass(SymbolTree? symbolTree = null)
        {
            _nameResolver = symbolTree != null
                ? new NameResolver(symbolTree, _diagnostics)
                : new NameResolver(_globalScope, _diagnostics);

            // Walk all file scopes
            foreach (var childScope in _globalScope.ChildScopes)
            {
                if (childScope is FileScope fileScope)
                {
                    ResolveInScope(fileScope);
                }
                else if (childScope is NamespaceScope nsScope)
                {
                    foreach (var blockScope in nsScope.ChildScopes)
                    {
                        ResolveInScope(blockScope);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves type references on all symbols within a scope, then recurses into child scopes.
        /// </summary>
        /// <remarks>
        /// Child recursion must use <see cref="IBaseScope.GetAllChildScopes"/>, not each scope
        /// kind's typed <c>ChildScopes</c> list. C# generic invariance parks some children (notably
        /// an <see cref="ObjectDeclarationScope"/> parented by a <see cref="CodeBlockScope"/> —
        /// every anonymous class inside a function/method body) in
        /// <c>_additionalChildScopes</c>; only <c>GetAllChildScopes</c> returns both lists.
        /// Per-scope-kind symbol resolution still runs off the concrete scope type below.
        /// </remarks>
        private void ResolveInScope(IBaseScope scope, int depth = 0)
        {
            if (depth > MaxScopeNestingDepth)
            {
                _diagnostics.AddError(MessageCode.BinderUnknownError, GetScopeSourceFile(scope),
                    scope.DeclarationSymbol?.Line ?? 0, scope.DeclarationSymbol?.Column ?? 0,
                    "Maximum scope nesting depth exceeded during resolution");
                return;
            }

            // Resolve type references on symbols in this scope
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                ResolveSymbolTypeReferences(symbol, scope);
            }

            // Per-scope-kind declaration resolution (before recursing into children).
            switch (scope)
            {
                case ObjectDeclarationScope { DeclarationSymbol: ObjectDeclarationSymbol objSymbol }:
                    ResolveObjectDeclarationTypes(objSymbol, scope);
                    break;

                case FunctionDeclarationScope { DeclarationSymbol: FunctionDeclarationSymbol funcSymbol }:
                    ResolveFunctionTypes(funcSymbol, scope);
                    break;

                case InstanceMethodDeclarationScope { DeclarationSymbol: ObjectMethodSymbol methodSym }:
                    ResolveFunctionTypes(methodSym, scope);
                    break;

                case StaticMethodDeclarationScope { DeclarationSymbol: ObjectMethodSymbol methodSym }:
                    ResolveFunctionTypes(methodSym, scope);
                    break;

                case AnonymousFunctionScope { DeclarationSymbol: AnonymousFunctionSymbol anonSymbol }:
                    // Closure return/parameter types must be bound like methods: otherwise the
                    // emitter spells free type parameters as bare PHP names (`fn(): T` → runtime
                    // TypeError looking up `Tyhp\T`). Parameter VariableSymbols also carry the same
                    // DeclaredType AST; resolving here is idempotent and covers ParameterInfo.
                    ResolveAnonymousFunctionTypes(anonSymbol, scope);
                    break;
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                ResolveInScope(child, depth + 1);
            }
        }

        /// <summary>
        /// Resolves return and parameter type references on a closure / arrow function.
        /// Enclosing method and class generics are visible via <paramref name="scope"/>'s parents,
        /// so <c>fn(): T</c> binds <c>T</c> to <see cref="GenericTypeParameterSymbol"/> for emit erasure.
        /// </summary>
        private void ResolveAnonymousFunctionTypes(AnonymousFunctionSymbol anonSymbol, IBaseScope scope)
        {
            if (anonSymbol.ReturnType != null)
            {
                NameResolverRequired.ResolveType(anonSymbol.ReturnType, scope);
            }

            foreach (var param in anonSymbol.Parameters)
            {
                if (param.DeclaredType != null)
                {
                    NameResolverRequired.ResolveType(param.DeclaredType, scope);
                }
            }
        }

        /// <summary>
        /// Resolves type references on a single symbol.
        /// </summary>
        private void ResolveSymbolTypeReferences(IBaseSymbol symbol, IBaseScope scope)
        {
            switch (symbol)
            {
                case ObjectMethodSymbol:
                    // Method-level resolution deferred to InstanceMethodDeclarationScope/StaticMethodDeclarationScope
                    // where method-level generic type parameters are in scope
                    break;

                case ObjectPropertySymbol propSymbol:
                    NameResolverRequired.ResolveType(propSymbol.DeclaredType, scope);
                    break;

                case ObjectConstantSymbol constSymbol:
                    NameResolverRequired.ResolveType(constSymbol.DeclaredType, scope);
                    break;

                case VariableSymbol varSymbol:
                    NameResolverRequired.ResolveType(varSymbol.DeclaredType, scope);
                    break;

                case ConstantSymbol freeConstSymbol:
                    NameResolverRequired.ResolveType(freeConstSymbol.DeclaredType, scope);
                    // Top-level / namespace `const` attributes live on PhpConstDeclListAst
                    // (DeclaringAstNode). Class consts are resolved via ResolveObjectDeclarationTypes
                    // walking body members instead.
                    ResolveDeclarationAttributes(freeConstSymbol.DeclaringAstNode, scope);
                    break;

                case TypeAliasSymbol aliasSymbol:
                    NameResolverRequired.ResolveType(aliasSymbol.AliasedType, scope);
                    ResolveGenericParameterConstraints(aliasSymbol.GenericParameters, scope);
                    break;

                // FunctionDeclarationSymbol: resolution deferred to FunctionDeclarationScope processing
                // where function-level generic type parameters are in scope.

                default:
                    // ObjectDeclarationSymbol, UseIncludeSymbol, DeclareBlockSymbol, etc.
                    // are handled at the scope level in ResolveInScope, not here.
                    break;
            }
        }

        /// <summary>
        /// Resolves type references on an object declaration (extends, implements, generic constraints).
        /// </summary>
        private void ResolveObjectDeclarationTypes(ObjectDeclarationSymbol objSymbol, IBaseScope scope)
        {
            if (objSymbol.ExtendsType != null)
            {
                var resolved = NameResolverRequired.ResolveType(objSymbol.ExtendsType, scope);
                if (resolved == null)
                {
                    AddUnresolvedNameError(
                        MessageCode.BinderUnresolvedExtendsType,
                        objSymbol.SourceFile ?? _currentFileName,
                        objSymbol.ExtendsType.Line,
                        objSymbol.ExtendsType.Column,
                        GetTypeDisplayName(objSymbol.ExtendsType),
                        scope);
                }
            }

            foreach (var implType in objSymbol.ImplementsTypes)
            {
                var resolved = NameResolverRequired.ResolveType(implType, scope);
                if (resolved == null)
                {
                    AddUnresolvedNameError(
                        MessageCode.BinderUnresolvedImplementsType,
                        objSymbol.SourceFile ?? _currentFileName,
                        implType.Line,
                        implType.Column,
                        GetTypeDisplayName(implType),
                        scope);
                }
            }

            if (objSymbol.PendingTyhpdefUseExtensionNamespaces is { Count: > 0 } pendingExt)
            {
                objSymbol.TyhpdefAutoActivatedExtensions = new List<ObjectDeclarationSymbol>();
                foreach (var path in pendingExt)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var trimmed = path.TrimStart('\\');
                    var segments = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0)
                        continue;

                    var resolvedExt = path.StartsWith("\\", StringComparison.Ordinal)
                        ? NameResolverRequired.ResolveQualifiedName(segments)
                        : NameResolverRequired.ResolveRelativeName(segments, scope);

                    if (resolvedExt is not ObjectDeclarationSymbol extSym || !extSym.IsExtension)
                    {
                        _diagnostics.AddError(
                            MessageCode.TyhpdefExtensionNotFound,
                            objSymbol.SourceFile ?? _currentFileName,
                            objSymbol.Line,
                            objSymbol.Column,
                            path);
                        continue;
                    }

                    objSymbol.TyhpdefAutoActivatedExtensions.Add(extSym);
                }

                objSymbol.PendingTyhpdefUseExtensionNamespaces = null;
            }

            ResolveGenericParameterConstraints(objSymbol.GenericParameters, scope);

            // Attribute class names live on AstAttributes of the declaration (and of body members
            // for properties/constants, whose DeclaringAstNode is the individual name, not the
            // attributed list). Resolve them here so AttributeRule can read BoundSymbol.
            ResolveDeclarationAttributes(objSymbol.DeclaringAstNode, scope);
            if (objSymbol.DeclaringAstNode is PhpObjectTypeDeclAst { Body: { } body })
            {
                foreach (var member in body.GetAllNotNull())
                {
                    ResolveDeclarationAttributes(member, scope);
                }
            }
        }

        /// <summary>
        /// Resolves each attribute class name attached to <paramref name="node"/> in
        /// <paramref name="scope"/>. Unresolved names are left unbound — AttributeRule keeps a
        /// name-only fallback for <c>\Attribute</c> / <c>\Override</c> (Override is absent from
        /// the 8.2 ExtCore stub). Also walks property-hook attributes (PHP 8.4+) nested under
        /// property declarations and promoted constructor parameters.
        /// </summary>
        private void ResolveDeclarationAttributes(IBase2Ast? node, IBaseScope scope)
        {
            if (node is null)
            {
                return;
            }

            foreach (var attribute in node.AstAttributes)
            {
                if (attribute is PhpAttributeAst { Name: IExpression nameExpr })
                {
                    NameResolverRequired.ResolveAttributeClassName(nameExpr, scope);
                }
            }

            switch (node)
            {
                case PhpPropertyDeclAst propertyDecl:
                    foreach (var property in propertyDecl.Properties?.GetAllNotNull() ?? [])
                    {
                        ResolvePropertyHookListAttributes(property.Hooks, scope);
                    }

                    break;
                case PhpPropertyAst property:
                    ResolvePropertyHookListAttributes(property.Hooks, scope);
                    break;
                case PhpParameterAst { PropertyHooks: PhpPropertyHookListAst hooks }:
                    ResolvePropertyHookListAttributes(hooks, scope);
                    break;
            }
        }

        private void ResolvePropertyHookListAttributes(PhpPropertyHookListAst? hooks, IBaseScope scope)
        {
            if (hooks is null)
            {
                return;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                ResolveDeclarationAttributes(hook, scope);
            }
        }

        /// <summary>
        /// Resolves type references on a function or method (return type, parameter types, generic constraints).
        /// </summary>
        private void ResolveFunctionTypes(FunctionDeclarationSymbol funcSymbol, IBaseScope scope)
        {
            ResolveDeclarationAttributes(funcSymbol.DeclaringAstNode, scope);

            if (funcSymbol.ReturnType != null)
            {
                var resolved = NameResolverRequired.ResolveType(funcSymbol.ReturnType, scope);
                if (resolved == null)
                {
                    AddUnresolvedNameError(
                        MessageCode.BinderUnresolvedReturnType,
                        funcSymbol.SourceFile ?? _currentFileName,
                        funcSymbol.ReturnType.Line,
                        funcSymbol.ReturnType.Column,
                        GetTypeDisplayName(funcSymbol.ReturnType),
                        scope);
                }
            }

            foreach (var param in funcSymbol.Parameters)
            {
                if (param.DeclaredType != null)
                {
                    var resolved = NameResolverRequired.ResolveType(param.DeclaredType, scope);
                    if (resolved == null)
                    {
                        AddUnresolvedNameError(
                            MessageCode.BinderUnresolvedParameterType,
                            funcSymbol.SourceFile ?? _currentFileName,
                            param.DeclaredType.Line,
                            param.DeclaredType.Column,
                            GetTypeDisplayName(param.DeclaredType),
                            scope);
                    }
                }
            }

            ResolveGenericParameterConstraints(funcSymbol.GenericParameters, scope);
        }

        /// <summary>
        /// Resolves type references on a method (return type, parameter types, generic constraints).
        /// </summary>
        private void ResolveFunctionTypes(ObjectMethodSymbol methodSymbol, IBaseScope scope)
        {
            // Method attributes are also resolved while walking the enclosing object's body; doing
            // it again here is idempotent and covers methods whose DeclaringAstNode was not reached
            // via that walk (e.g. synthesized / extension methods).
            ResolveDeclarationAttributes(methodSymbol.DeclaringAstNode, scope);

            if (methodSymbol.ReturnType != null)
            {
                var resolved = NameResolverRequired.ResolveType(methodSymbol.ReturnType, scope);
                if (resolved == null)
                {
                    AddUnresolvedNameError(
                        MessageCode.BinderUnresolvedReturnType,
                        methodSymbol.SourceFile ?? _currentFileName,
                        methodSymbol.ReturnType.Line,
                        methodSymbol.ReturnType.Column,
                        GetTypeDisplayName(methodSymbol.ReturnType),
                        scope);
                }
            }

            foreach (var param in methodSymbol.Parameters)
            {
                if (param.DeclaredType != null)
                {
                    var resolved = NameResolverRequired.ResolveType(param.DeclaredType, scope);
                    if (resolved == null)
                    {
                        AddUnresolvedNameError(
                            MessageCode.BinderUnresolvedParameterType,
                            methodSymbol.SourceFile ?? _currentFileName,
                            param.DeclaredType.Line,
                            param.DeclaredType.Column,
                            GetTypeDisplayName(param.DeclaredType),
                            scope);
                    }
                }
            }

            if (methodSymbol is ObjectOperatorOverloadMethodSymbol opOv
                && opOv.IsExtensionOperator
                && opOv.PendingExtensionTargetType != null)
            {
                var targetAst = opOv.PendingExtensionTargetType;
                var resolvedTarget = NameResolverRequired.ResolveType(targetAst, scope);
                if (resolvedTarget is ObjectDeclarationSymbol targetClass)
                {
                    opOv.ExtensionTargetSymbol = targetClass;
                    targetClass.ExtensionContributedOperators.Add(opOv);
                }
                else if (resolvedTarget is BuiltInTypeSymbol targetBuiltin)
                {
                    // Documented form: `operator *<string>(self $left, int $right)` — contribute
                    // onto the builtin so call-site rewrite can find the form when the left/right
                    // operand resolves to that scalar/builtin.
                    opOv.ExtensionTargetSymbol = targetBuiltin;
                    targetBuiltin.ExtensionContributedOperators.Add(opOv);
                }
                else
                {
                    AddUnresolvedNameError(
                        MessageCode.ExtensionOperatorTargetNotFound,
                        methodSymbol.SourceFile ?? _currentFileName,
                        targetAst.Line,
                        targetAst.Column,
                        GetTypeDisplayName(targetAst),
                        scope);
                }

                opOv.PendingExtensionTargetType = null;
            }

            ResolveGenericParameterConstraints(methodSymbol.GenericParameters, scope);
        }

        /// <summary>
        /// Resolves constraint and default type references on generic type parameters.
        /// </summary>
        private void ResolveGenericParameterConstraints(List<GenericTypeParameterSymbol>? genericParams, IBaseScope scope)
        {
            if (genericParams == null) return;

            foreach (var gp in genericParams)
            {
                if (gp.Constraint != null)
                {
                    var resolved = NameResolverRequired.ResolveType(gp.Constraint, scope);
                    if (resolved == null)
                    {
                        AddUnresolvedNameError(
                            MessageCode.BinderUnresolvedGenericConstraintType,
                            GetScopeSourceFile(scope),
                            gp.Constraint.Line,
                            gp.Constraint.Column,
                            GetTypeDisplayName(gp.Constraint),
                            scope);
                    }
                }

                if (gp.DefaultType != null)
                {
                    var resolved = NameResolverRequired.ResolveType(gp.DefaultType, scope);
                    if (resolved == null)
                    {
                        AddUnresolvedNameError(
                            MessageCode.BinderUnresolvedGenericDefaultType,
                            GetScopeSourceFile(scope),
                            gp.DefaultType.Line,
                            gp.DefaultType.Column,
                            GetTypeDisplayName(gp.DefaultType),
                            scope);
                    }
                }
            }
        }

        /// <summary>
        /// Reports an unresolved-name diagnostic and attaches a Levenshtein "did you mean"
        /// suggestion when a close in-scope type name exists (Story 14 Phase 3).
        /// </summary>
        private void AddUnresolvedNameError(
            MessageCode code,
            string fileName,
            int line,
            int column,
            string typeName,
            IBaseScope scope)
        {
            var diagnostic = Diagnostic.Error(code, fileName, line, column, [typeName]);
            diagnostic = DidYouMean.Attach(
                diagnostic,
                typeName,
                InScopeNameCandidates.CollectTypeNames(scope));
            _diagnostics.Add(diagnostic);
        }
    }
}
