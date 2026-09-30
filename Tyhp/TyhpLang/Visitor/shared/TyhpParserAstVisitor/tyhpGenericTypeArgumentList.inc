// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericTypeArgumentList
        ///     : Items+=tyhpGenericTypeArgument (T_SYM_COMMA Items+=tyhpGenericTypeArgument)*
        ///     ;
        /// </summary>
        public override PhpTypeExpressionListAst VisitTyhpGenericTypeArgumentList([NotNull] TyhpParser.TyhpGenericTypeArgumentListContext context)
            => PhpTypeExpressionListAst.Create(
                context._Items?.Select(item => this.VisitTyhpGenericTypeArgument(item)),
                context
            );
