// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits a type-position / alias-RHS <c>struct { … }</c> shape.
        /// </summary>
        public override TyhpStructShapeAst VisitTyhpStructShape(
            [NotNull] TyhpParser.TyhpStructShapeContext context)
        {
            TyhpStructPropertyListAst propertyList;
            if (context.PropertyList != null)
            {
                propertyList = this.VisitTyhpStructPropertyList(context.PropertyList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpStructShape.PropertyList");
                propertyList = TyhpStructPropertyListAst.Create(null, context);
            }

            return TyhpStructShapeAst.Create(
                propertyList,
                context.Extends != null ? this.VisitClassName(context.Extends) : null,
                context,
                GetCurrentLanguageMode(context));
        }
