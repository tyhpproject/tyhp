// #class TyhpParserAstVisitor
// #using System
// #using System.Collections.Generic
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        /// <summary>
        /// Visits a tyhpdef class-body thin extension function (<c>extension fn … =&gt; expr;</c>).
        /// Grammar: tyhpdefDeprecatedOrObsolete? tyhpdefExtensionFunction #tyhpdefExtensionFunctionDecl
        /// </summary>
        public override TyhpdefInlineExtensionFunctionAst VisitTyhpdefExtensionFunctionDecl(
            [NotNull] TyhpParser.TyhpdefExtensionFunctionDeclContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var languageMode = GetCurrentLanguageMode(context);
            var fx = context.tyhpdefExtensionFunction();
            PhpMethodDeclAst inner;
            if (fx == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExtensionFunctionDecl.tyhpdefExtensionFunction");
                inner = PhpMethodDeclAst.CreateError(context, languageMode);
            }
            else
            {
                inner = fx switch
                {
                    TyhpParser.TyhpdefExtensionFunctionShortDeclContext shortDecl
                        => this.BuildTyhpdefExtensionFunctionFromShort(shortDecl, context, languageMode),
                    _ => this.HandleUnexpectedAlternativeSpecial(
                        fx,
                        "tyhpdefExtensionFunction",
                        () => PhpMethodDeclAst.CreateError(context, languageMode)),
                };
            }

            var result = TyhpdefInlineExtensionFunctionAst.Create(inner, context, languageMode);

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                var attributes = this.VisitAttributes(context.Attributes);
                result.AddAttributes(attributes);
                inner.AddAttributes(attributes);
            }

            return result;
        }

        /// <summary>
        /// Do not call directly — use <see cref="VisitTyhpdefExtensionFunctionDecl"/> which handles
        /// <c>tyhpdefDeprecatedOrObsolete</c>. This override exists only to prevent ANTLR's default
        /// <c>VisitChildren</c> from running if the method is reached unexpectedly.
        /// </summary>
        public override TyhpdefInlineExtensionFunctionAst VisitTyhpdefExtensionFunctionShortDecl(
            [NotNull] TyhpParser.TyhpdefExtensionFunctionShortDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var inner = this.BuildTyhpdefExtensionFunctionFromShort(context, context, languageMode);
            return TyhpdefInlineExtensionFunctionAst.Create(inner, context, languageMode);
        }

        /// <summary>
        /// Visits a tyhpdef inline extension operator.
        /// Grammar: tyhpdefDeprecatedOrObsolete? tyhpdefExtensionOperator #tyhpdefExtensionOperatorDecl
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpdefExtensionOperatorDecl(
            [NotNull] TyhpParser.TyhpdefExtensionOperatorDeclContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var languageMode = GetCurrentLanguageMode(context);
            var opCtx = context.tyhpdefExtensionOperator();
            TyhpOperatorOverloadAst result;
            if (opCtx == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExtensionOperatorDecl.tyhpdefExtensionOperator");
                result = TyhpOperatorOverloadAst.CreateError(context, languageMode);
                result.IsInlineExtension = true;
            }
            else
            {
                result = opCtx switch
                {
                    TyhpParser.TyhpdefExtensionOperatorFullDeclContext fullOp =>
                        this.BuildTyhpdefExtensionOperatorFromFull(fullOp, context, languageMode),
                    TyhpParser.TyhpdefExtensionOperatorSignatureDeclContext sigOp =>
                        this.BuildTyhpdefExtensionOperatorFromSignature(sigOp, context, languageMode),
                    _ => this.HandleUnexpectedAlternativeSpecial(
                        opCtx,
                        "tyhpdefExtensionOperator",
                        () =>
                        {
                            var placeholder = TyhpOperatorOverloadAst.CreateError(context, languageMode);
                            placeholder.IsInlineExtension = true;
                            return placeholder;
                        }),
                };
            }

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }

        /// <summary>
        /// Do not call directly — use <see cref="VisitTyhpdefExtensionOperatorDecl"/>.
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpdefExtensionOperatorSignatureDecl(
            [NotNull] TyhpParser.TyhpdefExtensionOperatorSignatureDeclContext context)
            => this.BuildTyhpdefExtensionOperatorFromSignature(context, context, GetCurrentLanguageMode(context));

        /// <summary>
        /// Do not call directly — use <see cref="VisitTyhpdefExtensionOperatorDecl"/> which handles
        /// <c>tyhpdefDeprecatedOrObsolete</c>. This override exists only to prevent ANTLR's default
        /// <c>VisitChildren</c> from running if the method is reached unexpectedly.
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpdefExtensionOperatorFullDecl(
            [NotNull] TyhpParser.TyhpdefExtensionOperatorFullDeclContext context)
            => this.BuildTyhpdefExtensionOperatorFromFull(context, context, GetCurrentLanguageMode(context));

        private PhpMethodDeclAst BuildTyhpdefExtensionFunctionFromShort(
            TyhpParser.TyhpdefExtensionFunctionShortDeclContext shortDecl,
            ParserRuleContext context,
            string? languageMode)
        {
            var docComment = this.FindPossibleDocComment(shortDecl.FindDocComment);

            PhpNameAst nameAst;
            if (shortDecl.GenericIdentifier != null)
            {
                nameAst = this.VisitTyhpOptionalGenericIdentifierWithoutConstructor(shortDecl.GenericIdentifier);
            }
            else
            {
                this.ReportMissingRequired(shortDecl, "tyhpdefExtensionFunctionShortDecl.GenericIdentifier");
                nameAst = PhpNameAst.CreateError(context, languageMode);
            }

            var genericArgs = (nameAst as TyhpGenericIdentifierAst)?.GenericArguments;

            PhpParameterListAst? parameters;
            if (shortDecl.ParameterList != null)
            {
                parameters = this.VisitParameterList(shortDecl.ParameterList);
            }
            else
            {
                this.ReportMissingRequired(shortDecl, "tyhpdefExtensionFunctionShortDecl.ParameterList");
                parameters = PhpParameterListAst.Create([], context, languageMode);
            }

            ITypeExpression? returnType = null;
            if (shortDecl.ReturnType != null)
            {
                returnType = this.VisitReturnType(shortDecl.ReturnType);
            }
            else
            {
                this.ReportMissingRequired(shortDecl, "tyhpdefExtensionFunctionShortDecl.ReturnType");
            }

            IExpression expr;
            if (shortDecl.Expr != null)
            {
                expr = this.VisitExpr(shortDecl.Expr);
            }
            else
            {
                this.ReportMissingRequired(shortDecl, "tyhpdefExtensionFunctionShortDecl.Expr");
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

            return PhpMethodDeclAst.Create(
                    nameAst.ValueString ?? "",
                    shortDecl.ReturnsRef != null && this.VisitReturnsRef(shortDecl.ReturnsRef) != null,
                    null,
                    parameters,
                    returnType,
                    body,
                    docComment,
                    context,
                    languageMode,
                    isShortSyntax: true)
                .WithGrammarAddon("identifier", genericArgs);
        }

        private TyhpOperatorOverloadAst BuildTyhpdefExtensionOperatorFromFull(
            TyhpParser.TyhpdefExtensionOperatorFullDeclContext fullOp,
            ParserRuleContext context,
            string? languageMode)
        {
            TokenValueAst op;
            if (fullOp.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(fullOp.Op);
            }
            else
            {
                this.ReportMissingRequired(fullOp, "tyhpdefExtensionOperatorFullDecl.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            PhpParameterAst leftParam;
            if (fullOp.LeftParameter != null)
            {
                leftParam = this.VisitAttributedParameter(fullOp.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(fullOp, "tyhpdefExtensionOperatorFullDecl.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = fullOp.RightParameter != null
                ? this.VisitAttributedParameter(fullOp.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (fullOp.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(fullOp.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(fullOp, "tyhpdefExtensionOperatorFullDecl.ConvertReturnType");
            }

            PhpStatementBlockAst? body;
            if (fullOp.Expr != null)
            {
                var expr = this.VisitExpr(fullOp.Expr);
                body = PhpStatementBlockAst.Create(
                    [PhpUnaryOpAst.Create(
                        TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                        expr,
                        context,
                        languageMode
                    )],
                    context,
                    languageMode
                );
            }
            else
            {
                this.ReportMissingRequired(fullOp, "tyhpdefExtensionOperatorFullDecl.Expr");
                body = null;
            }

            var result = TyhpOperatorOverloadAst.Create(
                op,
                leftParam,
                rightParam,
                returnType,
                body,
                null,
                context,
                languageMode,
                isShortSyntax: true);

            result.IsInlineExtension = true;
            return result;
        }

        private TyhpOperatorOverloadAst BuildTyhpdefExtensionOperatorFromSignature(
            TyhpParser.TyhpdefExtensionOperatorSignatureDeclContext sigOp,
            ParserRuleContext context,
            string? languageMode)
        {
            TokenValueAst op;
            if (sigOp.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(sigOp.Op);
            }
            else
            {
                this.ReportMissingRequired(sigOp, "tyhpdefExtensionOperatorSignatureDecl.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            PhpParameterAst leftParam;
            if (sigOp.LeftParameter != null)
            {
                leftParam = this.VisitAttributedParameter(sigOp.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(sigOp, "tyhpdefExtensionOperatorSignatureDecl.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = sigOp.RightParameter != null
                ? this.VisitAttributedParameter(sigOp.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (sigOp.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(sigOp.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(sigOp, "tyhpdefExtensionOperatorSignatureDecl.ConvertReturnType");
            }

            var result = TyhpOperatorOverloadAst.Create(
                op,
                leftParam,
                rightParam,
                returnType,
                null,
                null,
                context,
                languageMode);

            result.IsInlineExtension = true;
            return result;
        }
