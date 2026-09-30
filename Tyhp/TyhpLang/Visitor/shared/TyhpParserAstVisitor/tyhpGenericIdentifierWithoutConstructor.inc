// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericIdentifierWithoutConstructor
        ///     : (Identifier=T_STRING | IdentifierSemiReserved=semiReservedWithoutConstructor)
        ///         GenericArguments=tyhpGenericParameterDeclarations
        ///     ;
        /// </summary>
        public override TyhpGenericIdentifierAst VisitTyhpGenericIdentifierWithoutConstructor([NotNull] TyhpParser.TyhpGenericIdentifierWithoutConstructorContext context)
        {
            TyhpGenericsTypeArgumentListAst genericParams;
            if (context.GenericArguments != null)
            {
                genericParams = this.VisitTyhpGenericParameterDeclarations(context.GenericArguments);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGenericIdentifierWithoutConstructor.GenericArguments");
                genericParams = TyhpGenericsTypeArgumentListAst.Create(null, context);
            }

            if (context.Identifier != null)
            {
                return TyhpGenericIdentifierAst.Create(context.Identifier, genericParams, context);
            }

            if (context.IdentifierSemiReserved == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericIdentifierWithoutConstructor.Identifier");
                return TyhpGenericIdentifierAst.Create("<error>", -1, genericParams, context);
            }

            var semiReserved = this.VisitSemiReservedWithoutConstructor(context.IdentifierSemiReserved);
            return TyhpGenericIdentifierAst.Create(semiReserved.ValueString ?? "", 0, genericParams, context);
        }
