// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpStatementBlockAst? VisitMethodBody([NotNull] TyhpParser.MethodBodyContext context)
        {
            if (context.StatementList != null)
            {
                return this.VisitInnerStatementList(context.StatementList);
            }

            // Interface/abstract methods terminate with `;` and have no body. Model the absence as a
            // null body (rather than an empty block) so the checker's no-body branch — which exempts
            // interface and abstract methods from the "must return a value" rule — is reached.
            return null;
        }
