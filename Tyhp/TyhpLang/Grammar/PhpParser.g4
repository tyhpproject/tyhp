/**
 * Php parser, based off of PHP grammar located at:
 * https://github.com/php/php-src/blob/PHP-8.5.9/Zend/zend_language_parser.y
 *
 * Lineage: php-src PHP-8.5.x (highest supported PHP minor for Tyhp).
 * This has been heavily modified from the original PHP grammar to convert the
 * original LALR grammar to LL grammar.
 *
 * https://php.watch/versions
 */

parser grammar PhpParser;

options {
    tokenVocab=PhpLexer;
}

@header {
#pragma warning disable CS3021
}

//#region Root

noGrammarAddon
    : T_NO_GRAMMAR_ADDON_0000
    ;

phpSrcFile
    : (startingInlineOutput+=phpInlineOutput)* (firstCodeBlock=codeBlock (T_CLOSE_TAG codeBlocks+=codeBlock)* T_CLOSE_TAG?)? (endingInlineOutput+=phpInlineOutput)* EOF
    ;

codeBlock
    : PhpBlock=phpBlock                                                         #codeBlockPhpBlock
    | codeBlockGrammarAddon {this._languageMode = "";}                          #codeBlockGrammarAddonHandler
    | TokenValue=T_ERROR                                                        #codeBlockError
    ;

codeBlockGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

phpBlock
locals [_languageMode:string = ""]
    : T_OPEN_TAG
        {
            this._languageMode = "php";
            _localctx._languageMode = this._languageMode;
        }
        // StatementList=topStatementListWithOptionalFinalTerminal?
        StatementList=topStatementListWithRequiredFinalTerminal?
    ;

phpEchoBlock
locals [_languageMode:string = ""]
    : T_OPEN_TAG_WITH_ECHO
        {
            this._languageMode = "php";
            _localctx._languageMode = this._languageMode;
        }
        Expr=echoExprList (T_CLOSE_TAG | T_SYM_SEMICOLON)+
    ;

phpInlineOutput
    // all items here are the same as echoing out the content
    : InlineHtml=T_INLINE_HTML
    | PhpEchoBlock=phpEchoBlock {this.isLanguageMode("php")}?
    ;

phpInlineOutputStatement
    : T_CLOSE_TAG InlineOutput+=phpInlineOutput+ T_OPEN_TAG
    | T_INLINE_HTML
    | phpInlineOutputStatementGrammarAddon
    ;

phpInlineOutputStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

possibleComma
    : T_SYM_COMMA?
    ;

//#endregion Root

//#region Expressions

expr
locals [ isTopExpr:bool = false ]
    : phpExprPrec
    ;

phpTopExpr
locals [ isTopExpr:bool = true ]
    : phpExprPrec
    ;

phpExprUnaryPreOps
    : TokenValue=T_SYM_PLUS
    | TokenValue=T_SYM_MINUS
    | TokenValue=T_INC
    | TokenValue=T_DEC
    | TokenValue=T_SYM_TILDE
    | TokenValue=T_SYM_AT
    | TokenValue=T_INT_CAST
    | TokenValue=T_DOUBLE_CAST
    | TokenValue=T_STRING_CAST
    | TokenValue=T_ARRAY_CAST
    | TokenValue=T_OBJECT_CAST
    | TokenValue=T_BOOL_CAST
    | phpExprUnaryPreOpsGrammarAddon
    ;

phpExprUnaryPreOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOps
    : TokenValue=T_INC
    | TokenValue=T_DEC
    | phpExprUnaryPostOpsGrammarAddon
    ;

phpExprUnaryPostOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryMulDivOps
    : TokenValue=T_SYM_ASTERISK
    | TokenValue=T_SYM_SLASH
    | TokenValue=T_SYM_PERCENT
    | phpExprBinaryMulDivOpsGrammarAddon
    ;

phpExprBinaryMulDivOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryAddSubOps
    : TokenValue=T_SYM_PLUS
    | TokenValue=T_SYM_MINUS
    | phpExprBinaryAddSubOpsGrammarAddon
    ;

phpExprBinaryAddSubOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryShiftOps
    : TokenValue=T_SL
    | TokenValue=T_SYM_GT IsSR=T_SYM_GT
    | phpExprBinaryShiftOpsGrammarAddon
    ;

phpExprBinaryShiftOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryConcatOps
    : TokenValue=T_SYM_PERIOD
    | phpExprBinaryConcatOpsGrammarAddon
    ;

phpExprBinaryConcatOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprCompareSizeOps
    : TokenValue=T_SYM_LT
    | TokenValue=T_IS_SMALLER_OR_EQUAL
    | TokenValue=T_SYM_GT
    | TokenValue=T_IS_GREATER_OR_EQUAL
    | phpExprCompareSizeOpsGrammarAddon
    ;

phpExprCompareSizeOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprCompareEqualityOps
    : TokenValue=T_IS_EQUAL
    | TokenValue=T_IS_NOT_EQUAL
    | TokenValue=T_IS_IDENTICAL
    | TokenValue=T_IS_NOT_IDENTICAL
    | TokenValue=T_SPACESHIP
    | phpExprCompareEqualityOpsGrammarAddon
    ;

phpExprCompareEqualityOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprAssignmentOps
    : TokenValue=T_SYM_EQUAL
    | TokenValue=T_PLUS_EQUAL
    | TokenValue=T_MINUS_EQUAL
    | TokenValue=T_MUL_EQUAL
    | TokenValue=T_POW_EQUAL
    | TokenValue=T_DIV_EQUAL
    | TokenValue=T_CONCAT_EQUAL
    | TokenValue=T_MOD_EQUAL
    | TokenValue=T_AND_EQUAL
    | TokenValue=T_OR_EQUAL
    | TokenValue=T_XOR_EQUAL
    | TokenValue=T_SL_EQUAL
    | TokenValue=T_SR_EQUAL
    | TokenValue=T_COALESCE_EQUAL
    | phpExprAssignmentOpsGrammarAddon
    ;

phpExprAssignmentOpsGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPreOpGrammarAddon001
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOpGrammarAddon001
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryOpGrammarAddon001
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPreOpGrammarAddon002
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOpGrammarAddon002
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryOpGrammarAddon002
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPreOpGrammarAddon003
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOpGrammarAddon003
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryOpGrammarAddon003
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPreOpGrammarAddon004
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOpGrammarAddon004
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryOpGrammarAddon004
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPreOpGrammarAddon005
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOpGrammarAddon005
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryOpGrammarAddon005
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPreOpGrammarAddon006
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprUnaryPostOpGrammarAddon006
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprBinaryOpGrammarAddon006
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

