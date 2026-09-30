// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmAst VisitCaseExpr([NotNull] TyhpParser.CaseExprContext context)
            => PhpConditionalArmAst.Create(
                PhpExpressionListAst.Create([this.VisitExpr(context.Expr)], context),
                PhpStatementBlockAst.Create(
                    context.StatementList._Items?.Select(this.VisitInnerStatement),
                    context
                ),
                false,
                context
            );
