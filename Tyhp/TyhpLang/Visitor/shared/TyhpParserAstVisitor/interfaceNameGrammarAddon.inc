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
        /// Visits the interface name grammar addon, returning generic type arguments
        /// if present in Tyhp mode.
        ///
        /// Grammar (TyhpParser):
        ///   interfaceNameGrammarAddon
        ///     : GenericArguments=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitInterfaceNameGrammarAddon([NotNull] TyhpParser.InterfaceNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;
