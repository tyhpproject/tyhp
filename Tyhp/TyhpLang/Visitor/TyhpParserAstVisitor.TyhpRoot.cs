namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Entry point for parsing Tyhp source files.
        /// Grammar: tyhpSrcFile
        ///   : (startingInlineOutput+=tyhpInlineOutput)*
        ///       (firstCodeBlock=tyhpCodeBlock
        ///       (T_CLOSE_TAG codeBlocks+=tyhpCodeBlock)* T_CLOSE_TAG?)?
        ///       (endingInlineOutput+=tyhpInlineOutput)* EOF   #tyhpFile
        /// </summary>
        public override TyhpSrcFileAst VisitTyhpFile([NotNull] TyhpParser.TyhpFileContext context)
        {
            var codeBlocks = new List<ISrcElement?>();
            if (context.firstCodeBlock != null) {
                codeBlocks.Add(this.VisitTyhpCodeBlock(context.firstCodeBlock));
            }
            if (context._codeBlocks != null) {
                codeBlocks.AddRange(context._codeBlocks.Select(c => this.VisitTyhpCodeBlock(c)));
            }

            return TyhpSrcFileAst.Create(
                this._filename,
                this._fileHash,
                context._startingInlineOutput?.Select(VisitTyhpInlineOutput),
                codeBlocks,
                context._endingInlineOutput?.Select(VisitTyhpInlineOutput)
            );
        }

        /// <summary>
        /// Entry point for parsing tagless Tyhp source files (source.tagless enabled).
        /// Grammar: tyhpTaglessSrcFile
        ///   : T_TYHP_OPEN_TAG? StatementList=topStatementListWithRequiredFinalTerminal? EOF
        ///       #tyhpTaglessFile
        /// The open tag is optional; the whole file is a single Tyhp code block.
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
        /// Visits Tyhp inline output (content between code blocks that gets echoed).
        /// Grammar: tyhpInlineOutput
        ///   : InlineHtml=T_INLINE_HTML
        ///   | PhpEchoBlock=phpEchoBlock
        ///   | PhpBlock=phpBlock (T_CLOSE_TAG | T_SYM_SEMICOLON)+
        /// </summary>
        public override PhpInlineOutputAst? VisitTyhpInlineOutput([NotNull] TyhpParser.TyhpInlineOutputContext context)
        {
            if (context.InlineHtml != null) {
                return PhpInlineOutputAst.Create(context.InlineHtml.Text, context);
            } else if (context.PhpEchoBlock != null) {
                return PhpInlineOutputAst.Create(this.VisitPhpEchoBlock(context.PhpEchoBlock, true), context);
            } else if (context.PhpBlock != null) {
                var statementList = this.VisitPhpBlock(context.PhpBlock);
                if (statementList != null) {
                    return PhpInlineOutputAst.Create(statementList, context);
                }
            }

            return null;
        }

        /// <summary>
        /// Visits a Tyhp inline output statement (inline output sandwiched between close/open tags).
        /// Grammar: tyhpInlineOutputStatement
        ///   : T_CLOSE_TAG InlineOutput+=tyhpInlineOutput+ T_TYHP_OPEN_TAG
        ///   | T_INLINE_HTML
        /// Saves and restores CurrentTopStatementList to avoid side effects from
        /// visiting inline output children.
        /// </summary>
        public override PhpInlineOutputListAst? VisitTyhpInlineOutputStatement([NotNull] TyhpParser.TyhpInlineOutputStatementContext context)
        {
            PhpInlineOutputListAst? result = null;

            // save the current top statement list
            var currentTopStatementList = this.CurrentTopStatementList;

            if (context._InlineOutput != null && context._InlineOutput.Count > 0) {
                result = PhpInlineOutputListAst.Create(context._InlineOutput.Select(VisitTyhpInlineOutput), context);
            } else if (context.T_INLINE_HTML() != null) {
                result = PhpInlineOutputListAst.Create([PhpInlineOutputAst.Create(context.T_INLINE_HTML().GetText(), context)], context);
            }

            // restore the current top statement list
            this.CurrentTopStatementList = currentTopStatementList;

            return result;
        }
    }
}
