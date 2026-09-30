using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTraitMemberRefAst : Base2Ast
    {
        private const short IS_OPERATOR_FLAG = -32;

        public IClassName? TraitName => Children.ElementAtOrDefault(0) as IClassName;
        public IClassMemberName? MemberName => Children.ElementAtOrDefault(1) as IClassMemberName;

        /// <summary>
        /// <c>use extension</c> operator reference (<c>Foo::operator *&lt;string&gt;</c> or
        /// unqualified <c>operator *</c>).
        /// </summary>
        public bool IsOperator
        {
            get => HasFlag(IS_OPERATOR_FLAG);
            set => SetFlag(IS_OPERATOR_FLAG, value);
        }

        /// <summary>Operator token text such as <c>*</c> or <c>+</c>.</summary>
        public string? OperatorToken => IsOperator ? (ValueString ?? Identifier) : null;

        /// <summary>Optional <c>&lt;Type&gt;</c> on an operator adaptation.</summary>
        public ITypeExpression? OperatorTarget => Children.ElementAtOrDefault(2) as ITypeExpression;

        public static PhpTraitMemberRefAst Create(IClassMemberName memberName, ParserRuleContext context, string? languageMode = null)
            => Create(null, memberName, context, languageMode);

        public static PhpTraitMemberRefAst Create(IClassName? traitName, IClassMemberName memberName, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTraitMemberRefAst
            {
                Children = [traitName, memberName],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }

        public static PhpTraitMemberRefAst CreateOperator(
            IClassName? traitName,
            string operatorToken,
            ITypeExpression? targetType,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new PhpTraitMemberRefAst
            {
                Children = [traitName, null, targetType],
                Identifier = operatorToken,
                ValueString = operatorToken,
            };
            result.IsOperator = true;
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 