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
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

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

            var statementList = VisitTyhpdefObjectBodyOrHeaderOnly(
                context.StatementList,
                context.T_SYM_SEMICOLON(),
                context);

            var enumDecl = TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("enum", TyhpParser.T_ENUM, context),
                null,
                identifier,
                null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                context.BackingType != null ? this.VisitEnumBackingType(context.BackingType) : null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context),
                isPartial: context.IsPartial != null,
                isOmit: isOmit);
            this.AttachOverlayAgainst(enumDecl, context);
            return enumDecl;
        }
