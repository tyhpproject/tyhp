using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// One parameter of a <c>callable(…): R</c> shape. Names are optional.
    /// <c>=</c> marks optional; a written default expression is stored for later
    /// discard and is not part of the type.
    /// </summary>
    public class TyhpCallableShapeParameterAst : Base2Ast
    {
        private const short IS_REF_FLAG = -9;
        private const short IS_VARIADIC_FLAG = -10;
        private const short IS_OPTIONAL_FLAG = -11;

        public ITypeExpression? TypeExpression =>
            Children.ElementAtOrDefault(0) as ITypeExpression;

        public PhpVariableAst? Variable =>
            Children.ElementAtOrDefault(1) as PhpVariableAst;

        public IExpression? DefaultValue =>
            Children.ElementAtOrDefault(2) as IExpression;

        public bool IsRef => HasFlag(IS_REF_FLAG);

        public bool IsVariadic => HasFlag(IS_VARIADIC_FLAG);

        public bool IsOptional => HasFlag(IS_OPTIONAL_FLAG);

        /// <summary>
        /// Written parameter name (<c>$i</c> or <c>i</c>), or null when the slot is unnamed.
        /// <see cref="PhpVariableAst.Identifier"/> is empty for token-backed variables; the
        /// name lives on <see cref="PhpVariableAst.VariableToken"/>.
        /// </summary>
        public string? SourceParameterName
        {
            get
            {
                var fromToken = Variable?.VariableToken?.ValueString;
                if (!string.IsNullOrEmpty(fromToken))
                {
                    return fromToken;
                }

                if (!string.IsNullOrEmpty(Identifier))
                {
                    return Identifier;
                }

                return null;
            }
        }

        public static TyhpCallableShapeParameterAst Create(
            ITypeExpression typeExpression,
            PhpVariableAst? variable,
            bool isRef,
            bool isVariadic,
            bool isOptional,
            IExpression? defaultValue,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpCallableShapeParameterAst
            {
                Identifier = variable?.Identifier ?? "",
                Children = [typeExpression, variable, defaultValue],
            };
            result.SetFlag(IS_REF_FLAG, isRef);
            result.SetFlag(IS_VARIADIC_FLAG, isVariadic);
            result.SetFlag(IS_OPTIONAL_FLAG, isOptional);
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
