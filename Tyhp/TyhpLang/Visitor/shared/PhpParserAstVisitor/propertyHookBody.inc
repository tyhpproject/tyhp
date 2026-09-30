// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpStatementBlockAst? VisitPropertyHookBody([NotNull] TyhpParser.PropertyHookBodyContext context)
        {
            if (context.StatementList != null)
            {
                return this.VisitInnerStatementList(context.StatementList);
            }
            else if (context.Expr != null)
            {
                // Arrow function style: => expr;
                var returnStmt = PhpUnaryOpAst.Create(
                    TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                    this.VisitExpr(context.Expr),
                    context
                );
                return PhpStatementBlockAst.Create([returnStmt], context);
            }
            else if (context.Start?.Type == TyhpParser.T_SYM_SEMICOLON)
            {
                // Abstract / interface hooks terminate with `;` and have no body (php-src
                // property_hook_body: ';'). Model absence as null, same as VisitMethodBody.
                return null;
            }
            else if (context.propertyHookBodyGrammarAddon() != null)
            {
                return this.VisitPropertyHookBodyGrammarAddon(context.propertyHookBodyGrammarAddon());
            }
            this.ReportUnexpectedAlternative(context, "property-hook-body");
            return PhpStatementBlockAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
