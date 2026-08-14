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
        /// functionDeclarationStatementGrammarAddon (dispatch)
        ///     : functionModifiersGrammarAddon function
        ///         ReturnsRef=returnsRef Identifier=functionName functionNameGrammarAddon
        ///         FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ///         ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///         IsOverloadSignature=T_SYM_SEMICOLON
        ///         {this.isLanguageMode("tyhp")}?          #tyhpFunctionOverloadDeclarationStatement
        ///     | functionModifiersGrammarAddon fn
        ///         ReturnsRef=returnsRef Identifier=functionName functionNameGrammarAddon
        ///         FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ///         ParameterList=parameterList T_CLOSE_ROUND_BRACE
        ///         OptionalReturnType=returnType T_DOUBLE_ARROW Expr=expr
        ///         T_SYM_SEMICOLON
        ///         {this.isLanguageMode("tyhp")}?          #tyhpShortFunctionOverloadDeclarationStatement
        ///     ;
        ///
        /// Dispatches to the appropriate labeled alternative visitor.
        /// </summary>
        public override PhpFunctionDeclAst VisitFunctionDeclarationStatementGrammarAddon(
            [NotNull] TyhpParser.FunctionDeclarationStatementGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpFunctionOverloadDeclarationStatementContext ctx
                    => this.VisitTyhpFunctionOverloadDeclarationStatement(ctx),
                TyhpParser.TyhpShortFunctionOverloadDeclarationStatementContext ctx
                    => this.VisitTyhpShortFunctionOverloadDeclarationStatement(ctx),
                _ => base.VisitFunctionDeclarationStatementGrammarAddon(context)
            };

        /// <summary>
        /// #tyhpFunctionOverloadDeclarationStatement
        ///     : functionModifiersGrammarAddon function
        ///         ReturnsRef=returnsRef Identifier=functionName functionNameGrammarAddon
        ///         FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ///         ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///         IsOverloadSignature=T_SYM_SEMICOLON
        ///         {this.isLanguageMode("tyhp")}?
        ///
        /// Creates a PhpFunctionDeclAst with no body (overload signature).
        /// The IsOverloadSignature token is stored as a grammar addon to distinguish
        /// overload signatures from regular function declarations.
        /// </summary>
        public override PhpFunctionDeclAst VisitTyhpFunctionOverloadDeclarationStatement(
            [NotNull] TyhpParser.TyhpFunctionOverloadDeclarationStatementContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);

            string name;
            if (context.Identifier != null)
            {
                name = this.VisitFunctionName(context.Identifier).ValueString ?? "";
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpFunctionOverloadDeclarationStatement.Identifier");
                name = string.Empty;
            }

            PhpParameterListAst parameters;
            if (context.ParameterList != null)
            {
                parameters = this.VisitParameterList(context.ParameterList)
                    ?? PhpParameterListAst.Create([], context, languageMode);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpFunctionOverloadDeclarationStatement.ParameterList");
                parameters = PhpParameterListAst.Create([], context, languageMode);
            }

            var returnType = context.ReturnType != null
                ? this.VisitReturnType(context.ReturnType)
                : null;
            if (context.ReturnType == null)
            {
                this.ReportMissingRequired(context, "tyhpFunctionOverloadDeclarationStatement.ReturnType");
            }

            return PhpFunctionDeclAst.Create(
                name,
                context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null,
                parameters,
                returnType,
                null,
                context,
                languageMode
            ).WithGrammarAddon(
                "modifiers",
                context.functionModifiersGrammarAddon() != null
                    ? this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon())
                    : null)
            .WithGrammarAddon(
                "identifier",
                context.functionNameGrammarAddon() != null
                    ? this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon())
                    : null)
            .WithGrammarAddon(
                "parameters",
                context.functionParametersGrammarAddon() != null
                    ? this.VisitFunctionParametersGrammarAddon(context.functionParametersGrammarAddon())
                    : null)
            .WithGrammarAddon(
                "isOverloadSignature",
                context.IsOverloadSignature != null
                    ? this.GetTokenValueAst(context, context.IsOverloadSignature)
                    : null);
        }

        /// <summary>
        /// #tyhpShortFunctionOverloadDeclarationStatement
        ///     : functionModifiersGrammarAddon fn
        ///         ReturnsRef=returnsRef Identifier=functionName functionNameGrammarAddon
        ///         FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ///         ParameterList=parameterList T_CLOSE_ROUND_BRACE
        ///         OptionalReturnType=returnType T_DOUBLE_ARROW Expr=expr
        ///         T_SYM_SEMICOLON
        ///         {this.isLanguageMode("tyhp")}?
        ///
        /// Named short-function sugar (<c>fn name(...) =&gt; expr;</c>), including short-form
        /// overload <em>implementations</em>. Desugars at visit time into a normal
        /// <see cref="PhpFunctionDeclAst"/> whose body is <c>return expr;</c> so the emitter can
        /// always print <c>function name(...) { return expr; }</c>. This is <em>not</em> an
        /// overload signature (those are bodyless <c>function name(...): T;</c> decls) and must
        /// not carry the <c>isOverloadSignature</c> addon — otherwise the binder would skip it.
        /// Anonymous PHP arrow functions (<c>fn($x) =&gt; …</c>) use a separate expression path.
        /// </summary>
        public override PhpFunctionDeclAst VisitTyhpShortFunctionOverloadDeclarationStatement(
            [NotNull] TyhpParser.TyhpShortFunctionOverloadDeclarationStatementContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);

            string name;
            if (context.Identifier != null)
            {
                name = this.VisitFunctionName(context.Identifier).ValueString ?? "";
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpShortFunctionOverloadDeclarationStatement.Identifier");
                name = string.Empty;
            }

            PhpParameterListAst parameters;
            if (context.ParameterList != null)
            {
                parameters = this.VisitParameterList(context.ParameterList)
                    ?? PhpParameterListAst.Create([], context, languageMode);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpShortFunctionOverloadDeclarationStatement.ParameterList");
                parameters = PhpParameterListAst.Create([], context, languageMode);
            }

            IExpression expr;
            if (context.Expr != null)
            {
                expr = this.VisitExpr(context.Expr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpShortFunctionOverloadDeclarationStatement.Expr");
                expr = ErrorAst.Create(context, languageMode);
            }

            var body = PhpStatementBlockAst.Create(
                [PhpUnaryOpAst.Create(
                    TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                    expr,
                    context,
                    languageMode
                )],
                context,
                languageMode
            );

            return PhpFunctionDeclAst.Create(
                name,
                context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null,
                parameters,
                context.OptionalReturnType != null
                    ? this.VisitReturnType(context.OptionalReturnType)
                    : null,
                body,
                context,
                languageMode
            ).WithGrammarAddon(
                "modifiers",
                context.functionModifiersGrammarAddon() != null
                    ? this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon())
                    : null)
            .WithGrammarAddon(
                "identifier",
                context.functionNameGrammarAddon() != null
                    ? this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon())
                    : null)
            .WithGrammarAddon(
                "parameters",
                context.functionParametersGrammarAddon() != null
                    ? this.VisitFunctionParametersGrammarAddon(context.functionParametersGrammarAddon())
                    : null);
        }

        /// <summary>
        /// functionModifiersGrammarAddon
        ///     : IsAsync=T_TYHP_ASYNC? {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns a TokenValueListAst containing the async modifier token if present,
        /// or null if no modifiers are specified.
        /// </summary>
        public override TokenValueListAst? VisitFunctionModifiersGrammarAddon(
            [NotNull] TyhpParser.FunctionModifiersGrammarAddonContext context)
        {
            if (context.IsAsync != null)
            {
                return TokenValueListAst.Create(
                    [this.GetTokenValueAst(context, context.IsAsync)],
                    context
                );
            }

            return null;
        }

        /// <summary>
        /// functionNameGrammarAddon
        ///     : GenericParameters=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns the generic type arguments for the function name if present,
        /// or null if no generic parameters are specified. Attached as the
        /// "identifier" grammar addon on PhpFunctionDeclAst / PhpInlineFunctionAst.
        /// </summary>
        public override IBase2Ast? VisitFunctionNameGrammarAddon(
            [NotNull] TyhpParser.FunctionNameGrammarAddonContext context)
            => context.GenericParameters != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericParameters)
                : null;

        /// <summary>
        /// functionParametersGrammarAddon
        ///     : IsExtension=T_EXTENDS? {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns a TokenValueAst for the extends keyword if present,
        /// indicating this function's first parameter is an extension receiver.
        /// Attached as the "parameters" grammar addon on PhpFunctionDeclAst.
        /// </summary>
        public override IBase2Ast? VisitFunctionParametersGrammarAddon(
            [NotNull] TyhpParser.FunctionParametersGrammarAddonContext context)
            => context.IsExtension != null
                ? this.GetTokenValueAst(context, context.IsExtension)
                : null;

        /// <summary>
        /// Attaches Tyhp generic type arguments from <c>functionCallGrammarAddon</c> to the call AST.
        /// The base <see cref="PhpParserAstVisitor.VisitFunctionCallGrammarAddon"/> return type
        /// (<see cref="PhpArgumentListAst"/>) cannot represent a type-argument list, so this override
        /// adds them as a separate grammar addon on <see cref="PhpCallAst"/>.
        /// </summary>
        public override PhpCallAst VisitCallArgumentList([NotNull] TyhpParser.CallArgumentListContext context)
        {
            var call = base.VisitCallArgumentList(context);
            var genericAddon = context.functionCallGrammarAddon();
            if (genericAddon?.GenericArguments != null)
            {
                call.AddGrammarAddon(
                    "genericTypeArguments",
                    this.VisitTyhpGenericTypeArguments(genericAddon.GenericArguments));
            }

            return call;
        }
    }
}
