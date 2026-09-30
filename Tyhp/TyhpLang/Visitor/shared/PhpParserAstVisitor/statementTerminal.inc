// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpInlineOutputListAst? VisitStatementTerminal([NotNull] TyhpParser.StatementTerminalContext context)
            => context switch
            {
                _ when context.InlineOutput != null => this.VisitPhpInlineOutputStatement(context.InlineOutput),
                _ => null
            };
