using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 21.7: PHP <c>list()</c> / <c>[]</c> destructure on array, string, and
    /// <c>ArrayAccess</c> (homogeneous <c>TValue</c> or <c>ArrayAccessShape</c> per-key).
    /// Each bound key is an <c>offsetGet</c> read — same types as <c>$obj[$k]</c>.
    /// </summary>
    internal static class ArrayAccessDestructureSupport
    {
        public static bool TryGetPattern(IExpression? left, out PhpArrayPairListAst pattern)
        {
            pattern = null!;
            if (left is PhpArrayPairListAst pairList)
            {
                pattern = pairList;
                return true;
            }

            return false;
        }

        public static void Check(
            PhpArrayPairListAst pattern,
            ICheckedType sourceType,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (CheckerHelpers.ReportUnresolvedReceiver(
                    diagnostics, state, reportNode, sourceType))
            {
                BindPattern(pattern, CheckedTypes.Unresolved, state, context, diagnostics);
                return;
            }

            if (!IsDestructurableSource(sourceType, state, context))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerDestructuringNonArray,
                    sourceType.DisplayName);
                return;
            }

            BindPattern(pattern, sourceType, state, context, diagnostics);
        }

        public static bool IsDestructurableSource(
            ICheckedType type,
            CheckerState state,
            CheckerRuleContext context)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (current is UnionCheckedType union)
            {
                return union.Members.Count > 0
                    && union.Members.All(member => IsDestructurableSource(member, state, context));
            }

            if (IsArrayOrStringType(current))
            {
                return true;
            }

            return GenericInheritanceBindings.TryGetArrayAccessShapeStruct(
                    current,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out _)
                || GenericInheritanceBindings.TryGetArrayAccessTypes(
                    current,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out _,
                    out _);
        }

        private static bool IsArrayOrStringType(ICheckedType type)
        {
            var current = type is GenericCheckedType generic ? generic.BaseType : type;
            return CheckerHelpers.IsBuiltInName(current, "array")
                || CheckerHelpers.IsBuiltInName(current, "string")
                || (type is LiteralCheckedType { Value: string });
        }

        private static void BindPattern(
            PhpArrayPairListAst pattern,
            ICheckedType sourceType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var pairs = pattern.GetAllNotNull().ToList();
            while (pairs.Count > 0 && pairs[^1].IsSkippedSlot)
            {
                pairs.RemoveAt(pairs.Count - 1);
            }

            var nextPositional = 0;
            foreach (var pair in pairs)
            {
                if (pair.IsSkippedSlot)
                {
                    nextPositional++;
                    continue;
                }

                if (pair.IsExpansion)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, pair, MessageCode.CheckerDestructuringSpread);
                    continue;
                }

                ICheckedType keyType;
                if (pair.KeyExpr is IExpression keyExpr)
                {
                    keyType = context.ResolveExpressionType(keyExpr, state);
                }
                else
                {
                    keyType = IntKeyType(nextPositional);
                    nextPositional++;
                }

                CheckOffsetKey(pair, sourceType, keyType, state, context, diagnostics);
                var elementType = context.InferIndexValueType(sourceType, keyType, state);
                BindTarget(pair.ValueExpr, elementType, state, context, diagnostics);
            }
        }

        private static void BindTarget(
            IExpression? target,
            ICheckedType elementType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            target = UnwrapReference(target);

            if (target is PhpArrayPairListAst nested)
            {
                Check(nested, elementType, nested, state, context, diagnostics);
                return;
            }

            if (target is PhpVariableAst variable
                && CheckerHelpers.GetVariableName(variable) is { } name)
            {
                TypeNarrowingRule.ResetNarrowingOnAssignment(name, state);
                state.AssignVariable(name, elementType, diagnostics);
            }
        }

        private static IExpression? UnwrapReference(IExpression? expression)
        {
            if (expression is PhpUnaryOpAst { Operator.ValueString: "&", Operand: IExpression operand })
            {
                return operand;
            }

            return expression;
        }

        private static ICheckedType IntKeyType(int value) =>
            new LiteralCheckedType(value, new SimpleCheckedType(new BuiltInTypeSymbol("int")));

        private static void CheckOffsetKey(
            IBase2Ast reportNode,
            ICheckedType receiverType,
            ICheckedType indexType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (indexType is UnresolvedCheckedType)
            {
                return;
            }

            if (ArrayAccessShapeSupport.TryGetStructType(
                    receiverType,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out var shapeStruct))
            {
                var keys = MagicUtilityTypeResolver.IndexKeysOf(
                    shapeStruct,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation);
                var finiteKeys = ArrayAccessShapeSupport.TryGetFiniteLiteralInhabitants(keys, out _);

                if (finiteKeys
                    && (CheckerHelpers.IsUnnarrowedMixed(indexType)
                        || ArrayAccessShapeSupport.IsWideOffsetType(indexType))
                    && !context.IsAssignable(indexType, keys, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerArrayAccessShapeWideKey,
                        indexType.DisplayName);
                    return;
                }

                if (CheckerHelpers.ReportMixedRequiresNarrowing(
                        diagnostics, state, reportNode, indexType))
                {
                    return;
                }

                if (TypeComparer.IsMixedType(keys)
                    || context.IsAssignable(indexType, keys, state))
                {
                    return;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerTypeMismatch,
                    indexType.DisplayName,
                    keys.DisplayName);
                return;
            }

            if (!GenericInheritanceBindings.TryGetArrayAccessTypes(
                    receiverType,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out var keyType,
                    out _))
            {
                keyType = ArrayKeyType(receiverType);
            }

            if (GenericInheritanceBindings.IsIllegalArrayAccessKeyType(keyType))
            {
                if (!GenericInheritanceBindings.IsDirectArrayAccessInstantiation(receiverType))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerArrayAccessKeyNotOffset,
                        keyType.DisplayName);
                }

                return;
            }

            if (TypeComparer.IsMixedType(keyType)
                || context.IsAssignable(indexType, keyType, state))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                reportNode,
                MessageCode.CheckerTypeMismatch,
                indexType.DisplayName,
                keyType.DisplayName);
        }

        private static ICheckedType ArrayKeyType(ICheckedType receiverType)
        {
            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is GenericCheckedType generic && IsArrayBaseName(generic.BaseType))
            {
                if (generic.TypeArguments.Count >= 2)
                {
                    return generic.TypeArguments[0];
                }

                if (generic.TypeArguments.Count == 1)
                {
                    return CheckedTypes.PhpArrayKey;
                }
            }

            if (IsArrayBaseName(type))
            {
                return CheckedTypes.PhpArrayKey;
            }

            return CheckedTypes.Int;
        }

        private static bool IsArrayBaseName(ICheckedType baseType)
        {
            var name = baseType.DisplayName;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lastSegment = name.TrimStart('?').TrimStart('\\');
            var angle = lastSegment.IndexOf('<');
            if (angle >= 0)
            {
                lastSegment = lastSegment[..angle];
            }

            return string.Equals(lastSegment, "array", StringComparison.OrdinalIgnoreCase);
        }
    }
}
