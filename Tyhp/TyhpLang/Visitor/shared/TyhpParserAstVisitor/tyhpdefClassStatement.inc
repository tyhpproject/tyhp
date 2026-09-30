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
        /// Brace form yields a class body (empty when the statement list is missing).
        /// Semicolon overlay header form yields <c>null</c> so the AST is header-only.
        /// </summary>
        private PhpClassBodyAst? VisitTyhpdefObjectBodyOrHeaderOnly(
            TyhpParser.TyhpdefClassStatementListContext? statementList,
            ITerminalNode? semicolon,
            ParserRuleContext context)
        {
            if (semicolon != null)
            {
                return null;
            }

            return statementList != null
                ? this.VisitTyhpdefClassStatementList(statementList)
                : PhpClassBodyAst.Create(null, context);
        }

        /// <summary>
        /// Visits a tyhpdef class statement list.
        /// Grammar: tyhpdefClassStatementList
        ///   : Items+=tyhpdefClassStatement*
        /// </summary>
        public override PhpClassBodyAst VisitTyhpdefClassStatementList([NotNull] TyhpParser.TyhpdefClassStatementListContext context)
            => PhpClassBodyAst.Create(
                (context._Items ?? []).Where(item => item != null).Select(this.VisitTyhpdefClassStatement!),
                context
            );

        /// <summary>
        /// Dispatches tyhpdefClassStatement labeled alternatives.
        /// </summary>
        public IClassMember? VisitTyhpdefClassStatement(TyhpParser.TyhpdefClassStatementContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.TyhpdefClassPropertyContext ctx => this.VisitTyhpdefClassProperty(ctx),
                TyhpParser.TyhpdefClassPropertyAccessorsContext ctx => this.VisitTyhpdefClassPropertyAccessors(ctx),
                TyhpParser.TyhpdefImportClassConstContext ctx => this.VisitTyhpdefImportClassConst(ctx),
                TyhpParser.TyhpdefPartialClassMethodContext ctx => this.VisitTyhpdefPartialClassMethod(ctx),
                TyhpParser.TyhpdefImportClassMethodContext ctx => this.VisitTyhpdefImportClassMethod(ctx),
                TyhpParser.TyhpdefEnumCaseContext ctx => this.VisitTyhpdefEnumCase(ctx),
                TyhpParser.TyhpdefTraitUseContext ctx => this.VisitTyhpdefTraitUse(ctx),
                TyhpParser.TyhpdefClassUseExtensionContext ctx => this.VisitTyhpdefClassUseExtension(ctx),
                TyhpParser.TyhpdefExtensionFunctionDeclContext ctx => this.VisitTyhpdefExtensionFunctionDecl(ctx),
                TyhpParser.TyhpdefExtensionOperatorDeclContext ctx => this.VisitTyhpdefExtensionOperatorDecl(ctx),
                TyhpParser.TyhpdefClassOperatorDeclContext ctx => this.VisitTyhpdefClassOperatorDecl(ctx),
                TyhpParser.TyhpdefClassTypeAliasContext ctx => this.VisitTyhpdefClassTypeAlias(ctx),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "tyhpdefClassStatement")
            };
        }

        /// <summary>
        /// Visits a class-body type alias in tyhpdef
        /// (<c>public type Name = …;</c>).
        /// </summary>
        public override TyhpTypeAliasAst VisitTyhpdefClassTypeAlias(
            [NotNull] TyhpParser.TyhpdefClassTypeAliasContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var modifiers = context.Modifier != null
                ? this.VisitNonEmptyMemberModifiers(context.Modifier)
                : null;

            if (context.TypeAlias is null)
            {
                this.ReportMissingRequired(context, "tyhpdefClassTypeAlias.TypeAlias");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var typeAlias = this.VisitTyhpTypeAlias(context.TypeAlias);
            var result = TyhpTypeAliasAst.Create(
                typeAlias.Name ?? PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                typeAlias.GenericArguments,
                typeAlias.TypeExpression
                    ?? PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context)),
                context,
                modifiers);
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
