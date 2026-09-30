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
        /// Visits a Tyhp constructor with explicit return type.
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassCtorWithReturnTypeContext.
        ///
        /// Grammar:
        ///   tyhpMethodDefinition
        ///     : Identifier=T_CONSTRUCT_METHOD
        ///         FindDocComment=T_OPEN_ROUND_BRACE ParameterList=ctorParameterList
        ///         T_CLOSE_ROUND_BRACE ReturnType=tyhpCtorReturnType?
        ///         StatementList=methodBody                            #tyhpClassCtorWithReturnType
        ///     ;
        ///
        /// The return type annotation is optional: omitting it is equivalent to `: void`
        /// (no `parent::__construct` insertion). When omitted, no `ctorReturnType` grammar
        /// addon is attached; the emitter and checker treat a missing addon as void.
        ///
        /// Note: When called standalone, modifiers and returnsRef will be defaults.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassCtorWithReturnType([NotNull] TyhpParser.TyhpClassCtorWithReturnTypeContext context)
            => this.CreateTyhpClassCtor(context, false, null);


        /// <summary>
        /// Visits a Tyhp generic class method definition.
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassGenericMethodContext.
        ///
        /// Grammar:
        ///   tyhpMethodDefinition
        ///     : GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        ///         FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///         T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///         StatementList=methodBody                            #tyhpClassGenericMethod
        ///     ;
        ///
        /// Note: When called standalone, modifiers and returnsRef will be defaults.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassGenericMethod([NotNull] TyhpParser.TyhpClassGenericMethodContext context)
            => this.CreateTyhpClassGenericMethod(context, false, null);
