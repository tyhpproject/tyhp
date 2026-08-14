using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private EmitItem EmitStatement(IStatement statement, EmitItem parent, EmitType emitType = EmitType.FunctionStatement)
        {
            EmitItem? emitted = statement switch
            {
                PhpIfAst ifAst => this.EmitIfStatement(ifAst, parent, emitType),
                PhpLoopAst loop => this.EmitLoopStatement(loop, parent, emitType),
                PhpTryCatchAst tryCatch => this.EmitTryCatchStatement(tryCatch, parent, emitType),
                PhpJumpStatementAst jump => this.EmitJumpStatement(jump, parent, emitType),
                PhpReturnStatementAst ret => this.EmitReturnStatement(ret, parent, emitType),
                PhpGotoStatementAst gotoStmt => this.EmitGotoStatement(gotoStmt, parent, emitType),
                PhpLabelStatementAst label => this.EmitLabelStatement(label, parent, emitType),
                PhpEchoStatementAst echo => this.EmitEchoStatement(echo, parent, emitType),
                PhpUnsetStatementAst unset => this.EmitUnsetStatement(unset, parent, emitType),
                PhpGlobalStatementAst global => this.EmitGlobalStatement(global, parent, emitType),
                PhpStaticStatementAst staticStmt => this.EmitStaticStatement(staticStmt, parent, emitType),
                PhpDeclareAst declare => this.EmitDeclareStatement(declare, parent, emitType),
                PhpStatementBlockAst block => this.EmitStatementBlock(block, parent, emitType),
                PhpNopStatementAst => EmitItem.Line(statement, emitType, ";", parent),
                PhpInlineOutputAst inlineOutput => this.EmitInlineOutput(inlineOutput, parent, emitType),
                PhpInlineOutputListAst inlineList => this.EmitInlineOutputList(inlineList, parent, emitType),
                PhpConditionalAst conditional when statement is IStatement => this.EmitConditionalStatement(conditional, parent, emitType),
                TyhpTypedVarExprAst typedVar => this.EmitTypedVarStatement(typedVar, parent, emitType),
                TyhpUsingBlockAst usingBlock => this.EmitUsingBlockStatement(usingBlock, parent, emitType),
                PhpUnaryOpAst unary when unary is IStatement => this.EmitUnaryStatement(unary, parent, emitType),
                _ => null,
            };

            if (emitted != null)
            {
                return emitted;
            }

            // Nested declarations (e.g. inside `if (!function_exists(...)) { function … }`) are
            // statements in the block walk but must emit via the declaration path.
            if (statement is PhpFunctionDeclAst or PhpObjectTypeDeclAst or TyhpExtensionDeclAst
                or PhpConstDeclAst or PhpConstDeclListAst)
            {
                return this.EmitNode(statement, parent);
            }

            if (statement is PhpBinaryOpAst weakAssign
                && this.TryEmitWeakReferencePropertyClosure(weakAssign, parent, emitType, out var weakEmitted))
            {
                return weakEmitted;
            }

            if (statement is PhpDereferenceableAst deref && IsUsingCall(deref))
            {
                return this.EmitUsingCallStatement(deref, parent, emitType);
            }

            if (statement is IExpression expression)
            {
                return EmitItem.Line(statement, emitType, this.BuildExpression(expression) + ";", parent);
            }

            return EmitItem.Empty(statement, emitType, parent);
        }

        private EmitItem EmitIfStatement(PhpIfAst ifAst, EmitItem parent, EmitType emitType)
        {
            // Handle `if (using(...))` condition specially
            if (ifAst.Condition is PhpDereferenceableAst usingDeref && IsUsingCall(usingDeref))
            {
                return this.EmitUsingCallIfStatement(ifAst, parent, emitType);
            }

            // Valid declaration gates always emit `__NAMESPACE__.'\Name'` so a source FQN written
            // against the Tyhp namespace still matches after output namespacePrefix is applied.
            if (this.TryEmitDeclarationExistenceGate(ifAst, parent, emitType, out var gatedEmit))
            {
                return gatedEmit;
            }

            var segments = new List<(string Open, Action<EmitItem> Body)>();

            PhpIfAst? current = ifAst;
            IStatement? elseTail = null;
            while (current != null)
            {
                var condition = this.BuildExpression(current.Condition);
                var keyword = segments.Count == 0 ? "if" : "elseif";
                var then = current.ThenStatement;
                segments.Add(($"{keyword} ({condition}) {{", block => this.EmitStatementBody(then, block)));

                if (current.ElseStatement is PhpIfAst elseif)
                {
                    current = elseif;
                }
                else
                {
                    elseTail = current.ElseStatement;
                    current = null;
                }
            }

            if (elseTail != null)
            {
                var tail = elseTail;
                segments.Add(("else {", block => this.EmitStatementBody(tail, block)));
            }

            var first = this.EmitBraceSegments(ifAst, parent, emitType, segments);
            return this.ApplyDocComment(ifAst, first);
        }

        private bool TryEmitDeclarationExistenceGate(
            PhpIfAst ifAst,
            EmitItem parent,
            EmitType emitType,
            out EmitItem emitted)
        {
            emitted = null!;
            var currentNamespace = this._context.CurrentOutputFile?.FileNameSpace switch
            {
                PhpNamespaceDeclAst ns => ns.Identifier,
                PhpBlockNamespaceDeclAst block => block.Identifier,
                _ => null,
            };

            if (!DeclarationExistenceGateHelper.TryBuildEmittedGateCondition(
                    ifAst,
                    currentNamespace,
                    out var condition))
            {
                return false;
            }

            var then = ifAst.ThenStatement;
            var segments = new List<(string Open, Action<EmitItem> Body)>
            {
                ($"if ({condition}) {{", block => this.EmitStatementBody(then, block)),
            };
            var first = this.EmitBraceSegments(ifAst, parent, emitType, segments);
            emitted = this.ApplyDocComment(ifAst, first);
            return true;
        }

        private EmitItem EmitUsingCallIfStatement(PhpIfAst ifAst, EmitItem parent, EmitType emitType)
        {
            this._context.EnterDisposableBlockScope();
            try
            {
                var scopeVar = this._context.EnsureDisposableScopeForCurrentBlock();
                var condition = this.BuildUsingCallCondition(
                    (PhpDereferenceableAst)ifAst.Condition,
                    scopeVar);

                var segments = new List<(string Open, Action<EmitItem> Body)>();
                var keyword = "if";
                segments.Add(($"{keyword} ({condition}) {{", block => this.EmitStatementBody(ifAst.ThenStatement, block)));

                if (ifAst.ElseStatement is PhpIfAst elseif)
                {
                    PhpIfAst? current = elseif;
                    IStatement? elseTail = null;
                    while (current != null)
                    {
                        var cond = this.BuildExpression(current.Condition);
                        var kw = "elseif";
                        var then = current.ThenStatement;
                        segments.Add(($"{kw} ({cond}) {{", block => this.EmitStatementBody(then, block)));

                        if (current.ElseStatement is PhpIfAst elseif2)
                        {
                            current = elseif2;
                        }
                        else
                        {
                            elseTail = current.ElseStatement;
                            current = null;
                        }
                    }

                    if (elseTail != null)
                    {
                        var tail = elseTail;
                        segments.Add(("else {", block => this.EmitStatementBody(tail, block)));
                    }
                }

                EmitItem.Line(ifAst, emitType, $"{scopeVar} = \\Tyhp\\DisposableScope::create();", parent);
                var first = this.EmitBraceSegments(ifAst, parent, emitType, segments);
                return this.ApplyDocComment(ifAst, first);
            }
            finally
            {
                this._context.ExitDisposableBlockScope();
            }
        }

        // Emits a sequence of brace-delimited segments that chain together with their closing
        // brace, e.g. `if (..) { .. } elseif (..) { .. } else { .. }` or
        // `try { .. } catch (..) { .. } finally { .. }`. Each segment becomes its own sibling
        // EmitItem under <paramref name="parent"/>; every segment after the first prefixes its
        // opening keyword with `} ` (closing the previous segment), and only the final segment
        // emits the trailing `}`.
        private EmitItem EmitBraceSegments(
            IBase2Ast provider,
            EmitItem parent,
            EmitType emitType,
            IReadOnlyList<(string Open, Action<EmitItem> Body)> segments)
        {
            EmitItem? first = null;
            for (var i = 0; i < segments.Count; i++)
            {
                var open = i == 0 ? segments[i].Open : "} " + segments[i].Open;
                var close = i == segments.Count - 1 ? "}" : "";
                var block = EmitItem.Block(provider, emitType, open, close, parent);
                segments[i].Body(block);
                first ??= block;
            }

            return first ?? EmitItem.Empty(provider, emitType, parent);
        }

        private void EmitStatementBody(IStatement? body, EmitItem parent)
        {
            if (body is PhpStatementBlockAst block)
            {
                this.EmitBlockContents(block, parent, EmitType.SubBlockStatement);
            }
            else if (body != null)
            {
                this.EmitStatement(body, parent, EmitType.SubBlockStatement);
            }
        }

        private EmitItem EmitLoopStatement(PhpLoopAst loop, EmitItem parent, EmitType emitType)
        {
            return loop.LoopType switch
            {
                PhpLoopType.While => this.EmitWhileLoop(loop, parent, emitType),
                PhpLoopType.DoWhile => this.EmitDoWhileLoop(loop, parent, emitType),
                PhpLoopType.For => this.EmitForLoop(loop, parent, emitType),
                PhpLoopType.Foreach => this.EmitForeachLoop(loop, parent, emitType),
                _ => EmitItem.Empty(loop, emitType, parent),
            };
        }

        private EmitItem EmitWhileLoop(PhpLoopAst loop, EmitItem parent, EmitType emitType)
        {
            var condition = this.BuildExpression(loop.Condition);
            var block = EmitItem.Block(loop, emitType, $"while ({condition}) {{", "}", parent);
            this.EmitLoopBody(loop.Body, block);
            return this.ApplyDocComment(loop, block);
        }

        private EmitItem EmitDoWhileLoop(PhpLoopAst loop, EmitItem parent, EmitType emitType)
        {
            var condition = this.BuildExpression(loop.Condition);
            var block = EmitItem.Block(loop, emitType, "do {", $"}} while ({condition});", parent);
            this.EmitLoopBody(loop.Body, block);
            return this.ApplyDocComment(loop, block);
        }

        private EmitItem EmitForLoop(PhpLoopAst loop, EmitItem parent, EmitType emitType)
        {
            var init = this.FormatExpressionList(loop.InitExpressions);
            var test = this.FormatExpressionList(loop.TestExpressions);
            var update = this.FormatExpressionList(loop.UpdateExpressions);
            var block = EmitItem.Block(loop, emitType, $"for ({init}; {test}; {update}) {{", "}", parent);
            this.EmitLoopBody(loop.Body, block);
            return this.ApplyDocComment(loop, block);
        }

        private EmitItem EmitForeachLoop(PhpLoopAst loop, EmitItem parent, EmitType emitType)
        {
            if (IsAwaitForeach(loop))
            {
                return this.EmitAsyncForeachLoop(loop, parent, emitType);
            }

            var expr = this.BuildExpression(loop.Condition);
            string foreachClause;
            if (loop.KeyVariable != null)
            {
                foreachClause = this.BuildForeachVariable(loop.KeyVariable) + " => " + this.BuildForeachVariable(loop.ValueVariable);
            }
            else
            {
                foreachClause = this.BuildForeachVariable(loop.ValueVariable);
            }

            var block = EmitItem.Block(loop, emitType, $"foreach ({expr} as {foreachClause}) {{", "}", parent);
            this.EmitLoopBody(loop.Body, block);
            return this.ApplyDocComment(loop, block);
        }

        private void EmitLoopBody(IBase2Ast? body, EmitItem parent)
        {
            if (body is IStatement stmt)
            {
                this.EmitStatementBody(stmt, parent);
            }
        }

        private EmitItem EmitTryCatchStatement(PhpTryCatchAst tryCatch, EmitItem parent, EmitType emitType)
        {
            var segments = new List<(string Open, Action<EmitItem> Body)>
            {
                ("try {", block => this.EmitStatementBlockInto(tryCatch.TryBlock, block)),
            };

            foreach (var catchClause in tryCatch.CatchClauses?.GetAllNotNull() ?? [])
            {
                var clause = catchClause;
                var types = this.FormatClassNameList(clause.ExceptionTypes, " | ");
                var variable = clause.Variable != null
                    ? " " + this.BuildExpression(clause.Variable)
                    : "";
                segments.Add(($"catch ({types}{variable}) {{", block => this.EmitStatementBlockInto(clause.Body, block)));
            }

            if (tryCatch.FinallyBlock != null)
            {
                segments.Add(("finally {", block => this.EmitStatementBlockInto(tryCatch.FinallyBlock, block)));
            }

            var first = this.EmitBraceSegments(tryCatch, parent, emitType, segments);
            return this.ApplyDocComment(tryCatch, first);
        }

        // Emits the statements of a block directly into <paramref name="parent"/> without wrapping
        // them in an extra `{ }` layer (used for try/catch/finally bodies whose braces already come
        // from the enclosing segment).
        private void EmitStatementBlockInto(PhpStatementBlockAst? block, EmitItem parent)
        {
            if (block == null)
            {
                return;
            }

            this.EmitBlockContents(block, parent, EmitType.SubBlockStatement);
        }

        private EmitItem EmitJumpStatement(PhpJumpStatementAst jump, EmitItem parent, EmitType emitType)
        {
            if (jump.JumpType == PhpJumpType.Return)
            {
                if (jump.Expression is null)
                {
                    return this.ApplyDocComment(jump, EmitItem.Line(jump, emitType, "return;", parent));
                }

                return this.EmitCheckedReturn(jump, jump.Expression, parent, emitType);
            }

            var line = jump.JumpType switch
            {
                PhpJumpType.Break => jump.Expression != null
                    ? "break " + this.BuildExpression(jump.Expression) + ";"
                    : "break;",
                PhpJumpType.Continue => jump.Expression != null
                    ? "continue " + this.BuildExpression(jump.Expression) + ";"
                    : "continue;",
                PhpJumpType.Goto => "goto " + jump.Identifier + ";",
                _ => ";",
            };

            return this.ApplyDocComment(jump, EmitItem.Line(jump, emitType, line, parent));
        }

        private EmitItem EmitReturnStatement(PhpReturnStatementAst ret, EmitItem parent, EmitType emitType)
        {
            if (ret.Expression is null)
            {
                return this.ApplyDocComment(ret, EmitItem.Line(ret, emitType, "return;", parent));
            }

            return this.EmitCheckedReturn(ret, ret.Expression, parent, emitType);
        }

        /// <summary>
        /// Emits <c>return expr;</c>, or when a generic return check is active:
        /// <c>$__tyhp_ret_N = expr; Type::check(...); return $__tyhp_ret_N;</c>.
        /// </summary>
        private EmitItem EmitCheckedReturn(
            IBase2Ast provider,
            IExpression expression,
            EmitItem parent,
            EmitType emitType)
        {
            var exprText = this.BuildExpression(expression);
            if (string.IsNullOrWhiteSpace(this._currentMethodGenericReturnCheck))
            {
                return this.ApplyDocComment(
                    provider,
                    EmitItem.Line(provider, emitType, "return " + exprText + ";", parent));
            }

            var tmp = this._context.GenerateUniqueVarName("__tyhp_ret");
            var expected = this._currentMethodGenericReturnCheck;
            return this.ApplyDocComment(
                provider,
                EmitItem.MultiLine(
                    provider,
                    emitType,
                    [
                        $"{tmp} = {exprText};",
                        $"\\Tyhp\\Type::check({tmp}, {expected});",
                        $"return {tmp};",
                    ],
                    parent));
        }

        private EmitItem EmitGotoStatement(PhpGotoStatementAst gotoStmt, EmitItem parent, EmitType emitType)
            => this.ApplyDocComment(gotoStmt, EmitItem.Line(gotoStmt, emitType, "goto " + gotoStmt.Identifier + ";", parent));

        private EmitItem EmitLabelStatement(PhpLabelStatementAst label, EmitItem parent, EmitType emitType)
            => EmitItem.Line(label, emitType, label.Identifier + ":", parent);

        private EmitItem EmitEchoStatement(PhpEchoStatementAst echo, EmitItem parent, EmitType emitType)
        {
            var line = "echo " + this.FormatExpressionList(echo.EchoExpressions) + ";";
            return this.ApplyDocComment(echo, EmitItem.Line(echo, emitType, line, parent));
        }

        private EmitItem EmitUnsetStatement(PhpUnsetStatementAst unset, EmitItem parent, EmitType emitType)
        {
            if (this.TryBuildHookBackingUnset(unset) is { } backingUnset)
            {
                return this.ApplyDocComment(unset, EmitItem.Line(unset, emitType, backingUnset, parent));
            }

            var line = "unset(" + this.FormatExpressionList(unset.Variables) + ");";
            return this.ApplyDocComment(unset, EmitItem.Line(unset, emitType, line, parent));
        }

        private EmitItem EmitGlobalStatement(PhpGlobalStatementAst global, EmitItem parent, EmitType emitType)
        {
            var variables = global.AstChildren.ElementAtOrDefault(0) as PhpVariableListAst;
            var line = "global " + this.FormatVariableList(variables) + ";";
            return this.ApplyDocComment(global, EmitItem.Line(global, emitType, line, parent));
        }

        private EmitItem EmitStaticStatement(PhpStaticStatementAst staticStmt, EmitItem parent, EmitType emitType)
        {
            var parts = staticStmt.Variables?.GetAllNotNull().Select(v =>
            {
                var name = this.BuildExpression(v);
                return v.DefaultValue != null ? name + " = " + this.BuildExpression(v.DefaultValue) : name;
            }) ?? [];
            var line = "static " + string.Join(", ", parts) + ";";
            return this.ApplyDocComment(staticStmt, EmitItem.Line(staticStmt, emitType, line, parent));
        }

        private EmitItem EmitConditionalStatement(PhpConditionalAst conditional, EmitItem parent, EmitType emitType)
        {
            if (conditional.IsMatchSyntax)
            {
                var content = this.BuildMatchExpression(conditional) + ";";
                return this.ApplyDocComment(conditional, EmitItem.Line(conditional, emitType, content, parent));
            }

            // PSR-12 §5.2: switch is a multiline braced structure, not a single compact line.
            return this.ApplyDocComment(
                conditional,
                this.EmitSwitchStatement(conditional, parent, emitType));
        }

        private EmitItem EmitSwitchStatement(PhpConditionalAst conditional, EmitItem parent, EmitType emitType)
        {
            var expr = this.BuildExpression(conditional.Expression);
            var block = EmitItem.Block(conditional, emitType, $"switch ({expr}) {{", "}", parent);
            var arms = conditional.Arms?.GetAllNotNull().ToList() ?? [];
            foreach (var arm in arms)
            {
                if (arm.IsDefault)
                {
                    this.EmitSwitchArm(block, arm, isDefault: true, condition: null);
                    continue;
                }

                foreach (var condition in arm.Conditions?.GetAllNotNull() ?? [])
                {
                    this.EmitSwitchArm(block, arm, isDefault: false, condition: condition);
                }
            }

            return block;
        }

        private void EmitSwitchArm(
            EmitItem switchBlock,
            PhpConditionalArmAst arm,
            bool isDefault,
            IExpression? condition)
        {
            var label = isDefault
                ? "default:"
                : "case " + this.BuildExpression(condition) + ":";

            var bodyStmts = arm.Body?.GetAllNotNull().ToList() ?? [];
            var lines = new List<string> { label };
            foreach (var stmt in bodyStmts)
            {
                var content = this.BuildStatementContent(stmt);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
                    {
                        lines.Add("    " + line);
                    }
                }
            }

            var bodyText = string.Join(" ", bodyStmts.Select(this.BuildStatementContent));
            if (!bodyText.Contains("break", StringComparison.Ordinal)
                && !bodyText.Contains("return", StringComparison.Ordinal))
            {
                lines.Add("    break;");
            }

            EmitItem.MultiLine(arm, EmitType.SubBlockStatement, lines, switchBlock);
        }

        private EmitItem EmitTypedVarStatement(TyhpTypedVarExprAst typedVar, EmitItem parent, EmitType emitType)
        {
            var line = this.BuildTypedVarExpression(typedVar) + ";";
            return this.ApplyDocComment(typedVar, EmitItem.Line(typedVar, emitType, line, parent));
        }

        private EmitItem EmitUsingBlockStatement(TyhpUsingBlockAst usingBlock, EmitItem parent, EmitType emitType)
        {
            var resources = usingBlock.Resources?.ToList() ?? [];
            var phpVars = new List<string>(resources.Count);
            var exprs = new List<string>(resources.Count);
            for (var i = 0; i < resources.Count; i++)
            {
                var resource = resources[i];
                var phpVar = resource.HasVariable && resource.Variable is IExpression varExpr
                    ? this.BuildExpression(varExpr)
                    : $"$__using_{i}";
                phpVars.Add(phpVar);
                exprs.Add(this.BuildExpression(resource.Expression));
            }

            var isAsync = usingBlock.IsAsync;
            if (isAsync)
            {
                this._context.RequirePackage("tyhp/async");
            }

            var multi = phpVars.Count > 1;

            if (!multi)
            {
                for (var i = 0; i < phpVars.Count; i++)
                {
                    EmitItem.Line(usingBlock, emitType, $"{phpVars[i]} = {exprs[i]};", parent);
                }
            }
            else
            {
                for (var i = 0; i < phpVars.Count; i++)
                {
                    EmitItem.Line(usingBlock, emitType, $"{phpVars[i]} = null;", parent);
                }
            }

            var first = this.EmitBraceSegments(usingBlock, parent, emitType,
            [
                ("try {", block =>
                {
                    if (multi)
                    {
                        for (var i = 0; i < phpVars.Count; i++)
                        {
                            EmitItem.Line(usingBlock, EmitType.SubBlockStatement, $"{phpVars[i]} = {exprs[i]};", block);
                        }
                    }

                    this.EmitStatementBody(usingBlock.Body, block);
                }),
                ("finally {", block =>
                {
                    if (!multi)
                    {
                        for (var i = phpVars.Count - 1; i >= 0; i--)
                        {
                            EmitDisposeForUsingResource(usingBlock, block, phpVars[i], isAsync, collectErrors: false);
                        }

                        return;
                    }

                    EmitItem.Line(usingBlock, EmitType.SubBlockStatement, "$__disposeErrors = [];", block);
                    for (var i = phpVars.Count - 1; i >= 0; i--)
                    {
                        EmitDisposeForUsingResource(usingBlock, block, phpVars[i], isAsync, collectErrors: true);
                    }

                    var throwIfErrors = EmitItem.Block(
                        usingBlock,
                        EmitType.SubBlockStatement,
                        "if (!empty($__disposeErrors)) {",
                        "}",
                        block);
                    EmitItem.Line(
                        usingBlock,
                        EmitType.SubBlockStatement,
                        "throw new \\Tyhp\\Exceptions\\AggregateException($__disposeErrors, 'One or more errors during disposal');",
                        throwIfErrors);
                }),
            ]);

            return this.ApplyDocComment(usingBlock, first);
        }

        private static void EmitDisposeForUsingResource(
            TyhpUsingBlockAst usingBlock,
            EmitItem finallyBlock,
            string phpVar,
            bool isAsync,
            bool collectErrors)
        {
            if (isAsync)
            {
                EmitAsyncDisposeForUsingResource(usingBlock, finallyBlock, phpVar, collectErrors);
                return;
            }

            var disposeGuard = EmitItem.Block(
                usingBlock,
                EmitType.SubBlockStatement,
                $"if ({phpVar} instanceof \\Tyhp\\Contracts\\IsDisposable) {{",
                "}",
                finallyBlock);

            EmitDisposeCall(usingBlock, disposeGuard, $"{phpVar}->dispose();", collectErrors);
        }

        private static void EmitAsyncDisposeForUsingResource(
            TyhpUsingBlockAst usingBlock,
            EmitItem finallyBlock,
            string phpVar,
            bool collectErrors)
        {
            // using await: prefer AsyncIsDisposable, fall back to sync IsDisposable.
            var asyncGuard = EmitItem.Block(
                usingBlock,
                EmitType.SubBlockStatement,
                $"if ({phpVar} instanceof \\Tyhp\\Contracts\\AsyncIsDisposable) {{",
                "}",
                finallyBlock);
            EmitDisposeCall(
                usingBlock,
                asyncGuard,
                $"\\Tyhp\\Promise::_await({phpVar}->disposeAsync());",
                collectErrors);

            var syncGuard = EmitItem.Block(
                usingBlock,
                EmitType.SubBlockStatement,
                $"elseif ({phpVar} instanceof \\Tyhp\\Contracts\\IsDisposable) {{",
                "}",
                finallyBlock);
            EmitDisposeCall(usingBlock, syncGuard, $"{phpVar}->dispose();", collectErrors);
        }

        private static void EmitDisposeCall(
            TyhpUsingBlockAst usingBlock,
            EmitItem disposeGuard,
            string disposeCall,
            bool collectErrors)
        {
            if (!collectErrors)
            {
                EmitItem.Line(usingBlock, EmitType.SubBlockStatement, disposeCall, disposeGuard);
                return;
            }

            var tryDispose = EmitItem.Block(
                usingBlock,
                EmitType.SubBlockStatement,
                "try {",
                "}",
                disposeGuard);
            EmitItem.Line(usingBlock, EmitType.SubBlockStatement, disposeCall, tryDispose);

            var catchBlock = EmitItem.Block(
                usingBlock,
                EmitType.SubBlockStatement,
                "catch (\\Throwable $__e) {",
                "}",
                disposeGuard);
            EmitItem.Line(usingBlock, EmitType.SubBlockStatement, "$__disposeErrors[] = $__e;", catchBlock);
        }

        private EmitItem EmitUnaryStatement(PhpUnaryOpAst unary, EmitItem parent, EmitType emitType)
        {
            var line = this.BuildUnaryExpression(unary) + ";";
            return this.ApplyDocComment(unary, EmitItem.Line(unary, emitType, line, parent));
        }

        private EmitItem EmitInlineOutput(PhpInlineOutputAst inlineOutput, EmitItem parent, EmitType emitType)
        {
            if (inlineOutput.IsEcho)
            {
                foreach (var stmt in inlineOutput.TopStatementList?.GetAllNotNull() ?? [])
                {
                    if (stmt is IStatement statement)
                    {
                        this.EmitStatement(statement, parent, emitType);
                    }
                }

                return EmitItem.Empty(inlineOutput, emitType, parent);
            }

            return EmitItem.Line(inlineOutput, EmitType.OutsideItems, "?>" + inlineOutput.Content + "<?php", parent);
        }

        private EmitItem EmitInlineOutputList(PhpInlineOutputListAst inlineList, EmitItem parent, EmitType emitType)
        {
            foreach (var item in inlineList.GetAllNotNull())
            {
                this.EmitInlineOutput(item, parent, emitType);
            }

            return EmitItem.Empty(inlineList, emitType, parent);
        }

        private static bool IsUsingCall(PhpDereferenceableAst deref)
        {
            return deref.Base is PhpNameAst name && name.ValueString == "using"
                && deref.Suffix is PhpCallAst;
        }

        private EmitItem EmitUsingCallStatement(PhpDereferenceableAst deref, EmitItem parent, EmitType emitType)
        {
            if (!IsUsingCall(deref))
            {
                return EmitItem.Empty(deref, emitType, parent);
            }

            this._context.EnterDisposableBlockScope();
            try
            {
                var scopeVar = this._context.EnsureDisposableScopeForCurrentBlock();
                EmitItem.Line(deref, emitType, $"{scopeVar} = \\Tyhp\\DisposableScope::create();", parent);

                var callAst = (PhpCallAst)deref.Suffix;
                var args = callAst.Arguments.GetAllNotNull().ToList();

                if (args.Count == 1
                    && args[0].Expression is PhpBinaryOpAst binaryOp
                    && TryGetUsingCallAssignment(binaryOp, out var varName, out var exprAst))
                {
                    var expr = this.BuildExpression(exprAst);
                    return EmitItem.Line(deref, emitType, $"{varName} = {scopeVar}->using({expr});", parent);
                }

                return EmitItem.Empty(deref, emitType, parent);
            }
            finally
            {
                this._context.ExitDisposableBlockScope();
            }
        }

        private string BuildUsingCallCondition(PhpDereferenceableAst deref, string scopeVar)
        {
            if (!IsUsingCall(deref))
            {
                return this.BuildExpression(deref);
            }

            var callAst = (PhpCallAst)deref.Suffix;
            var args = callAst.Arguments.GetAllNotNull().ToList();

            if (args.Count == 1
                && args[0].Expression is PhpBinaryOpAst binaryOp
                && TryGetUsingCallAssignment(binaryOp, out var varName, out var exprAst))
            {
                var expr = this.BuildExpression(exprAst);
                return $"{scopeVar}->using({varName} = {expr})";
            }

            return $"{scopeVar}->using({this.BuildDereferenceableSuffix(deref.Suffix)})";
        }

        private static bool TryGetUsingCallAssignment(
            PhpBinaryOpAst binaryOp,
            out string varName,
            out IExpression expr)
        {
            varName = string.Empty;
            expr = null!;

            if (binaryOp.Operator is null || binaryOp.Left is not PhpVariableAst leftVar)
            {
                return false;
            }

            var assignOp = PhpAssignmentOperatorExtensions.FromToken((int)binaryOp.Operator.ValueInt64);
            if (assignOp is not (PhpAssignmentOperator.Assign or PhpAssignmentOperator.UsingEqual)
                && binaryOp.Operator.ValueInt64 != TyhpParser.T_TYHP_USING_EQUAL)
            {
                return false;
            }

            varName = leftVar.VariableToken?.ValueString
                ?? string.Empty;
            if (string.IsNullOrEmpty(varName) && leftVar.VariableExpression != null)
            {
                return false;
            }

            if (binaryOp.Right is not IExpression rightExpr)
            {
                return false;
            }

            expr = rightExpr;
            return !string.IsNullOrEmpty(varName);
        }
    }
}

