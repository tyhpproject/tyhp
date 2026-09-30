using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        private void BindTopStatementList(PhpTopStatementListAst stmtList, IBaseScope parentScope)
        {
            var currentScope = parentScope;

            foreach (var stmt in stmtList.GetAllNotNull())
            {
                // Inactive file-level `declare(php=…);` skips the file's declarations silently.
                // Still bind declare statements so other directives (and 4301) are recorded.
                if (_currentFilePhpGateInactive && stmt is not PhpDeclareAst)
                {
                    continue;
                }

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

                case TyhpImportExtensionAst importExt:
                    BindFileUseExtension(importExt, parentScope);
                    break;

                case TyhpdefStandaloneExtensionDeclAst tyhpdefExt:
                    BindTyhpdefStandaloneExtensionDecl(tyhpdefExt, parentScope);
                    break;

                case PhpConstDeclListAst constList:
                    BindConstDeclList(constList, parentScope);
                    break;

                case PhpDeclareAst declareAst:
                    BindDeclare(declareAst, parentScope);
                    break;

                case PhpTopStatementListAst nestedList:
                    BindTopStatementList(nestedList, parentScope);
                    break;

                case TyhpTypeAliasAst typeAlias:
                {
                    if (typeAlias.StructShape is { } structShape)
                    {
                        BindStructShapeAlias(typeAlias, structShape, parentScope);
                        break;
                    }

                    var aliasName = typeAlias.Name?.ValueString ?? typeAlias.Identifier ?? "";
                    if (!string.IsNullOrEmpty(aliasName))
                    {
                        var aliasSymbol = new TypeAliasSymbol(
                            aliasName,
                            declaringNode: typeAlias,
                            sourceFile: _currentFileName);
                        aliasSymbol.AliasedType = typeAlias.TypeExpression;
                        ApplyInternal(aliasSymbol, typeAlias);
                        ValidateObjectShapeAlias(typeAlias);
                        if (typeAlias.GenericArguments != null)
                        {
                            PopulateGenericParameters(
                                typeAlias.GenericArguments,
                                aliasSymbol.GenericParameters,
                                _currentFileName,
                                SymbolType.ClassGenericTypeParameter);
                        }

                        switch (parentScope)
                        {
                            case FileScope fileScope:
                                BindSourceFileLevelTypeAlias(typeAlias, aliasSymbol, fileScope);
                                break;

                            case NamespaceBlockScope nsBlockScope:
                                BindSourceFileLevelTypeAlias(typeAlias, aliasSymbol, nsBlockScope);
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

                        bool added;
                        IBaseSymbol? existing;
                        switch (parentScope)
                        {
                            case FileScope fs:
                                added = fs.TryAddChildSymbol(varSymbol, out existing);
                                break;
                            case NamespaceBlockScope ns:
                                added = ns.TryAddChildSymbol(varSymbol, out var nsExisting);
                                existing = nsExisting;
                                break;
                            default:
                                added = false;
                                existing = null;
                                break;
                        }

                        if (!added)
                        {
                            ReportBinderDuplicate(typedVar, existing, varSymbol.Name);
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

            ReportReservedExtensionBackerName(identifier, objDecl);

            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    objDecl,
                    parentScope,
                    identifier,
                    SymbolType.ObjectTypeDeclaration,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var modifiers = ConvertModifiers(objDecl.Modifiers);
            var symbol = new ObjectDeclarationSymbol(
                identifier,
                objDecl,
                _currentFileName,
                modifiers
            );
            StampPhpVersionConstraints(symbol, phpConstraints);
            ApplyInternal(symbol, objDecl, modifiers);

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
            symbol.BackingType = objDecl.BackingType;
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
                    if (!fileScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportBinderDuplicate(objDecl, existing, symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScope);
                    BindObjectBody(objDecl, objScope, symbol);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportBinderDuplicate(objDecl, existing, symbol.Name);
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
            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    objDecl,
                    parentScope,
                    name,
                    SymbolType.ObjectTypeDeclaration,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var modifiers = ConvertModifiers(objDecl.Modifiers);
            var symbol = new ObjectDeclarationSymbol(name, objDecl, _currentFileName, modifiers);
            StampPhpVersionConstraints(symbol, phpConstraints);
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

            if (!ShouldRegisterPhpVersionGatedDeclaration(
                    funcDecl,
                    parentScope,
                    identifier,
                    SymbolType.FunctionDeclaration,
                    illegalAttributeTarget: false,
                    out var phpConstraints))
            {
                return;
            }

            var modifiers = ConvertModifiers(null);
            var symbol = new FunctionDeclarationSymbol(
                identifier,
                funcDecl,
                _currentFileName,
                modifiers
            );
            StampPhpVersionConstraints(symbol, phpConstraints);
            symbol.IsAsync = HasAsyncModifier(funcDecl);
            ApplyInternal(symbol, funcDecl, modifiers);
            PopulateGenericParametersFromGrammarAddon(
                funcDecl.AstGrammarAddons,
                symbol.GenericParameters,
                _currentFileName,
                SymbolType.FunctionGenericTypeParameter);

            switch (parentScope)
            {
                case FileScope fileScope:
                {
                    if (!fileScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportFunctionDeclarationConflict(funcDecl, existing, symbol.Name);
                    }

                    var funcScope = new FunctionDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(funcScope);
                    BindFunctionBody(funcDecl, funcScope, symbol);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportFunctionDeclarationConflict(funcDecl, existing, symbol.Name);
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

                if (importList.IsGlobal || importDecl.IsGlobal)
                {
                    _globalScope.GlobalImports.Add(symbol);
                }

                switch (parentScope)
                {
                    case FileScope fileScope:
                        if (!fileScope.TryAddChildSymbol(symbol, out var existing))
                        {
                            ReportBinderDuplicate(
                                importDecl,
                                existing,
                                symbol.Name,
                                existing is UseIncludeSymbol
                                    ? MessageCode.BinderDuplicateUseAlias
                                    : MessageCode.BinderDuplicateSymbolDeclaration);
                        }
                        break;

                    case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var nsExisting))
                    {
                        ReportBinderDuplicate(
                            importDecl,
                            nsExisting,
                            symbol.Name,
                            nsExisting is UseIncludeSymbol
                                ? MessageCode.BinderDuplicateUseAlias
                                : MessageCode.BinderDuplicateSymbolDeclaration);
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

                if (!ShouldRegisterPhpVersionGatedDeclaration(
                        constList,
                        parentScope,
                        constName,
                        SymbolType.Constant,
                        illegalAttributeTarget: false,
                        out var phpConstraints,
                        constDecl))
                {
                    continue;
                }

                var symbol = new ConstantSymbol(
                    constName,
                    sourceFile: _currentFileName,
                    // Attributes on PHP 8.5 attributed top-level `const` attach to the list AST
                    // (single declarator). Pass 2 resolves them via ResolveDeclarationAttributes.
                    declaringNode: constList
                );
                StampPhpVersionConstraints(symbol, phpConstraints);
                ApplyInternal(symbol, constList);
                constList.BoundSymbol ??= symbol;

                switch (parentScope)
                {
                    case FileScope fileScope:
                        if (!fileScope.TryAddChildSymbol(symbol, out var existing))
                        {
                            ReportBinderDuplicate(constDecl, existing, symbol.Name);
                        }
                        break;

                    case NamespaceBlockScope nsBlockScope:
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var nsExisting))
                    {
                        ReportBinderDuplicate(constDecl, nsExisting, symbol.Name);
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

        private const string PhpDeclareDirectiveKey = "php";

        /// <summary>
        /// Default compile target when <c>output.phpVersion</c> is missing. Checker emits 4306
        /// once per compilation when compilation options record that the version was defaulted.
        /// This method does not emit that warning.
        /// </summary>
        private const string DefaultPhpVersionTarget = "8.2";

        private void BindDeclare(PhpDeclareAst declareAst, IBaseScope parentScope)
        {
            var directives = ReadDeclareDirectives(declareAst);
            var phpAlone = TryGetAlonePhpConstraint(directives, out var phpConstraint);
            var extAlone = TryGetAloneExtConstraint(directives, out var extConstraint);
            var hasPhp = directives.Exists(d => IsPhpDeclareKey(d.Key));
            var hasExt = directives.Exists(d => IsExtDeclareKey(d.Key));
            if (hasPhp && !phpAlone)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.CheckerPhpVersionDeclareNotAlone,
                    declareAst,
                    _currentFileName);
            }

            if (hasExt && !extAlone)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefExtDeclareNotAlone,
                    declareAst,
                    _currentFileName);
            }

            if (!IsDeclareBlockForm(declareAst))
            {
                if (_phpDeclareBlockDepth == 0)
                {
                    BindFileLevelDeclare(directives, phpAlone, extAlone);
                }

                return;
            }

            BindDeclareBlock(
                declareAst,
                parentScope,
                directives,
                phpAlone ? phpConstraint : null,
                extAlone ? extConstraint : null);
        }

        private void BindFileLevelDeclare(
            List<DeclareDirective> directives,
            bool phpAlone,
            bool extAlone)
        {
            if (_currentFileScope == null)
            {
                return;
            }

            foreach (var directive in directives)
            {
                if (IsPhpDeclareKey(directive.Key) && !phpAlone)
                {
                    continue;
                }

                if (IsExtDeclareKey(directive.Key) && !extAlone)
                {
                    continue;
                }

                _currentFileScope.TryAddFileDeclareDirective(directive.Key, directive.Value, out _);
            }
        }

        private void BindDeclareBlock(
            PhpDeclareAst declareAst,
            IBaseScope parentScope,
            List<DeclareDirective> directives,
            string? phpConstraint,
            string? extConstraint = null)
        {
            if (parentScope is not ICodeBlockScopeParent codeBlockParent)
            {
                return;
            }

            var symbol = new DeclareBlockSymbol("declare", sourceFile: _currentFileName);
            symbol.DeclaringAstNode = declareAst;
            declareAst.BoundSymbol = symbol;
            foreach (var directive in directives)
            {
                symbol.Directives[directive.Key] = directive.Value;
            }

            var inactive = false;
            var phpUnsatisfied = false;
            var constraintValid = true;
            if (phpConstraint != null)
            {
                var result = PhpVersionConstraint.Evaluate(GetTargetPhpVersion(), phpConstraint);
                constraintValid = result.ConstraintIsValid;
                inactive = !result.ConstraintIsValid || !result.IsSatisfied;
                phpUnsatisfied = result.ConstraintIsValid && !result.IsSatisfied;
                symbol.PhpVersionConstraint = phpConstraint;
            }

            if (extConstraint != null)
            {
                symbol.ExtGate = extConstraint.Trim();
                var insideFunctionBody = parentScope is not (FileScope or NamespaceBlockScope);
                if (!TryEvaluateExtGate(extConstraint, declareAst, out var extSatisfied, insideFunctionBody)
                    || !extSatisfied)
                {
                    inactive = true;
                }
            }

            symbol.IsPhpVersionConstraintValid = constraintValid;
            symbol.IsPhpVersionGateInactive = inactive;

            if (phpConstraint != null)
            {
                _phpVersionConstraintStack.Add(phpConstraint);
            }

            if (extConstraint != null)
            {
                _extGateStack.Add(extConstraint.Trim());
            }

            symbol.EffectivePhpVersionConstraints = _phpVersionConstraintStack.ToArray();
            symbol.EffectiveExtGates = _extGateStack.ToArray();

            var declareScope = new DeclareBlockScope(codeBlockParent, symbol);
            codeBlockParent.AddCodeBlockChildScope(declareScope);

            _phpDeclareBlockDepth++;
            try
            {
                if (inactive || declareAst.Body == null)
                {
                    if (phpUnsatisfied && declareAst.Body != null)
                    {
                        RecordUncompiledDeclareBlockDeclarations(
                            declareAst.Body,
                            parentScope,
                            [.. _phpVersionConstraintStack]);
                    }

                    return;
                }

                BindDeclareBlockBody(declareAst.Body, parentScope, declareScope);
            }
            finally
            {
                _phpDeclareBlockDepth--;
                if (phpConstraint != null && _phpVersionConstraintStack.Count > 0)
                {
                    _phpVersionConstraintStack.RemoveAt(_phpVersionConstraintStack.Count - 1);
                }

                if (extConstraint != null && _extGateStack.Count > 0)
                {
                    _extGateStack.RemoveAt(_extGateStack.Count - 1);
                }
            }
        }

        /// <summary>
        /// An inactive <c>declare(php=…)</c> block binds nothing, but the function and type names it
        /// declares still count as variants of same-named declarations that are compiled
        /// (see <see cref="MarkDeclarationsWithUncompiledVersionVariants"/>).
        /// </summary>
        private void RecordUncompiledDeclareBlockDeclarations(
            IStatement body,
            IBaseScope parentScope,
            List<string> constraints)
        {
            IEnumerable<IBase2Ast?> children = body is PhpStatementBlockAst block
                ? block.GetAllNotNull()
                : [body];

            foreach (var child in children)
            {
                switch (child)
                {
                    case PhpFunctionDeclAst function:
                        RecordUncompiledDeclaration(
                            function.Identifier, SymbolType.FunctionDeclaration, parentScope, constraints);
                        break;

                    case PhpObjectTypeDeclAst { IsAnonymousClass: false } objectType:
                        RecordUncompiledDeclaration(
                            objectType.Identifier, SymbolType.ObjectTypeDeclaration, parentScope, constraints);
                        break;

                    case PhpDeclareAst nested when IsDeclareBlockForm(nested) && nested.Body != null:
                    {
                        var nestedConstraints = new List<string>(constraints);
                        if (TryGetAlonePhpConstraint(ReadDeclareDirectives(nested), out var nestedPhp))
                        {
                            nestedConstraints.Add(nestedPhp);
                        }

                        RecordUncompiledDeclareBlockDeclarations(nested.Body, parentScope, nestedConstraints);
                        break;
                    }
                }
            }
        }

        private void RecordUncompiledDeclaration(
            string? name,
            SymbolType symbolType,
            IBaseScope parentScope,
            IReadOnlyList<string> constraints)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var key = new GatedDeclarationKey(
                GetGatedContainerKey(parentScope),
                GatedNameKey(name, symbolType),
                NormalizeGatedKind(symbolType));
            if (!_uncompiledVersionVariants.TryGetValue(key, out var variants))
            {
                variants = [];
                _uncompiledVersionVariants[key] = variants;
            }

            variants.Add(constraints);
        }

        private void BindDeclareBlockBody(
            IStatement body,
            IBaseScope parentScope,
            DeclareBlockScope declareScope)
        {
            // Gate blocks are compile-time only. At file/namespace level, hoist declarations into
            // the enclosing scope so they resolve as normal globals. Inside functions, bind into
            // the declare scope like other statement blocks.
            if (parentScope is FileScope or NamespaceBlockScope)
            {
                if (body is PhpStatementBlockAst block)
                {
                    foreach (var child in block.GetAllNotNull())
                    {
                        BindTopStatement(child, parentScope);
                    }

                    return;
                }

                BindTopStatement(body, parentScope);
                return;
            }

            BindStatementBlock(body, declareScope);
        }

        private void ApplyFileLevelPhpGates(SrcFileAst srcFile, FileSymbol? fileSymbol)
        {
            _currentFilePhpGateInactive = false;
            if (fileSymbol == null)
            {
                return;
            }

            var constraints = new List<string>();
            var sawInvalid = false;
            var sawUnsatisfied = false;
            _pendingFileExtGates = [];

            foreach (var child in srcFile.AstChildren)
            {
                if (child is PhpTopStatementListAst list)
                {
                    CollectFileLevelPhpGates(list, constraints, ref sawInvalid, ref sawUnsatisfied);
                }
            }

            var fileExtGates = _pendingFileExtGates;
            _pendingFileExtGates = null;

            foreach (var constraint in constraints)
            {
                fileSymbol.AddPhpVersionConstraint(constraint);
            }

            fileSymbol.IsPhpVersionConstraintValid = !sawInvalid;
            fileSymbol.IsPhpVersionGateInactive = sawInvalid || sawUnsatisfied;
            _currentFilePhpGateInactive = fileSymbol.IsPhpVersionGateInactive;

            if (_currentFilePhpGateInactive)
            {
                return;
            }

            foreach (var constraint in constraints)
            {
                _phpVersionConstraintStack.Add(constraint);
            }

            _extGateStack.AddRange(fileExtGates);
        }

        private void CollectFileLevelPhpGates(
            ITopStatement stmt,
            List<string> constraints,
            ref bool sawInvalid,
            ref bool sawUnsatisfied)
        {
            switch (stmt)
            {
                case PhpTopStatementListAst list:
                    foreach (var child in list.GetAllNotNull())
                    {
                        CollectFileLevelPhpGates(child, constraints, ref sawInvalid, ref sawUnsatisfied);
                    }
                    break;

                case PhpNamespaceDeclAst ns when ns.TopStatements != null:
                    CollectFileLevelPhpGates(ns.TopStatements, constraints, ref sawInvalid, ref sawUnsatisfied);
                    break;

                case PhpBlockNamespaceDeclAst ns when ns.TopStatements != null:
                    CollectFileLevelPhpGates(ns.TopStatements, constraints, ref sawInvalid, ref sawUnsatisfied);
                    break;

                case PhpDeclareAst declareAst when !IsDeclareBlockForm(declareAst):
                {
                    var directives = ReadDeclareDirectives(declareAst);
                    if (TryGetAloneExtConstraint(directives, out var extSpec))
                    {
                        if (!TryEvaluateExtGate(extSpec, declareAst, out var extSatisfied) || !extSatisfied)
                        {
                            sawUnsatisfied = true;
                        }
                        else
                        {
                            _pendingFileExtGates?.Add(extSpec.Trim());
                        }
                    }

                    if (!TryGetAlonePhpConstraint(directives, out var phpConstraint))
                    {
                        break;
                    }

                    var result = PhpVersionConstraint.Evaluate(GetTargetPhpVersion(), phpConstraint);
                    if (!result.ConstraintIsValid)
                    {
                        sawInvalid = true;
                        break;
                    }

                    constraints.Add(phpConstraint);
                    if (!result.IsSatisfied)
                    {
                        sawUnsatisfied = true;
                    }
                    break;
                }
            }
        }

        private string GetTargetPhpVersion()
        {
            var version = _compilationOptions?.PhpVersion;
            return string.IsNullOrWhiteSpace(version) ? DefaultPhpVersionTarget : version.Trim();
        }

        private static bool IsDeclareBlockForm(PhpDeclareAst declareAst)
            => declareAst.Body is not null and not PhpNopStatementAst;

        private static bool IsPhpDeclareKey(string? key)
            => string.Equals(key, PhpDeclareDirectiveKey, StringComparison.OrdinalIgnoreCase);

        private const string ExtDeclareDirectiveKey = "ext";

        private static bool IsExtDeclareKey(string? key)
            => string.Equals(key, ExtDeclareDirectiveKey, StringComparison.OrdinalIgnoreCase);

        private static bool TryGetAloneExtConstraint(List<DeclareDirective> directives, out string extConstraint)
        {
            extConstraint = "";
            string? found = null;
            var otherCount = 0;
            foreach (var directive in directives)
            {
                if (IsExtDeclareKey(directive.Key))
                {
                    found = directive.Value;
                }
                else
                {
                    otherCount++;
                }
            }

            if (found == null || otherCount > 0)
            {
                return false;
            }

            extConstraint = found;
            return true;
        }

        private bool TryEvaluateExtGate(
            string spec,
            PhpDeclareAst declareAst,
            out bool satisfied,
            bool insideFunctionBody = false)
        {
            satisfied = false;
            var text = spec.Trim();
            var negative = text.StartsWith('!');
            var name = negative ? text[1..].Trim() : text;
            if (name.Length == 0
                || name.StartsWith("ext-", StringComparison.OrdinalIgnoreCase)
                || name.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefExtDeclareInvalid,
                    declareAst,
                    _currentFileName,
                    spec);
                return false;
            }

            // A positive gate needs a loaded tyhpdef package for the extension, or the block could not
            // be type-checked. Inside a function body a negative gate is the branch that runs when the
            // extension is missing at runtime, which no package can rule out, so it always compiles
            // (behind `!\extension_loaded('name')`). Around declarations it selects which declaration
            // exists, so it follows the loaded packages: an extension package that declares the same
            // name would make the fallback a duplicate.
            var present = ExtensionIsPresent(name);
            satisfied = negative ? !present || insideFunctionBody : present;
            return true;
        }

        private static List<DeclareDirective> ReadDeclareDirectives(PhpDeclareAst declareAst)
        {
            var result = new List<DeclareDirective>();
            if (declareAst.Declarations == null)
            {
                return result;
            }

            foreach (var decl in declareAst.Declarations.GetAllNotNull())
            {
                var key = decl.Identifier ?? "";
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var forPhp = IsPhpDeclareKey(key);
                var value = ExtractDeclareValue(decl.Value, forPhp);
                result.Add(new DeclareDirective(key, value));
            }

            return result;
        }

        private static bool TryGetAlonePhpConstraint(
            List<DeclareDirective> directives,
            out string phpConstraint)
        {
            phpConstraint = "";
            string? found = null;
            var otherCount = 0;
            foreach (var directive in directives)
            {
                if (IsPhpDeclareKey(directive.Key))
                {
                    found = directive.Value;
                }
                else
                {
                    otherCount++;
                }
            }

            if (found == null || otherCount > 0)
            {
                return false;
            }

            phpConstraint = found;
            return true;
        }

        private static string ExtractDeclareValue(IExpression? valueExpr, bool forPhpConstraint)
        {
            if (valueExpr == null)
            {
                return forPhpConstraint ? "" : "1";
            }

            switch (valueExpr)
            {
                case PhpScalarAst scalar:
                    if (!string.IsNullOrEmpty(scalar.ValueString))
                    {
                        return UnquotePhpString(scalar.ValueString);
                    }

                    if (scalar.ValueInt64.HasValue)
                    {
                        return scalar.ValueInt64.Value.ToString();
                    }

                    if (scalar.ValueBoolean.HasValue)
                    {
                        return scalar.ValueBoolean.Value ? "true" : "false";
                    }

                    break;

                case PhpEncapsStringAst encaps:
                    return UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);

                case PhpEncapsListAst list:
                    return string.Concat(
                        list.GetAllNotNull().Select(part => UnquotePhpString(part.ValueString)));
            }

            if (!string.IsNullOrEmpty(valueExpr.ValueString))
            {
                return UnquotePhpString(valueExpr.ValueString);
            }

            return forPhpConstraint ? "" : "1";
        }

        private static string UnquotePhpString(string? literal)
        {
            if (string.IsNullOrEmpty(literal))
            {
                return "";
            }

            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'')
                    || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1];
            }

            return literal;
        }

        private readonly record struct DeclareDirective(string Key, string Value);

        private void PopulateGenericParametersFromGrammarAddon(
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

        private void PopulateGenericParameters(
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

                GenericTypeParameterSymbol? existing = null;
                for (var i = 0; i < targetList.Count; i++)
                {
                    if (string.Equals(targetList[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = targetList[i];
                        break;
                    }
                }

                if (existing != null)
                {
                    _diagnostics.AddDuplicateFromAst(
                        MessageCode.BinderDuplicateGenericParameter,
                        genericArg,
                        _currentFileName,
                        existing,
                        name);
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
                    PhpModifier.Internal => MemberModifier.Internal,
                    _ => MemberModifier.None
                };
            }

            // Tyhp `async` / `internal` may arrive as grammar addons when they are not PhpModifier values
            // on the list (or in addition to PhpModifier.Internal).
            if (modifiers.AstGrammarAddons.ContainsKey("isAsync"))
            {
                result |= MemberModifier.Async;
            }

            if (modifiers.AstGrammarAddons.ContainsKey("isInternal"))
            {
                result |= MemberModifier.Internal;
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

        private static bool HasInternalModifier(IBase2Ast? node)
        {
            if (node is null)
            {
                return false;
            }

            if (node.AstGrammarAddons.ContainsKey("isInternal"))
            {
                return true;
            }

            if (node.AstGrammarAddons.TryGetValue("modifiers", out var addon))
            {
                var fromAddon = addon switch
                {
                    TokenValueListAst list => list.GetAllNotNull().Any(IsInternalToken),
                    TokenValueAst token => IsInternalToken(token),
                    PhpModifierListAst phpList => phpList.AstGrammarAddons.ContainsKey("isInternal")
                        || phpList.Modifiers.Contains(PhpModifier.Internal),
                    _ => false,
                };
                if (fromAddon)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInternalToken(TokenValueAst token) =>
            string.Equals(token.ValueString, "internal", StringComparison.OrdinalIgnoreCase)
            || token.ValueInt64 == Tyhp.TyhpLang.Parser.TyhpParser.T_TYHP_INTERNAL;

        private static void ApplyInternal(BaseSymbol symbol, IBase2Ast? node, MemberModifier modifiers = MemberModifier.None)
        {
            if ((modifiers & MemberModifier.Internal) != 0 || HasInternalModifier(node))
            {
                symbol.IsInternal = true;
                symbol.Visibility |= MemberModifier.Internal;
            }
        }

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

            BindNamedStruct(
                identifier,
                structDecl,
                structDecl.PropertyList,
                structDecl.Extends,
                genericArguments: null,
                genericAddons: structDecl.AstGrammarAddons,
                parentScope);
        }

        /// <summary>
        /// File/namespace and class-member <c>type Name = struct { … }</c> is today's named
        /// struct: an <see cref="ObjectDeclarationSymbol"/> with <c>IsStruct</c>, not a
        /// <see cref="TypeAliasSymbol"/> / <see cref="ObjectTypeAliasSymbol"/>.
        /// <c>new Name() with […]</c> / <c>new C\Name() with […]</c> constructs the array.
        /// Object- and callable-shape aliases stay type aliases and cannot be constructed.
        /// </summary>
        private void BindStructShapeAlias(
            TyhpTypeAliasAst typeAlias,
            TyhpStructShapeAst structShape,
            IBaseScope parentScope)
        {
            var identifier = typeAlias.Name?.ValueString ?? typeAlias.Identifier ?? "";
            if (string.IsNullOrEmpty(identifier))
            {
                _diagnostics.AddWarningFromAst(
                    MessageCode.BinderUnknownError,
                    typeAlias,
                    _currentFileName,
                    "Struct type alias has no identifier — skipping binding.");
                return;
            }

            BindNamedStruct(
                identifier,
                typeAlias,
                structShape.PropertyList,
                structShape.Extends,
                typeAlias.GenericArguments,
                genericAddons: null,
                parentScope);
            structShape.BoundSymbol = typeAlias.BoundSymbol;

            if (parentScope is ObjectDeclarationScope
                && typeAlias.BoundSymbol is ObjectDeclarationSymbol nested)
            {
                var modifiers = ConvertModifiers(typeAlias.Modifiers);
                if (modifiers != MemberModifier.None)
                {
                    nested.Visibility = modifiers;
                }

                ApplyInternal(nested, typeAlias, nested.Visibility);
            }
        }

        private void BindNamedStruct(
            string identifier,
            IBase2Ast declaringNode,
            TyhpStructPropertyListAst? propertyList,
            PhpNameAst? extends,
            TyhpGenericsTypeArgumentListAst? genericArguments,
            IReadOnlyDictionary<string, IBase2Ast>? genericAddons,
            IBaseScope parentScope)
        {
            ShouldRegisterPhpVersionGatedDeclaration(
                declaringNode,
                parentScope,
                identifier,
                SymbolType.ObjectTypeDeclaration,
                illegalAttributeTarget: true,
                out var phpConstraints);

            var symbol = new ObjectDeclarationSymbol(
                identifier,
                declaringNode,
                _currentFileName,
                MemberModifier.Public)
            {
                ObjectKind = PhpTypeDeclType.Class,
                IsStruct = true,
                ExtendsType = extends as ITypeExpression,
            };
            StampPhpVersionConstraints(symbol, phpConstraints);

            if (genericArguments != null)
            {
                PopulateGenericParameters(
                    genericArguments,
                    symbol.GenericParameters,
                    _currentFileName,
                    SymbolType.ClassGenericTypeParameter);
            }
            else if (genericAddons != null)
            {
                PopulateGenericParametersFromGrammarAddon(
                    genericAddons,
                    symbol.GenericParameters,
                    _currentFileName);
            }

            switch (parentScope)
            {
                case FileScope fileScope:
                {
                    if (!fileScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportBinderDuplicate(declaringNode, existing, symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(fileScope, symbol);
                    fileScope.AddChildScope(objScope);
                    BindStructBody(propertyList, objScope);
                    break;
                }

                case NamespaceBlockScope nsBlockScope:
                {
                    if (!nsBlockScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportBinderDuplicate(declaringNode, existing, symbol.Name);
                    }

                    var objScope = new ObjectDeclarationScope(nsBlockScope, symbol);
                    nsBlockScope.AddChildScope(objScope);
                    BindStructBody(propertyList, objScope);
                    break;
                }

                case ObjectDeclarationScope ownerScope:
                {
                    if (!ownerScope.TryAddChildSymbol(symbol, out var existing))
                    {
                        ReportBinderDuplicate(declaringNode, existing, symbol.Name);
                    }
                    else
                    {
                        RegisterObjectMember(ownerScope, symbol, identifier);
                    }

                    var nestedScope = new ObjectDeclarationScope(ownerScope, symbol);
                    ((IObjectDeclarationScopeParent)ownerScope).AddObjectDeclarationChildScope(nestedScope);
                    StampNestedStructFullyQualifiedName(symbol, ownerScope.DeclarationSymbol);
                    BindStructBody(propertyList, nestedScope);
                    break;
                }
            }
        }

        /// <summary>
        /// Nested structs are members of the owning class, so their FQN is
        /// <c>Owner\Name</c> (matching the <c>C\Point</c> spelling), not a sibling
        /// of the owner in the namespace.
        /// </summary>
        private static void StampNestedStructFullyQualifiedName(
            ObjectDeclarationSymbol nested,
            ObjectDeclarationSymbol? owner)
        {
            if (owner is null || string.IsNullOrEmpty(nested.Name))
            {
                return;
            }

            var ownerFqn = owner.FullyQualifiedName;
            if (string.IsNullOrWhiteSpace(ownerFqn))
            {
                ownerFqn = "\\" + owner.Name;
            }
            else if (ownerFqn[0] != '\\')
            {
                ownerFqn = "\\" + ownerFqn;
            }

            nested.FullyQualifiedName = ownerFqn.TrimEnd('\\') + "\\" + nested.Name;
        }

        private void BindStructBody(TyhpStructPropertyListAst? propertyList, ObjectDeclarationScope objScope)
        {
            foreach (var property in propertyList?.GetAllNotNull() ?? [])
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

                if (!objScope.TryAddChildSymbol(propSymbol, out var existing))
                {
                    ReportBinderDuplicate(property, existing, propSymbol.Name);
                }
                else
                {
                    RegisterObjectMember(objScope, propSymbol, propName);
                }
            }
        }

        private void BindSourceFileLevelTypeAlias(
            TyhpTypeAliasAst typeAlias,
            TypeAliasSymbol aliasSymbol,
            IBaseScope parentScope)
        {
            // Tyhpdef file-level aliases stay type-only: they never emit PHP and must not occupy
            // the PHP function namespace, nor be rejected for using a name that cannot be a PHP
            // function (that restriction only matters for source aliases, which do emit a
            // same-named function). Mirrors the tyhpdef exclusion in
            // TyhpBinder.Resolution.IsUserSourceTypeAlias.
            var isTyhpdefAlias = IsTyhpdefContext(typeAlias);

            if (!isTyhpdefAlias && PhpReservedFunctionNames.CannotBePhpFunction(aliasSymbol.Name))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderTypeAliasReservedFunctionName,
                    typeAlias,
                    _currentFileName,
                    aliasSymbol.Name);
            }

            bool added;
            IBaseSymbol? existing;
            switch (parentScope)
            {
                case FileScope fileScope:
                    added = fileScope.TryAddChildSymbol(aliasSymbol, out existing);
                    break;
                case NamespaceBlockScope nsBlockScope:
                    added = nsBlockScope.TryAddChildSymbol(aliasSymbol, out var nsExisting);
                    existing = nsExisting;
                    break;
                default:
                    added = false;
                    existing = null;
                    break;
            }

            if (!added)
            {
                ReportBinderDuplicate(typeAlias, existing, aliasSymbol.Name);
                return;
            }

            if (isTyhpdefAlias)
            {
                return;
            }

            if (!parentScope.TryOccupyFunctionNamespace(aliasSymbol))
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderTypeAliasConflictsWithFunction,
                    typeAlias,
                    _currentFileName,
                    aliasSymbol.Name);
            }
        }

        /// <summary>
        /// Object shapes are public-only and must have at least one member. Empty
        /// <c>type X = object {};</c> is TYHP4349; <c>private</c>/<c>protected</c>/<c>internal</c>
        /// members are TYHP4350. Signatures (including <c>__construct</c>) stay on the shape AST
        /// so later phases can read them; no <c>ObjectDeclarationSymbol</c> is invented.
        /// </summary>
        private void ValidateObjectShapeAlias(TyhpTypeAliasAst typeAlias)
        {
            var shape = typeAlias.ObjectShape;
            if (shape is null)
            {
                return;
            }

            var members = shape.Members?.GetAllNotNull().ToList() ?? [];
            if (members.Count == 0)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.CheckerObjectShapeEmpty,
                    shape,
                    _currentFileName);
                return;
            }

            foreach (var member in members)
            {
                if (!TryGetObjectShapeMemberVisibility(member, out var modifiers, out var memberName))
                {
                    continue;
                }

                var visibility = ConvertModifiers(modifiers);
                if ((visibility & (MemberModifier.Private | MemberModifier.Protected | MemberModifier.Internal)) == 0
                    && !HasInternalModifier(member)
                    && (modifiers is null || !HasInternalModifier(modifiers)))
                {
                    continue;
                }

                _diagnostics.AddErrorFromAst(
                    MessageCode.CheckerObjectShapeNonPublicMember,
                    member,
                    _currentFileName,
                    memberName);
            }
        }

        private static bool TryGetObjectShapeMemberVisibility(
            IClassMember member,
            out PhpModifierListAst? modifiers,
            out string memberName)
        {
            switch (member)
            {
                case PhpMethodDeclAst method:
                    modifiers = method.Modifiers;
                    memberName = method.Identifier ?? "";
                    return true;
                case PhpPropertyDeclAst property:
                    modifiers = property.Modifiers;
                    memberName = property.Properties?.GetAllNotNull().FirstOrDefault()?.Identifier
                        ?? property.Identifier
                        ?? "";
                    return true;
                case PhpConstDeclListAst constList:
                    modifiers = constList.GetAllNotNull().FirstOrDefault()?.Modifiers;
                    memberName = constList.GetAllNotNull().FirstOrDefault()?.Identifier ?? "";
                    return true;
                case TyhpdefImportConstDeclListAst tyhpdefConsts:
                    modifiers = tyhpdefConsts.AstGrammarAddons.TryGetValue("modifiers", out var addon)
                        ? addon as PhpModifierListAst
                        : null;
                    memberName = tyhpdefConsts.GetAllNotNull().FirstOrDefault()?.Identifier ?? "";
                    return true;
                default:
                    modifiers = null;
                    memberName = member.Identifier ?? "";
                    return modifiers is not null || HasExplicitNonPublicAddon(member);
            }
        }

        private static bool HasExplicitNonPublicAddon(IBase2Ast node) =>
            node.AstGrammarAddons.TryGetValue("modifiers", out var addon)
            && addon is PhpModifierListAst list
            && list.Modifiers.Any(m =>
                m is PhpModifier.Private or PhpModifier.Protected or PhpModifier.Internal);

        /// <summary>
        /// True when a node was declared in a <c>.tyhpdef</c> file or an embedded
        /// <c>&lt;tyhpdef:…&gt;</c> block, matching the tyhpdef detection used for circular-alias
        /// exemption in <c>TyhpBinder.Resolution.IsUserSourceTypeAlias</c>.
        /// </summary>
        private bool IsTyhpdefContext(IBase2Ast node) =>
            _currentFileName.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase)
            || _currentFileName.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.LanguageMode, "tyhpdef", StringComparison.Ordinal);

        private void ReportFunctionDeclarationConflict(
            PhpFunctionDeclAst funcDecl,
            IBaseSymbol? existing,
            string name)
        {
            if (existing is TypeAliasSymbol)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.BinderTypeAliasConflictsWithFunction,
                    funcDecl,
                    _currentFileName,
                    name);
                return;
            }

            ReportBinderDuplicate(funcDecl, existing, name);
        }

        // Defined in TyhpBinder.ObjectBody.cs
        partial void BindObjectBody(PhpObjectTypeDeclAst objDecl, ObjectDeclarationScope objScope, ObjectDeclarationSymbol symbol);

        // Defined in another partial
        partial void BindFunctionBody(PhpFunctionDeclAst funcDecl, FunctionDeclarationScope funcScope, FunctionDeclarationSymbol symbol);

        // Defined in another partial
        partial void BindStatementBlock(IStatement stmt, IBaseScope parentScope);
    }
}
