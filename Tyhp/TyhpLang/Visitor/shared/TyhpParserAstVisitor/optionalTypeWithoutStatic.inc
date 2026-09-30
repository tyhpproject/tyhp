// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// optionalTypeWithoutStatic
        ///     : TypeExpr=typeExprWithoutStatic? {!this.isLanguageMode("tyhp")}?
        ///     | TypeExpr=typeExprWithoutStatic {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// In Tyhp mode, the type expression is required (not optional).
        /// The base implementation already handles both cases correctly by
        /// checking if TypeExpr is present.
        /// </summary>
        public override ITypeExpression? VisitOptionalTypeWithoutStatic([NotNull] TyhpParser.OptionalTypeWithoutStaticContext context)
            => base.VisitOptionalTypeWithoutStatic(context);
