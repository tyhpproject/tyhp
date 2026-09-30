// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override IBase2Ast? VisitTraitAliasNameGrammarAddon([NotNull] TyhpParser.TraitAliasNameGrammarAddonContext context)
            => null;


        /// <summary>
        /// PHP <c>addPaths as private</c> (visibility-only trait alias) has no
        /// <c>traitAliasNameGrammarAddon</c> child. Visiting a null addon used to NRE.
        /// </summary>
        private IBase2Ast? VisitOptionalTraitAliasNameGrammarAddon(TyhpParser.TraitAliasNameGrammarAddonContext? addon)
            => addon == null ? null : this.VisitTraitAliasNameGrammarAddon(addon);
