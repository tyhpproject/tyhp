// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Grammar: #tyhpExtensionFunctionDecl — brace or bodyless <c>function</c> member.
        /// Name is <c>tyhpOptionalGenericIdentifierWithoutConstructor</c> (method-style).
        /// </summary>
        public override PhpFunctionDeclAst VisitTyhpExtensionFunctionDecl(
            [NotNull] TyhpParser.TyhpExtensionFunctionDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            PhpStatementBlockAst? body;
            if (context.StatementList != null)
            {
                body = this.VisitMethodBody(context.StatementList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionFunctionDecl.StatementList");
                body = null;
            }

            var ast = this.CreateLiveExtensionFunctionDecl(
                context,
                context.GenericIdentifier,
                context.ReturnsRef,
                context.functionModifiersGrammarAddon(),
                context.Parameters,
                context.ReturnType,
                body,
                isShortSyntax: false,
                languageMode,
                docComment);

            if (body == null && context.StatementList != null)
            {
                ast.AddGrammarAddon(
                    "isOverloadSignature",
                    this.GetTokenValueAst(context, context.StatementList.Start));
            }

            return ast.WithAttributes(
                context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }


        /// <summary>
        /// Grammar: #tyhpExtensionFunctionShortDecl — <c>fn name(...) =&gt; expr;</c>.
        /// Desugars to <c>return expr;</c> and must not carry <c>isOverloadSignature</c>.
        /// </summary>
        public override PhpFunctionDeclAst VisitTyhpExtensionFunctionShortDecl(
            [NotNull] TyhpParser.TyhpExtensionFunctionShortDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            IExpression expr;
            if (context.Expr != null)
            {
                expr = this.VisitExpr(context.Expr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionFunctionShortDecl.Expr");
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

            return this.CreateLiveExtensionFunctionDecl(
                    context,
                    context.GenericIdentifier,
                    context.ReturnsRef,
                    context.functionModifiersGrammarAddon(),
                    context.Parameters,
                    context.OptionalReturnType,
                    body,
                    isShortSyntax: true,
                    languageMode,
                    docComment)
                .WithAttributes(
                    context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }


        private IExtensionMemberAst VisitExtensionCallableMember(
            TyhpParser.TyhpExtensionCallableMemberContext? member,
            ParserRuleContext owner)
        {
            return member switch
            {
                TyhpParser.TyhpExtensionFunctionDeclContext ctx => this.VisitTyhpExtensionFunctionDecl(ctx),
                TyhpParser.TyhpExtensionFunctionShortDeclContext ctx => this.VisitTyhpExtensionFunctionShortDecl(ctx),
                null => this.ReportMissingRequiredAsErrorAst(owner, "tyhpExtensionCallableMember"),
                _ => this.HandleUnexpectedAlternativeSpecial<IExtensionMemberAst>(
                    member,
                    "tyhpExtensionCallableMember",
                    () => ErrorAst.Create(member, GetCurrentLanguageMode(member)))
            };
        }
