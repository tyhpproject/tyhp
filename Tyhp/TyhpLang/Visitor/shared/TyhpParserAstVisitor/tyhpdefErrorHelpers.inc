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

        //#endregion Identifier Visitors

        //#region Malformed-input recovery helpers

        /// <summary>
        /// Reports <see cref="MessageCode.VisitorMissingRequiredNode"/> for a child that ANTLR
        /// error recovery left null, so callers can continue with a placeholder AST instead of NRE.
        /// </summary>
        private void ReportMissingRequired(ParserRuleContext context, string ruleName)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorMissingRequiredNode,
                this._filename,
                context.Start?.Line ?? 0,
                context.Start?.Column ?? 0,
                ruleName);
        }

        /// <summary>
        /// Reports a missing required child and returns an <see cref="ErrorAst"/> usable wherever
        /// the visitor's interfaces (<see cref="ITopStatement"/>, <see cref="IAttributedStatement"/>, …)
        /// accept error recovery nodes.
        /// </summary>
        private ErrorAst ReportMissingRequiredAsErrorAst(ParserRuleContext context, string ruleName)
        {
            this.ReportMissingRequired(context, ruleName);
            return ErrorAst.Create(context, GetCurrentLanguageMode(context));
        }

        private PhpParameterAst CreateErrorParameter(ParserRuleContext context, string? languageMode)
            => PhpParameterAst.Create(
                "<error>",
                null,
                false,
                false,
                null,
                null,
                null,
                context,
                languageMode);

        private TyhpdefImportObjectDeclAst CreateErrorImportObjectDecl(ParserRuleContext context, string declKind)
        {
            var tokenType = declKind switch
            {
                "trait" => TyhpParser.T_TRAIT,
                "interface" => TyhpParser.T_INTERFACE,
                "enum" => TyhpParser.T_ENUM,
                "extern" => TyhpParser.T_TYHPDEF_EXTERN,
                _ => TyhpParser.T_CLASS,
            };

            return TyhpdefImportObjectDeclAst.Create(
                TokenValueAst.Create(declKind, tokenType, context),
                null,
                PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                null,
                null,
                null,
                PhpClassBodyAst.Create(null, context),
                false,
                false,
                null,
                context,
                GetCurrentLanguageMode(context));
        }
