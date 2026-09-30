namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;
    using static Tyhp.TyhpLang.Enum.PhpModifierExtensions;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {











        /// <summary>
        /// Helper: creates a PhpMethodDeclAst from a tyhpClassMethod context (function keyword).
        /// Dispatches to the inner tyhpMethodDefinition alternatives.
        /// </summary>
        private PhpMethodDeclAst CreateTyhpClassMethod(
            TyhpParser.TyhpClassMethodContext context,
            PhpModifierListAst? modifiers)
        {
            var returnsRef = this.VisitReturnsRef(context.ReturnsRef) != null;
            var methodDef = context.tyhpMethodDefinition();

            return methodDef switch
            {
                TyhpParser.TyhpClassCtorWithReturnTypeContext ctx
                    => this.CreateTyhpClassCtor(ctx, returnsRef, modifiers),
                TyhpParser.TyhpClassGenericMethodContext ctx
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
        private PhpMethodDeclAst CreateTyhpClassCtor(
            TyhpParser.TyhpClassCtorWithReturnTypeContext context,
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
        private PhpMethodDeclAst CreateTyhpClassGenericMethod(
            TyhpParser.TyhpClassGenericMethodContext context,
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
        private PhpMethodDeclAst CreateTyhpClassGenericMethodShort(
            TyhpParser.TyhpClassGenericMethodShortContext context,
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
                    TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
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












        private PhpTraitMemberRefAst BuildOperatorTraitMemberRef(
            IClassName? traitName,
            TyhpParser.TyhpClassOperatorOverloadOpContext? op,
            TyhpParser.TypeExprWithoutStaticContext? targetType,
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










        private PhpModifierListAst? VisitOptionalInternalModifierList(
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


        //#endregion Modifier and Parameter Grammar Addons
    }
}
