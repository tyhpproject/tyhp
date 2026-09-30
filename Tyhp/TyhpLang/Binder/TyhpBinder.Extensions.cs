using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        private readonly Dictionary<ObjectDeclarationSymbol, ObjectDeclarationScope> _syntheticInlineExtensionScopes = new();
        private readonly List<PendingFileUseExtension> _pendingFileUseExtensions = [];
        private int _extensionTargetGroupOrdinal;

        /// <summary>
        /// Lexical scope for a header or nested <c>extends</c> group, plus the extension
        /// symbol that publishes members for lookup.
        /// </summary>
        private readonly record struct ExtensionBlockBinding(
            ObjectDeclarationScope LexicalScope,
            ObjectDeclarationSymbol BlockSymbol,
            ObjectDeclarationScope ExtensionScope,
            ObjectDeclarationSymbol ExtensionSymbol)
        {
            public ITypeExpression? TargetType => this.BlockSymbol.PendingExtensionBlockTarget;

            public static ExtensionBlockBinding ForHeader(
                ObjectDeclarationScope scope,
                ObjectDeclarationSymbol symbol) =>
                new(scope, symbol, scope, symbol);
        }

        private readonly record struct PendingFileUseExtension(
            TyhpImportExtensionAst Import,
            string FileName,
            FileScope? FileScope);

        private ObjectDeclarationScope GetOrCreateSyntheticInlineExtensionScope(
            ObjectDeclarationSymbol ownerClass,
            ObjectDeclarationScope ownerScope)
        {
            if (_syntheticInlineExtensionScopes.TryGetValue(ownerClass, out var cached))
                return cached;

            var synthName = "__TyhpInlineExt_" + ownerClass.Name;
            var synthSymbol = new ObjectDeclarationSymbol(synthName, ownerClass.DeclaringAstNode, _currentFileName)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsExtension = true,
                IsCompilerGenerated = true,
                InlineExtensionReceiverClass = ownerClass,
            };
            ownerClass.SyntheticInlineExtension = synthSymbol;

            switch (ownerScope.Parent)
            {
                case FileScope fileScope:
                    if (!fileScope.TryAddChildSymbol(synthSymbol, out var existing))
                    {
                        var node = ownerClass.DeclaringAstNode;
                        if (node != null)
                        {
                            ReportBinderDuplicate(node, existing, synthName);
                        }
                        else
                        {
                            _diagnostics.AddError(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                _currentFileName,
                                0,
                                0,
                                synthName);
                        }
                        return ownerScope;
                    }

                    var fsScope = new ObjectDeclarationScope(fileScope, synthSymbol);
                    fileScope.AddChildScope(fsScope);
                    _syntheticInlineExtensionScopes[ownerClass] = fsScope;
                    return fsScope;

                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.TryAddChildSymbol(synthSymbol, out var nsExisting))
                    {
                        var node = ownerClass.DeclaringAstNode;
                        if (node != null)
                        {
                            ReportBinderDuplicate(node, nsExisting, synthName);
                        }
                        else
                        {
                            _diagnostics.AddError(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                _currentFileName,
                                0,
                                0,
                                synthName);
                        }
                        return ownerScope;
                    }

                    var nsScope = new ObjectDeclarationScope(nsBlockScope, synthSymbol);
                    nsBlockScope.AddChildScope(nsScope);
                    _syntheticInlineExtensionScopes[ownerClass] = nsScope;
                    return nsScope;

                default:
                    _diagnostics.AddError(
                        MessageCode.BinderUnknownError,
                        _currentFileName,
                        0,
                        0,
                        $"Cannot place synthetic inline extension for '{ownerClass.Name}': unexpected parent scope.");
                    return ownerScope;
            }
        }

        private void BindExtensionDeclaration(TyhpExtensionDeclAst decl, IBaseScope parentScope)
        {
            var name = decl.Identifier ?? "";
            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderUnknownError,
                    decl,
                    _currentFileName,
                    "Extension declaration has no name — skipping.");
                return;
            }

            ReportReservedExtensionBackerName(name, decl);

            ShouldRegisterPhpVersionGatedDeclaration(
                decl,
                parentScope,
                name,
                SymbolType.ObjectTypeDeclaration,
                illegalAttributeTarget: true,
                out var phpConstraints);

            var symbol = new ObjectDeclarationSymbol(name, decl, _currentFileName)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsExtension = true,
            };
            StampPhpVersionConstraints(symbol, phpConstraints);
            ApplyInternal(symbol, decl);
            ApplyExtensionHeaderBinding(symbol, decl.GenericParameters, decl.TargetType);

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!fileScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportBinderDuplicate(decl, existing, symbol.Name);
                        return;
                    }

                    var objScopeFs = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScopeFs);
                    fileScope.DeclaredExtensions.Add(symbol);
                    BindExtensionMemberList(decl.FunctionList, ExtensionBlockBinding.ForHeader(objScopeFs, symbol));
                    break;

                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var nsExisting))
                    {
                        ReportBinderDuplicate(decl, nsExisting, symbol.Name);
                        return;
                    }

                    var objScopeNs = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScopeNs);
                    _currentFileScope?.DeclaredExtensions.Add(symbol);
                    BindExtensionMemberList(decl.FunctionList, ExtensionBlockBinding.ForHeader(objScopeNs, symbol));
                    break;

                default:
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderUnknownError,
                        decl,
                        _currentFileName,
                        $"Unexpected parent scope type '{parentScope.GetType().Name}' for extension declaration");
                    break;
            }
        }

        private void ApplyExtensionHeaderBinding(
            ObjectDeclarationSymbol symbol,
            TyhpGenericsTypeArgumentListAst? genericParameters,
            ITypeExpression? targetType)
        {
            if (genericParameters != null)
            {
                PopulateGenericParameters(
                    genericParameters,
                    symbol.GenericParameters,
                    _currentFileName,
                    SymbolType.ClassGenericTypeParameter);
            }

            symbol.PendingExtensionBlockTarget = targetType;
        }

        private void BindExtensionMemberList(TyhpExtensionFunctionListAst? list, ExtensionBlockBinding block)
        {
            if (list == null) return;

            var members = list.GetAllNotNull().ToList();
            var implementedFunctionNames = OverloadSignatureHelper.CollectImplementedExtensionFunctionNames(members);

            foreach (var member in members)
            {
                switch (member)
                {
                    case PhpFunctionDeclAst funcDecl
                        when OverloadSignatureHelper.IsExtensionFunctionOverloadSignature(
                            funcDecl, implementedFunctionNames):
                        break;

                    case PhpFunctionDeclAst funcDecl:
                        BindExtensionFunctionDecl(funcDecl, block);
                        break;

                    case TyhpdefInlineExtensionFunctionAst inlineFn when inlineFn.Method != null:
                        BindExtensionMethodDecl(inlineFn.Method, block, inlineFn);
                        break;

                    case TyhpOperatorOverloadAst opOverload:
                        BindOperatorOverload(
                            opOverload,
                            block.LexicalScope,
                            block.ExtensionSymbol,
                            block.TargetType);
                        break;

                    case TyhpExtensionDeclAst { IsTargetGroup: true } group:
                        BindExtensionTargetGroup(group, block);
                        break;

                    case UnexpectedNodeAst:
                    case ErrorAst:
                        break;

                    default:
                        _diagnostics.AddWarningFromAst(
                            MessageCode.BinderUnknownError,
                            member,
                            _currentFileName,
                            $"Unexpected extension member type: {member.GetType().Name}");
                        break;
                }
            }

            foreach (var member in members)
            {
                if (member is PhpFunctionDeclAst funcDecl
                    && OverloadSignatureHelper.IsExtensionFunctionOverloadSignature(
                        funcDecl, implementedFunctionNames))
                {
                    AttachExtensionFunctionOverload(funcDecl, block);
                }
            }
        }

        private void BindExtensionTargetGroup(TyhpExtensionDeclAst group, ExtensionBlockBinding parent)
        {
            var groupSymbol = new ObjectDeclarationSymbol(
                "__TyhpExtTarget" + (++_extensionTargetGroupOrdinal),
                group,
                _currentFileName)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsCompilerGenerated = true,
                IsExtensionTargetGroup = true,
                PendingExtensionBlockTarget = group.TargetType,
            };
            if (group.GenericParameters != null)
            {
                PopulateGenericParameters(
                    group.GenericParameters,
                    groupSymbol.GenericParameters,
                    _currentFileName,
                    SymbolType.ClassGenericTypeParameter);
            }

            var groupScope = new ObjectDeclarationScope(parent.ExtensionScope, groupSymbol);
            ((IObjectDeclarationScopeParent)parent.ExtensionScope).AddObjectDeclarationChildScope(groupScope);
            BindExtensionMemberList(
                group.FunctionList,
                new ExtensionBlockBinding(
                    groupScope,
                    groupSymbol,
                    parent.ExtensionScope,
                    parent.ExtensionSymbol));
        }

        private void BindExtensionFunctionDecl(PhpFunctionDeclAst funcDecl, ExtensionBlockBinding block)
        {
            var name = funcDecl.Identifier ?? "";
            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderUnknownError,
                    funcDecl,
                    _currentFileName,
                    "extension function name");
                return;
            }

            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    funcDecl,
                    block.LexicalScope,
                    name,
                    SymbolType.StaticObjectMethod,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var methodSymbol = CreateExtensionFunctionSymbol(funcDecl, phpConstraints);

            if (!block.LexicalScope.TryAddChildSymbol(methodSymbol, out var existing))
            {
                ReportBinderDuplicate(funcDecl, existing, methodSymbol.Name);
                return;
            }
            RegisterObjectMember(block.LexicalScope, methodSymbol, name);
            PublishExtensionMethod(block.ExtensionSymbol, methodSymbol);

            var staticScope = new StaticMethodDeclarationScope(block.LexicalScope, methodSymbol);
            block.LexicalScope.AddChildScope(staticScope);
            BindImplicitExtensionReceiver(methodSymbol, staticScope, funcDecl, block.TargetType, HasByRefReceiver(funcDecl));
            BindMethodParameters(funcDecl.Parameters, staticScope, methodSymbol, block.LexicalScope);
            if (funcDecl.Body != null)
                BindStatementBlock(funcDecl.Body, staticScope);
        }

        private void AttachExtensionFunctionOverload(
            PhpFunctionDeclAst funcDecl,
            ExtensionBlockBinding block)
        {
            var name = funcDecl.Identifier ?? "";
            if (string.IsNullOrEmpty(name)
                || !block.ExtensionSymbol.Members.TryGetValue(name, out var existing)
                || existing is not ObjectMethodSymbol primary)
            {
                return;
            }

            var overload = CreateExtensionFunctionSymbol(funcDecl, []);
            overload.ContainingScope = block.LexicalScope;
            funcDecl.BoundSymbol = overload;

            // A bodyless overload signature is compile-time-only and erased at emit — there is no
            // StaticMethodDeclarationScope / `$this` VariableSymbol to bind (BindImplicitExtensionReceiver
            // is for the real implementation's scope). It still needs the same implicit receiver
            // *parameter* as the primary/implementation so arity lines up 1:1 with block.TargetType != null:
            // without this, every overload signature is short one parameter relative to the
            // implementation that erases it, and TYHP4180 / overload-arity checks misalign.
            if (block.TargetType != null
                && !overload.Parameters.Any(p => string.Equals(p.Name, "$this", StringComparison.Ordinal)))
            {
                overload.Parameters.Insert(0, new ParameterInfo(
                    "$this",
                    block.TargetType,
                    null,
                    false,
                    HasByRefReceiver(funcDecl),
                    MemberModifier.None));
            }

            primary.Overloads.Add(overload);
        }

        private ObjectMethodSymbol CreateExtensionFunctionSymbol(
            PhpFunctionDeclAst funcDecl,
            IReadOnlyList<string> phpConstraints)
        {
            var methodSymbol = new ObjectMethodSymbol(
                funcDecl.Identifier ?? "",
                funcDecl,
                _currentFileName,
                MemberModifier.Static,
                SymbolType.StaticObjectMethod);

            StampPhpVersionConstraints(methodSymbol, phpConstraints);
            methodSymbol.ReturnType = funcDecl.ReturnType;
            methodSymbol.IsStatic = true;
            ApplyInternal(methodSymbol, funcDecl);
            PopulateGenericParametersFromGrammarAddon(
                funcDecl.AstGrammarAddons,
                methodSymbol.GenericParameters,
                _currentFileName,
                SymbolType.FunctionGenericTypeParameter);

            if (funcDecl.Parameters != null)
            {
                foreach (var param in funcDecl.Parameters.GetAllNotNull())
                {
                    var paramModifiers = ConvertModifiers(param.Modifiers);
                    methodSymbol.Parameters.Add(new ParameterInfo(
                        param.ValueString ?? "",
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers
                    ));
                }
            }

            return methodSymbol;
        }

        private void BindExtensionMethodDecl(
            PhpMethodDeclAst methodDecl,
            ExtensionBlockBinding block,
            IBase2Ast? receiverAnchor = null)
        {
            var name = methodDecl.Identifier ?? "";
            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderUnknownError,
                    methodDecl,
                    _currentFileName,
                    "extension function name");
                return;
            }

            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    methodDecl,
                    block.LexicalScope,
                    name,
                    SymbolType.StaticObjectMethod,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var methodSymbol = new ObjectMethodSymbol(
                name,
                methodDecl,
                _currentFileName,
                MemberModifier.Static,
                SymbolType.StaticObjectMethod);

            StampPhpVersionConstraints(methodSymbol, phpConstraints);

            methodSymbol.ReturnType = methodDecl.ReturnType;
            methodSymbol.IsStatic = true;
            ApplyInternal(methodSymbol, methodDecl);
            PopulateGenericParametersFromGrammarAddon(
                methodDecl.AstGrammarAddons,
                methodSymbol.GenericParameters,
                _currentFileName,
                SymbolType.FunctionGenericTypeParameter);

            if (methodDecl.Parameters != null)
            {
                foreach (var param in methodDecl.Parameters.GetAllNotNull())
                {
                    var paramModifiers = ConvertModifiers(param.Modifiers);
                    methodSymbol.Parameters.Add(new ParameterInfo(
                        param.ValueString ?? "",
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers
                    ));
                }
            }

            if (!block.LexicalScope.TryAddChildSymbol(methodSymbol, out var existing))
            {
                ReportBinderDuplicate(methodDecl, existing, methodSymbol.Name);
                return;
            }
            RegisterObjectMember(block.LexicalScope, methodSymbol, name);
            PublishExtensionMethod(block.ExtensionSymbol, methodSymbol);

            var staticScope = new StaticMethodDeclarationScope(block.LexicalScope, methodSymbol);
            block.LexicalScope.AddChildScope(staticScope);
            var receiverNode = receiverAnchor ?? methodDecl;
            BindImplicitExtensionReceiver(
                methodSymbol,
                staticScope,
                receiverNode,
                block.TargetType,
                HasByRefReceiver(receiverNode) || HasByRefReceiver(methodDecl));
            BindMethodParameters(methodDecl.Parameters, staticScope, methodSymbol, block.LexicalScope);
            if (methodDecl.Body != null)
                BindStatementBlock(methodDecl.Body, staticScope);
        }

        private static void PublishExtensionMethod(ObjectDeclarationSymbol extension, ObjectMethodSymbol method)
        {
            if (string.IsNullOrEmpty(method.Name) || method is ObjectOperatorOverloadMethodSymbol)
            {
                return;
            }

            if (extension.Members.TryGetValue(method.Name, out var existing))
            {
                if (ReferenceEquals(existing, method))
                {
                    return;
                }

                if (existing is ObjectMethodSymbol primary)
                {
                    primary.Overloads.Add(method);
                }

                return;
            }

            extension.Members[method.Name] = method;
        }

        private void BindImplicitExtensionReceiver(
            ObjectMethodSymbol methodSymbol,
            StaticMethodDeclarationScope staticScope,
            IBase2Ast anchor,
            ITypeExpression? targetType,
            bool byRef)
        {
            if (targetType == null)
            {
                return;
            }

            if (methodSymbol.Parameters.Any(p => string.Equals(p.Name, "$this", StringComparison.Ordinal)))
            {
                return;
            }

            methodSymbol.Parameters.Insert(0, new ParameterInfo(
                "$this",
                targetType,
                null,
                false,
                byRef,
                MemberModifier.None));

            // `$this` is implied and never written at the declaration site (decision 4), so
            // there is no dedicated AST node of its own to anchor the synthetic receiver
            // VariableSymbol on. The `BaseSymbol` constructor unconditionally stamps
            // `declaringNode.BoundSymbol = this`. For `BindExtensionFunctionDecl`, `anchor` is
            // `funcDecl` itself — the same node the method symbol was just constructed with —
            // so that stamp would clobber `funcDecl.BoundSymbol` from the real
            // `ObjectMethodSymbol` back to this synthetic receiver. Restore it in that case so
            // callers (`ExtensionRule`, `InlineSpliceRule`, splice call-site resolution) keep
            // seeing `functionDecl.BoundSymbol` as the method. For the tyhpdef inline-`fn` path
            // `anchor` is the wrapping `TyhpdefInlineExtensionFunctionAst`, a distinct node from
            // the inner method declaration that owns the method symbol — there is nothing to
            // clobber there, so the receiver keeps that node's `BoundSymbol` as before.
            var previousBoundSymbol = anchor.BoundSymbol;
            var thisVar = new VariableSymbol("$this", declaringNode: anchor, sourceFile: _currentFileName)
            {
                DeclaredType = targetType,
                IsParameter = true,
                IsRef = byRef,
            };
            if (ReferenceEquals(previousBoundSymbol, methodSymbol))
            {
                anchor.BoundSymbol = previousBoundSymbol;
            }

            staticScope.AddChildSymbol(thisVar);
        }

        private static bool HasByRefReceiver(IBase2Ast node) =>
            node.AstGrammarAddons.ContainsKey(TyhpExtensionDeclAst.ByRefReceiverAddonKey);

        private void BindTyhpdefStandaloneExtensionDecl(TyhpdefStandaloneExtensionDeclAst decl, IBaseScope parentScope)
        {
            var name = decl.Identifier ?? "";
            if (string.IsNullOrEmpty(name) || name == "<error>")
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderUnknownError,
                    decl,
                    _currentFileName,
                    "Extension declaration has no name — skipping.");
                return;
            }

            ReportReservedExtensionBackerName(name, decl);

            ShouldRegisterPhpVersionGatedDeclaration(
                decl,
                parentScope,
                name,
                SymbolType.ObjectTypeDeclaration,
                illegalAttributeTarget: true,
                out var phpConstraints);

            var symbol = new ObjectDeclarationSymbol(name, decl, _currentFileName)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsExtension = true,
                IsObsolete = decl.IsObsolete,
            };
            if (decl.IsDeprecated)
            {
                symbol.IsDeprecated = true;
            }
            StampPhpVersionConstraints(symbol, phpConstraints);
            ApplyExtensionHeaderBinding(symbol, decl.GenericParameters, decl.TargetType);

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!TryRegisterTyhpdefTopLevelSymbol(symbol, decl, fileScope))
                    {
                        return;
                    }

                    var objScopeFs = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScopeFs);
                    fileScope.DeclaredExtensions.Add(symbol);
                    BindExtensionMemberList(decl.FunctionList, ExtensionBlockBinding.ForHeader(objScopeFs, symbol));
                    break;

                case NamespaceBlockScope nsBlockScope:
                    if (!TryRegisterTyhpdefTopLevelSymbol(symbol, decl, nsBlockScope))
                    {
                        return;
                    }

                    var objScopeNs = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScopeNs);
                    _currentFileScope?.DeclaredExtensions.Add(symbol);
                    BindExtensionMemberList(decl.FunctionList, ExtensionBlockBinding.ForHeader(objScopeNs, symbol));
                    break;

                default:
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderUnknownError,
                        decl,
                        _currentFileName,
                        $"Unexpected parent scope type '{parentScope.GetType().Name}' for extension declaration");
                    break;
            }
        }

        private void BindFileUseExtension(TyhpImportExtensionAst importExt, IBaseScope _)
        {
            if (importExt.UseDeclarations == null)
            {
                return;
            }

            // Stash until after Pass 1. FindExtensionSymbol walks the tree built so far, so a
            // `use extension` / `global use extension` that textually precedes the `extension`
            // declaration (same file or a later file) would otherwise silently no-op.
            _pendingFileUseExtensions.Add(new PendingFileUseExtension(
                importExt,
                _currentFileName,
                _currentFileScope));
        }

        private void ResolvePendingFileUseExtensions()
        {
            if (_pendingFileUseExtensions.Count == 0)
            {
                return;
            }

            var pending = _pendingFileUseExtensions.ToList();
            _pendingFileUseExtensions.Clear();

            var savedFileName = _currentFileName;
            var savedFileScope = _currentFileScope;
            try
            {
                foreach (var item in pending)
                {
                    _currentFileName = item.FileName;
                    _currentFileScope = item.FileScope;
                    ApplyFileUseExtension(item.Import, item.FileScope);
                }
            }
            finally
            {
                _currentFileName = savedFileName;
                _currentFileScope = savedFileScope;
            }
        }

        private void ApplyFileUseExtension(TyhpImportExtensionAst importExt, FileScope? fileScope)
        {
            if (importExt.UseDeclarations == null)
            {
                return;
            }

            var resolved = new List<ObjectDeclarationSymbol>();
            foreach (var decl in importExt.UseDeclarations.GetAllNotNull())
            {
                var ns = decl.NamespaceName ?? "";
                if (string.IsNullOrEmpty(ns))
                {
                    continue;
                }

                var ext = FindExtensionSymbol(ns);
                if (ext == null)
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.TyhpdefExtensionNotFound,
                        decl,
                        _currentFileName,
                        ns);
                    continue;
                }

                resolved.Add(ext);
                if (importExt.IsGlobal)
                {
                    if (!_globalScope.GloballyActivatedExtensions.Contains(ext))
                    {
                        _globalScope.GloballyActivatedExtensions.Add(ext);
                    }
                }
                else if (fileScope != null && !fileScope.ImportedExtensions.Contains(ext))
                {
                    fileScope.ImportedExtensions.Add(ext);
                }
            }

            if (importExt.IsGlobal)
            {
                ProcessExtensionUseAdaptations(
                    importExt,
                    resolved,
                    hidden: GetOrCreateGlobalHiddenMembers(),
                    precedence: GetOrCreateGlobalPrecedence(),
                    aliases: GetOrCreateGlobalAliases());
            }
            else if (fileScope != null)
            {
                ProcessExtensionUseAdaptations(
                    importExt,
                    resolved,
                    hidden: fileScope.ExtensionUseHiddenMembers ??=
                        new HashSet<string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer),
                    precedence: fileScope.ExtensionUseMethodPrecedence ??=
                        new Dictionary<string, string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer),
                    aliases: fileScope.ExtensionUseMethodAliases ??=
                        new Dictionary<string, (string?, string)>(ObjectDeclarationMemberNamePolicy.MemberNameComparer));
            }
            else
            {
                ProcessExtensionUseAdaptations(importExt, resolved, null, null, null);
            }
        }

        private HashSet<string> GetOrCreateGlobalHiddenMembers() =>
            _globalScope.ExtensionUseHiddenMembers ??=
                new HashSet<string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);

        private Dictionary<string, string> GetOrCreateGlobalPrecedence() =>
            _globalScope.ExtensionUseMethodPrecedence ??=
                new Dictionary<string, string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);

        private Dictionary<string, (string?, string)> GetOrCreateGlobalAliases() =>
            _globalScope.ExtensionUseMethodAliases ??=
                new Dictionary<string, (string?, string)>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);

        private ObjectDeclarationSymbol? FindExtensionSymbol(string importedName)
        {
            var needle = importedName.Trim().TrimStart('\\');
            if (string.IsNullOrEmpty(needle))
            {
                return null;
            }

            var shortName = needle;
            var lastSep = needle.LastIndexOf('\\');
            if (lastSep >= 0)
            {
                shortName = needle[(lastSep + 1)..];
            }

            foreach (var symbol in EnumerateObjectSymbols(_globalScope))
            {
                if (!symbol.IsExtension || symbol.IsCompilerGenerated)
                {
                    continue;
                }

                if (string.Equals(symbol.Name, shortName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(symbol.FullyQualifiedName.TrimStart('\\'), needle, StringComparison.OrdinalIgnoreCase))
                {
                    return symbol;
                }
            }

            return null;
        }

        private static IEnumerable<ObjectDeclarationSymbol> EnumerateObjectSymbols(IBaseScope root)
        {
            var stack = new Stack<IBaseScope>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                foreach (var symbol in current.GetAllChildSymbols())
                {
                    if (symbol is ObjectDeclarationSymbol obj)
                    {
                        yield return obj;
                    }
                }

                foreach (var child in current.GetAllChildScopes())
                {
                    stack.Push(child);
                }
            }
        }

        private void ReportReservedExtensionBackerName(string name, IBase2Ast node)
        {
            if (GeneratedNames.EndsWithExtensionBackerSuffix(name))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.CheckerReservedExtensionBackerSuffix,
                    node,
                    _currentFileName,
                    name,
                    GeneratedNames.ExtensionBackerSuffix);
            }
        }

        /// <summary>
        /// After tyhpdef overlays and user files are bound, reject Tyhp-facing names PHP 8.6
        /// reserves. A Layer 1 spelling such as <c>class Is</c> is legal when an overlay
        /// <c>as</c> alias replaced it; only the name that remains is checked.
        /// </summary>
        private void ReportReservedPhp86Names()
        {
            var seen = new HashSet<IBaseSymbol>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<IBaseScope>();
            stack.Push(_globalScope);
            while (stack.Count > 0)
            {
                var scope = stack.Pop();
                foreach (var symbol in scope.GetAllChildSymbols())
                {
                    if (symbol != null && seen.Add(symbol))
                    {
                        ReportReservedPhp86Name(symbol);
                    }
                }

                foreach (var child in scope.GetAllChildScopes())
                {
                    if (child != null)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        private void ReportReservedPhp86Name(IBaseSymbol symbol)
        {
            if (symbol is not BaseSymbol declared || declared.DeclaringAstNode is null)
            {
                return;
            }

            var name = SimpleDeclaredName(declared.Name);
            if (name.Length == 0)
            {
                return;
            }

            var kind = ReservedPhp86Kind(symbol, name);
            if (kind is null)
            {
                return;
            }

            _diagnostics.AddErrorFromAst(
                MessageCode.CheckerReservedPhp86Name,
                declared.DeclaringAstNode,
                declared.SourceFile,
                name,
                kind);
        }

        private static string? ReservedPhp86Kind(IBaseSymbol symbol, string name)
        {
            switch (symbol)
            {
                case ObjectDeclarationSymbol type when IsPhp86LetOrIs(name):
                    return type.ObjectKind == PhpTypeDeclType.Unspecified
                        ? "type"
                        : type.ObjectKind.ToString().ToLowerInvariant();
                case FunctionDeclarationSymbol when IsPhp86LetOrIs(name) || IsPhp86Readonly(name):
                    return "function";
                case ConstantSymbol when IsPhp86ConstantName(name):
                    return "constant";
                case ObjectConstantSymbol { IsEnumCase: false } when IsPhp86ConstantName(name):
                    return "class constant";
                case UseIncludeSymbol use:
                    return ReservedPhp86UseKind(use.UseType, name);
                default:
                    return null;
            }
        }

        private static string? ReservedPhp86UseKind(PhpUseType useType, string name)
        {
            return useType switch
            {
                PhpUseType.Function when IsPhp86LetOrIs(name) || IsPhp86Readonly(name) => "function alias",
                PhpUseType.Const when IsPhp86ConstantName(name) => "constant alias",
                PhpUseType.Class when IsPhp86LetOrIs(name) || name == "_" => "class alias",
                _ => null,
            };
        }

        private static bool IsPhp86ConstantName(string name) =>
            IsPhp86LetOrIs(name)
            || IsPhp86Namespace(name)
            || name == "_";

        private static bool IsPhp86LetOrIs(string name) =>
            name.Equals("let", StringComparison.OrdinalIgnoreCase)
            || name.Equals("is", StringComparison.OrdinalIgnoreCase);

        private static bool IsPhp86Readonly(string name) =>
            name.Equals("readonly", StringComparison.OrdinalIgnoreCase);

        private static bool IsPhp86Namespace(string name) =>
            name.Equals("namespace", StringComparison.OrdinalIgnoreCase);

        private static string SimpleDeclaredName(string name)
        {
            var trimmed = name.TrimStart('\\');
            var separator = trimmed.LastIndexOf('\\');
            return separator < 0 ? trimmed : trimmed[(separator + 1)..];
        }

        private void BindTyhpdefInlineExtensionFunction(
            TyhpdefInlineExtensionFunctionAst wrapper,
            ObjectDeclarationScope ownerScope,
            ObjectDeclarationSymbol ownerClass)
        {
            var methodDecl = wrapper.Method;
            if (methodDecl == null) return;

            var name = methodDecl.Identifier ?? "";
            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderUnknownError,
                    wrapper,
                    _currentFileName,
                    "extension function name");
                return;
            }

            if (ownerClass.Members.ContainsKey(name))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefExtensionConflict,
                    wrapper,
                    _currentFileName,
                    name,
                    ownerClass.Name);
                return;
            }

            var synthScope = GetOrCreateSyntheticInlineExtensionScope(ownerClass, ownerScope);

            methodDecl.CopyAttributesFrom(wrapper);

            var methodSymbol = new ObjectMethodSymbol(
                name,
                methodDecl,
                _currentFileName,
                MemberModifier.Static,
                SymbolType.StaticObjectMethod);

            methodSymbol.ReturnType = methodDecl.ReturnType;
            methodSymbol.IsStatic = true;
            PopulateGenericParametersFromGrammarAddon(
                methodDecl.AstGrammarAddons,
                methodSymbol.GenericParameters,
                _currentFileName,
                SymbolType.FunctionGenericTypeParameter);

            // Class-body `extension fn` has no block target. `$this` / `self` mean the
            // enclosing tyhpdef type. Prepend an implicit receiver so lookup and splice
            // skip it the same way a standalone block-target method does.
            methodSymbol.Parameters.Add(new ParameterInfo(
                "$this",
                CreateInlineExtensionThisType(ownerClass, methodDecl),
                null,
                false,
                false,
                MemberModifier.None));

            if (methodDecl.Parameters != null)
            {
                foreach (var param in methodDecl.Parameters.GetAllNotNull())
                {
                    var paramModifiers = ConvertModifiers(param.Modifiers);
                    methodSymbol.Parameters.Add(new ParameterInfo(
                        param.ValueString ?? "",
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers
                    ));
                }
            }

            if (!synthScope.TryAddChildSymbol(methodSymbol, out var existing))
            {
                ReportBinderDuplicate(methodDecl, existing, methodSymbol.Name);
                return;
            }
            RegisterObjectMember(synthScope, methodSymbol, name);

            var staticScope = new StaticMethodDeclarationScope(synthScope, methodSymbol);
            synthScope.AddChildScope(staticScope);
            BindImplicitInlineExtensionThis(wrapper, methodDecl, ownerClass, staticScope);
            BindMethodParameters(methodDecl.Parameters, staticScope, methodSymbol, synthScope);
            if (methodDecl.Body != null)
                BindStatementBlock(methodDecl.Body, staticScope);
        }

        private void BindImplicitInlineExtensionThis(
            TyhpdefInlineExtensionFunctionAst wrapper,
            PhpMethodDeclAst methodDecl,
            ObjectDeclarationSymbol ownerClass,
            StaticMethodDeclarationScope staticScope)
        {
            var thisType = CreateInlineExtensionThisType(ownerClass, methodDecl);
            var thisVar = new VariableSymbol("$this", declaringNode: wrapper, sourceFile: _currentFileName)
            {
                DeclaredType = thisType,
                IsParameter = true,
            };
            staticScope.AddChildSymbol(thisVar);
        }

        private static ITypeExpression CreateInlineExtensionThisType(
            ObjectDeclarationSymbol ownerClass,
            IBase2Ast context)
        {
            var nameText = string.IsNullOrEmpty(ownerClass.FullyQualifiedName)
                ? ownerClass.Name
                : ownerClass.FullyQualifiedName;
            var name = PhpNameAst.CreateFromContext(nameText, (Base2Ast)context);
            name.BoundSymbol = ownerClass;
            var named = PhpNamedTypeAst.WrapClassName(name, (Base2Ast)context);
            named.BoundSymbol = ownerClass;
            return named;
        }

        private void BindTyhpdefClassUseExtension(TyhpImportExtensionAst importExt, ObjectDeclarationSymbol symbol)
        {
            if (importExt.UseDeclarations == null) return;

            var resolved = new List<ObjectDeclarationSymbol>();
            foreach (var decl in importExt.UseDeclarations.GetAllNotNull())
            {
                var ns = decl.NamespaceName ?? "";
                if (string.IsNullOrEmpty(ns))
                    continue;

                symbol.PendingTyhpdefUseExtensionNamespaces ??= new List<string>();
                symbol.PendingTyhpdefUseExtensionNamespaces.Add(ns);

                var ext = FindExtensionSymbol(ns);
                if (ext != null)
                {
                    resolved.Add(ext);
                }
            }

            ProcessExtensionUseAdaptations(
                importExt,
                resolved,
                hidden: symbol.ExtensionUseHiddenMembers ??=
                    new HashSet<string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer),
                precedence: symbol.ExtensionUseMethodPrecedence ??=
                    new Dictionary<string, string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer),
                aliases: symbol.ExtensionUseMethodAliases ??=
                    new Dictionary<string, (string?, string)>(ObjectDeclarationMemberNamePolicy.MemberNameComparer));
        }

        private void ProcessExtensionUseAdaptations(
            TyhpImportExtensionAst importExt,
            IReadOnlyList<ObjectDeclarationSymbol> extensions,
            HashSet<string>? hidden,
            Dictionary<string, string>? precedence,
            Dictionary<string, (string?, string)>? aliases)
        {
            if (importExt.Adaptations == null) return;

            foreach (var adaptation in importExt.Adaptations.GetAllNotNull())
            {
                switch (adaptation)
                {
                    case PhpTraitPrecedenceAst precedenceAst:
                    {
                        var methodRef = precedenceAst.MethodReference;
                        var methodName = AdaptationMemberKey(methodRef);
                        var preferredExt = NameText(methodRef?.TraitName) ?? "";

                        if (!string.IsNullOrEmpty(methodName) && !string.IsNullOrEmpty(preferredExt) && precedence != null)
                        {
                            precedence[methodName] = preferredExt;
                        }

                        break;
                    }
                    case PhpTraitAliasAst alias:
                    {
                        var methodRef = alias.MethodReference;
                        var originalMethod = AdaptationMemberKey(methodRef);
                        var extNameStr = NameText(methodRef?.TraitName);

                        if (alias.IsHide)
                        {
                            if (!string.IsNullOrEmpty(originalMethod) && hidden != null)
                            {
                                hidden.Add(originalMethod);
                            }

                            if (!string.IsNullOrEmpty(originalMethod)
                                && !ExtensionDeclaresMember(extensions, extNameStr, originalMethod, methodRef))
                            {
                                _diagnostics.AddErrorFromAst(
                                    MessageCode.CheckerExtensionHideUnknownMember,
                                    alias,
                                    _currentFileName,
                                    originalMethod);
                            }

                            break;
                        }

                        if (methodRef?.IsOperator == true)
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.CheckerExtensionOperatorRenameForbidden,
                                alias,
                                _currentFileName,
                                methodRef.OperatorToken ?? originalMethod);
                            break;
                        }

                        var aliasName = alias.Identifier;
                        if (!string.IsNullOrEmpty(aliasName) && !string.IsNullOrEmpty(originalMethod) && aliases != null)
                        {
                            aliases[aliasName] = (extNameStr, originalMethod);
                        }

                        break;
                    }
                }
            }
        }

        private static string AdaptationMemberKey(PhpTraitMemberRefAst? methodRef)
        {
            if (methodRef == null)
            {
                return "";
            }

            if (methodRef.IsOperator)
            {
                var op = methodRef.OperatorToken ?? "";
                var target = methodRef.OperatorTarget?.Identifier
                    ?? methodRef.OperatorTarget?.ValueString;
                return string.IsNullOrEmpty(target) ? op : op + "<" + target + ">";
            }

            return NameText(methodRef.MemberName) ?? "";
        }

        /// <summary>
        /// Text of an <see cref="IBase2Ast"/> name node. <c>Identifier</c> defaults to <c>""</c>
        /// (not null) when unset on nodes such as <see cref="PhpNameAst"/>, so a plain
        /// <c>node.Identifier ?? node.ValueString</c> never falls through to <c>ValueString</c>.
        /// </summary>
        private static string? NameText(IBase2Ast? node)
        {
            if (node is null)
            {
                return null;
            }

            return string.IsNullOrEmpty(node.Identifier) ? node.ValueString : node.Identifier;
        }

        private static bool ExtensionDeclaresMember(
            IReadOnlyList<ObjectDeclarationSymbol> extensions,
            string? extensionFilter,
            string memberKey,
            PhpTraitMemberRefAst? methodRef)
        {
            var bareName = methodRef?.IsOperator == true
                ? (methodRef.OperatorToken ?? memberKey)
                : memberKey;

            foreach (var ext in extensions)
            {
                if (!string.IsNullOrEmpty(extensionFilter)
                    && !string.Equals(ext.Name, extensionFilter, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(ext.FullyQualifiedName.TrimStart('\\'), extensionFilter.TrimStart('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ext.Members.ContainsKey(bareName) || ext.Members.ContainsKey(memberKey))
                {
                    return true;
                }
            }

            return extensions.Count == 0;
        }
    }
}
