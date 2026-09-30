namespace Tyhp.TyhpLang.Visitor
{
    using System.Linq;
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {







        private PhpFunctionDeclAst CreateLiveExtensionFunctionDecl(
            Antlr4.Runtime.ParserRuleContext context,
            TyhpParser.TyhpOptionalGenericIdentifierWithoutConstructorContext? identifierCtx,
            TyhpParser.ReturnsRefContext? returnsRefCtx,
            TyhpParser.FunctionModifiersGrammarAddonContext? modifiersCtx,
            TyhpParser.TyhpExtensionCallableParametersContext? parametersCtx,
            TyhpParser.ReturnTypeContext? returnTypeCtx,
            PhpStatementBlockAst? body,
            bool isShortSyntax,
            string? languageMode,
            string? docComment)
        {
            PhpNameAst nameAst;
            if (identifierCtx != null)
            {
                nameAst = this.VisitTyhpOptionalGenericIdentifierWithoutConstructor(identifierCtx);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionMember.GenericIdentifier");
                nameAst = PhpNameAst.CreateError(context, languageMode);
            }

            var genericArgs = (nameAst as TyhpGenericIdentifierAst)?.GenericArguments;

            var parameters = this.ReadExtensionCallableParameters(
                parametersCtx,
                context,
                languageMode,
                out var byRefReceiver);

            ITypeExpression? returnType = null;
            if (returnTypeCtx != null)
            {
                returnType = this.VisitReturnType(returnTypeCtx);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionMember.ReturnType");
            }

            var ast = PhpFunctionDeclAst.Create(
                    nameAst.ValueString ?? "",
                    returnsRefCtx != null && this.VisitReturnsRef(returnsRefCtx) != null,
                    parameters,
                    returnType,
                    body,
                    context,
                    languageMode,
                    docComment,
                    isShortSyntax)
                .WithGrammarAddon(
                    "modifiers",
                    modifiersCtx != null ? this.VisitFunctionModifiersGrammarAddon(modifiersCtx) : null)
                .WithGrammarAddon("identifier", genericArgs);
            if (byRefReceiver != null)
            {
                ast.AddGrammarAddon(
                    TyhpExtensionDeclAst.ByRefReceiverAddonKey,
                    TokenValueAst.Create(byRefReceiver, context, languageMode));
            }

            return ast;
        }








        internal TyhpExtensionDeclAst CreateExtensionTargetGroup(
            ParserRuleContext context,
            TyhpParser.TyhpGenericParameterDeclarationsContext? genericParameters,
            TyhpParser.TypeExprWithoutStaticContext? targetType,
            IToken? findDocComment,
            TyhpExtensionFunctionListAst? functionList)
        {
            if (functionList == null)
            {
                this.ReportMissingRequired(context, "tyhpExtensionTargetGroup.FunctionList");
                functionList = TyhpExtensionFunctionListAst.Create(null, context);
            }

            if (targetType == null)
            {
                this.ReportMissingRequired(context, "tyhpExtensionTargetGroup.TargetType");
            }

            var group = TyhpExtensionDeclAst.Create(
                "",
                functionList,
                findDocComment != null ? this.FindPossibleDocComment(findDocComment) : null,
                context);
            group.IsTargetGroup = true;
            this.AttachExtensionBlockTarget(group, genericParameters, targetType);
            return group;
        }

        internal void AttachExtensionBlockTarget(
            TyhpExtensionDeclAst decl,
            TyhpParser.TyhpGenericParameterDeclarationsContext? genericParameters,
            TyhpParser.TypeExprWithoutStaticContext? targetType)
        {
            if (genericParameters != null)
            {
                decl.GenericParameters = this.VisitTyhpGenericParameterDeclarations(genericParameters);
            }

            if (targetType != null)
            {
                decl.TargetType = this.VisitTypeExprWithoutStatic(targetType);
            }
        }

        internal void AttachExtensionBlockTarget(
            TyhpdefStandaloneExtensionDeclAst decl,
            TyhpParser.TyhpGenericParameterDeclarationsContext? genericParameters,
            TyhpParser.TypeExprWithoutStaticContext? targetType)
        {
            if (genericParameters != null)
            {
                decl.GenericParameters = this.VisitTyhpGenericParameterDeclarations(genericParameters);
            }

            if (targetType != null)
            {
                decl.TargetType = this.VisitTypeExprWithoutStatic(targetType);
            }
        }

        internal PhpParameterListAst ReadExtensionCallableParameters(
            TyhpParser.TyhpExtensionCallableParametersContext? parameters,
            ParserRuleContext owner,
            string? languageMode,
            out IToken? byRefReceiver)
        {
            byRefReceiver = null;
            if (parameters == null)
            {
                this.ReportMissingRequired(owner, "tyhpExtensionCallableParameters");
                return PhpParameterListAst.Create([], owner, languageMode);
            }

            if (parameters.LegacyExtends != null)
            {
                this.Diagnostics.AddError(
                    MessageCode.ParserExtensionLegacyMemberTarget,
                    this._filename,
                    parameters.LegacyExtends.Line,
                    parameters.LegacyExtends.Column);
            }

            if (parameters.ReceiverVar != null)
            {
                byRefReceiver = parameters.ReceiverVar;
            }

            if (parameters.ParameterList != null)
            {
                return this.VisitParameterList(parameters.ParameterList)
                    ?? PhpParameterListAst.Create([], parameters, languageMode);
            }

            if (parameters.RestParameters != null)
            {
                return this.VisitNonEmptyParameterList(parameters.RestParameters);
            }

            return PhpParameterListAst.Create([], parameters, languageMode);
        }

        internal void ReportLegacyOperatorTarget(
            TyhpParser.TyhpExtensionOperatorLegacyTargetContext? legacy,
            ParserRuleContext context)
        {
            if (legacy == null)
            {
                return;
            }

            var token = legacy.Start ?? context.Start;
            this.Diagnostics.AddError(
                MessageCode.ParserExtensionLegacyMemberTarget,
                this._filename,
                token?.Line ?? 0,
                token?.Column ?? 0);
        }
    }
}
