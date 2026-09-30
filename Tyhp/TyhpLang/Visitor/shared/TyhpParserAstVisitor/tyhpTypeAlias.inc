// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Visits a Tyhp type alias declaration.
        ///
        /// Grammar:
        ///   tyhpTypeAlias
        ///     : T_TYHP_TYPE_ALIAS Identifier=name
        ///         GenericArguments=tyhpGenericParameterDeclarations? T_SYM_EQUAL
        ///         ( {this.looksLikeObjectShapeIntersection()}?
        ///             ObjectShapeIntersection=tyhpObjectShapeIntersection
        ///         | {this.looksLikeObjectShape()}? ObjectShape=tyhpObjectShape
        ///         | TypeExpr=typeExpr ) T_SYM_SEMICOLON
        ///     ;
        ///
        /// ANTLR recovery after truncated input (e.g. <c>type Foo</c> at EOF) can leave
        /// <c>Identifier</c> / <c>TypeExpr</c> / <c>ObjectShape</c> /
        /// <c>ObjectShapeIntersection</c> null — report
        /// <see cref="Domain.Exceptions.MessageCode.VisitorMissingRequiredNode"/> and use
        /// placeholders instead of throwing. Object-shape members become a
        /// <see cref="TyhpObjectShapeAst"/> attached as the alias type (or as an
        /// intersection item when the RHS is <c>Nominal &amp; object { … }</c>).
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
            else if (context.ObjectShapeIntersection != null)
            {
                typeExpr = this.VisitTyhpObjectShapeIntersection(context.ObjectShapeIntersection);
            }
            else if (context.ObjectShape != null)
            {
                typeExpr = this.VisitTyhpObjectShape(context.ObjectShape);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpTypeAlias.TypeExpr");
                typeExpr = PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return TyhpTypeAliasAst.Create(name, genericArgs, typeExpr, context);
        }
