using System.Diagnostics;

namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;
    using static Tyhp.TyhpLang.Enum.PhpModifierExtensions;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {










































































        private string RequiredVariableText(IToken? variable, ParserRuleContext context, string nodeName)
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
    }
}