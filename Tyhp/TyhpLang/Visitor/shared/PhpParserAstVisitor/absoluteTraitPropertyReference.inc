// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitMemberRefAst VisitAbsoluteTraitPropertyReference([NotNull] TyhpParser.AbsoluteTraitPropertyReferenceContext context)
            => PhpTraitMemberRefAst.Create(
                this.VisitClassName(context.ClassName),
                PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                context
            );
