// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits one item of <see cref="VisitTyhpObjectShapeIntersection"/>: either a
        /// nominal type or an <c>object { … }</c> shape.
        /// </summary>
        public override ITypeExpression VisitTyhpObjectShapeIntersectionItem(
            [NotNull] TyhpParser.TyhpObjectShapeIntersectionItemContext context)
        {
            if (context.ObjectShape != null)
            {
                return this.VisitTyhpObjectShape(context.ObjectShape);
            }
            else if (context.NominalType != null)
            {
                return this.VisitType(context.NominalType);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "tyhpObjectShapeIntersectionItem");
                return ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }
        }
