// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpArgumentAst VisitArgument([NotNull] TyhpParser.ArgumentContext context)
        {
            if (context.IsVariadic?.TokenValue != null && context.Expr != null)
            {
                // Variadic argument: ...expr
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    null,
                    true,
                    context
                );
            }
            else if (context.Identifier != null && context.Expr != null)
            {
                // Named argument: name: expr
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    this.VisitIdentifier(context.Identifier),
                    false,
                    context
                );
            }
            else if (context.Expr != null)
            {
                // Regular argument
                return PhpArgumentAst.Create(
                    this.VisitExpr(context.Expr),
                    context
                );
            }

            return PhpArgumentAst.Create(UnexpectedNodeAst.Create(context), context, GetCurrentLanguageMode(context));
        }
