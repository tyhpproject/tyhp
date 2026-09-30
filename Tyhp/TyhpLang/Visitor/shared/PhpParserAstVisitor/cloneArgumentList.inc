// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpArgumentListAst VisitCloneArgumentList([NotNull] TyhpParser.CloneArgumentListContext context)
        {
            if (context.ArgumentList != null)
            {
                return this.VisitNonEmptyCloneArgumentList(context.ArgumentList);
            }

            if (context.Expr != null)
            {
                // clone($x,) — trailing comma disambiguates from unary clone($x)
                return PhpArgumentListAst.Create(
                    [PhpArgumentAst.Create(this.VisitExpr(context.Expr), context)],
                    context
                );
            }

            // clone()
            return PhpArgumentListAst.Create([], context);
        }