phpExprPrec
    // Call-shaped clone first (php-src clone_argument_list). Does not match
    // clone($x) — that stays unary T_CLONE + parenthesized expr (#phpExprClone).
    : Op=T_CLONE ArgumentList=cloneArgumentList                                 #phpExprCloneCall
    | Op=T_CLONE R=phpExprPrec                                                  #phpExprClone
    | Op=phpExprUnaryPreOpGrammarAddon001 R=phpExprPrec                         #phpExprUnaryPreOpGrammarAddon001Handler
    | L=phpExprPrec Op=phpExprUnaryPostOpGrammarAddon001                        #phpExprUnaryPostOpGrammarAddon001Handler
    | L=phpExprPrec Op=phpExprBinaryOpGrammarAddon001 R=phpExprPrec             #phpExprBinaryOpGrammarAddon001Handler
    | <assoc=right> L=phpExprPrec Op=T_POW R=phpExprPrec                        #phpExprPow
    | Op=phpExprUnaryPreOps R=phpExprPrec                                       #phpExprUnaryPreOp
    | L=phpExprPrec Op=phpExprUnaryPostOps                                      #phpExprUnaryPostOp
    | L=phpExprPrec Op=T_INSTANCEOF R=phpExprPrec                               #phpExprInstanceOf
    | Op=phpExprUnaryPreOpGrammarAddon002 R=phpExprPrec                         #phpExprUnaryPreOpGrammarAddon002Handler
    | L=phpExprPrec Op=phpExprUnaryPostOpGrammarAddon002                        #phpExprUnaryPostOpGrammarAddon002Handler
    | L=phpExprPrec Op=phpExprBinaryOpGrammarAddon002 R=phpExprPrec             #phpExprBinaryOpGrammarAddon002Handler
    | Op=T_SYM_BANG R=phpExprPrec                                               #phpExprNot
    | L=phpExprPrec Op=phpExprBinaryMulDivOps R=phpExprPrec                     #phpExprBinaryMulDiv
    | L=phpExprPrec Op=phpExprBinaryAddSubOps R=phpExprPrec                     #phpExprBinaryAddSub
    | L=phpExprPrec Op=phpExprBinaryShiftOps R=phpExprPrec                      #phpExprBinaryShift
    | L=phpExprPrec Op=phpExprBinaryConcatOps R=phpExprPrec                     #phpExprBinaryConcat
    // PHP 8.5 pipe `|>` — left-associative; binds after concat/arithmetic, before comparison
    // (php-src: %left T_PIPE between '.' and comparison ops).
    | L=phpExprPrec Op=T_PIPE R=phpExprPrec                                     #phpExprPipe
    | L=phpExprPrec Op=phpExprCompareSizeOps R=phpExprPrec                      #phpExprCompareSize
    | L=phpExprPrec Op=phpExprCompareEqualityOps R=phpExprPrec                  #phpExprCompareEquality
    | Op=phpExprUnaryPreOpGrammarAddon003 R=phpExprPrec                         #phpExprUnaryPreOpGrammarAddon003Handler
    | L=phpExprPrec Op=phpExprUnaryPostOpGrammarAddon003                        #phpExprUnaryPostOpGrammarAddon003Handler
    | L=phpExprPrec Op=phpExprBinaryOpGrammarAddon003 R=phpExprPrec             #phpExprBinaryOpGrammarAddon003Handler
    | {!this.checkIsTopExpr(_localctx)}? Op=ampersand R=phpExprPrec             #phpExprAmpersand
    | L=phpExprPrec {!this.checkIsTopExpr(_localctx)}? Op=ampersand
        R=phpExprPrec                                                           #phpExprBitwiseAnd
    | L=phpExprPrec {!this.checkIsTopExpr(_localctx)}? Op=T_SYM_CARET
        R=phpExprPrec                                                           #phpExprBinaryXor
    | L=phpExprPrec {!this.checkIsTopExpr(_localctx)}? Op=T_SYM_PIPE
        R=phpExprPrec                                                           #phpExprBinaryOr
    | L=phpExprPrec Op=T_BOOLEAN_AND R=phpExprPrec                              #phpExprBooleanAnd
    | L=phpExprPrec Op=T_BOOLEAN_OR R=phpExprPrec                               #phpExprBooleanOr
    | <assoc=right> L=phpExprPrec Op=T_COALESCE R=phpExprPrec                   #phpExprCoalesce
    | Op=phpExprUnaryPreOpGrammarAddon004 R=phpExprPrec                         #phpExprUnaryPreOpGrammarAddon004Handler
    | L=phpExprPrec Op=phpExprUnaryPostOpGrammarAddon004                        #phpExprUnaryPostOpGrammarAddon004Handler
    | L=phpExprPrec Op=phpExprBinaryOpGrammarAddon004 R=phpExprPrec             #phpExprBinaryOpGrammarAddon004Handler
    | L=phpExprPrec Op1=T_SYM_QUESTION T=phpExprPrec? Op2=T_SYM_COLON
        F=phpExprPrec                                                           #phpExprTernary
    | <assoc=right> L=phpExprPrec Op=phpExprAssignmentOps R=phpExprPrec         #phpExprAssignment
    | Op=T_YIELD_FROM R=phpExprPrec                                             #phpExprYieldFrom
    | Op=T_YIELD (KeyValue=phpExprPrec T_DOUBLE_ARROW)? R=phpExprPrec           #phpExprYieldValue
    | Op=T_PRINT R=phpExprPrec                                                  #phpExprPrint
    | Op=phpExprUnaryPreOpGrammarAddon005 R=phpExprPrec                         #phpExprUnaryPreOpGrammarAddon005Handler
    | L=phpExprPrec Op=phpExprUnaryPostOpGrammarAddon005                        #phpExprUnaryPostOpGrammarAddon005Handler
    | L=phpExprPrec Op=phpExprBinaryOpGrammarAddon005 R=phpExprPrec             #phpExprBinaryOpGrammarAddon005Handler
    | L=phpExprPrec Op=T_LOGICAL_AND R=phpExprPrec                              #phpExprLogicalAnd
    | L=phpExprPrec Op=T_LOGICAL_XOR R=phpExprPrec                              #phpExprLogicalXor
    | L=phpExprPrec Op=T_LOGICAL_OR R=phpExprPrec                               #phpExprLogicalOr
    | <assoc=right> Op=T_INCLUDE R=phpExprPrec                                  #internalFunctionInclude
    | <assoc=right> Op=T_INCLUDE_ONCE R=phpExprPrec                             #internalFunctionIncludeOnce
    | <assoc=right> Op=T_REQUIRE R=phpExprPrec                                  #internalFunctionRequire
    | <assoc=right> Op=T_REQUIRE_ONCE R=phpExprPrec                             #internalFunctionRequireOnce
    | <assoc=right> Attributes=attributes? IsStatic=T_STATIC?
        functionModifiersGrammarAddon Op=fn ReturnsRef=returnsRef
        functionNameGrammarAddon FindDocComment=T_OPEN_ROUND_BRACE
        ParameterList=parameterList T_CLOSE_ROUND_BRACE
        OptionalReturnType=returnType T_DOUBLE_ARROW R=phpExprPrec              #phpExprInlineFunctionShort
    | <assoc=right> Op=T_THROW R=phpExprPrec                                    #phpExprThrow
    | Op=phpExprUnaryPreOpGrammarAddon006 R=phpExprPrec                         #phpExprUnaryPreOpGrammarAddon006Handler
    | L=phpExprPrec Op=phpExprUnaryPostOpGrammarAddon006                        #phpExprUnaryPostOpGrammarAddon006Handler
    | L=phpExprPrec Op=phpExprBinaryOpGrammarAddon006 R=phpExprPrec             #phpExprBinaryOpGrammarAddon006Handler
    | phpExprBase                                                               #phpExprBaseHandler
    ;

phpExprBase
    : Statement=newNonDereferenceable                                           #exprNewNonDRef
    | Variable=fullyDereferenceable                                             #phpExprVariable
    | Scalar=scalar                                                             #phpExprScalar
    | Function=inlineFunction                                                   #phpExprFunction
    | Function=internalFunctions                                                #phpExprInternalFunction
    | T_EXIT ArgumentList=argumentList?                                         #phpExprExit
    | Expr=matchCheck                                                           #phpExprMatchCheck
    | T_LIST T_OPEN_ROUND_BRACE ArrayPairList=arrayPairList T_CLOSE_ROUND_BRACE #phpExprList
    | phpExprPrecBaseGrammarAddon                                               #phpExprPrecBaseGrammarAddonHandler
    ;

phpExprPrecBaseGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

optionalExpr
    : Expr=expr?
    ;

//#endregion Expressions

//#region Identifiers

reservedNonModifiers
    : RNM=reservedNonModifiersWithoutConstructor
    | TokenValue=T_CONSTRUCT_METHOD
    ;

reservedNonModifiersWithoutConstructor
    : RNM=reservedNonModifiersBase
    | RNMGrammarAddon=reservedNonModifiersGrammarAddon
    ;

reservedNonModifiersGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

