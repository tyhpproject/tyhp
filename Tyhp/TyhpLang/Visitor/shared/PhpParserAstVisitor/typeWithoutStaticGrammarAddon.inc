// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override ITypeExpression VisitTypeWithoutStaticGrammarAddon([NotNull] TyhpParser.TypeWithoutStaticGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "typeWithoutStaticGrammarAddon");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
