using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp extension declaration, and a nested <c>extends Type { }</c>
    /// group inside one (<see cref="IsTargetGroup"/>).
    ///
    /// Grammar:
    ///   tyhpExtensionDeclarationStatement
    ///     : T_TYHP_INTERNAL? T_TYHP_EXTENSION Identifier=T_STRING
    ///         GenericParameters=tyhpGenericParameterDeclarations?
    ///         (T_EXTENDS TargetType=typeExprWithoutStatic)?
    ///         FindDocComment=T_OPEN_CURLY_BRACE FunctionList=tyhpExtensionFunctionList
    ///         T_CLOSE_CURLY_BRACE
    ///     ;
    ///
    /// The header target and the name's generic parameters are grammar addons
    /// (<see cref="TargetTypeAddonKey"/>, <see cref="GenericParametersAddonKey"/>).
    /// A nested group is the same node with <see cref="IsTargetGroup"/> set, an empty
    /// <see cref="Base2Ast.Identifier"/>, and its own target and member list. Groups
    /// sit in the enclosing extension's <see cref="FunctionList"/>.
    ///
    /// Used as a top-level statement via topStatementGrammarAddon #tyhpExtensionDecl.
    /// </summary>
    public class TyhpExtensionDeclAst : Base2Ast, IStatement, IExtensionMemberAst
    {
        /// <summary>Grammar-addon key for the header or group <c>extends</c> target.</summary>
        public const string TargetTypeAddonKey = "extensionTargetType";

        /// <summary>Grammar-addon key for <c>extension Name&lt;T&gt;</c> or <c>extends&lt;T&gt;</c>.</summary>
        public const string GenericParametersAddonKey = "GenericParameters";

        /// <summary>
        /// Grammar-addon key on a member when the source wrote the by-ref receiver
        /// annotation <c>&amp;$this</c>. Payload is the <c>$this</c> token.
        /// </summary>
        public const string ByRefReceiverAddonKey = "byRefReceiver";

        private const short IS_TARGET_GROUP_FLAG = -1;

        /// <summary>
        /// Extension body members: functions, operator overloads, and nested target groups.
        /// A target group's list contains functions and operators only.
        /// </summary>
        public TyhpExtensionFunctionListAst? FunctionList => Children.ElementAtOrDefault(0) as TyhpExtensionFunctionListAst;

        /// <summary>
        /// True for a nested <c>extends Type { }</c> group. False for a top-level
        /// <c>extension Name</c> declaration.
        /// </summary>
        public bool IsTargetGroup
        {
            get => HasFlag(IS_TARGET_GROUP_FLAG);
            set => SetFlag(IS_TARGET_GROUP_FLAG, value);
        }

        /// <summary>
        /// Header <c>extends Type</c>, or the target of a nested group. Null when the
        /// declaration has no header target.
        /// </summary>
        public ITypeExpression? TargetType
        {
            get => GrammarAddons.TryGetValue(TargetTypeAddonKey, out var node)
                ? node as ITypeExpression
                : null;
            set
            {
                if (value is null)
                {
                    GrammarAddons.Remove(TargetTypeAddonKey);
                }
                else
                {
                    GrammarAddons[TargetTypeAddonKey] = value;
                }
            }
        }

        /// <summary>
        /// Type parameters on <c>extension Name&lt;T&gt;</c> or <c>extends&lt;T&gt; Type</c>.
        /// </summary>
        public TyhpGenericsTypeArgumentListAst? GenericParameters
        {
            get => GrammarAddons.TryGetValue(GenericParametersAddonKey, out var node)
                ? node as TyhpGenericsTypeArgumentListAst
                : null;
            set
            {
                if (value is null)
                {
                    GrammarAddons.Remove(GenericParametersAddonKey);
                }
                else
                {
                    GrammarAddons[GenericParametersAddonKey] = value;
                }
            }
        }

        public static TyhpExtensionDeclAst Create(
            string name,
            TyhpExtensionFunctionListAst functionList,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpExtensionDeclAst
            {
                Identifier = name,
                Children = [functionList],
                DocComment = docComment,
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder for error recovery when ANTLR left required
        /// children null (e.g. truncated <c>extension Foo</c> with no <c>{ … }</c>).
        /// </summary>
        public static TyhpExtensionDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpExtensionDeclAst
            {
                Identifier = "<error>",
                Children = [TyhpExtensionFunctionListAst.Create(null, context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
