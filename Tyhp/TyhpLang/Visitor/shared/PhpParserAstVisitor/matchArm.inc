// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmAst VisitMatchArm([NotNull] TyhpParser.MatchArmContext context)
        {
            if (context.IsDefault != null)
            {
                // Default arm
                return PhpConditionalArmAst.Create(
                    null,
                    PhpStatementBlockAst.Create(
                        [PhpUnaryOpAst.Create(
                            TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                            this.VisitExpr(context.Expr),
                            context
                        )],
                        context
                    ),
                    true,
                    context
                );
            }
            else
            {
                // Regular arm with conditions
                return PhpConditionalArmAst.Create(
                    this.VisitMatchArmCondList(context.ArmCondList),
                    PhpStatementBlockAst.Create(
                        [PhpUnaryOpAst.Create(
                            TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                            this.VisitExpr(context.Expr),
                            context
                        )],
                        context
                    ),
                    false,
                    context
                );
            }
        }
