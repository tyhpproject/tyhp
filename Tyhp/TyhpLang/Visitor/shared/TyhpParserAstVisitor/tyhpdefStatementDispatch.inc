// #class TyhpParserAstVisitor
// #using System
// #using System.Collections.Generic
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Dispatches tyhpdefStatement labeled alternatives.
        /// Grammar: tyhpdefStatement
        ///   : T_SYM_SEMICOLON                            #tyhpdefEmptyStatement
        ///   | T_DECLARE ...                              #tyhpdefDeclare
        ///   | Statement=tyhpdefImportConstStatement       #tyhpdefImportConst
        ///   | Statement=tyhpdefImportVariableStatement    #tyhpdefImportVariable
        /// </summary>
        public ITopStatement VisitTyhpdefStatement(TyhpParser.TyhpdefStatementContext? context)
        {
            if (context == null)
            {
                return ErrorAst.Create(
                    "Missing tyhpdefStatement",
                    MessageCode.VisitorMissingRequiredNode,
                    0,
                    0,
                    languageMode: "tyhpdef");
            }

            return context switch
            {
                TyhpParser.TyhpdefEmptyStatementContext ctx => this.VisitTyhpdefEmptyStatement(ctx),
                TyhpParser.TyhpdefDeclareContext ctx => this.VisitTyhpdefDeclare(ctx),
                TyhpParser.TyhpdefImportConstContext ctx => this.VisitTyhpdefImportConst(ctx),
                TyhpParser.TyhpdefImportVariableContext ctx => this.VisitTyhpdefImportVariable(ctx),
                _ => HandleUnexpectedAlternative<ITopStatement>(context, "tyhpdefStatement")
            };
        }

        /// <summary>
        /// Visits a tyhpdef empty statement (bare semicolon).
        /// Grammar: T_SYM_SEMICOLON   #tyhpdefEmptyStatement
        /// </summary>
        public override PhpNopStatementAst VisitTyhpdefEmptyStatement([NotNull] TyhpParser.TyhpdefEmptyStatementContext context)
            => PhpNopStatementAst.Create(context, GetCurrentLanguageMode(context));

        /// <summary>
        /// Visits a tyhpdef declare statement.
        /// Grammar: T_DECLARE (...) Body=tyhpdefDeclareBody   #tyhpdefDeclare
        /// Body is <c>;</c>, <c>{ tyhpdefTopStatementList }</c>, or colon/enddeclare.
        /// </summary>
        public override PhpDeclareAst VisitTyhpdefDeclare([NotNull] TyhpParser.TyhpdefDeclareContext context)
        {
            var declareList = context.DeclareList != null
                ? this.VisitConstList(context.DeclareList)
                : PhpConstDeclListAst.Create(null, context);
            IStatement body = context.Body != null
                ? this.VisitTyhpdefDeclareBody(context.Body)
                : ErrorAst.Create(context, GetCurrentLanguageMode(context));
            return PhpDeclareAst.Create(declareList, body, context, GetCurrentLanguageMode(context));
        }

        public IStatement VisitTyhpdefDeclareBody(TyhpParser.TyhpdefDeclareBodyContext? context)
        {
            if (context == null)
            {
                return ErrorAst.Create(
                    "Missing tyhpdefDeclareBody",
                    MessageCode.VisitorMissingRequiredNode,
                    0,
                    0,
                    languageMode: "tyhpdef");
            }

            return context switch
            {
                TyhpParser.TyhpdefDeclareEmptyContext ctx => this.VisitTyhpdefDeclareEmpty(ctx),
                TyhpParser.TyhpdefDeclareBlockContext ctx => this.VisitTyhpdefDeclareBlock(ctx),
                TyhpParser.TyhpdefDeclareColonContext ctx => this.VisitTyhpdefDeclareColon(ctx),
                _ => HandleUnexpectedAlternative<IStatement>(context, "tyhpdefDeclareBody"),
            };
        }

        public override PhpNopStatementAst VisitTyhpdefDeclareEmpty([NotNull] TyhpParser.TyhpdefDeclareEmptyContext context)
            => PhpNopStatementAst.Create(context, GetCurrentLanguageMode(context));

        public override PhpStatementBlockAst VisitTyhpdefDeclareBlock([NotNull] TyhpParser.TyhpdefDeclareBlockContext context)
            => this.TyhpdefStatementsToBlock(
                context.StatementList != null
                    ? this.VisitTyhpdefTopStatementList(context.StatementList)
                    : null,
                context);

        public override PhpStatementBlockAst VisitTyhpdefDeclareColon([NotNull] TyhpParser.TyhpdefDeclareColonContext context)
            => this.TyhpdefStatementsToBlock(
                context.StatementList != null
                    ? this.VisitTyhpdefTopStatementList(context.StatementList)
                    : null,
                context);

        private PhpStatementBlockAst TyhpdefStatementsToBlock(
            PhpTopStatementListAst? statements,
            ParserRuleContext context)
        {
            var children = new List<IStatement>();
            if (statements != null)
            {
                foreach (var stmt in statements.GetAllNotNull())
                {
                    if (stmt is IStatement inner)
                    {
                        children.Add(inner);
                    }
                    else
                    {
                        this.ReportUnexpectedAlternative(context, stmt.GetType().Name);
                        children.Add(ErrorAst.Create(context, GetCurrentLanguageMode(context)));
                    }
                }
            }

            return PhpStatementBlockAst.Create(children, context, GetCurrentLanguageMode(context));
        }
