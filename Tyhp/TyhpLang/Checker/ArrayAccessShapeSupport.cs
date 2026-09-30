using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 21.6 Phase 6d: <c>ArrayAccessShape&lt;TStruct&gt;</c> call-site keys, no-append,
    /// and per-key <c>offsetGet</c>/<c>offsetSet</c> exhaustiveness.
    /// </summary>
    internal static class ArrayAccessShapeSupport
    {
        internal const string FullyQualifiedName = "Tyhp\\Contracts\\ArrayAccessShape";

        public static bool IsArrayAccessShapeNominal(ICheckedType type)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (current is GenericCheckedType generic)
            {
                current = generic.BaseType;
            }

            if (current is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return IsArrayAccessShapeFqn(obj.FullyQualifiedName);
            }

            var display = current.DisplayName.TrimStart('\\');
            var angle = display.IndexOf('<');
            if (angle >= 0)
            {
                display = display[..angle];
            }

            return IsArrayAccessShapeFqn(display);
        }

        public static bool TryGetStructType(
            ICheckedType receiverType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out ICheckedType structType) =>
            GenericInheritanceBindings.TryGetArrayAccessShapeStruct(
                receiverType, state, symbolTree, globalScope, resolveType, out structType);

        public static bool TryGetFiniteLiteralInhabitants(ICheckedType type, out List<ICheckedType> keys)
        {
            keys = [];
            foreach (var member in Flatten(type))
            {
                if (member is LiteralCheckedType { Value: string or int or long })
                {
                    keys.Add(member);
                    continue;
                }

                keys = [];
                return false;
            }

            return keys.Count > 0;
        }

        public static bool IsWideOffsetType(ICheckedType type)
        {
            foreach (var member in Flatten(type))
            {
                if (member is LiteralCheckedType)
                {
                    continue;
                }

                if (TypeComparer.IsMixedType(member)
                    || CheckerHelpers.IsBuiltInName(member, "string")
                    || CheckerHelpers.IsBuiltInName(member, "int"))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsOffsetGetOrSet(string? name, out bool isGet)
        {
            isGet = false;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (string.Equals(name, "offsetGet", StringComparison.OrdinalIgnoreCase))
            {
                isGet = true;
                return true;
            }

            return string.Equals(name, "offsetSet", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// When the method is <c>offsetGet</c>/<c>offsetSet</c> on an <c>ArrayAccessShape</c>
        /// whose keys are a finite literal union, type-check the body once per key and report
        /// unhandled keys. Returns <see langword="true"/> when that walk replaced the envelope
        /// body check.
        /// </summary>
        public static bool TryCheckPerKeyBodies(
            PhpMethodDeclAst method,
            ObjectMethodSymbol methodSymbol,
            CheckerState methodState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (method.Body is null
                || !IsOffsetGetOrSet(methodSymbol.Name, out var isGet)
                || methodState.EnclosingObject is not { } owner)
            {
                return false;
            }

            var receiver = methodState.EnclosingObjectType ?? CheckedTypes.FromSymbol(owner);
            if (!TryGetStructType(
                    receiver,
                    methodState,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out var structType))
            {
                return false;
            }

            var keyUnion = MagicUtilityTypeResolver.IndexKeysOf(
                structType,
                methodState,
                context.SymbolTree,
                context.GlobalScope,
                context.ResolveTypeAnnotation);
            if (!TryGetFiniteLiteralInhabitants(keyUnion, out var keys))
            {
                return false;
            }

            var parameters = method.Parameters?.GetAllNotNull().ToList() ?? [];
            var offsetName = parameters.Count > 0 ? parameters[0].Name.TrimStart('$') : "offset";
            var valueName = parameters.Count > 1 ? parameters[1].Name.TrimStart('$') : "value";

            var template = methodState.Fork();
            var returnedOnAll = true;
            foreach (var key in keys)
            {
                context.ClearCachedTypesUnder(method.Body);
                var keyState = template.Fork();
                AssumeParameterType(keyState, offsetName, key);
                var fieldType = MagicUtilityTypeResolver.IndexValueTypeOf(
                    structType,
                    key,
                    keyState,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation);
                if (isGet)
                {
                    keyState.ExpectedReturnType = fieldType;
                    keyState.TrackArrayAccessShapeOffsetGetCoverage = true;
                }
                else
                {
                    AssumeParameterType(keyState, valueName, fieldType);
                    keyState.ArrayAccessShapeValueParameterName = valueName;
                    keyState.TrackArrayAccessShapeOffsetSetCoverage = true;
                }

                keyState.HasArrayAccessShapeCoverage = false;
                keyState.HasReturnedOnAllPaths = false;
                context.CheckStatementBlock(method.Body, keyState);
                returnedOnAll = returnedOnAll && keyState.HasReturnedOnAllPaths;
                if (!keyState.HasArrayAccessShapeCoverage)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        methodState,
                        method,
                        MessageCode.CheckerArrayAccessShapeUnhandledKey,
                        key.DisplayName,
                        methodSymbol.Name);
                }
            }

            methodState.HasReturnedOnAllPaths = returnedOnAll;
            return true;
        }

        public static bool IsTrackingPerKeyCoverage(CheckerState state) =>
            state.TrackArrayAccessShapeOffsetGetCoverage || state.TrackArrayAccessShapeOffsetSetCoverage;

        /// <summary>
        /// When <paramref name="offset"/> is assumed a key literal, <c>$offset === 'host'</c>
        /// is proven true or false so dead <c>if</c> arms are not type-checked against the
        /// wrong field type.
        /// </summary>
        public static bool TryProveCondition(
            IExpression? condition,
            CheckerState state,
            CheckerRuleContext context,
            out bool value)
        {
            value = false;
            if (!IsTrackingPerKeyCoverage(state)
                || condition is not PhpBinaryOpAst binary)
            {
                return false;
            }

            var op = binary.Operator?.ValueString;
            var negated = op is "!==" or "!=";
            if (op is not ("===" or "==" or "!==" or "!="))
            {
                return false;
            }

            if (binary.Left is not IExpression leftExpr || binary.Right is not IExpression rightExpr)
            {
                return false;
            }

            var left = context.ResolveExpressionType(leftExpr, state);
            var right = context.ResolveExpressionType(rightExpr, state);
            if (left is not LiteralCheckedType leftLiteral
                || right is not LiteralCheckedType rightLiteral)
            {
                return false;
            }

            var equal = LiteralsEqual(leftLiteral.Value, rightLiteral.Value);
            value = negated ? !equal : equal;
            return true;
        }

        public static bool LiteralsEqual(object? left, object? right)
        {
            if (Equals(left, right))
            {
                return true;
            }

            return TryToInt64(left, out var leftInt) && TryToInt64(right, out var rightInt) && leftInt == rightInt;
        }

        private static bool TryToInt64(object? value, out long result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        public static bool ArmMatchesLiteralSubject(
            ICheckedType subjectType,
            IReadOnlyList<ICheckedType> armConditionTypes)
        {
            if (subjectType is not LiteralCheckedType subjectLiteral)
            {
                return false;
            }

            foreach (var conditionType in armConditionTypes)
            {
                if (conditionType is LiteralCheckedType conditionLiteral
                    && LiteralsEqual(subjectLiteral.Value, conditionLiteral.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssumeParameterType(CheckerState state, string name, ICheckedType type)
        {
            if (!state.Variables.TryGetValue(name, out var existing))
            {
                state.Variables[name] = VariableState.ForParameter(
                    new VariableSymbol(name) { IsParameter = true },
                    type,
                    isReference: false);
                return;
            }

            var clone = existing.Clone();
            clone.DeclaredType = type;
            clone.NarrowedType = type;
            clone.IsPossiblyNull = type.IsNullable;
            state.Variables[name] = clone;
        }

        private static bool IsArrayAccessShapeFqn(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.TrimStart('\\');
            return trimmed.Equals(FullyQualifiedName, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Contracts\\ArrayAccessShape", StringComparison.OrdinalIgnoreCase);
        }

        private static List<ICheckedType> Flatten(ICheckedType type)
        {
            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (current is UnionCheckedType union)
            {
                var members = new List<ICheckedType>();
                foreach (var member in union.Members)
                {
                    members.AddRange(Flatten(member));
                }

                return members;
            }

            return [current];
        }
    }
}
