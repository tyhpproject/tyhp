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
        /// Dispatches to name-only <c>extern class</c>.
        /// Grammar: Statement=tyhpdefExternClassDeclarationStatement   #tyhpdefExternClassDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternClassDecl([NotNull] TyhpParser.TyhpdefExternClassDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExternClassDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "class");
            }

            return this.VisitTyhpdefExternClassDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to name-only <c>extern interface</c>.
        /// Grammar: Statement=tyhpdefExternInterfaceDeclarationStatement   #tyhpdefExternInterfaceDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternInterfaceDecl([NotNull] TyhpParser.TyhpdefExternInterfaceDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExternInterfaceDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "interface");
            }

            return this.VisitTyhpdefExternInterfaceDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to name-only <c>extern enum</c>.
        /// Grammar: Statement=tyhpdefExternEnumDeclarationStatement   #tyhpdefExternEnumDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternEnumDecl([NotNull] TyhpParser.TyhpdefExternEnumDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExternEnumDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "enum");
            }

            return this.VisitTyhpdefExternEnumDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to name-only kind-unspecified <c>extern \Name;</c>.
        /// Grammar: Statement=tyhpdefExternDeclarationStatement   #tyhpdefExternDecl
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternDecl([NotNull] TyhpParser.TyhpdefExternDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExternDecl.Statement");
                return this.CreateErrorImportObjectDecl(context, "extern");
            }

            return this.VisitTyhpdefExternDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Dispatches to name-only <c>extern function</c>.
        /// Grammar: Statement=tyhpdefExternFunctionDeclarationStatement   #tyhpdefExternFunctionDecl
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefExternFunctionDecl([NotNull] TyhpParser.TyhpdefExternFunctionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExternFunctionDecl.Statement");
                return TyhpdefImportFunctionDeclAst.Create(
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    returnsRef: false,
                    isAsync: false,
                    isExtension: false,
                    parameters: null,
                    returnType: null,
                    isDeprecated: false,
                    isObsolete: false,
                    docComment: null,
                    context,
                    GetCurrentLanguageMode(context),
                    isExtern: true);
            }

            return this.VisitTyhpdefExternFunctionDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a name-only tyhpdef <c>extern function</c> placeholder.
        /// Grammar: tyhpdefExternFunctionDeclarationStatement
        ///   : T_TYHPDEF_EXTERN function Identifier=name T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefExternFunctionDeclarationStatement(
            [NotNull] TyhpParser.TyhpdefExternFunctionDeclarationStatementContext context)
        {
            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefExternFunctionDeclarationStatement.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var functionDecl = TyhpdefImportFunctionDeclAst.Create(
                nameOrAlias,
                returnsRef: false,
                isAsync: false,
                isExtension: false,
                parameters: null,
                returnType: null,
                isDeprecated: false,
                isObsolete: false,
                docComment: this.FindPossibleDocComment(context.Start),
                context,
                GetCurrentLanguageMode(context),
                isExtern: true);
            this.AttachOverlayAgainst(functionDecl, context);
            this.AttachProvidedBy(functionDecl, context);
            return functionDecl;
        }

        /// <summary>
        /// Dispatches to name-only <c>extern const</c>.
        /// Grammar: Statement=tyhpdefExternConstDeclarationStatement   #tyhpdefExternConstDecl
        /// </summary>
        public override TyhpdefImportConstAst VisitTyhpdefExternConstDecl([NotNull] TyhpParser.TyhpdefExternConstDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefExternConstDecl.Statement");
                return TyhpdefImportConstAst.Create(
                    typeExpr: null,
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    coalesceExpr: null,
                    isDeprecated: false,
                    isObsolete: false,
                    docComment: null,
                    context,
                    GetCurrentLanguageMode(context),
                    isExtern: true);
            }

            return this.VisitTyhpdefExternConstDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a name-only tyhpdef <c>extern const</c> placeholder.
        /// Grammar: tyhpdefExternConstDeclarationStatement
        ///   : T_TYHPDEF_EXTERN T_CONST Identifier=name T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportConstAst VisitTyhpdefExternConstDeclarationStatement(
            [NotNull] TyhpParser.TyhpdefExternConstDeclarationStatementContext context)
        {
            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefExternConstDeclarationStatement.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var constDecl = TyhpdefImportConstAst.Create(
                typeExpr: null,
                nameOrAlias,
                coalesceExpr: null,
                isDeprecated: false,
                isObsolete: false,
                docComment: this.FindPossibleDocComment(context.Start),
                context,
                GetCurrentLanguageMode(context),
                isExtern: true);
            this.AttachOverlayAgainst(constDecl, context);
            this.AttachProvidedBy(constDecl, context);
            return constDecl;
        }

        /// <summary>
        /// Visits a name-only tyhpdef <c>extern class</c> placeholder.
        /// Grammar: tyhpdefExternClassDeclarationStatement
        ///   : T_TYHPDEF_EXTERN T_CLASS Identifier=name T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternClassDeclarationStatement([NotNull] TyhpParser.TyhpdefExternClassDeclarationStatementContext context)
            => this.VisitTyhpdefExternObjectDeclaration(
                context,
                context.Identifier,
                "class",
                TyhpParser.T_CLASS,
                "tyhpdefExternClassDeclarationStatement.Identifier");

        /// <summary>
        /// Visits a name-only tyhpdef <c>extern interface</c> placeholder.
        /// Grammar: tyhpdefExternInterfaceDeclarationStatement
        ///   : T_TYHPDEF_EXTERN T_INTERFACE Identifier=name T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternInterfaceDeclarationStatement([NotNull] TyhpParser.TyhpdefExternInterfaceDeclarationStatementContext context)
            => this.VisitTyhpdefExternObjectDeclaration(
                context,
                context.Identifier,
                "interface",
                TyhpParser.T_INTERFACE,
                "tyhpdefExternInterfaceDeclarationStatement.Identifier");

        /// <summary>
        /// Visits a name-only tyhpdef <c>extern enum</c> placeholder.
        /// Grammar: tyhpdefExternEnumDeclarationStatement
        ///   : T_TYHPDEF_EXTERN T_ENUM Identifier=name T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternEnumDeclarationStatement([NotNull] TyhpParser.TyhpdefExternEnumDeclarationStatementContext context)
            => this.VisitTyhpdefExternObjectDeclaration(
                context,
                context.Identifier,
                "enum",
                TyhpParser.T_ENUM,
                "tyhpdefExternEnumDeclarationStatement.Identifier");

        /// <summary>
        /// Visits a name-only kind-unspecified tyhpdef <c>extern \Name;</c> placeholder.
        /// Grammar: tyhpdefExternDeclarationStatement
        ///   : T_TYHPDEF_EXTERN Identifier=name T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportObjectDeclAst VisitTyhpdefExternDeclarationStatement([NotNull] TyhpParser.TyhpdefExternDeclarationStatementContext context)
            => this.VisitTyhpdefExternObjectDeclaration(
                context,
                context.Identifier,
                "extern",
                TyhpParser.T_TYHPDEF_EXTERN,
                "tyhpdefExternDeclarationStatement.Identifier");

        private TyhpdefImportObjectDeclAst VisitTyhpdefExternObjectDeclaration(
            ParserRuleContext context,
            TyhpParser.NameContext? identifierContext,
            string declKind,
            int tokenType,
            string missingIdentifierField)
        {
            var docComment = this.FindPossibleDocComment(context.Start);

            PhpNameAst identifier;
            if (identifierContext != null)
            {
                identifier = this.VisitName(identifierContext);
            }
            else
            {
                this.ReportMissingRequired(context, missingIdentifierField);
                identifier = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var externDecl = TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create(declKind, tokenType, context),
                modifiers: null,
                identifier,
                extends: null,
                implements: null,
                backingType: null,
                PhpClassBodyAst.Create(null, context),
                isDeprecated: false,
                isObsolete: false,
                docComment,
                context,
                GetCurrentLanguageMode(context),
                isPartial: false,
                isOmit: false,
                isExtern: true);
            this.AttachOverlayAgainst(externDecl, context);
            this.AttachProvidedBy(externDecl, context);
            return externDecl;
        }
