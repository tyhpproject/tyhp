// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpTraitPrecedenceAst VisitTraitPrecedence([NotNull] TyhpParser.TraitPrecedenceContext context)
            => PhpTraitPrecedenceAst.Create(
                this.VisitAbsoluteTraitMethodReference(context.MethodReference),
                this.VisitClassNameList(context.TraitNameList),
                context
            );
