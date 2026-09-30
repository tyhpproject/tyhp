using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Type-position / alias-RHS <c>struct { … }</c> (and <c>struct extends Parent { … }</c>).
    /// Named structs are <c>type Name = struct { … }</c>; this node is the RHS shape.
    /// Expression <c>new struct { … }</c> still uses <see cref="TyhpStructDeclAst"/>.
    /// </summary>
    public class TyhpStructShapeAst : Base2Ast, ITypeExpression
    {
        public TyhpStructPropertyListAst? PropertyList =>
            Children.ElementAtOrDefault(0) as TyhpStructPropertyListAst;

        public PhpNameAst? Extends =>
            Children.ElementAtOrDefault(1) as PhpNameAst;

        public static TyhpStructShapeAst Create(
            TyhpStructPropertyListAst propertyList,
            PhpNameAst? extends,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpStructShapeAst
            {
                Identifier = "struct",
                Children = [propertyList, extends],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        public static TyhpStructShapeAst CreateError(
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpStructShapeAst
            {
                Identifier = "struct",
                Children = [TyhpStructPropertyListAst.Create(null, context, languageMode), null],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// The entire type is a struct shape (possibly wrapped in a Simple
        /// <see cref="PhpTypeExpressionAst"/>, including grouping). Unions,
        /// intersections, and <c>?struct { … }</c> return null so
        /// <c>type Name = (struct { … }) | null</c> stays a type alias.
        /// </summary>
        public static TyhpStructShapeAst? UnwrapBareRhs(ITypeExpression? type)
        {
            while (true)
            {
                switch (type)
                {
                    case TyhpStructShapeAst shape:
                        return shape;
                    case PhpTypeExpressionAst expr:
                        if (expr.IsNullable
                            || expr.TypeKind is PhpTypeKind.Union or PhpTypeKind.Intersection
                            || expr.Types is null)
                        {
                            return null;
                        }

                        ITypeExpression? only = null;
                        foreach (var child in expr.Types.GetAllNotNull())
                        {
                            if (only is not null)
                            {
                                return null;
                            }

                            only = child;
                        }

                        if (only is null)
                        {
                            return null;
                        }

                        type = only;
                        continue;
                    default:
                        return null;
                }
            }
        }

        public static TyhpStructShapeAst? Find(ITypeExpression? type)
        {
            switch (type)
            {
                case TyhpStructShapeAst shape:
                    return shape;
                case PhpTypeExpressionAst expr:
                    if (expr.Types is null)
                    {
                        return null;
                    }

                    foreach (var child in expr.Types.GetAllNotNull())
                    {
                        var found = Find(child);
                        if (found is not null)
                        {
                            return found;
                        }
                    }

                    return null;
                default:
                    return null;
            }
        }
    }
}
