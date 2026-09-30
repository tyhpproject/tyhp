// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpConditionalArmAst VisitCaseDefault([NotNull] TyhpParser.CaseDefaultContext context)
            => PhpConditionalArmAst.Create(
                null,
                PhpStatementBlockAst.Create(
                    context.DefaultStatementList._Items?.Select(this.VisitInnerStatement),
                    context
                ),
                true,
                context
            );
