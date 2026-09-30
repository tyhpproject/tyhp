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

        public override TyhpImportExtensionAst VisitTyhpdefGlobalImportExtension(
            [NotNull] TyhpParser.TyhpdefGlobalImportExtensionContext context)
        {
            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportExtension.Adaptations");
            }

            return TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context),
                isGlobal: true);
        }

        public override PhpImportDeclListAst VisitTyhpdefGlobalImportGroupDecls(
            [NotNull] TyhpParser.TyhpdefGlobalImportGroupDeclsContext context)
        {
            if (context.UseDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportGroupDecls.UseDecl");
                return PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context)).MarkGlobal();
            }

            return this.VisitMixedGroupUseDeclaration(context.UseDecl).MarkGlobal();
        }

        public override PhpImportDeclListAst VisitTyhpdefGlobalImportTypedGroupDecls(
            [NotNull] TyhpParser.TyhpdefGlobalImportTypedGroupDeclsContext context)
        {
            TokenValueAst useType;
            if (context.UseType != null)
            {
                useType = this.VisitUseType(context.UseType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportTypedGroupDecls.UseType");
                useType = TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpImportDeclListAst importList;
            if (context.UseDecl != null)
            {
                importList = this.VisitGroupUseDeclaration(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportTypedGroupDecls.UseDecl");
                importList = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            foreach (var import in importList.GetAllNotNull())
            {
                import.SetUseType(useType);
            }

            return importList.MarkGlobal();
        }

        public override PhpImportDeclListAst VisitTyhpdefGlobalImportDecls(
            [NotNull] TyhpParser.TyhpdefGlobalImportDeclsContext context)
        {
            if (context.UseDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportDecls.UseDecl");
                return PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context)).MarkGlobal();
            }

            return this.VisitUseDeclarations(context.UseDecl).MarkGlobal();
        }

        public override PhpImportDeclListAst VisitTyhpdefGlobalImportType(
            [NotNull] TyhpParser.TyhpdefGlobalImportTypeContext context)
        {
            TokenValueAst useType;
            if (context.UseType != null)
            {
                useType = this.VisitUseType(context.UseType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportType.UseType");
                useType = TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpImportDeclListAst importList;
            if (context.UseDecl != null)
            {
                importList = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefGlobalImportType.UseDecl");
                importList = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            foreach (var import in importList.GetAllNotNull())
            {
                import.SetUseType(useType);
            }

            return importList.MarkGlobal();
        }
