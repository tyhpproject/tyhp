using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker.Rules
{
    internal static class ClosureParameterInference
    {
        public static void InferAndRegisterParameters(
            PhpInlineFunctionAst closure,
            CheckerState closureState,
            CheckerState outerState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var expectedType = closureState.ExpectedClosureType ?? outerState.ExpectedClosureType;
            CallableCheckedType? expectedCallable = null;
            var parameters = closure.Parameters?.GetAllNotNull().ToList() ?? [];
            if (expectedType is not null)
            {
                CallableArityFacetBuilder.TrySelectCallableFacetForClosure(
                    expectedType,
                    parameters.Count,
                    out expectedCallable);
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                ICheckedType paramType;

                if (param.Type is not null)
                {
                    paramType = context.ResolveTypeAnnotation(param.Type, closureState);
                    // Closure parameter types are never CheckNode'd — still count import usage
                    // for TYHP4130 (parity with function/method parameter types).
                    context.MarkImportNames(param.Type, outerState);
                }
                else if (expectedCallable is not null && i < expectedCallable.ParameterTypes.Count)
                {
                    paramType = expectedCallable.ParameterTypes[i];
                }
                else if (param.DefaultValue is not null)
                {
                    // Extra trailing params with defaults (beyond the expected facet arity) are
                    // allowed without an annotation — they are the optional-arity expansion.
                    paramType = CheckedTypes.Unresolved;
                }
                else
                {
                    CheckerHelpers.ReportError(
                        diagnostics, outerState, param, MessageCode.CheckerClosureParameterTypeRequired, param.Name);
                    paramType = CheckedTypes.Unresolved;
                }

                if (param.Type is not null && expectedCallable is not null
                    && i < expectedCallable.ParameterTypes.Count
                    && !context.IsAssignable(paramType, expectedCallable.ParameterTypes[i])
                    && !context.IsAssignable(expectedCallable.ParameterTypes[i], paramType))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, outerState, param, MessageCode.CheckerTypeMismatch,
                        paramType.DisplayName, expectedCallable.ParameterTypes[i].DisplayName);
                }

                var variable = new VariableSymbol(param.Name) { IsParameter = true, IsRef = param.IsRef };
                var varState = VariableState.ForParameter(variable, paramType, param.IsRef);
                varState.IsInferred = param.Type is null;
                closureState.Variables[param.Name.TrimStart('$')] = varState;
            }

            // Contextual typing fills omitted annotations for checking; also hand the recovered
            // shape to the emitter so PHP typehints are not dropped when Tyhp source left them out.
            if (expectedCallable is not null)
            {
                RecordInferredSignatureForEmit(closure, parameters, expectedCallable, context);
            }

            if (expectedCallable is not null && parameters.Count > expectedCallable.ParameterTypes.Count)
            {
                var extras = parameters.Skip(expectedCallable.ParameterTypes.Count);
                foreach (var extra in extras)
                {
                    // Required extras beyond the expected arity still need a type; defaulted extras
                    // are covered by optional-arity facets and are not an error.
                    if (extra.DefaultValue is null && extra.Type is null)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, outerState, extra, MessageCode.CheckerClosureParameterTypeRequired, extra.Name);
                    }
                }
            }
        }

        private static void RecordInferredSignatureForEmit(
            PhpInlineFunctionAst closure,
            IReadOnlyList<PhpParameterAst> parameters,
            CallableCheckedType expectedCallable,
            CheckerRuleContext context)
        {
            ICheckedType? inferredReturn = closure.ReturnType is null
                ? expectedCallable.ReturnType
                : null;

            var inferredParams = new ICheckedType?[parameters.Count];
            var anyInferredParam = false;
            for (var i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].Type is null && i < expectedCallable.ParameterTypes.Count)
                {
                    inferredParams[i] = expectedCallable.ParameterTypes[i];
                    anyInferredParam = true;
                }
            }

            if (inferredReturn is null && !anyInferredParam)
            {
                return;
            }

            context.RecordInferredClosureSignature(
                closure,
                new InferredClosureSignature(inferredReturn, inferredParams));
        }

        public static void SetExpectedClosureTypeFromArgument(
            ICheckedType parameterType,
            CheckerState callState)
        {
            if (CallableArityFacetBuilder.IsCallableFacetType(parameterType)
                || TryAsCallable(parameterType) is not null)
            {
                callState.ExpectedClosureType = parameterType;
                return;
            }

            // Story 16 Phase 1/2: PropertyPath / Expression contextual-type the inline fn as callable.
            if (PropertyPathSupport.TryMapToCallable(parameterType, out var mapped)
                || ExpressionTreeSupport.TryMapToCallable(parameterType, out mapped))
            {
                callState.ExpectedClosureType = mapped;
            }
        }

        public static void SetExpectedClosureTypeFromAnnotation(
            ICheckedType variableType,
            CheckerState state)
        {
            if (CallableArityFacetBuilder.IsCallableFacetType(variableType)
                || TryAsCallable(variableType) is not null)
            {
                state.ExpectedClosureType = variableType;
                return;
            }

            if (PropertyPathSupport.TryMapToCallable(variableType, out var mapped)
                || ExpressionTreeSupport.TryMapToCallable(variableType, out mapped))
            {
                state.ExpectedClosureType = mapped;
            }
        }

        private static CallableCheckedType? TryAsCallable(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is CallableCheckedType direct)
            {
                return direct;
            }

            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                && CheckerHelpers.IsBuiltInName(generic.BaseType, "callable"))
            {
                return new CallableCheckedType(
                    generic.TypeArguments.Take(generic.TypeArguments.Count - 1).ToList(),
                    generic.TypeArguments[^1]);
            }

            if (CheckerHelpers.IsBuiltInName(type, "callable"))
            {
                return new CallableCheckedType([], CheckedTypes.Mixed);
            }

            return null;
        }
    }
}
