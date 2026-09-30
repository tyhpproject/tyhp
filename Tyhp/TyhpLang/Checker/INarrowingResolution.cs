using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Minimal type-resolution surface required by control-flow narrowing. Implemented by both the
    /// checker rule context (for statement narrowing) and the type inferrer (for narrowing the
    /// branches of conditional/ternary expressions during inference).
    /// </summary>
    internal interface INarrowingResolution
    {
        ICheckedType ResolveExpressionType(IBase2Ast expression, CheckerState state);

        ICheckedType ResolveTypeAnnotation(
            ITypeExpression typeAst,
            CheckerState state,
            bool isReturnTypePosition = false,
            bool isUserTypeDeclaration = true);

        /// <summary>
        /// Argument-driven generic inference for a call (same rules as named-call checking).
        /// Used by user-defined <c>$param is Type</c> guards so <c>T</c> can bind from a
        /// sibling argument (<c>T|__ClassName&lt;T&gt;</c> vs a receiver or <c>Foo::class</c>).
        /// </summary>
        bool TryInferGenericBindings(
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IReadOnlyList<ParameterInfo> parameters,
            PhpCallAst call,
            CheckerState state,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            ICheckedType? receiverType = null,
            ObjectMethodSymbol? method = null);
    }
}
