using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp extension import declaration: `use extension NameList { adaptations }`
    ///
    /// Grammar:
    ///   topStatementGrammarAddon
    ///     : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
    ///         Adaptations=traitAdaptations                                        #tyhpImportExtension
    ///     ;
    ///
    /// Imports one or more extensions with optional trait-like adaptations
    /// (precedence, aliasing) to resolve conflicts between extensions.
    ///
    /// Also used for <c>use extension</c> inside tyhpdef class bodies (<c>#tyhpdefClassUseExtension</c>).
    /// </summary>
    public class TyhpImportExtensionAst : Base2Ast, IStatement, IClassMember
    {
        private const short IS_GLOBAL_FLAG = -30;

        /// <summary>
        /// The list of extension import declarations being imported.
        /// </summary>
        public PhpImportDeclListAst? UseDeclarations => Children.ElementAtOrDefault(0) as PhpImportDeclListAst;

        /// <summary>
        /// Optional trait-like adaptations (precedence, aliasing) for the imported extensions.
        /// Null when the import ends with a semicolon (no curly brace block).
        /// </summary>
        public PhpTraitAdaptationListAst? Adaptations => Children.ElementAtOrDefault(1) as PhpTraitAdaptationListAst;

        /// <summary>
        /// <c>global use extension</c>: activates the listed extensions for every file in the
        /// compilation (C# <c>global using</c>, not PHP <c>global $var</c>).
        /// </summary>
        public bool IsGlobal
        {
            get => HasFlag(IS_GLOBAL_FLAG);
            set => SetFlag(IS_GLOBAL_FLAG, value);
        }

        public static TyhpImportExtensionAst Create(
            PhpImportDeclListAst useDeclarations,
            PhpTraitAdaptationListAst? adaptations,
            ParserRuleContext context,
            string? languageMode = null,
            bool isGlobal = false)
        {
            var result = new TyhpImportExtensionAst
            {
                Children = [useDeclarations, adaptations],
            };
            result.IsGlobal = isGlobal;
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Placeholder used when required <c>use extension</c> children are missing after ANTLR recovery.
        /// </summary>
        public static TyhpImportExtensionAst CreateError(ParserRuleContext context, string? languageMode = null)
            => Create(
                PhpImportDeclListAst.Create(null, context, languageMode),
                null,
                context,
                languageMode);
    }
}
