// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public virtual IStatement VisitInternalFunctionsGrammarAddon([NotNull] TyhpParser.InternalFunctionsGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "internalFunctionsGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
