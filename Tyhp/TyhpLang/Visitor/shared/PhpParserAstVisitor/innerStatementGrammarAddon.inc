// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        // Not an ANTLR override: Tyhp's innerStatementGrammarAddon has only labeled
        // alternatives, so the generated visitor has no VisitInnerStatementGrammarAddon.
        public virtual IStatement VisitInnerStatementGrammarAddon([NotNull] TyhpParser.InnerStatementGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "innerStatementGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
