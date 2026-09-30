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
            // PhpYieldAst is not produced for source `yield`; keep the path for constructed
            // nodes. Child traversal is suppressed on this type, so walk key/value here.
            if (yield.KeyExpr is not null)
            {
                CheckerHelpers.CheckCompileTimeConstructsInTree(yield.KeyExpr, state, context, diagnostics);
            }

            var value = yield.ValueExpr;
            var isYieldFrom = value is PhpUnaryOpAst nested && IsYieldFromUnary(nested);
            var operand = isYieldFrom ? (value as PhpUnaryOpAst)?.Operand : value;
            CheckYield(yield, operand, isYieldFrom, walkOperand: true, state, context, diagnostics);
        }

        /// <summary>
        /// Shared yield-site checks for source unary <c>yield</c> / <c>yield from</c> and the
        /// unused <see cref="PhpYieldAst"/> shape. <paramref name="walkOperand"/> is true only
        /// when child traversal is suppressed (PhpYieldAst); unary yield leaves children to
        /// the default walk so <c>TypeCompatibilityRule</c> still sees operand operators.
        /// Generator-ness is <see cref="IsInsideGenerator"/> — not
        /// <c>EnclosingFunction.IsGenerator</c>, which leaks through closure boundaries.
        /// </summary>
        private static void CheckYield(
            IBase2Ast yieldNode,
            IExpression? operand,
            bool isYieldFrom,
            bool walkOperand,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!IsInsideGenerator(state))
            {
                CheckerHelpers.ReportError(context, state, yieldNode, MessageCode.CheckerYieldOutsideGenerator);
            }

            if (state.IsInsideFinally)
            {
                CheckerHelpers.ReportError(context, state, yieldNode, MessageCode.CheckerYieldInFinally);
            }

            if (walkOperand && operand is not null)
            {
                CheckerHelpers.CheckCompileTimeConstructsInTree(operand, state, context, diagnostics);
            }

            if (!isYieldFrom || operand is null)
            {
                GeneratorBodyInference.CollectYieldSite(yieldNode, operand, isYieldFrom: false, state, context);
                return;
            }

            var iterableType = context.ResolveExpressionType(operand, state);
            if (!IsYieldFromIterable(iterableType, context))
            {
                CheckerHelpers.ReportError(
                    context,
                    state,
                    yieldNode,
                    MessageCode.CheckerYieldFromNonIterable,
                    iterableType.DisplayName);
            }

            GeneratorBodyInference.CollectYieldSite(yieldNode, operand, isYieldFrom, state, context);
        }

        /// <summary>
        /// True for the <c>yield from</c> operator (token <c>T_YIELD_FROM</c> or text containing
        /// both <c>yield</c> and <c>from</c>). Also accepts a nested unary <c>from</c> so the
        /// unused <see cref="PhpYieldAst"/> value-expr shape still classifies as yield-from.
        /// </summary>
        private static bool IsYieldFromUnary(PhpUnaryOpAst unary)
        {
            if (unary.Operator?.ValueInt64 == Parser.TyhpParser.T_YIELD_FROM)
            {
                return true;
            }

            var op = unary.Operator?.ValueString;
            if (string.IsNullOrEmpty(op))
            {
                return false;
            }

            if (string.Equals(op, "from", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return op.Contains("yield", StringComparison.OrdinalIgnoreCase)
                && op.Contains("from", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>yield from</c> accepts arrays, <c>iterable</c>, and <c>Traversable</c> (including
        /// <c>Generator</c> / <c>Iterator</c>). Unwrap generic wrappers so
        /// <c>Generator&lt;K,V,…&gt;</c> is not rejected as a non-<c>SimpleCheckedType</c>.
        /// Every union member must be iterable; nullable wrappers are not unwrapped (PHP
        /// TypeError on null).
        /// </summary>
        private static bool IsYieldFromIterable(ICheckedType type, CheckerRuleContext context)
        {
            if (type is UnionCheckedType union)
            {
                return union.Members.Count > 0
                    && union.Members.All(member => IsYieldFromIterable(member, context));
            }

            var check = type is GenericCheckedType generic ? generic.BaseType : type;
            return CheckerHelpers.IsIterableType(check, context.SymbolTree, context.GlobalScope);
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
                if (!CheckerHelpers.IsStringableType(exprType, context.SymbolTree, context.GlobalScope))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, echo, MessageCode.CheckerConcatNonStringable, exprType.DisplayName);
                }
            }
        }

        /// <summary>
        /// <see cref="CheckerState.IsInGeneratorContext"/> alone — not a
        /// <see cref="CheckerState.EnclosingFunction"/> fallback. <c>EnclosingFunction</c>
        /// deliberately keeps pointing at the lexically enclosing named function/method through a
        /// closure boundary (needed elsewhere for generic/name resolution), so falling back to its
        /// <c>IsGenerator</c> here would wrongly attribute the *outer* callable's generator-ness to
        /// an inner closure that is not itself a generator (see <c>ClosureRule</c>, which sets
        /// <c>IsInGeneratorContext</c> from the closure's own body).
        /// </summary>
        private static bool IsInsideGenerator(CheckerState state) => state.IsInGeneratorContext;

        /// <summary>
        /// Payload type of <c>return</c> inside a generator: <c>TReturn</c> from
        /// <c>Generator&lt;TKey, TValue, TSend, TReturn&gt;</c>, or <c>mixed</c> when the
        /// declared return is bare <c>Generator</c> / <c>Iterator</c> / <c>Traversable</c> /
        /// <c>iterable</c> (no TReturn slot).
        /// </summary>
        private static bool TryGetGeneratorReturnPayloadType(
            ICheckedType declaredReturn,
            out ICheckedType payloadType)
        {
            payloadType = CheckedTypes.Mixed;
            var type = declaredReturn;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is GenericCheckedType generic)
            {
                if (IsNominalName(generic.BaseType, "Generator"))
                {
                    payloadType = generic.TypeArguments.Count >= 4
                        ? generic.TypeArguments[3]
                        : CheckedTypes.Mixed;
                    return true;
                }

                if (IsNominalName(generic.BaseType, "Iterator")
                    || IsNominalName(generic.BaseType, "Traversable")
                    || IsNominalName(generic.BaseType, "iterable")
                    || CheckerHelpers.IsBuiltInName(generic.BaseType, "iterable"))
                {
                    payloadType = CheckedTypes.Mixed;
                    return true;
                }

                return false;
            }

            if (IsNominalName(type, "Generator")
                || IsNominalName(type, "Iterator")
                || IsNominalName(type, "Traversable")
                || CheckerHelpers.IsBuiltInName(type, "iterable"))
            {
                payloadType = CheckedTypes.Mixed;
                return true;
            }

            return false;
        }

        private static bool IsNominalName(ICheckedType type, string name)
        {
            if (type is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase);
            }

            var display = type.DisplayName.TrimStart('\\');
            var angle = display.IndexOf('<');
            if (angle >= 0)
            {
                display = display[..angle];
            }

            var slash = display.LastIndexOf('\\');
            if (slash >= 0)
            {
                display = display[(slash + 1)..];
            }

            return string.Equals(display, name, StringComparison.OrdinalIgnoreCase);
        }

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
        /// <c>&&</c>/<c>||</c> operand narrowing cannot leak into the caller's continuation. Also
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
            CheckerState state,
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
                    valueType = ExtractIterableValueType(promised, state, context);
                    keyType = ExtractIterableKeyType(promised, state, context);
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

        private static ICheckedType ExtractIterableValueType(
            ICheckedType iterableType,
            CheckerState state,
            CheckerRuleContext context)
        {
            // `array<Struct>` / `iterable<Struct>` are builtins, not foreach-over-the-struct
            // (property-name keys). Check them before named/anonymous struct shapes.
            if (TryGetArrayOrIterableBuiltinArgs(iterableType, out _, out var builtinValue))
            {
                return builtinValue;
            }

            if (TryGetStructForeachTypes(iterableType, out _, out var structValue))
            {
                return structValue;
            }

            if (GenericInheritanceBindings.TryGetTraversableIterationTypes(
                    iterableType,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out _,
                    out var contractValue))
            {
                return contractValue;
            }

            // Fallback for other generics whose own parameter list is already <…, TValue>
            // (or a single-arg value-only shape). Prefer the Traversable-contract path above when
            // the type implements Iterator / IteratorAggregate with a different parameter order.
            if (iterableType is GenericCheckedType { TypeArguments.Count: > 0 } generic)
            {
                return generic.TypeArguments[^1];
            }

            return CheckedTypes.Mixed;
        }

        private static ICheckedType ExtractIterableKeyType(
            ICheckedType iterableType,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (TryGetArrayOrIterableBuiltinArgs(iterableType, out var builtinKey, out _))
            {
                return builtinKey;
            }

            if (TryGetStructForeachTypes(iterableType, out var structKey, out _))
            {
                return structKey;
            }

            if (GenericInheritanceBindings.TryGetTraversableIterationTypes(
                    iterableType,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out var contractKey,
                    out _))
            {
                return contractKey;
            }

            if (iterableType is GenericCheckedType { TypeArguments.Count: >= 2 } generic)
            {
                return generic.TypeArguments[0];
            }

            return CheckedTypes.Int;
        }

        /// <summary>
        /// <c>array&lt;V&gt;</c> / <c>array&lt;K,V&gt;</c> / <c>iterable&lt;…&gt;</c> use positional
        /// args (value last; key first when present). These are builtins, not
        /// <c>Iterator</c>-implementing classes.
        /// </summary>
        private static bool TryGetArrayOrIterableBuiltinArgs(
            ICheckedType iterableType,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Int;
            valueType = CheckedTypes.Mixed;

            if (iterableType is not GenericCheckedType { TypeArguments.Count: > 0 } generic)
            {
                return false;
            }

            var baseType = generic.BaseType;
            if (!CheckerHelpers.IsBuiltInName(baseType, "array")
                && !CheckerHelpers.IsBuiltInName(baseType, "iterable"))
            {
                return false;
            }

            valueType = generic.TypeArguments[^1];
            if (generic.TypeArguments.Count >= 2)
            {
                keyType = generic.TypeArguments[0];
            }

            return true;
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
            ICheckedType inferredType,
            string bindingKind,
            CheckerState loopState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (variable is PhpArrayPairListAst pattern)
            {
                ArrayAccessDestructureSupport.Check(
                    pattern, inferredType, pattern, loopState, context, diagnostics);
                return;
            }

            if (variable is not PhpVariableAst varAst)
            {
                return;
            }

            var name = CheckerHelpers.GetVariableName(varAst);
            if (name is null)
            {
                return;
            }

            var type = inferredType;
            if (TryGetForeachDeclaredType(varAst, loopState, context) is { } declaredType)
            {
                if (inferredType.Kind != CheckedTypeKind.Unresolved
                    && declaredType.Kind != CheckedTypeKind.Unresolved
                    && !context.IsAssignable(inferredType, declaredType, loopState))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        loopState,
                        (IBase2Ast?)varAst.Type ?? varAst,
                        MessageCode.CheckerForeachBindingTypeMismatch,
                        bindingKind,
                        inferredType.DisplayName,
                        declaredType.DisplayName);
                }

                // Annotations do not narrow: the loop variable has the declared type even when the
                // iterable is more specific (e.g. `array<int>` as `mixed $v` keeps `$v` as mixed).
                type = declaredType;
            }

            // Foreach loop variables leak into the enclosing function scope in PHP. Reusing the same
            // name across multiple loops (or after an earlier declaration) is a reassignment, not a
            // redeclaration, so update the existing binding instead of emitting a duplicate-declaration
            // error.
            if (loopState.LookupVariable(name) is not null)
            {
                loopState.AssignVariable(name, type, diagnostics);
            }
            else
            {
                loopState.DeclareVariable(
                    name,
                    new Binder.Symbols.VariableSymbol(name),
                    type,
                    isAssigned: true,
                    diagnostics);
            }

            // Record the type on the foreach wrapper and the inner `$var` node. VisitForeachVariable
            // wraps the real target in an extra PhpVariableAst; hover lands on the inner node.
            RecordExpressionTypeOnVariable(varAst, loopState, context);
        }

        private static ICheckedType? TryGetForeachDeclaredType(
            PhpVariableAst variable,
            CheckerState loopState,
            CheckerRuleContext context)
        {
            if (variable.Type is null)
            {
                return null;
            }

            context.CheckNode(variable.Type, loopState);
            return context.ResolveTypeAnnotation(variable.Type, loopState);
        }

        private static void RecordExpressionTypeOnVariable(
            PhpVariableAst variable,
            CheckerState state,
            CheckerRuleContext context)
        {
            context.ResolveExpressionType(variable, state);
            if (variable.VariableExpression is PhpVariableAst inner
                && !ReferenceEquals(inner, variable))
            {
                RecordExpressionTypeOnVariable(inner, state, context);
            }
        }
    }
}
