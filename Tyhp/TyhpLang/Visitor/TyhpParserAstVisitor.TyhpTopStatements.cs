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
        /// Dispatches the topStatementGrammarAddon labeled alternatives to
        /// their corresponding Tyhp visitor methods.
        ///
        /// Grammar (TyhpParser.g4):
        ///   topStatementGrammarAddon
        ///     : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        ///         Adaptations=traitAdaptations                                    #tyhpImportExtension
        ///     | Statement=tyhpTypeAlias                                           #tyhpTypeAliasDecl
        ///     | Statement=tyhpExtensionDeclarationStatement                       #tyhpExtensionDecl
        ///     ;
        /// </summary>
        public override Ast.Interfaces.ITopStatement VisitTopStatementGrammarAddonHandler([NotNull] TyhpParser.TopStatementGrammarAddonHandlerContext context)
        {
            var addon = context.topStatementGrammarAddon();
            return addon switch
            {
                TyhpParser.TyhpTypeAliasDeclContext c => this.VisitTyhpTypeAliasDecl(c),
                TyhpParser.TyhpExtensionDeclContext c => this.VisitTyhpExtensionDecl(c),
                TyhpParser.TyhpInternalConstDeclContext c => this.VisitTyhpInternalConstDecl(c),
                TyhpParser.TyhpFallbackDeclContext c => this.VisitTyhpFallbackDecl(c),
                TyhpParser.TyhpFallbackConstDeclContext c => this.VisitTyhpFallbackConstDecl(c),
                TyhpParser.TyhpImportExtensionContext c => this.VisitTyhpImportExtension(c),
                TyhpParser.TyhpGlobalImportExtensionContext c => this.VisitTyhpGlobalImportExtension(c),
                TyhpParser.TyhpGlobalImportGroupDeclsContext c => this.VisitTyhpGlobalImportGroupDecls(c),
                TyhpParser.TyhpGlobalImportTypedGroupDeclsContext c => this.VisitTyhpGlobalImportTypedGroupDecls(c),
                TyhpParser.TyhpGlobalImportDeclsContext c => this.VisitTyhpGlobalImportDecls(c),
                TyhpParser.TyhpGlobalImportTypeContext c => this.VisitTyhpGlobalImportType(c),
                _ => base.VisitTopStatementGrammarAddonHandler(context),
            };
        }

        /// <summary>
        /// Visits a Tyhp extension import declaration.
        ///
        /// Grammar (TyhpParser.g4):
        ///   topStatementGrammarAddon
        ///     : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        ///         Adaptations=traitAdaptations                                    #tyhpImportExtension
        ///     ;
        ///
        /// Syntax: `use extension Foo\Bar, Baz\Qux { ... }`
        ///
        /// The UseDecl is a comma-separated list of namespace names (use declarations),
        /// and Adaptations are trait-like adaptations (precedence, aliasing) enclosed
        /// in curly braces, or a simple semicolon if no adaptations are needed.
        /// </summary>
        public override TyhpImportExtensionAst VisitTyhpImportExtension([NotNull] TyhpParser.TyhpImportExtensionContext context)
        {
            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpImportExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpImportExtension.Adaptations");
            }

            return TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        public override TyhpImportExtensionAst VisitTyhpGlobalImportExtension(
            [NotNull] TyhpParser.TyhpGlobalImportExtensionContext context)
        {
            PhpImportDeclListAst useDeclarations;
            if (context.UseDecl != null)
            {
                useDeclarations = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportExtension.UseDecl");
                useDeclarations = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            PhpTraitAdaptationListAst? adaptations = null;
            if (context.Adaptations != null)
            {
                adaptations = this.VisitTraitAdaptations(context.Adaptations);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportExtension.Adaptations");
            }

            return TyhpImportExtensionAst.Create(
                useDeclarations,
                adaptations,
                context,
                GetCurrentLanguageMode(context),
                isGlobal: true);
        }

        public override PhpImportDeclListAst VisitTyhpGlobalImportGroupDecls(
            [NotNull] TyhpParser.TyhpGlobalImportGroupDeclsContext context)
        {
            if (context.UseDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportGroupDecls.UseDecl");
                return PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context)).MarkGlobal();
            }

            return this.VisitMixedGroupUseDeclaration(context.UseDecl).MarkGlobal();
        }

        public override PhpImportDeclListAst VisitTyhpGlobalImportTypedGroupDecls(
            [NotNull] TyhpParser.TyhpGlobalImportTypedGroupDeclsContext context)
        {
            TokenValueAst useType;
            if (context.UseType != null)
            {
                useType = this.VisitUseType(context.UseType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportTypedGroupDecls.UseType");
                useType = TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpImportDeclListAst importList;
            if (context.UseDecl != null)
            {
                importList = this.VisitGroupUseDeclaration(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportTypedGroupDecls.UseDecl");
                importList = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            foreach (var import in importList.GetAllNotNull())
            {
                import.SetUseType(useType);
            }

            return importList.MarkGlobal();
        }

        public override PhpImportDeclListAst VisitTyhpGlobalImportDecls(
            [NotNull] TyhpParser.TyhpGlobalImportDeclsContext context)
        {
            if (context.UseDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportDecls.UseDecl");
                return PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context)).MarkGlobal();
            }

            return this.VisitUseDeclarations(context.UseDecl).MarkGlobal();
        }

        public override PhpImportDeclListAst VisitTyhpGlobalImportType(
            [NotNull] TyhpParser.TyhpGlobalImportTypeContext context)
        {
            TokenValueAst useType;
            if (context.UseType != null)
            {
                useType = this.VisitUseType(context.UseType);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportType.UseType");
                useType = TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            PhpImportDeclListAst importList;
            if (context.UseDecl != null)
            {
                importList = this.VisitUseDeclarations(context.UseDecl);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpGlobalImportType.UseDecl");
                importList = PhpImportDeclListAst.Create(null, context, GetCurrentLanguageMode(context));
            }

            foreach (var import in importList.GetAllNotNull())
            {
                import.SetUseType(useType);
            }

            return importList.MarkGlobal();
        }

        public override TyhpTypeAliasAst VisitTyhpTypeAliasDecl([NotNull] TyhpParser.TyhpTypeAliasDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpTypeAliasDecl.Statement");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var alias = this.VisitTyhpTypeAlias(context.Statement)
                .WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
            if (context.IsInternal != null)
            {
                alias.AddGrammarAddon("isInternal", TokenValueAst.Create(context.IsInternal, context));
            }

            return alias;
        }

        public override PhpConstDeclListAst VisitTyhpInternalConstDecl([NotNull] TyhpParser.TyhpInternalConstDeclContext context)
        {
            var list = this.VisitConstList(context.ConstList);
            list.AddGrammarAddon("isInternal", TokenValueAst.Create(context.IsInternal, context));
            return list;
        }

        /// <summary>
        /// <c>fallback function|class|interface|trait|enum</c>. The declaration is an ordinary AST
        /// carrying the fallback marker so the emitter wraps it in an existence check.
        /// </summary>
        public override Ast.Interfaces.IAttributedStatement VisitTyhpFallbackDecl([NotNull] TyhpParser.TyhpFallbackDeclContext context)
        {
            var statement = this.VisitAttributedStatement(context.Statement);
            if (context.Attributes != null)
            {
                statement.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            statement.MarkFallback(context.IsFallback, context);
            return statement;
        }

        public override PhpConstDeclListAst VisitTyhpFallbackConstDecl([NotNull] TyhpParser.TyhpFallbackConstDeclContext context)
        {
            var list = this.VisitConstList(context.ConstList);
            list.MarkFallback(context.IsFallback, context);
            return list;
        }

        public override TyhpExtensionDeclAst VisitTyhpExtensionDecl([NotNull] TyhpParser.TyhpExtensionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpExtensionDecl.Statement");
                return TyhpExtensionDeclAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var decl = this.VisitTyhpExtensionDeclarationStatement(context.Statement);
            if (context.Attributes != null)
            {
                decl.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return decl;
        }




    }
}