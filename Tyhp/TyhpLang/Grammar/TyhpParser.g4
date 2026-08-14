/**
 * Tyhp parser, extends PhpParser.
 *
 * Shared PHP syntax comes from the imported PhpParser (php-src PHP-8.5.x lineage;
 * see PhpParser.g4 / PhpLexer.g4 headers). Tyhp-only rules and *GrammarAddon
 * overrides live in this file.
 */

parser grammar TyhpParser;

options {
    tokenVocab=TyhpLexer;
}

@header {
#pragma warning disable CS3021
}

import PhpParser;

//#region Tyhp Root

tyhpdefSrcFile
    : TyhpdefBlock=tyhpdefBlock EOF                                             #tyhpdefFile
    ;

tyhpSrcFile
    : (startingInlineOutput+=tyhpInlineOutput)*
        (firstCodeBlock=tyhpCodeBlock
        (T_CLOSE_TAG codeBlocks+=tyhpCodeBlock)* T_CLOSE_TAG?)?
        (endingInlineOutput+=tyhpInlineOutput)* EOF                             #tyhpFile
    ;

// Tagless entry points (Story 06, Phase 7). Used instead of tyhpSrcFile / tyhpdefSrcFile
// when `source.tagless` is enabled. The open tag is optional and there is no inline
// output / closing tag, so a `?>` cannot appear (it is not in this rule's follow set).
// The leading action sets the parser language mode so that semantic predicates behave
// identically whether or not a literal open tag is present.
tyhpTaglessSrcFile
locals [_languageMode:string = ""]
    : {
        this._languageMode = "tyhp";
        _localctx._languageMode = this._languageMode;
      }
        T_TYHP_OPEN_TAG?
        StatementList=topStatementListWithRequiredFinalTerminal?
        EOF                                                                    #tyhpTaglessFile
    ;

tyhpdefTaglessSrcFile
locals [_languageMode:string = ""]
    : {
        this._languageMode = "tyhp";
        _localctx._languageMode = this._languageMode;
      }
        T_TYHPDEF_OPEN_TAG?
        StatementList=tyhpdefTopStatementList
        EOF                                                                    #tyhpdefTaglessFile
    ;

tyhpCodeBlock
    : TyhpBlock=tyhpBlock                                                       #tyhpCodeBlockTyhpBlock
    ;

tyhpdefBlock
locals [_languageMode:string = ""]
    : T_TYHPDEF_OPEN_TAG
        {
            this._languageMode = "tyhp";
            _localctx._languageMode = this._languageMode;
        }
        StatementList=tyhpdefTopStatementList
    ;

tyhpBlock
locals [_languageMode:string = ""]
    : T_TYHP_OPEN_TAG
        {
            this._languageMode = "tyhp";
            _localctx._languageMode = this._languageMode;
        }
        StatementList=topStatementListWithRequiredFinalTerminal?
    ;

tyhpInlineOutput
    // all items here are the same as echoing out the content
    : InlineHtml=T_INLINE_HTML
    | PhpEchoBlock=phpEchoBlock {this.isLanguageMode("php")}?
    | PhpBlock=phpBlock (T_CLOSE_TAG | T_SYM_SEMICOLON)+
    ;

tyhpInlineOutputStatement
    : T_CLOSE_TAG InlineOutput+=tyhpInlineOutput+ T_TYHP_OPEN_TAG
    | T_INLINE_HTML
    ;

//#endregion Tyhp Root

//#region Tyhpdef Top Statements

tyhpdefTopStatementList
    : Items+=tyhpdefTopStatement*
    ;

tyhpdefTopStatement
    : Statement=tyhpdefStatement                                                #tyhpdefNotAttributedTopStatement
    | Attributes=attributes? Statement=tyhpdefAttributedStatement               #tyhpdefAttributedTopStatement
    | T_NAMESPACE NamespaceName=namespaceDeclarationName T_SYM_SEMICOLON        #tyhpdefNameSpaceDecl
    | T_NAMESPACE NamespaceName=namespaceDeclarationName?
        T_OPEN_CURLY_BRACE StatementList=tyhpdefTopStatementList
        T_CLOSE_CURLY_BRACE                                                     #tyhpdefNamespaceGroupDecl
    | T_USE UseDecl=mixedGroupUseDeclaration T_SYM_SEMICOLON                    #tyhpdefImportGroupDecls
    | T_USE UseType=useType UseDecl=groupUseDeclaration T_SYM_SEMICOLON         #tyhpdefImportTypedGroupDecls
    | T_USE UseDecl=useDeclarations T_SYM_SEMICOLON                             #tyhpdefImportDecls
    | T_USE UseType=useType UseDecl=useDeclarations T_SYM_SEMICOLON             #tyhpdefImportType
    | T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations                                            #tyhpdefImportExtension
    | Statement=tyhpTypeAlias                                                   #tyhpdefTypeAliasDecl
    ;

