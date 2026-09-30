// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst VisitSemiReservedGrammarAddon([NotNull] TyhpParser.SemiReservedGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "semi-reserved-grammar-addon", context.GetType().Name
            );
            return TokenValueAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
