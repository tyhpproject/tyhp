using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpLoopAst : Base2Ast, IStatement
    {
        private const short LOOP_TYPE_OFFSET = 7000;
        
        public PhpLoopType LoopType => GetEnumFlags<PhpLoopType>(LOOP_TYPE_OFFSET).FirstOrDefault();
        public IExpression? Condition => Children.ElementAtOrDefault(0) as IExpression;
        public IBase2Ast? Body => Children.ElementAtOrDefault(1) as IBase2Ast;
        public IExpression? KeyVariable => Children.ElementAtOrDefault(2) as IExpression;
        public IExpression? ValueVariable => Children.ElementAtOrDefault(3) as IExpression;
        public PhpExpressionListAst? InitExpressions => Children.ElementAtOrDefault(4) as PhpExpressionListAst;
        public PhpExpressionListAst? TestExpressions => Children.ElementAtOrDefault(5) as PhpExpressionListAst;
        public PhpExpressionListAst? UpdateExpressions => Children.ElementAtOrDefault(6) as PhpExpressionListAst;


        public static PhpLoopAst CreateWhile(
            IExpression? condition,
            IBase2Ast? body,
            ParserRuleContext context,
            string? languageMode = null)
            => Create(PhpLoopType.While, condition, body, null, null, null, null, null, context, languageMode);
        
        public static PhpLoopAst CreateDoWhile(
            IExpression? condition,
            IBase2Ast? body,
            ParserRuleContext context,
            string? languageMode = null)
            => Create(PhpLoopType.DoWhile, condition, body, null, null, null, null, null, context, languageMode);

        public static PhpLoopAst CreateFor(
            IBase2Ast? body,
            PhpExpressionListAst? initExpressions,
            PhpExpressionListAst? testExpressions,
            PhpExpressionListAst? updateExpressions,
            ParserRuleContext context,
            string? languageMode = null)
            => Create(PhpLoopType.For, null, body, null, null, initExpressions, testExpressions, updateExpressions, context, languageMode);
        
        public static PhpLoopAst CreateForeach(
            IExpression? expr,
            IExpression? keyVariable,
            IExpression? valueVariable,
            IBase2Ast? body,
            ParserRuleContext context,
            string? languageMode = null)
            => Create(PhpLoopType.Foreach, expr, body, keyVariable, valueVariable, null, null, null, context, languageMode);

        public static PhpLoopAst Create(
            PhpLoopType loopType,
            IExpression? condition,
            IBase2Ast? body,
            IExpression? keyVariable,
            IExpression? valueVariable,
            PhpExpressionListAst? initExpressions,
            PhpExpressionListAst? testExpressions,
            PhpExpressionListAst? updateExpressions,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new PhpLoopAst
            {
                Children = [condition, body, keyVariable, valueVariable, initExpressions, testExpressions, updateExpressions],
            };
            result.SetContext(context, languageMode);
            result.SetFlag(LOOP_TYPE_OFFSET, loopType, true);
            return result;
        }
    }
}