//#endregion Tyhpdef Top Statements

//#region Tyhpdef Statements

tyhpdefDeprecatedOrObsolete
    : TokenValue=(T_TYHPDEF_DEPRECATED|T_TYHPDEF_OBSOLETE)
    ;

tyhpdefStatement
    : Statement=tyhpStructDeclarationStatement                                  #tyhpdefStructDecl
    | T_SYM_SEMICOLON                                                           #tyhpdefEmptyStatement
    | T_DECLARE T_OPEN_ROUND_BRACE DeclareList=constList T_CLOSE_ROUND_BRACE
        Statement=declareStatement T_SYM_SEMICOLON                              #tyhpdefDeclare
    | Statement=tyhpdefImportConstStatement                                     #tyhpdefImportConst
    | Statement=tyhpdefImportVariableStatement                                  #tyhpdefImportVariable
    ;

tyhpdefImportConstStatement
    : tyhpdefDeprecatedOrObsolete? T_CONST TypeExpr=typeExprWithoutStatic
        (AliasedIdentifier=tyhpdefIdentifierWithOptionalAlias | Identifier=name)
        (T_COALESCE CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
    ;

tyhpdefImportVariableStatement
    : tyhpdefDeprecatedOrObsolete? TypeExpr=typeExprWithoutStatic
        Variable=T_VARIABLE (T_AS AliasedAs=T_VARIABLE)? (T_COALESCE
        CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
    ;

//#endregion Tyhpdef Statements

//#region Tyhpdef Attributed Statements

tyhpdefAttributedStatement
    : Statement=tyhpdefImportFunctionDeclarationStatement                       #tyhpdefImportFunctionDecl
    | Statement=tyhpdefImportClassDeclarationStatement                          #tyhpdefImportClassDecl
    | Statement=tyhpdefImportTraitDeclarationStatement                          #tyhpdefImportTraitDecl
    | Statement=tyhpdefImportInterfaceDeclarationStatement                      #tyhpdefImportInterfaceDecl
    | Statement=tyhpdefImportEnumDeclarationStatement                           #tyhpdefImportEnumDecl
    ;

tyhpdefImportFunctionDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC? function
        ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        FindDocComment=T_OPEN_ROUND_BRACE IsExtension=T_EXTENDS?
        ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        T_SYM_SEMICOLON
    ;

tyhpdefImportClassDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? Modifiers=classModifiers? T_CLASS
        Identifier=tyhpdefClassNameWithOptionalAlias Extends=extendsFrom
        Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    ;

tyhpdefImportTraitDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? T_TRAIT
        Identifier=tyhpdefIdentifierWithOptionalAlias
         FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    ;

tyhpdefImportInterfaceDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? T_INTERFACE
        Identifier=tyhpdefIdentifierWithOptionalAlias
        Extends=interfaceExtendsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    ;

tyhpdefImportEnumDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? T_ENUM
        Identifier=tyhpdefIdentifierWithOptionalAlias 
        BackingType=enumBackingType Implements=implementsList
        FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    ;

//#endregion Tyhpdef Attributed Statements

//#region Tyhpdef Object Members

tyhpdefClassStatementList
    : Items+=tyhpdefClassStatement*
    ;

tyhpdefClassStatement
    : tyhpdefDeprecatedOrObsolete? Modifiers=propertyModifiers
        TypeExpr=typeExprWithoutStatic PropertyList=tyhpdefPropertyList
        T_SYM_SEMICOLON                                                         #tyhpdefClassProperty
    | tyhpdefDeprecatedOrObsolete? Modifiers=methodModifiers T_CONST
        TypeExpr=typeExprWithoutStatic ConstList=tyhpdefImportClassConstList
        T_SYM_SEMICOLON                                                         #tyhpdefImportClassConst
    | tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC? Modifiers=methodModifiers function
        ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType T_SYM_SEMICOLON               #tyhpdefImportClassMethod
    | tyhpdefDeprecatedOrObsolete? EnumCase=enumCase                            #tyhpdefEnumCase
    | tyhpdefDeprecatedOrObsolete? T_USE TraitNameList=classNameList
        Adaptations=traitAdaptations                                            #tyhpdefTraitUse
    | tyhpdefDeprecatedOrObsolete? T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations                                            #tyhpdefClassUseExtension
    | tyhpdefDeprecatedOrObsolete? tyhpdefExtensionFunction                     #tyhpdefExtensionFunctionDecl
    | tyhpdefDeprecatedOrObsolete? tyhpdefExtensionOperator                     #tyhpdefExtensionOperatorDecl
    | tyhpdefClassOperator T_SYM_SEMICOLON                                      #tyhpdefClassOperatorDecl
    ;

tyhpdefExtensionFunction
    : T_TYHP_EXTENSION function ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType
        StatementList=methodBody                                                #tyhpdefExtensionFunctionFullDecl
    | T_TYHP_EXTENSION fn ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType T_DOUBLE_ARROW
        Expr=expr T_SYM_SEMICOLON                                               #tyhpdefExtensionFunctionShortDecl
    ;

tyhpdefExtensionOperator
    : T_TYHP_EXTENSION T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType T_SYM_SEMICOLON                              #tyhpdefExtensionOperatorSignatureDecl
    | T_TYHP_EXTENSION T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody | (T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON)) #tyhpdefExtensionOperatorFullDecl
    ;

tyhpdefClassOperator
    : T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
    ;

tyhpdefClassConstDecl
locals [ _findDocComment:IToken = null ]
@after { _localctx._findDocComment = _localctx.Stop; }
    : Identifier=identifier (T_COALESCE CoalesceExpr=expr)?
    ;

tyhpdefClassConstList
    : Items+=tyhpdefClassConstDecl (T_SYM_COMMA Items+=tyhpdefClassConstDecl)*
    ;

tyhpdefImportClassConstDecl
    : Identifier=tyhpdefIdentifierWithOptionalAlias (T_COALESCE CoalesceExpr=expr)?
    ;

tyhpdefImportClassConstList
    : Items+=tyhpdefImportClassConstDecl
        (T_SYM_COMMA Items+=tyhpdefImportClassConstDecl)*
    ;

tyhpdefProperty
    : Variable=T_VARIABLE
    ;

tyhpdefPropertyList
    : Items+=tyhpdefProperty (T_SYM_COMMA Items+=tyhpdefProperty)*
    ;

//#endregion Tyhpdef Object Members

//#region Tyhpdef Identifiers

tyhpdefIdentifierWithOptionalAlias
    : Identifier=tyhpOptionalGenericIdentifier
    | AliasedIdentifier=tyhpdefIdentifierWithAlias
    ;

tyhpdefClassNameWithOptionalAlias
    : (AliasOf=className T_AS)? 
        Identifier=T_STRING GenericParameters=tyhpGenericParameterDeclarations?
    ;

tyhpdefFunctionNameWithOptionalAlias
    : Identifier=identifier GenericArguments=tyhpGenericParameterDeclarations
        (T_AS AliasedAs=tyhpStringWithOptionalGeneric)?                         #tyhpdefFunctionNameGenericAlias
    | Identifier=identifier (T_AS AliasedAs=T_STRING)?                                #tyhpdefFunctionNameAlias
    ;

tyhpdefIdentifierWithAlias
    : Identifier=name T_AS AliasedAs=tyhpOptionalGenericIdentifier              #tyhpdefIdentifierAlias
    | ClassName=className T_DOUBLE_COLON
        Identifier=tyhpOptionalGenericIdentifier
        T_AS AliasedAs=tyhpOptionalGenericIdentifier                            #tyhpdefClassMemberIdentifierAlias
    ;

//#endregion Tyhpdef Identifiers

//#region Tyhp Structs

tyhpStructDeclarationStatement
    : T_TYHP_STRUCT Identifier=T_STRING
        GenericParameters=tyhpGenericParameterDeclarations?
        (T_EXTENDS Extends=className)?
        FindDocComment=T_OPEN_CURLY_BRACE PropertyList=tyhpStructPropertyList
        T_CLOSE_CURLY_BRACE
    ;

tyhpAnonymousStruct
    : T_TYHP_STRUCT (T_EXTENDS Extends=className)? (T_OPEN_ROUND_BRACE
        T_CLOSE_ROUND_BRACE)? FindDocComment=T_OPEN_CURLY_BRACE
        PropertyList=tyhpStructPropertyList T_CLOSE_CURLY_BRACE
    ;

tyhpStructProperty
    // Alias key may be a quoted string (`'Reply-To' as $replyTo`) or a decimal
    // integer (`0 as $arg1`) for PHP array keys that are not valid identifiers.
    : TypeExpr=typeExprWithoutStatic
        (
            (AliasOfString=T_CONSTANT_ENCAPSED_STRING | AliasOfInt=T_LNUMBER)
            T_AS
        )?
        Property=property T_SYM_SEMICOLON
    ;

tyhpStructPropertyList
    : Items+=tyhpStructProperty*
    ;

//#endregion Tyhp Structs

//#region Tyhp Extensions

tyhpExtensionDeclarationStatement
    : T_TYHP_EXTENSION Identifier=T_STRING Extends=extendsFrom
        FindDocComment=T_OPEN_CURLY_BRACE FunctionList=tyhpExtensionFunctionList
        T_CLOSE_CURLY_BRACE
    ;

tyhpExtensionFunctionList
    : Items+=tyhpExtensionMember*
    ;

tyhpExtensionMember
    : functionDeclarationStatement
    | tyhpExtensionOperatorOverload
    ;

tyhpExtensionOperatorOverload
    : T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_SYM_LT TargetType=typeExprWithoutStatic T_SYM_GT
        T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr))
    ;

