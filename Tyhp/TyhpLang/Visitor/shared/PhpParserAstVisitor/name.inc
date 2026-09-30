// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public PhpNameAst VisitName([NotNull] TyhpParser.NameContext context)
            => context switch {
                TyhpParser.NameNotQualifiedContext nameNotQualifiedContext => this.VisitNameNotQualified(nameNotQualifiedContext),
                TyhpParser.NameSemiQualifiedContext nameSemiQualifiedContext => this.VisitNameSemiQualified(nameSemiQualifiedContext),
                TyhpParser.NameFullyQualifiedContext nameFullyQualifiedContext => this.VisitNameFullyQualified(nameFullyQualifiedContext),
                TyhpParser.NameRelativeContext nameRelativeContext => this.VisitNameRelative(nameRelativeContext),
                TyhpParser.NameTokenValueGrammarAddonHandlerContext nameTokenValueGrammarAddonHandlerContext => this.VisitNameTokenValueGrammarAddonHandler(nameTokenValueGrammarAddonHandlerContext),
                _ => this.VisitNameAlt(context),
            };


        public virtual PhpNameAst VisitNameAlt(TyhpParser.NameContext context)
        {
            this.ReportUnexpectedAlternative(context, "name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpNameAst VisitNameNotQualified([NotNull] TyhpParser.NameNotQualifiedContext context)
            => PhpNameAst.Create(context.TokenValue, context);


        public override PhpNameAst VisitNameSemiQualified([NotNull] TyhpParser.NameSemiQualifiedContext context)
            => PhpNameAst.Create(context.TokenValue, context);


        public override PhpNameAst VisitNameFullyQualified([NotNull] TyhpParser.NameFullyQualifiedContext context)
            => PhpNameAst.Create(context.TokenValue, context);


        public override PhpNameAst VisitNameRelative([NotNull] TyhpParser.NameRelativeContext context)
            => PhpNameAst.Create(context.TokenValue, context);


        public override PhpNameAst VisitNameTokenValueGrammarAddonHandler([NotNull] TyhpParser.NameTokenValueGrammarAddonHandlerContext context)
            => this.VisitNameTokenValueGrammarAddon(context.TokenValueGrammarAddon);
