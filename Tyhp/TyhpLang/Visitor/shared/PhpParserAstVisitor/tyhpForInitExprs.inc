// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        // The for-init clause is overridden in TyhpParser.g4 (forSyntax) to accept typed-local
        // declarations alongside ordinary expressions, e.g. `for (int $i = 0; ...)`. The clause
        // is optional, so a null context (empty init, `for (;;)`) yields a null list.
        private PhpExpressionListAst? VisitForInitExprs(TyhpParser.TyhpForInitExprsContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return PhpExpressionListAst.Create(
                context._Items.Select(this.VisitForInitExpr),
                context
            );
        }
