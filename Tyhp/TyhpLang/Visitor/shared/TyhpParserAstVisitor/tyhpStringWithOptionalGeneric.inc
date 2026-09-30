// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpStringWithOptionalGeneric
        ///     : Identifier=T_STRING GenericArguments=tyhpGenericTypeArguments?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitTyhpStringWithOptionalGeneric([NotNull] TyhpParser.TyhpStringWithOptionalGenericContext context)
        {
            if (context.GenericArguments != null)
            {
                return TyhpGenericIdentifierAst.Create(
                    context.Identifier,
                    this.VisitTyhpGenericTypeArguments(context.GenericArguments),
                    context);
            }

            return PhpNameAst.Create(context.Identifier, context);
        }
