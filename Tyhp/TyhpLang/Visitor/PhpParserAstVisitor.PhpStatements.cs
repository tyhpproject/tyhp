namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;

    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {





        protected T HandleUnexpectedAlternative<T>(ParserRuleContext context, string ruleName) where T : class
        {
            this.ReportUnexpectedAlternative(context, ruleName);
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context)) as T ?? throw new InvalidOperationException($"Cannot cast ErrorAst to {typeof(T).Name}");
        }





















































    }
}