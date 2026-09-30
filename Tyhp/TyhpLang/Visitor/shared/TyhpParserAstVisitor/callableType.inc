// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TyhpCallableShapeAst VisitCallableType(
            [NotNull] TyhpParser.CallableTypeContext context)
        {
            ITypeExpression returnType;
            if (context.ReturnType != null)
            {
                returnType = this.VisitTypeExpr(context.ReturnType);
            }
            else
            {
                this.ReportMissingRequired(context, "callableType.ReturnType");
                returnType = PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var isUnknownArity = context.WildcardEllipsis != null;
            TyhpCallableShapeParameterListAst parameters;
            if (context.BuiltinCast != null)
            {
                // PHP lexes `(int)` as T_INT_CAST, so unnamed `callable(int): R`
                // cannot match the parenthesized parameter-list alternative.
                var typeName = CastTokenTypeToTypeName(context.BuiltinCast.Type);
                var paramType = PhpBuiltinTypeAst.Create(typeName, context, GetCurrentLanguageMode(context));
                var parameter = TyhpCallableShapeParameterAst.Create(
                    paramType,
                    variable: null,
                    isRef: false,
                    isVariadic: false,
                    isOptional: false,
                    defaultValue: null,
                    context,
                    GetCurrentLanguageMode(context));
                parameters = TyhpCallableShapeParameterListAst.Create(
                    [parameter],
                    context,
                    GetCurrentLanguageMode(context));
            }
            else
            {
                parameters = context.Parameters != null
                    ? this.VisitCallableShapeParameterList(context.Parameters)
                    : TyhpCallableShapeParameterListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            return TyhpCallableShapeAst.Create(
                parameters,
                returnType,
                isUnknownArity,
                context,
                GetCurrentLanguageMode(context));
        }
