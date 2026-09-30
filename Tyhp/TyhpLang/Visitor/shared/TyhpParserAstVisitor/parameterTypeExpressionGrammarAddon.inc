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
        /// Visits the parameter type expression grammar addon.
        /// In Tyhp mode, parameter types are required (typeExprWithoutStatic).
        /// In PHP mode, parameter types are optional (optionalTypeWithoutStatic).
        ///
        /// Grammar (TyhpParser):
        ///   parameterTypeExpressionGrammarAddon
        ///     : {this.isLanguageMode("tyhp")}? typeExprWithoutStatic
        ///     | {!this.isLanguageMode("tyhp")}? optionalTypeWithoutStatic
        ///     ;
        ///
        /// The base PhpParserAstVisitor already handles both alternatives by
        /// checking which sub-rule is present, so we delegate to it.
        /// </summary>
        public override ITypeExpression? VisitParameterTypeExpressionGrammarAddon([NotNull] TyhpParser.ParameterTypeExpressionGrammarAddonContext context)
            => base.VisitParameterTypeExpressionGrammarAddon(context);
