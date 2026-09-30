// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override IStatement VisitStatement([NotNull] TyhpParser.StatementContext context)
        {
            return context switch {
                _ when context.statementWithoutTerminal() != null => this.VisitStatementWithoutTerminal(context.statementWithoutTerminal()),
                _ when context.statementRequiringTerminal() != null => this.HandleWithStatementTerminal(
                    this.VisitStatementRequiringTerminal(context.statementRequiringTerminal()),
                    context.statementTerminal(),
                    context
                ),
                _ => HandleUnexpectedAlternative<IStatement>(context, "statement")
            };
        }
