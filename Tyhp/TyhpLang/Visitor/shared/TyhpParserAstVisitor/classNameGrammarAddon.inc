// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions

        //#region Name Grammar Addons (Generics on class/trait/interface/enum names)

        /// <summary>
        /// Visits the class name grammar addon, returning generic type arguments
        /// if present in Tyhp mode.
        ///
        /// Grammar (TyhpParser):
        ///   classNameGrammarAddon
        ///     : GenericArguments=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitClassNameGrammarAddon([NotNull] TyhpParser.ClassNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;
