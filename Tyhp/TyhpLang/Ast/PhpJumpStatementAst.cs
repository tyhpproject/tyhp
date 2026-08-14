using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpJumpStatementAst : Base2Ast, IStatement
    {
        private const short JUMP_TYPE_OFFSET = 16000;
        public PhpJumpType JumpType => GetEnumFlags<PhpJumpType>(JUMP_TYPE_OFFSET).FirstOrDefault();

        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;

        public static PhpJumpStatementAst Create(PhpJumpType jumpType, IExpression? expression, ParserRuleContext context, string? languageMode = null)
            => Create(jumpType, expression, null, context, languageMode);

        public static PhpJumpStatementAst CreateGoto(string? label, ParserRuleContext context, string? languageMode = null)
            => Create(PhpJumpType.Goto, null, label, context, languageMode);
        
        public static PhpJumpStatementAst Create(PhpJumpType jumpType, IExpression? expression, string? label, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpJumpStatementAst
            {
                Children = [ expression ],
                Identifier = label ?? "",
            };

            result.SetContext(context, languageMode);
            result.SetFlag(JUMP_TYPE_OFFSET, jumpType, true);
            return result;
        }
    }
} 