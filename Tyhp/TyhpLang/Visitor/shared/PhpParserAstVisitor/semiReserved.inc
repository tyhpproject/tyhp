// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst VisitSemiReserved([NotNull] TyhpParser.SemiReservedContext context)
        {
            if (context.RNM != null) return this.VisitReservedNonModifiers(context.RNM);
            if (context.SemiReserved != null) return this.VisitSemiReservedBase(context.SemiReserved);
            if (context.SemiReservedGrammarAddon != null) return this.VisitSemiReservedGrammarAddon(context.SemiReservedGrammarAddon);

            this.ReportUnexpectedAlternative(context, "semi-reserved");
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
