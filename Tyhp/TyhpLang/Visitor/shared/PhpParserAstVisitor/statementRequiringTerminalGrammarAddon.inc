// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public virtual IStatement VisitStatementRequiringTerminalGrammarAddon([NotNull] TyhpParser.StatementRequiringTerminalGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "statementRequiringTerminalGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
