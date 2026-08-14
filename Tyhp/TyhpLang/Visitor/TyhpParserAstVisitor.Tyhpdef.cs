namespace Tyhp.TyhpLang.Visitor
{
    using System;
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Entry point for parsing Tyhpdef source files.
        /// Grammar: tyhpdefSrcFile
        ///   : TyhpdefBlock=tyhpdefBlock EOF   #tyhpdefFile
        /// </summary>
        public override TyhpdefSrcFileAst VisitTyhpdefFile([NotNull] TyhpParser.TyhpdefFileContext context)
        {
            // Missing/invalid open tag leaves TyhpdefBlock null after recovery — do not NRE.
            if (context.TyhpdefBlock == null)
            {
                return TyhpdefSrcFileAst.Create(this._filename, this._fileHash, null, null, null);
            }

            var block = this.VisitTyhpdefBlock(context.TyhpdefBlock);
            return TyhpdefSrcFileAst.Create(
                this._filename,
                this._fileHash,
                null,
                block != null ? [block] : null,
                null
            );
        }

        /// <summary>
        /// Visits a tyhpdef block (tyhpdef open tag followed by statement list).
        /// Grammar: tyhpdefBlock
        ///   : T_TYHPDEF_OPEN_TAG StatementList=tyhpdefTopStatementList
        /// </summary>
        public override PhpTopStatementListAst VisitTyhpdefBlock([NotNull] TyhpParser.TyhpdefBlockContext context)
        {
            if (context.StatementList == null)
            {
                return PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefTopStatementList(context.StatementList);
        }

        /// <summary>
        /// Entry point for parsing tagless Tyhpdef source files (source.tagless enabled).
        /// Grammar: tyhpdefTaglessSrcFile
        ///   : T_TYHPDEF_OPEN_TAG? StatementList=tyhpdefTopStatementList EOF   #tyhpdefTaglessFile
        /// The open tag is optional; the whole file is a single Tyhpdef block.
        /// </summary>
        public override TyhpdefSrcFileAst VisitTyhpdefTaglessFile([NotNull] TyhpParser.TyhpdefTaglessFileContext context)
        {
            var block = context.StatementList != null
                ? this.VisitTyhpdefTopStatementList(context.StatementList)
                : PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            return TyhpdefSrcFileAst.Create(
                this._filename,
                this._fileHash,
                null,
                block != null ? [block] : null,
                null
            );
        }

        /// <summary>
        /// Visits a list of tyhpdef top-level statements.
        /// Grammar: tyhpdefTopStatementList
        ///   : Items+=tyhpdefTopStatement*
        /// </summary>
        public override PhpTopStatementListAst VisitTyhpdefTopStatementList([NotNull] TyhpParser.TyhpdefTopStatementListContext context)
        {
            var result = PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            if (context._Items != null)
            {
                foreach (var item in context._Items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var topStatement = this.VisitTyhpdefTopStatement(item);
                    if (topStatement != null)
                    {
                        result.Add(topStatement);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Dispatches tyhpdefTopStatement labeled alternatives.
        /// </summary>
        public ITopStatement? VisitTyhpdefTopStatement(TyhpParser.TyhpdefTopStatementContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.TyhpdefNotAttributedTopStatementContext ctx => this.VisitTyhpdefNotAttributedTopStatement(ctx),
                TyhpParser.TyhpdefAttributedTopStatementContext ctx => this.VisitTyhpdefAttributedTopStatement(ctx),
                TyhpParser.TyhpdefNameSpaceDeclContext ctx => this.VisitTyhpdefNameSpaceDecl(ctx),
                TyhpParser.TyhpdefNamespaceGroupDeclContext ctx => this.VisitTyhpdefNamespaceGroupDecl(ctx),
                TyhpParser.TyhpdefImportGroupDeclsContext ctx => this.VisitTyhpdefImportGroupDecls(ctx),
                TyhpParser.TyhpdefImportTypedGroupDeclsContext ctx => this.VisitTyhpdefImportTypedGroupDecls(ctx),
                TyhpParser.TyhpdefImportDeclsContext ctx => this.VisitTyhpdefImportDecls(ctx),
                TyhpParser.TyhpdefImportTypeContext ctx => this.VisitTyhpdefImportType(ctx),
                TyhpParser.TyhpdefImportExtensionContext ctx => this.VisitTyhpdefImportExtension(ctx),
                TyhpParser.TyhpdefTypeAliasDeclContext ctx => this.VisitTyhpdefTypeAliasDecl(ctx),
                _ => HandleUnexpectedAlternative<ITopStatement>(context, "tyhpdefTopStatement")
            };
        }

        /// <summary>
        /// Visits a non-attributed tyhpdef top statement (struct, empty, declare, const, variable).
        /// Grammar: tyhpdefTopStatement
        ///   : Statement=tyhpdefStatement   #tyhpdefNotAttributedTopStatement
        /// </summary>
        public override ITopStatement VisitTyhpdefNotAttributedTopStatement([NotNull] TyhpParser.TyhpdefNotAttributedTopStatementContext context)
        {
            if (context.Statement == null)
            {
                return this.ReportMissingRequiredAsErrorAst(context, "tyhpdefNotAttributedTopStatement.Statement");
            }

            return this.VisitTyhpdefStatement(context.Statement);
        }

        /// <summary>
        /// Visits an attributed tyhpdef top statement (function, class, trait, interface, enum imports).
        /// Grammar: tyhpdefTopStatement
        ///   : Attributes=attributes? Statement=tyhpdefAttributedStatement   #tyhpdefAttributedTopStatement
        /// </summary>
        public override IAttributedStatement VisitTyhpdefAttributedTopStatement([NotNull] TyhpParser.TyhpdefAttributedTopStatementContext context)
        {
            if (context.Statement == null)
            {
                return this.ReportMissingRequiredAsErrorAst(context, "tyhpdefAttributedTopStatement.Statement");
            }

            var statement = this.VisitTyhpdefAttributedStatement(context.Statement);
            if (statement == null)
            {
                return this.ReportMissingRequiredAsErrorAst(context, "tyhpdefAttributedTopStatement.Statement");
            }

            if (context.Attributes != null)
            {
                var attributes = this.VisitAttributes(context.Attributes);
                statement.AddAttributes(attributes);
            }
            return statement;
        }

        /// <summary>
        /// Dispatches tyhpdefStatement labeled alternatives.
        /// Grammar: tyhpdefStatement
        ///   : Statement=tyhpStructDeclarationStatement   #tyhpdefStructDecl
        ///   | T_SYM_SEMICOLON                            #tyhpdefEmptyStatement
        ///   | T_DECLARE ...                              #tyhpdefDeclare
        ///   | Statement=tyhpdefImportConstStatement       #tyhpdefImportConst
        ///   | Statement=tyhpdefImportVariableStatement    #tyhpdefImportVariable
        /// </summary>
        public ITopStatement VisitTyhpdefStatement(TyhpParser.TyhpdefStatementContext? context)
        {
            if (context == null)
            {
                return ErrorAst.Create(
                    "Missing tyhpdefStatement",
                    MessageCode.VisitorMissingRequiredNode,
                    0,
                    0,
                    languageMode: "tyhpdef");
            }

            return context switch
            {
                TyhpParser.TyhpdefStructDeclContext ctx => this.VisitTyhpdefStructDecl(ctx),
                TyhpParser.TyhpdefEmptyStatementContext ctx => this.VisitTyhpdefEmptyStatement(ctx),
                TyhpParser.TyhpdefDeclareContext ctx => this.VisitTyhpdefDeclare(ctx),
                TyhpParser.TyhpdefImportConstContext ctx => this.VisitTyhpdefImportConst(ctx),
                TyhpParser.TyhpdefImportVariableContext ctx => this.VisitTyhpdefImportVariable(ctx),
                _ => HandleUnexpectedAlternative<ITopStatement>(context, "tyhpdefStatement")
            };
        }

        /// <summary>
        /// Dispatches tyhpdefAttributedStatement labeled alternatives.
        /// Grammar: tyhpdefAttributedStatement
        ///   : Statement=tyhpdefImportFunctionDeclarationStatement   #tyhpdefImportFunctionDecl
        ///   | Statement=tyhpdefImportClassDeclarationStatement      #tyhpdefImportClassDecl
        ///   | Statement=tyhpdefImportTraitDeclarationStatement      #tyhpdefImportTraitDecl
        ///   | Statement=tyhpdefImportInterfaceDeclarationStatement  #tyhpdefImportInterfaceDecl
        ///   | Statement=tyhpdefImportEnumDeclarationStatement       #tyhpdefImportEnumDecl
        /// </summary>
        public IAttributedStatement? VisitTyhpdefAttributedStatement(TyhpParser.TyhpdefAttributedStatementContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.TyhpdefImportFunctionDeclContext ctx => this.VisitTyhpdefImportFunctionDecl(ctx),
                TyhpParser.TyhpdefImportClassDeclContext ctx => this.VisitTyhpdefImportClassDecl(ctx),
                TyhpParser.TyhpdefImportTraitDeclContext ctx => this.VisitTyhpdefImportTraitDecl(ctx),
                TyhpParser.TyhpdefImportInterfaceDeclContext ctx => this.VisitTyhpdefImportInterfaceDecl(ctx),
                TyhpParser.TyhpdefImportEnumDeclContext ctx => this.VisitTyhpdefImportEnumDecl(ctx),
                _ => HandleUnexpectedAlternative<IAttributedStatement>(context, "tyhpdefAttributedStatement")
            };
        }

        /// <summary>
        /// Visits a tyhpdef namespace declaration (non-block).
        /// Grammar: T_NAMESPACE NamespaceName=namespaceDeclarationName T_SYM_SEMICOLON
        /// </summary>
        public override PhpNamespaceDeclAst VisitTyhpdefNameSpaceDecl([NotNull] TyhpParser.TyhpdefNameSpaceDeclContext context)
        {
            if (context.NamespaceName == null)
            {
                this.ReportMissingRequired(context, "tyhpdefNameSpaceDecl.NamespaceName");
                return PhpNamespaceDeclAst.Create("", null, context, GetCurrentLanguageMode(context));
            }

            var namespaceName = this.VisitNamespaceDeclarationName(context.NamespaceName);
            return PhpNamespaceDeclAst.Create(namespaceName.ValueString, null, context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Visits a tyhpdef block namespace declaration.
        /// Grammar: T_NAMESPACE NamespaceName=namespaceDeclarationName?
        ///   T_OPEN_CURLY_BRACE StatementList=tyhpdefTopStatementList T_CLOSE_CURLY_BRACE
        /// </summary>
        public override PhpNamespaceDeclAst VisitTyhpdefNamespaceGroupDecl([NotNull] TyhpParser.TyhpdefNamespaceGroupDeclContext context)
        {
            var namespaceName = context.NamespaceName != null
                ? this.VisitNamespaceDeclarationName(context.NamespaceName)
                : null;
            var statementList = context.StatementList != null
                ? this.VisitTyhpdefTopStatementList(context.StatementList)
                : PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            return PhpNamespaceDeclAst.Create(namespaceName?.ValueString, statementList, context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Visits a tyhpdef mixed group use declaration.
        /// Grammar: T_USE UseDecl=mixedGroupUseDeclaration T_SYM_SEMICOLON
        /// </summary>
        public override PhpImportDeclListAst VisitTyhpdefImportGroupDecls([NotNull] TyhpParser.TyhpdefImportGroupDeclsContext context)
        {
            if (context.UseDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportGroupDecls.UseDecl");
                return PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            return this.VisitMixedGroupUseDeclaration(context.UseDecl);
        }

        /// <summary>
        /// Visits a tyhpdef typed group use declaration.
        /// Grammar: T_USE UseType=useType UseDecl=groupUseDeclaration T_SYM_SEMICOLON
        /// </summary>
        public override PhpImportDeclListAst VisitTyhpdefImportTypedGroupDecls([NotNull] TyhpParser.TyhpdefImportTypedGroupDeclsContext context)
        {
            TokenValueAst useType;
            if (context.UseType != null)
            {
                useType = this.VisitUseType(context.UseType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportTypedGroupDecls.UseType");
                useType = TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpImportDeclListAst importList;
            if (context.UseDecl != null)
            {
                importList = this.VisitGroupUseDeclaration(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportTypedGroupDecls.UseDecl");
                importList = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            foreach (var import in importList.GetAllNotNull())
            {
                import.SetUseType(useType);
            }
            return importList;
        }

        /// <summary>
        /// Visits tyhpdef use declarations.
        /// Grammar: T_USE UseDecl=useDeclarations T_SYM_SEMICOLON
        /// </summary>
        public override PhpImportDeclListAst VisitTyhpdefImportDecls([NotNull] TyhpParser.TyhpdefImportDeclsContext context)
        {
            if (context.UseDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportDecls.UseDecl");
                return PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            return this.VisitUseDeclarations(context.UseDecl);
        }

        /// <summary>
        /// Visits tyhpdef typed use declarations.
        /// Grammar: T_USE UseType=useType UseDecl=useDeclarations T_SYM_SEMICOLON
        /// </summary>
        public override PhpImportDeclListAst VisitTyhpdefImportType([NotNull] TyhpParser.TyhpdefImportTypeContext context)
        {
            TokenValueAst useType;
            if (context.UseType != null)
            {
                useType = this.VisitUseType(context.UseType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportType.UseType");
                useType = TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpImportDeclListAst importList;
            if (context.UseDecl != null)
            {
                importList = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportType.UseDecl");
                importList = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            foreach (var import in importList.GetAllNotNull())
            {
                import.SetUseType(useType);
            }
            return importList;
        }

        /// <summary>
        /// Visits a tyhpdef extension import declaration.
        /// Grammar: T_USE T_TYHP_EXTENSION UseDecl=useDeclarations Adaptations=traitAdaptations
        /// </summary>
        public override TyhpImportExtensionAst VisitTyhpdefImportExtension([NotNull] TyhpParser.TyhpdefImportExtensionContext context)
        {
            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportExtension.Adaptations");
            }

            return TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        public override TyhpTypeAliasAst VisitTyhpdefTypeAliasDecl([NotNull] TyhpParser.TyhpdefTypeAliasDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefTypeAliasDecl.Statement");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpTypeAlias(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef deprecated or obsolete marker.
        /// Grammar: tyhpdefDeprecatedOrObsolete
        ///   : TokenValue=(T_TYHPDEF_DEPRECATED|T_TYHPDEF_OBSOLETE)
        /// </summary>
        public override TokenValueAst VisitTyhpdefDeprecatedOrObsolete([NotNull] TyhpParser.TyhpdefDeprecatedOrObsoleteContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        /// <summary>
        /// Visits a tyhpdef struct declaration.
        /// Grammar: Statement=tyhpStructDeclarationStatement   #tyhpdefStructDecl
        /// </summary>
        public override TyhpStructDeclAst VisitTyhpdefStructDecl([NotNull] TyhpParser.TyhpdefStructDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefStructDecl.Statement");
                return TyhpStructDeclAst.Create(
                    "<error>",
                    TyhpStructPropertyListAst.Create(null, context),
                    null,
                    null,
                    context,
                    GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpStructDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef empty statement (bare semicolon).
        /// Grammar: T_SYM_SEMICOLON   #tyhpdefEmptyStatement
        /// </summary>
        public override PhpNopStatementAst VisitTyhpdefEmptyStatement([NotNull] TyhpParser.TyhpdefEmptyStatementContext context)
            => PhpNopStatementAst.Create(context, GetCurrentLanguageMode(context));

        /// <summary>
        /// Visits a tyhpdef declare statement.
        /// Grammar: T_DECLARE T_OPEN_ROUND_BRACE DeclareList=constList T_CLOSE_ROUND_BRACE
        ///   Statement=declareStatement T_SYM_SEMICOLON   #tyhpdefDeclare
        /// </summary>
        public override PhpDeclareAst VisitTyhpdefDeclare([NotNull] TyhpParser.TyhpdefDeclareContext context)
        {
            var declareList = context.DeclareList != null
                ? this.VisitConstList(context.DeclareList)
                : PhpConstDeclListAst.Create(null, context);
            IStatement body = context.Statement != null
                ? this.VisitDeclareStatement(context.Statement)
                : ErrorAst.Create(context, GetCurrentLanguageMode(context));
            return PhpDeclareAst.Create(declareList, body, context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Visits a tyhpdef import const (dispatches to import const statement).
        /// Grammar: Statement=tyhpdefImportConstStatement   #tyhpdefImportConst
        /// </summary>
        public override TyhpdefImportConstAst VisitTyhpdefImportConst([NotNull] TyhpParser.TyhpdefImportConstContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportConst.Statement");
                return TyhpdefImportConstAst.Create(
                    ErrorAst.Create(context, GetCurrentLanguageMode(context)),
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    null, false, false, null,
                    context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefImportConstStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef import variable (dispatches to import variable statement).
        /// Grammar: Statement=tyhpdefImportVariableStatement   #tyhpdefImportVariable
        /// </summary>
        public override TyhpdefImportVariableAst VisitTyhpdefImportVariable([NotNull] TyhpParser.TyhpdefImportVariableContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportVariable.Statement");
                return TyhpdefImportVariableAst.Create(
                    ErrorAst.Create(context, GetCurrentLanguageMode(context)),
                    "<error>", null, null, false, false, null,
                    context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefImportVariableStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef constant import statement.
        /// Grammar: tyhpdefImportConstStatement
        ///   : tyhpdefDeprecatedOrObsolete? T_CONST TypeExpr=typeExprWithoutStatic
        ///       (AliasedIdentifier=tyhpdefIdentifierWithOptionalAlias | Identifier=name)
        ///       (T_COALESCE CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportConstAst VisitTyhpdefImportConstStatement([NotNull] TyhpParser.TyhpdefImportConstStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportConstStatement.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            IBase2Ast nameOrAlias;
            if (context.AliasedIdentifier != null)
            {
                nameOrAlias = this.VisitTyhpdefIdentifierWithOptionalAlias(context.AliasedIdentifier);
            }
            else if (context.Identifier != null)
            {
                nameOrAlias = this.VisitName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportConstStatement.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;
            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            return TyhpdefImportConstAst.Create(
                typeExpr, nameOrAlias, coalesceExpr,
                isDeprecated, isObsolete, docComment,
                context, GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef variable import statement.
        /// Grammar: tyhpdefImportVariableStatement
        ///   : tyhpdefDeprecatedOrObsolete? TypeExpr=typeExprWithoutStatic
        ///       Variable=T_VARIABLE (T_AS AliasedAs=T_VARIABLE)? (T_COALESCE
        ///       CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportVariableAst VisitTyhpdefImportVariableStatement([NotNull] TyhpParser.TyhpdefImportVariableStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportVariableStatement.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            if (context.Variable == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportVariableStatement.Variable");
                return TyhpdefImportVariableAst.Create(
                    typeExpr, "<error>", null, null,
                    isDeprecated, isObsolete, null,
                    context, GetCurrentLanguageMode(context));
            }

            var variableName = context.Variable.Text;
            var aliasedAs = context.AliasedAs?.Text;
            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;
            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            return TyhpdefImportVariableAst.Create(
                typeExpr, variableName, aliasedAs, coalesceExpr,
                isDeprecated, isObsolete, docComment,
                context, GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Dispatches to function declaration statement.
        /// Grammar: Statement=tyhpdefImportFunctionDeclarationStatement   #tyhpdefImportFunctionDecl
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefImportFunctionDecl([NotNull] TyhpParser.TyhpdefImportFunctionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportFunctionDecl.Statement");
                return TyhpdefImportFunctionDeclAst.Create(
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    false, false, false, null, null, false, false, null,
                    context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefImportFunctionDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to class declaration statement.
        /// Grammar: Statement=tyhpdefImportClassDeclarationStatement   #tyhpdefImportClassDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportClassDecl([NotNull] TyhpParser.TyhpdefImportClassDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "class");
            }

            return this.VisitTyhpdefImportClassDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to trait declaration statement.
        /// Grammar: Statement=tyhpdefImportTraitDeclarationStatement   #tyhpdefImportTraitDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportTraitDecl([NotNull] TyhpParser.TyhpdefImportTraitDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportTraitDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "trait");
            }

            return this.VisitTyhpdefImportTraitDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to interface declaration statement.
        /// Grammar: Statement=tyhpdefImportInterfaceDeclarationStatement   #tyhpdefImportInterfaceDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportInterfaceDecl([NotNull] TyhpParser.TyhpdefImportInterfaceDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportInterfaceDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "interface");
            }

            return this.VisitTyhpdefImportInterfaceDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to enum declaration statement.
        /// Grammar: Statement=tyhpdefImportEnumDeclarationStatement   #tyhpdefImportEnumDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportEnumDecl([NotNull] TyhpParser.TyhpdefImportEnumDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportEnumDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "enum");
            }

            return this.VisitTyhpdefImportEnumDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef function import declaration.
        /// Grammar: tyhpdefImportFunctionDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC? function
        ///       ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        ///       FindDocComment=T_OPEN_ROUND_BRACE IsExtension=T_EXTENDS?
        ///       ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///       T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefImportFunctionDeclarationStatement([NotNull] TyhpParser.TyhpdefImportFunctionDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitTyhpdefFunctionNameWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportFunctionDeclarationStatement.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var returnsRef = context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null;
            var isAsync = context.IsAsync != null;
            var isExtension = context.IsExtension != null;
            var parameters = context.ParameterList != null
                ? this.VisitParameterList(context.ParameterList)
                : null;
            var returnType = context.ReturnType != null
                ? this.VisitReturnType(context.ReturnType)
                : null;
            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            return TyhpdefImportFunctionDeclAst.Create(
                nameOrAlias, returnsRef, isAsync, isExtension,
                parameters, returnType,
                isDeprecated, isObsolete, docComment,
                context, GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef class import declaration.
        /// Grammar: tyhpdefImportClassDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? Modifiers=classModifiers? T_CLASS
        ///       Identifier=tyhpdefClassNameWithOptionalAlias Extends=extendsFrom
        ///       Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        ///       StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportClassDeclarationStatement([NotNull] TyhpParser.TyhpdefImportClassDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            var modifiers = context.Modifiers != null
                ? this.VisitClassModifiers(context.Modifiers)
                : null;

            PhpNameAst identifier;
            if (context.Identifier != null)
            {
                identifier = this.VisitTyhpdefClassNameWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassDeclarationStatement.Identifier");
                identifier = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var statementList = context.StatementList != null
                ? this.VisitTyhpdefClassStatementList(context.StatementList)
                : PhpClassBodyAst.Create(null, context);

            return TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("class", TyhpParser.T_CLASS, context),
                modifiers,
                identifier,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef trait import declaration.
        /// Grammar: tyhpdefImportTraitDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? T_TRAIT
        ///       Identifier=tyhpdefIdentifierWithOptionalAlias
        ///       FindDocComment=T_OPEN_CURLY_BRACE
        ///       StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportTraitDeclarationStatement([NotNull] TyhpParser.TyhpdefImportTraitDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            IBase2Ast identifier;
            if (context.Identifier != null)
            {
                identifier = this.VisitTyhpdefIdentifierWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportTraitDeclarationStatement.Identifier");
                identifier = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var statementList = context.StatementList != null
                ? this.VisitTyhpdefClassStatementList(context.StatementList)
                : PhpClassBodyAst.Create(null, context);

            return TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("trait", TyhpParser.T_TRAIT, context),
                null,
                identifier,
                null,
                null,
                null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef interface import declaration.
        /// Grammar: tyhpdefImportInterfaceDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? T_INTERFACE
        ///       Identifier=tyhpdefIdentifierWithOptionalAlias
        ///       Extends=interfaceExtendsList FindDocComment=T_OPEN_CURLY_BRACE
        ///       StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportInterfaceDeclarationStatement([NotNull] TyhpParser.TyhpdefImportInterfaceDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            IBase2Ast identifier;
            if (context.Identifier != null)
            {
                identifier = this.VisitTyhpdefIdentifierWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportInterfaceDeclarationStatement.Identifier");
                identifier = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var statementList = context.StatementList != null
                ? this.VisitTyhpdefClassStatementList(context.StatementList)
                : PhpClassBodyAst.Create(null, context);

            return TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("interface", TyhpParser.T_INTERFACE, context),
                null,
                identifier,
                context.Extends != null ? this.VisitInterfaceExtendsList(context.Extends) : null,
                null,
                null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef enum import declaration.
        /// Grammar: tyhpdefImportEnumDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? T_ENUM
        ///       Identifier=tyhpdefIdentifierWithOptionalAlias
        ///       BackingType=enumBackingType Implements=implementsList
        ///       FindDocComment=T_OPEN_CURLY_BRACE
        ///       StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefImportEnumDeclarationStatement([NotNull] TyhpParser.TyhpdefImportEnumDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;

            IBase2Ast identifier;
            if (context.Identifier != null)
            {
                identifier = this.VisitTyhpdefIdentifierWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportEnumDeclarationStatement.Identifier");
                identifier = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var statementList = context.StatementList != null
                ? this.VisitTyhpdefClassStatementList(context.StatementList)
                : PhpClassBodyAst.Create(null, context);

            return TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("enum", TyhpParser.T_ENUM, context),
                null,
                identifier,
                null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                context.BackingType != null ? this.VisitEnumBackingType(context.BackingType) : null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context)
            );
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
                TyhpParser.TyhpdefImportClassConstContext ctx => this.VisitTyhpdefImportClassConst(ctx),
                TyhpParser.TyhpdefImportClassMethodContext ctx => this.VisitTyhpdefImportClassMethod(ctx),
                TyhpParser.TyhpdefEnumCaseContext ctx => this.VisitTyhpdefEnumCase(ctx),
                TyhpParser.TyhpdefTraitUseContext ctx => this.VisitTyhpdefTraitUse(ctx),
                TyhpParser.TyhpdefClassUseExtensionContext ctx => this.VisitTyhpdefClassUseExtension(ctx),
                TyhpParser.TyhpdefExtensionFunctionDeclContext ctx => this.VisitTyhpdefExtensionFunctionDecl(ctx),
                TyhpParser.TyhpdefExtensionOperatorDeclContext ctx => this.VisitTyhpdefExtensionOperatorDecl(ctx),
                TyhpParser.TyhpdefClassOperatorDeclContext ctx => this.VisitTyhpdefClassOperatorDecl(ctx),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "tyhpdefClassStatement")
            };
        }

        /// <summary>
        /// Visits a tyhpdef class property declaration.
        /// Grammar: tyhpdefDeprecatedOrObsolete? Modifiers=propertyModifiers
        ///   TypeExpr=typeExprWithoutStatic PropertyList=tyhpdefPropertyList T_SYM_SEMICOLON
        /// </summary>
        public override PhpPropertyDeclAst VisitTyhpdefClassProperty([NotNull] TyhpParser.TyhpdefClassPropertyContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            ITypeExpression typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassProperty.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            var propertyList = context.PropertyList != null
                ? this.VisitTyhpdefPropertyList(context.PropertyList)
                : TyhpdefPropertyListAst.Create(null, context);

            var result = PhpPropertyDeclAst.Create(
                context.Modifiers != null ? this.VisitPropertyModifiers(context.Modifiers) : null,
                typeExpr,
                // Wrap tyhpdef property list as PhpPropertyListAst for compatibility
                PhpPropertyListAst.Create(
                    propertyList.GetAllNotNull()
                        .Select(p => PhpPropertyAst.Create(p.VariableName, null, null, null, context)),
                    context
                ),
                context,
                GetCurrentLanguageMode(context)
            );

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }
            return result;
        }

        /// <summary>
        /// Visits a tyhpdef import class constant.
        /// Grammar: tyhpdefDeprecatedOrObsolete? Modifiers=methodModifiers T_CONST
        ///   TypeExpr=typeExprWithoutStatic ConstList=tyhpdefImportClassConstList T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportConstDeclListAst VisitTyhpdefImportClassConst([NotNull] TyhpParser.TyhpdefImportClassConstContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var modifiers = context.Modifiers != null
                ? this.VisitMethodModifiers(context.Modifiers)
                : null;

            ITypeExpression? typeExpr;
            if (context.TypeExpr != null)
            {
                typeExpr = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassConst.TypeExpr");
                typeExpr = ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            TyhpdefImportConstDeclListAst constList;
            if (context.ConstList != null)
            {
                constList = this.VisitTyhpdefImportClassConstList(context.ConstList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassConst.ConstList");
                constList = TyhpdefImportConstDeclListAst.Create(null, context);
            }

            if (modifiers != null) constList.AddGrammarAddon("modifiers", modifiers);
            if (typeExpr != null) constList.AddGrammarAddon("typeExpr", typeExpr);
            if (deprecatedOrObsolete != null) constList.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);

            return constList;
        }

        /// <summary>
        /// Visits a tyhpdef import class method.
        /// Grammar: tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC? Modifiers=methodModifiers function
        ///   ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        ///   FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///   T_CLOSE_ROUND_BRACE ReturnType=returnType T_SYM_SEMICOLON
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpdefImportClassMethod([NotNull] TyhpParser.TyhpdefImportClassMethodContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitTyhpdefFunctionNameWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportClassMethod.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            // Name AST nodes such as PhpNameAst carry the method name in ValueString, while the
            // Base2Ast.Identifier property defaults to "" (never null). A plain `Identifier ?? ValueString`
            // therefore always yields the empty string, leaving the method unnamed (and unresolvable as a
            // member). Prefer Identifier only when non-empty, otherwise fall back to ValueString.
            var methodName = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                ? nameOrAlias.Identifier
                : (nameOrAlias.ValueString ?? "");

            var result = PhpMethodDeclAst.Create(
                methodName,
                context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null,
                context.Modifiers != null ? this.VisitMethodModifiers(context.Modifiers) : null,
                context.ParameterList != null ? this.VisitParameterList(context.ParameterList) : null,
                context.ReturnType != null ? this.VisitReturnType(context.ReturnType) : null,
                null, // no body in tyhpdef
                docComment,
                context,
                GetCurrentLanguageMode(context)
            );

            result.AddGrammarAddon("nameOrAlias", nameOrAlias);

            // Method-level generic parameters (e.g. `function with<T, U>(...)`) are attached to the
            // name AST under "GenericArguments". Re-expose them under "identifier" so the shared
            // method binder (PopulateGenericParametersFromGrammarAddon) registers them.
            if (nameOrAlias.AstGrammarAddons.TryGetValue("GenericArguments", out var methodGenerics))
            {
                result.AddGrammarAddon("identifier", methodGenerics);
            }

            if (context.IsAsync != null)
            {
                result.AddGrammarAddon("isAsync", TokenValueAst.Create("async", TyhpParser.T_TYHP_ASYNC, context));
            }
            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }
            return result;
        }

        /// <summary>
        /// Visits <c>use extension</c> inside a tyhpdef class body.
        /// Grammar: tyhpdefDeprecatedOrObsolete? T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        ///   Adaptations=traitAdaptations   #tyhpdefClassUseExtension
        /// </summary>
        public override TyhpImportExtensionAst VisitTyhpdefClassUseExtension([NotNull] TyhpParser.TyhpdefClassUseExtensionContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassUseExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefClassUseExtension.Adaptations");
            }

            var result = TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context));

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            return result;
        }

        /// <summary>
        /// Visits a tyhpdef enum case.
        /// Grammar: tyhpdefDeprecatedOrObsolete? EnumCase=enumCase
        /// </summary>
        public override PhpEnumCaseAst VisitTyhpdefEnumCase([NotNull] TyhpParser.TyhpdefEnumCaseContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            var result = this.VisitEnumCase(context.EnumCase);
            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }
            return result;
        }

        /// <summary>
        /// Visits a tyhpdef trait use.
        /// Grammar: tyhpdefDeprecatedOrObsolete? T_USE TraitNameList=classNameList
        ///   Adaptations=traitAdaptations
        /// </summary>
        public override PhpTraitUseAst VisitTyhpdefTraitUse([NotNull] TyhpParser.TyhpdefTraitUseContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;

            PhpClassNameListAst? traitNames;
            if (context.TraitNameList != null)
            {
                traitNames = this.VisitClassNameList(context.TraitNameList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefTraitUse.TraitNameList");
                traitNames = PhpClassNameListAst.Create(null, context);
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefTraitUse.Adaptations");
            }

            var result = PhpTraitUseAst.Create(
                traitNames,
                adaptations,
                context,
                GetCurrentLanguageMode(context)
            );

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }
            return result;
        }

        /// <summary>
        /// Visits a tyhpdef inline extension function (full or short form).
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
                    TyhpParser.TyhpdefExtensionFunctionFullDeclContext full
                        => this.BuildTyhpdefExtensionFunctionFromFull(full, context, languageMode),
                    TyhpParser.TyhpdefExtensionFunctionShortDeclContext shortDecl
                        => this.BuildTyhpdefExtensionFunctionFromShort(shortDecl, context, languageMode),
                    _ => this.HandleUnexpectedAlternativeSpecial(
                        context,
                        "tyhpdefExtensionFunction",
                        () => PhpMethodDeclAst.CreateError(context, languageMode)),
                };
            }

            var result = TyhpdefInlineExtensionFunctionAst.Create(inner, context, languageMode);

            if (deprecatedOrObsolete != null)
            {
                result.AddGrammarAddon("deprecatedOrObsolete", deprecatedOrObsolete);
            }

            return result;
        }

        /// <summary>
        /// Do not call directly — use <see cref="VisitTyhpdefExtensionFunctionDecl"/> which handles
        /// <c>tyhpdefDeprecatedOrObsolete</c>. This override exists only to prevent ANTLR's default
        /// <c>VisitChildren</c> from running if the method is reached unexpectedly.
        /// </summary>
        public override TyhpdefInlineExtensionFunctionAst VisitTyhpdefExtensionFunctionFullDecl(
            [NotNull] TyhpParser.TyhpdefExtensionFunctionFullDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var inner = this.BuildTyhpdefExtensionFunctionFromFull(context, context, languageMode);
            return TyhpdefInlineExtensionFunctionAst.Create(inner, context, languageMode);
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
                        context,
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

        private PhpMethodDeclAst BuildTyhpdefExtensionFunctionFromFull(
            TyhpParser.TyhpdefExtensionFunctionFullDeclContext full,
            ParserRuleContext context,
            string? languageMode)
        {
            var docComment = this.FindPossibleDocComment(full.FindDocComment);

            PhpNameAst nameAst;
            if (full.GenericIdentifier != null)
            {
                nameAst = this.VisitTyhpOptionalGenericIdentifierWithoutConstructor(full.GenericIdentifier);
            }
            else
            {
                this.ReportMissingRequired(full, "tyhpdefExtensionFunctionFullDecl.GenericIdentifier");
                nameAst = PhpNameAst.CreateError(context, languageMode);
            }

            var genericArgs = (nameAst as TyhpGenericIdentifierAst)?.GenericArguments;

            PhpParameterListAst? parameters;
            if (full.ParameterList != null)
            {
                parameters = this.VisitParameterList(full.ParameterList);
            }
            else
            {
                this.ReportMissingRequired(full, "tyhpdefExtensionFunctionFullDecl.ParameterList");
                parameters = PhpParameterListAst.Create([], context, languageMode);
            }

            ITypeExpression? returnType = null;
            if (full.ReturnType != null)
            {
                returnType = this.VisitReturnType(full.ReturnType);
            }
            else
            {
                this.ReportMissingRequired(full, "tyhpdefExtensionFunctionFullDecl.ReturnType");
            }

            PhpStatementBlockAst? body = null;
            if (full.StatementList != null)
            {
                body = this.VisitMethodBody(full.StatementList);
            }
            else
            {
                this.ReportMissingRequired(full, "tyhpdefExtensionFunctionFullDecl.StatementList");
            }

            return PhpMethodDeclAst.Create(
                    nameAst.ValueString ?? "",
                    full.ReturnsRef != null && this.VisitReturnsRef(full.ReturnsRef) != null,
                    null,
                    parameters,
                    returnType,
                    body,
                    docComment,
                    context,
                    languageMode)
                .WithGrammarAddon("identifier", genericArgs);
        }

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
                    languageMode)
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
                leftParam = this.VisitParameter(fullOp.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(fullOp, "tyhpdefExtensionOperatorFullDecl.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = fullOp.RightParameter != null
                ? this.VisitParameter(fullOp.RightParameter)
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
            if (fullOp.StatementList != null)
            {
                body = this.VisitMethodBody(fullOp.StatementList);
            }
            else if (fullOp.Expr != null)
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
                languageMode);

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
                leftParam = this.VisitParameter(sigOp.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(sigOp, "tyhpdefExtensionOperatorSignatureDecl.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = sigOp.RightParameter != null
                ? this.VisitParameter(sigOp.RightParameter)
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

        /// <summary>
        /// Visits a tyhpdef class operator overload.
        /// Grammar: tyhpdefClassOperator T_SYM_SEMICOLON   #tyhpdefClassOperatorDecl
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpdefClassOperatorDecl([NotNull] TyhpParser.TyhpdefClassOperatorDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var opCtx = context.tyhpdefClassOperator();
            if (opCtx == null)
            {
                this.ReportMissingRequired(context, "tyhpdefClassOperatorDecl.tyhpdefClassOperator");
                return TyhpOperatorOverloadAst.CreateError(context, languageMode);
            }

            TokenValueAst op;
            if (opCtx.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(opCtx.Op);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpdefClassOperator.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            PhpParameterAst leftParam;
            if (opCtx.LeftParameter != null)
            {
                leftParam = this.VisitParameter(opCtx.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpdefClassOperator.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = opCtx.RightParameter != null
                ? this.VisitParameter(opCtx.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (opCtx.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(opCtx.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpdefClassOperator.ConvertReturnType");
            }

            return TyhpOperatorOverloadAst.Create(
                op, leftParam, rightParam, returnType, null, null,
                context, languageMode
            );
        }

        //#region Const and Property Lists

        /// <summary>
        /// Visits a tyhpdef class const declaration.
        /// Grammar: tyhpdefClassConstDecl
        ///   : Identifier=identifier (T_COALESCE CoalesceExpr=expr)?
        /// </summary>
        public override TyhpdefConstDeclAst VisitTyhpdefClassConstDecl([NotNull] TyhpParser.TyhpdefClassConstDeclContext context)
        {
            var name = this.VisitIdentifier(context.Identifier);
            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;
            var docComment = context._findDocComment != null
                ? this.FindPossibleDocComment(context._findDocComment)
                : null;

            return TyhpdefConstDeclAst.Create(
                name.ValueString ?? "",
                coalesceExpr,
                docComment,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef class const list.
        /// Grammar: tyhpdefClassConstList
        ///   : Items+=tyhpdefClassConstDecl (T_SYM_COMMA Items+=tyhpdefClassConstDecl)*
        /// </summary>
        public override TyhpdefConstDeclListAst VisitTyhpdefClassConstList([NotNull] TyhpParser.TyhpdefClassConstListContext context)
            => TyhpdefConstDeclListAst.Create(
                context._Items.Select(this.VisitTyhpdefClassConstDecl),
                context
            );

        /// <summary>
        /// Visits a tyhpdef import class const declaration.
        /// Grammar: tyhpdefImportClassConstDecl
        ///   : Identifier=tyhpdefIdentifierWithOptionalAlias (T_COALESCE CoalesceExpr=expr)?
        /// </summary>
        public override TyhpdefImportConstDeclAst VisitTyhpdefImportClassConstDecl([NotNull] TyhpParser.TyhpdefImportClassConstDeclContext context)
        {
            var aliasedIdentifier = this.VisitTyhpdefIdentifierWithOptionalAlias(context.Identifier);
            var coalesceExpr = context.CoalesceExpr != null
                ? this.VisitExpr(context.CoalesceExpr)
                : null;

            return TyhpdefImportConstDeclAst.Create(
                aliasedIdentifier,
                coalesceExpr,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef import class const list.
        /// Grammar: tyhpdefImportClassConstList
        ///   : Items+=tyhpdefImportClassConstDecl (T_SYM_COMMA Items+=tyhpdefImportClassConstDecl)*
        /// </summary>
        public override TyhpdefImportConstDeclListAst VisitTyhpdefImportClassConstList([NotNull] TyhpParser.TyhpdefImportClassConstListContext context)
            => TyhpdefImportConstDeclListAst.Create(
                context._Items.Select(this.VisitTyhpdefImportClassConstDecl),
                context
            );

        /// <summary>
        /// Visits a single tyhpdef property (just a variable name).
        /// Grammar: tyhpdefProperty
        ///   : Variable=T_VARIABLE
        /// </summary>
        public override TyhpdefPropertyAst VisitTyhpdefProperty([NotNull] TyhpParser.TyhpdefPropertyContext context)
        {
            if (context.Variable == null)
            {
                this.ReportMissingRequired(context, "tyhpdefProperty.Variable");
                return TyhpdefPropertyAst.Create("<error>", context, GetCurrentLanguageMode(context));
            }

            return TyhpdefPropertyAst.Create(
                context.Variable.Text,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        /// <summary>
        /// Visits a tyhpdef property list.
        /// Grammar: tyhpdefPropertyList
        ///   : Items+=tyhpdefProperty (T_SYM_COMMA Items+=tyhpdefProperty)*
        /// </summary>
        public override TyhpdefPropertyListAst VisitTyhpdefPropertyList([NotNull] TyhpParser.TyhpdefPropertyListContext context)
            => TyhpdefPropertyListAst.Create(
                (context._Items ?? []).Where(item => item != null).Select(this.VisitTyhpdefProperty!),
                context
            );

        //#endregion Const and Property Lists

        //#region Identifier Visitors

        /// <summary>
        /// Visits a tyhpdef identifier with optional alias.
        /// Grammar: tyhpdefIdentifierWithOptionalAlias
        ///   : Identifier=tyhpOptionalGenericIdentifier
        ///   | AliasedIdentifier=tyhpdefIdentifierWithAlias
        /// </summary>
        public override IBase2Ast VisitTyhpdefIdentifierWithOptionalAlias([NotNull] TyhpParser.TyhpdefIdentifierWithOptionalAliasContext context)
        {
            if (context.Identifier != null)
            {
                return this.VisitTyhpOptionalGenericIdentifier(context.Identifier);
            }
            else if (context.AliasedIdentifier != null)
            {
                return this.VisitTyhpdefIdentifierWithAlias(context.AliasedIdentifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefIdentifierWithOptionalAlias");
                return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }
        }

        /// <summary>
        /// Dispatches tyhpdefIdentifierWithAlias labeled alternatives.
        /// </summary>
        public TyhpdefIdentifierAliasAst VisitTyhpdefIdentifierWithAlias(TyhpParser.TyhpdefIdentifierWithAliasContext context)
        {
            return context switch
            {
                TyhpParser.TyhpdefIdentifierAliasContext ctx => this.VisitTyhpdefIdentifierAlias(ctx),
                TyhpParser.TyhpdefClassMemberIdentifierAliasContext ctx => this.VisitTyhpdefClassMemberIdentifierAlias(ctx),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpdefIdentifierWithAlias",
                    () => TyhpdefIdentifierAliasAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }

        private T HandleUnexpectedAlternativeSpecial<T>(ParserRuleContext context, string ruleName, Func<T> errorFactory)
        {
            this.ReportUnexpectedAlternative(context, ruleName);
            return errorFactory();
        }

        /// <summary>
        /// Visits a tyhpdef class name with optional alias.
        /// Grammar: tyhpdefClassNameWithOptionalAlias
        ///   : (AliasOf=className T_AS)?
        ///       Identifier=T_STRING GenericParameters=tyhpGenericParameterDeclarations?
        /// </summary>
        public override PhpNameAst VisitTyhpdefClassNameWithOptionalAlias([NotNull] TyhpParser.TyhpdefClassNameWithOptionalAliasContext context)
        {
            if (context.Identifier == null)
            {
                this.ReportMissingRequired(context, "tyhpdefClassNameWithOptionalAlias.Identifier");
                return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var identifier = PhpNameAst.Create(context.Identifier, context);
            if (context.GenericParameters != null)
            {
                var genericParameters = this.VisitTyhpGenericParameterDeclarations(context.GenericParameters);
                identifier.AddGrammarAddon("GenericParameters", genericParameters);
            }

            if (context.AliasOf != null)
            {
                var aliasOf = this.VisitClassName(context.AliasOf);
                identifier.AddGrammarAddon("aliasOf", aliasOf);
            }
            return identifier;
        }

        /// <summary>
        /// Dispatches tyhpdefFunctionNameWithOptionalAlias labeled alternatives.
        /// </summary>
        public IBase2Ast VisitTyhpdefFunctionNameWithOptionalAlias(TyhpParser.TyhpdefFunctionNameWithOptionalAliasContext context)
        {
            return context switch
            {
                TyhpParser.TyhpdefFunctionNameGenericAliasContext ctx => this.VisitTyhpdefFunctionNameGenericAlias(ctx),
                TyhpParser.TyhpdefFunctionNameAliasContext ctx => this.VisitTyhpdefFunctionNameAlias(ctx),
                _ => HandleUnexpectedAlternative<IBase2Ast>(context, "tyhpdefFunctionNameWithOptionalAlias")
            };
        }

        /// <summary>
        /// Visits a tyhpdef function name with generic arguments and optional alias.
        /// Grammar: tyhpdefFunctionNameWithOptionalAlias
        ///   : Identifier=identifier GenericArguments=tyhpGenericParameterDeclarations
        ///       (T_AS AliasedAs=tyhpStringWithOptionalGeneric)?   #tyhpdefFunctionNameGenericAlias
        /// </summary>
        public override PhpNameAst VisitTyhpdefFunctionNameGenericAlias([NotNull] TyhpParser.TyhpdefFunctionNameGenericAliasContext context)
        {
            var name = this.VisitIdentifier(context.Identifier);
            var genericArgs = this.VisitTyhpGenericParameterDeclarations(context.GenericArguments);
            name.AddGrammarAddon("GenericArguments", genericArgs);

            if (context.AliasedAs != null)
            {
                var aliasedAs = this.VisitTyhpStringWithOptionalGeneric(context.AliasedAs);
                name.AddGrammarAddon("aliasedAs", aliasedAs);
            }
            return name;
        }

        /// <summary>
        /// Visits a tyhpdef function name with optional simple alias (no generics).
        /// Grammar: tyhpdefFunctionNameWithOptionalAlias
        ///   : Identifier=identifier (T_AS AliasedAs=T_STRING)?   #tyhpdefFunctionNameAlias
        /// </summary>
        public override PhpNameAst VisitTyhpdefFunctionNameAlias([NotNull] TyhpParser.TyhpdefFunctionNameAliasContext context)
        {
            var name = this.VisitIdentifier(context.Identifier);
            if (context.AliasedAs != null)
            {
                var aliasedAs = PhpNameAst.Create(context.AliasedAs, context);
                name.AddGrammarAddon("aliasedAs", aliasedAs);
            }
            return name;
        }

        /// <summary>
        /// Visits a tyhpdef identifier alias (name AS aliasedName).
        /// Grammar: tyhpdefIdentifierWithAlias
        ///   : Identifier=name T_AS AliasedAs=tyhpOptionalGenericIdentifier   #tyhpdefIdentifierAlias
        /// </summary>
        public override TyhpdefIdentifierAliasAst VisitTyhpdefIdentifierAlias([NotNull] TyhpParser.TyhpdefIdentifierAliasContext context)
            => TyhpdefIdentifierAliasAst.Create(
                null,
                this.VisitName(context.Identifier),
                this.VisitTyhpOptionalGenericIdentifier(context.AliasedAs),
                context,
                GetCurrentLanguageMode(context)
            );

        /// <summary>
        /// Visits a tyhpdef class member identifier alias (ClassName::name AS aliasedName).
        /// Grammar: tyhpdefIdentifierWithAlias
        ///   : ClassName=className T_DOUBLE_COLON Identifier=tyhpOptionalGenericIdentifier
        ///       T_AS AliasedAs=tyhpOptionalGenericIdentifier   #tyhpdefClassMemberIdentifierAlias
        /// </summary>
        public override TyhpdefIdentifierAliasAst VisitTyhpdefClassMemberIdentifierAlias([NotNull] TyhpParser.TyhpdefClassMemberIdentifierAliasContext context)
            => TyhpdefIdentifierAliasAst.Create(
                this.VisitClassName(context.ClassName),
                this.VisitTyhpOptionalGenericIdentifier(context.Identifier),
                this.VisitTyhpOptionalGenericIdentifier(context.AliasedAs),
                context,
                GetCurrentLanguageMode(context)
            );

        //#endregion Identifier Visitors

        //#region Malformed-input recovery helpers

        /// <summary>
        /// Reports <see cref="MessageCode.VisitorMissingRequiredNode"/> for a child that ANTLR
        /// error recovery left null, so callers can continue with a placeholder AST instead of NRE.
        /// </summary>
        private void ReportMissingRequired(ParserRuleContext context, string ruleName)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorMissingRequiredNode,
                this._filename,
                context.Start?.Line ?? 0,
                context.Start?.Column ?? 0,
                ruleName);
        }

        /// <summary>
        /// Reports a missing required child and returns an <see cref="ErrorAst"/> usable wherever
        /// the visitor's interfaces (<see cref="ITopStatement"/>, <see cref="IAttributedStatement"/>, …)
        /// accept error recovery nodes.
        /// </summary>
        private ErrorAst ReportMissingRequiredAsErrorAst(ParserRuleContext context, string ruleName)
        {
            this.ReportMissingRequired(context, ruleName);
            return ErrorAst.Create(context, GetCurrentLanguageMode(context));
        }

        private PhpParameterAst CreateErrorParameter(ParserRuleContext context, string? languageMode)
            => PhpParameterAst.Create(
                "<error>",
                null,
                false,
                false,
                null,
                null,
                null,
                context,
                languageMode);

        private TyhpdefImportObjectDeclAst CreateErrorImportObjectDecl(ParserRuleContext context, string declKind)
        {
            var tokenType = declKind switch
            {
                "trait" => TyhpParser.T_TRAIT,
                "interface" => TyhpParser.T_INTERFACE,
                "enum" => TyhpParser.T_ENUM,
                _ => TyhpParser.T_CLASS,
            };

            return TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create(declKind, tokenType, context),
                null,
                PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                null,
                null,
                null,
                PhpClassBodyAst.Create(null, context),
                false,
                false,
                null,
                context,
                GetCurrentLanguageMode(context));
        }

        //#endregion Malformed-input recovery helpers
    }
}