reservedNonModifiersBase
    : TokenValue=T_INCLUDE
    | TokenValue=T_INCLUDE_ONCE
    | TokenValue=T_EVAL
    | TokenValue=T_REQUIRE
    | TokenValue=T_REQUIRE_ONCE
    | TokenValue=T_LOGICAL_OR
    | TokenValue=T_LOGICAL_XOR
    | TokenValue=T_LOGICAL_AND
    | TokenValue=T_INSTANCEOF
    | TokenValue=T_NEW
    | TokenValue=T_CLONE
    | TokenValue=T_EXIT
    | TokenValue=T_IF
    | TokenValue=T_ELSEIF
    | TokenValue=T_ELSE
    | TokenValue=T_ENDIF
    | TokenValue=T_ECHO
    | TokenValue=T_DO
    | TokenValue=T_WHILE
    | TokenValue=T_ENDWHILE
    | TokenValue=T_FOR
    | TokenValue=T_ENDFOR
    | TokenValue=T_FOREACH
    | TokenValue=T_ENDFOREACH
    | TokenValue=T_DECLARE
    | TokenValue=T_ENDDECLARE
    | TokenValue=T_AS
    | TokenValue=T_TRY
    | TokenValue=T_CATCH
    | TokenValue=T_FINALLY
    | TokenValue=T_THROW
    | TokenValue=T_USE
    | TokenValue=T_INSTEADOF
    | TokenValue=T_GLOBAL
    | TokenValue=T_VAR
    | TokenValue=T_UNSET
    | TokenValue=T_ISSET
    | TokenValue=T_EMPTY
    | TokenValue=T_CONTINUE
    | TokenValue=T_GOTO
    | TokenValue=T_FUNCTION
    | TokenValue=T_CONST
    | TokenValue=T_RETURN
    | TokenValue=T_PRINT
    | TokenValue=T_YIELD
    | TokenValue=T_LIST
    | TokenValue=T_SWITCH
    | TokenValue=T_ENDSWITCH
    | TokenValue=T_CASE
    | TokenValue=T_DEFAULT
    | TokenValue=T_BREAK
    | TokenValue=T_ARRAY
    | TokenValue=T_CALLABLE
    | TokenValue=T_EXTENDS
    | TokenValue=T_IMPLEMENTS
    | TokenValue=T_NAMESPACE
    | TokenValue=T_TRAIT
    | TokenValue=T_INTERFACE
    | TokenValue=T_CLASS
    | TokenValue=T_CLASS_C
    | TokenValue=T_TRAIT_C
    | TokenValue=T_FUNC_C
    | TokenValue=T_METHOD_C
    | TokenValue=T_LINE
    | TokenValue=T_FILE
    | TokenValue=T_DIR
    | TokenValue=T_NS_C
    | TokenValue=T_FN
    | TokenValue=T_MATCH
    | TokenValue=T_ENUM
    | TokenValue=T_PROPERTY_C
    ;

semiReserved
    : RNM=reservedNonModifiers
    | SemiReserved=semiReservedBase
    | SemiReservedGrammarAddon=semiReservedGrammarAddon
    ;

semiReservedWithoutConstructor
    : RNM=reservedNonModifiersWithoutConstructor
    | SemiReserved=semiReservedBase
    | SemiReservedGrammarAddon=semiReservedGrammarAddon
    ;

semiReservedGrammarAddon
    // ! to be overridden in other grammars
    : TokenValue=T_NO_GRAMMAR_ADDON_0000
    ;

semiReservedBase
    : TokenValue=T_STATIC
    | TokenValue=T_ABSTRACT
    | TokenValue=T_FINAL
    | TokenValue=T_PRIVATE
    | TokenValue=T_PROTECTED
    | TokenValue=T_PUBLIC
    | TokenValue=T_READONLY
    ;

ampersand
    : TokenValue=T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG
    | TokenValue=T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG
    ;

identifier
    : TokenValue=T_STRING
    | SemiReserved=semiReserved
    ;

identifierWithoutConstructor
    : TokenValue=T_STRING
    | SemiReserved=semiReservedWithoutConstructor
    ;

namespaceDeclarationName
    : Name=identifier
    | QualifiedName=T_NAME_QUALIFIED
    ;

namespaceName
    : (Name=T_STRING | QualifiedName=T_NAME_QUALIFIED)
    | namespaceNameGrammarAddon
    ;

namespaceNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

legacyNamespaceName
    : Name=namespaceName
    | FullyQualifiedName=T_NAME_FULLY_QUALIFIED
    | legacyNamespaceNameGrammarAddon
    ;

legacyNamespaceNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

name
    : TokenValue=T_STRING                                                       #nameNotQualified
    | TokenValue=T_NAME_QUALIFIED                                               #nameSemiQualified
    | TokenValue=T_NAME_FULLY_QUALIFIED                                         #nameFullyQualified
    | TokenValue=T_NAME_RELATIVE                                                #nameRelative
    | TokenValueGrammarAddon=nameTokenValueGrammarAddon                         #nameTokenValueGrammarAddonHandler
    ;

nameTokenValueGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

className
    : (Identifier=name | IsStatic=T_STATIC)
        classNameIdentifierGrammarAddon
    ;

classNameIdentifierGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

classNameReference
    : ClassName=className
    | NewVariable=newVariable classNameIdentifierGrammarAddon
    | T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        classNameIdentifierGrammarAddon
    ;

//#endregion Identifiers

//#region Attributes

attributeDecl
    : ClassName=className ArgumentList=argumentList?
    ;

attributeGroup
    : Items+=attributeDecl (T_SYM_COMMA Items+=attributeDecl)*
    ;

attribute
    : T_ATTRIBUTE AttributesList=attributeGroup possibleComma
        T_CLOSE_SQUARE_BRACE
    ;

attributes
    : Items+=attribute+
    ;

attributedStatement
    : Statement=functionDeclarationStatement                                    #functionDeclStatement
    | Statement=classDeclarationStatement                                       #classDeclStatement
    | Statement=traitDeclarationStatement                                       #traitDeclStatement
    | Statement=interfaceDeclarationStatement                                   #interfaceDeclStatement
    | Statement=enumDeclarationStatement                                        #enumDeclStatement
    ;

// PHP 8.5: attributes on compile-time non-class `const` (php-src attributed_top_statement
// includes T_CONST). Attributes require a single declarator — multi-const lists are illegal
// when attributed (php-src compile error: "Cannot apply attributes to multiple constants").
attributedConstStatement
    : T_CONST ConstDecl=constDecl T_SYM_SEMICOLON
    ;

//#endregion Attributes

//#region Top Statements

topStatementListWithRequiredFinalTerminal
    : Items+=topStatement+
    ;

topStatementNoTerminal
    : Statement=statementWithoutTerminal                                        #topStatementStatementWithoutTerminal
    // Attributed top-level const must precede the general attributedStatement alternative
    // so `#[…] const X = …;` is not attempted as class/function/etc.
    | Attributes=attributes Statement=attributedConstStatement                  #attributedConstTopStatement
    | Attributes=attributes? Statement=attributedStatement                      #attributedTopStatement
    | T_NAMESPACE NamespaceName=namespaceDeclarationName T_SYM_SEMICOLON        #nameSpaceDecl
    | T_NAMESPACE NamespaceName=namespaceDeclarationName T_OPEN_CURLY_BRACE
        StatementList=topStatementListWithRequiredFinalTerminal?
        T_CLOSE_CURLY_BRACE                                                     #namespaceGroupDecl
    | T_NAMESPACE T_OPEN_CURLY_BRACE
        StatementList=topStatementListWithRequiredFinalTerminal?
        T_CLOSE_CURLY_BRACE                                                     #anonNamespaceDecl
    | T_USE UseDecl=mixedGroupUseDeclaration T_SYM_SEMICOLON                    #importGroupDecls
    | T_USE UseType=useType UseDecl=groupUseDeclaration T_SYM_SEMICOLON         #importTypeGroupDecls
    | T_USE UseDecl=useDeclarations T_SYM_SEMICOLON                             #importDecls
    | T_USE UseType=useType UseDecl=useDeclarations T_SYM_SEMICOLON             #importType
    | T_CONST ConstList=constList T_SYM_SEMICOLON                               #constDeclStmt
    | topStatementGrammarAddon                                                  #topStatementGrammarAddonHandler
    ;

