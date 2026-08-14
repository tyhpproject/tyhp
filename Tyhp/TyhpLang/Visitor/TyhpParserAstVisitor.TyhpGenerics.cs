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

        /// <summary>
        /// tyhpGenericTypeArgument
        ///     : TypeExpr=typeExpr
        ///     ;
        /// </summary>
        public override ITypeExpression VisitTyhpGenericTypeArgument([NotNull] TyhpParser.TyhpGenericTypeArgumentContext context)
        {
            if (context.TypeExpr == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericTypeArgument.TypeExpr");
                return PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTypeExpr(context.TypeExpr);
        }

        /// <summary>
        /// tyhpGenericTypeArgumentList
        ///     : Items+=tyhpGenericTypeArgument (T_SYM_COMMA Items+=tyhpGenericTypeArgument)*
        ///     ;
        /// </summary>
        public override PhpTypeExpressionListAst VisitTyhpGenericTypeArgumentList([NotNull] TyhpParser.TyhpGenericTypeArgumentListContext context)
            => PhpTypeExpressionListAst.Create(
                context._Items?.Select(item => this.VisitTyhpGenericTypeArgument(item)),
                context
            );

        /// <summary>
        /// tyhpGenericTypeArguments
        ///     : T_SYM_LT GenericArgumentsList=tyhpGenericTypeArgumentList T_SYM_GT
        ///     ;
        /// </summary>
        public override PhpTypeExpressionListAst VisitTyhpGenericTypeArguments([NotNull] TyhpParser.TyhpGenericTypeArgumentsContext context)
        {
            if (context.GenericArgumentsList == null)
            {
                this.ReportMissingRequired(context, "tyhpGenericTypeArguments.GenericArgumentsList");
                return PhpTypeExpressionListAst.Create(null, context);
            }

            return this.VisitTyhpGenericTypeArgumentList(context.GenericArgumentsList);
        }

        /// <summary>
        /// tyhpOptionalGenericIdentifier
        ///     : Identifier=identifier
        ///     | GenericIdentifier=tyhpGenericIdentifier {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitTyhpOptionalGenericIdentifier([NotNull] TyhpParser.TyhpOptionalGenericIdentifierContext context)
        {
            return context switch
            {
                _ when context.Identifier != null => this.VisitIdentifier(context.Identifier),
                _ when context.GenericIdentifier != null => this.VisitTyhpGenericIdentifier(context.GenericIdentifier),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpOptionalGenericIdentifier",
                    () => PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }

        /// <summary>
        /// tyhpOptionalGenericIdentifierWithoutConstructor
        ///     : Identifier=identifierWithoutConstructor
        ///     | GenericIdentifier=tyhpGenericIdentifierWithoutConstructor {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitTyhpOptionalGenericIdentifierWithoutConstructor([NotNull] TyhpParser.TyhpOptionalGenericIdentifierWithoutConstructorContext context)
        {
            return context switch
            {
                _ when context.Identifier != null => this.VisitIdentifierWithoutConstructor(context.Identifier),
                _ when context.GenericIdentifier != null => this.VisitTyhpGenericIdentifierWithoutConstructor(context.GenericIdentifier),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpOptionalGenericIdentifierWithoutConstructor",
                    () => PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }

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
    }
}
