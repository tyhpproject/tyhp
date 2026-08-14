using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef aliased identifier (original name aliased to a new name).
    ///
    /// Grammar variants:
    ///   tyhpdefIdentifierAlias:
    ///     Identifier=name T_AS AliasedAs=tyhpOptionalGenericIdentifier
    ///
    ///   tyhpdefClassMemberIdentifierAlias:
    ///     ClassName=className T_DOUBLE_COLON Identifier=tyhpOptionalGenericIdentifier
    ///       T_AS AliasedAs=tyhpOptionalGenericIdentifier
    /// </summary>
    public class TyhpdefIdentifierAliasAst : Base2Ast
    {
        /// <summary>
        /// Optional class name prefix (for class member aliases).
        /// </summary>
        public IClassName? ClassName => Children.ElementAtOrDefault(0) as IClassName;

        /// <summary>
        /// The original identifier.
        /// </summary>
        public IBase2Ast? OriginalIdentifier => Children.ElementAtOrDefault(1);

        /// <summary>
        /// The aliased-as identifier.
        /// </summary>
        public PhpNameAst? AliasedAs => Children.ElementAtOrDefault(2) as PhpNameAst;

        public static TyhpdefIdentifierAliasAst Create(
            IClassName? className,
            IBase2Ast originalIdentifier,
            PhpNameAst aliasedAs,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefIdentifierAliasAst
            {
                Identifier = aliasedAs.ValueString ?? aliasedAs.Identifier ?? "",
                ValueString = originalIdentifier.ValueString ?? originalIdentifier.Identifier ?? "",
                Children = [className, originalIdentifier, aliasedAs],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder TyhpdefIdentifierAliasAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static TyhpdefIdentifierAliasAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpdefIdentifierAliasAst
            {
                Identifier = "<error>",
                ValueString = "<error>",
                Children = [null, PhpNameAst.CreateError(context, languageMode), PhpNameAst.CreateError(context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
