namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Visits a Tyhp type alias declaration.
        ///
        /// Grammar:
        ///   tyhpTypeAlias
        ///     : T_TYHP_TYPE_ALIAS Identifier=name
        ///         GenericArguments=tyhpGenericParameterDeclarations? T_SYM_EQUAL
        ///         TypeExpr=typeExpr T_SYM_SEMICOLON
        ///     ;
        ///
        /// ANTLR recovery after truncated input (e.g. <c>type Foo</c> at EOF) can leave
        /// <c>Identifier</c> / <c>TypeExpr</c> null — report
        /// <see cref="Domain.Exceptions.MessageCode.VisitorMissingRequiredNode"/> and use
        /// placeholders instead of throwing.
        /// </summary>
        public override TyhpTypeAliasAst VisitTyhpTypeAlias([NotNull] TyhpParser.TyhpTypeAliasContext context)
        {
            PhpNameAst name;
            if (context.Identifier != null)
            {
                name = this.VisitName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpTypeAlias.Identifier");
                name = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var genericArgs = context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExpr(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpTypeAlias.TypeExpr");
                typeExpr = PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return TyhpTypeAliasAst.Create(name, genericArgs, typeExpr, context);
        }
    }
}
