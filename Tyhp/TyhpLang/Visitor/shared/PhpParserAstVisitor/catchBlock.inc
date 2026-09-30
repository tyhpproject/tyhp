// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpCatchClauseAst VisitCatchBlock([NotNull] TyhpParser.CatchBlockContext context)
            => PhpCatchClauseAst.Create(
                this.VisitCatchNameList(context.CatchNameList),
                this.VisitOptionalVariable(context.Variable),
                this.VisitInnerStatementList(context.StatementList),
                context
            );
