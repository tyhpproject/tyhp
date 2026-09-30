using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Structural <c>object { … }</c> on a <c>type</c> alias RHS. Members reuse the tyhpdef
    /// class statement list (signatures only) plus PHP class-const spelling
    /// (<c>const NAME = expr</c> / typed <c>const T NAME = expr</c>). Not a PHP class: no FQN, no
    /// <c>extends</c>/<c>implements</c> on the <c>object</c> header.
    ///
    /// Bare alias: <c>type Clock = object { public function now(): \DateTimeImmutable; };</c>
    /// — this node is the alias <see cref="TyhpTypeAliasAst.TypeExpression"/>.
    ///
    /// Intersection alias: <c>type Log = \Psr\Log\LoggerInterface &amp; object { … };</c>
    /// — this node is one item of the <see cref="PhpTypeKind.Intersection"/> list; nominal
    /// parents stay the other items. Use <see cref="Find"/> to recover the shape from either form.
    /// </summary>
    public class TyhpObjectShapeAst : Base2Ast, ITypeExpression
    {
        /// <summary>Public instance members (and <c>__construct</c> as a constructability signature).</summary>
        public PhpClassBodyAst? Members => Children.ElementAtOrDefault(0) as PhpClassBodyAst;

        public static TyhpObjectShapeAst Create(
            PhpClassBodyAst members,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpObjectShapeAst
            {
                Identifier = "object",
                Children = [members],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        public static TyhpObjectShapeAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpObjectShapeAst
            {
                Identifier = "object",
                Children = [PhpClassBodyAst.Create(null, context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// The object-shape item of an alias RHS: the node itself, or the first shape
        /// nested in a union/intersection wrapper.
        /// </summary>
        public static TyhpObjectShapeAst? Find(ITypeExpression? type)
        {
            switch (type)
            {
                case TyhpObjectShapeAst shape:
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
