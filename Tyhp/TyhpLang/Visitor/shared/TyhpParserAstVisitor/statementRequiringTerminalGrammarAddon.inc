// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using System.Linq
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Override the statement requiring terminal grammar addon to dispatch
        /// to the Tyhp typed variable expression handler.
        ///
        /// Grammar (TyhpParser.g4):
        ///   statementRequiringTerminalGrammarAddon
        ///       : Statement=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?   #tyhpStatementTypedVarExpr
        ///       ;
        /// </summary>
        public override IStatement VisitStatementRequiringTerminalGrammarAddon(
            [NotNull] TyhpParser.StatementRequiringTerminalGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpStatementTypedVarExprContext typedVarExpr
                    => this.VisitTyhpStatementTypedVarExpr(typedVarExpr),
                _ => base.VisitStatementRequiringTerminalGrammarAddon(context),
            };


        /// <summary>
        /// Visit the labeled alternative #tyhpStatementTypedVarExpr.
        /// Delegates to VisitTyhpTypedVarExpr for the actual typed variable expression.
        ///
        /// Grammar:
        ///   #tyhpStatementTypedVarExpr
        ///       : Statement=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?
        /// </summary>
        public override TyhpTypedVarExprAst VisitTyhpStatementTypedVarExpr(
            [NotNull] TyhpParser.TyhpStatementTypedVarExprContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpStatementTypedVarExpr.Statement");
                return TyhpTypedVarExprAst.Create(
                    PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context)),
                    PhpVariableAst.CreateError(context, GetCurrentLanguageMode(context)),
                    null,
                    false,
                    false,
                    context);
            }

            return this.VisitTyhpTypedVarExpr(context.Statement);
        }
