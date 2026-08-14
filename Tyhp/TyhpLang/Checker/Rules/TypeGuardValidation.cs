using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Shared validation for functions and methods declaring a <c>$param is Type</c> return type.
    /// </summary>
    internal static class TypeGuardValidation
    {
        public static bool IsTypeGuardReturnType(ITypeExpression? returnType) =>
            returnType is TyhpReturnTypeGuardAst;

        public static ICheckedType ResolveExpectedReturnType(
            ITypeExpression? returnTypeAst,
            CheckerState funcState,
            CheckerRuleContext context)
        {
            if (returnTypeAst is TyhpReturnTypeGuardAst)
            {
                return CheckedTypes.Bool;
            }

            return returnTypeAst is not null
                ? context.ResolveTypeAnnotation(returnTypeAst, funcState, isReturnTypePosition: true)
                : CheckedTypes.Mixed;
        }

        public static void ValidateGuardParameter(
            TyhpReturnTypeGuardAst guard,
            PhpParameterListAst? parameterList,
            IReadOnlyList<ParameterInfo> symbolParameters,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var guardVarName = guard.GuardVariable?.ValueString?.TrimStart('$');
            if (guardVarName is null || guard.TypeExpression is null)
            {
                return;
            }

            var paramExists =
                parameterList?.GetAllNotNull()
                    .Any(p => string.Equals(p.Name.TrimStart('$'), guardVarName, StringComparison.Ordinal)) == true
                || symbolParameters.Any(p => string.Equals(p.Name.TrimStart('$'), guardVarName, StringComparison.Ordinal));
            if (!paramExists)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, reportNode, MessageCode.CheckerTypeGuardInvalidReturn, guardVarName);
            }
        }

        public static void ReportMustReturnBool(
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics) =>
            CheckerHelpers.ReportError(
                diagnostics, state, reportNode, MessageCode.CheckerTypeGuardInvalidReturn);
    }
}
