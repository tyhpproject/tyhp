using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp type alias declaration: `type Alias&lt;T&gt; = SomeType;`
    ///
    /// Grammar:
    ///   tyhpTypeAlias
    ///     : T_TYHP_TYPE_ALIAS Identifier=name
    ///         GenericArguments=tyhpGenericParameterDeclarations? T_SYM_EQUAL
    ///         TypeExpr=typeExpr T_SYM_SEMICOLON
    ///     ;
    ///
    /// Used as:
    ///   - Top-level statement (via topStatementGrammarAddon #tyhpTypeAliasDecl)
    ///   - Class member (via classStatementGrammarAddon #tyhpClassTypeAlias, with optional modifiers)
    ///   - Tyhpdef statement (via tyhpdefTopStatement #tyhpdefTypeAliasDecl)
    /// </summary>
    public class TyhpTypeAliasAst : Base2Ast, ITopStatement, IClassMember
    {
        /// <summary>
        /// The name of the type alias (e.g., "MyAlias")
        /// </summary>
        public PhpNameAst? Name => Children.ElementAtOrDefault(0) as PhpNameAst;

        /// <summary>
        /// Optional generic type arguments (e.g., &lt;T, U extends Foo&gt;)
        /// </summary>
        public TyhpGenericsTypeArgumentListAst? GenericArguments => Children.ElementAtOrDefault(1) as TyhpGenericsTypeArgumentListAst;

        /// <summary>
        /// The type expression being aliased (e.g., int|string, SomeClass&lt;T&gt;)
        /// </summary>
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(2) as ITypeExpression;

        /// <summary>
        /// Optional member modifiers (only present when used inside a class body)
        /// </summary>
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(3) as PhpModifierListAst;

        public static TyhpTypeAliasAst Create(
            PhpNameAst name,
            TyhpGenericsTypeArgumentListAst? genericArguments,
            ITypeExpression typeExpression,
            ParserRuleContext context,
            PhpModifierListAst? modifiers = null,
            string? languageMode = null)
        {
            var result = new TyhpTypeAliasAst
            {
                Identifier = name.ValueString ?? "",
                Children = [name, genericArguments, typeExpression, modifiers],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder for error recovery when ANTLR left required
        /// children null (e.g. truncated <c>type Foo</c> with no <c>= &lt;type&gt;;</c>).
        /// </summary>
        public static TyhpTypeAliasAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpTypeAliasAst
            {
                Identifier = "<error>",
                Children =
                [
                    PhpNameAst.CreateError(context, languageMode),
                    null,
                    PhpTypeExpressionAst.CreateError(context, languageMode),
                    null,
                ],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
