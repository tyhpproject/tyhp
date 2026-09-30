using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Shared validation for functions and methods declaring a <c>$param is Type</c> or
    /// <c>$array[$key] is Type</c> return type.
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
            if (guard.TypeExpression is null)
            {
                return;
            }

            var names = CollectGuardSubjectParameterNames(guard);
            if (names.Count == 0)
            {
                return;
            }

            foreach (var guardVarName in names)
            {
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
        }

        public static void ReportMustReturnBool(
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics) =>
            CheckerHelpers.ReportError(
                diagnostics, state, reportNode, MessageCode.CheckerTypeGuardInvalidReturn);

        /// <summary>
        /// Parameter names named by the guard subject: the bare <c>$param</c>, or both the
        /// array base and (when it is a variable) the index of <c>$array[$key]</c>.
        /// Constant indices are not parameters.
        /// </summary>
        internal static List<string> CollectGuardSubjectParameterNames(TyhpReturnTypeGuardAst guard)
        {
            var names = new List<string>();
            switch (guard.GuardSubject)
            {
                case PhpDereferenceableAst
                {
                    Base: PhpVariableAst arrayVar,
                    Suffix: PhpArrayAccessAst { IndexExpression: { } index }
                }:
                    AddVariableName(names, arrayVar);
                    if (index is PhpVariableAst indexVar)
                    {
                        AddVariableName(names, indexVar);
                    }

                    break;
                case PhpVariableAst variable:
                    AddVariableName(names, variable);
                    break;
                default:
                {
                    var legacy = guard.GuardVariable?.ValueString?.TrimStart('$');
                    if (!string.IsNullOrEmpty(legacy))
                    {
                        names.Add(legacy);
                    }

                    break;
                }
            }

            return names;
        }

        private static void AddVariableName(List<string> names, PhpVariableAst variable)
        {
            var name = CheckerHelpers.GetVariableName(variable);
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }
    }
}
