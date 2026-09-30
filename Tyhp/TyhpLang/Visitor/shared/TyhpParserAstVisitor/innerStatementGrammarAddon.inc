// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TyhpExtensionDeclAst VisitTyhpInnerExtensionDecl([NotNull] TyhpParser.TyhpInnerExtensionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpInnerExtensionDecl.Statement");
                return TyhpExtensionDeclAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var decl = this.VisitTyhpExtensionDeclarationStatement(context.Statement);
            if (context.Attributes != null)
            {
                decl.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            return decl;
        }


        public override TyhpTypeAliasAst VisitTyhpInnerTypeAliasDecl([NotNull] TyhpParser.TyhpInnerTypeAliasDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpInnerTypeAliasDecl.Statement");
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


        public override IStatement VisitTyhpInnerFallbackDecl([NotNull] TyhpParser.TyhpInnerFallbackDeclContext context)
        {
            var statement = this.VisitAttributedStatement(context.Statement);
            if (context.Attributes != null)
            {
                statement.AddAttributes(this.VisitAttributes(context.Attributes));
            }

            statement.MarkFallback(context.IsFallback, context);
            return statement;
        }


        public override IStatement VisitInnerStatementGrammarAddon([NotNull] TyhpParser.InnerStatementGrammarAddonContext context)
        {
            return context switch
            {
                TyhpParser.TyhpInnerExtensionDeclContext c => this.VisitTyhpInnerExtensionDecl(c),
                TyhpParser.TyhpInnerTypeAliasDeclContext c => this.VisitTyhpInnerTypeAliasDecl(c),
                TyhpParser.TyhpInnerFallbackDeclContext c => this.VisitTyhpInnerFallbackDecl(c),
                _ => base.VisitInnerStatementGrammarAddon(context),
            };
        }
