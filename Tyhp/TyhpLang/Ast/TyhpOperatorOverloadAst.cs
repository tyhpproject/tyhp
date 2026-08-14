using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp operator overload declaration inside a class body.
    ///
    /// Grammar:
    ///   tyhpClassOperatorOverload
    ///     : Modifier=(T_ABSTRACT | T_FINAL)? T_TYHP_OPERATOR
    ///         Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
    ///         functionParametersGrammarAddon LeftParameter=parameter
    ///         (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
    ///         ConvertReturnType=returnType
    ///         (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr))
    ///     ;
    ///
    /// Used as a class member via classStatementGrammarAddon #tyhpClassOperatorOverload,
    /// as a tyhpdef inline extension operator, or inside an extension body with
    /// <c>operator +&lt;Target&gt;(...)</c> (<see cref="ExtensionTargetType"/> non-null).
    /// </summary>
    public class TyhpOperatorOverloadAst : Base2Ast, IClassMember, IExtensionMemberAst
    {
        private const short MODIFIER_OFFSET = 14000;

        /// <summary>
        /// For <c>extension</c> body operators with <c>operator +&lt;Type&gt;(...)</c>; null for class-level and tyhpdef inline operators.
        /// </summary>
        public ITypeExpression? ExtensionTargetType { get; set; }

        /// <summary>
        /// True when this overload is declared in an extension block with an explicit <c>&lt;Type&gt;</c> target.
        /// Note: this is narrower than <see cref="Symbols.ObjectOperatorOverloadMethodSymbol.IsExtensionOperator"/>
        /// on the symbol, which is also true for tyhpdef inline <c>extension operator</c> members.
        /// </summary>
        public bool IsExtensionOperator => this.ExtensionTargetType != null;

        /// <summary>
        /// True for <c>extension operator</c> inside a tyhpdef class body (target type is the enclosing class at bind time).
        /// </summary>
        public bool IsInlineExtension { get; set; }

        /// <summary>
        /// The operator token being overloaded (e.g., "+", "-", "==", "convert").
        /// </summary>
        public TokenValueAst? Op => Children.ElementAtOrDefault(0) as TokenValueAst;

        /// <summary>
        /// The left (or only) parameter of the operator overload.
        /// </summary>
        public PhpParameterAst? LeftParameter => Children.ElementAtOrDefault(1) as PhpParameterAst;

        /// <summary>
        /// The optional right parameter for binary operators.
        /// </summary>
        public PhpParameterAst? RightParameter => Children.ElementAtOrDefault(2) as PhpParameterAst;

        /// <summary>
        /// The return type of the operator overload.
        /// </summary>
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(3) as ITypeExpression;

        /// <summary>
        /// The method body. For shorthand expressions (=&gt; expr), this wraps
        /// the expression in a return statement.
        /// </summary>
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(4) as PhpStatementBlockAst;

        /// <summary>
        /// Optional modifier (Abstract or Final).
        /// </summary>
        public IEnumerable<PhpModifier> Modifiers => GetEnumFlags<PhpModifier>(MODIFIER_OFFSET);

        public static TyhpOperatorOverloadAst Create(
            TokenValueAst op,
            PhpParameterAst leftParam,
            PhpParameterAst? rightParam,
            ITypeExpression? returnType,
            PhpStatementBlockAst? body,
            PhpModifier? modifier,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpOperatorOverloadAst
            {
                Identifier = op.ValueString ?? "",
                Children = [op, leftParam, rightParam, returnType, body],
            };

            if (modifier.HasValue && modifier.Value != PhpModifier.None)
            {
                result.SetFlag(MODIFIER_OFFSET, modifier.Value);
            }

            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Placeholder used when required operator-overload children are missing after ANTLR recovery.
        /// </summary>
        public static TyhpOperatorOverloadAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpOperatorOverloadAst
            {
                Identifier = "<error>",
                Children =
                [
                    TokenValueAst.CreateError(context, languageMode),
                    PhpParameterAst.Create(
                        "<error>",
                        null,
                        false,
                        false,
                        null,
                        null,
                        null,
                        context,
                        languageMode),
                    null,
                    null,
                    null,
                ],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
