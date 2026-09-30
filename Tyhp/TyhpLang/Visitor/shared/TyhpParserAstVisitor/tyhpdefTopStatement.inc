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
                TyhpParser.TyhpdefGlobalImportExtensionContext ctx => this.VisitTyhpdefGlobalImportExtension(ctx),
                TyhpParser.TyhpdefGlobalImportGroupDeclsContext ctx => this.VisitTyhpdefGlobalImportGroupDecls(ctx),
                TyhpParser.TyhpdefGlobalImportTypedGroupDeclsContext ctx => this.VisitTyhpdefGlobalImportTypedGroupDecls(ctx),
                TyhpParser.TyhpdefGlobalImportDeclsContext ctx => this.VisitTyhpdefGlobalImportDecls(ctx),
                TyhpParser.TyhpdefGlobalImportTypeContext ctx => this.VisitTyhpdefGlobalImportType(ctx),
                TyhpParser.TyhpdefTypeAliasDeclContext ctx => this.VisitTyhpdefTypeAliasDecl(ctx),
                TyhpParser.TyhpdefStandaloneExtensionTopDeclContext ctx => this.VisitTyhpdefStandaloneExtensionTopDecl(ctx),
                _ => HandleUnexpectedAlternative<ITopStatement>(context, "tyhpdefTopStatement")
            };
        }

        /// <summary>
        /// Visits a non-attributed tyhpdef top statement (empty, declare, const, variable).
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

            // Attributes sit on this wrapper, not on the inner function/class rule, so
            // AttachOverlayAgainst from the keyword misses `// @overlay-against:` written
            // before `#[…]`. Retry from the attribute start only when the inner visit
            // did not already pick up a stamp between the attribute and the keyword.
            if (statement is Base2Ast attributed
                && !attributed.AstGrammarAddons.ContainsKey("overlayAgainst"))
            {
                this.AttachOverlayAgainst(attributed, context);
            }

            return statement;
        }
