namespace Tyhp.TyhpLang.Visitor
{
    using System;
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;

    public partial class TyhpdefParserAstVisitor
    {









        protected PhpFunctionDeclAst CreateLiveExtensionFunctionDecl(
            Antlr4.Runtime.ParserRuleContext context,
            TyhpdefParser.TyhpOptionalGenericIdentifierWithoutConstructorContext? identifierCtx,
            TyhpdefParser.ReturnsRefContext? returnsRefCtx,
            TyhpdefParser.FunctionModifiersGrammarAddonContext? modifiersCtx,
            TyhpdefParser.TyhpExtensionCallableParametersContext? parametersCtx,
            TyhpdefParser.ReturnTypeContext? returnTypeCtx,
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
            TyhpdefParser.TyhpGenericParameterDeclarationsContext? genericParameters,
            TyhpdefParser.TypeExprWithoutStaticContext? targetType,
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
            TyhpdefParser.TyhpGenericParameterDeclarationsContext? genericParameters,
            TyhpdefParser.TypeExprWithoutStaticContext? targetType)
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
            TyhpdefParser.TyhpExtensionCallableParametersContext? parameters,
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
            TyhpdefParser.TyhpExtensionOperatorLegacyTargetContext? legacy,
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











        /// <summary>
        /// Maps a PHP cast token type to the builtin type name used by the emitter's
        /// BuildDefaultExpression to select the zero value. <c>T_VOID_CAST</c> only
        /// reaches here from <c>callableType</c>'s BuiltinCast alternative (an unnamed
        /// <c>callable(void): R</c> parameter) — <c>typeof</c>/<c>default</c> do not
        /// accept <c>T_VOID_CAST</c>, so <c>default(void)</c> is not a thing this maps.
        /// </summary>
        protected static string CastTokenTypeToTypeName(int tokenType) => tokenType switch
        {
            TyhpdefParser.T_INT_CAST => "int",
            TyhpdefParser.T_STRING_CAST => "string",
            TyhpdefParser.T_BOOL_CAST => "bool",
            TyhpdefParser.T_ARRAY_CAST => "array",
            TyhpdefParser.T_DOUBLE_CAST => "float",
            TyhpdefParser.T_OBJECT_CAST => "object",
            TyhpdefParser.T_DECIMAL_CAST => "decimal",
            TyhpdefParser.T_VOID_CAST => "void",
            _ => "mixed",
        };















        /// <summary>
        /// Helper: creates a PhpMethodDeclAst from a tyhpClassMethod context (function keyword).
        /// Dispatches to the inner tyhpMethodDefinition alternatives.
        /// </summary>
        protected PhpMethodDeclAst CreateTyhpClassMethod(
            TyhpdefParser.TyhpClassMethodContext context,
            PhpModifierListAst? modifiers)
        {
            var returnsRef = this.VisitReturnsRef(context.ReturnsRef) != null;
            var methodDef = context.tyhpMethodDefinition();

            return methodDef switch
            {
                TyhpdefParser.TyhpClassCtorWithReturnTypeContext ctx
                    => this.CreateTyhpClassCtor(ctx, returnsRef, modifiers),
                TyhpdefParser.TyhpClassGenericMethodContext ctx
                    => this.CreateTyhpClassGenericMethod(ctx, returnsRef, modifiers),
                _ => HandleUnexpectedAlternativeSpecial(context, "tyhpMethodDefinition",
                    () => PhpMethodDeclAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }





        /// <summary>
        /// Helper: creates a PhpMethodDeclAst for a Tyhp constructor with return type.
        ///
        /// Grammar:
        ///   Identifier=T_CONSTRUCT_METHOD
        ///     FindDocComment=T_OPEN_ROUND_BRACE ParameterList=ctorParameterList
        ///     T_CLOSE_ROUND_BRACE ReturnType=tyhpCtorReturnType?
        ///     StatementList=methodBody
        ///
        /// `ReturnType` is optional. Omitting it is equivalent to `: void`: no
        /// `ctorReturnType` grammar addon is attached, so the emitter skips the
        /// `parent::__construct(...)` insertion and the checker treats the ctor
        /// as void (its ordinary ReturnType slot is always null for constructors).
        /// </summary>
        protected PhpMethodDeclAst CreateTyhpClassCtor(
            TyhpdefParser.TyhpClassCtorWithReturnTypeContext context,
            bool returnsRef,
            PhpModifierListAst? modifiers)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpMethodDeclAst.Create(
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                returnsRef,
                modifiers,
                this.VisitCtorParameterList(context.ParameterList),
                null, // ctor return type is not a standard type expression
                this.VisitMethodBody(context.StatementList),
                docComment,
                context,
                GetCurrentLanguageMode(context)
            ).WithGrammarAddon(
                "ctorReturnType",
                context.ReturnType is null ? null : this.VisitTyhpCtorReturnType(context.ReturnType));
        }





        /// <summary>
        /// Helper: creates a PhpMethodDeclAst for a Tyhp generic class method.
        ///
        /// Grammar:
        ///   GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        ///     FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///     T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///     StatementList=methodBody
        /// </summary>
        protected PhpMethodDeclAst CreateTyhpClassGenericMethod(
            TyhpdefParser.TyhpClassGenericMethodContext context,
            bool returnsRef,
            PhpModifierListAst? modifiers)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var genericIdentifier = this.VisitTyhpGenericIdentifierWithoutConstructor(context.GenericIdentifier);

            return PhpMethodDeclAst.Create(
                genericIdentifier.ValueString,
                returnsRef,
                modifiers,
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitMethodBody(context.StatementList),
                docComment,
                context,
                GetCurrentLanguageMode(context)
            ).WithGrammarAddon("identifier", genericIdentifier.GenericArguments);
        }





