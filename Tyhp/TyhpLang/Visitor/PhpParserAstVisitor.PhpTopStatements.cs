namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override Ast.PhpTopStatementListAst? VisitTopStatementListWithRequiredFinalTerminal([NotNull] TyhpParser.TopStatementListWithRequiredFinalTerminalContext context)
            => this.VisitTopStatementListWithRequiredFinalTerminal(context, false);

        public Ast.PhpTopStatementListAst? VisitTopStatementListWithRequiredFinalTerminal([NotNull] TyhpParser.TopStatementListWithRequiredFinalTerminalContext context, bool isCurrentTopStatementList)
        {
            var result = PhpTopStatementListAst.Create(null, context, GetCurrentLanguageMode(context));
            if (isCurrentTopStatementList) {
                this.CurrentTopStatementList = result;
            }
            
            // Add all top statements from the Items collection
            if (context._Items != null) {
                foreach (var item in context._Items) {
                    var topStatement = this.VisitTopStatement(item);
                    if (topStatement != null) {
                        result.Add(topStatement);
                    }
                }
            }
            
            return result;
        }

        public override Ast.Interfaces.ITopStatement VisitTopStatement([NotNull] TyhpParser.TopStatementContext context)
        {
            if (context.topStatementNoTerminal() != null) {
                return this.VisitTopStatementNoTerminal(context.topStatementNoTerminal());
            } else if (context.topStatementNeedsTerminal() != null) {
                return this.HandleWithStatementTerminal(
                    this.VisitTopStatementNeedsTerminal(context.topStatementNeedsTerminal()),
                    context.statementTerminal(),
                    context
                );
            }

            this.ReportUnexpectedAlternative(context, "topStatement");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public ITopStatement VisitTopStatementNoTerminal([NotNull] TyhpParser.TopStatementNoTerminalContext context)
            => context switch {
                TyhpParser.TopStatementStatementWithoutTerminalContext topStatementStatementWithoutTerminalContext => this.VisitTopStatementStatementWithoutTerminal(topStatementStatementWithoutTerminalContext),
                TyhpParser.AttributedConstTopStatementContext attributedConstTopStatementContext => this.VisitAttributedConstTopStatement(attributedConstTopStatementContext),
                TyhpParser.AttributedTopStatementContext attributedTopStatementContext => this.VisitAttributedTopStatement(attributedTopStatementContext),
                TyhpParser.NameSpaceDeclContext nameSpaceDeclContext => this.VisitNameSpaceDecl(nameSpaceDeclContext),
                TyhpParser.NamespaceGroupDeclContext namespaceGroupDeclContext => this.VisitNamespaceGroupDecl(namespaceGroupDeclContext),
                TyhpParser.AnonNamespaceDeclContext anonNamespaceDeclContext => this.VisitAnonNamespaceDecl(anonNamespaceDeclContext),
                TyhpParser.ImportGroupDeclsContext importGroupDeclsContext => this.VisitImportGroupDecls(importGroupDeclsContext),
                TyhpParser.ImportTypeGroupDeclsContext importTypeGroupDeclsContext => this.VisitImportTypeGroupDecls(importTypeGroupDeclsContext),
                TyhpParser.ImportDeclsContext importDeclsContext => this.VisitImportDecls(importDeclsContext),
                TyhpParser.ImportTypeContext importTypeContext => this.VisitImportType(importTypeContext),
                TyhpParser.ConstDeclStmtContext constDeclStmtContext => this.VisitConstDeclStmt(constDeclStmtContext),
                TyhpParser.TopStatementGrammarAddonHandlerContext topStatementGrammarAddonHandlerContext => this.VisitTopStatementGrammarAddonHandler(topStatementGrammarAddonHandlerContext),
                _ => this.VisitTopStatementNoTerminalAlt(context),
            };

        public virtual ITopStatement VisitTopStatementNoTerminalAlt(TyhpParser.TopStatementNoTerminalContext context)
        {
            this.ReportUnexpectedAlternative(context, "topStatementNoTerminal");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public ITopStatement VisitTopStatementNeedsTerminal([NotNull] TyhpParser.TopStatementNeedsTerminalContext context)
        {
            return context switch {
                TyhpParser.TopStatementRequiringTerminalContext topStatementRequiringTerminalContext =>
                    this.VisitTopStatementRequiringTerminal(topStatementRequiringTerminalContext) as ITopStatement
                    ?? this.HandleFailedCast(context, "topStatementRequiringTerminal"),
                TyhpParser.TopStatementHaltCompilerContext topStatementHaltCompilerContext =>
                    this.VisitTopStatementHaltCompiler(topStatementHaltCompilerContext),
                _ => this.VisitTopStatementNeedsTerminalAlt(context),
            };
        }

        private ITopStatement HandleFailedCast(ParserRuleContext context, string ruleName)
        {
            this.ReportUnexpectedAlternative(context, ruleName, "cast failed to ITopStatement");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public virtual ITopStatement VisitTopStatementNeedsTerminalAlt(TyhpParser.TopStatementNeedsTerminalContext context)
        {
            this.ReportUnexpectedAlternative(context, "topStatementNeedsTerminal");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override Ast.Interfaces.ITopStatement VisitTopStatementStatementWithoutTerminal([NotNull] TyhpParser.TopStatementStatementWithoutTerminalContext context)
            => this.VisitStatementWithoutTerminal(context.Statement);

        public override Ast.Interfaces.IAttributedStatement VisitAttributedTopStatement([NotNull] TyhpParser.AttributedTopStatementContext context)
        {
            var statement = this.VisitAttributedStatement(context.Statement);
            if (context.Attributes != null)
            {
                var attributes = this.VisitAttributes(context.Attributes);
                statement.AddAttributes(attributes);
            }
            return statement;
        }

        /// <summary>
        /// PHP 8.5 attributed top-level <c>const</c>: attributes attach to the const declaration
        /// list (single declarator — multi-const with attributes is rejected by the grammar).
        /// </summary>
        public override Ast.Interfaces.ITopStatement VisitAttributedConstTopStatement([NotNull] TyhpParser.AttributedConstTopStatementContext context)
        {
            var constList = this.VisitAttributedConstStatement(context.Statement);
            return constList.WithAttributes(this.VisitAttributes(context.Attributes));
        }

        public override PhpConstDeclListAst VisitAttributedConstStatement([NotNull] TyhpParser.AttributedConstStatementContext context)
            => PhpConstDeclListAst.Create(
                [this.VisitConstDecl(context.ConstDecl)],
                context,
                GetCurrentLanguageMode(context)
            );

        public override PhpNamespaceDeclAst VisitNameSpaceDecl([NotNull] TyhpParser.NameSpaceDeclContext context)
        {
            var namespaceName = this.VisitNamespaceDeclarationName(context.NamespaceName);
            return PhpNamespaceDeclAst.Create(namespaceName.ValueString, null, context, GetCurrentLanguageMode(context));
        }

        public override PhpNamespaceDeclAst VisitNamespaceGroupDecl([NotNull] TyhpParser.NamespaceGroupDeclContext context)
        {
            var namespaceName = this.VisitNamespaceDeclarationName(context.NamespaceName);
            var statementList = context.StatementList != null ? this.VisitTopStatementListWithRequiredFinalTerminal(context.StatementList) : null;
            return PhpNamespaceDeclAst.Create(namespaceName.ValueString, statementList, context, GetCurrentLanguageMode(context));
        }

        public override PhpNamespaceDeclAst VisitAnonNamespaceDecl([NotNull] TyhpParser.AnonNamespaceDeclContext context)
        {
            var statementList = context.StatementList != null ? this.VisitTopStatementListWithRequiredFinalTerminal(context.StatementList) : null;
            return PhpNamespaceDeclAst.Create(null, statementList, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclListAst VisitImportGroupDecls([NotNull] TyhpParser.ImportGroupDeclsContext context)
            => this.VisitMixedGroupUseDeclaration(context.UseDecl);

        public override PhpImportDeclListAst VisitImportTypeGroupDecls([NotNull] TyhpParser.ImportTypeGroupDeclsContext context)
        {
            var useType = this.VisitUseType(context.UseType);
            var importList = this.VisitGroupUseDeclaration(context.UseDecl);
            
            // Apply the use type to all imports in the list
            foreach (var import in importList.GetAllNotNull()) {
                import.SetUseType(useType);
            }
            
            return importList;
        }

        public override PhpImportDeclListAst VisitImportDecls([NotNull] TyhpParser.ImportDeclsContext context)
            => this.VisitUseDeclarations(context.UseDecl);

        public override PhpImportDeclListAst VisitImportType([NotNull] TyhpParser.ImportTypeContext context)
        {
            var useType = this.VisitUseType(context.UseType);
            var importList = this.VisitUseDeclarations(context.UseDecl);
            
            // Apply the use type to all imports in the list
            foreach (var import in importList.GetAllNotNull()) {
                import.SetUseType(useType);
            }
            
            return importList;
        }

        public override PhpConstDeclListAst VisitConstDeclStmt([NotNull] TyhpParser.ConstDeclStmtContext context)
            => this.VisitConstList(context.ConstList);

        public override Ast.Interfaces.ITopStatement VisitTopStatementGrammarAddonHandler([NotNull] TyhpParser.TopStatementGrammarAddonHandlerContext context)
        {
            this.ReportUnexpectedAlternative(context, "topStatementGrammarAddonHandler");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }

        public override IStatement VisitTopStatementRequiringTerminal([NotNull] TyhpParser.TopStatementRequiringTerminalContext context)
            => this.VisitStatementRequiringTerminal(context.Statement);

        public override PhpHaltCompilerAst VisitTopStatementHaltCompiler([NotNull] TyhpParser.TopStatementHaltCompilerContext context)
            => PhpHaltCompilerAst.Create(context, GetCurrentLanguageMode(context));

        public override TokenValueAst VisitUseType([NotNull] TyhpParser.UseTypeContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitUseTypeGrammarAddon(context.TokenValueGrammarAddon)
            ) ?? TokenValueAst.Create("", 0, context);

        public override TokenValueAst VisitUseTypeGrammarAddon([NotNull] TyhpParser.UseTypeGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "useTypeGrammarAddon");
            return TokenValueAst.Create("", 0, context);
        }

        public override PhpImportDeclListAst VisitGroupUseDeclaration([NotNull] TyhpParser.GroupUseDeclarationContext context)
        {
            var namespaceName = this.VisitLegacyNamespaceName(context.NamespaceName);
            var useDeclList = this.VisitUnprefixedUseDeclarations(context.UseDeclList);
            
            var prefix = namespaceName.ValueString + (context.NsSep?.Text ?? "");
            
            // Create new import declarations with the prefix applied
            var newItems = new List<PhpImportDeclAst>();
            foreach (var useDecl in useDeclList.GetAllNotNull()) {
                var prefixedNamespace = prefix + useDecl.NamespaceName;
                var newImport = PhpImportDeclAst.Create(
                    useDecl.UseType,
                    prefixedNamespace,
                    useDecl.Identifier, // Keep the alias if any
                    context,
                    GetCurrentLanguageMode(context)
                );
                newItems.Add(newImport);
            }
            
            return PhpImportDeclListAst.Create(newItems, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclListAst VisitMixedGroupUseDeclaration([NotNull] TyhpParser.MixedGroupUseDeclarationContext context)
        {
            var namespaceName = this.VisitLegacyNamespaceName(context.NamespaceName);
            var useDeclList = this.VisitInlineUseDeclarations(context.UseDeclList);
            
            var prefix = namespaceName.ValueString + (context.NsSep?.Text ?? "");
            
            // Create new import declarations with the prefix applied
            var newItems = new List<PhpImportDeclAst>();
            foreach (var useDecl in useDeclList.GetAllNotNull()) {
                var prefixedNamespace = prefix + useDecl.NamespaceName;
                var newImport = PhpImportDeclAst.Create(
                    useDecl.UseType,
                    prefixedNamespace,
                    useDecl.Identifier, // Keep the alias if any
                    context,
                    GetCurrentLanguageMode(context)
                );
                newItems.Add(newImport);
            }
            
            return PhpImportDeclListAst.Create(newItems, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclListAst VisitInlineUseDeclarations([NotNull] TyhpParser.InlineUseDeclarationsContext context)
        {
            var items = context._Items?.Select(this.VisitInlineUseDeclaration) ?? null;
            return PhpImportDeclListAst.Create(items, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclListAst VisitUnprefixedUseDeclarations([NotNull] TyhpParser.UnprefixedUseDeclarationsContext context)
        {
            var items = context._Items?.Select(this.VisitUnprefixedUseDeclaration) ?? null;
            return PhpImportDeclListAst.Create(items, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclListAst VisitUseDeclarations([NotNull] TyhpParser.UseDeclarationsContext context)
        {
            var items = context._Items?.Select(this.VisitUseDeclaration) ?? null;
            return PhpImportDeclListAst.Create(items, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclAst VisitInlineUseDeclaration([NotNull] TyhpParser.InlineUseDeclarationContext context)
        {
            var useType = context.UseType != null ? this.VisitUseType(context.UseType) : null;
            var useDecl = this.VisitUnprefixedUseDeclaration(context.UseDecl);
            
            // Apply the inline use type if specified
            if (useType != null) {
                useDecl.SetUseType(useType);
            }
            
            return useDecl;
        }

        public override PhpImportDeclAst VisitUnprefixedUseDeclaration([NotNull] TyhpParser.UnprefixedUseDeclarationContext context)
        {
            if (context.NamespaceName != null) {
                var namespaceName = this.VisitNamespaceName(context.NamespaceName);
                var aliasedAs = context.AliasedAs?.Text;
                
                return PhpImportDeclAst.Create(
                    null, // Use type will be set at higher level if needed
                    namespaceName.ValueString,
                    aliasedAs,
                    context,
                    GetCurrentLanguageMode(context)
                );
            } else if (context.unprefixedUseDeclarationGrammarAddon() != null) {
                return this.VisitUnprefixedUseDeclarationGrammarAddon(context.unprefixedUseDeclarationGrammarAddon());
            }

            this.ReportUnexpectedAlternative(context, "unprefixedUseDeclaration");
            return PhpImportDeclAst.Create(null, "", null, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclAst VisitUnprefixedUseDeclarationGrammarAddon([NotNull] TyhpParser.UnprefixedUseDeclarationGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "unprefixedUseDeclarationGrammarAddon");
            return PhpImportDeclAst.Create(null, "", null, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclAst VisitUseDeclaration([NotNull] TyhpParser.UseDeclarationContext context)
        {
            if (context.NamespaceName != null) {
                var namespaceName = this.VisitLegacyNamespaceName(context.NamespaceName);
                var aliasedAs = context.AliasedAs?.Text;
                
                return PhpImportDeclAst.Create(
                    null, // Use type will be set at higher level if needed
                    namespaceName.ValueString,
                    aliasedAs,
                    context,
                    GetCurrentLanguageMode(context)
                );
            } else if (context.useDeclarationGrammarAddon() != null) {
                return this.VisitUseDeclarationGrammarAddon(context.useDeclarationGrammarAddon());
            }

            this.ReportUnexpectedAlternative(context, "useDeclaration");
            return PhpImportDeclAst.Create(null, "", null, context, GetCurrentLanguageMode(context));
        }

        public override PhpImportDeclAst VisitUseDeclarationGrammarAddon([NotNull] TyhpParser.UseDeclarationGrammarAddonContext context)
        {
            this.ReportUnexpectedAlternative(context, "useDeclarationGrammarAddon");
            return PhpImportDeclAst.Create(null, "", null, context, GetCurrentLanguageMode(context));
        }
    }
}