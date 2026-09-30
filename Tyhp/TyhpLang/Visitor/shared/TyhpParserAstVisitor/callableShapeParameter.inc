// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TyhpCallableShapeParameterAst VisitCallableShapeParameter(
            [NotNull] TyhpParser.CallableShapeParameterContext context)
        {
            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExpr(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "callableShapeParameter.TypeExpr");
                typeExpr = PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpVariableAst? variable = context.Variable != null
                ? PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context)
                : null;

            IExpression? defaultValue = context.DefaultExpr != null
                ? this.VisitExpr(context.DefaultExpr)
                : null;

            return TyhpCallableShapeParameterAst.Create(
                typeExpr,
                variable,
                isRef: context.IsRef != null,
                isVariadic: context.Ellipsis != null,
                isOptional: context.T_SYM_EQUAL() != null,
                defaultValue,
                context,
                GetCurrentLanguageMode(context));
        }