//#endregion Tyhp Extensions

//#region Tyhp Type Aliases

tyhpTypeAlias
    : T_TYHP_TYPE_ALIAS Identifier=name
        GenericArguments=tyhpGenericParameterDeclarations? T_SYM_EQUAL
        TypeExpr=typeExpr T_SYM_SEMICOLON
    ;

//#endregion Tyhp Type Aliases

//#region Tyhp Generics

tyhpGenericIdentifier
    : (Identifier=T_STRING | IdentifierSemiReserved=semiReserved)
        GenericArguments=tyhpGenericTypeArguments
    ;

tyhpGenericIdentifierWithoutConstructor
    : (
        Identifier=T_STRING
        | IdentifierSemiReserved=semiReservedWithoutConstructor
    ) GenericArguments=tyhpGenericParameterDeclarations
    ;

tyhpGenericParameterDeclaration
    : Identifier=name (T_EXTENDS TypeExpr=typeExpr)? (T_SYM_EQUAL DefaultExpr=typeExpr)?
    ;

tyhpGenericParameterDeclarationList
    : Items+=tyhpGenericParameterDeclaration (T_SYM_COMMA
        Items+=tyhpGenericParameterDeclaration)*
    ;

tyhpGenericParameterDeclarations
    : T_SYM_LT GenericParametersList=tyhpGenericParameterDeclarationList T_SYM_GT
    ;

