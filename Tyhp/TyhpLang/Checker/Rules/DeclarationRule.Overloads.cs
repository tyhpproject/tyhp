using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Declaration checks for <c>.tyhp</c> overload sets: each bodyless signature gets the same
    /// type checks as a declaration (without a body), and each signature must be compatible with
    /// the declaration of the set.
    /// </summary>
    public sealed partial class DeclarationRule
    {
        /// <summary>
        /// Checks a bodyless method overload signature: generic constraints, parameter and return
        /// types. The signature has no body, so nothing else applies.
        /// </summary>
        private void CheckMethodOverloadSignature(
            PhpMethodDeclAst method,
            ObjectMethodSymbol overload,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var signatureState = CreateOverloadState(state, overload.IsStatic
                ? ScopeType.StaticMethodDeclaration
                : ScopeType.InstanceMethodDeclaration, overload.GenericParameters);
            signatureState.EnclosingCallable = overload;
            signatureState.ObjectGenerics = state.ObjectGenerics;
            if (state.ObjectGenerics.Count > 0)
            {
                GenericConstraintResolver.ResolveAll(state.ObjectGenerics, signatureState, context);
            }

            GenericConstraintResolver.ResolveAll(overload.GenericParameters, signatureState, context);
            if (method.ReturnType is not null)
            {
                context.MarkImportNames(method.ReturnType, state);
                ExternTypeUse.ReportIfTypeAnnotation(
                    method.ReturnType, signatureState, context, diagnostics, isReturnTypePosition: true);
            }

            RegisterParameters(method.Parameters, overload.Parameters, signatureState, state, context, diagnostics);
        }

        private static CheckerState CreateOverloadState(
            CheckerState state,
            ScopeType scopeType,
            List<GenericTypeParameterSymbol> generics)
        {
            var signatureState = state.Split(scopeType);
            signatureState.FunctionGenerics = generics;
            return signatureState;
        }

        /// <summary>
        /// Every overload signature of a method set must be compatible with the set's declaration.
        /// </summary>
        private static void ValidateMethodOverloadSet(
            ObjectMethodSymbol implementation,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (implementation.Overloads.Count == 0)
            {
                return;
            }

            var scopeType = implementation.IsStatic
                ? ScopeType.StaticMethodDeclaration
                : ScopeType.InstanceMethodDeclaration;
            var implementationState = CreateOverloadState(state, scopeType, implementation.GenericParameters);
            implementationState.ObjectGenerics = state.ObjectGenerics;

            foreach (var overload in implementation.Overloads)
            {
                var overloadState = CreateOverloadState(state, scopeType, overload.GenericParameters);
                overloadState.ObjectGenerics = state.ObjectGenerics;
                if (!SourceOverloadCompatibility.IsCompatible(
                        overload.Parameters,
                        overload.ReturnType,
                        overload.GenericParameters,
                        overloadState,
                        implementation.Parameters,
                        implementation.ReturnType,
                        implementation.GenericParameters,
                        implementationState,
                        context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        overload.DeclaringAstNode ?? (IBase2Ast)implementation.DeclaringAstNode!,
                        MessageCode.CheckerOverloadSignatureIncompatible,
                        overload.Name);
                }
            }
        }

        /// <summary>
        /// Every overload signature of a function set must be compatible with the implementation.
        /// </summary>
        private static void ValidateFunctionOverloadSet(
            FunctionDeclarationSymbol implementation,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var implementationState = CreateOverloadState(
                state, ScopeType.FunctionDeclaration, implementation.GenericParameters);
            foreach (var overload in implementation.Overloads)
            {
                var overloadState = CreateOverloadState(
                    state, ScopeType.FunctionDeclaration, overload.GenericParameters);
                if (!SourceOverloadCompatibility.IsCompatible(
                        overload.Parameters,
                        overload.ReturnType,
                        overload.GenericParameters,
                        overloadState,
                        implementation.Parameters,
                        implementation.ReturnType,
                        implementation.GenericParameters,
                        implementationState,
                        context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        overload.DeclaringAstNode ?? (IBase2Ast)implementation.DeclaringAstNode!,
                        MessageCode.CheckerOverloadSignatureIncompatible,
                        overload.Name);
                }
            }
        }
    }

    /// <summary>
    /// Compatibility of an overload signature with the declaration of its set: the overload's
    /// parameters must be assignable to the declaration's, and the declaration's return type and
    /// the overload's must be assignable one way or the other. Generic parameters of either
    /// signature are erased, so they never make a signature incompatible on their own.
    /// </summary>
    internal static class SourceOverloadCompatibility
    {
        public static bool IsCompatible(
            IReadOnlyList<ParameterInfo> overloadParameters,
            ITypeExpression? overloadReturn,
            IReadOnlyList<GenericTypeParameterSymbol> overloadGenerics,
            CheckerState overloadState,
            IReadOnlyList<ParameterInfo> implementationParameters,
            ITypeExpression? implementationReturn,
            IReadOnlyList<GenericTypeParameterSymbol> implementationGenerics,
            CheckerState implementationState,
            CheckerRuleContext context)
        {
            if (!OverloadRule.IsArityCompatible(overloadParameters.Count, implementationParameters))
            {
                return false;
            }

            var erased = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            foreach (var generic in overloadGenerics.Concat(implementationGenerics))
            {
                erased[generic] = CheckedTypes.Unresolved;
            }

            ICheckedType Erase(ICheckedType type) =>
                erased.Count == 0
                    ? type
                    : TypeComparer.ResolveGenericTypeBySymbol(type, erased, context.SymbolTree, context.GlobalScope);

            var hasVariadic = implementationParameters.Count > 0 && implementationParameters[^1].IsVariadic;
            for (var i = 0; i < overloadParameters.Count; i++)
            {
                var implementationParam = i < implementationParameters.Count
                    ? implementationParameters[i]
                    : hasVariadic
                        ? implementationParameters[^1]
                        : null;
                if (implementationParam is null)
                {
                    return false;
                }

                var overloadType = overloadParameters[i].DeclaredType is { } overloadAst
                    ? Erase(context.ResolveTypeAnnotation(overloadAst, overloadState))
                    : CheckedTypes.Mixed;
                var implementationType = implementationParam.DeclaredType is { } implementationAst
                    ? Erase(context.ResolveTypeAnnotation(implementationAst, implementationState))
                    : CheckedTypes.Mixed;
                if (!context.IsAssignable(overloadType, implementationType))
                {
                    return false;
                }
            }

            var overloadReturnType = overloadReturn is not null
                ? Erase(context.ResolveTypeAnnotation(overloadReturn, overloadState, isReturnTypePosition: true))
                : CheckedTypes.Mixed;
            var implementationReturnType = implementationReturn is not null
                ? Erase(context.ResolveTypeAnnotation(implementationReturn, implementationState, isReturnTypePosition: true))
                : CheckedTypes.Mixed;
            return context.IsAssignable(implementationReturnType, overloadReturnType)
                || context.IsAssignable(overloadReturnType, implementationReturnType);
        }
    }
}
