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
        /// Block-target type nodes that already reported TYHP3016 / TYHP3025 / TYHP3015.
        /// The synthesized <c>$this</c> parameter reuses that node; Pass 2 must not also
        /// report an unresolved parameter type for it.
        /// </summary>
        private readonly HashSet<ITypeExpression> _diagnosedExtensionBlockTargets =
            new(ReferenceEqualityComparer.Instance);

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

                case TyhpObjectShapeAst:
                    return "object{…}";

                case TyhpCallableShapeAst:
                    return "callable(…)";

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
        /// Extracts the source file name from a scope. Prefer the declaration's
        /// <see cref="IBaseSymbol.SourceFile"/> (the tyhpdef / source that owns the
        /// symbol) over <c>_currentFileName</c>, which is the file currently being
        /// walked and misattributes constraint errors onto a later consumer.
        /// </summary>
        private string GetScopeSourceFile(IBaseScope scope)
        {
            for (var current = scope; current != null; current = current.ParentScope)
            {
                var declared = current.DeclarationSymbol?.SourceFile;
                if (!string.IsNullOrEmpty(declared))
                {
                    return declared;
                }

                if (current is FileScope fileScope)
                {
                    return fileScope.SourceFile ?? _currentFileName;
                }
            }

            return _currentFileName;
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

            DetectCircularTypeAliases();
            ApplyGenericRuntimeAttributes();
        }

        /// <summary>
        /// Copies <c>#[\Tyhp\GenericRuntime]</c> named arguments onto symbols after attribute
        /// class names are bound, so consumer emit can dispatch compiled helpers.
        /// </summary>
        private void ApplyGenericRuntimeAttributes()
        {
            ApplyGenericRuntimeAttributesInScope(_globalScope);
        }

        private static void ApplyGenericRuntimeAttributesInScope(IBaseScope scope)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is BaseSymbol baseSymbol)
                {
                    var info = GenericRuntimeAttributeSupport.TryRead(baseSymbol.DeclaringAstNode);
                    if (info is not null)
                    {
                        baseSymbol.GenericRuntime = info;
                    }
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                ApplyGenericRuntimeAttributesInScope(child);
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
        /// Also binds attribute class names on the closure and its parameters via the shared
        /// <see cref="ResolveDeclarationAttributes"/> parameter-list walk (<c>PhpInlineFunctionAst</c>
        /// case), the same Pass 2 walk named functions/methods use, so <c>#[UserAttr]</c> on a closure
        /// parameter is not left unbound. This relies on <see cref="AnonymousFunctionSymbol"/> carrying
        /// its declaring <c>PhpInlineFunctionAst</c>/<c>TyhpAsyncBlockAst</c> node — without that, the
        /// parameter-list walk is unreachable and closure parameter attributes are silently never bound.
        /// </summary>
        private void ResolveAnonymousFunctionTypes(AnonymousFunctionSymbol anonSymbol, IBaseScope scope)
        {
            ResolveDeclarationAttributes(anonSymbol.DeclaringAstNode, scope);

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
                {
                    var previousAlias = NameResolverRequired.GenericAliasParameters;
                    NameResolverRequired.GenericAliasParameters = aliasSymbol.GenericParameters;
                    try
                    {
                        ResolveGenericParameterConstraints(aliasSymbol.GenericParameters, scope);
                        NameResolverRequired.ResolveType(aliasSymbol.AliasedType, scope);
                    }
                    finally
                    {
                        NameResolverRequired.GenericAliasParameters = previousAlias;
                    }

                    ResolveDeclarationAttributes(aliasSymbol.DeclaringAstNode, scope);
                    break;
                }

                case ObjectTypeAliasSymbol objectAliasSymbol:
                {
                    var previousAlias = NameResolverRequired.GenericAliasParameters;
                    NameResolverRequired.GenericAliasParameters = objectAliasSymbol.GenericParameters;
                    try
                    {
                        ResolveGenericParameterConstraints(objectAliasSymbol.GenericParameters, scope);
                        NameResolverRequired.ResolveType(objectAliasSymbol.AliasedType, scope);
                    }
                    finally
                    {
                        NameResolverRequired.GenericAliasParameters = previousAlias;
                    }

                    ResolveDeclarationAttributes(objectAliasSymbol.DeclaringAstNode, scope);
                    break;
                }

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
                else if (resolved is ObjectDeclarationSymbol { IsExtern: true } externExtends)
                {
                    ReportBinderExternInheritance(
                        MessageCode.BinderExternExtendsType,
                        objSymbol.ExtendsType,
                        GetTypeDisplayName(objSymbol.ExtendsType),
                        objSymbol.SourceFile ?? _currentFileName,
                        externExtends);
                }
            }

            foreach (var implType in objSymbol.ImplementsTypes)
            {
                var resolved = NameResolverRequired.ResolveType(implType, scope);
                if (resolved == null)
                {
                    // PHP `use Other\HasEmbeddedView;` then `implements HasEmbeddedView` /
                    // `use HasEmbeddedView;` names the import. Harvest compiles one package
                    // and does not load the imported package, so the target symbol is often
                    // absent. The short name is still resolved. `.tyhp` / tyhpdef keep TYHP3018.
                    var displayName = GetTypeDisplayName(implType);
                    if (string.Equals(implType.LanguageMode, "php", StringComparison.OrdinalIgnoreCase)
                        && NameResolverRequired.HasClassUseImport(displayName, scope))
                    {
                        continue;
                    }

                    AddUnresolvedNameError(
                        MessageCode.BinderUnresolvedImplementsType,
                        objSymbol.SourceFile ?? _currentFileName,
                        implType.Line,
                        implType.Column,
                        displayName,
                        scope);
                }
                else if (resolved is ObjectDeclarationSymbol { IsExtern: true } externImpl)
                {
                    ReportBinderExternInheritance(
                        MessageCode.BinderExternImplementsType,
                        implType,
                        GetTypeDisplayName(implType),
                        objSymbol.SourceFile ?? _currentFileName,
                        externImpl);
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
            ResolveExtensionBlockTarget(objSymbol, scope);

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
        /// the 8.2 ExtCore stub). Also walks nested attribute targets that are not themselves
        /// declaration symbols: method/function/closure/tyhpdef-import/operator/hook parameter
        /// lists, plus property-hook attributes (PHP 8.4+) under property declarations and
        /// promoted constructor parameters.
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
                case PhpMethodDeclAst methodDecl:
                    ResolveParameterListAttributes(methodDecl.Parameters, scope);
                    break;
                case PhpFunctionDeclAst functionDecl:
                    ResolveParameterListAttributes(functionDecl.Parameters, scope);
                    break;
                case TyhpdefImportFunctionDeclAst importFunction:
                    ResolveParameterListAttributes(importFunction.Parameters, scope);
                    break;
                case PhpInlineFunctionAst inlineFunction:
                    ResolveParameterListAttributes(inlineFunction.Parameters, scope);
                    break;
                case PhpPropertyHookAst hook:
                    ResolveParameterListAttributes(hook.Parameters, scope);
                    break;
                case TyhpOperatorOverloadAst op:
                    ResolveDeclarationAttributes(op.LeftParameter, scope);
                    ResolveDeclarationAttributes(op.RightParameter, scope);
                    break;
            }
        }

        private void ResolveParameterListAttributes(PhpParameterListAst? parameters, IBaseScope scope)
        {
            if (parameters is null)
            {
                return;
            }

            foreach (var param in parameters.GetAllNotNull())
            {
                ResolveDeclarationAttributes(param, scope);
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
                    if (resolved == null
                        && !_diagnosedExtensionBlockTargets.Contains(param.DeclaredType))
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
                ResolveExtensionOperatorTarget(opOv, scope);
            }

            ResolveGenericParameterConstraints(methodSymbol.GenericParameters, scope);
        }

        /// <summary>
        /// Resolves a header or nested-group <c>extends</c> target. Operators in that
        /// block are contributed later from <see cref="ResolveExtensionOperatorTarget"/>.
        /// </summary>
        private void ResolveExtensionBlockTarget(ObjectDeclarationSymbol objSymbol, IBaseScope scope)
        {
            var targetAst = objSymbol.PendingExtensionBlockTarget;
            if (targetAst == null)
            {
                return;
            }

            if (!objSymbol.IsExtension && !objSymbol.IsExtensionTargetGroup)
            {
                _diagnosedExtensionBlockTargets.Add(targetAst);
                objSymbol.ExtensionBlockTargetRejectContribution = true;
                _diagnostics.AddErrorFromAst(
                    MessageCode.ExtensionOperatorTargetNotAllowed,
                    targetAst,
                    objSymbol.SourceFile ?? _currentFileName,
                    "Operator target type is only allowed inside extension declarations.");
                return;
            }

            var resolved = NameResolverRequired.ResolveType(targetAst, scope);
            ResolveNestedExtensionTargetTypes(targetAst, scope, 0);
            var namedAlias = resolved is TypeAliasSymbol or ObjectTypeAliasSymbol;
            resolved = UnwrapExtensionBlockTargetAlias(resolved, scope);

            if (resolved is ObjectDeclarationSymbol targetClass)
            {
                objSymbol.ExtensionBlockTargetSymbol = targetClass;
                return;
            }

            if (resolved is BuiltInTypeSymbol targetBuiltin)
            {
                objSymbol.ExtensionBlockTargetSymbol = targetBuiltin;
                if (BuiltInTypeSymbol.IsNonInstantiableExtensionOperatorTarget(targetBuiltin.Name)
                    && BlockDeclaresOperator(objSymbol))
                {
                    _diagnosedExtensionBlockTargets.Add(targetAst);
                    objSymbol.ExtensionBlockTargetRejectContribution = true;
                    _diagnostics.AddErrorFromAst(
                        MessageCode.ExtensionOperatorTargetNotInstantiable,
                        targetAst,
                        objSymbol.SourceFile ?? _currentFileName,
                        targetBuiltin.Name);
                }

                return;
            }

            _diagnosedExtensionBlockTargets.Add(targetAst);
            objSymbol.ExtensionBlockTargetRejectContribution = true;
            // The name resolved to an alias. Target-shape rules (nullable, union,
            // object shape, …) run in the checker against the alias body.
            if (namedAlias)
            {
                return;
            }

            AddUnresolvedNameError(
                MessageCode.ExtensionOperatorTargetNotFound,
                objSymbol.SourceFile ?? _currentFileName,
                targetAst.Line,
                targetAst.Column,
                GetTypeDisplayName(targetAst),
                scope);
        }

        /// <summary>
        /// Follows <see cref="TypeAliasSymbol"/> / <see cref="ObjectTypeAliasSymbol"/>
        /// through <see cref="ObjectShapeSupport.GetAliasedType"/> until the target is
        /// a class or builtin. The written target AST stays the alias so <c>?T</c> and
        /// other alias bodies keep their nullability for <c>$this</c>.
        /// </summary>
        private IBaseSymbol? UnwrapExtensionBlockTargetAlias(IBaseSymbol? resolved, IBaseScope scope)
        {
            var visiting = new HashSet<IBaseSymbol>(ReferenceEqualityComparer.Instance);
            while (resolved is not null && ObjectShapeSupport.GetAliasedType(resolved) is { } aliased)
            {
                if (!visiting.Add(resolved))
                {
                    return null;
                }

                resolved = NameResolverRequired.ResolveType(aliased, scope);
            }

            return resolved;
        }

        private void ResolveExtensionOperatorTarget(ObjectOperatorOverloadMethodSymbol opOv, IBaseScope scope)
        {
            var targetAst = opOv.PendingExtensionTargetType;
            if (targetAst == null)
            {
                return;
            }

            var block = FindExtensionBlockSymbol(scope);
            if (block != null && ReferenceEquals(targetAst, block.PendingExtensionBlockTarget))
            {
                if (!block.ExtensionBlockTargetRejectContribution
                    && block.ExtensionBlockTargetSymbol != null)
                {
                    opOv.ExtensionTargetSymbol = block.ExtensionBlockTargetSymbol;
                    switch (block.ExtensionBlockTargetSymbol)
                    {
                        case ObjectDeclarationSymbol targetClass:
                            targetClass.ExtensionContributedOperators.Add(opOv);
                            break;
                        case BuiltInTypeSymbol targetBuiltin:
                            targetBuiltin.ExtensionContributedOperators.Add(opOv);
                            break;
                    }
                }

                opOv.PendingExtensionTargetType = null;
                return;
            }

            var resolvedTarget = NameResolverRequired.ResolveType(targetAst, scope);
            if (resolvedTarget is ObjectDeclarationSymbol looseClass)
            {
                opOv.ExtensionTargetSymbol = looseClass;
                looseClass.ExtensionContributedOperators.Add(opOv);
            }
            else if (resolvedTarget is BuiltInTypeSymbol looseBuiltin)
            {
                if (BuiltInTypeSymbol.IsNonInstantiableExtensionOperatorTarget(looseBuiltin.Name))
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.ExtensionOperatorTargetNotInstantiable,
                        targetAst,
                        opOv.SourceFile ?? _currentFileName,
                        looseBuiltin.Name);
                }
                else
                {
                    opOv.ExtensionTargetSymbol = looseBuiltin;
                    looseBuiltin.ExtensionContributedOperators.Add(opOv);
                }
            }
            else
            {
                AddUnresolvedNameError(
                    MessageCode.ExtensionOperatorTargetNotFound,
                    opOv.SourceFile ?? _currentFileName,
                    targetAst.Line,
                    targetAst.Column,
                    GetTypeDisplayName(targetAst),
                    scope);
            }

            opOv.PendingExtensionTargetType = null;
        }

        private static ObjectDeclarationSymbol? FindExtensionBlockSymbol(IBaseScope scope)
        {
            for (var current = scope; current != null; current = current.ParentScope)
            {
                if (current.DeclarationSymbol is ObjectDeclarationSymbol obj
                    && obj.PendingExtensionBlockTarget != null)
                {
                    return obj;
                }
            }

            return null;
        }

        private static bool BlockDeclaresOperator(ObjectDeclarationSymbol symbol)
        {
            var members = symbol.DeclaringAstNode switch
            {
                TyhpExtensionDeclAst extension => extension.FunctionList,
                TyhpdefStandaloneExtensionDeclAst extension => extension.FunctionList,
                _ => null,
            };
            if (members == null)
            {
                return false;
            }

            foreach (var member in members.GetAllNotNull())
            {
                if (member is TyhpOperatorOverloadAst)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveNestedExtensionTargetTypes(IBase2Ast? node, IBaseScope scope, int depth)
        {
            if (node == null || depth > 64)
            {
                return;
            }

            if (node is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst genericArgs })
            {
                foreach (var arg in genericArgs.GetAllNotNull())
                {
                    if (arg is ITypeExpression typeArg)
                    {
                        NameResolverRequired.ResolveType(typeArg, scope);
                    }

                    ResolveNestedExtensionTargetTypes(arg, scope, depth + 1);
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child == null)
                {
                    continue;
                }

                if (child is ITypeExpression typeExpr
                    && child is not TyhpObjectShapeAst
                    && child is not TyhpCallableShapeAst
                    && child is not TyhpStructShapeAst)
                {
                    NameResolverRequired.ResolveType(typeExpr, scope);
                }

                ResolveNestedExtensionTargetTypes(child, scope, depth + 1);
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                ResolveNestedExtensionTargetTypes(addon, scope, depth + 1);
            }
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
        /// Errors when file-level or class-level type aliases form a cycle after names are bound.
        /// </summary>
        private void DetectCircularTypeAliases()
        {
            var aliases = new List<IBaseSymbol>();
            CollectTypeAliasSymbols(_globalScope, aliases);

            var dependents = new Dictionary<IBaseSymbol, List<IBaseSymbol>>();
            foreach (var alias in aliases)
            {
                var refs = new HashSet<IBaseSymbol>();
                var body = alias switch
                {
                    TypeAliasSymbol fileAlias => fileAlias.AliasedType,
                    ObjectTypeAliasSymbol objectAlias => objectAlias.AliasedType,
                    _ => null,
                };
                CollectReferencedTypeAliases(body, refs, 0);
                dependents[alias] = refs.ToList();
            }

            var visiting = new HashSet<IBaseSymbol>();
            var visited = new HashSet<IBaseSymbol>();
            foreach (var alias in aliases)
            {
                if (TypeAliasGraphHasCycle(alias, dependents, visiting, visited)
                    && !TypeAliasCycleIsObjectShapeOnly(alias, dependents))
                {
                    ReportCircularTypeAlias(alias);
                }
            }
        }

        private static void CollectTypeAliasSymbols(IBaseScope scope, List<IBaseSymbol> aliases)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is TypeAliasSymbol or ObjectTypeAliasSymbol
                    && IsUserSourceTypeAlias(symbol))
                {
                    aliases.Add(symbol);
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                CollectTypeAliasSymbols(child, aliases);
            }
        }

        /// <summary>
        /// Circular-alias diagnostics apply to source <c>.tyhp</c> aliases. Compiler-embedded
        /// and package tyhpdefs may use mutually recursive type-name helpers that expand with
        /// a visiting set in the checker instead.
        /// </summary>
        private static bool IsUserSourceTypeAlias(IBaseSymbol alias)
        {
            var file = alias.SourceFile ?? "";
            if (file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (alias is BaseSymbol { DeclaringAstNode.LanguageMode: "tyhpdef" })
            {
                return false;
            }

            return true;
        }

        private static void CollectReferencedTypeAliases(IBase2Ast? node, HashSet<IBaseSymbol> refs, int depth)
        {
            if (node is null || depth > 500)
            {
                return;
            }

            if (node.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                refs.Add(node.BoundSymbol);
            }

            foreach (var child in node.AstChildren)
            {
                CollectReferencedTypeAliases(child, refs, depth + 1);
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                CollectReferencedTypeAliases(addon, refs, depth + 1);
            }
        }

        private static bool TypeAliasGraphHasCycle(
            IBaseSymbol start,
            Dictionary<IBaseSymbol, List<IBaseSymbol>> dependents,
            HashSet<IBaseSymbol> visiting,
            HashSet<IBaseSymbol> visited)
        {
            if (visited.Contains(start))
            {
                return false;
            }

            if (!visiting.Add(start))
            {
                return true;
            }

            if (dependents.TryGetValue(start, out var refs))
            {
                foreach (var next in refs)
                {
                    if (TypeAliasGraphHasCycle(next, dependents, visiting, visited))
                    {
                        visiting.Remove(start);
                        return true;
                    }
                }
            }

            visiting.Remove(start);
            visited.Add(start);
            return false;
        }

        /// <summary>
        /// Recursive object-shape aliases (<c>type Node = object { public function parent(): ?Node; }</c>)
        /// are allowed. A cycle is exempt from TYHP3029 only when every participant's RHS
        /// resolves to an object shape — either directly (including <c>Logger &amp; object { … }</c>)
        /// or by referencing another alias that does, e.g. a bare rename (<c>type Foo = Box;</c>)
        /// or a generic instantiation of a shape-producing alias (<c>type Foo = Box&lt;Foo&gt;;</c>).
        /// Plain <c>type A = B; type B = A</c> (never bottoming out at a shape) still errors.
        /// Shares <see cref="ObjectShapeSupport.AliasResolvesToObjectShape(IBaseSymbol?)"/> with the
        /// checker's <c>new</c>/<c>::</c> shape-used-as-class diagnostics so both call sites agree on
        /// what "resolves to a shape" means.
        /// </summary>
        private static bool TypeAliasCycleIsObjectShapeOnly(
            IBaseSymbol start,
            Dictionary<IBaseSymbol, List<IBaseSymbol>> dependents)
        {
            var cycle = new List<IBaseSymbol>();
            var visiting = new HashSet<IBaseSymbol>();
            var stack = new List<IBaseSymbol>();
            if (!TryCollectTypeAliasCycle(start, dependents, visiting, stack, cycle))
            {
                return false;
            }

            return cycle.Count > 0
                && cycle.All(ObjectShapeSupport.AliasResolvesToObjectShape);
        }

        private static bool TryCollectTypeAliasCycle(
            IBaseSymbol node,
            Dictionary<IBaseSymbol, List<IBaseSymbol>> dependents,
            HashSet<IBaseSymbol> visiting,
            List<IBaseSymbol> stack,
            List<IBaseSymbol> cycle)
        {
            if (!visiting.Add(node))
            {
                var start = stack.IndexOf(node);
                if (start < 0)
                {
                    return false;
                }

                cycle.AddRange(stack.Skip(start));
                return true;
            }

            stack.Add(node);
            if (dependents.TryGetValue(node, out var refs))
            {
                foreach (var next in refs)
                {
                    if (TryCollectTypeAliasCycle(next, dependents, visiting, stack, cycle))
                    {
                        return true;
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(node);
            return false;
        }

        private void ReportCircularTypeAlias(IBaseSymbol alias)
        {
            if (alias is BaseSymbol { DeclaringAstNode: { } node })
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderCircularTypeAlias,
                    node,
                    alias.SourceFile,
                    alias.Name);
                return;
            }

            _diagnostics.AddError(
                MessageCode.BinderCircularTypeAlias,
                alias.SourceFile,
                alias.Line,
                alias.Column,
                alias.Name);
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
