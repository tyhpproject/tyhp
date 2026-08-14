using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.BuiltIn;
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
        /// Loads tyhpdef files and binds their declarations into the GlobalScope.
        /// This must be called before user code binding so that external type information is available.
        /// </summary>
        private void LoadTyhpdefSymbols()
        {
            var registrar = new TyhpdefSymbolRegistrar(this, _diagnostics);
            _tyhpdefRegistrar = registrar;
            try
            {
                registrar.RegisterAll(BuiltIn.Tyhpdef.GetSourceFiles(_diagnostics, _compilationOptions));
            }
            finally
            {
                _tyhpdefRegistrar = null;
            }
        }

        /// <summary>
        /// Binds a single tyhpdef source file with package provenance metadata.
        /// </summary>
        internal void BindTyhpdefSourceFile(TyhpdefSourceFile source)
        {
            _currentTyhpdefPackageSource = source.PackageSource;
            try
            {
                BindFile(source.Ast);
            }
            finally
            {
                _currentTyhpdefPackageSource = "<tyhpdef>";
            }
        }

        private void TrackTyhpdefSymbol(IBaseSymbol symbol)
        {
            _tyhpdefRegistrar?.TrackSymbol(symbol, _currentTyhpdefPackageSource);
        }

        private bool TryRegisterTyhpdefFunction(
            FunctionDeclarationSymbol symbol,
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope targetScope
        )
        {
            switch (targetScope)
            {
                case FileScope fileScope when fileScope.AddChildSymbol(symbol):
                    TrackTyhpdefSymbol(symbol);
                    return true;

                case NamespaceBlockScope nsBlockScope when nsBlockScope.AddChildSymbol(symbol):
                    TrackTyhpdefSymbol(symbol);
                    return true;

                case FileScope fileScopeDup:
                {
                    var existing = ((IBaseScope)fileScopeDup).FindChildSymbolByName(symbol.Name);
                    if (TryAddTyhpdefFunctionOverload(existing, symbol))
                    {
                        return true;
                    }

                    if (_tyhpdefRegistrar?.TryReportCrossPackageConflict(
                            existing,
                            symbol,
                            funcDecl,
                            _currentTyhpdefPackageSource,
                            _currentFileName) == true)
                    {
                        return false;
                    }

                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefDuplicateDeclaration,
                        funcDecl,
                        _currentFileName,
                        symbol.Name);
                    return false;
                }

                case NamespaceBlockScope nsBlockScopeDup:
                {
                    var existing = ((IBaseScope)nsBlockScopeDup).FindChildSymbolByName(symbol.Name);
                    if (TryAddTyhpdefFunctionOverload(existing, symbol))
                    {
                        return true;
                    }

                    if (_tyhpdefRegistrar?.TryReportCrossPackageConflict(
                            existing,
                            symbol,
                            funcDecl,
                            _currentTyhpdefPackageSource,
                            _currentFileName) == true)
                    {
                        return false;
                    }

                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefDuplicateDeclaration,
                        funcDecl,
                        _currentFileName,
                        symbol.Name);
                    return false;
                }

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    return false;
            }
        }

        private static bool TryAddTyhpdefFunctionOverload(IBaseSymbol? existing, FunctionDeclarationSymbol newSignature)
        {
            if (existing is not FunctionDeclarationSymbol primary)
            {
                return false;
            }

            primary.Overloads.Add(CreateFunctionOverloadSignature(newSignature));
            return true;
        }

        private static FunctionDeclarationSymbol CreateFunctionOverloadSignature(FunctionDeclarationSymbol source)
        {
            var overload = new FunctionDeclarationSymbol(
                source.Name,
                source.DeclaringAstNode,
                source.SourceFile,
                source.Visibility)
            {
                ReturnType = source.ReturnType,
                IsAsync = source.IsAsync,
                IsDeprecated = source.IsDeprecated,
                IsObsolete = source.IsObsolete,
                OriginalPhpName = source.OriginalPhpName,
            };
            overload.Parameters = new List<ParameterInfo>(source.Parameters);
            if (source.GenericParameters.Count > 0)
            {
                overload.GenericParameters = new List<GenericTypeParameterSymbol>(source.GenericParameters);
            }

            return overload;
        }

        private bool TryRegisterTyhpdefTopLevelSymbol(
            BaseSymbol symbol,
            IBase2Ast declaringNode,
            IBaseScope targetScope
        )
        {
            switch (targetScope)
            {
                case FileScope fileScope when fileScope.AddChildSymbol(symbol):
                    TrackTyhpdefSymbol(symbol);
                    return true;

                case NamespaceBlockScope nsBlockScope when symbol is INamespaceBlockScopeSymbol nsSymbol
                    && nsBlockScope.AddChildSymbol(nsSymbol):
                    TrackTyhpdefSymbol(symbol);
                    return true;

                case FileScope fileScopeDup:
                {
                    var existing = ((IBaseScope)fileScopeDup).FindChildSymbolByName(symbol.Name);
                    if (_tyhpdefRegistrar?.TryReportCrossPackageConflict(
                            existing,
                            symbol,
                            declaringNode,
                            _currentTyhpdefPackageSource,
                            _currentFileName) == true)
                    {
                        return false;
                    }

                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefDuplicateDeclaration,
                        declaringNode,
                        _currentFileName,
                        symbol.Name);
                    return false;
                }

                case NamespaceBlockScope nsBlockScopeDup:
                {
                    var existing = ((IBaseScope)nsBlockScopeDup).FindChildSymbolByName(symbol.Name);
                    if (_tyhpdefRegistrar?.TryReportCrossPackageConflict(
                            existing,
                            symbol,
                            declaringNode,
                            _currentTyhpdefPackageSource,
                            _currentFileName) == true)
                    {
                        return false;
                    }

                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefDuplicateDeclaration,
                        declaringNode,
                        _currentFileName,
                        symbol.Name);
                    return false;
                }

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    return false;
            }
        }

        private void BindTyhpdefObjectDecl(TyhpdefImportObjectDeclAst objDecl, IBaseScope parentScope)
        {
            var (originalName, aliasName) = ExtractTyhpdefName(objDecl.NameOrAlias);
            if (string.IsNullOrEmpty(originalName)) return;

            var targetScope = ResolveNamespacedScope(originalName, parentScope, out var shortName);
            var modifiers = ConvertModifiers(objDecl.Modifiers);
            var symbol = new ObjectDeclarationSymbol(shortName, objDecl, _currentFileName, modifiers);

            // Class-level generic parameters (e.g. `class Foo<TValue>`) must be registered so
            // member signatures can resolve references to those type parameters. Depending on the
            // declaration kind, the tyhpdef visitor exposes them either as a "GenericParameters"
            // grammar addon on the name (class declarations) or as the GenericArguments child of a
            // TyhpGenericIdentifierAst name (trait/interface/enum declarations).
            var classGenericList = ExtractTyhpdefObjectGenericList(objDecl.NameOrAlias);
            if (classGenericList != null)
            {
                PopulateGenericParameters(
                    classGenericList,
                    symbol.GenericParameters,
                    _currentFileName,
                    SymbolType.ClassGenericTypeParameter);
            }

            if (objDecl.DeclType?.ValueString != null)
            {
                symbol.ObjectKind = objDecl.DeclType.ValueString.ToLowerInvariant() switch
                {
                    "class" => PhpTypeDeclType.Class,
                    "interface" => PhpTypeDeclType.Interface,
                    "trait" => PhpTypeDeclType.Trait,
                    "enum" => PhpTypeDeclType.Enum,
                    _ => PhpTypeDeclType.Class
                };
            }

            symbol.ExtendsType = objDecl.Extends as ITypeExpression
                ?? (objDecl.Extends is IExpression extendsName
                    ? PhpNamedTypeAst.WrapClassName(extendsName, objDecl)
                    : null);
            if (objDecl.Implements != null)
            {
                foreach (var impl in objDecl.Implements.GetAllNotNull())
                {
                    var typeExpr = AsTypeExpression(impl, objDecl);
                    if (typeExpr is not null)
                    {
                        symbol.ImplementsTypes.Add(typeExpr);
                    }
                }
            }

            symbol.IsDeprecated = objDecl.IsDeprecated;
            symbol.IsObsolete = objDecl.IsObsolete;

            switch (targetScope)
            {
                case FileScope fileScope:
                {
                    if (!TryRegisterTyhpdefTopLevelSymbol(symbol, objDecl, fileScope))
                    {
                        return;
                    }

                    var objScope = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScope);
                    BindTyhpdefObjectBody(objDecl.Body, objScope, symbol);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!TryRegisterTyhpdefTopLevelSymbol(symbol, objDecl, nsBlockScope))
                    {
                        return;
                    }

                    var objScope = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScope);
                    BindTyhpdefObjectBody(objDecl.Body, objScope, symbol);
                    break;
                }

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    break;
            }

            if (!string.IsNullOrEmpty(aliasName))
            {
                CreateTyhpdefAlias(aliasName, originalName, objDecl, parentScope, PhpUseType.Class);
            }
        }

        private void BindTyhpdefFunctionDecl(TyhpdefImportFunctionDeclAst funcDecl, IBaseScope parentScope)
        {
            var (originalName, aliasName) = ExtractTyhpdefName(funcDecl.NameOrAlias);
            if (string.IsNullOrEmpty(originalName)) return;

            // PHP placement for the underlying name (namespace of `\App\foo` in
            // `function \App\foo as bar`). The Tyhp-facing symbol name prefers the `as` alias —
            // same pattern as tyhpdef methods (`ObjectMethodSymbol` + `OriginalPhpName`) and
            // docs/content/tyhpdef_importAliases.md (only the aliased name is visible in Tyhp).
            var phpTargetScope = ResolveNamespacedScope(originalName, parentScope, out var phpShortName);
            var hasAlias = !string.IsNullOrEmpty(aliasName)
                && !string.Equals(aliasName, phpShortName, StringComparison.OrdinalIgnoreCase);

            string symbolName;
            IBaseScope targetScope;
            string? originalPhpName = null;
            if (hasAlias)
            {
                symbolName = aliasName!;
                // Alias is a Tyhp-global short name (file scope), not nested under the PHP namespace.
                targetScope = parentScope;
                originalPhpName = originalName.TrimStart('\\');
            }
            else
            {
                symbolName = phpShortName;
                targetScope = phpTargetScope;
            }

            var modifiers = ConvertModifiers(null);
            var symbol = new FunctionDeclarationSymbol(symbolName, funcDecl, _currentFileName, modifiers);

            symbol.ReturnType = funcDecl.ReturnType;
            symbol.IsAsync = funcDecl.IsAsync;
            symbol.IsDeprecated = funcDecl.IsDeprecated;
            symbol.IsObsolete = funcDecl.IsObsolete;
            symbol.OriginalPhpName = originalPhpName;

            if (funcDecl.NameOrAlias?.AstGrammarAddons.TryGetValue("GenericArguments", out var genericAddon) == true
                && genericAddon is TyhpGenericsTypeArgumentListAst genericList)
            {
                PopulateGenericParameters(
                    genericList,
                    symbol.GenericParameters,
                    _currentFileName,
                    SymbolType.FunctionGenericTypeParameter);
            }

            if (funcDecl.Parameters != null)
            {
                foreach (var param in funcDecl.Parameters.GetAllNotNull())
                {
                    var paramModifiers = ConvertModifiers(param.Modifiers);
                    symbol.Parameters.Add(new ParameterInfo(
                        param.ValueString ?? "",
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers
                    ));
                }
            }

            TryRegisterTyhpdefFunction(symbol, funcDecl, targetScope);

            // Function aliases are the FunctionDeclarationSymbol itself (above). Do not also
            // CreateTyhpdefAlias — UseIncludeSymbol under the same name would collide, and
            // SearchGlobalNamespace skips UseIncludeSymbol so that path never resolved calls.
        }

        private void BindTyhpdefConstDecl(TyhpdefImportConstAst constDecl, IBaseScope parentScope)
        {
            var (originalName, aliasName) = ExtractTyhpdefName(constDecl.NameOrAlias);
            if (string.IsNullOrEmpty(originalName)) return;

            var targetScope = ResolveNamespacedScope(originalName, parentScope, out var shortName);
            var symbol = new ConstantSymbol(shortName, sourceFile: _currentFileName);
            symbol.DeclaredType = constDecl.TypeExpr;
            symbol.IsDeprecated = constDecl.IsDeprecated;
            symbol.IsObsolete = constDecl.IsObsolete;

            TryRegisterTyhpdefTopLevelSymbol(symbol, constDecl, targetScope);

            if (!string.IsNullOrEmpty(aliasName))
            {
                CreateTyhpdefAlias(aliasName, originalName, constDecl, parentScope, PhpUseType.Const);
            }
        }

        private void BindTyhpdefVariableDecl(TyhpdefImportVariableAst varDecl, IBaseScope parentScope)
        {
            var variableName = varDecl.VariableName;
            if (string.IsNullOrEmpty(variableName)) return;

            // PHP variables (superglobals like $_SERVER, $_GET, etc.) are never namespaced,
            // so namespace resolution is intentionally skipped for tyhpdef variable declarations.
            var symbol = new VariableSymbol(
                variableName,
                declaringNode: varDecl,
                sourceFile: _currentFileName);
            symbol.DeclaredType = varDecl.TypeExpr;
            symbol.IsDeprecated = varDecl.IsDeprecated;
            symbol.IsObsolete = varDecl.IsObsolete;

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!fileScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            varDecl,
                            _currentFileName,
                            symbol.Name);
                    }
                    break;

                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            varDecl,
                            _currentFileName,
                            symbol.Name);
                    }
                    break;

                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    break;
            }

            if (!string.IsNullOrEmpty(varDecl.AliasedAs))
            {
                CreateTyhpdefAlias(varDecl.AliasedAs, variableName, varDecl, parentScope, PhpUseType.Variable);
            }
        }

        /// <summary>
        /// Binds tyhpdef object body members into the object scope.
        /// Mirrors <see cref="BindObjectBody"/> but works directly with <see cref="PhpClassBodyAst"/>.
        /// </summary>
        private void BindTyhpdefObjectBody(PhpClassBodyAst? body, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol)
        {
            Types.PopulateObject(objScope);

            if (body == null) return;

            var members = body.GetAllNotNull().ToList();

            foreach (var member in members)
            {
                switch (member)
                {
                    case TyhpdefInlineExtensionFunctionAst:
                    case TyhpOperatorOverloadAst opSkip when opSkip.IsInlineExtension:
                        continue;

                    case PhpMethodDeclAst methodDecl:
                        BindMethodDecl(methodDecl, objScope);
                        break;

                    case PhpPropertyDeclAst propDecl:
                        BindPropertyDecl(propDecl, objScope);
                        break;

                    case PhpConstDeclListAst constList:
                        BindObjectConstDecl(constList, objScope);
                        break;

                    case TyhpdefImportConstDeclListAst tyhpdefConstList:
                        BindTyhpdefImportObjectConstDecl(tyhpdefConstList, objScope);
                        break;

                    case PhpEnumCaseAst enumCase:
                        BindEnumCase(enumCase, objScope, symbol);
                        break;

                    case TyhpOperatorOverloadAst opOverload:
                        BindOperatorOverload(opOverload, objScope);
                        break;

                    case TyhpTypeAliasAst typeAlias:
                    {
                        var tyhpdefAliasName = typeAlias.Name?.ValueString ?? typeAlias.Identifier ?? "";
                        if (!string.IsNullOrEmpty(tyhpdefAliasName))
                        {
                            var aliasSymbol = new ObjectTypeAliasSymbol(tyhpdefAliasName, sourceFile: _currentFileName);
                            aliasSymbol.AliasedType = typeAlias.TypeExpression;

                            if (!objScope.AddChildSymbol(aliasSymbol))
                            {
                                _diagnostics.AddErrorFromAst(
                                    MessageCode.TyhpdefDuplicateDeclaration,
                                    typeAlias,
                                    _currentFileName,
                                    aliasSymbol.Name);
                            }
                            else
                            {
                                RegisterObjectMember(objScope, aliasSymbol, tyhpdefAliasName);
                            }
                        }
                        break;
                    }

                    case PhpTraitUseAst traitUse:
                        BindTraitUseBlock(traitUse, symbol);
                        break;

                    case TyhpImportExtensionAst useExt:
                        BindTyhpdefClassUseExtension(useExt, symbol);
                        break;

                    case UnexpectedNodeAst:
                        // Intentionally skipped: unexpected nodes in tyhpdef bodies are non-fatal
                        break;
                    case ErrorAst errorAst:
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefInvalidFormat,
                            errorAst,
                            _currentFileName,
                            "Error node encountered in tyhpdef object body");
                        break;
                }
            }

            foreach (var member in members)
            {
                switch (member)
                {
                    case TyhpdefInlineExtensionFunctionAst inlineFx:
                        BindTyhpdefInlineExtensionFunction(inlineFx, objScope, symbol);
                        break;

                    case TyhpOperatorOverloadAst opInline when opInline.IsInlineExtension:
                        BindOperatorOverload(opInline, objScope);
                        break;
                }
            }
        }

        /// <summary>
        /// Binds a tyhpdef class constant list (<c>const int ROUND_DOWN ?? 102, …;</c>) into the
        /// object scope. Unlike <see cref="BindObjectConstDecl"/>'s <see cref="PhpConstDeclAst"/>
        /// (type/modifiers per item), <see cref="TyhpdefImportConstDeclListAst"/> hangs a single
        /// shared type/modifiers pair off the list itself (see
        /// <c>VisitTyhpdefImportClassConst</c>) — without this, extension class constants (e.g.
        /// <c>\Decimal\Decimal::ROUND_DOWN</c>) had no <see cref="ObjectConstantSymbol"/> at all and
        /// resolved as bare <c>mixed</c> at every use site.
        /// </summary>
        private void BindTyhpdefImportObjectConstDecl(
            TyhpdefImportConstDeclListAst constList, ObjectDeclarationScope objScope)
        {
            var visibility = constList.AstGrammarAddons.TryGetValue("modifiers", out var modifiersAddon)
                && modifiersAddon is PhpModifierListAst modifiers
                    ? ConvertModifiers(modifiers)
                    : MemberModifier.None;
            var declaredType = constList.AstGrammarAddons.TryGetValue("typeExpr", out var typeAddon)
                ? typeAddon as ITypeExpression
                : null;

            foreach (var constDecl in constList.GetAllNotNull())
            {
                var (originalName, aliasName) = ExtractTyhpdefName(constDecl.AliasedIdentifier);
                var name = aliasName ?? originalName;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var constSymbol = new ObjectConstantSymbol(
                    name,
                    sourceFile: _currentFileName,
                    declaringNode: constDecl,
                    visibility: visibility)
                {
                    DeclaredType = declaredType,
                };

                if (!objScope.AddChildSymbol(constSymbol))
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefDuplicateDeclaration,
                        constDecl,
                        _currentFileName,
                        constSymbol.Name);
                }
                else
                {
                    RegisterObjectMember(objScope, constSymbol, name);
                }
            }
        }

        /// <summary>
        /// Extracts the generic parameter declaration list from a tyhpdef object's name AST.
        /// Class declarations attach the list under the "GenericParameters" grammar addon, whereas
        /// trait/interface/enum declarations carry it as the GenericArguments child of a
        /// <see cref="TyhpGenericIdentifierAst"/>.
        /// </summary>
        private static TyhpGenericsTypeArgumentListAst? ExtractTyhpdefObjectGenericList(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias == null) return null;

            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericParameters", out var addon) &&
                addon is TyhpGenericsTypeArgumentListAst addonList)
            {
                return addonList;
            }

            if (nameOrAlias is TyhpGenericIdentifierAst genericId &&
                genericId.GenericArguments is TyhpGenericsTypeArgumentListAst childList)
            {
                return childList;
            }

            return null;
        }

        private static (string originalName, string? aliasName) ExtractTyhpdefName(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias is TyhpdefIdentifierAliasAst aliasNode)
            {
                var originalName = aliasNode.ValueString ?? "";
                var alias = aliasNode.Identifier ?? "";
                return (originalName, string.IsNullOrEmpty(alias) ? null : alias);
            }

            // Function / method names: `function original as alias` — visitor stores a plain
            // PhpNameAst for `original` and hangs the Tyhp-facing name on the `aliasedAs` addon.
            if (nameOrAlias?.AstGrammarAddons.TryGetValue("aliasedAs", out var aliasedAsNode) == true)
            {
                var original = GetAstNameText(nameOrAlias);
                var alias = GetAstNameText(aliasedAsNode);
                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(alias))
                {
                    return (original, alias);
                }
            }

            // Class names: `class \Vendor\Long as Short` — visitor stores a PhpNameAst for `Short`
            // and hangs the original PHP name on the `aliasOf` addon.
            if (nameOrAlias?.AstGrammarAddons.TryGetValue("aliasOf", out var aliasOfNode) == true)
            {
                var alias = GetAstNameText(nameOrAlias);
                var original = GetAstNameText(aliasOfNode);
                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(alias))
                {
                    return (original, alias);
                }
            }

            // Identifier defaults to "" (never null) on Base2Ast, while name AST nodes such as
            // PhpNameAst carry the actual name in ValueString. A null-coalesce on Identifier would
            // stop at the empty string and never reach ValueString, so check for emptiness instead.
            return (GetAstNameText(nameOrAlias), null);
        }

        private static string GetAstNameText(IBase2Ast? node)
        {
            if (node is null)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(node.Identifier))
            {
                return node.Identifier;
            }

            return node.ValueString ?? "";
        }

        /// <summary>
        /// If the name contains namespace separators, splits off the namespace part and
        /// returns a <see cref="NamespaceBlockScope"/> for it. Otherwise returns the parent scope unchanged.
        /// </summary>
        private IBaseScope ResolveNamespacedScope(string name, IBaseScope parentScope, out string shortName)
        {
            var lastSep = name.LastIndexOf('\\');
            if (lastSep < 0)
            {
                shortName = name;
                return parentScope;
            }

            var namespacePart = name[..lastSep];
            shortName = name[(lastSep + 1)..];

            if (string.IsNullOrEmpty(shortName))
            {
                _diagnostics.AddError(
                    MessageCode.TyhpdefInvalidFormat,
                    _currentFileName,
                    0, 0,
                    _currentFileName);
                shortName = name;
                return parentScope;
            }

            var nsScope = _globalScope.AddNamespaceScope(namespacePart);
            return GetOrCreateNamespaceBlockScope(nsScope, namespacePart);
        }

        private readonly Dictionary<NamespaceScope, NamespaceBlockScope> _tyhpdefNamespaceBlockScopes = new();

        /// <summary>
        /// Returns an existing <see cref="NamespaceBlockScope"/> within the given namespace scope,
        /// or creates a new one if none exists. Unlike user-code binding (which creates a new
        /// <see cref="NamespaceBlockScope"/> per file), tyhpdef loading intentionally shares a single
        /// block scope across all tyhpdef files declaring into the same namespace. This consolidates
        /// tyhpdef symbols into one scope per namespace for efficient lookup during name resolution.
        /// </summary>
        private NamespaceBlockScope GetOrCreateNamespaceBlockScope(NamespaceScope nsScope, string namespaceName)
        {
            if (_tyhpdefNamespaceBlockScopes.TryGetValue(nsScope, out var cached))
                return cached;

            var existing = nsScope.ChildScopes.OfType<NamespaceBlockScope>().FirstOrDefault();
            if (existing != null)
            {
                _tyhpdefNamespaceBlockScopes[nsScope] = existing;
                return existing;
            }

            var blockSymbol = new NamespaceBlockSymbol(namespaceName, _currentFileScope);
            var blockScope = new NamespaceBlockScope(nsScope, blockSymbol);
            nsScope.AddChildScope(blockScope);
            _tyhpdefNamespaceBlockScopes[nsScope] = blockScope;
            return blockScope;
        }

        private void CreateTyhpdefAlias(string aliasName, string originalName, IBase2Ast declaringNode, IBaseScope parentScope, PhpUseType useType)
        {
            var useSymbol = new UseIncludeSymbol(
                aliasName,
                originalName,
                declaringNode,
                sourceFile: _currentFileName,
                useType: useType
            );

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!fileScope.AddChildSymbol(useSymbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            declaringNode,
                            _currentFileName,
                            aliasName);
                    }
                    break;
                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.AddChildSymbol(useSymbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefDuplicateDeclaration,
                            declaringNode,
                            _currentFileName,
                            aliasName);
                    }
                    break;
                default:
                    _diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, _currentFileName, 0, 0, _currentFileName);
                    break;
            }
        }
    }
}
