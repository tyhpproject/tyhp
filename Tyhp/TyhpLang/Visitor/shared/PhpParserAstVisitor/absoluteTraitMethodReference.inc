// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitMemberRefAst VisitAbsoluteTraitMethodReference([NotNull] TyhpParser.AbsoluteTraitMethodReferenceContext context)
        {
            if (context.absoluteTraitMethodReferenceGrammarAddon() != null)
            {
                return this.VisitAbsoluteTraitMethodReferenceGrammarAddon(
                    context.absoluteTraitMethodReferenceGrammarAddon());
            }

            return PhpTraitMemberRefAst.Create(
                this.VisitClassName(context.ClassName),
                this.VisitIdentifier(context.Identifier),
                context
            ).WithGrammarAddon("identifier", this.VisitTraitMethodIdentifierGrammarAddon(context.traitMethodIdentifierGrammarAddon()));
        }