topStatementNeedsTerminal
    : Statement=statementRequiringTerminal                                      #topStatementRequiringTerminal
    | T_HALT_COMPILER T_OPEN_ROUND_BRACE T_CLOSE_ROUND_BRACE                    #topStatementHaltCompiler
    ;

topStatement
    : topStatementNoTerminal
    | topStatementNeedsTerminal statementTerminal
    ;

topStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

useType
    : TokenValue=T_FUNCTION
    | TokenValue=T_CONST
    | TokenValueGrammarAddon=useTypeGrammarAddon
    ;

useTypeGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

groupUseDeclaration
    : NamespaceName=legacyNamespaceName NsSep=T_NS_SEPARATOR T_OPEN_CURLY_BRACE
        UseDeclList=unprefixedUseDeclarations possibleComma T_CLOSE_CURLY_BRACE
    ;

mixedGroupUseDeclaration
    : NamespaceName=legacyNamespaceName NsSep=T_NS_SEPARATOR T_OPEN_CURLY_BRACE
        UseDeclList=inlineUseDeclarations possibleComma T_CLOSE_CURLY_BRACE
    ;

inlineUseDeclarations
    : Items+=inlineUseDeclaration
        (T_SYM_COMMA Items+=inlineUseDeclaration)*
    ;

unprefixedUseDeclarations
    : Items+=unprefixedUseDeclaration
        (T_SYM_COMMA Items+=unprefixedUseDeclaration)*
    ;

useDeclarations
    : Items+=useDeclaration (T_SYM_COMMA Items+=useDeclaration)*
    ;

inlineUseDeclaration
    : UseType=useType? UseDecl=unprefixedUseDeclaration
    ;

unprefixedUseDeclaration
    : NamespaceName=namespaceName (T_AS AliasedAs=T_STRING)?
    | unprefixedUseDeclarationGrammarAddon
    ;

unprefixedUseDeclarationGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

useDeclaration
    : NamespaceName=legacyNamespaceName (T_AS AliasedAs=T_STRING)?
    | useDeclarationGrammarAddon
    ;

useDeclarationGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

//#endregion Top Statements

//#region Statements

constList
    : Items+=constDecl (T_SYM_COMMA Items+=constDecl)*
    ;

innerStatementList
    : Items+=innerStatement*
    ;

statementTerminal
    : T_SYM_SEMICOLON
    | InlineOutput=phpInlineOutputStatement
    ;

innerStatement
    : Statement=statement                                                       #notAttributedInnerStatement
    | Attributes=attributes? Statement=attributedStatement                      #attributedInnerStatement
    | Op=T_YIELD statementTerminal                                              #innerStatementYield
    | StatementGrammarAddon=innerStatementGrammarAddon                          #innerStatementGrammarAddonHandler
    ;

innerStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

statementRequiringTerminal
    : T_DO Statement=statement T_WHILE T_OPEN_ROUND_BRACE Expr=expr
        T_CLOSE_ROUND_BRACE                                                     #statementDoWhile
    | Op=T_BREAK Expr=optionalExpr                                              #statementBreak
    | Op=T_CONTINUE Expr=optionalExpr                                           #statementContinue
    | Op=T_RETURN Expr=optionalExpr                                             #statementReturn
    | Op=T_GLOBAL VariableList=globalVarList                                    #statementGlobal
    | Op=T_STATIC VariableList=staticVarList                                    #statementStatic
    | Op=T_ECHO Expr=echoExprList                                               #statementEcho
    // PHP 8.5 `(void) expr;` — statement discard form (not a value-producing unary cast).
    | Op=T_VOID_CAST Expr=expr                                                  #statementVoidCast
    | Statement=phpTopExpr                                                      #statementTopExpr
    | Op=T_UNSET T_OPEN_ROUND_BRACE VariableList=unsetVariables possibleComma
        T_CLOSE_ROUND_BRACE                                                     #statementUnset
    | T_GOTO Label=T_STRING                                                     #statementGoto
    | Statement=altIfStmt                                                       #statementAltIf
    | T_WHILE T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        Statement=whileStatement                                                #statementAltWhile
    | ForSyntax=forSyntax Statement=forStatement                                #statementAltFor
    | T_FOREACH T_OPEN_ROUND_BRACE Expr=expr T_AS (KeyVariable=foreachVariable
        T_DOUBLE_ARROW)? ValueVariable=foreachVariable T_CLOSE_ROUND_BRACE
        Statement=foreachStatement                                              #statementAltForeach
    | T_DECLARE T_OPEN_ROUND_BRACE DeclareList=constList T_CLOSE_ROUND_BRACE
        Statement=declareStatement                                              #statementAltDeclare
    | T_SWITCH T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        CaseList=switchCaseList                                                 #statementAltSwitch
    | StatementGrammarAddon=statementRequiringTerminalGrammarAddon              #statementRequiringTerminalGrammarAddonHandler
    ;

statementWithoutTerminal
    : T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE   #statementBlock
    | Statement=ifStmt                                                          #statementIf
    | T_WHILE T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        Statement=statement                                                     #statementWhile
    | ForSyntax=forSyntax Statement=statement                                   #statementFor
    | T_SWITCH T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        T_OPEN_CURLY_BRACE T_SYM_SEMICOLON? CaseList=caseList
        T_CLOSE_CURLY_BRACE                                                     #statementSwitch
    | T_FOREACH T_OPEN_ROUND_BRACE Expr=expr T_AS (KeyVariable=foreachVariable
        T_DOUBLE_ARROW)? ValueVariable=foreachVariable T_CLOSE_ROUND_BRACE
        Statement=statement                                                     #statementForeach
    | T_DECLARE T_OPEN_ROUND_BRACE DeclareList=constList T_CLOSE_ROUND_BRACE
        Statement=statement                                                     #statementDeclare
    | T_TRY T_OPEN_CURLY_BRACE StatementList=innerStatementList
        T_CLOSE_CURLY_BRACE
        CatchList=catchList FinallyStatement=finallyStatement                   #statementTryCatch
    | Label=T_STRING T_SYM_COLON                                                #statementLabel
    | T_SYM_SEMICOLON                                                           #statementEmptyStatement
    | InlineOutput=phpInlineOutputStatement                                     #statementInlineOutput
    | StatementGrammarAddon=statementWithoutTerminalGrammarAddon                #statementWithoutTerminalGrammarAddonHandler
    ;

statement
    : statementWithoutTerminal
    | statementRequiringTerminal statementTerminal
    ;

statementWithoutTerminalGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

statementRequiringTerminalGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

unsetVariables
    : Items+=unsetVariable (T_SYM_COMMA Items+=unsetVariable)*
    ;

unsetVariable
    : Variable=variable
    ;

constDecl
locals [ _findDocComment:IToken = null ]
@after { _localctx._findDocComment = _localctx.Stop; }
    : Identifier=T_STRING T_SYM_EQUAL ValueExpr=expr
    ;

echoExprList
    : Items+=echoExpr (T_SYM_COMMA Items+=echoExpr)*
    ;

echoExpr
    : Expr=expr
    ;

internalFunctions
    : T_ISSET T_OPEN_ROUND_BRACE VariableList=issetVariables possibleComma
        T_CLOSE_ROUND_BRACE                                                     #internalFunctionIsset
    | T_EMPTY T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE                  #internalFunctionEmpty
    | T_EVAL T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE                   #internalFunctionEval
    | internalFunctionsGrammarAddon                                             #internalFunctionsGrammarAddonHandler
    ;

internalFunctionsGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

issetVariables
    : Items+=issetVariable (T_SYM_COMMA Items+=issetVariable)*
    ;

