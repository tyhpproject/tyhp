namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Entry point for parsing Tyhp source files.
        /// Grammar: tyhpSrcFile : chunks+=tyhpFileChunk* EOF #tyhpFile
        /// Chunks are inline HTML, <c>&lt;?=</c> echoes, <c>&lt;?tyhp</c> blocks, close tags,
        /// and rejected <c>&lt;?php</c> blocks, in source order.
        /// </summary>
        public override TyhpSrcFileAst VisitTyhpFile([NotNull] TyhpParser.TyhpFileContext context)
        {
            var children = new List<ISrcElement?>();
            foreach (var chunk in context._chunks)
            {
                switch (chunk)
                {
                    case TyhpParser.TyhpFileChunkInlineContext inline:
                        children.Add(this.VisitTyhpInlineOutput(inline.Output));
                        break;
                    case TyhpParser.TyhpFileChunkCodeContext code:
                        children.Add(this.VisitTyhpCodeBlock(code.Code));
                        break;
                    case TyhpParser.TyhpFileChunkPhpContext php:
                        this.ReportPhpOpenTagInTyhpFile(php.Rejected);
                        break;
                    case TyhpParser.TyhpFileChunkCloseContext:
                        break;
                }
            }

            return TyhpSrcFileAst.Create(
                this._filename,
                this._fileHash,
                codeBlocks: children
            );
        }

        /// <summary>
        /// Entry point for parsing tagless Tyhp source files (source.tagless enabled).
        /// Grammar: tyhpTaglessSrcFile
        ///   : StatementList=topStatementListWithRequiredFinalTerminal? EOF
        ///       #tyhpTaglessFile
        /// The whole file is a single Tyhp code block and has no open tag. A file
        /// whose first six characters are <c>&lt;?tyhp</c> is parsed with tyhpSrcFile.
        /// </summary>
        public override TyhpSrcFileAst VisitTyhpTaglessFile([NotNull] TyhpParser.TyhpTaglessFileContext context)
        {
            var result = context.StatementList != null
                ? this.VisitTopStatementListWithRequiredFinalTerminal(context.StatementList, true)
                : null;
            this.CurrentTopStatementList = result;

            var codeBlocks = new List<ISrcElement?>();
            if (result != null) {
                codeBlocks.Add(result);
            }

            return TyhpSrcFileAst.Create(
                this._filename,
                this._fileHash,
                null,
                codeBlocks,
                null
            );
        }

        /// <summary>
        /// Dispatch method for tyhpCodeBlock labeled alternatives.
        /// Grammar: tyhpCodeBlock
        ///   : TyhpBlock=tyhpBlock   #tyhpCodeBlockTyhpBlock
        /// </summary>
        public ISrcElement? VisitTyhpCodeBlock([NotNull] TyhpParser.TyhpCodeBlockContext context)
            => context switch {
                TyhpParser.TyhpCodeBlockTyhpBlockContext ctx => this.VisitTyhpCodeBlockTyhpBlock(ctx),
                _ => UnexpectedNodeAst.Create(context), // Unexpected tyhpCodeBlock alternative
            };

        /// <summary>
        /// Delegates to VisitTyhpBlock for the tyhpCodeBlockTyhpBlock alternative.
        /// </summary>
        public override PhpTopStatementListAst? VisitTyhpCodeBlockTyhpBlock([NotNull] TyhpParser.TyhpCodeBlockTyhpBlockContext context)
            => this.VisitTyhpBlock(context.TyhpBlock);

        /// <summary>
        /// Visits a Tyhp block (tyhp open tag followed by optional top statements).
        /// Grammar: tyhpBlock
        ///   : T_TYHP_OPEN_TAG StatementList=topStatementListWithRequiredFinalTerminal?
        /// Sets the current language mode to "tyhp" (handled by grammar action).
        /// </summary>
        public override PhpTopStatementListAst? VisitTyhpBlock([NotNull] TyhpParser.TyhpBlockContext context)
        {
            var result = context.StatementList != null
                ? this.VisitTopStatementListWithRequiredFinalTerminal(context.StatementList, true)
                : null;
            this.CurrentTopStatementList = result;
            return result;
        }

        /// <summary>
        /// Visits Tyhp inline output (HTML or a <c>&lt;?=</c> echo between code blocks).
        /// Grammar: tyhpInlineOutput
        ///   : InlineHtml=T_INLINE_HTML
        ///   | PhpEchoBlock=phpEchoBlock
        /// </summary>
        public override PhpInlineOutputAst? VisitTyhpInlineOutput([NotNull] TyhpParser.TyhpInlineOutputContext context)
        {
            if (context.InlineHtml != null) {
                return PhpInlineOutputAst.Create(context.InlineHtml.Text, context);
            } else if (context.PhpEchoBlock != null) {
                return PhpInlineOutputAst.Create(this.VisitPhpEchoBlock(context.PhpEchoBlock, true), context);
            }

            return null;
        }

        /// <summary>
        /// A <c>&lt;?php</c> reopen inside a <c>.tyhp</c> statement is the Tyhp inline-output
        /// statement. HTML and <c>&lt;?=</c> echoes stay in the statement list. The PHP block
        /// is not visited, so it is not type-checked or emitted.
        /// </summary>
        public override PhpInlineOutputListAst? VisitPhpInlineOutputStatement([NotNull] TyhpParser.PhpInlineOutputStatementContext context)
        {
            if (context.Reopen != null) {
                return this.VisitTyhpInlineOutputStatement(context.Reopen);
            }

            // The generated list is never null, so a lone HTML token (the alternative that
            // is not `?> … <?php`) must be read here. An empty list would drop that token.
            if (context._InlineOutput.Count == 0 && context.T_INLINE_HTML() != null) {
                return PhpInlineOutputListAst.Create(
                    [PhpInlineOutputAst.Create(context.T_INLINE_HTML().GetText(), context)],
                    context);
            }

            return base.VisitPhpInlineOutputStatement(context);
        }

        /// <summary>
        /// Visits inline output inside a statement, between <c>?&gt;</c> and the next open tag.
        /// Grammar: tyhpInlineOutputStatement
        ///   : T_CLOSE_TAG InlineOutput+=tyhpInlineOutput*
        ///       (T_TYHP_OPEN_TAG | Rejected=tyhpRejectedPhpBlock After+=tyhpInlineOutput* T_TYHP_OPEN_TAG?)
        /// Saves and restores CurrentTopStatementList so an echo does not replace the
        /// enclosing statement list.
        /// </summary>
        public override PhpInlineOutputListAst? VisitTyhpInlineOutputStatement([NotNull] TyhpParser.TyhpInlineOutputStatementContext context)
        {
            PhpInlineOutputListAst? result = null;

            var currentTopStatementList = this.CurrentTopStatementList;

            if (context.Rejected != null) {
                this.ReportPhpOpenTagInTyhpFile(context.Rejected);
            }

            var pieces = new List<PhpInlineOutputAst?>();
            if (context._InlineOutput != null) {
                pieces.AddRange(context._InlineOutput.Select(VisitTyhpInlineOutput));
            }

            if (context._After != null) {
                pieces.AddRange(context._After.Select(VisitTyhpInlineOutput));
            }

            if (pieces.Count > 0) {
                result = PhpInlineOutputListAst.Create(pieces, context);
            }

            this.CurrentTopStatementList = currentTopStatementList;

            return result;
        }

        /// <summary>
        /// Reports TYHP2005 at a <c>&lt;?php</c> block. The block's statements are not visited.
        /// </summary>
        private void ReportPhpOpenTagInTyhpFile(ParserRuleContext phpBlock)
        {
            this.Diagnostics.AddError(
                MessageCode.PhpOpenTagNotAllowedInTyhpFile,
                this._filename,
                phpBlock.Start?.Line ?? 0,
                phpBlock.Start?.Column ?? 0);
        }
    }
}
