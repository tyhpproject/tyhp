// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        public override IForeachVariable VisitForeachVariable([NotNull] TyhpParser.ForeachVariableContext context)
        {
            if (context.TypedVariable != null)
            {
                // Tyhp typed binding: Type &?$var
                return PhpVariableAst.Create(
                    this.VisitSimpleVariable(context.TypedVariable),
                    context.IsRef != null,
                    context,
                    type: context.TypeExpr != null
                        ? this.VisitTypeExprWithoutStatic(context.TypeExpr)
                        : null);
            }

            if (context.Variable != null)
            {
                // Regular variable: &?$var
                return PhpVariableAst.Create(
                    this.VisitVariable(context.Variable), 
                    context.IsRef != null, 
                    context
                );
            }
            else if (context.ArrayPairList != null)
            {
                return this.VisitArrayPairList(context.ArrayPairList);
            }

            this.ReportUnexpectedAlternative(context, "foreachVariable");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
