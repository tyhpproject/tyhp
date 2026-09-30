// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpEnumCaseAst VisitEnumCase([NotNull] TyhpParser.EnumCaseContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpEnumCaseAst.Create(
                this.VisitIdentifier(context.Identifier),
                this.VisitEnumCaseExpr(context.Expr),
                docComment,
                context
            );
        }
