using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpMagicConstantAst : TokenValueAst, IExpression, IScalar, IDereferenceableBase
    {
        public new static PhpMagicConstantAst Create(IToken token, ParserRuleContext context, string? languageMode = null)
            => CreateBase<PhpMagicConstantAst>(token, context, languageMode);

        public new static PhpMagicConstantAst Create(string tokenText, int tokenType, ParserRuleContext context, string? languageMode = null)
            => CreateBase<PhpMagicConstantAst>(tokenText, tokenType, context, languageMode);

        /// <summary>
        /// Creates an error placeholder PhpMagicConstantAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public new static PhpMagicConstantAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var ast = new PhpMagicConstantAst
            {
                ValueString = "<error>",
                ValueInt64 = -1,
            };
            ast.SetContext(context, languageMode);
            return ast;
        }
    }
} 