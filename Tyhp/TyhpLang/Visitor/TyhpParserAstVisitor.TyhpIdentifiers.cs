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
        /// tyhpReservedNonModifiers
        ///     : TokenValue=T_TYHP_STRUCT
        ///     | TokenValue=T_TYHP_TYPE_ALIAS
        ///     | TokenValue=T_TYHP_AWAIT
        ///     | TokenValue=T_TYHP_WITH
        ///     | TokenValue=T_TYHP_OPERATOR
        ///     | TokenValue=T_TYHP_VOID
        ///     | TokenValue=T_TYHP_PARENT
        ///     | TokenValue=T_TYHP_EXTENSION
        ///     ;
        /// </summary>
        public override TokenValueAst VisitTyhpReservedNonModifiers([NotNull] TyhpParser.TyhpReservedNonModifiersContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        /// <summary>
        /// reservedNonModifiersGrammarAddon
        ///     : tyhpReservedNonModifiers {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override TokenValueAst VisitReservedNonModifiersGrammarAddon([NotNull] TyhpParser.ReservedNonModifiersGrammarAddonContext context)
            => this.VisitTyhpReservedNonModifiers(context.tyhpReservedNonModifiers());

        /// <summary>
        /// tyhpSemiReserved
        ///     : TokenValue=T_TYHP_ASYNC
        ///     | TokenValue=T_TYHP_OPERATOR
        ///     | TokenValue=T_TYHPDEF_DEPRECATED
        ///     | TokenValue=T_TYHPDEF_OBSOLETE
        ///     | TokenValue=T_TYHP_IS
        ///     ;
        /// </summary>
        public override TokenValueAst VisitTyhpSemiReserved([NotNull] TyhpParser.TyhpSemiReservedContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        /// <summary>
        /// semiReservedGrammarAddon
        ///     : tyhpSemiReserved {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override TokenValueAst VisitSemiReservedGrammarAddon([NotNull] TyhpParser.SemiReservedGrammarAddonContext context)
            => this.VisitTyhpSemiReserved(context.tyhpSemiReserved());

        /// <summary>
        /// namespaceNameGrammarAddon
        ///     : (Name=T_STRING | QualifiedName=T_NAME_QUALIFIED)
        ///         GenericArguments=tyhpGenericTypeArguments
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitNamespaceNameGrammarAddon([NotNull] TyhpParser.NamespaceNameGrammarAddonContext context)
        {
            var genericArgs = this.VisitTyhpGenericTypeArguments(context.GenericArguments);
            var nameToken = context.Name ?? context.QualifiedName;
            if (nameToken == null)
            {
                return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }
            return TyhpGenericIdentifierAst.Create(nameToken, genericArgs, context);
        }

        /// <summary>
        /// legacyNamespaceNameGrammarAddon
        ///     : FullyQualifiedName=T_NAME_FULLY_QUALIFIED
        ///         GenericArguments=tyhpGenericTypeArguments
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitLegacyNamespaceNameGrammarAddon([NotNull] TyhpParser.LegacyNamespaceNameGrammarAddonContext context)
            => TyhpGenericIdentifierAst.Create(
                context.FullyQualifiedName,
                this.VisitTyhpGenericTypeArguments(context.GenericArguments),
                context
            );

        /// <summary>
        /// nameTokenValueGrammarAddon
        ///     : T_TYHP_VOID {this.isLanguageMode("tyhp")}?
        ///     | T_TYHP_PARENT {this.isLanguageMode("tyhp")}?
        ///     | T_TYHP_USING {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override PhpNameAst VisitNameTokenValueGrammarAddon([NotNull] TyhpParser.NameTokenValueGrammarAddonContext context)
            => PhpNameAst.Create(context.Start, context);

        /// <summary>
        /// typeNameGrammarAddon
        ///     : GenericArguments=tyhpGenericTypeArguments?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitTypeNameGrammarAddon([NotNull] TyhpParser.TypeNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericTypeArguments(context.GenericArguments)
                : null;

        /// <summary>
        /// classNameIdentifierGrammarAddon
        ///     : GenericArguments=tyhpGenericTypeArguments?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitClassNameIdentifierGrammarAddon([NotNull] TyhpParser.ClassNameIdentifierGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericTypeArguments(context.GenericArguments)
                : null;

        /// <summary>
        /// memberNameIdentifierGrammarAddon
        ///     : GenericArguments=tyhpGenericTypeArguments?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitMemberNameIdentifierGrammarAddon([NotNull] TyhpParser.MemberNameIdentifierGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericTypeArguments(context.GenericArguments)
                : null;

        /// <summary>
        /// optionalTypeWithoutStatic
        ///     : TypeExpr=typeExprWithoutStatic? {!this.isLanguageMode("tyhp")}?
        ///     | TypeExpr=typeExprWithoutStatic {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// In Tyhp mode, the type expression is required (not optional).
        /// The base implementation already handles both cases correctly by
        /// checking if TypeExpr is present.
        /// </summary>
        public override ITypeExpression? VisitOptionalTypeWithoutStatic([NotNull] TyhpParser.OptionalTypeWithoutStaticContext context)
            => base.VisitOptionalTypeWithoutStatic(context);
    }
}