        /// <summary>
        /// Helper: creates a PhpMethodDeclAst for a Tyhp short (fn/arrow) class method.
        ///
        /// Grammar:
        ///   fn ReturnsRef=returnsRef
        ///     GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        ///     FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///     T_CLOSE_ROUND_BRACE OptionalReturnType=returnType T_DOUBLE_ARROW
        ///     Expr=expr T_SYM_SEMICOLON
        ///
        /// The expression is wrapped in a return statement to form the method body,
        /// following the same pattern as arrow functions in PhpInlineFunctionAst.
        /// </summary>
        protected PhpMethodDeclAst CreateTyhpClassGenericMethodShort(
            TyhpdefParser.TyhpClassGenericMethodShortContext context,
            PhpModifierListAst? modifiers)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var identifier = this.VisitTyhpOptionalGenericIdentifierWithoutConstructor(context.GenericIdentifier);
            var genericArguments = (identifier as TyhpGenericIdentifierAst)?.GenericArguments;
            var expr = this.VisitExpr(context.Expr);

            // Wrap the expression in a return statement, mirroring arrow function behavior
            var body = PhpStatementBlockAst.Create(
                [PhpUnaryOpAst.Create(
                    TokenValueAst.Create("return", TyhpdefParser.T_RETURN, context),
                    expr,
                    context,
                    languageMode
                )],
                context,
                languageMode
            );

            return PhpMethodDeclAst.Create(
                identifier.ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                modifiers,
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.OptionalReturnType),
                body,
                docComment,
                context,
                languageMode,
                isShortSyntax: true
            ).WithGrammarAddon("identifier", genericArguments);
        }
















        protected PhpTraitMemberRefAst BuildOperatorTraitMemberRef(
            IClassName? traitName,
            TyhpdefParser.TyhpClassOperatorOverloadOpContext? op,
            TyhpdefParser.TypeExprWithoutStaticContext? targetType,
            ParserRuleContext context)
        {
            string operatorToken;
            if (op != null)
            {
                var opAst = this.VisitTyhpClassOperatorOverloadOp(op);
                operatorToken = opAst.ValueString ?? opAst.Identifier ?? "";
            }
            else
            {
                this.ReportMissingRequired(context, "traitOperatorMethodReference.Op");
                operatorToken = "";
            }

            var target = targetType != null ? this.VisitTypeExprWithoutStatic(targetType) : null;
            return PhpTraitMemberRefAst.CreateOperator(
                traitName,
                operatorToken,
                target,
                context,
                GetCurrentLanguageMode(context));
        }














        protected PhpModifierListAst? VisitOptionalInternalModifierList(
            ParserRuleContext context,
            IToken? internalToken)
        {
            if (internalToken is null)
            {
                return null;
            }

            var list = PhpModifierListAst.Create([PhpModifier.Internal], context);
            list.AddGrammarAddon("isInternal", TokenValueAst.Create(internalToken, context));
            return list;
        }







        protected IExpression? TryVisitTypeGuardIndex(TyhpdefParser.TyhpReturnTypeGuardContext context)
        {
            if (context.GuardIndexVariable != null)
            {
                return PhpVariableAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexVariable),
                    false,
                    context);
            }

            if (context.GuardIndexInt != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexInt),
                    PhpScalarType.Integer,
                    context);
            }

            if (context.GuardIndexHex != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexHex),
                    PhpScalarType.HexNumber,
                    context);
            }

            if (context.GuardIndexOct != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexOct),
                    PhpScalarType.OctalNumber,
                    context);
            }

            if (context.GuardIndexBin != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexBin),
                    PhpScalarType.BinaryNumber,
                    context);
            }

            if (context.GuardIndexString != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexString),
                    PhpScalarType.String,
                    context);
            }

            return null;
        }











        protected static string ScalarTypeSpelling(TyhpdefParser.TyhpScalarTypeContext context)
        {
            var text = context.GetText();
            return string.IsNullOrEmpty(text)
                ? context.Start?.Text ?? "<unknown>"
                : text;
        }


        protected void AttachExtensionBlockTarget(
            TyhpdefStandaloneExtensionDeclAst decl,
            TyhpdefParser.TyhpGenericParameterDeclarationsContext? genericParameters,
            TyhpdefParser.TypeExprWithoutStaticContext? targetType)
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
    }
}
