// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public ITypeExpression VisitReturnTypeGrammarAddon([NotNull] TyhpParser.ReturnTypeGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "returnTypeGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
