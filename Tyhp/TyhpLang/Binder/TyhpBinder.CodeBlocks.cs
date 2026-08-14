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
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        private const int MaxBindNestingDepth = 500;

        partial void BindFunctionBody(
            PhpFunctionDeclAst funcDecl,
            FunctionDeclarationScope funcScope,
            FunctionDeclarationSymbol symbol)
        {
            if (funcDecl.Parameters != null)
            {
                foreach (var param in funcDecl.Parameters.GetAllNotNull())
                {
                    var paramName = param.ValueString ?? "";
                    var paramModifiers = ConvertModifiers(param.Modifiers);

                    var varSymbol = new VariableSymbol(
                        paramName,
                        declaringNode: param,
                        sourceFile: _currentFileName,
                        visibility: paramModifiers);

                    varSymbol.DeclaredType = param.Type;
                    varSymbol.IsParameter = true;
                    varSymbol.DefaultValue = param.DefaultValue;

                    if (!funcScope.AddChildSymbol(varSymbol))
                    {
                        _diagnostics.AddErrorFromAst(MessageCode.BinderDuplicateSymbolDeclaration, param, _currentFileName, paramName);
                    }

                    symbol.Parameters.Add(new ParameterInfo(
                        paramName,
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers));
                }
            }

            symbol.ReturnType = funcDecl.ReturnType;

            if (funcDecl.Body != null)
            {
                BindStatementBlock(funcDecl.Body, funcScope);
            }
        }

        partial void BindStatementBlock(IStatement stmt, IBaseScope parentScope)
        {
            _bindDepth++;
            try
            {
                if (_bindDepth > MaxBindNestingDepth)
                {
                    _diagnostics.AddError(MessageCode.BinderUnknownError, _currentFileName, 0, 0,
                        "Maximum binding nesting depth exceeded");
                    return;
                }

                switch (stmt)
                {
                    case PhpStatementBlockAst stmtBlock:
                        BindStatementBlockChildren(stmtBlock, parentScope);
                        break;

                    case PhpIfAst ifAst:
                        BindCodeBlockNode(ifAst, "if", parentScope);
                        break;

                    case PhpLoopAst loopAst:
                        BindCodeBlockNode(loopAst, "loop", parentScope);
                        break;

                    case PhpTryCatchAst tryAst:
                        BindCodeBlockNode(tryAst, "try", parentScope);
                        break;

                    case PhpConditionalAst condAst:
                        BindCodeBlockNode(condAst, "match", parentScope);
                        break;

                    case PhpLabelStatementAst labelAst:
                        BindLabel(labelAst, parentScope);
                        break;

                    case PhpInlineFunctionAst inlineFunc:
                        BindAnonymousFunction(inlineFunc, parentScope);
                        break;

                    // Named function declared inside a statement block (FOUND_BUGS #36). Without
                    // this arm the default path walks the function's AST children as if they
                    // belonged to the enclosing block and never calls BindFunctionDecl.
                    case PhpFunctionDeclAst funcDecl:
                        BindFunctionDecl(funcDecl, parentScope);
                        break;

                    case PhpDeclareAst declareAst when declareAst.Body != null:
                        BindDeclareBlock(declareAst, parentScope);
                        break;

                    case TyhpTypedVarExprAst typedVar:
                        BindTypedVarDecl(typedVar, parentScope);
                        break;

                    case TyhpUsingBlockAst usingBlock:
                        BindUsingBlock(usingBlock, parentScope);
                        break;

                    case PhpGlobalStatementAst globalStmt:
                        BindGlobalStatement(globalStmt, parentScope);
                        break;

                    case PhpStaticStatementAst staticStmt:
                        BindStaticStatement(staticStmt, parentScope);
                        break;

                    // Story 14.5: keyword call forms (`exit(...)` / `die(...)` / `clone(...)`)
                    // carry a PhpArgumentListAst operand — attach the ExtCore tyhpdef function
                    // symbol so checker arg validation matches normal free-function calls.
                    // Bare `exit;` / unary `clone $x` are not call forms (no ArgumentList).
                    case PhpUnaryOpAst unary:
                        TryBindKeywordConstructCall(unary, parentScope);
                        BindCodeBlockChildren(unary, parentScope);
                        break;

                    case UnexpectedNodeAst:
                    case ErrorAst:
                        break;

                    default:
                        BindCodeBlockChildren(stmt, parentScope);
                        break;
                }
            }
            finally
            {
                _bindDepth--;
            }
        }

        private void BindStatementBlockChildren(PhpStatementBlockAst stmtBlock, IBaseScope parentScope)
        {
            if (parentScope is not ICodeBlockScopeParent cbParent)
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderInvalidSymbolTypeForParent, stmtBlock, _currentFileName, "statement block");
                return;
            }

            var blockSymbol = new CodeBlockSymbol(
                $"block@{stmtBlock.Line}:{stmtBlock.Column}",
                ScopeType.CodeBlock,
                _currentFileName);

            var blockScope = new CodeBlockScope(cbParent, blockSymbol);
            cbParent.AddCodeBlockChildScope(blockScope);

            foreach (var child in stmtBlock.GetAllNotNull())
            {
                BindStatementBlock(child, blockScope);
            }
        }

        private void BindCodeBlockNode(IBase2Ast node, string blockKind, IBaseScope parentScope)
        {
            if (parentScope is not ICodeBlockScopeParent cbParent)
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderInvalidSymbolTypeForParent, node, _currentFileName, blockKind);
                return;
            }

            var blockSymbol = new CodeBlockSymbol(
                $"{blockKind}@{node.Line}:{node.Column}",
                ScopeType.CodeBlock,
                _currentFileName);

            var blockScope = new CodeBlockScope(cbParent, blockSymbol);
            cbParent.AddCodeBlockChildScope(blockScope);

            BindCodeBlockChildren(node, blockScope);
        }

        private void BindLabel(PhpLabelStatementAst labelAst, IBaseScope parentScope)
        {
            if (parentScope is not ILabelScopeParent labelParent)
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderInvalidSymbolTypeForParent, labelAst, _currentFileName, "label");
                return;
            }

            var labelSymbol = new LabelSymbol(
                labelAst.Identifier ?? "",
                _currentFileName);

            var labelScope = new LabelScope(labelParent, labelSymbol);
            labelParent.AddLabelChildScope(labelScope);
        }

        private void BindAnonymousFunction(PhpInlineFunctionAst funcAst, IBaseScope parentScope)
        {
            var name = $"closure@{funcAst.Line}:{funcAst.Column}";
            var anonSymbol = new AnonymousFunctionSymbol(name, _currentFileName);

            anonSymbol.ReturnType = funcAst.ReturnType;

            if (funcAst.LexicalVars != null)
            {
                foreach (var lexVar in funcAst.LexicalVars.GetAllNotNull())
                {
                    var capturedVar = new VariableSymbol(
                        lexVar.Identifier ?? lexVar.ValueString ?? "",
                        declaringNode: lexVar,
                        sourceFile: _currentFileName);
                    capturedVar.IsRef = lexVar.IsRef;

                    anonSymbol.CapturedVariables.Add(capturedVar);
                }
            }

            if (parentScope is not ICodeBlockScopeParent cbParent)
            {
                _diagnostics.AddErrorFromAst(MessageCode.BinderInvalidSymbolTypeForParent, funcAst,
                    _currentFileName, "anonymous function");
                return;
            }
            var anonScope = new AnonymousFunctionScope(cbParent, anonSymbol);
            cbParent.AddCodeBlockChildScope(anonScope);

            foreach (var capturedVar in anonSymbol.CapturedVariables)
            {
                anonScope.AddChildSymbol(capturedVar);
            }

            if (funcAst.Parameters != null)
            {
                foreach (var param in funcAst.Parameters.GetAllNotNull())
                {
                    var paramName = param.ValueString ?? "";
                    var paramModifiers = ConvertModifiers(param.Modifiers);

                    anonSymbol.Parameters.Add(new ParameterInfo(
                        paramName,
                        param.Type,
                        param.DefaultValue,
                        param.IsVariadic,
                        param.IsRef,
                        paramModifiers));

                    var varSymbol = new VariableSymbol(
                        paramName,
                        declaringNode: param,
                        sourceFile: _currentFileName);
                    varSymbol.DeclaredType = param.Type;
                    varSymbol.IsParameter = true;
                    varSymbol.DefaultValue = param.DefaultValue;

                    anonScope.AddChildSymbol(varSymbol);
                }
            }

            if (funcAst.Body != null)
            {
                BindStatementBlock(funcAst.Body, anonScope);
            }
        }

        private void BindCodeBlockChildren(IBase2Ast node, IBaseScope parentScope)
        {
            _bindDepth++;
            try
            {
                if (_bindDepth > MaxBindNestingDepth)
                {
                    _diagnostics.AddError(MessageCode.BinderUnknownError, _currentFileName, 0, 0,
                        "Maximum binding nesting depth exceeded");
                    return;
                }

                if (node.AstChildren == null) return;

                foreach (var child in node.AstChildren)
                {
                    // Declaration forms also implement IStatement (via IAttributedStatement), so
                    // check them before the generic IStatement arm — otherwise nested function/
                    // class decls are walked as statement trees and never registered as symbols.
                    if (child is PhpObjectTypeDeclAst objDecl)
                    {
                        BindObjectTypeDecl(objDecl, parentScope);
                    }
                    else if (child is PhpFunctionDeclAst funcDecl)
                    {
                        BindFunctionDecl(funcDecl, parentScope);
                    }
                    else if (child is PhpInlineFunctionAst inlineFunc)
                    {
                        BindAnonymousFunction(inlineFunc, parentScope);
                    }
                    else if (child is IStatement childStmt)
                    {
                        BindStatementBlock(childStmt, parentScope);
                    }
                    else if (child != null)
                    {
                        BindCodeBlockChildren(child, parentScope);
                    }
                }
            }
            finally
            {
                _bindDepth--;
            }
        }

        /// <summary>
        /// Attaches ExtCore tyhpdef <see cref="FunctionDeclarationSymbol"/>s to keyword call
        /// forms (<c>exit(...)</c> / <c>die(...)</c> / <c>clone(...)</c>). Call forms are
        /// recognized by a <see cref="PhpArgumentListAst"/> operand; bare <c>exit;</c> and
        /// unary <c>clone $x</c> / parenthesized <c>clone($x)</c> are left unbound.
        /// </summary>
        private void TryBindKeywordConstructCall(PhpUnaryOpAst unary, IBaseScope fromScope)
        {
            if (unary.Operand is not PhpArgumentListAst)
            {
                return;
            }

            var name = unary.Operator?.ValueString;
            if (!IsKeywordConstructName(name))
            {
                return;
            }

            var resolver = new NameResolver(_globalScope, _diagnostics);
            var symbol = (resolver.ResolveRelativeName([name!], fromScope)
                ?? resolver.ResolveRelativeName([name!], _globalScope)) as FunctionDeclarationSymbol;

            if (symbol is not null)
            {
                unary.BoundSymbol = symbol;
            }
        }

        private static bool IsKeywordConstructName(string? name) =>
            string.Equals(name, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "die", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "clone", StringComparison.OrdinalIgnoreCase);

        private void BindTypedVarDecl(TyhpTypedVarExprAst typedVar, IBaseScope parentScope)
        {
            var varName = typedVar.Variable?.VariableToken?.ValueString ?? "";
            if (string.IsNullOrEmpty(varName)) return;

            var varSymbol = new VariableSymbol(
                varName,
                declaringNode: typedVar,
                sourceFile: _currentFileName);
            varSymbol.DeclaredType = typedVar.TypeExpression;
            varSymbol.IsRef = typedVar.IsRef;

            TryAddVariableToCurrentScope(varSymbol, parentScope);
        }

        /// <summary>
        /// Binds a using block statement. Creates a code block scope for the using block body
        /// and registers resource variables within that scope.
        /// </summary>
        private void BindUsingBlock(TyhpUsingBlockAst usingBlock, IBaseScope parentScope)
        {
            if (parentScope is not ICodeBlockScopeParent cbParent)
            {
                _diagnostics.AddError(MessageCode.BinderInvalidSymbolTypeForParent,
                    _currentFileName, usingBlock.Line, usingBlock.Column, "using block");
                return;
            }

            var blockSymbol = new CodeBlockSymbol(
                $"using@{usingBlock.Line}:{usingBlock.Column}",
                ScopeType.CodeBlock,
                _currentFileName);

            var blockScope = new CodeBlockScope(cbParent, blockSymbol);
            cbParent.AddCodeBlockChildScope(blockScope);

            foreach (var resource in usingBlock.Resources)
            {
                BindUsingResource(resource, blockScope, usingBlock.IsAsync);
            }

            if (usingBlock.Body != null)
            {
                BindStatementBlock(usingBlock.Body, blockScope);
            }
        }

        /// <summary>
        /// Binds an individual resource declaration within a using block.
        /// Creates a VariableSymbol with IsDisposable=true for assigned resources.
        /// For unassigned resources, registers a synthetic variable ($__using_N).
        /// </summary>
        private void BindUsingResource(TyhpUsingResourceAst resource, CodeBlockScope blockScope, bool isAsync)
        {
            if (resource.HasVariable && resource.Variable is PhpVariableAst varAst)
            {
                var varName = varAst.VariableToken?.ValueString ?? "";
                if (!string.IsNullOrEmpty(varName))
                {
                    var varSymbol = new VariableSymbol(
                        varName,
                        declaringNode: resource,
                        sourceFile: _currentFileName);
                    varSymbol.IsDisposable = true;

                    if (resource.HasTypeAnnotation)
                    {
                        varSymbol.DeclaredType = resource.TypeExpr as ITypeExpression;
                    }

                    if (!blockScope.AddChildSymbol(varSymbol))
                    {
                        _diagnostics.AddErrorFromAst(MessageCode.BinderDuplicateSymbolDeclaration,
                            resource, _currentFileName, varName);
                    }
                }
            }
            else if (resource.HasVariable)
            {
                _diagnostics.AddError(
                    MessageCode.BinderUnknownError,
                    _currentFileName,
                    resource.Line, resource.Column,
                    $"Expected PhpVariableAst for using resource variable, got {resource.Variable?.GetType().Name ?? "null"}");
            }
            else
            {
                var syntheticName = $"$__using_{blockScope.SyntheticCounter++}";
                var syntheticSymbol = new VariableSymbol(
                    syntheticName,
                    declaringNode: resource,
                    sourceFile: _currentFileName)
                {
                    IsDisposable = true
                };
                blockScope.AddChildSymbol(syntheticSymbol);
            }
            // TODO: Validate that resource expression type implements IsDisposable (sync) or AsyncIsDisposable (async).
            //       This requires type resolution which is not available at the binder stage — defer to the resolution/checker pass.
            // TODO: Validate that := operator is not used inside using() declarations.
            //       The := operator is not yet defined in the grammar; implement this check when the operator is added.
        }

        private void BindGlobalStatement(PhpGlobalStatementAst globalStmt, IBaseScope parentScope)
        {
            if (globalStmt.Variables == null) return;

            foreach (var varExpr in globalStmt.Variables.GetAllNotNull())
            {
                if (varExpr is not PhpVariableAst varAst) continue;
                var varName = varAst.VariableToken?.ValueString ?? "";
                if (string.IsNullOrEmpty(varName)) continue;

                var varSymbol = new VariableSymbol(
                    varName,
                    declaringNode: varAst,
                    sourceFile: _currentFileName);

                TryAddVariableToCurrentScope(varSymbol, parentScope);
            }
        }

        private void BindStaticStatement(PhpStaticStatementAst staticStmt, IBaseScope parentScope)
        {
            if (staticStmt.Variables == null) return;

            foreach (var varAst in staticStmt.Variables.GetAllNotNull())
            {
                var varName = varAst.VariableToken?.ValueString ?? "";
                if (string.IsNullOrEmpty(varName)) continue;

                var varSymbol = new VariableSymbol(
                    varName,
                    declaringNode: varAst,
                    sourceFile: _currentFileName);
                varSymbol.DefaultValue = varAst.DefaultValue;

                TryAddVariableToCurrentScope(varSymbol, parentScope);
            }
        }

        /// <summary>
        /// Adds a variable symbol to the appropriate parent scope, dispatching based on the scope's concrete type.
        /// </summary>
        private void TryAddVariableToCurrentScope(VariableSymbol varSymbol, IBaseScope currentScope)
        {
            switch (currentScope)
            {
                case CodeBlockScope scope:
                    scope.AddChildSymbol(varSymbol);
                    break;
                case DeclareBlockScope scope:
                    scope.AddChildSymbol(varSymbol);
                    break;
                case FunctionDeclarationScope scope:
                    scope.AddChildSymbol(varSymbol);
                    break;
                case AnonymousFunctionScope scope:
                    scope.AddChildSymbol(varSymbol);
                    break;
                case InstanceMethodDeclarationScope scope:
                    scope.AddChildSymbol(varSymbol);
                    break;
                case StaticMethodDeclarationScope scope:
                    scope.AddChildSymbol(varSymbol);
                    break;
                default:
                    _diagnostics.AddWarning(MessageCode.BinderUnknownError, _currentFileName, 0, 0, $"Unexpected scope type for variable declaration: {currentScope.GetType().Name}");
                    break;
            }
        }
    }
}