issetVariable
    : Expr=expr
    ;

//#endregion Statements

//#region TryCatch Blocks

catchList
    : Items+=catchBlock*
    ;

catchBlock
    : T_CATCH T_OPEN_ROUND_BRACE CatchNameList=catchNameList
        Variable=optionalVariable T_CLOSE_ROUND_BRACE T_OPEN_CURLY_BRACE
        StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    | catchBlockGrammarAddon
    ;

catchBlockGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

catchNameList
    : Items+=className (T_SYM_PIPE Items+=className)*
    ;

optionalVariable
    : TokenValue=T_VARIABLE?
    ;

finallyStatement
    : (T_FINALLY T_OPEN_CURLY_BRACE StatementList=innerStatementList
        T_CLOSE_CURLY_BRACE)?
    ;

//#endregion TryCatch Blocks

//#region Functions

functionName
    : TokenValue=T_STRING
    | TokenValue=T_READONLY
    ;

functionDeclarationStatement
    : functionModifiersGrammarAddon function ReturnsRef=returnsRef
        Identifier=functionName functionNameGrammarAddon
        FindDocComment=T_OPEN_ROUND_BRACE functionParametersGrammarAddon
        ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    | functionDeclarationStatementGrammarAddon
    ;

functionModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

functionNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

functionParametersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

functionDeclarationStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

isReference
    : TokenValue=T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG?
    ;

isVariadic
    : TokenValue=T_ELLIPSIS?
    ;

inlineFunction
    : Attributes=attributes? IsStatic=T_STATIC? functionModifiersGrammarAddon
        function ReturnsRef=returnsRef functionNameGrammarAddon
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE LexicalVars=lexicalVars ReturnType=returnType
        T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    ;

fn
    : T_FN
    ;

function
    : T_FUNCTION
    ;

returnsRef
    : ReturnsRef=ampersand?
    ;

lexicalVars
    : (T_USE T_OPEN_ROUND_BRACE LexicalVarsList=lexicalVarList possibleComma
        T_CLOSE_ROUND_BRACE)?
    ;

lexicalVarList
    : Items+=lexicalVar (T_SYM_COMMA Items+=lexicalVar)*
    ;

lexicalVar
    : IsRef=ampersand? Variable=T_VARIABLE
    ;

//#endregion Functions

//#region Objects

classDeclarationStatement
    : Modifiers=classModifiers? ObjectType=T_CLASS Identifier=T_STRING
        classNameGrammarAddon Extends=extendsFrom
        Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=classStatementList T_CLOSE_CURLY_BRACE
    | classDeclarationStatementGrammarAddon
    ;

classDeclarationStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

classNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

classModifiers
    : Items+=classModifier+
    ;

classModifiersOptional
    : classModifiers?
    ;

classModifier
    : TokenValue=T_ABSTRACT
    | TokenValue=T_FINAL
    | TokenValue=T_READONLY
    | classModifierGrammarAddon
    ;

classModifierGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

traitDeclarationStatement
    : traitModifiersGrammarAddon ObjectType=T_TRAIT Identifier=T_STRING
        traitNameGrammarAddon Extends=extendsFrom Implements=implementsList
        FindDocComment=T_OPEN_CURLY_BRACE StatementList=classStatementList
        T_CLOSE_CURLY_BRACE
    | traitDeclarationStatementGrammarAddon
    ;

traitDeclarationStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

traitNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

traitModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

interfaceDeclarationStatement
    : interfaceModifiersGrammarAddon ObjectType=T_INTERFACE Identifier=T_STRING
        interfaceNameGrammarAddon Extends=interfaceExtendsList
        FindDocComment=T_OPEN_CURLY_BRACE StatementList=classStatementList
        T_CLOSE_CURLY_BRACE
    | interfaceDeclarationStatementGrammarAddon
    ;

interfaceDeclarationStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

interfaceNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

interfaceModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

enumDeclarationStatement
    : enumModifiersGrammarAddon ObjectType=T_ENUM Identifier=T_STRING
        enumNameGrammarAddon enumBackingType Implements=implementsList
        FindDocComment=T_OPEN_CURLY_BRACE StatementList=classStatementList
        T_CLOSE_CURLY_BRACE
    | enumDeclarationStatementGrammarAddon
    ;

enumDeclarationStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

enumNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

enumModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

enumBackingType
    : (T_SYM_COLON TypeExpr=typeExpr)?
    ;

enumCase
    : FindDocComment=T_CASE Identifier=identifier Expr=enumCaseExpr
        T_SYM_SEMICOLON
    ;

enumCaseExpr
    : (T_SYM_EQUAL Expr=expr)?
    ;

extendsFrom
    : (T_EXTENDS ClassName=className)?
    ;

interfaceExtendsList
    : (T_EXTENDS ClassNameList=classNameList)?
    ;

implementsList
    : (T_IMPLEMENTS ClassNameList=classNameList)?
    ;

classStatementList
    : Items+=classStatement*
    ;

attributedClassStatement
    : Modifiers=propertyModifiers TypeExpr=optionalTypeWithoutStatic
        PropertyList=propertyList T_SYM_SEMICOLON                               #classProperties
    | Modifiers=propertyModifiers TypeExpr=optionalTypeWithoutStatic
        PropertyAccessors=hookedProperty                                        #classPropertyAccessors
    | Modifiers=classConstModifiers T_CONST ConstList=classConstList
        T_SYM_SEMICOLON                                                         #classConsts
    | Modifiers=classConstModifiers T_CONST typeExpr ConstList=classConstList
        T_SYM_SEMICOLON                                                         #classTypedConsts
    | EnumCase=enumCase                                                         #classEnumCase
    | Modifiers=methodModifiers function Identifier=T_CONSTRUCT_METHOD
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=ctorParameterList
        T_CLOSE_ROUND_BRACE StatementList=methodBody                            #phpClassCtor
    | Modifiers=methodModifiers function ReturnsRef=returnsRef
        Identifier=identifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType StatementList=methodBody      #phpClassMethod
    | attributedClassStatementGrammarAddon                                      #attributedClassStatementGrammarAddonHandler
    ;

attributedClassStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

classStatement
    : Attributes=attributes? Statement=attributedClassStatement                 #classStatementAttributed
    | T_USE TraitNameList=classNameList Adaptations=traitAdaptations            #classTraitUse
    | classStatementGrammarAddon                                                #classStatementGrammarAddonHandler
    ;

classStatementGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

classNameList
    : Items+=className (T_SYM_COMMA Items+=className)*
    ;

traitAdaptations
    : T_SYM_SEMICOLON
    | T_OPEN_CURLY_BRACE TraitAdaptationList=traitAdaptationList?
        T_CLOSE_CURLY_BRACE
    ;

traitAdaptationList
    : Items+=traitAdaptation+
    ;

traitAdaptation
    : Precedence=traitPrecedence T_SYM_SEMICOLON
    | Alias=traitAlias T_SYM_SEMICOLON
    ;

traitPrecedence
    : MethodReference=absoluteTraitMethodReference T_INSTEADOF
        TraitNameList=classNameList
    ;

traitAlias
    : AliasOf=traitMethodReference T_AS AliasString=T_STRING
        traitAliasNameGrammarAddon                                              #traitAliasRename
    | AliasOf=traitMethodReference T_AS AliasRNM=reservedNonModifiers
        traitAliasNameGrammarAddon                                              #traitAliasRename
    | AliasOf=traitMethodReference T_AS Modifier=memberModifier
        (Identifier=identifier traitAliasNameGrammarAddon)?                     #traitAliasVisibility
    | traitAliasGrammarAddon                                                    #traitAliasGrammarAddonHandler
    ;

traitAliasGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

traitAliasNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

traitMethodReference
    : Identifier=identifier traitMethodIdentifierGrammarAddon
    | MethodReference=absoluteTraitMethodReference
    ;

