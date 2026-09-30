// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitMemberRefAst VisitTraitMethodReference([NotNull] TyhpParser.TraitMethodReferenceContext context)
        {
            if (context.traitMethodReferenceGrammarAddon() != null)
            {
                return this.VisitTraitMethodReferenceGrammarAddon(context.traitMethodReferenceGrammarAddon());
            }

            if (context.MethodReference != null)
            {
                return this.VisitAbsoluteTraitMethodReference(context.MethodReference);
            }

            return PhpTraitMemberRefAst.Create(
                this.VisitIdentifier(context.Identifier),
                context
            ).WithGrammarAddon("identifier", this.VisitTraitMethodIdentifierGrammarAddon(context.traitMethodIdentifierGrammarAddon()));
        }
