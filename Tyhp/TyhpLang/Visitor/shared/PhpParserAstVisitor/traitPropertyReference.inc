// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitMemberRefAst VisitTraitPropertyReference([NotNull] TyhpParser.TraitPropertyReferenceContext context)
        {
            if (context.VariableReference != null)
            {
                return this.VisitAbsoluteTraitPropertyReference(context.VariableReference);
            }
            else
            {
                return PhpTraitMemberRefAst.Create(
                    PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                    context
                );
            }
        }