absoluteTraitMethodReference
    : ClassName=className T_DOUBLE_COLON
        Identifier=identifier traitMethodIdentifierGrammarAddon
    ;

traitMethodIdentifierGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

traitPropertyReference
    : Variable=T_VARIABLE
    | VariableReference=absoluteTraitPropertyReference
    ;

absoluteTraitPropertyReference
    : ClassName=className T_DOUBLE_COLON Variable=T_VARIABLE
    ;

methodBody
    : T_SYM_SEMICOLON
    | T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    ;

propertyModifiers
    : Modifiers=nonEmptyMemberModifiers
    | IsVar=T_VAR
    | ModifiersGrammarAddon=propertyModifiersGrammarAddon
    ;

propertyModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

methodModifiers
    : (Modifiers=nonEmptyMemberModifiers)?
    | ModifiersGrammarAddon=methodModifiersGrammarAddon
    ;

methodModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

classConstModifiers
    : (Modifiers=nonEmptyMemberModifiers)?
    | ModifiersGrammarAddon=classConstModifiersGrammarAddon
    ;

classConstModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

nonEmptyMemberModifiers
    : Items+=memberModifier+
    ;

memberModifier
    : TokenValue=T_PUBLIC
    | TokenValue=T_PROTECTED
    | TokenValue=T_PRIVATE
    | TokenValue=T_PUBLIC_SET
    | TokenValue=T_PROTECTED_SET
    | TokenValue=T_PRIVATE_SET
    | TokenValue=T_STATIC
    | TokenValue=T_ABSTRACT
    | TokenValue=T_FINAL
    | TokenValue=T_READONLY
    | TokenValueGrammarAddon=memberModifierGrammarAddon
    ;

memberModifierGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

propertyList
    : Items+=property (T_SYM_COMMA Items+=property)*
    ;

property
locals [ _findDocComment:IToken = null ]
@after { _localctx._findDocComment = _localctx.Stop; }
    : Variable=T_VARIABLE (T_SYM_EQUAL ValueExpr=expr)?
    ;

hookedProperty
    : Variable=T_VARIABLE FindDocComment=T_OPEN_CURLY_BRACE
        Accessors=propertyHookList T_CLOSE_CURLY_BRACE                          #propertyAccessor
    | Variable=T_VARIABLE T_SYM_EQUAL Expr=expr
        FindDocComment=T_OPEN_CURLY_BRACE Accessors=propertyHookList
        T_CLOSE_CURLY_BRACE                                                     #propertyAccessorWithDefaultValue
    | Property=hookedPropertyGrammarAddon                                       #hookedPropertyGrammarAddonHandler
    ;

hookedPropertyGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

propertyHookList
    // attributes? before each hook matches php-src property_hook_list
    // (attributes attach to the following property_hook AST node).
    : Items+=propertyHook*
    ;

optionalPropertyHookList
locals [ _findDocComment:IToken = null ]
@after { _localctx._findDocComment = _localctx.Stop; }
    : (T_OPEN_CURLY_BRACE propertyHookList T_CLOSE_CURLY_BRACE)?
    ;

propertyHookModifiers
    : (Modifiers=nonEmptyMemberModifiers)?
    | ModifiersGrammarAddon=propertyHookModifiersGrammarAddon
    ;

propertyHookModifiersGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

propertyHook
    : Attributes=attributes? Modifiers=propertyHookModifiers ReturnsRef=returnsRef
        AccessorName=T_STRING Parameters=optionalParameterList
        AccessorBody=propertyHookBody
    ;

propertyHookBody
    // Bare `;` matches php-src property_hook_body (abstract / interface hooks: `{ get; set; }`).
    : T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    | T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON
    | T_SYM_SEMICOLON
    | propertyHookBodyGrammarAddon
    ;

propertyHookBodyGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

optionalParameterList
    : (T_OPEN_ROUND_BRACE ParameterList=parameterList T_CLOSE_ROUND_BRACE)?
    ;

classConstList
    : Items+=classConstDecl (T_SYM_COMMA Items+=classConstDecl)*
    ;

classConstDecl
locals [ _findDocComment:IToken = null ]
@after { _localctx._findDocComment = _localctx.Stop; }
    : (Identifier=T_STRING|IdentifierSR=semiReserved) T_SYM_EQUAL
        ValueExpr=expr
    ;

anonymousClass
    : Modifiers=classModifiersOptional ObjectType=T_CLASS Arguments=ctorArguments
        Extends=extendsFrom Implements=implementsList
        FindDocComment=T_OPEN_CURLY_BRACE StatementList=classStatementList
        T_CLOSE_CURLY_BRACE
    ;

ctorArguments
    : ArgumentList=argumentList?
    ;

//#endregion Objects

//#region Blocks

forSyntax
    : T_FOR T_OPEN_ROUND_BRACE InitExpr=forExprs T_SYM_SEMICOLON
        TestExpr=forCondExprs T_SYM_SEMICOLON UpdateExpr=forExprs
        T_CLOSE_ROUND_BRACE
    ;

foreachVariable
    : IsRef=ampersand? Variable=variable
    | T_LIST T_OPEN_ROUND_BRACE ArrayPairList=arrayPairList T_CLOSE_ROUND_BRACE
    ;

forStatement
    : T_SYM_COLON StatementList=innerStatementList T_ENDFOR
    ;

foreachStatement
    : T_SYM_COLON StatementList=innerStatementList T_ENDFOREACH
    ;

declareStatement
    : T_SYM_COLON StatementList=innerStatementList T_ENDDECLARE
    ;

switchCaseList
    : T_SYM_COLON T_SYM_SEMICOLON? CaseList=caseList T_ENDSWITCH
    ;

caseList
    : Items+=caseItem*
    ;

caseItem
    : (CaseExpr=caseExpr | CaseDefault=caseDefault)
    ;

caseExpr
    : T_CASE Expr=expr caseSeparator StatementList=innerStatementList
    ;

caseDefault
    : T_DEFAULT caseSeparator DefaultStatementList=innerStatementList
    ;

caseSeparator
    : T_SYM_COLON
    | T_SYM_SEMICOLON
    ;

matchCheck
    : T_MATCH T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        T_OPEN_CURLY_BRACE ArmList=matchArmList T_CLOSE_CURLY_BRACE
    ;

matchArmList
    : (ArmList=nonEmptyMatchArmList possibleComma)?
    ;

nonEmptyMatchArmList
    : Items+=matchArm (T_SYM_COMMA Items+=matchArm)*
    ;

matchArm
    : ArmCondList=matchArmCondList possibleComma T_DOUBLE_ARROW Expr=expr
    | IsDefault=T_DEFAULT possibleComma T_DOUBLE_ARROW Expr=expr
    ;

matchArmCondList
    : Items+=expr (T_SYM_COMMA Items+=expr)*
    ;


whileStatement
    : T_SYM_COLON StatementList=innerStatementList T_ENDWHILE
    ;

ifStmtWithoutElse
    : T_IF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        Statement=statement
    | ChainedIfStatement=ifStmtWithoutElse T_ELSEIF T_OPEN_ROUND_BRACE Expr=expr
        T_CLOSE_ROUND_BRACE Statement=statement
    ;

ifStmt
    : IfStatement=ifStmtWithoutElse (T_ELSE ElseStatement=statement)?
    ;

altIfStmtWithoutElse
    : T_IF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE T_SYM_COLON
        Statement=innerStatementList
    | ChainedIfStatement=altIfStmtWithoutElse T_ELSEIF T_OPEN_ROUND_BRACE
        Expr=expr T_CLOSE_ROUND_BRACE T_SYM_COLON Statement=innerStatementList
    ;

altIfStmt
    : IfStatement=altIfStmtWithoutElse
        (T_ELSE T_SYM_COLON ElseStatement=innerStatementList)? T_ENDIF
    ;

forExprs
    : ExprList=nonEmptyForExprs?
    ;

