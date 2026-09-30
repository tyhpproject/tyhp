// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum

        public override PhpConstDeclAst VisitConstDecl([NotNull] TyhpParser.ConstDeclContext context)
        {
            var docComment = this.FindPossibleDocComment(context.Start);

            return PhpConstDeclAst.Create(
                context.Identifier.Text,
                this.VisitExpr(context.ValueExpr),
                docComment,
                context,
                type: context.TypeExpr != null
                    ? this.VisitTypeExprWithoutStatic(context.TypeExpr)
                    : null
            );
        }
