using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class ControlFlowRule
    {
        private static void CheckForeach(
            PhpLoopAst loop,
            CheckerState loopState,
            CheckerState outerState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var iterableExpr = loop.Condition;
            if (iterableExpr is null)
            {
                return;
            }

            var isAwait = IsAwaitExpression(iterableExpr);
            if (isAwait && !outerState.IsInAsyncContext && !outerState.IsTopLevelAwaitableScope())
            {
                CheckerHelpers.ReportError(
                    diagnostics, outerState, loop, MessageCode.CheckerAwaitOutsideAsync);
            }

            var exprForType = isAwait ? UnwrapAwait(iterableExpr) : iterableExpr;
            var iterableType = context.ResolveExpressionType(exprForType, loopState);
            if (!isAwait && IsAsyncIterableType(iterableType))
            {
                var typeArg = iterableType is GenericCheckedType generic
                    && generic.TypeArguments.Count > 0
                    ? generic.TypeArguments[0].DisplayName
                    : "?";
                CheckerHelpers.ReportError(
                    diagnostics,
                    outerState,
                    loop,
                    MessageCode.CheckerAsyncIterableMissingAwait,
                    typeArg);
            }

            ICheckedType valueType;
            ICheckedType keyType;
            if (isAwait)
            {
                var kind = ClassifyAsyncForeach(iterableType, context, out valueType, out keyType);
                if (kind == AsyncForeachKind.None)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        outerState,
                        loop,
                        MessageCode.CheckerAwaitNonAsyncIterable,
                        iterableType.DisplayName);
                    valueType = CheckedTypes.Mixed;
                    keyType = CheckedTypes.Int;
                }
                else
                {
                    context.MarkAsyncForeachKind(loop, kind);
                }
            }
            else
            {
                if (CheckerHelpers.ReportMixedRequiresNarrowing(
                        diagnostics, outerState, exprForType, iterableType))
                {
                    valueType = CheckedTypes.Mixed;
                    keyType = CheckedTypes.Int;
                }
                else
                {
                    valueType = ExtractIterableValueType(iterableType);
                    keyType = ExtractIterableKeyType(iterableType);
                }
            }

            DeclareForeachVariable(loop.ValueVariable, valueType, loopState, context, diagnostics);
            DeclareForeachVariable(loop.KeyVariable, keyType, loopState, context, diagnostics);
        }

        private static void CheckTryCatch(
            PhpTryCatchAst tryCatch,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var beforeTry = state.SnapShot();
            var tryState = beforeTry.Split(ScopeType.CodeBlock);
            context.CheckStatementBlock(tryCatch.TryBlock, tryState);
            state.Merge(tryState);

            var mergedReturned = tryState.HasReturnedOnAllPaths;

            foreach (var catchClause in tryCatch.CatchClauses?.GetAllNotNull() ?? [])
            {
                ValidateCatchTypes(catchClause, state, context, diagnostics);

                var catchState = beforeTry.Split(ScopeType.CodeBlock);
                if (catchClause.Variable is not null
                    && CheckerHelpers.GetVariableName(catchClause.Variable) is { } key)
                {
                    var catchType = ResolveCatchType(catchClause, catchState, context);
                    catchState.Variables[key] =
                        VariableState.ForParameter(
                            new Binder.Symbols.VariableSymbol(key),
                            catchType,
                            isReference: false);
                    context.ResolveExpressionType(catchClause.Variable, catchState);
                }

                context.CheckStatementBlock(catchClause.Body, catchState);
                state.Merge(catchState);
                mergedReturned = mergedReturned && catchState.HasReturnedOnAllPaths;
            }

            if (tryCatch.FinallyBlock is not null)
            {
                var finallyState = state.Split(ScopeType.CodeBlock);
                finallyState.IsInsideFinally = true;
                context.CheckStatementBlock(tryCatch.FinallyBlock, finallyState);
                state.Merge(finallyState);
            }

            state.HasReturnedOnAllPaths = mergedReturned;
        }

        private static void ValidateCatchTypes(
            PhpCatchClauseAst catchClause,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var types = catchClause.ExceptionTypes?.GetAllNotNull().ToList() ?? [];
            if (catchClause.Body is null || !catchClause.Body.GetAllNotNull().Any())
            {
                CheckerHelpers.ReportWarning(diagnostics, state, catchClause, MessageCode.CheckerEmptyCatch);
            }

            foreach (var typeName in types)
            {
                // PhpTryCatchAst suppresses child traversal, so catch-clause exception types are
                // never CheckNode'd — still count import usage for TYHP4130.
                context.MarkImportNames(typeName, state);

                // Catch types are class/interface names, not value expressions — expression
                // inference yields `unresolved` for bare `\Throwable` (same pitfall as
                // `instanceof`). Resolve as types so `$e` is typed and TYHP4040 is accurate.
                var resolved = CheckerHelpers.ResolveInstanceofTargetType(
                    typeName, state, context, context.SymbolTree, context.GlobalScope);
                if (resolved is IntersectionCheckedType)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, catchClause, MessageCode.CheckerCatchNoIntersection);
                }

                if (CheckerHelpers.IsScalarOrStructOrEnum(resolved))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, catchClause, MessageCode.CheckerCatchNoScalar, resolved.DisplayName);
                }

                if (!CheckerHelpers.IsThrowableType(resolved, context.SymbolTree, context.GlobalScope))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, catchClause, MessageCode.CheckerCatchNotThrowable, resolved.DisplayName);
                }
            }
        }

        private static ICheckedType ResolveCatchType(
            PhpCatchClauseAst catchClause,
            CheckerState state,
            CheckerRuleContext context)
        {
            var types = catchClause.ExceptionTypes?.GetAllNotNull().ToList() ?? [];
            if (types.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            ICheckedType? result = null;
            foreach (var typeName in types)
            {
                var resolved = CheckerHelpers.ResolveInstanceofTargetType(
                    typeName, state, context, context.SymbolTree, context.GlobalScope);
                result = result is null ? resolved : CheckedTypes.UnionTypes(result, resolved);
            }

            return result ?? CheckedTypes.Unresolved;
        }
    }
}
