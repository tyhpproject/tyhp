// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum

        public override ITypeExpression? VisitOptionalTypeWithoutStatic([NotNull] TyhpParser.OptionalTypeWithoutStaticContext context)
            => context.TypeExpr != null ? this.VisitTypeExprWithoutStatic(context.TypeExpr) : null;
