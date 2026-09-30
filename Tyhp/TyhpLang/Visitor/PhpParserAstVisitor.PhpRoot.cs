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
        {
            // phpSrcFile labels the first block as firstCodeBlock and only later
            // ?>…<?php blocks as codeBlocks+=. Skipping firstCodeBlock dropped every
            // statement in a typical single-block PHP file (PHP-source harvest).
            var children = new List<Ast.Interfaces.ISrcElement?>();
            if (context._startingInlineOutput != null)
            {
                children.AddRange(context._startingInlineOutput.Select(this.VisitPhpInlineOutput));
            }

            if (context.firstCodeBlock != null)
            {
                children.Add(this.VisitCodeBlock(context.firstCodeBlock));
            }

            if (context._codeBlocks != null)
            {
                children.AddRange(context._codeBlocks.Select(this.VisitCodeBlock));
            }

            if (context._endingInlineOutput != null)
            {
                children.AddRange(context._endingInlineOutput.Select(this.VisitPhpInlineOutput));
            }

            return Ast.PhpSrcFileAst.Create(this._filename, this._fileHash, children);
        }

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






    }
}