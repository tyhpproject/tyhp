using System.Collections.Generic;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        private readonly Dictionary<ObjectDeclarationSymbol, ObjectDeclarationScope> _syntheticInlineExtensionScopes = new();

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
                    if (!fileScope.AddChildSymbol(synthSymbol))
                    {
                        _diagnostics.AddError(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            _currentFileName,
                            0,
                            0,
                            synthName);
                        return ownerScope;
                    }

                    var fsScope = new ObjectDeclarationScope(fileScope, synthSymbol);
                    fileScope.AddChildScope(fsScope);
                    _syntheticInlineExtensionScopes[ownerClass] = fsScope;
                    return fsScope;

                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.AddChildSymbol(synthSymbol))
                    {
                        _diagnostics.AddError(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            _currentFileName,
                            0,
                            0,
                            synthName);
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

            var symbol = new ObjectDeclarationSymbol(name, decl, _currentFileName)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsExtension = true,
            };

            if (decl.Extends is ITypeExpression extExpr)
                symbol.ExtendsType = extExpr;

            switch (parentScope)
            {
                case FileScope fileScope:
                    if (!fileScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            decl,
                            _currentFileName,
                            symbol.Name);
                        return;
                    }

                    var objScopeFs = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScopeFs);
                    BindExtensionMemberList(decl.FunctionList, objScopeFs, symbol);
                    break;

                case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            decl,
                            _currentFileName,
                            symbol.Name);
                        return;
                    }

                    var objScopeNs = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScopeNs);
                    BindExtensionMemberList(decl.FunctionList, objScopeNs, symbol);
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

        private void BindExtensionMemberList(TyhpExtensionFunctionListAst? list, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol)
        {
            if (list == null) return;

            foreach (var member in list.GetAllNotNull())
            {
                switch (member)
                {
                    case PhpFunctionDeclAst funcDecl:
                        BindExtensionFunctionDecl(funcDecl, objScope);
                        break;

                    case TyhpOperatorOverloadAst opOverload:
                        BindOperatorOverload(opOverload, objScope);
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
        }

        private void BindExtensionFunctionDecl(PhpFunctionDeclAst funcDecl, ObjectDeclarationScope objScope)
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

            var methodSymbol = new ObjectMethodSymbol(
                name,
                funcDecl,
                _currentFileName,
                MemberModifier.Static,
                SymbolType.StaticObjectMethod);

            methodSymbol.ReturnType = funcDecl.ReturnType;
            methodSymbol.IsStatic = true;

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

            if (!objScope.AddChildSymbol(methodSymbol))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderDuplicateSymbolDeclaration,
                    funcDecl,
                    _currentFileName,
                    methodSymbol.Name);
                return;
            }
            RegisterObjectMember(objScope, methodSymbol, name);

            var staticScope = new StaticMethodDeclarationScope(objScope, methodSymbol);
            objScope.AddChildScope(staticScope);
            BindMethodParameters(funcDecl.Parameters, staticScope, methodSymbol, objScope);
            if (funcDecl.Body != null)
                BindStatementBlock(funcDecl.Body, staticScope);
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

            var methodSymbol = new ObjectMethodSymbol(
                name,
                methodDecl,
                _currentFileName,
                MemberModifier.Static,
                SymbolType.StaticObjectMethod);

            methodSymbol.ReturnType = methodDecl.ReturnType;
            methodSymbol.IsStatic = true;

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

            if (!synthScope.AddChildSymbol(methodSymbol))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderDuplicateSymbolDeclaration,
                    methodDecl,
                    _currentFileName,
                    methodSymbol.Name);
                return;
            }
            RegisterObjectMember(synthScope, methodSymbol, name);

            var staticScope = new StaticMethodDeclarationScope(synthScope, methodSymbol);
            synthScope.AddChildScope(staticScope);
            BindMethodParameters(methodDecl.Parameters, staticScope, methodSymbol, synthScope);
            if (methodDecl.Body != null)
                BindStatementBlock(methodDecl.Body, staticScope);
        }

        private void BindTyhpdefClassUseExtension(TyhpImportExtensionAst importExt, ObjectDeclarationSymbol symbol)
        {
            if (importExt.UseDeclarations == null) return;

            foreach (var decl in importExt.UseDeclarations.GetAllNotNull())
            {
                var ns = decl.NamespaceName ?? "";
                if (string.IsNullOrEmpty(ns))
                    continue;

                symbol.PendingTyhpdefUseExtensionNamespaces ??= new List<string>();
                symbol.PendingTyhpdefUseExtensionNamespaces.Add(ns);
            }

            ProcessExtensionUseAdaptations(importExt, symbol);
        }

        private static void ProcessExtensionUseAdaptations(TyhpImportExtensionAst importExt, ObjectDeclarationSymbol symbol)
        {
            if (importExt.Adaptations == null) return;

            foreach (var adaptation in importExt.Adaptations.GetAllNotNull())
            {
                switch (adaptation)
                {
                    case PhpTraitPrecedenceAst precedence:
                    {
                        var methodRef = precedence.MethodReference;
                        var methodName = methodRef?.MemberName?.Identifier ?? methodRef?.MemberName?.ValueString ?? "";
                        var preferredExt = methodRef?.TraitName?.Identifier ?? methodRef?.TraitName?.ValueString ?? "";

                        if (!string.IsNullOrEmpty(methodName) && !string.IsNullOrEmpty(preferredExt))
                        {
                            symbol.ExtensionUseMethodPrecedence ??=
                                new Dictionary<string, string>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);
                            symbol.ExtensionUseMethodPrecedence[methodName] = preferredExt;
                        }

                        break;
                    }
                    case PhpTraitAliasAst alias:
                    {
                        var aliasName = alias.Identifier;
                        var methodRef = alias.MethodReference;
                        var originalMethod = methodRef?.MemberName?.Identifier ?? methodRef?.MemberName?.ValueString ?? "";
                        var extNameStr = methodRef?.TraitName?.Identifier ?? methodRef?.TraitName?.ValueString;

                        if (!string.IsNullOrEmpty(aliasName) && !string.IsNullOrEmpty(originalMethod))
                        {
                            symbol.ExtensionUseMethodAliases ??=
                                new Dictionary<string, (string?, string)>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);
                            symbol.ExtensionUseMethodAliases[aliasName] = (extNameStr, originalMethod);
                        }

                        break;
                    }
                }
            }
        }
    }
}
