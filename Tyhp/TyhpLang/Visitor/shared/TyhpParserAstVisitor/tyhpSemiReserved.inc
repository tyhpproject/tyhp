// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpSemiReserved
        ///     : TokenValue=T_TYHP_ASYNC
        ///     | TokenValue=T_TYHP_OPERATOR
        ///     | TokenValue=T_TYHPDEF_DEPRECATED
        ///     | TokenValue=T_TYHPDEF_OBSOLETE
        ///     | TokenValue=T_TYHP_IS
        ///     ;
        /// </summary>
        public override TokenValueAst VisitTyhpSemiReserved([NotNull] TyhpParser.TyhpSemiReservedContext context)
            => this.GetTokenValueAst(context, context.TokenValue);
