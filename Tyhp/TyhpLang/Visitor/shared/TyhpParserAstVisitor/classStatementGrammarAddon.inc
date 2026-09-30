// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        //#endregion Attributed Class Statement Grammar Addon

        //#region Class Statement Grammar Addon (Type aliases and Operator overloads)

        /// <summary>
        /// Dispatches class statement grammar addon alternatives.
        /// Overrides the PhpParserAstVisitor's throwing implementation.
        ///
        /// Grammar (TyhpParser):
        ///   classStatementGrammarAddon
        ///     : Modifier=nonEmptyMemberModifiers? TypeAlias=tyhpTypeAlias
        ///         {this.isLanguageMode("tyhp")}?          #tyhpClassTypeAlias
        ///     | OperatorOverload=tyhpClassOperatorOverload
        ///         {this.isLanguageMode("tyhp")}?          #tyhpClassOperatorOverload
        ///     ;
        /// </summary>
        public override IClassMember VisitClassStatementGrammarAddon([NotNull] TyhpParser.ClassStatementGrammarAddonContext context)
        {
            return context switch
            {
                TyhpParser.TyhpClassTypeAliasContext ctx => this.VisitTyhpClassTypeAlias(ctx),
                TyhpParser.TyhpClassOperatorOverloadDeclContext ctx => this.VisitTyhpClassOperatorOverloadDecl(ctx),
                TyhpParser.TyhpModifiedEnumCaseContext ctx => this.VisitTyhpModifiedEnumCase(ctx),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "classStatementGrammarAddon")
            };
        }


        public override PhpEnumCaseAst VisitTyhpModifiedEnumCase([NotNull] TyhpParser.TyhpModifiedEnumCaseContext context)
        {
            var enumCase = this.VisitEnumCase(context.EnumCase);
            if (context.Modifiers != null)
            {
                enumCase.AddGrammarAddon("modifiers", this.VisitNonEmptyMemberModifiers(context.Modifiers));
            }

            return enumCase;
        }


        /// <summary>
        /// Visits a type alias declaration inside a class body.
        ///
        /// Grammar:
        ///   classStatementGrammarAddon
        ///     : Modifier=nonEmptyMemberModifiers? TypeAlias=tyhpTypeAlias
        ///         {this.isLanguageMode("tyhp")}?          #tyhpClassTypeAlias
        ///     ;
        /// </summary>
        public override TyhpTypeAliasAst VisitTyhpClassTypeAlias([NotNull] TyhpParser.TyhpClassTypeAliasContext context)
        {
            var modifiers = context.Modifier != null
                ? this.VisitNonEmptyMemberModifiers(context.Modifier)
                : null;

            if (context.TypeAlias is null)
            {
                this.ReportMissingRequired(context, "tyhpClassTypeAlias.TypeAlias");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            // Delegate so truncated `type Foo` inside a class body gets the same
            // Identifier / TypeExpr null guards as the top-level type-alias visitor.
            var typeAlias = this.VisitTyhpTypeAlias(context.TypeAlias);
            var result = TyhpTypeAliasAst.Create(
                typeAlias.Name ?? PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                typeAlias.GenericArguments,
                typeAlias.TypeExpression
                    ?? PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context)),
                context,
                modifiers);
            if (context.Attributes != null)
            {
                result.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return result;
        }


        /// <summary>
        /// Visits an operator overload declaration inside a class body.
        ///
        /// Grammar:
        ///   tyhpClassOperatorOverload (via classStatementGrammarAddon #tyhpClassOperatorOverload)
        ///     : Modifier=(T_ABSTRACT | T_FINAL)? T_TYHP_OPERATOR
        ///         Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        ///         functionParametersGrammarAddon LeftParameter=attributedParameter
        ///         (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ///         ConvertReturnType=returnType
        ///         (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr))
        ///     ;
        ///
        /// Creates a TyhpOperatorOverloadAst with the operator, parameters,
        /// return type, and body (either method body or shorthand expression
        /// wrapped in a return statement).
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpClassOperatorOverloadDecl([NotNull] TyhpParser.TyhpClassOperatorOverloadDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var opCtx = context.tyhpClassOperatorOverload();
            if (opCtx == null)
            {
                this.ReportMissingRequired(context, "tyhpClassOperatorOverloadDecl.tyhpClassOperatorOverload");
                return TyhpOperatorOverloadAst.CreateError(context, languageMode);
            }

            TokenValueAst op;
            if (opCtx.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(opCtx.Op);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpClassOperatorOverload.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            PhpParameterAst leftParam;
            if (opCtx.LeftParameter != null)
            {
                leftParam = this.VisitAttributedParameter(opCtx.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpClassOperatorOverload.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = opCtx.RightParameter != null
                ? this.VisitAttributedParameter(opCtx.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (opCtx.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(opCtx.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpClassOperatorOverload.ConvertReturnType");
            }

            PhpStatementBlockAst? body;
            if (opCtx.StatementList != null)
            {
                body = this.VisitMethodBody(opCtx.StatementList);
            }
            else if (opCtx.ShorthandExpr != null)
            {
                // Shorthand expression: => expr
                var expr = this.VisitExpr(opCtx.ShorthandExpr);
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
                body = null;
            }

            PhpModifier? modifier = opCtx.Modifier != null
                ? FromToken(opCtx.Modifier.Type)
                : null;

            var result = TyhpOperatorOverloadAst.Create(
                op, leftParam, rightParam, returnType, body, modifier,
                context, languageMode,
                isShortSyntax: opCtx.ShorthandExpr != null);
            if (opCtx.IsInternal != null)
            {
                result.AddGrammarAddon("isInternal", TokenValueAst.Create(opCtx.IsInternal, context));
            }

            return result;
        }
