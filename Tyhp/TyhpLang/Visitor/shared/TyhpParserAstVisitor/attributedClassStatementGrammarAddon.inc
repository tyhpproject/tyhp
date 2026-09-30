// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        //#endregion Tyhp Constructor Return Type

        //#region Attributed Class Statement Grammar Addon (Tyhp method declarations)

        /// <summary>
        /// Visits the attributed class statement grammar addon for Tyhp method
        /// declarations (regular methods, short methods, and constructors).
        ///
        /// Grammar (TyhpParser):
        ///   attributedClassStatementGrammarAddon
        ///     : Modifiers=methodModifiers tyhpClassMethodDefinition
        ///     ;
        ///
        /// Dispatches to the appropriate method definition handler based on
        /// the type of tyhpClassMethodDefinition alternative.
        /// </summary>
        public override IClassMember VisitAttributedClassStatementGrammarAddon([NotNull] TyhpParser.AttributedClassStatementGrammarAddonContext context)
        {
            var modifiers = this.VisitMethodModifiers(context.Modifiers);
            var methodDef = context.tyhpClassMethodDefinition();

            return methodDef switch
            {
                TyhpParser.TyhpClassMethodContext ctx
                    => this.CreateTyhpClassMethod(ctx, modifiers),
                TyhpParser.TyhpClassGenericMethodShortContext ctx
                    => this.CreateTyhpClassGenericMethodShort(ctx, modifiers),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "attributedClassStatementGrammarAddon")
            };
        }
