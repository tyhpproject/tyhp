namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {







        private PhpFunctionDeclAst CreateErrorFunctionDecl(ParserRuleContext context)
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









    }
}