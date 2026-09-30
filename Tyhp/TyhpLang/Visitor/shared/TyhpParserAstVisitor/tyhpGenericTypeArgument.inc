// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericTypeArgument
        ///     : TypeExpr=typeExpr PostfixEllipsis=T_ELLIPSIS?
        ///     | WildcardEllipsis=T_ELLIPSIS
        ///     ;
        /// </summary>
        public override ITypeExpression VisitTyhpGenericTypeArgument([NotNull] TyhpParser.TyhpGenericTypeArgumentContext context)
        {
            if (context.WildcardEllipsis != null)
            {
                return TyhpEllipsisTypeAst.Create(context, GetCurrentLanguageMode(context));
            }

            if (context.TypeExpr == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericTypeArgument.TypeExpr");
                return PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var inner = this.VisitTypeExpr(context.TypeExpr);
            if (context.PostfixEllipsis != null)
            {
                return TyhpPostfixEllipsisTypeAst.Create(inner, context, GetCurrentLanguageMode(context));
            }

            return inner;
        }
