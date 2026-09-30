// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpMagicConstantAst VisitConstantTokenValue([NotNull] TyhpParser.ConstantTokenValueContext context)
        {
            if (context.TokenValue != null) return PhpMagicConstantAst.Create(context.TokenValue, context);
            if (context.TokenValueGrammarAddon != null) return this.VisitConstantTokenValueGrammarAddon(context.TokenValueGrammarAddon);

            this.ReportUnexpectedAlternative(context, "constant-token-value");
            return PhpMagicConstantAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
