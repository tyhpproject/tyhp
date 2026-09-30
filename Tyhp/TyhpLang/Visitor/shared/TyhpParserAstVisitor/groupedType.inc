// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override ITypeExpression VisitGroupedType(
            [NotNull] TyhpParser.GroupedTypeContext context)
        {
            if (context.TypeExpr != null)
            {
                return this.VisitTypeExpr(context.TypeExpr);
            }

            this.ReportMissingRequired(context, "groupedType.TypeExpr");
            return PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
        }
