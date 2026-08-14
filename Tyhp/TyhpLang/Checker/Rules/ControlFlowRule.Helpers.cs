using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class ControlFlowRule
    {
        private static void CheckYield(
            PhpYieldAst yield,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!state.IsInGeneratorContext && state.EnclosingFunction?.IsGenerator != true)
            {
                CheckerHelpers.ReportError(context, state, yield, MessageCode.CheckerYieldOutsideGenerator);
            }

            if (state.IsInsideFinally)
            {
                CheckerHelpers.ReportError(context, state, yield, MessageCode.CheckerYieldInFinally);
            }

            if (yield.KeyExpr is not null)
            {
                CheckerHelpers.CheckCompileTimeConstructsInTree(yield.KeyExpr, state, context, diagnostics);
            }

            if (yield.ValueExpr is not null)
            {
                CheckerHelpers.CheckCompileTimeConstructsInTree(yield.ValueExpr, state, context, diagnostics);
            }

            if (yield.ValueExpr is PhpUnaryOpAst { Operator.ValueString: "from" })
            {
                var operand = (yield.ValueExpr as PhpUnaryOpAst)?.Operand;
                if (operand is not null)
                {
                    var iterableType = context.ResolveExpressionType(operand, state);
                    if (!CheckerHelpers.IsIterableType(iterableType, context.SymbolTree, context.GlobalScope))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, yield, MessageCode.CheckerYieldFromNonIterable, iterableType.DisplayName);
                    }
                }
            }
        }

        private static void CheckEcho(
            PhpEchoStatementAst echo,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            foreach (var expr in echo.EchoExpressions?.GetAllNotNull() ?? [])
            {
                CheckerHelpers.CheckCompileTimeConstructsInTree(expr, state, context, diagnostics);
                var exprType = context.ResolveExpressionType(expr, state);
                if (!IsStringable(exprType, context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, echo, MessageCode.CheckerConcatNonStringable, exprType.DisplayName);
                }
            }
        }

        private static bool IsStringable(ICheckedType type, CheckerRuleContext context) =>
            CheckerHelpers.IsScalarType(type)
            || CheckerHelpers.IsBuiltInName(type, "string")
            || CheckerHelpers.ImplementsInterface(type, "Stringable", context.SymbolTree, context.GlobalScope);

        private static void CheckBoolCondition(
            IExpression? condition,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (condition is null)
            {
                return;
            }

            // Same SuppressChildTraversal gap as CheckReturn — validate compile-time constructs
            // in the condition without a full expression CheckNode walk.
            CheckerHelpers.CheckCompileTimeConstructsInTree(condition, state, context, diagnostics);

            var conditionType = context.ResolveExpressionType(condition, state);
            if (!CheckerHelpers.IsBoolType(conditionType))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, condition, MessageCode.CheckerConditionNotBool, conditionType.DisplayName);
            }
        }

        /// <summary>
        /// Type-checks a boolean condition on a disposable probe state so progressive
        /// <c>&&</c> operand narrowing cannot leak into the caller's continuation. Also
        /// runs a full <see cref="CheckerRuleContext.CheckNode"/> walk of the condition.
        /// </summary>
        private static void CheckConditionExpression(
            IExpression? condition,
            CheckerState ambient,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (condition is null)
            {
                return;
            }

            var probe = ambient.Split(ScopeType.CodeBlock);
            CheckBoolCondition(condition, probe, context, diagnostics);
            context.CheckNode(condition, probe);
        }

        private static void ApplyConditionNarrowing(
            IExpression? condition,
            CheckerState branchState,
            CheckerRuleContext context,
            bool positive)
        {
            TypeNarrowingRule.ApplyConditionNarrowing(
                condition, branchState, context, context.SymbolTree, context.GlobalScope, positive);
        }

        private static bool IsAwaitExpression(IExpression expression) =>
            expression is PhpUnaryOpAst unary
            && (string.Equals(unary.Operator?.ValueString, "await", StringComparison.OrdinalIgnoreCase)
                || unary.Operator?.ValueInt64 == Parser.TyhpParser.T_TYHP_AWAIT);

        private static IExpression UnwrapAwait(IExpression expression) =>
            expression is PhpUnaryOpAst { Operand: IExpression operand } ? operand : expression;

        private static bool IsAsyncIterableType(ICheckedType type) =>
            type is GenericCheckedType generic
                ? BaseTypeNameContains(generic.BaseType, "AsyncIterable")
                : TypeDisplayContains(type, "AsyncIterable");

        private static bool IsPromiseType(ICheckedType type, out ICheckedType? inner)
        {
            inner = null;
            if (type is not GenericCheckedType generic
                || !BaseTypeNameContains(generic.BaseType, "Promise")
                || generic.TypeArguments.Count == 0)
            {
                return false;
            }

            inner = generic.TypeArguments[0];
            return true;
        }

        private static bool BaseTypeNameContains(ICheckedType type, string fragment) =>
            type.DisplayName.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        private static bool TypeDisplayContains(ICheckedType type, string fragment) =>
            type.DisplayName.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Classifies <c>foreach (await $expr as …)</c> into the three Story 08/11 cases.
        /// </summary>
        private static AsyncForeachKind ClassifyAsyncForeach(
            ICheckedType operandType,
            CheckerRuleContext context,
            out ICheckedType valueType,
            out ICheckedType keyType)
        {
            valueType = CheckedTypes.Mixed;
            keyType = CheckedTypes.Int;

            if (IsAsyncIterableType(operandType))
            {
                ExtractAsyncIterableItemTypes(operandType, out valueType, out keyType);
                return AsyncForeachKind.AsyncIterable;
            }

            if (IsPromiseType(operandType, out var promised) && promised is not null)
            {
                if (IsAsyncIterableType(promised))
                {
                    ExtractAsyncIterableItemTypes(promised, out valueType, out keyType);
                    return AsyncForeachKind.PromiseAsyncIterable;
                }

                if (CheckerHelpers.IsIterableType(promised, context.SymbolTree, context.GlobalScope)
                    || IsBuiltInIterableName(promised))
                {
                    valueType = ExtractIterableValueType(promised);
                    keyType = ExtractIterableKeyType(promised);
                    return AsyncForeachKind.PromiseIterable;
                }
            }

            return AsyncForeachKind.None;
        }

        private static bool IsBuiltInIterableName(ICheckedType type)
        {
            var name = type.DisplayName.TrimStart('\\');
            // Strip generic args for array<…> / iterable<…>.
            var angle = name.IndexOf('<');
            if (angle >= 0)
            {
                name = name[..angle];
            }

            return string.Equals(name, "array", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "iterable", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Traversable", StringComparison.OrdinalIgnoreCase);
        }

        private static void ExtractAsyncIterableItemTypes(
            ICheckedType asyncIterableType,
            out ICheckedType valueType,
            out ICheckedType keyType)
        {
            valueType = CheckedTypes.Mixed;
            keyType = CheckedTypes.Int;

            if (asyncIterableType is not GenericCheckedType generic || generic.TypeArguments.Count == 0)
            {
                return;
            }

            // AsyncKeyValueIterator<TKey, TValue> / AsyncIterable<T> — value is always the last arg.
            valueType = generic.TypeArguments[^1];
            if (generic.TypeArguments.Count >= 2
                && BaseTypeNameContains(generic.BaseType, "AsyncKeyValue"))
            {
                keyType = generic.TypeArguments[0];
            }
        }

        private static ICheckedType ExtractIterableValueType(ICheckedType iterableType)
        {
            if (iterableType is GenericCheckedType { TypeArguments.Count: > 0 } generic)
            {
                return generic.TypeArguments[^1];
            }

            // Struct shapes erase to PHP arrays keyed by property name; values are untyped at the
            // foreach site unless a concrete StructCheckedType property map is available.
            if (TryGetStructForeachTypes(iterableType, out _, out var valueType))
            {
                return valueType;
            }

            return CheckedTypes.Mixed;
        }

        private static ICheckedType ExtractIterableKeyType(ICheckedType iterableType)
        {
            if (iterableType is GenericCheckedType { TypeArguments.Count: >= 2 } generic)
            {
                return generic.TypeArguments[0];
            }

            // `struct` / named structs / `T extends struct` iterate property names as string keys
            // (not PHP's default int keys for list-shaped arrays).
            if (TryGetStructForeachTypes(iterableType, out var keyType, out _))
            {
                return keyType;
            }

            return CheckedTypes.Int;
        }

        /// <summary>
        /// True when <paramref name="type"/> is (or is constrained to) a struct shape, which foreach
        /// exposes as string property-name keys.
        /// </summary>
        private static bool TryGetStructForeachTypes(
            ICheckedType type,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.String;
            valueType = CheckedTypes.Mixed;

            if (type is StructCheckedType structType)
            {
                if (structType.Properties.Count > 0)
                {
                    keyType = StructTypeHelper.BuildStructKeyUnion(structType);
                    valueType = structType.Properties.Count == 1
                        ? structType.Properties.Values.First().Type
                        : CheckedTypes.Mixed;
                }

                return true;
            }

            if (TypeComparer.IsBuiltInName(type, "struct"))
            {
                return true;
            }

            if (TypeComparer.TryGetObjectDeclaration(type) is { IsStruct: true })
            {
                return true;
            }

            if (type is SimpleCheckedType
                {
                    ResolvedSymbol: GenericTypeParameterSymbol { ResolvedConstraint: { } constraint }
                })
            {
                return TryGetStructForeachTypes(constraint, out keyType, out valueType);
            }

            if (type is IntersectionCheckedType intersection)
            {
                foreach (var member in intersection.Members)
                {
                    if (TryGetStructForeachTypes(member, out keyType, out valueType))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void DeclareForeachVariable(
            IExpression? variable,
            ICheckedType type,
            CheckerState loopState,
            DiagnosticBag diagnostics)
        {
            if (variable is not PhpVariableAst varAst)
            {
                return;
            }

            var name = CheckerHelpers.GetVariableName(varAst);
            if (name is null)
            {
                return;
            }

            // Foreach loop variables leak into the enclosing function scope in PHP. Reusing the same
            // name across multiple loops (or after an earlier declaration) is a reassignment, not a
            // redeclaration, so update the existing binding instead of emitting a duplicate-declaration
            // error.
            if (loopState.LookupVariable(name) is not null)
            {
                loopState.AssignVariable(name, type, diagnostics);
                return;
            }

            loopState.DeclareVariable(
                name,
                new Binder.Symbols.VariableSymbol(name),
                type,
                isAssigned: true,
                diagnostics);
        }
    }
}
