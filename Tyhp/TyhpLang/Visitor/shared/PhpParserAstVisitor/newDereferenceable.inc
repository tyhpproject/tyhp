// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser

        public PhpNewAst VisitNewDereferenceable([NotNull] TyhpParser.NewDereferenceableContext context)
            => context switch {
                TyhpParser.NewClassInstanceContext newClassInstanceContext => this.VisitNewClassInstance(newClassInstanceContext),
                TyhpParser.NewAnonClassInstanceContext newAnonClassInstanceContext => this.VisitNewAnonClassInstance(newAnonClassInstanceContext),
                TyhpParser.NewDereferenceableGrammarAddonHandlerContext newDereferenceableGrammarAddonHandlerContext => this.VisitNewDereferenceableGrammarAddonHandler(newDereferenceableGrammarAddonHandlerContext),
                _ => this.VisitNewDereferenceableAlt(context),
            };


        public virtual PhpNewAst VisitNewDereferenceableAlt(TyhpParser.NewDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-dereferenceable");
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpNewAst VisitNewClassInstance([NotNull] TyhpParser.NewClassInstanceContext context)
            => PhpNewAst.Create(
                this.VisitClassNameReference(context.Identifier),
                this.VisitArgumentList(context.argumentList()),
                context
            );


        public override PhpNewAst VisitNewAnonClassInstance([NotNull] TyhpParser.NewAnonClassInstanceContext context)
            => PhpNewAst.CreateAnonymous(
                this.VisitAnonymousClass(context.anonymousClass()),
                this.VisitCtorArguments(context.anonymousClass().ctorArguments()),
                context
            );


        public override PhpNewAst VisitNewDereferenceableGrammarAddonHandler([NotNull] TyhpParser.NewDereferenceableGrammarAddonHandlerContext context)
            => this.VisitNewDereferenceableGrammarAddon(context.newDereferenceableGrammarAddon());
