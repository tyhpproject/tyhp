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
        /// Visits a Tyhp class method definition (function keyword variant).
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassMethodContext.
        ///
        /// Grammar:
        ///   tyhpClassMethodDefinition
        ///     : function ReturnsRef=returnsRef tyhpMethodDefinition  #tyhpClassMethod
        ///     ;
        ///
        /// Note: When called standalone (not via VisitAttributedClassStatementGrammarAddon),
        /// modifiers will be null since they live on the parent context.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassMethod([NotNull] TyhpParser.TyhpClassMethodContext context)
            => this.CreateTyhpClassMethod(context, null);


        /// <summary>
        /// Visits a Tyhp short (arrow) class method definition.
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassGenericMethodShortContext.
        ///
        /// Grammar:
        ///   tyhpClassMethodDefinition
        ///     : fn ReturnsRef=returnsRef
        ///         GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        ///         FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///         T_CLOSE_ROUND_BRACE OptionalReturnType=returnType T_DOUBLE_ARROW
        ///         Expr=expr T_SYM_SEMICOLON {this.isLanguageMode("tyhp")}?
        ///                                                            #tyhpClassGenericMethodShort
        ///     ;
        ///
        /// Note: When called standalone, modifiers will be null.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassGenericMethodShort([NotNull] TyhpParser.TyhpClassGenericMethodShortContext context)
            => this.CreateTyhpClassGenericMethodShort(context, null);
