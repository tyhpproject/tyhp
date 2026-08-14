using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef object type import declaration (class, trait, interface, or enum).
    ///
    /// Grammar (class variant):
    ///   tyhpdefImportClassDeclarationStatement
    ///     : tyhpdefDeprecatedOrObsolete? Modifiers=classModifiers? T_CLASS
    ///         Identifier=tyhpdefClassNameWithOptionalAlias Extends=extendsFrom
    ///         Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
    ///         StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    ///     ;
    ///
    /// Similar grammar for trait, interface, and enum variants.
    /// The DeclType token distinguishes between them.
    /// </summary>
    public class TyhpdefImportObjectDeclAst : Base2Ast, IAttributedStatement
    {
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;

        /// <summary>
        /// The declaration type token (T_CLASS, T_TRAIT, T_INTERFACE, T_ENUM).
        /// </summary>
        public TokenValueAst? DeclType => Children.ElementAtOrDefault(0) as TokenValueAst;

        /// <summary>
        /// Class modifiers (abstract, final, readonly). Only for class declarations.
        /// </summary>
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(1) as PhpModifierListAst;

        /// <summary>
        /// The identifier (possibly with alias and/or generics).
        /// </summary>
        public IBase2Ast? NameOrAlias => Children.ElementAtOrDefault(2);

        /// <summary>
        /// Extends clause (for class, trait) or extends list (for interface).
        /// </summary>
        public IBase2Ast? Extends => Children.ElementAtOrDefault(3);

        /// <summary>
        /// Implements clause (for class, enum).
        /// </summary>
        public PhpClassNameListAst? Implements => Children.ElementAtOrDefault(4) as PhpClassNameListAst;

        /// <summary>
        /// Enum backing type (for enum declarations).
        /// </summary>
        public ITypeExpression? BackingType => Children.ElementAtOrDefault(5) as ITypeExpression;

        /// <summary>
        /// The class body containing tyhpdef class members.
        /// </summary>
        public PhpClassBodyAst? Body => Children.ElementAtOrDefault(6) as PhpClassBodyAst;

        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);

        public static TyhpdefImportObjectDeclAst Create(
            TokenValueAst declType,
            PhpModifierListAst? modifiers,
            IBase2Ast nameOrAlias,
            IBase2Ast? extends,
            PhpClassNameListAst? implements,
            ITypeExpression? backingType,
            PhpClassBodyAst? body,
            bool isDeprecated,
            bool isObsolete,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefImportObjectDeclAst
            {
                Identifier = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                    ? nameOrAlias.Identifier
                    : (nameOrAlias.ValueString ?? ""),
                Children = [declType, modifiers, nameOrAlias, extends, implements, backingType, body],
                DocComment = docComment,
            };

            result.SetFlag(IS_DEPRECATED_FLAG, isDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, isObsolete);
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
