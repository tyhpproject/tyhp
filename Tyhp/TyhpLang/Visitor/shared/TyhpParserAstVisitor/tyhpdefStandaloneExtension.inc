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
        /// Dispatches to the standalone tyhpdef extension declaration statement.
        /// Grammar: Statement=tyhpdefStandaloneExtensionDeclarationStatement   #tyhpdefStandaloneExtensionTopDecl
        /// </summary>
        public override TyhpdefStandaloneExtensionDeclAst VisitTyhpdefStandaloneExtensionTopDecl(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionTopDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionTopDecl.Statement");
                return TyhpdefStandaloneExtensionDeclAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var decl = this.VisitTyhpdefStandaloneExtensionDeclarationStatement(context.Statement);
            if (context.Attributes != null)
            {
                decl.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return decl;
        }

        /// <summary>
        /// Visits a standalone tyhpdef <c>extension Name { ... }</c> declaration.
        /// Optional generic parameters and an optional header <c>extends Type</c> match live Tyhp.
        /// Grammar: tyhpdefStandaloneExtensionDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? T_TYHP_EXTENSION Identifier=T_STRING
        ///       GenericParameters=tyhpGenericParameterDeclarations?
        ///       (T_EXTENDS TargetType=typeExprWithoutStatic)?
        ///       FindDocComment=T_OPEN_CURLY_BRACE
        ///       StatementList=tyhpdefStandaloneExtensionMemberList
        ///       T_CLOSE_CURLY_BRACE
        /// </summary>
        public override TyhpdefStandaloneExtensionDeclAst VisitTyhpdefStandaloneExtensionDeclarationStatement(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            string name;
            if (context.Identifier != null)
            {
                name = context.Identifier.Text;
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionDeclarationStatement.Identifier");
                name = "<error>";
            }

            TyhpExtensionFunctionListAst functionList;
            if (context.StatementList != null)
            {
                functionList = this.VisitTyhpdefStandaloneExtensionMemberList(context.StatementList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionDeclarationStatement.StatementList");
                functionList = TyhpExtensionFunctionListAst.Create(null, context);
            }

            var result = TyhpdefStandaloneExtensionDeclAst.Create(
                name,
                functionList,
                isDeprecated,
                isObsolete,
                docComment,
                context,
                GetCurrentLanguageMode(context)
            );
            this.AttachExtensionBlockTarget(result, context.GenericParameters, context.TargetType);
            return result;
        }

        /// <summary>
        /// Visits the member list of a standalone tyhpdef extension body.
        /// Grammar: tyhpdefStandaloneExtensionMemberList
        ///   : Items+=tyhpdefStandaloneExtensionMember*
        /// </summary>
        public override TyhpExtensionFunctionListAst VisitTyhpdefStandaloneExtensionMemberList(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionMemberListContext context)
            => TyhpExtensionFunctionListAst.Create(
                (context._Items ?? []).Where(item => item != null)
                    .Select(this.VisitTyhpdefStandaloneExtensionMemberAsExtensionMember!),
                context
            );

        /// <summary>
        /// Dispatches tyhpdefStandaloneExtensionMember labeled alternatives (no unlabeled base
        /// rule visitor is generated since every alternative carries a label).
        /// </summary>
        private IExtensionMemberAst VisitTyhpdefStandaloneExtensionMemberAsExtensionMember(
            TyhpParser.TyhpdefStandaloneExtensionMemberContext context)
        {
            return context switch
            {
                TyhpParser.TyhpdefStandaloneExtensionFunctionMemberContext ctx =>
                    this.VisitTyhpdefStandaloneExtensionFunctionOrError(ctx.FunctionDecl, context),
                TyhpParser.TyhpdefStandaloneExtensionOperatorMemberContext ctx =>
                    this.VisitTyhpdefStandaloneExtensionOperatorOrError(ctx.OperatorDecl, context),
                TyhpParser.TyhpdefStandaloneExtensionTargetGroupMemberContext ctx =>
                    this.VisitTyhpdefStandaloneExtensionTargetGroupOrError(ctx.TargetGroup, context),
                _ => this.HandleUnexpectedAlternativeSpecial<IExtensionMemberAst>(
                    context,
                    "tyhpdefStandaloneExtensionMember",
                    () => TyhpdefInlineExtensionFunctionAst.Create(
                        PhpMethodDeclAst.CreateError(context, GetCurrentLanguageMode(context)),
                        context,
                        GetCurrentLanguageMode(context)))
            };
        }

        /// <summary>
        /// Visits a standalone tyhpdef extension function member (short <c>fn ... =&gt; ...;</c>
        /// only — <c>function</c> and brace bodies are illegal on standalone tyhpdef extensions,
        /// matching live Tyhp short syntax).
        /// Grammar: tyhpdefStandaloneExtensionFunctionDecl
        ///   : Attributes=attributes? tyhpdefDeprecatedOrObsolete? fn
        ///       GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        ///       FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///       T_CLOSE_ROUND_BRACE ReturnType=returnType T_DOUBLE_ARROW
        ///       Expr=expr T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefInlineExtensionFunctionAst VisitTyhpdefStandaloneExtensionFunction(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionFunctionContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var languageMode = GetCurrentLanguageMode(context);
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            PhpNameAst nameAst;
            if (context.GenericIdentifier != null)
            {
                nameAst = this.VisitTyhpOptionalGenericIdentifierWithoutConstructor(context.GenericIdentifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionFunctionDecl.GenericIdentifier");
                nameAst = PhpNameAst.CreateError(context, languageMode);
            }

            var genericArgs = (nameAst as TyhpGenericIdentifierAst)?.GenericArguments;

            var parameters = this.ReadExtensionCallableParameters(
                context.Parameters,
                context,
                languageMode,
                out var byRefReceiver);

            ITypeExpression? returnType = null;
            if (context.ReturnType != null)
            {
                returnType = this.VisitReturnType(context.ReturnType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionFunctionDecl.ReturnType");
            }

            IExpression expr;
            if (context.Expr != null)
            {
                expr = this.VisitExpr(context.Expr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionFunctionDecl.Expr");
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

            var method = PhpMethodDeclAst.Create(
                    nameAst.ValueString ?? "",
                    false,
                    null,
                    parameters,
                    returnType,
                    body,
                    docComment,
                    context,
                    languageMode,
                    isShortSyntax: true)
                .WithGrammarAddon("identifier", genericArgs);

            var result = TyhpdefInlineExtensionFunctionAst.Create(method, context, languageMode);
            if (byRefReceiver != null)
            {
                var receiverToken = TokenValueAst.Create(byRefReceiver, context, languageMode);
                result.AddGrammarAddon(TyhpExtensionDeclAst.ByRefReceiverAddonKey, receiverToken);
                method.AddGrammarAddon(TyhpExtensionDeclAst.ByRefReceiverAddonKey, receiverToken);
            }
            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            if (context.Attributes != null)
            {
                var attributes = this.VisitAttributes(context.Attributes);
                result.AddAttributes(attributes);
                method.AddAttributes(attributes);
            }

            return result;
        }

        /// <summary>
        /// Visits a standalone tyhpdef extension operator member (short <c>=&gt;</c> form only).
        /// Grammar: tyhpdefStandaloneExtensionOperatorDecl
        ///   : Attributes=attributes? tyhpdefDeprecatedOrObsolete? T_TYHP_OPERATOR
        ///       Op=tyhpClassOperatorOverloadOp T_SYM_LT
        ///       TargetType=typeExprWithoutStatic T_SYM_GT T_OPEN_ROUND_BRACE
        ///       functionParametersGrammarAddon LeftParameter=attributedParameter
        ///       (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ///       ConvertReturnType=returnType T_DOUBLE_ARROW Expr=expr
        ///       T_SYM_SEMICOLON
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpdefStandaloneExtensionOperator(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionOperatorContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var languageMode = GetCurrentLanguageMode(context);

            TokenValueAst op;
            if (context.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(context.Op);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionOperatorDecl.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            this.ReportLegacyOperatorTarget(context.LegacyTarget, context);

            PhpParameterAst leftParam;
            if (context.LeftParameter != null)
            {
                leftParam = this.VisitAttributedParameter(context.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionOperatorDecl.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = context.RightParameter != null
                ? this.VisitAttributedParameter(context.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (context.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(context.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionOperatorDecl.ConvertReturnType");
            }

            PhpStatementBlockAst? body = null;
            if (context.Expr != null)
            {
                var expr = this.VisitExpr(context.Expr);
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
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionOperatorDecl.Expr");
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

        public override TyhpExtensionDeclAst VisitTyhpdefStandaloneExtensionTargetGroup(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionTargetGroupContext context)
        {
            TyhpExtensionFunctionListAst functionList;
            if (context.StatementList != null)
            {
                functionList = this.VisitTyhpdefStandaloneExtensionGroupMemberList(context.StatementList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefStandaloneExtensionTargetGroup.StatementList");
                functionList = TyhpExtensionFunctionListAst.Create(null, context);
            }

            return this.CreateExtensionTargetGroup(
                context,
                context.GenericParameters,
                context.TargetType,
                context.FindDocComment,
                functionList);
        }

        public override TyhpExtensionFunctionListAst VisitTyhpdefStandaloneExtensionGroupMemberList(
            [NotNull] TyhpParser.TyhpdefStandaloneExtensionGroupMemberListContext context)
            => TyhpExtensionFunctionListAst.Create(
                (context.tyhpdefStandaloneExtensionGroupMember() ?? [])
                    .Select(this.VisitTyhpdefStandaloneExtensionGroupMemberAsExtensionMember),
                context);

        private IExtensionMemberAst VisitTyhpdefStandaloneExtensionFunctionOrError(
            TyhpParser.TyhpdefStandaloneExtensionFunctionContext? function,
            ParserRuleContext owner)
        {
            if (function == null)
            {
                this.ReportMissingRequired(owner, "tyhpdefStandaloneExtensionFunction");
                return TyhpdefInlineExtensionFunctionAst.Create(
                    PhpMethodDeclAst.CreateError(owner, GetCurrentLanguageMode(owner)),
                    owner,
                    GetCurrentLanguageMode(owner));
            }

            return this.VisitTyhpdefStandaloneExtensionFunction(function);
        }

        private IExtensionMemberAst VisitTyhpdefStandaloneExtensionOperatorOrError(
            TyhpParser.TyhpdefStandaloneExtensionOperatorContext? op,
            ParserRuleContext owner)
        {
            if (op == null)
            {
                this.ReportMissingRequired(owner, "tyhpdefStandaloneExtensionOperator");
                return TyhpOperatorOverloadAst.CreateError(owner, GetCurrentLanguageMode(owner));
            }

            return this.VisitTyhpdefStandaloneExtensionOperator(op);
        }

        private IExtensionMemberAst VisitTyhpdefStandaloneExtensionTargetGroupOrError(
            TyhpParser.TyhpdefStandaloneExtensionTargetGroupContext? group,
            ParserRuleContext owner)
        {
            if (group == null)
            {
                this.ReportMissingRequired(owner, "tyhpdefStandaloneExtensionTargetGroup");
                return TyhpExtensionDeclAst.CreateError(owner, GetCurrentLanguageMode(owner));
            }

            return this.VisitTyhpdefStandaloneExtensionTargetGroup(group);
        }

        private IExtensionMemberAst VisitTyhpdefStandaloneExtensionGroupMemberAsExtensionMember(
            TyhpParser.TyhpdefStandaloneExtensionGroupMemberContext context)
        {
            return context switch
            {
                TyhpParser.TyhpdefStandaloneExtensionGroupFunctionMemberContext ctx =>
                    this.VisitTyhpdefStandaloneExtensionFunctionOrError(ctx.FunctionDecl, context),
                TyhpParser.TyhpdefStandaloneExtensionGroupOperatorMemberContext ctx =>
                    this.VisitTyhpdefStandaloneExtensionOperatorOrError(ctx.OperatorDecl, context),
                _ => this.HandleUnexpectedAlternativeSpecial<IExtensionMemberAst>(
                    context,
                    "tyhpdefStandaloneExtensionGroupMember",
                    () => TyhpdefInlineExtensionFunctionAst.Create(
                        PhpMethodDeclAst.CreateError(context, GetCurrentLanguageMode(context)),
                        context,
                        GetCurrentLanguageMode(context)))
            };
        }
