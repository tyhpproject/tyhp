// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Parser


        public override Ast.PhpInlineOutputAst? VisitPhpInlineOutput([NotNull] TyhpParser.PhpInlineOutputContext context)
        {
            if (context.InlineHtml != null) {
                return Ast.PhpInlineOutputAst.Create(context.InlineHtml.Text, context);
            } else if (context.PhpEchoBlock != null) {
                return Ast.PhpInlineOutputAst.Create(this.VisitPhpEchoBlock(context.PhpEchoBlock, true), context);
            }

            return null;
        }
