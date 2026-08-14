using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpInlineFunctionAst : Base2Ast, IExpression
    {
        private const short RETURNS_REF_FLAG = -12;
        private const short IS_ARROW_FUNCTION_FLAG = -13;
        
        public bool ReturnsRef => this.HasFlag(RETURNS_REF_FLAG);
        public bool IsArrowFunction => this.HasFlag(IS_ARROW_FUNCTION_FLAG);
        
        public TokenValueListAst? Modifiers => Children.ElementAtOrDefault(0) as TokenValueListAst;
        public IBase2Ast? FunctionNameAddons => Children.ElementAtOrDefault(1);
        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(2) as PhpParameterListAst;
        public PhpVariableListAst? LexicalVars => Children.ElementAtOrDefault(3) as PhpVariableListAst;
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(4) as ITypeExpression;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(5) as PhpStatementBlockAst;

        public static PhpInlineFunctionAst Create(TokenValueListAst? modifiers, bool returnsRef, IBase2Ast? functionNameAddons, PhpParameterListAst? parameters, ITypeExpression? returnType, IExpression arrowExpression, ParserRuleContext context, string? languageMode = null)
        {
            return Create(
                modifiers,
                returnsRef,
                true,
                functionNameAddons,
                parameters,
                null,
                returnType,
                PhpStatementBlockAst.Create(
                    [PhpUnaryOpAst.Create(TokenValueAst.Create("return", TyhpParser.T_RETURN, context), arrowExpression, context, languageMode)],
                    context,
                    languageMode
                ),
                context,
                languageMode
            );
        }

        public static PhpInlineFunctionAst Create(TokenValueListAst? modifiers, bool returnsRef, IBase2Ast? functionNameAddons, PhpParameterListAst? parameters, ITypeExpression? returnType, PhpVariableListAst? lexicalVars, PhpStatementBlockAst body, ParserRuleContext context, string? languageMode = null)
        {
            return Create(
                modifiers,
                returnsRef,
                false,
                functionNameAddons,
                parameters,
                lexicalVars,
                returnType,
                body,
                context,
                languageMode
            );
        }

        public static PhpInlineFunctionAst Create(TokenValueListAst? modifiers, bool returnsRef, bool isArrowFunction, IBase2Ast? functionNameAddons, PhpParameterListAst? parameters, PhpVariableListAst? lexicalVars, ITypeExpression? returnType, PhpStatementBlockAst body, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpInlineFunctionAst
            {
                Children = [modifiers, functionNameAddons, parameters, lexicalVars, returnType, body],
            };

            result.SetContext(context, languageMode);

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);
            result.SetFlag(IS_ARROW_FUNCTION_FLAG, isArrowFunction);

            return result;
        }
    }
} 