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
        /// Dispatches to function declaration statement.
        /// Grammar: Statement=tyhpdefImportFunctionDeclarationStatement   #tyhpdefImportFunctionDecl
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefImportFunctionDecl([NotNull] TyhpParser.TyhpdefImportFunctionDeclContext context)
        {
            if (context.Statement == null)
            {
                this.ReportMissingRequired(context, "tyhpdefImportFunctionDecl.Statement");
                return TyhpdefImportFunctionDeclAst.Create(
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    false, false, false, null, null, false, false, null,
                    context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefImportFunctionDeclarationStatement(context.Statement);
        }

        /// <summary>
        /// Visits a tyhpdef function import declaration.
        /// Grammar: tyhpdefImportFunctionDeclarationStatement
        ///   : tyhpdefDeprecatedOrObsolete? IsFallback=T_TYHP_FALLBACK? IsAsync=T_TYHP_ASYNC? function
        ///       ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        ///       FindDocComment=T_OPEN_ROUND_BRACE IsExtension=T_EXTENDS?
        ///       ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///       T_SYM_SEMICOLON
        /// </summary>
        public override TyhpdefImportFunctionDeclAst VisitTyhpdefImportFunctionDeclarationStatement([NotNull] TyhpParser.TyhpdefImportFunctionDeclarationStatementContext context)
        {
            var deprecatedOrObsolete = context.tyhpdefDeprecatedOrObsolete() != null
                ? this.VisitTyhpdefDeprecatedOrObsolete(context.tyhpdefDeprecatedOrObsolete())
                : null;
            var isDeprecated = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_DEPRECATED;
            var isObsolete = deprecatedOrObsolete?.ValueInt64 == TyhpParser.T_TYHPDEF_OBSOLETE;
            ReadTyhpdefDeclModifier(deprecatedOrObsolete, out isDeprecated, out isObsolete, out var isOmit);

            IBase2Ast nameOrAlias;
            if (context.Identifier != null)
            {
                nameOrAlias = this.VisitTyhpdefFunctionNameWithOptionalAlias(context.Identifier);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpdefImportFunctionDeclarationStatement.Identifier");
                nameOrAlias = PhpNameAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var returnsRef = context.ReturnsRef != null && this.VisitReturnsRef(context.ReturnsRef) != null;
            var isAsync = context.IsAsync != null;
            var isExtension = context.IsExtension != null;
            var parameters = context.ParameterList != null
                ? this.VisitParameterList(context.ParameterList)
                : null;
            var returnType = context.ReturnType != null
                ? this.VisitReturnType(context.ReturnType)
                : null;
            var docComment = context.FindDocComment != null
                ? this.FindPossibleDocComment(context.FindDocComment)
                : null;

            var functionDecl = TyhpdefImportFunctionDeclAst.Create(
                nameOrAlias, returnsRef, isAsync, isExtension,
                parameters, returnType,
                isDeprecated, isObsolete, docComment,
                context, GetCurrentLanguageMode(context),
                isOmit: isOmit,
                isFallback: context.IsFallback != null);
            this.AttachOverlayAgainst(functionDecl, context);
            return functionDecl;
        }
