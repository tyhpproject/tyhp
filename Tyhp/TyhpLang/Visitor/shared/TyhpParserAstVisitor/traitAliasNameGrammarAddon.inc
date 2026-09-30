// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        //#endregion Trait Alias Grammar Addon

        //#region Identifier and Name Grammar Addons

        /// <summary>
        /// Visits the trait alias name grammar addon, returning generic type
        /// arguments if present.
        ///
        /// Grammar (TyhpParser):
        ///   traitAliasNameGrammarAddon
        ///     : GenericArguments=tyhpGenericTypeArguments?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitTraitAliasNameGrammarAddon([NotNull] TyhpParser.TraitAliasNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericTypeArguments(context.GenericArguments)
                : null;
