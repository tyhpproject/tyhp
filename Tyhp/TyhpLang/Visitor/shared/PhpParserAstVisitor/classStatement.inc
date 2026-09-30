// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public IClassMember VisitClassStatement([NotNull] TyhpParser.ClassStatementContext context)
            => context switch
            {
                TyhpParser.ClassStatementAttributedContext ctx => this.VisitClassStatementAttributed(ctx),
                TyhpParser.ClassTraitUseContext ctx => this.VisitClassTraitUse(ctx),
                TyhpParser.ClassStatementGrammarAddonHandlerContext ctx => this.VisitClassStatementGrammarAddonHandler(ctx),
                _ => this.VisitClassStatementAlternative(context)
            };


        public virtual IClassMember VisitClassStatementAlternative([NotNull] TyhpParser.ClassStatementContext context)
        {
            this.ReportUnexpectedAlternative(context, "class-statement");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override IClassMember VisitClassStatementAttributed([NotNull] TyhpParser.ClassStatementAttributedContext context)
            => this.VisitAttributedClassStatement(context.attributedClassStatement()).WithAttributes(
                context.Attributes != null ? this.VisitAttributes(context.Attributes) : null
            );


        public override PhpTraitUseAst VisitClassTraitUse([NotNull] TyhpParser.ClassTraitUseContext context)
            => PhpTraitUseAst.Create(
                this.VisitClassNameList(context.TraitNameList),
                this.VisitTraitAdaptations(context.Adaptations),
                context
            );


        public override IClassMember VisitClassStatementGrammarAddonHandler([NotNull] TyhpParser.ClassStatementGrammarAddonHandlerContext context)
            => this.VisitClassStatementGrammarAddon(context.classStatementGrammarAddon());
