// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits a single struct property declaration.
        ///
        /// Grammar:
        ///   tyhpStructProperty
        ///     : TypeExpr=typeExprWithoutStatic
        ///         ((AliasOfString=T_CONSTANT_ENCAPSED_STRING | AliasOfInt=T_LNUMBER) T_AS)?
        ///         Property=property T_SYM_SEMICOLON
        ///     ;
        /// </summary>
        public override TyhpStructPropertyAst VisitTyhpStructProperty([NotNull] TyhpParser.TyhpStructPropertyContext context)
        {
            string? aliasOf = null;
            var aliasIsNumeric = false;
            if (context.AliasOfString != null)
            {
                aliasOf = context.AliasOfString.Text;
            }
            else if (context.AliasOfInt != null)
            {
                aliasOf = context.AliasOfInt.Text;
                aliasIsNumeric = true;
            }

            return TyhpStructPropertyAst.Create(
                this.VisitTypeExprWithoutStatic(context.TypeExpr),
                this.VisitProperty(context.Property),
                aliasOf,
                aliasIsNumeric,
                context
            );
        }
