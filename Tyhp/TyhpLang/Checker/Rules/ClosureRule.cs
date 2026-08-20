using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Closure and arrow-function capture validation.</summary>
    public sealed class ClosureRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(PhpInlineFunctionAst)];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is not PhpInlineFunctionAst closure)
            {
                return;
            }

            var isStatic = HasStaticModifier(closure.Modifiers) || ClosureHasStaticModifier(closure);

            ValidateUseVariables(closure, isStatic, state, context, diagnostics);

            var closureState = state.Split(ScopeType.AnonymousFunctionDeclaration);
            closureState.IsInAsyncContext = state.IsInAsyncContext || IsAsyncClosure(closure);
            closureState.IsInsideClosure = true;
            // A closure's return type is its own, not the enclosing callable's — an untyped closure
            // must not silently inherit whatever the lexically enclosing function/method expected
            // (e.g. `void` for a closure declared inside `__construct`/`__destruct`). Call-site /
            // annotation contextual typing (`ExpectedClosureType`) is different and is applied below.
            closureState.ExpectedReturnType = null;
            if (isStatic)
            {
                closureState.Modifiers |= MemberModifier.Static;
            }

            if (closure.ReturnType is not null)
            {
                closureState.ExpectedReturnType = context.ResolveTypeAnnotation(
                    closure.ReturnType, closureState, isReturnTypePosition: true);
                // ClosureRule suppresses child traversal, so the return-type annotation is never
                // CheckNode'd — still count import usage for TYHP4130.
                context.MarkImportNames(closure.ReturnType, state);
            }

            RegisterCapturedVariables(closure, closureState, state);
            // Non-static closures automatically bind `$this` from the enclosing instance method
            // (PHP does not require `use ($this)`). Without this, `LookupVariable("this")` stops at
            // the anonymous-function boundary, `$this` types as unresolved, and suffixes like
            // `$this->map[$k]->method()` fall through array-access's mixed default — false TYHP4160.
            if (!isStatic)
            {
                BindEnclosingThis(closureState, state);
            }

            ClosureParameterInference.InferAndRegisterParameters(closure, closureState, state, context, diagnostics);

            // Contextual `callable<…>` / annotation expectation supplies the return when the author
            // omitted it (same source as inferred parameter types).
            if (closure.ReturnType is null && closureState.ExpectedReturnType is null)
            {
                var expectedType = closureState.ExpectedClosureType ?? state.ExpectedClosureType;
                var parameterCount = closure.Parameters?.GetAllNotNull().Count() ?? 0;
                if (expectedType is not null
                    && CallableArityFacetBuilder.TrySelectCallableFacetForClosure(
                        expectedType, parameterCount, out var expectedFacet)
                    && expectedFacet is not null)
                {
                    closureState.ExpectedReturnType = expectedFacet.ReturnType;
                }
            }

            if (closure.Body is not null)
            {
                context.CheckStatementBlock(closure.Body, closureState);
                if (isStatic)
                {
                    foreach (var variable in FindVariables(closure.Body))
                    {
                        if (CheckerHelpers.IsThisVariable(variable))
                        {
                            CheckerHelpers.ReportError(
                                diagnostics, state, variable, MessageCode.CheckerStaticClosureThis);
                        }
                    }
                }
            }
            else if (closure.IsArrowFunction)
            {
                foreach (var child in closure.AstChildren)
                {
                    if (child is not null)
                    {
                        context.CheckNode(child, closureState);
                    }
                }

                if (isStatic)
                {
                    foreach (var variable in FindVariables(closure))
                    {
                        if (CheckerHelpers.IsThisVariable(variable))
                        {
                            CheckerHelpers.ReportError(
                                diagnostics, state, variable, MessageCode.CheckerStaticClosureThis);
                        }
                    }
                }
            }

            // The closure's own scope is the only place its receivers resolve correctly, so generic
            // call targets inside it are recorded here rather than from the enclosing callable.
            context.RecordGenericCallTargetsIn(closure, closureState);
        }

        /// <summary>
        /// Seeds outer locals and <c>$this</c> into an <c>async { }</c> block's checker state
        /// (implicit capture, like arrow functions).
        /// </summary>
        internal static void PrepareAsyncBlockCaptures(
            IBase2Ast? body,
            CheckerState innerState,
            CheckerState outerState)
        {
            BindEnclosingThis(innerState, outerState);
            if (body is null)
            {
                return;
            }

            foreach (var variable in FindVariables(body))
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is null
                    || string.Equals(name, "this", StringComparison.OrdinalIgnoreCase)
                    || innerState.Variables.ContainsKey(name))
                {
                    continue;
                }

                if (outerState.LookupVariable(name) is { } outerVar)
                {
                    innerState.Variables[name] = outerVar.Clone();
                }
            }
        }

        private static bool IsAsyncClosure(PhpInlineFunctionAst closure)
        {
            if (closure.AstGrammarAddons.ContainsKey("isAsync"))
            {
                return true;
            }

            if (closure.AstGrammarAddons.TryGetValue("modifiers", out var addon))
            {
                if (addon is TokenValueListAst list && list.GetAllNotNull().Any(IsAsyncToken))
                {
                    return true;
                }

                if (addon is TokenValueAst token && IsAsyncToken(token))
                {
                    return true;
                }
            }

            return HasAsyncModifier(closure.Modifiers);
        }

        private static bool HasAsyncModifier(TokenValueListAst? modifiers)
        {
            if (modifiers is null)
            {
                return false;
            }

            foreach (var token in modifiers.GetAllNotNull())
            {
                if (IsAsyncToken(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAsyncToken(TokenValueAst token) =>
            token.ValueInt64 == TyhpParser.T_TYHP_ASYNC
            || string.Equals(token.ValueString, "async", StringComparison.OrdinalIgnoreCase);

        private static bool ClosureHasStaticModifier(PhpInlineFunctionAst closure)
        {
            foreach (var child in closure.AstChildren)
            {
                if (child is TokenValueListAst list && HasStaticModifier(list))
                {
                    return true;
                }

                if (child is TokenValueAst token && IsStaticToken(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStaticToken(TokenValueAst token) =>
            string.Equals(token.ValueString, "static", StringComparison.OrdinalIgnoreCase)
            || token.ValueInt64 == TyhpParser.T_STATIC;

        private static bool HasStaticModifier(TokenValueListAst? modifiers)
        {
            if (modifiers is null)
            {
                return false;
            }

            foreach (var token in modifiers.GetAllNotNull())
            {
                if (IsStaticToken(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateUseVariables(
            PhpInlineFunctionAst closure,
            bool isStatic,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            foreach (var used in closure.LexicalVars?.GetAllNotNull() ?? [])
            {
                var name = CheckerHelpers.GetVariableName(used);
                if (name is null)
                {
                    continue;
                }

                if (CheckerHelpers.IsThisVariable(used))
                {
                    if (isStatic)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, used, MessageCode.CheckerStaticClosureThis);
                    }
                    else
                    {
                        CheckerHelpers.ReportWarning(
                            diagnostics, state, used, MessageCode.CheckerClosureUseThis);
                    }

                    continue;
                }

                if (state.LookupVariable(name) is null && state.EnclosingObject is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, used, MessageCode.CheckerClosureUseUndefined, name);
                }
            }

            if (isStatic && closure.Body is not null)
            {
                foreach (var variable in FindVariables(closure.Body))
                {
                    if (CheckerHelpers.IsThisVariable(variable))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, variable, MessageCode.CheckerStaticClosureThis);
                    }
                }
            }
        }

        private static void RegisterCapturedVariables(
            PhpInlineFunctionAst closure,
            CheckerState closureState,
            CheckerState outerState)
        {
            foreach (var used in closure.LexicalVars?.GetAllNotNull() ?? [])
            {
                var name = CheckerHelpers.GetVariableName(used);
                if (name is null)
                {
                    continue;
                }

                var outerVar = outerState.LookupVariable(name);
                if (outerVar is not null)
                {
                    closureState.Variables[name] = outerVar.Clone();
                }
            }

            if (!closure.IsArrowFunction)
            {
                return;
            }

            foreach (var name in CollectFreeVariables(closure, outerState))
            {
                if (closureState.Variables.ContainsKey(name))
                {
                    continue;
                }

                var outerVar = outerState.LookupVariable(name);
                if (outerVar is not null)
                {
                    closureState.Variables[name] = outerVar.Clone();
                }
            }
        }

        /// <summary>
        /// Binds the enclosing method's <c>$this</c> into a non-static closure and re-seeds
        /// <c>$this-&gt;prop</c> initialization / narrowing across the function boundary.
        /// </summary>
        private static void BindEnclosingThis(CheckerState closureState, CheckerState outerState)
        {
            if (outerState.LookupVariable("this") is not { } thisVar)
            {
                return;
            }

            closureState.Variables["this"] = thisVar.Clone();
            // Anonymous-function scopes start with a fresh PropertyInit map (function boundary).
            // Re-seed from the enclosing method so definite-assignment and control-flow narrowing
            // for `$this->prop` remain visible inside the closure body.
            closureState.ReplacePropertyInit(outerState.CloneVisiblePropertyInit());
        }

        private static IEnumerable<string> CollectFreeVariables(PhpInlineFunctionAst closure, CheckerState outerState)
        {
            if (closure.Body is null)
            {
                yield break;
            }

            var paramNames = closure.Parameters?.GetAllNotNull()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal) ?? [];

            foreach (var stmt in closure.Body.GetAllNotNull())
            {
                foreach (var variable in FindVariables(stmt))
                {
                    var name = CheckerHelpers.GetVariableName(variable);
                    if (name is null || paramNames.Contains(name))
                    {
                        continue;
                    }

                    if (outerState.LookupVariable(name) is not null)
                    {
                        yield return name;
                    }
                }
            }
        }

        private static IEnumerable<PhpVariableAst> FindVariables(IBase2Ast node)
        {
            if (node is PhpVariableAst variable)
            {
                yield return variable;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    foreach (var found in FindVariables(child))
                    {
                        yield return found;
                    }
                }
            }
        }
    }
}
