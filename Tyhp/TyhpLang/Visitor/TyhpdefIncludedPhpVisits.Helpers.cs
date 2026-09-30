namespace Tyhp.TyhpLang.Visitor
{
    using System;
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;

    public partial class TyhpdefIncludedPhpVisits
    {

        protected static bool IsInTyhpOrTyhpdefSource(Antlr4.Runtime.RuleContext? context)
        {
            for (var current = context; current != null; current = current.Parent)
            {
                if (current is TyhpdefParser.TyhpdefBlockContext
                    or TyhpdefParser.TyhpdefTaglessFileContext
                    or TyhpdefParser.TyhpdefSrcFileContext)
                {
                    return true;
                }
            }
            return false;
        }

        protected void ReportMissingRequired(ParserRuleContext context, string ruleName)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorMissingRequiredNode,
                this._filename,
                context.Start?.Line ?? 0,
                context.Start?.Column ?? 0,
                ruleName);
        }
























        protected static PhpIfAst GetIfChainTail(PhpIfAst head)
        {
            var current = head;
            while (current.ElseStatement is PhpIfAst elseif)
            {
                current = elseif;
            }

            return current;
        }











        protected PhpFunctionDeclAst CreateErrorFunctionDecl(ParserRuleContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            return PhpFunctionDeclAst.Create(
                string.Empty,
                false,
                PhpParameterListAst.Create([], context, languageMode),
                null,
                null,
                context,
                languageMode
            );
        }














































































        protected string RequiredVariableText(IToken? variable, ParserRuleContext context, string nodeName)
        {
            if (variable != null)
            {
                return variable.Text;
            }

            this.Diagnostics.AddError(
                MessageCode.VisitorMissingRequiredNode,
                this._filename,
                context.Start?.Line ?? 0,
                context.Start?.Column ?? 0,
                nodeName);
            return "<error>";
        }



























        /// <summary>
        /// Builds the decl-kind token (<c>class</c>/<c>trait</c>/…), or an error placeholder when
        /// recovery left <c>ObjectType</c> null.
        /// </summary>
        protected TokenValueAst CreateObjectTypeToken(IToken? objectType, ParserRuleContext context)
        {
            if (objectType is null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename,
                    context.Start?.Line ?? 0,
                    context.Start?.Column ?? 0,
                    "objectType");
                return TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return TokenValueAst.Create(objectType, context);
        }













        /// <summary>
        /// Postfix <c>T...</c> glued to a value-parameter type is not PHP variadic syntax.
        /// <c>int... $x</c> (ellipsis glued to the type) is not the PHP variadic
        /// <c>int ...$x</c> and must not be accepted as one in Tyhp / tyhpdef.
        /// </summary>
        protected static bool IsTyhpPostfixEllipsisOnValueParameter(TyhpdefParser.ParameterContext context)
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









        protected T HandleUnexpectedAlternative<T>(ParserRuleContext context, string ruleName) where T : class
        {
            this.ReportUnexpectedAlternative(context, ruleName);
            return ErrorAst.Create(context, TyhpdefIncludedPhpVisits.GetCurrentLanguageMode(context)) as T ?? throw new InvalidOperationException($"Cannot cast ErrorAst to {typeof(T).Name}");
        }
    }
}
