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
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

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

            var statementList = VisitTyhpdefObjectBodyOrHeaderOnly(
                context.StatementList,
                context.T_SYM_SEMICOLON(),
                context);

            var traitDecl = TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("trait", TyhpParser.T_TRAIT, context),
                null,
                identifier,
                null,
                null,
                null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context),
                isPartial: context.IsPartial != null,
                isOmit: isOmit);
            this.AttachOverlayAgainst(traitDecl, context);
            return traitDecl;
        }
