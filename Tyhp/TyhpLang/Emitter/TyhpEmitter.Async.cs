using Tyhp.Config;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private EmitItem EmitAsyncForeachLoop(PhpLoopAst loop, EmitItem parent, EmitType emitType)
        {
            this._context.RequirePackage("tyhp/async");

            var awaitExpr = loop.Condition as PhpUnaryOpAst
                ?? throw new InvalidOperationException("Async foreach requires await unary operand.");
            var operand = awaitExpr.Operand as IExpression;
            var operandPhp = this.BuildExpression(operand);
            var kind = this._context.GetAsyncForeachKind(loop);

            // Parse-only / missing checker classification: PromiseIterable is the safe default
            // (matches `foreach (_await($expr) as …)`).
            if (kind == AsyncForeachKind.None)
            {
                kind = AsyncForeachKind.PromiseIterable;
            }

            if (kind == AsyncForeachKind.PromiseIterable)
            {
                return this.EmitPromiseIterableForeach(loop, parent, emitType, operandPhp);
            }

            return this.EmitAsyncIteratorWhile(loop, parent, emitType, operandPhp, kind);
        }

        private EmitItem EmitPromiseIterableForeach(
            PhpLoopAst loop,
            EmitItem parent,
            EmitType emitType,
            string operandPhp)
        {
            string foreachClause;
            if (loop.KeyVariable != null)
            {
                foreachClause = this.BuildForeachVariable(loop.KeyVariable)
                    + " => "
                    + this.BuildForeachVariable(loop.ValueVariable);
            }
            else
            {
                foreachClause = this.BuildForeachVariable(loop.ValueVariable);
            }

            var block = EmitItem.Block(
                loop,
                emitType,
                $"foreach (\\Tyhp\\Promise::_await({operandPhp}) as {foreachClause}) {{",
                "}",
                parent);
            this.EmitLoopBody(loop.Body, block);
            return this.ApplyDocComment(loop, block);
        }

        private EmitItem EmitAsyncIteratorWhile(
            PhpLoopAst loop,
            EmitItem parent,
            EmitType emitType,
            string operandPhp,
            AsyncForeachKind kind)
        {
            var iterVar = this._context.GenerateAsyncIterVarName();
            var initRhs = kind == AsyncForeachKind.PromiseAsyncIterable
                ? $"\\Tyhp\\Promise::_await({operandPhp})->getAsyncIterator()"
                : $"{operandPhp}->getAsyncIterator()";

            // Emit as sibling lines under parent: init; while (…) { assigns; body }
            var initLine = EmitItem.Line(loop, emitType, $"{iterVar} = {initRhs};", parent);
            var whileBlock = EmitItem.Block(
                loop,
                emitType,
                $"while (\\Tyhp\\Promise::_await({iterVar}->next())) {{",
                "}",
                parent);

            if (loop.KeyVariable != null)
            {
                var keyPhp = this.BuildForeachVariable(loop.KeyVariable);
                var valuePhp = this.BuildForeachVariable(loop.ValueVariable);
                EmitItem.Line(
                    loop,
                    EmitType.SubBlockStatement,
                    $"{keyPhp} = \\Tyhp\\Promise::_await({iterVar}->currentKey());",
                    whileBlock);
                EmitItem.Line(
                    loop,
                    EmitType.SubBlockStatement,
                    $"{valuePhp} = \\Tyhp\\Promise::_await({iterVar}->currentValue());",
                    whileBlock);
            }
            else
            {
                var valuePhp = this.BuildForeachVariable(loop.ValueVariable);
                EmitItem.Line(
                    loop,
                    EmitType.SubBlockStatement,
                    $"{valuePhp} = \\Tyhp\\Promise::_await({iterVar}->current());",
                    whileBlock);
            }

            this.EmitLoopBody(loop.Body, whileBlock);
            _ = this.ApplyDocComment(loop, whileBlock);
            return initLine;
        }

        private static bool IsAwaitForeach(PhpLoopAst loop) =>
            loop.Condition is PhpUnaryOpAst unary
            && (string.Equals(unary.Operator?.ValueString, "await", StringComparison.OrdinalIgnoreCase)
                || unary.Operator?.ValueInt64 == TyhpParser.T_TYHP_AWAIT);

        /// <summary>
        /// Application projects wrap entry-point top-level statements that contain <c>await</c>
        /// in <c>\Tyhp\Promise::run(function() { … })</c>. Library projects skip auto-start.
        /// </summary>
        private bool ShouldWrapEntryPointInPromiseRun(PHPOutputFile outputFile)
        {
            if (!outputFile.IsEntryPoint)
            {
                return false;
            }

            if (this._context.Project?.Type == ProjectType.Library)
            {
                return false;
            }

            return outputFile.Statements
                .Where(s => !IsTopLevelDeclaration(s, outputFile))
                .Any(ContainsAwaitExpression);
        }

        private static bool ContainsAwaitExpression(IBase2Ast? node)
        {
            if (node is null)
            {
                return false;
            }

            if (node is PhpUnaryOpAst unary
                && (string.Equals(unary.Operator?.ValueString, "await", StringComparison.OrdinalIgnoreCase)
                    || unary.Operator?.ValueInt64 == TyhpParser.T_TYHP_AWAIT))
            {
                return true;
            }

            // Do not descend into nested function/method/closure declarations — only top-level
            // statements of the entry point should trigger Promise::run wrapping.
            if (node is PhpFunctionDeclAst or PhpMethodDeclAst or PhpInlineFunctionAst or PhpObjectTypeDeclAst)
            {
                return false;
            }

            foreach (var child in node.AstChildren)
            {
                if (ContainsAwaitExpression(child))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAsyncInlineFunction(PhpInlineFunctionAst inlineFn)
        {
            if (this.IsAsyncModifiers(inlineFn))
            {
                return true;
            }

            if (inlineFn.Modifiers == null)
            {
                return false;
            }

            return inlineFn.Modifiers.GetAllNotNull().Any(IsAsyncToken);
        }

        private string BuildAsyncInlineFunctionExpression(PhpInlineFunctionAst inlineFn)
        {
            this._context.RequirePackage("tyhp/async");

            var staticPrefix = this.FormatInlineFunctionModifiersStrippingAsync(inlineFn.Modifiers);
            var refPrefix = inlineFn.ReturnsRef ? "&" : "";
            var paramsText = this.FormatInlineFunctionParameterList(inlineFn);

            if (inlineFn.IsArrowFunction)
            {
                var bodyExpr = inlineFn.Body?.GetAllNotNull().FirstOrDefault() is PhpUnaryOpAst ret
                    ? this.BuildExpression(ret.Operand)
                    : "null";

                // async arrow → outer arrow returning Promise wrapping an inner arrow handed to
                // _async. Nested PHP arrow functions auto-capture outer variables by value, so this
                // preserves the capture semantics of the original `async fn` (both the outer
                // parameters and any free variables referenced in the body). A `function() use (…)`
                // desugaring would silently drop those free variables.
                return $"{staticPrefix}fn{refPrefix}({paramsText}): \\Tyhp\\Promise => \\Tyhp\\Promise::_async(fn() => {bodyExpr})";
            }

            var originalReturn = this.FormatInlineFunctionReturnType(inlineFn);

            var useParts = this.CollectInlineFunctionUseParts(inlineFn);
            // Capture parameters into the inner _async closure (like named async functions).
            if (inlineFn.Parameters != null)
            {
                foreach (var parameter in inlineFn.Parameters.GetAllNotNull())
                {
                    if (string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        continue;
                    }

                    if (!useParts.Any(p => string.Equals(p.TrimStart('&'), parameter.Name, StringComparison.Ordinal)))
                    {
                        useParts.Add(parameter.Name);
                    }
                }
            }

            var useClause = useParts.Count > 0 ? " use (" + string.Join(", ", useParts) + ")" : "";
            var outerUse = this.BuildOuterClosureUseClause(inlineFn);

            var body = this.BuildMethodBodyInline(inlineFn.Body);
            // Strip surrounding braces from BuildMethodBodyInline if present — we nest inside _async.
            var innerBody = body.Trim();
            if (innerBody.StartsWith("{", StringComparison.Ordinal) && innerBody.EndsWith("}", StringComparison.Ordinal))
            {
                innerBody = innerBody[1..^1].Trim();
            }

            // Re-indent stripped body lines one level for the inner _async closure.
            var innerBodyIndented = string.IsNullOrEmpty(innerBody)
                ? ""
                : string.Join(
                    "\n",
                    innerBody.Replace("\r\n", "\n").Split('\n').Select(l =>
                        string.IsNullOrEmpty(l) ? "" : "    " + l));

            var functionKeyword = inlineFn.ReturnsRef ? "function &" : "function ";
            var outerBody = string.IsNullOrEmpty(innerBodyIndented)
                ? "return \\Tyhp\\Promise::_async(function ()" + useClause + originalReturn + " {\n    });"
                : "return \\Tyhp\\Promise::_async(function ()" + useClause + originalReturn + " {\n"
                    + string.Join("\n", innerBodyIndented.Split('\n').Select(l => "    " + l))
                    + "\n    });";

            return $"{staticPrefix}{functionKeyword}({paramsText}){outerUse}: \\Tyhp\\Promise {{\n    {outerBody}\n}}";
        }

        private string FormatInlineFunctionModifiersStrippingAsync(TokenValueListAst? modifiers)
        {
            if (modifiers == null)
            {
                return "";
            }

            var parts = modifiers.GetAllNotNull()
                .Select(t => t.ValueString ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s)
                    && !string.Equals(s, "async", StringComparison.OrdinalIgnoreCase));
            var text = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(text) ? "" : text + " ";
        }

        private List<string> CollectInlineFunctionUseParts(PhpInlineFunctionAst inlineFn)
        {
            var useParts = new List<string>();
            if (inlineFn.LexicalVars?.GetAllNotNull().Any() == true)
            {
                foreach (var lexical in inlineFn.LexicalVars.GetAllNotNull())
                {
                    var rendered = this.FormatVariableListItem(lexical);
                    if (this._context.WeakSelfCaptureVar is not null
                        && string.Equals(rendered.TrimStart('&', '$'), "this", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    useParts.Add(rendered);
                }
            }

            if (this._context.WeakSelfCaptureVar is { } weakVar
                && !useParts.Any(p => string.Equals(p.TrimStart('&'), weakVar, StringComparison.Ordinal)))
            {
                useParts.Add(weakVar);
            }

            foreach (var capture in this.CollectVariantCapturesFor(inlineFn))
            {
                if (!useParts.Any(p => string.Equals(p.TrimStart('&'), capture, StringComparison.Ordinal)))
                {
                    useParts.Add(capture);
                }
            }

            return useParts;
        }

        private string BuildOuterClosureUseClause(PhpInlineFunctionAst inlineFn)
        {
            if (inlineFn.IsArrowFunction)
            {
                return "";
            }

            var useParts = new List<string>();
            if (inlineFn.LexicalVars?.GetAllNotNull().Any() == true)
            {
                foreach (var lexical in inlineFn.LexicalVars.GetAllNotNull())
                {
                    var rendered = this.FormatVariableListItem(lexical);
                    if (this._context.WeakSelfCaptureVar is not null
                        && string.Equals(rendered.TrimStart('&', '$'), "this", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    useParts.Add(rendered);
                }
            }

            if (this._context.WeakSelfCaptureVar is { } weakVar
                && !useParts.Any(p => string.Equals(p.TrimStart('&'), weakVar, StringComparison.Ordinal)))
            {
                useParts.Add(weakVar);
            }

            foreach (var capture in this.CollectVariantCapturesFor(inlineFn))
            {
                if (!useParts.Any(p => string.Equals(p.TrimStart('&'), capture, StringComparison.Ordinal)))
                {
                    useParts.Add(capture);
                }
            }

            return useParts.Count > 0 ? " use (" + string.Join(", ", useParts) + ")" : "";
        }
    }
}
