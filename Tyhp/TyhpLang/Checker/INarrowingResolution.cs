using Tyhp.TyhpLang.Ast.Interfaces;

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
    }
}
