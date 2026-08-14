namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override Ast.Interfaces.IBase2Ast? VisitNoGrammarAddon([NotNull] TyhpParser.NoGrammarAddonContext context)
            => null;
        
        /// <summary>
        /// This is the entry point for parsing PHP source files.
        /// </summary>
        public override Ast.PhpSrcFileAst VisitPhpSrcFile([NotNull] TyhpParser.PhpSrcFileContext context)
            => Ast.PhpSrcFileAst.Create(
                this._filename,
                this._fileHash,
                [
                    .. context._startingInlineOutput?.Select(VisitPhpInlineOutput) ?? [],
                    .. context._codeBlocks?.Select(VisitCodeBlock) ?? [],
                    .. context._endingInlineOutput?.Select(VisitPhpInlineOutput) ?? []
                ]);

        public Ast.Interfaces.ISrcElement? VisitCodeBlock([NotNull] TyhpParser.CodeBlockContext context)
            => context switch {
                TyhpParser.CodeBlockPhpBlockContext phpBlockContext => this.VisitCodeBlockPhpBlock(phpBlockContext),
                TyhpParser.CodeBlockGrammarAddonHandlerContext grammarAddonHandlerContext => this.VisitCodeBlockGrammarAddonHandler(grammarAddonHandlerContext),
                TyhpParser.CodeBlockErrorContext errorContext => this.VisitCodeBlockError(errorContext),
                _ => this.VisitCodeBlockAlt(context),
            };

        public virtual Ast.Interfaces.ISrcElement? VisitCodeBlockAlt([NotNull] TyhpParser.CodeBlockContext context)
            => (this.Visit(context) as Ast.Interfaces.ISrcElement) ?? Ast.UnexpectedNodeAst.Create(context);

        public override Ast.PhpTopStatementListAst? VisitCodeBlockPhpBlock([NotNull] TyhpParser.CodeBlockPhpBlockContext context)
            => this.VisitPhpBlock(context.PhpBlock);

        public override Ast.Interfaces.ISrcElement? VisitCodeBlockGrammarAddonHandler([NotNull] TyhpParser.CodeBlockGrammarAddonHandlerContext context)
            => null;

        public override Ast.Interfaces.ISrcElement? VisitCodeBlockError([NotNull] TyhpParser.CodeBlockErrorContext context)
            => Ast.UnexpectedNodeAst.Create(context); // Unexpected error in code block

        public override Ast.Interfaces.IBase2Ast? VisitCodeBlockGrammarAddon([NotNull] TyhpParser.CodeBlockGrammarAddonContext context)
            => null;

        public override Ast.PhpTopStatementListAst? VisitPhpBlock([NotNull] TyhpParser.PhpBlockContext context)
        {
            var result = context.StatementList != null ? this.VisitTopStatementListWithRequiredFinalTerminal(context.StatementList, true) : null;
            this.CurrentTopStatementList = result;
            return result;
        }

        public override Ast.PhpTopStatementListAst VisitPhpEchoBlock([NotNull] TyhpParser.PhpEchoBlockContext context)
            => this.VisitPhpEchoBlock(context, false);

        public Ast.PhpTopStatementListAst VisitPhpEchoBlock([NotNull] TyhpParser.PhpEchoBlockContext context, bool isCurrentTopStatementList)
        {
            var result = Ast.PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            if (isCurrentTopStatementList) {
                this.CurrentTopStatementList = result;
            }
            result.Add(Ast.PhpEchoStatementAst.Create(this.VisitEchoExprList(context.Expr), context));
            return result;
        }

        public override Ast.PhpInlineOutputAst? VisitPhpInlineOutput([NotNull] TyhpParser.PhpInlineOutputContext context)
        {
            if (context.InlineHtml != null) {
                return Ast.PhpInlineOutputAst.Create(context.InlineHtml.Text, context);
            } else if (context.PhpEchoBlock != null) {
                return Ast.PhpInlineOutputAst.Create(this.VisitPhpEchoBlock(context.PhpEchoBlock, true), context);
            }

            return null;
        }

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

        public override Ast.PhpInlineOutputListAst? VisitPhpInlineOutputStatementGrammarAddon([NotNull] TyhpParser.PhpInlineOutputStatementGrammarAddonContext context)
            => null;

        public override Ast.TokenValueAst? VisitPossibleComma([NotNull] TyhpParser.PossibleCommaContext context)
            => context.T_SYM_COMMA() != null ? Ast.TokenValueAst.Create(context.T_SYM_COMMA().Symbol, context) : null;
    }
}