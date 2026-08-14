using System.Globalization;
using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a single property declaration within a Tyhp struct.
    ///
    /// Grammar:
    ///   tyhpStructProperty
    ///     : TypeExpr=typeExprWithoutStatic
    ///         ((AliasOfString=T_CONSTANT_ENCAPSED_STRING | AliasOfInt=T_LNUMBER) T_AS)?
    ///         Property=property T_SYM_SEMICOLON
    ///     ;
    ///
    /// Example:
    ///   int $count;
    ///   string 'original_name' as $alias;
    ///   mixed 0 as $arg1;
    /// </summary>
    public class TyhpStructPropertyAst : Base2Ast
    {
        /// <summary>
        /// The type expression of the property (e.g., int, string, SomeClass)
        /// </summary>
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(0) as ITypeExpression;

        /// <summary>
        /// The property itself (variable name and optional default value)
        /// </summary>
        public PhpPropertyAst? Property => Children.ElementAtOrDefault(1) as PhpPropertyAst;

        /// <summary>
        /// Optional PHP array key alias source.
        /// String aliases keep their quotes (e.g. <c>'original_name'</c>);
        /// integer aliases are the raw decimal digits (e.g. <c>0</c>).
        /// </summary>
        public string? AliasOf => ValueString;

        /// <summary>
        /// True when <see cref="AliasOf"/> is a decimal integer array key
        /// (<c>mixed 0 as $arg1</c>), not a quoted string key.
        /// </summary>
        public bool IsNumericAlias =>
            ValueInt64 is not null
            && !string.IsNullOrEmpty(AliasOf)
            && AliasOf[0] != '\''
            && AliasOf[0] != '"';

        public static TyhpStructPropertyAst Create(
            ITypeExpression typeExpression,
            PhpPropertyAst property,
            string? aliasOf,
            bool aliasIsNumeric,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpStructPropertyAst
            {
                Identifier = property.Identifier ?? "",
                ValueString = aliasOf,
                Children = [typeExpression, property],
            };

            if (aliasIsNumeric
                && aliasOf is not null
                && long.TryParse(
                    aliasOf.Replace("_", string.Empty),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var numericKey))
            {
                result.ValueInt64 = numericKey;
            }

            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Backward-compatible factory for string aliases (or no alias).
        /// </summary>
        public static TyhpStructPropertyAst Create(
            ITypeExpression typeExpression,
            PhpPropertyAst property,
            string? aliasOf,
            ParserRuleContext context,
            string? languageMode = null)
            => Create(typeExpression, property, aliasOf, aliasIsNumeric: false, context, languageMode);
    }
}