// php-src for_cond_exprs: empty | non_empty_for_exprs ',' expr | expr
// — `(void)` may appear only as a non-final item before a trailing plain expr.
forCondExprs
    : ExprList=nonEmptyForCondExprs?
    ;

nonEmptyForExprs
    : Items+=forExprItem (T_SYM_COMMA Items+=forExprItem)*
    ;

nonEmptyForCondExprs
    : (Items+=forExprItem T_SYM_COMMA)* Last=expr
    ;

forExprItem
    : Op=T_VOID_CAST Expr=expr                                                  #forVoidCastExpr
    | Expr=expr                                                                 #forPlainExpr
    ;

//#endregion Blocks

//#region Parameters And Arguments

parameterList
    : (ParameterList=nonEmptyParameterList possibleComma)?
    ;

ctorParameterList
    : (ParameterList=nonEmptyCtorParameterList possibleComma)?
    ;

nonEmptyParameterList
    : Items+=attributedParameter (T_SYM_COMMA Items+=attributedParameter)*
    ;

nonEmptyCtorParameterList
    : Items+=attributedCtorParameter
        (T_SYM_COMMA Items+=attributedCtorParameter)*
    ;

attributedParameter
    : Attributes=attributes? Parameter=parameter
    ;

attributedCtorParameter
    : Attributes=attributes? Modifiers=optionalCppModifiers
        Parameter=parameter Accessors=optionalPropertyHookList
    ;

optionalCppModifiers
    : (nonEmptyMemberModifiers)?
    ;

parameter
locals [ _findDocComment:IToken = null ]
@after {
    _localctx._findDocComment = _localctx.FindDocCommentCheck ??
        _localctx.Stop;
}
    : TypeExpr=parameterTypeExpressionGrammarAddon
        IsRef=isReference IsVariadic=isVariadic Variable=T_VARIABLE
        (FindDocCommentCheck=T_SYM_EQUAL ValueExpr=expr)?
    ;

parameterTypeExpressionGrammarAddon
    // ! to be overridden in other grammars
    : optionalTypeWithoutStatic
    ;

argumentList
    : T_OPEN_ROUND_BRACE
        (
            (ArgumentList=nonEmptyArgumentList possibleComma)
            | Ellipsis=T_ELLIPSIS // first class callable
        )?
        T_CLOSE_ROUND_BRACE
    ;

nonEmptyArgumentList
    : Items+=argument (T_SYM_COMMA Items+=argument)*
    ;

argument
    : IsVariadic=isVariadic Expr=expr
    | Identifier=identifier T_SYM_COLON Expr=expr
    ;

// php-src clone_argument_list — resolves shift/reduce of clone($expr):
// a bare first expr must be followed by `,` (not valid for parenthesized expr),
// so clone($x) is T_CLONE + parenthesized expr, while clone($x,), clone($x, $y),
// clone(), clone(...), named/unpack-first forms use this production.
cloneArgumentList
    : T_OPEN_ROUND_BRACE T_CLOSE_ROUND_BRACE
    | T_OPEN_ROUND_BRACE ArgumentList=nonEmptyCloneArgumentList possibleComma
        T_CLOSE_ROUND_BRACE
    | T_OPEN_ROUND_BRACE Expr=expr T_SYM_COMMA T_CLOSE_ROUND_BRACE
    ;

nonEmptyCloneArgumentList
    : FirstExpr=expr T_SYM_COMMA FirstArg=argument
        (T_SYM_COMMA Rest+=argument)*
    | FirstNoExpr=cloneArgumentNoExpr (T_SYM_COMMA Rest+=argument)*
    ;

// Subset of php-src argument_no_expr (no `?` placeholders — not in Tyhp yet).
cloneArgumentNoExpr
    : Identifier=identifier T_SYM_COLON Expr=expr
    | IsVariadic=T_ELLIPSIS Expr=expr
    | Ellipsis=T_ELLIPSIS
    ;

globalVarList
    : Items+=globalVar (T_SYM_COMMA Items+=globalVar)*
    ;

globalVar
    : Variable=simpleVariable
    ;


staticVarList
    : Items+=staticVar (T_SYM_COMMA Items+=staticVar)*
    ;

staticVar
    : Variable=T_VARIABLE (T_SYM_EQUAL Expr=expr)?
    ;

//#endregion Parameters And Arguments

//#region Types

optionalTypeWithoutStatic
    : TypeExpr=typeExprWithoutStatic?
    ;

typeExpr
    : IsNullable=T_SYM_QUESTION? BaseType=type
    | UnionType=unionType
    | IntersectionType=intersectionType
    | typeExprGrammarAddon
    ;

typeExprGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

type
    : BaseType=typeWithoutStatic
    | StaticType=T_STATIC typeNameGrammarAddon
    ;

unionTypeElement
    : BaseType=type
    | T_OPEN_ROUND_BRACE IntersectionType=intersectionType T_CLOSE_ROUND_BRACE
    ;

unionType
    : Items+=unionTypeElement (T_SYM_PIPE Items+=unionTypeElement)+
    ;

intersectionType
    : Items+=type (T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG Items+=type)+
    ;

typeExprWithoutStatic
    : IsNullable=T_SYM_QUESTION? BaseType=typeWithoutStatic
    | UnionType=unionTypeWithoutStatic
    | IntersectionType=intersectionTypeWithoutStatic
    | typeExprWithoutStaticGrammarAddon
    ;

typeExprWithoutStaticGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

typeWithoutStatic
    : (ArrayType=T_ARRAY | CallableType=T_CALLABLE | Identifier=name)
        typeNameGrammarAddon
    | typeWithoutStaticGrammarAddon
    ;

typeWithoutStaticGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

typeNameGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

unionTypeWithoutStaticElement
    : BaseType=typeWithoutStatic
    | T_OPEN_ROUND_BRACE IntersectionType=intersectionTypeWithoutStatic
        T_CLOSE_ROUND_BRACE
    ;

unionTypeWithoutStatic
    : Items+=unionTypeWithoutStaticElement T_SYM_PIPE
        Items+=unionTypeWithoutStaticElement
        (T_SYM_PIPE Items+=unionTypeWithoutStaticElement)*
    ;

intersectionTypeWithoutStatic
    : Items+=typeWithoutStatic T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG
        Items+=typeWithoutStatic
        (T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG Items+=typeWithoutStatic)*
    ;

returnType
    : (T_SYM_COLON TypeExpr=typeExpr)?                                          #returnTypeType
    | returnTypeGrammarAddon                                                    #returnTypeGrammarAddonHandler
    ;

returnTypeGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

//#endregion Types

//#region Dereferenceables

newDereferenceable
    : T_NEW Identifier=classNameReference Arguments=argumentList                #newClassInstance
    | T_NEW Attributes=attributes? AnonClassDecl=anonymousClass                 #newAnonClassInstance
    | newDereferenceableGrammarAddon                                            #newDereferenceableGrammarAddonHandler
    ;

newDereferenceableGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

newNonDereferenceable
    : T_NEW Identifier=classNameReference                                       #newClassInstanceNonDereferenceable
    | newNonDereferenceableGrammarAddon                                         #newNonDereferenceableGrammarAddonHandler
    ;

newNonDereferenceableGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

dereferenceableScalar
    : TokenValue=T_ARRAY T_OPEN_ROUND_BRACE ArrayPairList=arrayPairList
        T_CLOSE_ROUND_BRACE
    | TokenValue=T_OPEN_SQUARE_BRACE ArrayPairList=arrayPairList
        T_CLOSE_SQUARE_BRACE
    | TokenValue=T_CONSTANT_ENCAPSED_STRING
    | TokenValue=T_BINARY_DOUBLE_QUOTE EncapsList=encapsList? T_DOUBLE_QUOTE
    | TokenValue=T_DOUBLE_QUOTE EncapsList=encapsList? T_DOUBLE_QUOTE
    | TokenValue=T_BACKQUOTE EncapsList=encapsList? T_BACKQUOTE
    | TokenValue=T_BINARY_BACKQUOTE EncapsList=encapsList? T_BACKQUOTE
    ;

