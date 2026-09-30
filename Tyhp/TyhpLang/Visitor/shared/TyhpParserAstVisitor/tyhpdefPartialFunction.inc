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
        /// Dispatches to name-only overlay <c>partial function</c>.
        /// Grammar: Statement=tyhpdefPartialFunctionDeclarationStatement   #tyhpdefPartialFunctionDecl
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefPartialFunctionDecl([NotNull] TyhpParser.TyhpdefPartialFunctionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefPartialFunctionDecl.Statement");
                return TyhpdefImportFunctionDeclAst.Create(
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    false, false, false, null, null, false, false, null,
                    context, GetCurrentLanguageMode(context),
                    isPartial: true);
            }

            return this.VisitTyhpdefPartialFunctionDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a name-only overlay <c>partial function</c> (attributes only; no signature).
        /// Grammar: T_TYHPDEF_PARTIAL function Identifier=tyhpdefPartialFunctionName T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefPartialFunctionDeclarationStatement(
            [NotNull] TyhpParser.TyhpdefPartialFunctionDeclarationStatementContext context)
        {
            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitTyhpdefPartialFunctionName(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefPartialFunctionDeclarationStatement.Identifier");
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
                docComment: null,
                context,
                GetCurrentLanguageMode(context),
                isPartial: true);
            this.AttachPartialFunctionAlias(nameOrAlias, context.AliasedAs, context);
            this.AttachOverlayAgainst(functionDecl, context);
            return functionDecl;
        }

        /// <summary>
        /// Visits a name-only <c>partial function</c> identifier (FQCN or semi-reserved; no generics).
        /// Optional <c>as</c> is attached by the declaration visitor, not this name rule.
        /// </summary>
        public override PhpNameAst VisitTyhpdefPartialFunctionName(
            [NotNull] TyhpParser.TyhpdefPartialFunctionNameContext context)
        {
            if (context.Identifier != null)
            {
                return this.VisitName(context.Identifier);
            }

            if (context.SemiReserved != null)
            {
                var reserved = this.VisitSemiReserved(context.SemiReserved);
                return PhpNameAst.Create(reserved.ValueString ?? "", 0, context);
            }

            this.ReportMissingRequired(context, "tyhpdefPartialFunctionName");
            return PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
        }
