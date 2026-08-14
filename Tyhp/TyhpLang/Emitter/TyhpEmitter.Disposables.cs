using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        /// <summary>
        /// Emits block statements into an existing parent (braces already present), applying
        /// disposable-scope or try/finally strategy for this block depth.
        /// </summary>
        private void EmitBlockContents(PhpStatementBlockAst block, EmitItem parent, EmitType emitType)
        {
            this._context.EnterDisposableBlockScope();
            try
            {
                if (ContainsUsingEqualAssignment(block)
                    && this._context.RequiresDisposableTryFinallyFor(block))
                {
                    this.EmitDisposableTryFinallyInto(block, parent, emitType);
                    return;
                }

                if (ContainsUsingEqualAssignment(block))
                {
                    var scopeVar = this._context.EnsureDisposableScopeForCurrentBlock();
                    EmitItem.Line(
                        block,
                        emitType,
                        $"{scopeVar} = \\Tyhp\\DisposableScope::create();",
                        parent);
                }

                foreach (var stmt in block.GetAllNotNull())
                {
                    this.EmitStatement(stmt, parent, emitType);
                }
            }
            finally
            {
                this._context.ExitDisposableBlockScope();
            }
        }

        private bool TryEmitWeakReferencePropertyClosure(
            PhpBinaryOpAst binary,
            EmitItem parent,
            EmitType emitType,
            out EmitItem emitted)
        {
            emitted = EmitItem.Empty(binary, emitType, parent);
            if (!IsPlainAssignment(binary)
                || binary.Right is not PhpInlineFunctionAst closure
                || !this._context.RequiresWeakReferenceCaptureFor(closure)
                || !IsThisPropertyTarget(binary.Left))
            {
                return false;
            }

            var weakVar = this._context.GenerateUniqueVarName("__weakSelf");
            EmitItem.Line(binary, emitType, $"{weakVar} = \\WeakReference::create($this);", parent);

            // LHS must keep `$this->prop` — only the closure body is rewritten.
            var left = this.BuildExpression(binary.Left);

            var previous = this._context.WeakSelfCaptureVar;
            this._context.WeakSelfCaptureVar = weakVar;
            try
            {
                var right = this.BuildInlineFunctionExpression(closure);
                emitted = EmitItem.Line(binary, emitType, $"{left} = {right};", parent);
            }
            finally
            {
                this._context.WeakSelfCaptureVar = previous;
            }

            return true;
        }

        /// <summary>
        /// Emits try/finally into an existing parent (e.g. function body braces).
        /// </summary>
        private void EmitDisposableTryFinallyInto(
            PhpStatementBlockAst body,
            EmitItem parent,
            EmitType emitType)
        {
            var disposableVars = CollectUsingEqualVariableNames(body);
            this._context.BeginDisposableTryFinallyFallback();
            try
            {
                // Null-init so a constructor throw on the Nth resource still leaves earlier
                // vars defined (and later ones null) for safe finally disposal.
                foreach (var phpVar in disposableVars)
                {
                    EmitItem.Line(body, emitType, $"{phpVar} = null;", parent);
                }

                this.EmitBraceSegments(body, parent, emitType,
                [
                    ("try {", block =>
                    {
                        foreach (var stmt in body.GetAllNotNull())
                        {
                            this.EmitStatement(stmt, block, EmitType.SubBlockStatement);
                        }
                    }),
                    ("finally {", block =>
                    {
                        var collectErrors = disposableVars.Count > 1;
                        if (collectErrors)
                        {
                            EmitItem.Line(body, EmitType.SubBlockStatement, "$__disposeErrors = [];", block);
                        }

                        for (var i = disposableVars.Count - 1; i >= 0; i--)
                        {
                            EmitSyncDisposeGuard(body, block, disposableVars[i], collectErrors);
                        }

                        if (!collectErrors)
                        {
                            return;
                        }

                        var throwIfErrors = EmitItem.Block(
                            body,
                            EmitType.SubBlockStatement,
                            "if (!empty($__disposeErrors)) {",
                            "}",
                            block);
                        EmitItem.Line(
                            body,
                            EmitType.SubBlockStatement,
                            "throw new \\Tyhp\\Exceptions\\AggregateException($__disposeErrors, 'One or more errors during disposal');",
                            throwIfErrors);
                    }),
                ]);
            }
            finally
            {
                this._context.EndDisposableTryFinallyFallback();
            }
        }

        private static void EmitSyncDisposeGuard(
            IBase2Ast provider,
            EmitItem parent,
            string phpVar,
            bool collectErrors)
        {
            var disposeGuard = EmitItem.Block(
                provider,
                EmitType.SubBlockStatement,
                $"if ({phpVar} instanceof \\Tyhp\\Contracts\\IsDisposable) {{",
                "}",
                parent);

            if (!collectErrors)
            {
                EmitItem.Line(provider, EmitType.SubBlockStatement, $"{phpVar}->dispose();", disposeGuard);
                return;
            }

            var tryDispose = EmitItem.Block(
                provider,
                EmitType.SubBlockStatement,
                "try {",
                "}",
                disposeGuard);
            EmitItem.Line(provider, EmitType.SubBlockStatement, $"{phpVar}->dispose();", tryDispose);

            var catchBlock = EmitItem.Block(
                provider,
                EmitType.SubBlockStatement,
                "catch (\\Throwable $__e) {",
                "}",
                disposeGuard);
            EmitItem.Line(provider, EmitType.SubBlockStatement, "$__disposeErrors[] = $__e;", catchBlock);
        }

        private static List<string> CollectUsingEqualVariableNames(IBase2Ast node)
        {
            var names = new List<string>();
            CollectUsingEqualVariableNames(node, names);
            return names;
        }

        private static void CollectUsingEqualVariableNames(IBase2Ast node, List<string> names)
        {
            if (node is PhpBinaryOpAst binary && IsUsingEqualOperator(binary) && binary.Left is PhpVariableAst variable)
            {
                var raw = variable.VariableToken?.ValueString;
                if (!string.IsNullOrEmpty(raw))
                {
                    names.Add(raw.StartsWith('$') ? raw : "$" + raw);
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child is null || child is PhpStatementBlockAst || child is PhpInlineFunctionAst)
                {
                    continue;
                }

                CollectUsingEqualVariableNames(child, names);
            }
        }

        private static bool IsThisPropertyTarget(IExpression? left) =>
            left is PhpDereferenceableAst
            {
                Base: PhpVariableAst baseVar,
                Suffix: PhpInstanceMemberAccessAst
            } && IsThisVariable(baseVar);

        private static bool IsThisVariable(PhpVariableAst variable)
        {
            var name = variable.VariableToken?.ValueString?.TrimStart('$');
            return string.Equals(name, "this", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlainAssignment(PhpBinaryOpAst binary) =>
            binary.Operator?.ValueString == "="
            || (binary.Operator?.ValueInt64 is long token && (int)token == TyhpParser.T_SYM_EQUAL)
            || PhpAssignmentOperatorExtensions.FromToken(
                    binary.Operator?.ValueInt64 is long t ? (int)t : -1)
                == PhpAssignmentOperator.Assign;
    }
}
