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
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

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

            var statementList = VisitTyhpdefObjectBodyOrHeaderOnly(
                context.StatementList,
                context.T_SYM_SEMICOLON(),
                context);

            var interfaceDecl = TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create("interface", TyhpParser.T_INTERFACE, context),
                null,
                identifier,
                context.Extends != null ? this.VisitInterfaceExtendsList(context.Extends) : null,
                null,
                null,
                statementList,
                isDeprecated, isObsolete,
                context.FindDocComment != null ? this.FindPossibleDocComment(context.FindDocComment) : null,
                context, GetCurrentLanguageMode(context),
                isPartial: context.IsPartial != null,
                isOmit: isOmit);
            this.AttachOverlayAgainst(interfaceDecl, context);
            return interfaceDecl;
        }