tyhpGenericTypeArgument
    : TypeExpr=typeExpr
    ;

tyhpGenericTypeArgumentList
    : Items+=tyhpGenericTypeArgument (T_SYM_COMMA Items+=tyhpGenericTypeArgument)*
    ;

tyhpGenericTypeArguments
    : T_SYM_LT GenericArgumentsList=tyhpGenericTypeArgumentList T_SYM_GT
    ;

tyhpOptionalGenericIdentifier
    : Identifier=identifier
    | GenericIdentifier=tyhpGenericIdentifier {this.isLanguageMode("tyhp")}?
    ;

tyhpOptionalGenericIdentifierWithoutConstructor
    : Identifier=identifierWithoutConstructor
    | GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        {this.isLanguageMode("tyhp")}?
    ;

tyhpStringWithOptionalGeneric
    : Identifier=T_STRING GenericArguments=tyhpGenericTypeArguments?
    ;

//#endregion Tyhp Generics

//#region Tyhp Expressions

// ! OVERRIDE
phpExprUnaryPreOpsGrammarAddon
    : TokenValue=T_DECIMAL_CAST
    | TokenValue=T_TYHP_AWAIT {this.isLanguageMode("tyhp")}?
    ;

tyhpWithList
    : T_OPEN_CURLY_BRACE ArrayPairList=arrayPairList T_CLOSE_CURLY_BRACE
    ;

