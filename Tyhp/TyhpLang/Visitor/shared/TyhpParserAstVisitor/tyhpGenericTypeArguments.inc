// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericTypeArguments
        ///     : T_SYM_LT GenericArgumentsList=tyhpGenericTypeArgumentList T_SYM_GT
        ///     ;
        /// </summary>
        public override PhpTypeExpressionListAst VisitTyhpGenericTypeArguments([NotNull] TyhpParser.TyhpGenericTypeArgumentsContext context)
        {
            if (context.GenericArgumentsList == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericTypeArguments.GenericArgumentsList");
                return PhpTypeExpressionListAst.Create(null, context);
            }

            return this.VisitTyhpGenericTypeArgumentList(context.GenericArgumentsList);
        }
