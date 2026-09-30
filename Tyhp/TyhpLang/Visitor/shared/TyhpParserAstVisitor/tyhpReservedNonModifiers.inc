// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// tyhpReservedNonModifiers
        ///     : TokenValue=T_TYHP_TYPE_ALIAS
        ///     | TokenValue=T_TYHP_AWAIT
        ///     | TokenValue=T_TYHP_WITH
        ///     | TokenValue=T_TYHP_OPERATOR
        ///     | TokenValue=T_TYHP_VOID
        ///     | TokenValue=T_TYHP_PARENT
        ///     | TokenValue=T_TYHP_EXTENSION
        ///     ;
        /// </summary>
        public override TokenValueAst VisitTyhpReservedNonModifiers([NotNull] TyhpParser.TyhpReservedNonModifiersContext context)
            => this.GetTokenValueAst(context, context.TokenValue);
