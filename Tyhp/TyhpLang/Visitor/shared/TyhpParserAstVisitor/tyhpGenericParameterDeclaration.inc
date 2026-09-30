// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// tyhpGenericParameterDeclaration
        ///     : Identifier=name (T_EXTENDS TypeExpr=typeExpr)? (T_SYM_EQUAL DefaultExpr=typeExpr)?
        ///     ;
        /// </summary>
        public override TyhpGenericsTypeArgumentAst VisitTyhpGenericParameterDeclaration([NotNull] TyhpParser.TyhpGenericParameterDeclarationContext context)
        {
            PhpNameAst name;
            if (context.Identifier != null)
            {
                name = this.VisitName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGenericParameterDeclaration.Identifier");
                name = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var typeConstraint = context.TypeExpr != null ? this.VisitTypeExpr(context.TypeExpr) : null;
            var defaultType = context.DefaultExpr != null ? this.VisitTypeExpr(context.DefaultExpr) : null;
            return TyhpGenericsTypeArgumentAst.Create(name, typeConstraint, defaultType, context);
        }
