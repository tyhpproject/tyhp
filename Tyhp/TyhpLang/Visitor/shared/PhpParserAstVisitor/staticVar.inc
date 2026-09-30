// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableAst VisitStaticVar([NotNull] TyhpParser.StaticVarContext context)
            => PhpVariableAst.Create(
                this.GetTokenValueAst(context, context.Variable),
                null,
                false,
                context.Expr != null ? this.VisitExpr(context.Expr) : null,
                context
            );
