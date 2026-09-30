// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        /// <summary>
        /// Visits the operator token in an operator overload declaration.
        ///
        /// Grammar:
        ///   tyhpClassOperatorOverloadOp
        ///     : TokenValue=T_SYM_PLUS
        ///     | TokenValue=T_SYM_MINUS
        ///     | ... (many operator token alternatives)
        ///     | TokenValue=T_STRING   // for true, false, empty, null, convert
        ///     ;
        ///
        /// Returns a TokenValueAst holding the operator's text and token type.
        /// </summary>
        public override TokenValueAst VisitTyhpClassOperatorOverloadOp([NotNull] TyhpParser.TyhpClassOperatorOverloadOpContext context)
        {
            if (context.TokenValue == null)
            {
                this.ReportMissingRequired(context, "tyhpClassOperatorOverloadOp.TokenValue");
                return TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return context.TokenValue.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT && context.IsSR?.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT ?
                TokenValueAst.Create(">>", TyhpLang.Parser.TyhpParser.T_SR, context) :
                this.GetTokenValueAst(context, context.TokenValue);
        }
