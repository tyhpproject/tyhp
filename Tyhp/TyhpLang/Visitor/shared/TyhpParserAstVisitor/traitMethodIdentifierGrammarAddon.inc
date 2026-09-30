// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        /// <summary>
        /// Visits the trait method identifier grammar addon, returning a generic
        /// identifier if present.
        ///
        /// Grammar (TyhpParser):
        ///   traitMethodIdentifierGrammarAddon
        ///     : (GenericIdentifier=tyhpGenericIdentifier
        ///         {this.isLanguageMode("tyhp")}?)?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitTraitMethodIdentifierGrammarAddon([NotNull] TyhpParser.TraitMethodIdentifierGrammarAddonContext context)
            => context.GenericIdentifier != null
                ? this.VisitTyhpGenericIdentifier(context.GenericIdentifier)
                : null;
