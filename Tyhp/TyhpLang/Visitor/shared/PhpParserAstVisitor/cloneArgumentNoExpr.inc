// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpArgumentAst VisitCloneArgumentNoExpr([NotNull] TyhpParser.CloneArgumentNoExprContext context)
        {
            if (context.Identifier != null && context.Expr != null)
            {
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    this.VisitIdentifier(context.Identifier),
                    false,
                    context
                );
            }

            if (context.IsVariadic != null && context.Expr != null)
            {
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    null,
                    true,
                    context
                );
            }

            // First-class callable: clone(...)
            return PhpArgumentAst.Create(null, null, true, context);
        }
