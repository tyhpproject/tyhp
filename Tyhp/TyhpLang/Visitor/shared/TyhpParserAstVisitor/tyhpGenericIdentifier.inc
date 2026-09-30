// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// tyhpGenericIdentifier
        ///     : (Identifier=T_STRING | IdentifierSemiReserved=semiReserved)
        ///         GenericArguments=tyhpGenericTypeArguments
        ///     ;
        /// </summary>
        public override TyhpGenericIdentifierAst VisitTyhpGenericIdentifier([NotNull] TyhpParser.TyhpGenericIdentifierContext context)
        {
            PhpTypeExpressionListAst genericArgs;
            if (context.GenericArguments != null)
            {
                genericArgs = this.VisitTyhpGenericTypeArguments(context.GenericArguments);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGenericIdentifier.GenericArguments");
                genericArgs = PhpTypeExpressionListAst.Create(null, context);
            }

            if (context.Identifier != null)
            {
                return TyhpGenericIdentifierAst.Create(context.Identifier, genericArgs, context);
            }

            if (context.IdentifierSemiReserved == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericIdentifier.Identifier");
                return TyhpGenericIdentifierAst.Create("<error>", -1, genericArgs, context);
            }

            var semiReserved = this.VisitSemiReserved(context.IdentifierSemiReserved);
            return TyhpGenericIdentifierAst.Create(semiReserved.ValueString ?? "", 0, genericArgs, context);
        }
