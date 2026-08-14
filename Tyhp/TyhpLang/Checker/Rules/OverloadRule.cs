using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Validates tyhpdef overload signatures against their implementation declaration.
    /// </summary>
    public sealed class OverloadRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(PhpFunctionDeclAst)];

        public bool Handles(IBase2Ast node) =>
            node is PhpFunctionDeclAst function
            && function.BoundSymbol is FunctionDeclarationSymbol { Overloads.Count: > 0 };

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is PhpFunctionDeclAst function
                && function.BoundSymbol is FunctionDeclarationSymbol functionSymbol)
            {
                ValidateOverloads(function, functionSymbol, function.Parameters, function.ReturnType, state, context, diagnostics);
            }
        }

        private static void ValidateOverloads(
            IBase2Ast node,
            FunctionDeclarationSymbol implementationSymbol,
            PhpParameterListAst? parameterList,
            ITypeExpression? returnTypeAst,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var implementationParameters = implementationSymbol.Parameters;
            var implementationReturnType = returnTypeAst is not null
                ? context.ResolveTypeAnnotation(returnTypeAst, state, isReturnTypePosition: true)
                : implementationSymbol.ReturnType is not null
                    ? context.ResolveTypeAnnotation(implementationSymbol.ReturnType, state, isReturnTypePosition: true)
                    : CheckedTypes.Mixed;

            foreach (var overload in implementationSymbol.Overloads)
            {
                if (!IsOverloadCompatible(overload, implementationParameters, implementationReturnType, state, context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        node,
                        MessageCode.CheckerOverloadSignatureIncompatible,
                        overload.Name);
                }
            }
        }

        private static bool IsOverloadCompatible(
            FunctionDeclarationSymbol overload,
            IReadOnlyList<ParameterInfo> implementationParameters,
            ICheckedType implementationReturnType,
            CheckerState state,
            CheckerRuleContext context)
        {
            // An overload need only be a valid SUBSET of the implementation signature. When the
            // implementation declares optional (default-valued) or variadic parameters, an overload
            // with fewer parameters is legitimate, so accept any arity between the implementation's
            // required-parameter count and its total parameter count (unbounded above if variadic).
            if (!IsArityCompatible(overload.Parameters.Count, implementationParameters))
            {
                return false;
            }

            var hasVariadic = HasVariadicParameter(implementationParameters);

            for (var i = 0; i < overload.Parameters.Count; i++)
            {
                // Positions beyond the implementation's fixed parameters are only reachable when the
                // implementation is variadic, in which case they bind to the trailing variadic param.
                var implementationParam = i < implementationParameters.Count
                    ? implementationParameters[i]
                    : hasVariadic
                        ? implementationParameters[implementationParameters.Count - 1]
                        : null;

                if (implementationParam is null)
                {
                    return false;
                }

                var overloadParamType = overload.Parameters[i].DeclaredType is not null
                    ? context.ResolveTypeAnnotation(overload.Parameters[i].DeclaredType!, state)
                    : CheckedTypes.Mixed;
                var implementationParamType = implementationParam.DeclaredType is not null
                    ? context.ResolveTypeAnnotation(implementationParam.DeclaredType!, state)
                    : CheckedTypes.Mixed;

                if (!context.IsAssignable(overloadParamType, implementationParamType))
                {
                    return false;
                }
            }

            var overloadReturnType = overload.ReturnType is not null
                ? context.ResolveTypeAnnotation(overload.ReturnType, state, isReturnTypePosition: true)
                : CheckedTypes.Mixed;

            return context.IsAssignable(implementationReturnType, overloadReturnType);
        }

        /// <summary>
        /// Returns whether an overload declaring <paramref name="overloadParameterCount"/> parameters is a
        /// valid arity subset of the implementation. An overload may omit trailing optional/variadic
        /// parameters, so its count must be at least the implementation's required-parameter count and at
        /// most its total parameter count (unbounded above when the implementation is variadic).
        /// </summary>
        public static bool IsArityCompatible(
            int overloadParameterCount,
            IReadOnlyList<ParameterInfo> implementationParameters)
        {
            var requiredCount = 0;
            var hasVariadic = false;
            foreach (var implParam in implementationParameters)
            {
                if (implParam.IsVariadic)
                {
                    hasVariadic = true;
                }
                else if (implParam.DefaultValue is null)
                {
                    requiredCount++;
                }
            }

            if (overloadParameterCount < requiredCount)
            {
                return false;
            }

            return hasVariadic || overloadParameterCount <= implementationParameters.Count;
        }

        private static bool HasVariadicParameter(IReadOnlyList<ParameterInfo> parameters)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.IsVariadic)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
