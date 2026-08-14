using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpUnaryOpAst : Base2Ast, IExpression, IStatement
    {
        private const short IS_PREFIX_FLAG = -1;
        
        public IExpression? Operand => Children.ElementAtOrDefault(1) as IExpression;
        
        public TokenValueAst? Operator => Children.ElementAtOrDefault(0) as TokenValueAst;
        
        public bool IsPrefix => HasFlag(IS_PREFIX_FLAG);

        public static PhpUnaryOpAst Create(TokenValueAst? op, IExpression? operand, ParserRuleContext context, string? languageMode = null)
        {
            return Create(op, operand, true, context, languageMode);
        }

        public static PhpUnaryOpAst Create(IExpression? operand, TokenValueAst? op, ParserRuleContext context, string? languageMode = null)
        {
            return Create(op, operand, false, context, languageMode);
        }
        
        public static PhpUnaryOpAst Create(TokenValueAst? op, IExpression? operand, bool isPrefix, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpUnaryOpAst
            {
                Children = [op, operand],
            };
            
            result.SetFlag(IS_PREFIX_FLAG, isPrefix);
            result.SetContext(context, languageMode);
            
            return result;
        }

        internal static PhpUnaryOpAst CreateFromContext(TokenValueAst? op, IExpression? operand, Base2Ast context)
        {
            var result = new PhpUnaryOpAst
            {
                Children = [op, operand],
            };
            result.SetFlag((short)(-1), true);
            result.SetContext(context);
            return result;
        }
    }
} 