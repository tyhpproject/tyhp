// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Parser


        public override Ast.PhpInlineOutputListAst? VisitPhpInlineOutputStatement([NotNull] TyhpParser.PhpInlineOutputStatementContext context)
        {
            Ast.PhpInlineOutputListAst? result = null;
            
            // save the current top statement list
            var currentTopStatementList = this.CurrentTopStatementList;

            if (context._InlineOutput != null) {
                result = Ast.PhpInlineOutputListAst.Create(context._InlineOutput.Select(VisitPhpInlineOutput), context);
            } else if (context.T_INLINE_HTML() != null) {
                result = Ast.PhpInlineOutputListAst.Create([Ast.PhpInlineOutputAst.Create(context.T_INLINE_HTML().GetText(), context)], context);
            } else if (context.phpInlineOutputStatementGrammarAddon() != null) {
                result = this.VisitPhpInlineOutputStatementGrammarAddon(context.phpInlineOutputStatementGrammarAddon());
            }

            // restore the current top statement list
            this.CurrentTopStatementList = currentTopStatementList;

            return result;
        }
