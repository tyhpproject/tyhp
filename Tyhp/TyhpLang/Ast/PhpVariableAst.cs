using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpVariableAst : Base2Ast, IExpression, IForeachVariable, IClassMemberName, IDereferenceableBase
    {
        private const short IS_REF_FLAG = -9;

        public TokenValueAst? VariableToken => Children.ElementAtOrDefault(0) as TokenValueAst;

        // Expression can be string for simple vars or expression for $$var
        public IExpression? VariableExpression => Children.ElementAtOrDefault(1) as IExpression;

        public IExpression? DefaultValue => Children.ElementAtOrDefault(2) as IExpression;

        public bool IsRef => HasFlag(IS_REF_FLAG);
        
        public static PhpVariableAst Create(IExpression expression, bool isRef, ParserRuleContext context, string? languageMode = null)
            => Create(null, expression, isRef, null, context, languageMode);

        public static PhpVariableAst Create(IExpression expression, bool isRef, IExpression? defaultValue, ParserRuleContext context, string? languageMode = null)
            => Create(null, expression, isRef, defaultValue, context, languageMode);

        public static PhpVariableAst Create(TokenValueAst variableToken, bool isRef, ParserRuleContext context, string? languageMode = null)
            => Create(variableToken, null, isRef, null, context, languageMode);

        public static PhpVariableAst Create(TokenValueAst variableToken, bool isRef, IExpression? defaultValue, ParserRuleContext context, string? languageMode = null)
            => Create(variableToken, null, isRef, defaultValue, context, languageMode);
        
        public static PhpVariableAst Create(TokenValueAst? variableToken, IExpression? expression, bool isRef, IExpression? defaultValue, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpVariableAst
            {
                Children = [variableToken, expression, defaultValue],
            };
            result.SetContext(context, languageMode);
            result.SetFlag(IS_REF_FLAG, isRef);
            return result;
        }

        /// <summary>Creates a simple <c>$name</c> variable for emitter synthesis (no parse context).</summary>
        internal static PhpVariableAst CreateFromContext(string variableName, Base2Ast context, bool isRef = false)
        {
            var name = variableName.StartsWith('$') ? variableName : "$" + variableName;
            var token = TokenValueAst.CreateFromContext(name, TyhpLang.Parser.TyhpParser.T_VARIABLE, context);
            var result = new PhpVariableAst
            {
                Children = [token, null, null],
            };
            result.SetContext(context);
            result.SetFlag(IS_REF_FLAG, isRef);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpVariableAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpVariableAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpVariableAst
            {
                Children = [TokenValueAst.CreateError(context, languageMode), null, null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 