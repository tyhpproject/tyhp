// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public virtual IStatement VisitStatementWithoutTerminalGrammarAddon([NotNull] TyhpParser.StatementWithoutTerminalGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "statementWithoutTerminalGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
