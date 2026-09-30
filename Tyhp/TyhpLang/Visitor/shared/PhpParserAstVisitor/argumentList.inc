// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpArgumentListAst? VisitArgumentList([NotNull] TyhpParser.ArgumentListContext context)
        {
            if (context.ArgumentList != null)
            {
                return this.VisitNonEmptyArgumentList(context.ArgumentList);
            }
            else if (context.Ellipsis != null)
            {
                // Variadic argument list with ellipsis only
                return PhpArgumentListAst.Create(
                    [PhpArgumentAst.Create(null, null, true, context)],
                    context
                );
            }
            
            return null;
        }
