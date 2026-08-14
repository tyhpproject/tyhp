using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp struct declaration (both named and anonymous).
    ///
    /// Grammar (named):
    ///   tyhpStructDeclarationStatement
    ///     : T_TYHP_STRUCT Identifier=T_STRING
    ///         GenericParameters=tyhpGenericParameterDeclarations?
    ///         (T_EXTENDS Extends=className)?
    ///         FindDocComment=T_OPEN_CURLY_BRACE PropertyList=tyhpStructPropertyList
    ///         T_CLOSE_CURLY_BRACE
    ///     ;
    ///
    /// Generic parameters (when present) are stored on AstGrammarAddons["identifier"]
    /// as a TyhpGenericsTypeArgumentListAst, matching class/interface declarations.
    ///
    /// Grammar (anonymous):
    ///   tyhpAnonymousStruct
    ///     : T_TYHP_STRUCT (T_EXTENDS Extends=className)? (T_OPEN_ROUND_BRACE
    ///         T_CLOSE_ROUND_BRACE)? FindDocComment=T_OPEN_CURLY_BRACE
    ///         PropertyList=tyhpStructPropertyList T_CLOSE_CURLY_BRACE
    ///     ;
    ///
    /// Used as:
    ///   - Top-level statement (via topStatementGrammarAddon #tyhpStructDecl)
    ///   - Tyhpdef statement (via tyhpdefStatement #tyhpdefStructDecl)
    ///   - Anonymous struct expression (via newDereferenceableGrammarAddon #tyhpNewAnonStructInstance)
    /// </summary>
    public class TyhpStructDeclAst : Base2Ast, ITopStatement
    {
        private const short IS_ANONYMOUS_FLAG = -1;

        /// <summary>
        /// The list of struct properties.
        /// </summary>
        public TyhpStructPropertyListAst? PropertyList => Children.ElementAtOrDefault(0) as TyhpStructPropertyListAst;

        /// <summary>
        /// Optional extends clause (the name of the parent struct/type).
        /// </summary>
        public PhpNameAst? Extends => Children.ElementAtOrDefault(1) as PhpNameAst;

        /// <summary>
        /// Whether this is an anonymous struct declaration (used with new keyword).
        /// </summary>
        public bool IsAnonymous => HasFlag(IS_ANONYMOUS_FLAG);

        public static TyhpStructDeclAst Create(
            string? name,
            TyhpStructPropertyListAst propertyList,
            PhpNameAst? extends,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpStructDeclAst
            {
                Identifier = name ?? "",
                Children = [propertyList, extends],
                DocComment = docComment,
            };
            if (string.IsNullOrWhiteSpace(name))
            {
                result.SetFlag(IS_ANONYMOUS_FLAG);
                result.Identifier = "anonStruct@" + Guid.NewGuid().ToString("N");
            }
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder for error recovery when ANTLR left required
        /// children null (e.g. truncated <c>struct Foo</c> with no body).
        /// </summary>
        public static TyhpStructDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpStructDeclAst
            {
                Identifier = "<error>",
                Children = [TyhpStructPropertyListAst.Create(null, context, languageMode), null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
