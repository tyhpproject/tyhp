// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using System.Linq
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visit a statement without a terminal grammar addon (tyhp-specific statements).
        /// Routes tyhpUsingBlock to the Tyhp using block handler.
        ///
        /// Grammar (TyhpParser.g4):
        ///   statementWithoutTerminalGrammarAddon
        ///       : Statement=tyhpUsingBlock {this.isLanguageMode("tyhp")}?   #tyhpStatementUsingBlock
        ///       ;
        /// </summary>
        public override IStatement VisitStatementWithoutTerminalGrammarAddon(
            [NotNull] TyhpParser.StatementWithoutTerminalGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpStatementUsingBlockContext usingCtx
                    => this.VisitTyhpUsingBlock(usingCtx.Statement),
                _ => base.VisitStatementWithoutTerminalGrammarAddon(context),
            };
