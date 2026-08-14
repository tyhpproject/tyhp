using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Ast
{
    public class TyhpGenericIdentifierAst : PhpNameAst
    {
        /// <summary>
        /// Generic type arguments (usage) from <c>tyhpGenericTypeArguments</c>,
        /// or generic parameter declarations from <c>tyhpGenericParameterDeclarations</c>
        /// when the identifier appears in a declaration context.
        /// </summary>
        public Base2Ast? GenericArguments => Children.ElementAtOrDefault(0) as Base2Ast;

        public static TyhpGenericIdentifierAst Create(IToken token, Base2Ast? genericArguments, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpGenericIdentifierAst
            {
                ValueString = token.Text,
                ValueInt64 = (long)token.Type,
                Children = [genericArguments],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        public static TyhpGenericIdentifierAst Create(string name, int tokenType, Base2Ast? genericArguments, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpGenericIdentifierAst
            {
                ValueString = name,
                ValueInt64 = (long)tokenType,
                Children = [genericArguments],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>Clone with a rewritten name while preserving generic type arguments and source context.</summary>
        internal static TyhpGenericIdentifierAst CreateFromContext(
            string name,
            Base2Ast? genericArguments,
            Base2Ast context)
        {
            var result = new TyhpGenericIdentifierAst
            {
                ValueString = name,
                ValueInt64 = 0,
                Children = [genericArguments],
            };
            result.SetContext(context);
            return result;
        }
    }
}
