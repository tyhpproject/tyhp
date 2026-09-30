// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public PhpNewAst VisitNewNonDereferenceable([NotNull] TyhpParser.NewNonDereferenceableContext context)
            => context switch {
                TyhpParser.NewClassInstanceNonDereferenceableContext newClassInstanceNonDereferenceableContext => this.VisitNewClassInstanceNonDereferenceable(newClassInstanceNonDereferenceableContext),
                TyhpParser.NewNonDereferenceableGrammarAddonHandlerContext newNonDereferenceableGrammarAddonHandlerContext => this.VisitNewNonDereferenceableGrammarAddonHandler(newNonDereferenceableGrammarAddonHandlerContext),
                _ => this.VisitNewNonDereferenceableAlt(context),
            };

        
        public virtual PhpNewAst VisitNewNonDereferenceableAlt(TyhpParser.NewNonDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-non-dereferenceable");
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpNewAst VisitNewClassInstanceNonDereferenceable([NotNull] TyhpParser.NewClassInstanceNonDereferenceableContext context)
            => PhpNewAst.Create(
                this.VisitClassNameReference(context.Identifier),
                null,
                context
            );


        public override PhpNewAst VisitNewNonDereferenceableGrammarAddonHandler([NotNull] TyhpParser.NewNonDereferenceableGrammarAddonHandlerContext context)
            => this.VisitNewNonDereferenceableGrammarAddon(context.newNonDereferenceableGrammarAddon());