// ! OVERRIDE
phpExprBinaryOpGrammarAddon001
    : TokenValue=T_TYHP_WITH {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
phpExprBinaryOpGrammarAddon002
    // alias of T_INSTANCEOF
    : TokenValue=T_TYHP_IS {this.isLanguageMode("tyhp")}?
    ;

phpExprAssignmentOpsGrammarAddon
    // using assignment operator
    : TokenValue=T_TYHP_USING_EQUAL {this.isLanguageMode("tyhp")}?
    ;

//#endregion Tyhp Expressions

//#region Tyhp Identifiers

tyhpReservedNonModifiers
    : TokenValue=T_TYHP_STRUCT
    | TokenValue=T_TYHP_TYPE_ALIAS
    | TokenValue=T_TYHP_AWAIT
    | TokenValue=T_TYHP_WITH
    | TokenValue=T_TYHP_OPERATOR
    | TokenValue=T_TYHP_VOID
    | TokenValue=T_TYHP_PARENT
    | TokenValue=T_TYHP_EXTENSION
    | TokenValue=T_TYHP_TYPEOF
    | TokenValue=T_TYHP_NAMEOF
    | TokenValue=T_TYHP_VARIABLE_EXISTS
    | TokenValue=T_TYHP_USING
    ;

// ! OVERRIDE
reservedNonModifiersGrammarAddon
    : tyhpReservedNonModifiers {this.isLanguageMode("tyhp")}?
    ;

tyhpSemiReserved
    : TokenValue=T_TYHP_ASYNC
    | TokenValue=T_TYHP_OPERATOR
    | TokenValue=T_TYHPDEF_DEPRECATED
    | TokenValue=T_TYHPDEF_OBSOLETE
    | TokenValue=T_TYHP_IS
    ;

// ! OVERRIDE
semiReservedGrammarAddon
    : tyhpSemiReserved {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
namespaceNameGrammarAddon
    : (Name=T_STRING | QualifiedName=T_NAME_QUALIFIED)
        GenericArguments=tyhpGenericTypeArguments
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
legacyNamespaceNameGrammarAddon
    : FullyQualifiedName=T_NAME_FULLY_QUALIFIED
        GenericArguments=tyhpGenericTypeArguments
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
nameTokenValueGrammarAddon
    : T_TYHP_VOID {this.isLanguageMode("tyhp")}?
    | T_TYHP_PARENT {this.isLanguageMode("tyhp")}?
    | T_TYHP_USING {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
typeNameGrammarAddon
    : GenericArguments=tyhpGenericTypeArguments?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
classNameIdentifierGrammarAddon
    : GenericArguments=tyhpGenericTypeArguments?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
memberNameIdentifierGrammarAddon
    : GenericArguments=tyhpGenericTypeArguments?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
optionalTypeWithoutStatic
    : TypeExpr=typeExprWithoutStatic?
    ;

//#endregion Tyhp Identifiers

//#region Tyhp Dereferencables

// ! OVERRIDE
newDereferenceableGrammarAddon
    : T_NEW AnonStructDecl=tyhpAnonymousStruct {this.isLanguageMode("tyhp")}?   #tyhpNewAnonStructInstance
    ;

// ! OVERRIDE
// `new X<T>(args)` is ambiguous with the comparison chain `(new X) < T > (args)`,
// because classNameReference consumes the generic argument list here just as it does
// in newDereferenceable. The lookahead rules this alternative out whenever an argument
// list follows, so `new X<T>(args)` can only parse as a generic instantiation.
newNonDereferenceable
    : {!this.newIsFollowedByArgumentList()}?
        T_NEW Identifier=classNameReference                                     #newClassInstanceNonDereferenceable
    | newNonDereferenceableGrammarAddon                                         #newNonDereferenceableGrammarAddonHandler
    ;

//#endregion Tyhp Dereferencables

//#region Tyhp Top Statements

// ! OVERRIDE
topStatementGrammarAddon
    : T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations {this.isLanguageMode("tyhp")}?             #tyhpImportExtension
    | Statement=tyhpTypeAlias {this.isLanguageMode("tyhp")}?                    #tyhpTypeAliasDecl
    | Statement=tyhpStructDeclarationStatement {this.isLanguageMode("tyhp")}?   #tyhpStructDecl
    | Statement=tyhpExtensionDeclarationStatement
        {this.isLanguageMode("tyhp")}?                                          #tyhpExtensionDecl
    ;

// ! OVERRIDE
unprefixedUseDeclarationGrammarAddon
    : NamespaceName=namespaceName
        (T_AS AliasedAs=T_STRING GenericArguments=tyhpGenericTypeArguments)
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
useDeclarationGrammarAddon
    : NamespaceName=legacyNamespaceName
        (T_AS AliasedAs=T_STRING GenericArguments=tyhpGenericTypeArguments)
        {this.isLanguageMode("tyhp")}?
    ;

//#endregion Tyhp Top Statements

//#region Tyhp Statements

// ! OVERRIDE
// `Type<Arg> $var = ...` is ambiguous with the comparison chain `(Type < Arg) > $var = ...`.
// Statement prediction prefers phpTopExpr over the typed-local addon, so gate top-expr when
// lookahead matches a generic typed local (see looksLikeGenericTypedLocal).
phpTopExpr
locals [ isTopExpr:bool = true ]
    : {!this.isLanguageMode("tyhp") || !this.looksLikeGenericTypedLocal()}?
        phpExprPrec
    ;

// ! OVERRIDE
statementRequiringTerminalGrammarAddon
    : Statement=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?                 #tyhpStatementTypedVarExpr
    ;

tyhpTypedVarExpr
    : <assoc=right> TypeExpr=optionalTypeWithoutStatic Variable=simpleVariable
        (FindDocCommentCheck=T_SYM_EQUAL IsRef=ampersand? EqualsExpr=expr)?
    | <assoc=right> T_OPEN_ROUND_BRACE TypeExpr=optionalTypeWithoutStatic
        T_CLOSE_ROUND_BRACE Variable=simpleVariable
        (FindDocCommentCheck=T_SYM_EQUAL IsRef=ampersand? EqualsExpr=expr)?
    ;

// ! OVERRIDE
// Route the for-loop init clause through a Tyhp-aware list so typed-local declarations
// (e.g. `for (int $i = 0; ...)`) are accepted. Test/update keep the base expression-only
// form. PHP mode falls through to plain expressions because the typed-var alternative is
// gated by the language-mode predicate. The same generic-typed-local / comparison ambiguity
// as phpTopExpr is gated here so `for (Box<int> $i = ...; ...)` is not parsed as a comparison.
forSyntax
    : T_FOR T_OPEN_ROUND_BRACE InitExpr=tyhpForInitExprs? T_SYM_SEMICOLON
        TestExpr=forCondExprs T_SYM_SEMICOLON UpdateExpr=forExprs
        T_CLOSE_ROUND_BRACE
    ;

tyhpForInitExprs
    : Items+=tyhpForInitExpr (T_SYM_COMMA Items+=tyhpForInitExpr)*
    ;

tyhpForInitExpr
    : {!this.isLanguageMode("tyhp") || !this.looksLikeGenericTypedLocal()}?
        Op=T_VOID_CAST Expr=expr                                                #tyhpForInitVoidCast
    | {!this.isLanguageMode("tyhp") || !this.looksLikeGenericTypedLocal()}?
        Expr=expr                                                               #tyhpForInitPlainExpr
    | TypedVar=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?                  #tyhpForInitTypedVar
    ;

//#endregion Tyhp Statements

//#region Tyhp Using Block

// ! OVERRIDE
statementWithoutTerminalGrammarAddon
    : Statement=tyhpUsingBlock {this.isLanguageMode("tyhp")}?                 #tyhpStatementUsingBlock
    ;

tyhpUsingBlock
    : T_TYHP_USING IsAsync=T_TYHP_AWAIT?
      T_OPEN_ROUND_BRACE Resources=tyhpUsingResourceList T_CLOSE_ROUND_BRACE
      T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    ;

tyhpUsingResourceList
    : Items+=tyhpUsingResource (T_SYM_COMMA Items+=tyhpUsingResource)*
    ;

tyhpUsingResource
    : TypeExpr=typeExprWithoutStatic Variable=simpleVariable T_SYM_EQUAL Expr=expr  #tyhpUsingResourceTyped
    | Variable=simpleVariable T_SYM_EQUAL Expr=expr                                 #tyhpUsingResourceInferred
    | Expr=expr                                                                     #tyhpUsingResourceUnassigned
    ;

//#endregion Tyhp Using Block

//#region Tyhp Functions

// ! OVERRIDE
functionDeclarationStatementGrammarAddon
    : functionModifiersGrammarAddon function
        ReturnsRef=returnsRef Identifier=functionName functionNameGrammarAddon
        FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        IsOverloadSignature=T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpFunctionOverloadDeclarationStatement
    | functionModifiersGrammarAddon fn
        ReturnsRef=returnsRef Identifier=functionName functionNameGrammarAddon
        FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ParameterList=parameterList T_CLOSE_ROUND_BRACE
        OptionalReturnType=returnType T_DOUBLE_ARROW Expr=expr
        T_SYM_SEMICOLON
        {this.isLanguageMode("tyhp")}?                                          #tyhpShortFunctionOverloadDeclarationStatement
    ;

// ! OVERRIDE
functionModifiersGrammarAddon
    : IsAsync=T_TYHP_ASYNC? {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
functionNameGrammarAddon
    : GenericParameters=tyhpGenericParameterDeclarations?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
functionCallGrammarAddon
    : GenericArguments=tyhpGenericTypeArguments?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
functionParametersGrammarAddon
    : IsExtension=T_EXTENDS? {this.isLanguageMode("tyhp")}?
    ;

//#endregion Tyhp Functions

//#region Tyhp Objects

// ! OVERRIDE
classNameGrammarAddon
    : GenericArguments=tyhpGenericParameterDeclarations?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
traitNameGrammarAddon
    : GenericArguments=tyhpGenericParameterDeclarations?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
interfaceNameGrammarAddon
    : GenericArguments=tyhpGenericParameterDeclarations?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
enumNameGrammarAddon
    : GenericArguments=tyhpGenericParameterDeclarations?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
tyhpCtorReturnType
    : T_SYM_COLON TokenValue=T_TYHP_VOID
    | T_SYM_COLON TokenValue=T_TYHP_PARENT ArgumentsList=argumentList
    ;

// ! OVERRIDE
attributedClassStatementGrammarAddon
    : Modifiers=methodModifiers tyhpClassMethodDefinition
        {this.isLanguageMode("tyhp")}?
    ;

tyhpClassMethodDefinition
    // @ visitor will need to throw a parse error if it has ReturnsRef and is the constructor method
    : function ReturnsRef=returnsRef tyhpMethodDefinition                       #tyhpClassMethod
    | fn ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE OptionalReturnType=returnType T_DOUBLE_ARROW
        Expr=expr T_SYM_SEMICOLON {this.isLanguageMode("tyhp")}?                #tyhpClassGenericMethodShort
    ;

tyhpMethodDefinition
    // @ we will throw an error during the visit if this has a return type or generics and is not tyhp
    : Identifier=T_CONSTRUCT_METHOD
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=ctorParameterList
        T_CLOSE_ROUND_BRACE ReturnType=tyhpCtorReturnType
        StatementList=methodBody                                                #tyhpClassCtorWithReturnType
    | GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType StatementList=methodBody      #tyhpClassGenericMethod
    ;

// ! OVERRIDE
classStatementGrammarAddon
    : Modifier=nonEmptyMemberModifiers? TypeAlias=tyhpTypeAlias
        {this.isLanguageMode("tyhp")}?                                          #tyhpClassTypeAlias
    | OperatorOverload=tyhpClassOperatorOverload {this.isLanguageMode("tyhp")}? #tyhpClassOperatorOverloadDecl
    ;

tyhpClassOperatorOverload
    : Modifier=(T_ABSTRACT | T_FINAL)? T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=parameter
        (T_SYM_COMMA RightParameter=parameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr))
    ;

tyhpClassOperatorOverloadOp
    : TokenValue=T_SYM_PLUS
    | TokenValue=T_SYM_MINUS
    | TokenValue=T_SYM_SLASH
    | TokenValue=T_SYM_ASTERISK
    | TokenValue=T_SYM_PERCENT
    | TokenValue=T_INC
    | TokenValue=T_DEC
    | TokenValue=T_POW
    | TokenValue=T_SYM_TILDE
    | TokenValue=T_SYM_BANG
    | TokenValue=T_SL
    | TokenValue=T_SYM_GT IsSR=T_SYM_GT   // >> (two GT tokens; must precede bare >)
    | TokenValue=T_SYM_GT                // bare >
    | TokenValue=T_SYM_PERIOD
    | TokenValue=T_SYM_LT
    | TokenValue=T_IS_SMALLER_OR_EQUAL
    | TokenValue=T_IS_GREATER_OR_EQUAL
    | TokenValue=T_IS_EQUAL
    | TokenValue=T_IS_NOT_EQUAL
    | TokenValue=T_IS_IDENTICAL
    | TokenValue=T_IS_NOT_IDENTICAL
    | TokenValue=T_SPACESHIP
    | TokenValue=T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG
    | TokenValue=T_SYM_CARET
    | TokenValue=T_SYM_PIPE
    | TokenValue=T_EMPTY // the `empty` word operator (lexed as the T_EMPTY keyword)
    | TokenValue=T_STRING // for `convert` (and other word operators lexed as identifiers)

    ;

// ! OVERRIDE
traitAliasGrammarAddon
    : AliasOf=traitPropertyReference T_AS AliasString=T_VARIABLE
        {this.isLanguageMode("tyhp")}?                                          #tyhpTraitAliasPropertyRename
    ;

// ! OVERRIDE
traitAliasNameGrammarAddon
    : GenericArguments=tyhpGenericTypeArguments?
        {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
traitMethodIdentifierGrammarAddon
    : (GenericIdentifier=tyhpGenericIdentifier {this.isLanguageMode("tyhp")}?)?
    ;

// ! OVERRIDE
memberModifierGrammarAddon
    : TokenValue=T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
parameterTypeExpressionGrammarAddon
    : optionalTypeWithoutStatic
    ;

//#endregion Tyhp Objects

//#region Tyhp Types

// ! OVERRIDE
typeWithoutStaticGrammarAddon
    : ScalarType=tyhpScalarType {this.isLanguageMode("tyhp")}?
    ;

tyhpScalarType
    : Scalar=T_LNUMBER                                                          #scalarTypeLNumber
    | Scalar=T_DNUMBER                                                          #scalarTypeDNumber
    | Scalar=T_ONUMBER                                                          #scalarTypeONumber
    | Scalar=T_HNUMBER                                                          #scalarTypeHNumber
    | Scalar=T_BNUMBER                                                          #scalarTypeBNumber
    | Scalar=T_CONSTANT_ENCAPSED_STRING                                         #scalarTypeSingleQuoteString
    | T_DOUBLE_QUOTE EncapsList=encapsList? T_DOUBLE_QUOTE                      #scalarTypeDoubleQuoteString
    ;

//#endregion Tyhp Types

//#region Tyhp Return Types

// ! OVERRIDE
returnTypeGrammarAddon
    : T_SYM_COLON GuardVariable=T_VARIABLE (T_INSTANCEOF|T_TYHP_IS)
        TypeExpr=typeExpr {this.isLanguageMode("tyhp")}?                        #tyhpReturnTypeGuard
    ;

//#endregion Tyhp Return Types


//#region Tyhp Internal Functions

// ! OVERRIDE
internalFunctionsGrammarAddon
    : T_TYHP_VARIABLE_EXISTS T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionVariableExists
    | T_TYHP_TYPEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionTypeof
    | T_DEFAULT T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionDefault
    | T_DEFAULT BuiltinCast=(T_DOUBLE_CAST|T_OBJECT_CAST|T_INT_CAST|
        T_STRING_CAST|T_BOOL_CAST|T_ARRAY_CAST|T_DECIMAL_CAST)
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionDefaultBuiltinCast
    | T_TYHP_NAMEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionNameof
    ;

//#endregion Tyhp Internal Functions