scalar
    : RealScalar=realScalar                                                     #scalarReal
    | Scalar=dereferenceableScalar                                              #scalarDereferenceable
    | Scalar=constant                                                           #scalarConstant
    | Scalar=classConstant                                                      #scalarClassConstant
    ;

realScalar
    : Scalar=T_LNUMBER                                                          #scalarLNumber
    | Scalar=T_DNUMBER                                                          #scalarDNumber
    | Scalar=T_ONUMBER                                                          #scalarONumber
    | Scalar=T_HNUMBER                                                          #scalarHNumber
    | Scalar=T_BNUMBER                                                          #scalarBNumber
    | TokenValue=T_START_HEREDOC EncapsList=encapsList? T_END_HEREDOC           #scalarHeredoc
    ;

constant
    : Identifier=name
    | TokenValue=constantTokenValue
    ;

constantTokenValue
    : TokenValue=T_LINE
    | TokenValue=T_FILE
    | TokenValue=T_DIR
    | TokenValue=T_TRAIT_C
    | TokenValue=T_METHOD_C
    | TokenValue=T_FUNC_C
    | TokenValue=T_PROPERTY_C
    | TokenValue=T_NS_C
    | TokenValue=T_CLASS_C
    | TokenValueGrammarAddon=constantTokenValueGrammarAddon
    ;

constantTokenValueGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

simpleVariable
    : Variable=T_VARIABLE
    | T_SYM_DOLLAR T_OPEN_CURLY_BRACE BracedExpr=expr T_CLOSE_CURLY_BRACE
    | T_SYM_DOLLAR DoubleDollarVariable=simpleVariable
    | simpleVariableGrammarAddon
    ;

simpleVariableGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

classConstant
    : (ClassName=className | Prefix=variableClassName)
        Suffix=dereferenceableClassConstantAccessSuffix
    ;

variableClassName
    : fullyDereferenceable
    ;

callArgumentList
    : functionCallGrammarAddon ArgumentList=argumentList
    ;

functionCallGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

fullyDereferenceable
    : DRef=fullyDereferenceable Suffix=fullyDereferenceableSuffix               #fullyDereferenceableDRefSuffix
    | Variable=simpleVariable                                                   #dereferenceableSimple
    | Constant=constantTokenValue                                               #dereferenceableConstant
    | ClassName=className                                                       #dereferenceableClassNamePrefix
    | IsReadOnlyPrefix=T_READONLY                                               #dereferenceableReadOnly
    | Scalar=dereferenceableScalar                                              #dereferenceableScalarRef
    | NewDRef=newDereferenceable                                                #dereferenceableNewDRef
    | T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE                          #dereferenceableExpr
    | dereferenceableBaseGrammarAddon                                           #dereferenceableBaseGrammarAddonHandler
    ;

dereferenceableBaseGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

fullyDereferenceableSuffix
    : SuffixArray=dereferenceableArrayAccessSuffix                              #dereferenceableSuffixArrayAccess
    | SuffixMember=dereferenceableMemberAccessSuffix                            #dereferenceableSuffixMemberAccess
    | SuffixStaticMember=dereferenceableStaticMemberAccessSuffix                #dereferenceableSuffixStaticMemberAccess
    | SuffixClassConst=dereferenceableClassConstantAccessSuffix                 #dereferenceableSuffixClassConstantAccess
    | ArgumentList=callArgumentList                                             #dereferenceableSuffixCallAccess
    | dereferenceableSuffixGrammarAddon                                         #dereferenceableSuffixGrammarAddonHandler
    ;

dereferenceableSuffixGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000
    ;

variable
    : fullyDereferenceable
    ;

dereferenceableMemberAccessSuffix
    : (TokenValue=T_OBJECT_OPERATOR | TokenValue=T_NULLSAFE_OBJECT_OPERATOR)
        MemberName=memberName
    ;

dereferenceableStaticMemberAccessSuffix
    : T_DOUBLE_COLON Identifier=memberInstanceName
    ;

dereferenceableClassConstantAccessSuffix
    : T_DOUBLE_COLON Identifier=memberConstantName
    ;

dereferenceableArrayAccessSuffix
    : TokenValue=T_OPEN_SQUARE_BRACE OptionalExpr=optionalExpr
        T_CLOSE_SQUARE_BRACE
    ;

newVariable
    : Variable=simpleVariable                                                   #newVariableSimple
    | NewVariable=newVariable TokenValue=T_OPEN_SQUARE_BRACE
        OptionalExpr=optionalExpr T_CLOSE_SQUARE_BRACE                          #newVariableArrayIndex
    | NewVariable=newVariable (TokenValue=T_OBJECT_OPERATOR
        | TokenValue=T_NULLSAFE_OBJECT_OPERATOR) MemberName=memberName          #newVariableProperty
    | ClassName=className T_DOUBLE_COLON Identifier=simpleVariable              #newVariableStaticProperty
    | NewVariable=newVariable T_DOUBLE_COLON Identifier=simpleVariable          #newVariableStaticProperty
    ;

memberConstantName
    : Identifier=identifier memberNameIdentifierGrammarAddon                    #memberNameIdentifier
    | T_OPEN_CURLY_BRACE Expr=expr T_CLOSE_CURLY_BRACE
        memberNameIdentifierGrammarAddon                                        #memberNameExpr
    ;

memberInstanceName
    : Identifier=simpleVariable memberNameIdentifierGrammarAddon                #memberNameSimple
    ;

memberName
    : Identifier=memberConstantName                                             #memberNameConstant
    | Identifier=memberInstanceName                                             #memberNameInstance
    ;

memberNameIdentifierGrammarAddon
    // ! to be overridden in other grammars
    : T_NO_GRAMMAR_ADDON_0000?
    ;

possibleArrayPair
    : ArrayPair=arrayPair?
    ;

arrayPairList
    : Items+=possibleArrayPair (Commas+=T_SYM_COMMA Items+=possibleArrayPair)*
    ;

arrayPair
    : KeyOrValueExpr=expr (isKey=T_DOUBLE_ARROW Value=expr)?                    #arrayPairItem
    | T_ELLIPSIS Expr=expr                                                      #arrayPairExpansion
    ;

encapsList
    : Items+=encapsVarOrWhitespace+
    ;

encapsVarOrWhitespace
    : EncapsVar=encapsVar
    | EncapsWhitespace=T_ENCAPSED_AND_WHITESPACE
    ;

encapsVar
    : Variable=T_VARIABLE T_OPEN_SQUARE_BRACE ArrayIndex=encapsVarOffset
        T_CLOSE_SQUARE_BRACE                                                    #encapsVarVariableTokenWithArrayIndex
    | Variable=T_VARIABLE (TokenValue=T_OBJECT_OPERATOR
        | TokenValue=T_NULLSAFE_OBJECT_OPERATOR) Identifier=T_STRING            #encapsVarObjectMember
    | Variable=T_VARIABLE                                                       #encapsVarVariableToken
    | T_DOLLAR_OPEN_CURLY_BRACES (Expr=expr | VarName=T_STRING_VARNAME)
        T_CLOSE_CURLY_BRACE                                                     #encapsVarDollarBraceExpr
    | T_DOLLAR_OPEN_CURLY_BRACES VarName=T_STRING_VARNAME T_OPEN_SQUARE_BRACE
        Expr=expr T_CLOSE_SQUARE_BRACE T_CLOSE_CURLY_BRACE                      #encapsVarBraceDollarExprWithArrayIndex
    | T_OPEN_CURLY_BRACE Variable=variable T_CLOSE_CURLY_BRACE                  #encapsVarBraceVariable
    ;

encapsVarOffset
    : TokenValue=T_STRING
    | IsNegative=T_SYM_MINUS? TokenValue=T_NUM_STRING
    | TokenValue=T_VARIABLE
    ;

//#endregion Dereferenceables
