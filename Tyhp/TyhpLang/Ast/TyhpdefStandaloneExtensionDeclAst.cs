using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a standalone tyhpdef <c>extension Name { ... }</c> declaration (Story 20).
    /// Unlike <see cref="TyhpExtensionDeclAst"/> (<c>.tyhp</c> source), every member uses the
    /// short <c>fn ... =&gt; ...;</c> form only and is implicitly inline. The header target
    /// and generic parameters use the same grammar addons as
    /// <see cref="TyhpExtensionDeclAst.TargetType"/> /
    /// <see cref="TyhpExtensionDeclAst.GenericParameters"/>. Nested
    /// <c>extends Type { }</c> groups are <see cref="TyhpExtensionDeclAst"/> nodes with
    /// <see cref="TyhpExtensionDeclAst.IsTargetGroup"/> set.
    ///
    /// Grammar:
    ///   tyhpdefStandaloneExtensionDeclarationStatement
    ///     : tyhpdefDeprecatedOrObsolete? T_TYHP_EXTENSION Identifier=T_STRING
    ///         GenericParameters=tyhpGenericParameterDeclarations?
    ///         (T_EXTENDS TargetType=typeExprWithoutStatic)?
    ///         FindDocComment=T_OPEN_CURLY_BRACE
    ///         StatementList=tyhpdefStandaloneExtensionMemberList
    ///         T_CLOSE_CURLY_BRACE
    ///     ;
    ///
    /// Full binder semantics (activation via <c>use extension</c> / <c>global use extension</c>,
    /// `hide`/`insteadof`) are Phase 6 / Story 21 work; this node only carries the parsed shape.
    /// </summary>
    public class TyhpdefStandaloneExtensionDeclAst : Base2Ast, IStatement
    {
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;

        /// <summary>Extension body members (short-form functions, operators, and target groups).</summary>
        public TyhpExtensionFunctionListAst? FunctionList => Children.ElementAtOrDefault(0) as TyhpExtensionFunctionListAst;

        /// <summary>Header <c>extends Type</c>. Null when the declaration has no header target.</summary>
        public ITypeExpression? TargetType
        {
            get => GrammarAddons.TryGetValue(TyhpExtensionDeclAst.TargetTypeAddonKey, out var node)
                ? node as ITypeExpression
                : null;
            set
            {
                if (value is null)
                {
                    GrammarAddons.Remove(TyhpExtensionDeclAst.TargetTypeAddonKey);
                }
                else
                {
                    GrammarAddons[TyhpExtensionDeclAst.TargetTypeAddonKey] = value;
                }
            }
        }

        /// <summary>Type parameters on <c>extension Name&lt;T&gt;</c>.</summary>
        public TyhpGenericsTypeArgumentListAst? GenericParameters
        {
            get => GrammarAddons.TryGetValue(TyhpExtensionDeclAst.GenericParametersAddonKey, out var node)
                ? node as TyhpGenericsTypeArgumentListAst
                : null;
            set
            {
                if (value is null)
                {
                    GrammarAddons.Remove(TyhpExtensionDeclAst.GenericParametersAddonKey);
                }
                else
                {
                    GrammarAddons[TyhpExtensionDeclAst.GenericParametersAddonKey] = value;
                }
            }
        }

        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);

        public static TyhpdefStandaloneExtensionDeclAst Create(
            string name,
            TyhpExtensionFunctionListAst functionList,
            bool isDeprecated,
            bool isObsolete,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefStandaloneExtensionDeclAst
            {
                Identifier = name,
                Children = [functionList],
                DocComment = docComment,
            };

            result.SetFlag(IS_DEPRECATED_FLAG, isDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, isObsolete);
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder for error recovery when ANTLR left required children null.
        /// </summary>
        public static TyhpdefStandaloneExtensionDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpdefStandaloneExtensionDeclAst
            {
                Identifier = "<error>",
                Children = [TyhpExtensionFunctionListAst.Create(null, context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
