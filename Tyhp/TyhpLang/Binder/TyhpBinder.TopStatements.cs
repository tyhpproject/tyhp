using System.Linq;
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
        private void BindTopStatementList(PhpTopStatementListAst stmtList, IBaseScope parentScope)
        {
            var currentScope = parentScope;

            foreach (var stmt in stmtList.GetAllNotNull())
            {
                // A statement-form `namespace Foo;` (no braces) carries no captured body. Per PHP
                // semantics it applies to every following sibling statement until the next namespace
                // declaration or end of file. Establish its namespace scope and bind subsequent siblings
                // into it so their FullyQualifiedName (and therefore PSR-4 output path) includes the
                // namespace segment, matching the block-namespace form.
                if (stmt is PhpNamespaceDeclAst { TopStatements: null } statementNs
                    && !string.IsNullOrEmpty(statementNs.Identifier))
                {
                    currentScope = BindNamespaceDeclCore(statementNs.Identifier, null);
                    continue;
                }

                BindTopStatement(stmt, currentScope);
            }
        }

        private void BindTopStatement(ITopStatement stmt, IBaseScope parentScope)
        {
            switch (stmt)
            {
                case PhpBlockNamespaceDeclAst blockNs:
                    BindBlockNamespaceDecl(blockNs);
                    break;

                case PhpNamespaceDeclAst ns:
                    BindNamespaceDecl(ns);
                    break;

                case PhpObjectTypeDeclAst objDecl:
                    BindObjectTypeDecl(objDecl, parentScope);
                    break;

                case PhpFunctionDeclAst funcDecl:
                    BindFunctionDecl(funcDecl, parentScope);
                    break;

                case PhpImportDeclListAst importList:
                    BindImportDeclList(importList, parentScope);
                    break;

                case PhpConstDeclListAst constList:
                    BindConstDeclList(constList, parentScope);
                    break;

                case PhpDeclareAst declareAst when parentScope is FileScope:
                    BindFileLevelDeclare(declareAst);
                    break;

                case PhpDeclareAst declareAst:
                    BindDeclareBlock(declareAst, parentScope);
                    break;

                case PhpTopStatementListAst nestedList:
                    BindTopStatementList(nestedList, parentScope);
                    break;

                case TyhpTypeAliasAst typeAlias:
                {
                    var aliasName = typeAlias.Name?.ValueString ?? typeAlias.Identifier ?? "";
                    if (!string.IsNullOrEmpty(aliasName))
                    {
                        var aliasSymbol = new TypeAliasSymbol(
                            aliasName,
                            declaringNode: typeAlias,
                            sourceFile: _currentFileName);
                        aliasSymbol.AliasedType = typeAlias.TypeExpression;

                        switch (parentScope)
                        {
                            case FileScope fileScope:
                                if (!fileScope.AddChildSymbol(aliasSymbol))
                                {
                                    _diagnostics.AddErrorFromAst(
                                        MessageCode.BinderDuplicateSymbolDeclaration,
                                        typeAlias,
                                        _currentFileName,
                                        aliasSymbol.Name);
                                }
                                break;

                            case NamespaceBlockScope nsBlockScope:
                                if (!nsBlockScope.AddChildSymbol(aliasSymbol))
                                {
                                    _diagnostics.AddErrorFromAst(
                                        MessageCode.BinderDuplicateSymbolDeclaration,
                                        typeAlias,
                                        _currentFileName,
                                        aliasSymbol.Name);
                                }
                                break;
                        }
                    }
                    break;
                }

                case TyhpTypedVarExprAst typedVar when parentScope is FileScope or NamespaceBlockScope:
                {
                    var varName = typedVar.Variable?.VariableToken?.ValueString ?? "";
                    if (!string.IsNullOrEmpty(varName))
                    {
                        var varSymbol = new VariableSymbol(
                            varName,
                            declaringNode: typedVar,
                            sourceFile: _currentFileName);
                        varSymbol.DeclaredType = typedVar.TypeExpression;
                        varSymbol.IsRef = typedVar.IsRef;

                        bool added = parentScope switch
                        {
                            FileScope fs => fs.AddChildSymbol(varSymbol),
                            NamespaceBlockScope ns => ns.AddChildSymbol(varSymbol),
                            _ => false
                        };

                        if (!added)
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                typedVar,
                                _currentFileName,
                                varSymbol.Name);
                        }
                    }
                    break;
                }

                case TyhpdefImportObjectDeclAst tyhpdefObj:
                    BindTyhpdefObjectDecl(tyhpdefObj, parentScope);
                    break;

                case TyhpdefImportFunctionDeclAst tyhpdefFunc:
                    BindTyhpdefFunctionDecl(tyhpdefFunc, parentScope);
                    break;

                case TyhpdefImportConstAst tyhpdefConst:
                    BindTyhpdefConstDecl(tyhpdefConst, parentScope);
                    break;

                case TyhpdefImportVariableAst tyhpdefVar:
                    BindTyhpdefVariableDecl(tyhpdefVar, parentScope);
                    break;

                case TyhpExtensionDeclAst extensionDecl:
                    BindExtensionDeclaration(extensionDecl, parentScope);
                    break;

                case TyhpStructDeclAst structDecl:
                    BindStructDecl(structDecl, parentScope);
                    break;

                case PhpHaltCompilerAst:
                case UnexpectedNodeAst:
                case ErrorAst:
                    // Declaration-free no-ops at file/namespace level.
                    break;

                case IStatement statement:
                    // Top-level executable statements (expressions, echo, throw, include, etc.)
                    // — walk via the same statement binder used inside function bodies so
                    // nested name references are not left unbound.
                    BindStatementBlock(statement, parentScope);
                    break;

                default:
                    _diagnostics.AddWarningFromAst(
                        MessageCode.BinderUnknownError,
                        stmt,
                        _currentFileName,
                        $"Unhandled top-level statement type: {stmt.GetType().Name}");
                    break;
            }
        }

        private void BindBlockNamespaceDecl(PhpBlockNamespaceDeclAst nsDecl)
        {
            BindNamespaceDeclCore(nsDecl.Identifier, nsDecl.TopStatements);
        }

        private void BindNamespaceDecl(PhpNamespaceDeclAst nsDecl)
        {
            BindNamespaceDeclCore(nsDecl.Identifier, nsDecl.TopStatements);
        }

        private NamespaceBlockScope BindNamespaceDeclCore(string? identifier, PhpTopStatementListAst? topStatements)
        {
            var namespaceName = identifier ?? "";
            var namespaceScope = _globalScope.AddNamespaceScope(namespaceName);

            var blockSymbol = new NamespaceBlockSymbol(namespaceName, _currentFileScope);
            var blockScope = new NamespaceBlockScope(namespaceScope, blockSymbol);
            namespaceScope.AddChildScope(blockScope);

            if (topStatements != null)
            {
                BindTopStatementList(topStatements, blockScope);
            }

            return blockScope;
        }

        private void BindObjectTypeDecl(PhpObjectTypeDeclAst objDecl, IBaseScope parentScope)
        {
            if (objDecl.IsAnonymousClass)
            {
                BindAnonymousObjectTypeDecl(objDecl, parentScope);
                return;
            }

            var identifier = objDecl.Identifier;
            if (string.IsNullOrEmpty(identifier))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderUnknownError,
                    objDecl,
                    _currentFileName,
                    "Object declaration has no identifier — skipping binding.");
                return;
            }

            var modifiers = ConvertModifiers(objDecl.Modifiers);
            var symbol = new ObjectDeclarationSymbol(
                identifier,
                objDecl,
                _currentFileName,
                modifiers
            );

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
            PopulateGenericParametersFromGrammarAddon(
                objDecl.AstGrammarAddons,
                symbol.GenericParameters,
                _currentFileName);

            switch (parentScope)
            {
                case FileScope fileScope:
                {
                    if (!fileScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            objDecl,
                            _currentFileName,
                            symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScope);
                    BindObjectBody(objDecl, objScope, symbol);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!nsBlockScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            objDecl,
                            _currentFileName,
                            symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScope);
                    BindObjectBody(objDecl, objScope, symbol);
                    break;
                }

                default:
                    _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, objDecl, _currentFileName,
                        $"Unexpected parent scope type '{parentScope.GetType().Name}' for object type declaration");
                    break;
            }
        }

        private void BindAnonymousObjectTypeDecl(PhpObjectTypeDeclAst objDecl, IBaseScope parentScope)
        {
            if (parentScope is not IObjectDeclarationScopeParent objParent)
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, objDecl, _currentFileName,
                    $"Unexpected parent scope type '{parentScope.GetType().Name}' for anonymous object type declaration");
                return;
            }

            var name = objDecl.Identifier ?? $"anon@{objDecl.Line}:{objDecl.Column}";
            var modifiers = ConvertModifiers(objDecl.Modifiers);
            var symbol = new ObjectDeclarationSymbol(name, objDecl, _currentFileName, modifiers);
            symbol.ObjectKind = PhpTypeDeclType.Class;

            var objScope = new ObjectDeclarationScope(objParent, symbol);
            objParent.AddObjectDeclarationChildScope(objScope);
            BindObjectBody(objDecl, objScope, symbol);
        }

        private void BindFunctionDecl(PhpFunctionDeclAst funcDecl, IBaseScope parentScope)
        {
            // Skip bodyless overload signatures only. Named short-function implementations
            // (`fn name(...) => expr;`) also arrive via the short-function grammar alt but already
            // have a desugared body and must be bound like normal functions.
            if (OverloadSignatureHelper.IsErasableFunctionOverloadSignature(funcDecl))
            {
                return;
            }

            var identifier = funcDecl.Identifier;
            if (string.IsNullOrEmpty(identifier))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderUnknownError,
                    funcDecl,
                    _currentFileName,
                    "Function declaration has no identifier — skipping binding.");
                return;
            }

            var modifiers = ConvertModifiers(null);
            var symbol = new FunctionDeclarationSymbol(
                identifier,
                funcDecl,
                _currentFileName,
                modifiers
            );
            symbol.IsAsync = HasAsyncModifier(funcDecl);
            PopulateGenericParametersFromGrammarAddon(
                funcDecl.AstGrammarAddons,
                symbol.GenericParameters,
                _currentFileName,
                SymbolType.FunctionGenericTypeParameter);

            switch (parentScope)
            {
                case FileScope fileScope:
                {
                    if (!fileScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            funcDecl,
                            _currentFileName,
                            symbol.Name);
                    }

                    var funcScope = new FunctionDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(funcScope);
                    BindFunctionBody(funcDecl, funcScope, symbol);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!nsBlockScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            funcDecl,
                            _currentFileName,
                            symbol.Name);
                    }

                    var funcScope = new FunctionDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(funcScope);
                    BindFunctionBody(funcDecl, funcScope, symbol);
                    break;
                }

                // Nested named function inside a statement/code block (FOUND_BUGS #36).
                // FileScope / NamespaceBlockScope are handled above so their AddChildSymbol
                // uniqueness rules still apply; body-nested functions live on the parent via
                // AddFunctionDeclarationChildScope (CodeBlockScope → _additionalChildScopes),
                // the same path item 33 opened for method-body anonymous classes.
                case IFunctionDeclarationScopeParent funcParent:
                {
                    var funcScope = new FunctionDeclarationScope(funcParent, symbol);
                    funcParent.AddFunctionDeclarationChildScope(funcScope);
                    BindFunctionBody(funcDecl, funcScope, symbol);
                    break;
                }

                default:
                    _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, funcDecl, _currentFileName,
                        $"Unexpected parent scope type '{parentScope.GetType().Name}' for function declaration");
                    break;
            }
        }

        private void BindImportDeclList(PhpImportDeclListAst importList, IBaseScope parentScope)
        {
            foreach (var importDecl in importList.GetAllNotNull())
            {
                var namespaceName = importDecl.NamespaceName ?? "";
                var aliasName = string.IsNullOrEmpty(importDecl.Identifier) ? null : importDecl.Identifier;

                var useType = PhpUseType.Class;
                if (importDecl.UseType != null)
                {
                    useType = importDecl.UseType.ValueString?.ToLowerInvariant() switch
                    {
                        "const" => PhpUseType.Const,
                        "function" => PhpUseType.Function,
                        _ => PhpUseType.Class
                    };
                }

                var effectiveName = aliasName ?? namespaceName[(namespaceName.LastIndexOf('\\') + 1)..];
                if (string.IsNullOrEmpty(effectiveName))
                {
                    _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, importDecl, _currentFileName, "import name is empty");
                    continue;
                }

                var symbol = new UseIncludeSymbol(
                    effectiveName,
                    namespaceName,
                    importDecl,
                    sourceFile: _currentFileName,
                    aliasName: aliasName != namespaceName ? aliasName : null,
                    useType: useType
                );

                switch (parentScope)
                {
                    case FileScope fileScope:
                        if (!fileScope.AddChildSymbol(symbol))
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                importDecl,
                                _currentFileName,
                                symbol.Name);
                        }
                        break;

                    case NamespaceBlockScope nsBlockScope:
                        if (!nsBlockScope.AddChildSymbol(symbol))
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                importDecl,
                                _currentFileName,
                                symbol.Name);
                        }
                        break;
                    default:
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderInvalidSymbolTypeForParent,
                            importDecl,
                            _currentFileName,
                            "use/import statement");
                        break;
                }
            }
        }

        private void BindConstDeclList(PhpConstDeclListAst constList, IBaseScope parentScope)
        {
            foreach (var constDecl in constList.GetAllNotNull())
            {
                var constName = constDecl.Identifier ?? "";
                if (string.IsNullOrEmpty(constName))
                {
                    _diagnostics.AddErrorFromAst(MessageCode.BinderUnknownError, constDecl, _currentFileName, "constant declaration identifier");
                    continue;
                }

                var symbol = new ConstantSymbol(
                    constName,
                    sourceFile: _currentFileName,
                    // Attributes on PHP 8.5 attributed top-level `const` attach to the list AST
                    // (single declarator). Pass 2 resolves them via ResolveDeclarationAttributes.
                    declaringNode: constList
                );

                switch (parentScope)
                {
                    case FileScope fileScope:
                        if (!fileScope.AddChildSymbol(symbol))
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                constDecl,
                                _currentFileName,
                                symbol.Name);
                        }
                        break;

                    case NamespaceBlockScope nsBlockScope:
                        if (!nsBlockScope.AddChildSymbol(symbol))
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.BinderDuplicateSymbolDeclaration,
                                constDecl,
                                _currentFileName,
                                symbol.Name);
                        }
                        break;
                    default:
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderInvalidSymbolTypeForParent,
                            constDecl,
                            _currentFileName,
                            "constant declaration");
                        break;
                }
            }
        }

        private void BindFileLevelDeclare(PhpDeclareAst declareAst)
        {
            if (_currentFileScope == null || declareAst.Declarations == null) return;

            foreach (var decl in declareAst.Declarations.GetAllNotNull())
            {
                var key = decl.Identifier ?? "";
                var valueExpr = decl.Value;
                var value = valueExpr?.ValueString ?? "1";

                _currentFileScope.AddFileDeclareDirective(key, value);
            }
        }

        private void BindDeclareBlock(PhpDeclareAst declareAst, IBaseScope parentScope)
        {
            if (parentScope is not ICodeBlockScopeParent codeBlockParent) return;

            var symbol = new DeclareBlockSymbol("declare", sourceFile: _currentFileName);

            if (declareAst.Declarations != null)
            {
                foreach (var decl in declareAst.Declarations.GetAllNotNull())
                {
                    var key = decl.Identifier ?? "";
                    var valueExpr = decl.Value;
                    var value = valueExpr?.ValueString ?? "1";
                    symbol.Directives[key] = value;
                }
            }

            var declareScope = new DeclareBlockScope(codeBlockParent, symbol);
            codeBlockParent.AddCodeBlockChildScope(declareScope);

            if (declareAst.Body != null)
            {
                BindStatementBlock(declareAst.Body, declareScope);
            }
        }

        private static void PopulateGenericParametersFromGrammarAddon(
            IReadOnlyDictionary<string, IBase2Ast> grammarAddons,
            List<GenericTypeParameterSymbol> targetList,
            string sourceFile,
            SymbolType genericParameterKind = SymbolType.ClassGenericTypeParameter,
            string addonKey = "identifier")
        {
            if (!grammarAddons.TryGetValue(addonKey, out var addon) ||
                addon is not TyhpGenericsTypeArgumentListAst genericList)
            {
                return;
            }

            PopulateGenericParameters(genericList, targetList, sourceFile, genericParameterKind);
        }

        private static void PopulateGenericParameters(
            TyhpGenericsTypeArgumentListAst genericList,
            List<GenericTypeParameterSymbol> targetList,
            string sourceFile,
            SymbolType genericParameterKind)
        {
            foreach (var genericArg in genericList.GetAllNotNull())
            {
                var name = !string.IsNullOrEmpty(genericArg.Identifier)
                    ? genericArg.Identifier
                    : genericArg.Name?.ValueString;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var genericParam = new GenericTypeParameterSymbol(
                    name,
                    genericParameterKind,
                    genericArg,
                    sourceFile);
                genericParam.Constraint = genericArg.TypeConstraint;
                genericParam.DefaultType = genericArg.DefaultType;
                targetList.Add(genericParam);
            }
        }

        private static MemberModifier ConvertModifiers(PhpModifierListAst? modifiers)
        {
            if (modifiers == null) return MemberModifier.None;

            var result = MemberModifier.None;

            foreach (var mod in modifiers.Modifiers)
            {
                result |= mod switch
                {
                    PhpModifier.Public => MemberModifier.Public,
                    PhpModifier.Protected => MemberModifier.Protected,
                    PhpModifier.Private => MemberModifier.Private,
                    PhpModifier.Static => MemberModifier.Static,
                    PhpModifier.Abstract => MemberModifier.Abstract,
                    PhpModifier.Final => MemberModifier.Final,
                    PhpModifier.Readonly => MemberModifier.Readonly,
                    PhpModifier.Var => MemberModifier.Var,
                    _ => MemberModifier.None
                };
            }

            // Tyhp `async` is not a PhpModifier; the visitor attaches it as an "isAsync" addon.
            if (modifiers.AstGrammarAddons.ContainsKey("isAsync"))
            {
                result |= MemberModifier.Async;
            }

            return result;
        }

        /// <summary>
        /// True when a declaration carries Tyhp <c>async</c> via grammar addons
        /// (<c>modifiers</c> / <c>isAsync</c>) used by free functions and some method forms.
        /// </summary>
        private static bool HasAsyncModifier(IBase2Ast node)
        {
            if (node.AstGrammarAddons.ContainsKey("isAsync"))
            {
                return true;
            }

            if (!node.AstGrammarAddons.TryGetValue("modifiers", out var addon))
            {
                return false;
            }

            return addon switch
            {
                TokenValueListAst list => list.GetAllNotNull().Any(IsAsyncToken),
                TokenValueAst token => IsAsyncToken(token),
                _ => false,
            };
        }

        private static bool IsAsyncToken(TokenValueAst token) =>
            string.Equals(token.ValueString, "async", StringComparison.OrdinalIgnoreCase)
            || token.ValueInt64 == Tyhp.TyhpLang.Parser.TyhpParser.T_TYHP_ASYNC;

        private void BindStructDecl(TyhpStructDeclAst structDecl, IBaseScope parentScope)
        {
            var identifier = structDecl.Identifier;
            if (string.IsNullOrEmpty(identifier))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderUnknownError,
                    structDecl,
                    _currentFileName,
                    "Struct declaration has no identifier — skipping binding.");
                return;
            }

            var symbol = new ObjectDeclarationSymbol(
                identifier,
                structDecl,
                _currentFileName,
                MemberModifier.Public)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsStruct = true,
                ExtendsType = structDecl.Extends as ITypeExpression,
            };

            PopulateGenericParametersFromGrammarAddon(
                structDecl.AstGrammarAddons,
                symbol.GenericParameters,
                _currentFileName);

            switch (parentScope)
            {
                case FileScope fileScope:
                {
                    if (!fileScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            structDecl,
                            _currentFileName,
                            symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScope);
                    BindStructBody(structDecl, objScope);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!nsBlockScope.AddChildSymbol(symbol))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.BinderDuplicateSymbolDeclaration,
                            structDecl,
                            _currentFileName,
                            symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScope);
                    BindStructBody(structDecl, objScope);
                    break;
                }
            }
        }

        private void BindStructBody(TyhpStructDeclAst structDecl, ObjectDeclarationScope objScope)
        {
            foreach (var property in structDecl.PropertyList?.GetAllNotNull() ?? [])
            {
                var propName = property.Property?.Identifier ?? property.Identifier;
                if (string.IsNullOrEmpty(propName))
                {
                    continue;
                }

                var propSymbol = new ObjectPropertySymbol(
                    propName,
                    sourceFile: _currentFileName,
                    declaringNode: property,
                    visibility: MemberModifier.Public)
                {
                    DeclaredType = property.TypeExpression,
                    DefaultValue = property.Property?.DefaultValue,
                };

                if (!objScope.AddChildSymbol(propSymbol))
                {
                    _diagnostics.AddErrorFromAst(
                        MessageCode.BinderDuplicateSymbolDeclaration,
                        property,
                        _currentFileName,
                        propSymbol.Name);
                }
                else
                {
                    RegisterObjectMember(objScope, propSymbol, propName);
                }
            }
        }

        // Defined in TyhpBinder.ObjectBody.cs
        partial void BindObjectBody(PhpObjectTypeDeclAst objDecl, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol);

        // Defined in another partial
        partial void BindFunctionBody(PhpFunctionDeclAst funcDecl, FunctionDeclarationScope funcScope, FunctionDeclarationSymbol symbol);

        // Defined in another partial
        partial void BindStatementBlock(IStatement stmt, IBaseScope parentScope);
    }
}
