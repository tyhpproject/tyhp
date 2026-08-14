using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpNameAst : TokenValueAst, IExpression, IMemberAccessor, IClassName, IScalar, IClassMemberName, IDereferenceableBase
    {
        public new static PhpNameAst Create(IToken token, ParserRuleContext context, string? languageMode = null)
            => CreateBase<PhpNameAst>(token, context, languageMode);

        public new static PhpNameAst Create(string tokenText, int tokenType, ParserRuleContext context, string? languageMode = null)
            => CreateBase<PhpNameAst>(tokenText, tokenType, context, languageMode);

        /// <summary>
        /// Creates an error placeholder PhpNameAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public new static PhpNameAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var ast = new PhpNameAst
            {
                ValueString = "<error>",
                ValueInt64 = -1,
            };
            ast.SetContext(context, languageMode);
            return ast;
        }

        internal static PhpNameAst CreateFromContext(string name, Base2Ast context)
        {
            var ast = new PhpNameAst
            {
                ValueString = name,
                ValueInt64 = TyhpLang.Parser.TyhpParser.T_STRING,
            };
            ast.SetContext(context);
            return ast;
        }
    }
} 