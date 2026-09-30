namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {









        /// <summary>
        /// Postfix <c>T...</c> glued to a value-parameter type is not PHP variadic syntax.
        /// <c>int... $x</c> (ellipsis glued to the type) is not the PHP variadic
        /// <c>int ...$x</c> and must not be accepted as one in Tyhp / tyhpdef.
        /// </summary>
        private static bool IsTyhpPostfixEllipsisOnValueParameter(TyhpParser.ParameterContext context)
        {
            if (context.IsVariadic?.TokenValue == null || context.TypeExpr?.Stop == null)
            {
                return false;
            }

            if (!IsInTyhpOrTyhpdefSource(context))
            {
                return false;
            }

            var typeStop = context.TypeExpr.Stop;
            var ellipsis = context.IsVariadic.Start;
            return ellipsis != null && typeStop.StopIndex + 1 == ellipsis.StartIndex;
        }

        /// <summary>
        /// <see cref="GetCurrentLanguageMode"/> returns empty at <c>TyhpSrcFile</c> /
        /// <c>TyhpdefSrcFile</c> roots, so tagged <c>&lt;?tyhp</c> files must be
        /// recognized by walking to the file or block context. PHP islands stay PHP.
        /// </summary>
        private static bool IsInTyhpOrTyhpdefSource(Antlr4.Runtime.RuleContext? context)
        {
            for (var current = context; current != null; current = current.Parent)
            {
                if (current is TyhpParser.PhpBlockContext or TyhpParser.PhpSrcFileContext)
                {
                    return false;
                }

                if (current is TyhpParser.TyhpBlockContext
                    or TyhpParser.TyhpTaglessFileContext
                    or TyhpParser.TyhpSrcFileContext
                    or TyhpParser.TyhpdefBlockContext
                    or TyhpParser.TyhpdefTaglessFileContext
                    or TyhpParser.TyhpdefSrcFileContext)
                {
                    return true;
                }
            }

            return false;
        }











    }
}