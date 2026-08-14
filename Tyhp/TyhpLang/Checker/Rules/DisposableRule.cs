using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Validates disposable <c>:=</c> assignments and <c>using</c> blocks, and flags emission strategy
    /// for WeakReference closure captures and try/finally circular-reference fallback.
    /// </summary>
    public sealed class DisposableRule : ICheckerRule
    {
        // PhpMethodDeclAst is intentionally absent: CheckObjectBody calls CheckMethod directly
        // (not CheckNode), so circular-disposable analysis runs via AnalyzeMethodBody from that path.
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpBinaryOpAst),
            typeof(TyhpUsingBlockAst),
            typeof(PhpStatementBlockAst),
            typeof(PhpFunctionDeclAst),
        ];

        public bool Handles(IBase2Ast node) =>
            node is TyhpUsingBlockAst
            || node is PhpStatementBlockAst
            || node is PhpFunctionDeclAst
            || node is PhpBinaryOpAst binary
                && (IsUsingEqualOperator(binary) || IsPlainAssignment(binary));

        public bool SuppressChildTraversal(IBase2Ast node) =>
            node is TyhpUsingBlockAst;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpBinaryOpAst binary when IsUsingEqualOperator(binary):
                    CheckDisposableAssignment(binary, state, context, diagnostics);
                    break;
                case PhpBinaryOpAst binary when IsPlainAssignment(binary):
                    CheckClosurePropertyAssignment(binary, state, context);
                    break;
                case TyhpUsingBlockAst usingBlock:
                    CheckUsingBlock(usingBlock, state, context, diagnostics);
                    break;
                case PhpStatementBlockAst block:
                    AnalyzeCircularDisposableReferences(block, context, diagnostics, state);
                    break;
                case PhpFunctionDeclAst { Body: PhpStatementBlockAst funcBody }:
                    // DeclarationRule checks body via CheckStatementBlock (skips Dispatch on the block).
                    AnalyzeCircularDisposableReferences(funcBody, context, diagnostics, state);
                    break;
            }
        }

        /// <summary>
        /// Analyzes a method body for circular disposable references. Invoked explicitly from
        /// <c>DeclarationRule.CheckMethod</c> because class members bypass <c>CheckNode</c>.
        /// </summary>
        public static void AnalyzeMethodBody(
            PhpMethodDeclAst method,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (method.Body is PhpStatementBlockAst methodBody)
            {
                AnalyzeCircularDisposableReferences(methodBody, context, diagnostics, state);
            }
        }

        private static void CheckDisposableAssignment(
            PhpBinaryOpAst binary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (binary.Right is null)
            {
                return;
            }

            var resourceType = context.ResolveExpressionType(binary.Right, state);
            if (!ImplementsAnyDisposable(resourceType, context))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, binary, MessageCode.CheckerDisposableRequiresInterface);
            }

            if (binary.Left is PhpVariableAst variable)
            {
                MarkVariableDisposable(variable, state);
            }
        }

        private static void CheckUsingBlock(
            TyhpUsingBlockAst usingBlock,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            foreach (var resource in usingBlock.Resources)
            {
                if (resource.Expression is null)
                {
                    continue;
                }

                var resourceType = context.ResolveExpressionType(resource.Expression, state);
                var ok = usingBlock.IsAsync
                    ? ImplementsAnyDisposable(resourceType, context)
                    : ImplementsDisposable(resourceType, context);
                if (!ok)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, resource, MessageCode.CheckerDisposableRequiresInterface);
                }

                if (resource.Variable is PhpVariableAst variable)
                {
                    MarkVariableDisposable(variable, state);
                }
            }

            if (usingBlock.Body is IStatement body)
            {
                var blockState = state.Split(ScopeType.CodeBlock);
                context.CheckNode(body, blockState);
            }
        }

        private static void CheckClosurePropertyAssignment(
            PhpBinaryOpAst binary,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.EnclosingObject is null
                || binary.Right is not PhpInlineFunctionAst closure
                || !IsThisPropertyTarget(binary.Left)
                || !ClosureCapturesThis(closure))
            {
                return;
            }

            context.MarkRequiresWeakReferenceCapture(closure);
        }

        private static void AnalyzeCircularDisposableReferences(
            PhpStatementBlockAst block,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            CheckerState state)
        {
            // Only scopes that own := disposables need a strategy decision.
            var disposableVars = CollectDirectDisposableVariableNames(block);
            if (disposableVars.Count == 0)
            {
                return;
            }

            var edges = new HashSet<(string From, string To)>();
            CollectDisposableReferenceEdges(block, disposableVars, edges);
            if (!HasReferenceCycle(edges))
            {
                return;
            }

            if (context.MarkRequiresDisposableTryFinally(block))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, block, MessageCode.CheckerDisposableCircularReference);
            }
        }

        /// <summary>
        /// Collects <c>:=</c> locals declared directly in this block (not nested blocks).
        /// </summary>
        private static HashSet<string> CollectDirectDisposableVariableNames(PhpStatementBlockAst block)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stmt in block.GetAllNotNull())
            {
                CollectUsingEqualNamesShallow(stmt, names);
            }

            return names;
        }

        private static void CollectUsingEqualNamesShallow(IBase2Ast node, HashSet<string> names)
        {
            if (node is PhpBinaryOpAst binary
                && IsUsingEqualOperator(binary)
                && binary.Left is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is not null)
                {
                    names.Add(name);
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child is null || child is PhpStatementBlockAst || child is PhpInlineFunctionAst)
                {
                    continue;
                }

                CollectUsingEqualNamesShallow(child, names);
            }
        }

        private static void CollectDisposableReferenceEdges(
            IBase2Ast node,
            HashSet<string> disposableVars,
            HashSet<(string From, string To)> edges)
        {
            if (node is PhpBinaryOpAst binary
                && IsPlainAssignment(binary)
                && TryGetPropertyAssignmentEndpoints(binary, out var targetObj, out var sourceVar)
                && disposableVars.Contains(targetObj)
                && disposableVars.Contains(sourceVar))
            {
                edges.Add((targetObj, sourceVar));
            }

            foreach (var child in node.AstChildren)
            {
                if (child is null || child is PhpInlineFunctionAst)
                {
                    continue;
                }

                // Recurse into nested blocks — edges inside if/loops still affect this scope's disposables.
                CollectDisposableReferenceEdges(child, disposableVars, edges);
            }
        }

        private static bool HasReferenceCycle(HashSet<(string From, string To)> edges)
        {
            if (edges.Count == 0)
            {
                return false;
            }

            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (from, to) in edges)
            {
                if (!adjacency.TryGetValue(from, out var list))
                {
                    list = [];
                    adjacency[from] = list;
                }

                list.Add(to);
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            bool Dfs(string node)
            {
                if (visiting.Contains(node))
                {
                    return true;
                }

                if (!visited.Add(node))
                {
                    return false;
                }

                visiting.Add(node);
                if (adjacency.TryGetValue(node, out var neighbors))
                {
                    foreach (var next in neighbors)
                    {
                        if (Dfs(next))
                        {
                            return true;
                        }
                    }
                }

                visiting.Remove(node);
                return false;
            }

            foreach (var node in adjacency.Keys)
            {
                if (Dfs(node))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPropertyAssignmentEndpoints(
            PhpBinaryOpAst binary,
            out string targetObject,
            out string sourceVariable)
        {
            targetObject = string.Empty;
            sourceVariable = string.Empty;

            if (binary.Left is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst targetVar,
                    Suffix: PhpInstanceMemberAccessAst
                })
            {
                return false;
            }

            if (binary.Right is not PhpVariableAst sourceVar)
            {
                return false;
            }

            var targetName = CheckerHelpers.GetVariableName(targetVar);
            var sourceName = CheckerHelpers.GetVariableName(sourceVar);
            if (targetName is null || sourceName is null)
            {
                return false;
            }

            targetObject = targetName;
            sourceVariable = sourceName;
            return true;
        }

        private static bool IsThisPropertyTarget(IExpression? left)
        {
            return left is PhpDereferenceableAst
            {
                Base: PhpVariableAst baseVar,
                Suffix: PhpInstanceMemberAccessAst
            } && CheckerHelpers.IsThisVariable(baseVar);
        }

        private static bool ClosureCapturesThis(PhpInlineFunctionAst closure)
        {
            foreach (var used in closure.LexicalVars?.GetAllNotNull() ?? [])
            {
                if (CheckerHelpers.IsThisVariable(used))
                {
                    return true;
                }
            }

            return ContainsThisReference(closure.Body ?? (IBase2Ast)closure);
        }

        private static bool ContainsThisReference(IBase2Ast node)
        {
            if (node is PhpVariableAst variable && CheckerHelpers.IsThisVariable(variable))
            {
                return true;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is null || child is PhpInlineFunctionAst)
                {
                    continue;
                }

                if (ContainsThisReference(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static void MarkVariableDisposable(PhpVariableAst variable, CheckerState state)
        {
            var name = CheckerHelpers.GetVariableName(variable);
            if (name is null)
            {
                return;
            }

            var location = state.LookupVariable(name);
            if (location is not null)
            {
                location.IsDisposable = true;
            }
        }

        private static bool ImplementsAnyDisposable(ICheckedType type, CheckerRuleContext context) =>
            ImplementsDisposable(type, context) || ImplementsAsyncDisposable(type, context);

        private static bool ImplementsDisposable(ICheckedType type, CheckerRuleContext context) =>
            CheckerHelpers.ImplementsInterface(type, "IsDisposable", context.SymbolTree, context.GlobalScope);

        private static bool ImplementsAsyncDisposable(ICheckedType type, CheckerRuleContext context) =>
            CheckerHelpers.ImplementsInterface(type, "AsyncIsDisposable", context.SymbolTree, context.GlobalScope);

        private static bool IsUsingEqualOperator(PhpBinaryOpAst binary) =>
            GetTokenType(binary.Operator) == TyhpParser.T_TYHP_USING_EQUAL
            || PhpAssignmentOperatorExtensions.FromToken(GetTokenType(binary.Operator))
                == PhpAssignmentOperator.UsingEqual;

        private static bool IsPlainAssignment(PhpBinaryOpAst binary) =>
            binary.Operator?.ValueString == "="
            || GetTokenType(binary.Operator) == TyhpParser.T_SYM_EQUAL
            || PhpAssignmentOperatorExtensions.FromToken(GetTokenType(binary.Operator))
                == PhpAssignmentOperator.Assign;

        private static int GetTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : -1;
    }
}
