using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class TypeCompatibilityRule
    {
        /// <summary>
        /// PHP 8.5 <c>|&gt;</c>: RHS must be a single-argument callable; result typing lives in
        /// <see cref="TypeInferrer"/>. Diagnoses non-callable RHS, wrong arity, by-ref first
        /// parameters (when resolvable), and LHS/parameter type mismatches.
        /// </summary>
        private static void CheckPipe(
            PhpBinaryOpAst pipe,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (pipe.Left is null || pipe.Right is null)
            {
                return;
            }

            var leftType = context.ResolveExpressionType(pipe.Left, state);
            var rightType = context.ResolveExpressionType(pipe.Right, state);

            if (!TryValidatePipeRhsCallable(pipe.Right, rightType, state, context, diagnostics, out var facet))
            {
                return;
            }

            if (facet is not null
                && facet.ParameterTypes.Count > 0)
            {
                var paramType = facet.ParameterTypes[0];
                if (CallableGenericInference.FacetNeedsArgumentInference(facet)
                    && CallableGenericInference.TryInferFacetBindings(
                        facet, [leftType], out var bindings)
                    && bindings.Count > 0)
                {
                    paramType = TypeComparer.ResolveGenericTypeBySymbol(
                        paramType, bindings, context.SymbolTree, context.GlobalScope);
                }

                if (CallableGenericInference.ContainsUnboundGeneric(paramType))
                {
                    paramType = CheckedTypes.Mixed;
                }

                if (!context.IsAssignable(leftType, paramType, state)
                    && leftType is not UnresolvedCheckedType
                    && paramType is not UnresolvedCheckedType)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        pipe.Left,
                        MessageCode.CheckerIncompatibleArgumentType,
                        leftType.DisplayName,
                        paramType.DisplayName);
                }
            }
        }

        /// <summary>
        /// Returns <c>false</c> when a pipe-specific diagnostic was reported (or RHS is unusable).
        /// <paramref name="arityOneFacet"/> is the callable facet for a one-argument invocation when known.
        /// </summary>
        private static bool TryValidatePipeRhsCallable(
            IExpression rhs,
            ICheckedType rightType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            out CallableCheckedType? arityOneFacet)
        {
            arityOneFacet = null;

            if (CheckerHelpers.ReportMixedRequiresNarrowing(diagnostics, state, rhs, rightType))
            {
                return false;
            }

            if (!IsPipeRhsCallableType(rightType, context))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, rhs, MessageCode.CheckerPipeRhsNotCallable);
                return false;
            }

            if (TryGetPipeRhsByRefFirstParameter(rhs, state, context, out var byRefName)
                && byRefName is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, rhs, MessageCode.CheckerPipeRhsByRefParameter, byRefName);
                return false;
            }

            if (CallableArityFacetBuilder.TrySelectCallableFacet(rightType, argumentCount: 1, out var facet)
                && facet is not null)
            {
                arityOneFacet = facet;
                return true;
            }

            var facets = CallableArityFacetBuilder.GetCallableFacets(rightType);
            if (facets.Count > 0)
            {
                // Known signature that cannot accept exactly one argument.
                CheckerHelpers.ReportError(
                    diagnostics, state, rhs, MessageCode.CheckerPipeRhsInvalidArity);
                return false;
            }

            // Opaque callable / Closure / __invoke — accept without an arity facet.
            if (TryGetPipeRhsParameterInfos(rhs, state, context, out var parameters)
                && parameters is not null)
            {
                var required = CountRequiredParameters(parameters);
                if (required > 1 || parameters.Count == 0)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, rhs, MessageCode.CheckerPipeRhsInvalidArity);
                    return false;
                }

                if (parameters.Count > 0 && parameters[0].IsByReference
                    && !IsPreferRefPipeException(parameters, rhs))
                {
                    var name = parameters[0].Name.TrimStart('$');
                    CheckerHelpers.ReportError(
                        diagnostics, state, rhs, MessageCode.CheckerPipeRhsByRefParameter, name);
                    return false;
                }
            }

            return true;
        }

        private static bool IsPipeRhsCallableType(ICheckedType type, CheckerRuleContext context)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (CallableArityFacetBuilder.IsCallableFacetType(type))
            {
                return true;
            }

            if (CallableArityFacetBuilder.IsClosureTypeName(type))
            {
                return true;
            }

            if (SymbolNameTypeHelper.TryGetBehavior(type, out var behavior)
                && behavior == UtilityBehavior.FunctionName)
            {
                return true;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is not { } obj)
            {
                return false;
            }

            var member = context.SymbolTree.ResolveMember(
                "__invoke", obj, new DiagnosticBag());
            return member is ObjectMethodSymbol { IsStatic: false };
        }

        /// <summary>
        /// When the RHS is an FCC or inline function/closure, recover the callee parameter list
        /// for by-ref / arity checks that callable facets do not encode.
        /// </summary>
        private static bool TryGetPipeRhsParameterInfos(
            IExpression rhs,
            CheckerState state,
            CheckerRuleContext context,
            out IReadOnlyList<ParameterInfo>? parameters)
        {
            parameters = null;
            rhs = UnwrapParenthesizedExpression(rhs);

            if (rhs is PhpInlineFunctionAst inline
                && inline.Parameters is not null)
            {
                parameters = inline.Parameters.GetAllNotNull()
                    .Select(p => new ParameterInfo(
                        p.ValueString ?? "",
                        p.Type,
                        p.DefaultValue,
                        p.IsVariadic,
                        p.IsRef,
                        MemberModifier.None))
                    .ToList();
                return true;
            }

            if (rhs is not PhpDereferenceableAst { Suffix: PhpCallAst call } deref
                || !CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
            {
                return false;
            }

            if (deref.Base is PhpNameAst nameAst)
            {
                var function = CheckerHelpers.ResolveFreeFunction(
                    nameAst, state, context.SymbolTree, context.GlobalScope);
                if (function is null)
                {
                    return false;
                }

                parameters = function.Parameters;
                return true;
            }

            if (deref.Base is PhpDereferenceableAst chain)
            {
                string? methodName = null;
                var staticOnly = false;
                switch (chain.Suffix)
                {
                    case PhpInstanceMemberAccessAst instance:
                        methodName = GetExpressionText(instance.MemberName);
                        staticOnly = false;
                        break;
                    case PhpStaticMemberAccessAst staticAccess:
                        methodName = GetExpressionText(staticAccess.Member);
                        staticOnly = true;
                        break;
                    case PhpClassConstantAccessAst classConst:
                        methodName = GetExpressionText(classConst.Member);
                        staticOnly = true;
                        break;
                }

                if (methodName is null || chain.Base is null)
                {
                    return false;
                }

                var receiverType = context.ResolveExpressionType(chain.Base, state);
                if (!TryResolveMethod(receiverType, methodName, staticOnly, context, out var method)
                    || method is null)
                {
                    return false;
                }

                parameters = method.Parameters;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parenthesized expressions are <see cref="PhpDereferenceableExpressionAst"/> wrappers;
        /// peel them so FCC / closure inspection sees the real RHS.
        /// </summary>
        private static IExpression UnwrapParenthesizedExpression(IExpression expression)
        {
            while (expression is PhpDereferenceableExpressionAst { Expression: IExpression inner })
            {
                expression = inner;
            }

            return expression;
        }

        private static bool TryGetPipeRhsByRefFirstParameter(
            IExpression rhs,
            CheckerState state,
            CheckerRuleContext context,
            out string? parameterName)
        {
            parameterName = null;
            if (!TryGetPipeRhsParameterInfos(rhs, state, context, out var parameters)
                || parameters is null
                || parameters.Count == 0
                || !parameters[0].IsByReference
                || IsPreferRefPipeException(parameters, rhs))
            {
                return false;
            }

            parameterName = parameters[0].Name.TrimStart('$');
            return true;
        }

        /// <summary>
        /// PHP allows a tiny set of <c>@prefer-ref</c> stdlib callables in pipes; Tyhp does not
        /// model the attribute, so skip the by-ref diagnostic for the known core names.
        /// </summary>
        private static bool IsPreferRefPipeException(
            IReadOnlyList<ParameterInfo> parameters,
            IExpression rhs)
        {
            _ = parameters;
            rhs = UnwrapParenthesizedExpression(rhs);
            if (rhs is not PhpDereferenceableAst { Base: PhpNameAst nameAst })
            {
                return false;
            }

            var simple = SymbolNameTypeHelper.GetSimpleFunctionName(nameAst.ValueString);
            return simple is "extract" or "array_multisort";
        }

        private static int CountRequiredParameters(IReadOnlyList<ParameterInfo> parameters)
        {
            var count = 0;
            foreach (var param in parameters)
            {
                if (param.IsVariadic)
                {
                    break;
                }

                if (param.DefaultValue is null)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
