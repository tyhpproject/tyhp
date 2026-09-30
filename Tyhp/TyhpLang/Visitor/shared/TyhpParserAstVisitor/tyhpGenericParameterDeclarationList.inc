// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericParameterDeclarationList
        ///     : Items+=tyhpGenericParameterDeclaration (T_SYM_COMMA Items+=tyhpGenericParameterDeclaration)*
        ///     ;
        /// </summary>
        public override TyhpGenericsTypeArgumentListAst VisitTyhpGenericParameterDeclarationList([NotNull] TyhpParser.TyhpGenericParameterDeclarationListContext context)
            => TyhpGenericsTypeArgumentListAst.Create(
                context._Items?.Select(this.VisitTyhpGenericParameterDeclaration),
                context
            );
