using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Structural <c>callable(…): R</c> type. Bare <c>callable</c> stays
    /// <see cref="PhpBuiltinTypeAst"/>. Unknown arity is <c>callable(...): R</c>
    /// (ellipsis is the only thing in the parameter list).
    /// </summary>
    public class TyhpCallableShapeAst : Base2Ast, ITypeExpression
    {
        private const short IS_UNKNOWN_ARITY_FLAG = -1;

        public TyhpCallableShapeParameterListAst? Parameters =>
            Children.ElementAtOrDefault(0) as TyhpCallableShapeParameterListAst;

        public ITypeExpression? ReturnType =>
            Children.ElementAtOrDefault(1) as ITypeExpression;

        public bool IsUnknownArity => HasFlag(IS_UNKNOWN_ARITY_FLAG);

        public static TyhpCallableShapeAst Create(
            TyhpCallableShapeParameterListAst parameters,
            ITypeExpression returnType,
            bool isUnknownArity,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpCallableShapeAst
            {
                Identifier = "callable",
                Children = [parameters, returnType],
            };
            result.SetFlag(IS_UNKNOWN_ARITY_FLAG, isUnknownArity);
            result.SetContext(context, languageMode);
            return result;
        }

        public static TyhpCallableShapeAst CreateError(
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpCallableShapeAst
            {
                Identifier = "callable",
                Children =
                [
                    TyhpCallableShapeParameterListAst.Create(null, context, languageMode),
                    PhpTypeExpressionAst.CreateError(context, languageMode),
                ],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// The callable-shape item of a type: the node itself, or the first shape
        /// nested in a union/intersection / simple wrapper.
        /// </summary>
        public static TyhpCallableShapeAst? Find(ITypeExpression? type)
        {
            switch (type)
            {
                case TyhpCallableShapeAst shape:
                    return shape;
                case PhpTypeExpressionAst expr:
                    if (expr.TypeKind == PhpTypeKind.Simple && expr.Types is not null)
                    {
                        foreach (var child in expr.Types.GetAllNotNull())
                        {
                            var found = Find(child);
                            if (found is not null)
                            {
                                return found;
                            }
                        }
                    }

                    return null;
                default:
                    return null;
            }
        }
    }
}
