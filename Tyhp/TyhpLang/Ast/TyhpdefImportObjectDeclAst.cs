using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef object type import declaration (class, trait, interface, or enum),
    /// including name-only <c>extern</c> placeholders (same node; <see cref="IsExtern"/>).
    ///
    /// Grammar (class variant): brace form (optional <c>partial</c>, member merge)
    /// or overlay header-only <c>partial class Name …;</c> (<see cref="IsHeaderOnly"/>).
    /// Similar grammar for trait, interface, and enum variants.
    /// Name-only placeholders: <c>extern class|interface|enum Name;</c> or kind-unspecified
    /// <c>extern \Name;</c> (<c>tyhpdefExtern*DeclarationStatement</c>). The DeclType token
    /// distinguishes them (<c>extern</c> for the bare form).
    /// </summary>
    public class TyhpdefImportObjectDeclAst : Base2Ast, IAttributedStatement
    {
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;
        private const short IS_PARTIAL_FLAG = -22;
        private const short IS_OMIT_FLAG = -23;
        private const short IS_EXTERN_FLAG = -24;

        internal const string ProvidedByAddonKey = "providedBy";

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
        /// The class body containing tyhpdef class members. Null for overlay
        /// header-only <c>partial class Name …;</c> (no braces).
        /// </summary>
        public PhpClassBodyAst? Body => Children.ElementAtOrDefault(6) as PhpClassBodyAst;

        /// <summary>
        /// Overlay header-only form: <c>partial class Foo&lt;T&gt; implements …;</c>.
        /// Body is null; binder replaces written header clauses and merges attributes.
        /// </summary>
        public bool IsHeaderOnly => IsPartial && !IsExtern && Body == null;

        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);
        public bool IsOmit => HasFlag(IS_OMIT_FLAG);

        /// <summary>
        /// <c>partial class</c> / <c>partial trait</c> / <c>partial interface</c> / <c>partial enum</c>
        /// (Story 20 decision 15). Include load is additive; overlay load last-wins.
        /// </summary>
        public bool IsPartial => HasFlag(IS_PARTIAL_FLAG);

        /// <summary>
        /// Name-only tyhpdef placeholder: <c>extern class</c> / <c>extern interface</c> /
        /// <c>extern enum</c> / kind-unspecified <c>extern \Name;</c>. Empty body, no
        /// extends/implements. Not a usable real type; binder merge (real-wins) is a later pass.
        /// </summary>
        public bool IsExtern => HasFlag(IS_EXTERN_FLAG);

        /// <summary>
        /// Optional wrapper package from the immediately preceding
        /// <c>// @provided-by:</c> comment (trimmed). Null when absent.
        /// Stored as grammar addon <see cref="ProvidedByAddonKey"/> so AST cache round-trips keep it.
        /// </summary>
        public string? ProvidedBy
            => AstGrammarAddons.TryGetValue(ProvidedByAddonKey, out var node)
                && !string.IsNullOrEmpty(node.ValueString)
                    ? node.ValueString
                    : null;

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
            string? languageMode = null,
            bool isPartial = false,
            bool isOmit = false,
            bool isExtern = false)
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
            result.SetFlag(IS_PARTIAL_FLAG, isPartial);
            result.SetFlag(IS_OMIT_FLAG, isOmit);
            result.SetFlag(IS_EXTERN_FLAG, isExtern);
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Shallow copy of this header (children and addons shared) so overlay
        /// attribute merge does not mutate a cached Layer 1 AST.
        /// </summary>
        internal TyhpdefImportObjectDeclAst CloneHeader()
        {
            var result = new TyhpdefImportObjectDeclAst
            {
                Identifier = Identifier,
                Children = [DeclType, Modifiers, NameOrAlias, Extends, Implements, BackingType, Body],
                DocComment = DocComment,
            };
            result.SetFlag(IS_DEPRECATED_FLAG, IsDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, IsObsolete);
            result.SetFlag(IS_PARTIAL_FLAG, IsPartial);
            result.SetFlag(IS_OMIT_FLAG, IsOmit);
            result.SetFlag(IS_EXTERN_FLAG, IsExtern);
            result.SetContext(this);
            foreach (var addon in AstGrammarAddons)
            {
                result.AddGrammarAddon(addon.Key, addon.Value);
            }

            result.CopyAttributesFrom(this);
            return result;
        }
    }
}
