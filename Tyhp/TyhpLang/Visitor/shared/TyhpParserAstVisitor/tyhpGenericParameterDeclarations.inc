// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericParameterDeclarations
        ///     : T_SYM_LT GenericParametersList=tyhpGenericParameterDeclarationList T_SYM_GT
        ///     ;
        /// </summary>
        public override TyhpGenericsTypeArgumentListAst VisitTyhpGenericParameterDeclarations([NotNull] TyhpParser.TyhpGenericParameterDeclarationsContext context)
        {
            if (context.GenericParametersList == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericParameterDeclarations.GenericParametersList");
                return TyhpGenericsTypeArgumentListAst.Create(null, context);
            }

            return this.VisitTyhpGenericParameterDeclarationList(context.GenericParametersList);
        }
