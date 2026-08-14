namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    using Tyhp.TyhpLang.Enum;
    using static Tyhp.TyhpLang.Enum.PhpModifierExtensions;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        //#region Name Grammar Addons (Generics on class/trait/interface/enum names)

        /// <summary>
        /// Visits the class name grammar addon, returning generic type arguments
        /// if present in Tyhp mode.
        ///
        /// Grammar (TyhpParser):
        ///   classNameGrammarAddon
        ///     : GenericArguments=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitClassNameGrammarAddon([NotNull] TyhpParser.ClassNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;

        /// <summary>
        /// Visits the trait name grammar addon, returning generic type arguments
        /// if present in Tyhp mode.
        ///
        /// Grammar (TyhpParser):
        ///   traitNameGrammarAddon
        ///     : GenericArguments=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitTraitNameGrammarAddon([NotNull] TyhpParser.TraitNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;

        /// <summary>
        /// Visits the interface name grammar addon, returning generic type arguments
        /// if present in Tyhp mode.
        ///
        /// Grammar (TyhpParser):
        ///   interfaceNameGrammarAddon
        ///     : GenericArguments=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitInterfaceNameGrammarAddon([NotNull] TyhpParser.InterfaceNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;

        /// <summary>
        /// Visits the enum name grammar addon, returning generic type arguments
        /// if present in Tyhp mode.
        ///
        /// Grammar (TyhpParser):
        ///   enumNameGrammarAddon
        ///     : GenericArguments=tyhpGenericParameterDeclarations?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitEnumNameGrammarAddon([NotNull] TyhpParser.EnumNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericParameterDeclarations(context.GenericArguments)
                : null;

        //#endregion Name Grammar Addons

        //#region Tyhp Constructor Return Type

        /// <summary>
        /// Visits a Tyhp constructor return type annotation.
        ///
        /// Grammar:
        ///   tyhpCtorReturnType
        ///     : T_SYM_COLON TokenValue=T_TYHP_VOID
        ///     | T_SYM_COLON TokenValue=T_TYHP_PARENT ArgumentsList=argumentList
        ///     ;
        ///
        /// Returns a TyhpCtorReturnTypeAst with the type token and optional
        /// argument list for parent constructor delegation.
        /// </summary>
        public override TyhpCtorReturnTypeAst VisitTyhpCtorReturnType([NotNull] TyhpParser.TyhpCtorReturnTypeContext context)
        {
            var typeToken = this.GetTokenValueAst(context, context.TokenValue);
            var arguments = context.ArgumentsList != null
                ? this.VisitArgumentList(context.ArgumentsList)
                : null;

            return TyhpCtorReturnTypeAst.Create(typeToken, arguments, context);
        }

        //#endregion Tyhp Constructor Return Type

        //#region Attributed Class Statement Grammar Addon (Tyhp method declarations)

        /// <summary>
        /// Visits the attributed class statement grammar addon for Tyhp method
        /// declarations (regular methods, short methods, and constructors).
        ///
        /// Grammar (TyhpParser):
        ///   attributedClassStatementGrammarAddon
        ///     : Modifiers=methodModifiers tyhpClassMethodDefinition
        ///     ;
        ///
        /// Dispatches to the appropriate method definition handler based on
        /// the type of tyhpClassMethodDefinition alternative.
        /// </summary>
        public override IClassMember VisitAttributedClassStatementGrammarAddon([NotNull] TyhpParser.AttributedClassStatementGrammarAddonContext context)
        {
            var modifiers = this.VisitMethodModifiers(context.Modifiers);
            var methodDef = context.tyhpClassMethodDefinition();

            return methodDef switch
            {
                TyhpParser.TyhpClassMethodContext ctx
                    => this.CreateTyhpClassMethod(ctx, modifiers),
                TyhpParser.TyhpClassGenericMethodShortContext ctx
                    => this.CreateTyhpClassGenericMethodShort(ctx, modifiers),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "attributedClassStatementGrammarAddon")
            };
        }

        /// <summary>
        /// Visits a Tyhp class method definition (function keyword variant).
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassMethodContext.
        ///
        /// Grammar:
        ///   tyhpClassMethodDefinition
        ///     : function ReturnsRef=returnsRef tyhpMethodDefinition  #tyhpClassMethod
        ///     ;
        ///
        /// Note: When called standalone (not via VisitAttributedClassStatementGrammarAddon),
        /// modifiers will be null since they live on the parent context.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassMethod([NotNull] TyhpParser.TyhpClassMethodContext context)
            => this.CreateTyhpClassMethod(context, null);

        /// <summary>
        /// Visits a Tyhp short (arrow) class method definition.
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassGenericMethodShortContext.
        ///
        /// Grammar:
        ///   tyhpClassMethodDefinition
        ///     : fn ReturnsRef=returnsRef
        ///         GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        ///         FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///         T_CLOSE_ROUND_BRACE OptionalReturnType=returnType T_DOUBLE_ARROW
        ///         Expr=expr T_SYM_SEMICOLON {this.isLanguageMode("tyhp")}?
        ///                                                            #tyhpClassGenericMethodShort
        ///     ;
        ///
        /// Note: When called standalone, modifiers will be null.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassGenericMethodShort([NotNull] TyhpParser.TyhpClassGenericMethodShortContext context)
            => this.CreateTyhpClassGenericMethodShort(context, null);

        /// <summary>
        /// Visits a Tyhp constructor with explicit return type.
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassCtorWithReturnTypeContext.
        ///
        /// Grammar:
        ///   tyhpMethodDefinition
        ///     : Identifier=T_CONSTRUCT_METHOD
        ///         FindDocComment=T_OPEN_ROUND_BRACE ParameterList=ctorParameterList
        ///         T_CLOSE_ROUND_BRACE ReturnType=tyhpCtorReturnType
        ///         StatementList=methodBody                            #tyhpClassCtorWithReturnType
        ///     ;
        ///
        /// Note: When called standalone, modifiers and returnsRef will be defaults.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassCtorWithReturnType([NotNull] TyhpParser.TyhpClassCtorWithReturnTypeContext context)
            => this.CreateTyhpClassCtor(context, false, null);

        /// <summary>
        /// Visits a Tyhp generic class method definition.
        /// Called by ANTLR dispatch when Visit() is used on a TyhpClassGenericMethodContext.
        ///
        /// Grammar:
        ///   tyhpMethodDefinition
        ///     : GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        ///         FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        ///         T_CLOSE_ROUND_BRACE ReturnType=returnType
        ///         StatementList=methodBody                            #tyhpClassGenericMethod
        ///     ;
        ///
        /// Note: When called standalone, modifiers and returnsRef will be defaults.
        /// </summary>
        public override PhpMethodDeclAst VisitTyhpClassGenericMethod([NotNull] TyhpParser.TyhpClassGenericMethodContext context)
            => this.CreateTyhpClassGenericMethod(context, false, null);

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
        ///     T_CLOSE_ROUND_BRACE ReturnType=tyhpCtorReturnType
        ///     StatementList=methodBody
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
            ).WithGrammarAddon("ctorReturnType", this.VisitTyhpCtorReturnType(context.ReturnType));
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
                languageMode
            ).WithGrammarAddon("identifier", genericArguments);
        }

        //#endregion Attributed Class Statement Grammar Addon

        //#region Class Statement Grammar Addon (Type aliases and Operator overloads)

        /// <summary>
        /// Dispatches class statement grammar addon alternatives.
        /// Overrides the PhpParserAstVisitor's throwing implementation.
        ///
        /// Grammar (TyhpParser):
        ///   classStatementGrammarAddon
        ///     : Modifier=nonEmptyMemberModifiers? TypeAlias=tyhpTypeAlias
        ///         {this.isLanguageMode("tyhp")}?          #tyhpClassTypeAlias
        ///     | OperatorOverload=tyhpClassOperatorOverload
        ///         {this.isLanguageMode("tyhp")}?          #tyhpClassOperatorOverload
        ///     ;
        /// </summary>
        public override IClassMember VisitClassStatementGrammarAddon([NotNull] TyhpParser.ClassStatementGrammarAddonContext context)
        {
            return context switch
            {
                TyhpParser.TyhpClassTypeAliasContext ctx => this.VisitTyhpClassTypeAlias(ctx),
                TyhpParser.TyhpClassOperatorOverloadDeclContext ctx => this.VisitTyhpClassOperatorOverloadDecl(ctx),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "classStatementGrammarAddon")
            };
        }

        /// <summary>
        /// Visits a type alias declaration inside a class body.
        ///
        /// Grammar:
        ///   classStatementGrammarAddon
        ///     : Modifier=nonEmptyMemberModifiers? TypeAlias=tyhpTypeAlias
        ///         {this.isLanguageMode("tyhp")}?          #tyhpClassTypeAlias
        ///     ;
        /// </summary>
        public override TyhpTypeAliasAst VisitTyhpClassTypeAlias([NotNull] TyhpParser.TyhpClassTypeAliasContext context)
        {
            var modifiers = context.Modifier != null
                ? this.VisitNonEmptyMemberModifiers(context.Modifier)
                : null;

            if (context.TypeAlias is null)
            {
                this.ReportMissingRequired(context, "tyhpClassTypeAlias.TypeAlias");
                return TyhpTypeAliasAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            // Delegate so truncated `type Foo` inside a class body gets the same
            // Identifier / TypeExpr null guards as the top-level type-alias visitor.
            var typeAlias = this.VisitTyhpTypeAlias(context.TypeAlias);
            return TyhpTypeAliasAst.Create(
                typeAlias.Name ?? PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                typeAlias.GenericArguments,
                typeAlias.TypeExpression
                    ?? PhpTypeExpressionAst.CreateError(context, GetCurrentLanguageMode(context)),
                context,
                modifiers);
        }

        /// <summary>
        /// Visits an operator overload declaration inside a class body.
        ///
        /// Grammar:
        ///   tyhpClassOperatorOverload (via classStatementGrammarAddon #tyhpClassOperatorOverload)
        ///     : Modifier=(T_ABSTRACT | T_FINAL)? T_TYHP_OPERATOR
        ///         Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        ///         functionParametersGrammarAddon LeftParameter=parameter
        ///         (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ///         ConvertReturnType=returnType
        ///         (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr))
        ///     ;
        ///
        /// Creates a TyhpOperatorOverloadAst with the operator, parameters,
        /// return type, and body (either method body or shorthand expression
        /// wrapped in a return statement).
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpClassOperatorOverloadDecl([NotNull] TyhpParser.TyhpClassOperatorOverloadDeclContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var opCtx = context.tyhpClassOperatorOverload();
            if (opCtx == null)
            {
                this.ReportMissingRequired(context, "tyhpClassOperatorOverloadDecl.tyhpClassOperatorOverload");
                return TyhpOperatorOverloadAst.CreateError(context, languageMode);
            }

            TokenValueAst op;
            if (opCtx.Op != null)
            {
                op = this.VisitTyhpClassOperatorOverloadOp(opCtx.Op);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpClassOperatorOverload.Op");
                op = TokenValueAst.CreateError(context, languageMode);
            }

            PhpParameterAst leftParam;
            if (opCtx.LeftParameter != null)
            {
                leftParam = this.VisitParameter(opCtx.LeftParameter);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpClassOperatorOverload.LeftParameter");
                leftParam = this.CreateErrorParameter(context, languageMode);
            }

            var rightParam = opCtx.RightParameter != null
                ? this.VisitParameter(opCtx.RightParameter)
                : null;

            ITypeExpression? returnType = null;
            if (opCtx.ConvertReturnType != null)
            {
                returnType = this.VisitReturnType(opCtx.ConvertReturnType);
            }
            else
            {
                this.ReportMissingRequired(opCtx, "tyhpClassOperatorOverload.ConvertReturnType");
            }

            PhpStatementBlockAst? body;
            if (opCtx.StatementList != null)
            {
                body = this.VisitMethodBody(opCtx.StatementList);
            }
            else if (opCtx.ShorthandExpr != null)
            {
                // Shorthand expression: => expr
                var expr = this.VisitExpr(opCtx.ShorthandExpr);
                body = PhpStatementBlockAst.Create(
                    [PhpUnaryOpAst.Create(
                        TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                        expr,
                        context,
                        languageMode
                    )],
                    context,
                    languageMode
                );
            }
            else
            {
                body = null;
            }

            PhpModifier? modifier = opCtx.Modifier != null
                ? FromToken(opCtx.Modifier.Type)
                : null;

            return TyhpOperatorOverloadAst.Create(
                op, leftParam, rightParam, returnType, body, modifier,
                context, languageMode
            );
        }

        /// <summary>
        /// Visits the operator token in an operator overload declaration.
        ///
        /// Grammar:
        ///   tyhpClassOperatorOverloadOp
        ///     : TokenValue=T_SYM_PLUS
        ///     | TokenValue=T_SYM_MINUS
        ///     | ... (many operator token alternatives)
        ///     | TokenValue=T_STRING   // for true, false, empty, null, convert
        ///     ;
        ///
        /// Returns a TokenValueAst holding the operator's text and token type.
        /// </summary>
        public override TokenValueAst VisitTyhpClassOperatorOverloadOp([NotNull] TyhpParser.TyhpClassOperatorOverloadOpContext context)
        {
            if (context.TokenValue == null)
            {
                this.ReportMissingRequired(context, "tyhpClassOperatorOverloadOp.TokenValue");
                return TokenValueAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            return context.TokenValue.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT && context.IsSR?.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT ?
                TokenValueAst.Create(">>", TyhpLang.Parser.TyhpParser.T_SR, context) :
                this.GetTokenValueAst(context, context.TokenValue);
        }

        //#endregion Class Statement Grammar Addon

        //#region Trait Alias Grammar Addon

        /// <summary>
        /// Dispatches trait alias grammar addon alternatives.
        /// Overrides the PhpParserAstVisitor's throwing implementation.
        ///
        /// Grammar (TyhpParser):
        ///   traitAliasGrammarAddon
        ///     : AliasOf=traitPropertyReference T_AS AliasString=T_VARIABLE
        ///         {this.isLanguageMode("tyhp")}?          #tyhpTraitAliasPropertyRename
        ///     ;
        /// </summary>
        public override PhpTraitAliasAst VisitTraitAliasGrammarAddon([NotNull] TyhpParser.TraitAliasGrammarAddonContext context)
        {
            return context switch
            {
                TyhpParser.TyhpTraitAliasPropertyRenameContext ctx => this.VisitTyhpTraitAliasPropertyRename(ctx),
                _ => HandleUnexpectedAlternativeSpecial(context, "traitAliasGrammarAddon",
                    () => PhpTraitAliasAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }

        /// <summary>
        /// Visits a trait alias property rename declaration.
        /// This is a Tyhp extension that allows renaming trait properties
        /// (not just methods) when using traits.
        ///
        /// Grammar:
        ///   traitAliasGrammarAddon
        ///     : AliasOf=traitPropertyReference T_AS AliasString=T_VARIABLE
        ///         {this.isLanguageMode("tyhp")}?          #tyhpTraitAliasPropertyRename
        ///     ;
        ///
        /// Creates a PhpTraitAliasAst with the property reference and the
        /// new variable name (AliasString is a T_VARIABLE token).
        /// </summary>
        public override PhpTraitAliasAst VisitTyhpTraitAliasPropertyRename([NotNull] TyhpParser.TyhpTraitAliasPropertyRenameContext context)
        {
            var aliasOf = this.VisitTraitPropertyReference(context.AliasOf);
            var newName = context.AliasString.Text;

            return PhpTraitAliasAst.Create(
                aliasOf,
                newName,
                context,
                GetCurrentLanguageMode(context)
            );
        }

        //#endregion Trait Alias Grammar Addon

        //#region Identifier and Name Grammar Addons

        /// <summary>
        /// Visits the trait alias name grammar addon, returning generic type
        /// arguments if present.
        ///
        /// Grammar (TyhpParser):
        ///   traitAliasNameGrammarAddon
        ///     : GenericArguments=tyhpGenericTypeArguments?
        ///         {this.isLanguageMode("tyhp")}?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitTraitAliasNameGrammarAddon([NotNull] TyhpParser.TraitAliasNameGrammarAddonContext context)
            => context.GenericArguments != null
                ? this.VisitTyhpGenericTypeArguments(context.GenericArguments)
                : null;

        /// <summary>
        /// Visits the trait method identifier grammar addon, returning a generic
        /// identifier if present.
        ///
        /// Grammar (TyhpParser):
        ///   traitMethodIdentifierGrammarAddon
        ///     : (GenericIdentifier=tyhpGenericIdentifier
        ///         {this.isLanguageMode("tyhp")}?)?
        ///     ;
        /// </summary>
        public override IBase2Ast? VisitTraitMethodIdentifierGrammarAddon([NotNull] TyhpParser.TraitMethodIdentifierGrammarAddonContext context)
            => context.GenericIdentifier != null
                ? this.VisitTyhpGenericIdentifier(context.GenericIdentifier)
                : null;

        //#endregion Identifier and Name Grammar Addons

        //#region Modifier and Parameter Grammar Addons

        /// <summary>
        /// Visits the member modifier grammar addon for the Tyhp async keyword.
        ///
        /// Grammar (TyhpParser):
        ///   memberModifierGrammarAddon
        ///     : TokenValue=T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
        ///     ;
        ///
        /// Returns a TokenValueAst for the async modifier token.
        /// </summary>
        public override TokenValueAst VisitMemberModifierGrammarAddon([NotNull] TyhpParser.MemberModifierGrammarAddonContext context)
            => this.GetTokenValueAst(context, context.TokenValue);

        /// <summary>
        /// Preserve Tyhp <c>async</c> on member modifier lists. PHP's <see cref="PhpModifier"/>
        /// has no Async value, so the base visitor maps the token to <see cref="PhpModifier.None"/>.
        /// Attach an <c>isAsync</c> grammar addon so the binder can set <c>IsAsync</c> / MemberModifier.Async.
        /// </summary>
        public override PhpModifierListAst VisitNonEmptyMemberModifiers([NotNull] TyhpParser.NonEmptyMemberModifiersContext context)
        {
            var result = base.VisitNonEmptyMemberModifiers(context);
            foreach (var item in context._Items)
            {
                var token = this.VisitMemberModifier(item);
                if (string.Equals(token.ValueString, "async", StringComparison.OrdinalIgnoreCase)
                    || token.ValueInt64 == TyhpParser.T_TYHP_ASYNC)
                {
                    result.AddGrammarAddon("isAsync", token);
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Visits the parameter type expression grammar addon.
        /// In Tyhp mode, parameter types are required (typeExprWithoutStatic).
        /// In PHP mode, parameter types are optional (optionalTypeWithoutStatic).
        ///
        /// Grammar (TyhpParser):
        ///   parameterTypeExpressionGrammarAddon
        ///     : {this.isLanguageMode("tyhp")}? typeExprWithoutStatic
        ///     | {!this.isLanguageMode("tyhp")}? optionalTypeWithoutStatic
        ///     ;
        ///
        /// The base PhpParserAstVisitor already handles both alternatives by
        /// checking which sub-rule is present, so we delegate to it.
        /// </summary>
        public override ITypeExpression? VisitParameterTypeExpressionGrammarAddon([NotNull] TyhpParser.ParameterTypeExpressionGrammarAddonContext context)
            => base.VisitParameterTypeExpressionGrammarAddon(context);

        //#endregion Modifier and Parameter Grammar Addons
    }
}
