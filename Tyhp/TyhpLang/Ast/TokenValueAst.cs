using Antlr4.Runtime;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Visitor;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class TokenValueAst : Base2Ast
    {
        public int TokenValue => ValueInt64 is long value ? (int)value : -1;

        public static TokenValueAst Create(IToken token, ParserRuleContext context, string? languageMode = null)
            => CreateBase<TokenValueAst>(token, context, languageMode);

        public static TokenValueAst Create(string tokenText, int tokenType, ParserRuleContext context, string? languageMode = null)
            => CreateBase<TokenValueAst>(tokenText, tokenType, context, languageMode);

        protected static TConcrete CreateBase<TConcrete>(IToken token, ParserRuleContext context, string? languageMode = null)
            where TConcrete : TokenValueAst, new()
        {
            var ast = new TConcrete
            {
                ValueString = token.Text,
                ValueInt64 = (long)token.Type,
            };
            ast.SetContext(context, languageMode);
            return ast;
        }

        protected static TConcrete CreateBase<TConcrete>(string tokenText, int tokenType, ParserRuleContext context, string? languageMode = null)
            where TConcrete : TokenValueAst, new()
        {
            var ast = new TConcrete
            {
                ValueString = tokenText,
                ValueInt64 = (long)tokenType,
            };
            ast.SetContext(context, languageMode);
            return ast;
        }

        /// <summary>
        /// Creates an error placeholder TokenValueAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static TokenValueAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var ast = new TokenValueAst
            {
                ValueString = "<error>",
                ValueInt64 = -1,
            };
            ast.SetContext(context, languageMode);
            return ast;
        }

        internal static TokenValueAst CreateFromContext(string text, long tokenType, Base2Ast context)
        {
            var ast = new TokenValueAst
            {
                ValueString = text,
                ValueInt64 = tokenType,
            };
            ast.SetContext(context);
            return ast;
        }
    }
}