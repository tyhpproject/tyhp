
//#endregion Tyhpdef Identifiers

//#region Tyhp Structs

// Named `struct Name { }` is not a declaration. Name a struct with
// `type Name = struct { … };` (tyhpStructShape on a type alias RHS / typeExpr).
// Expression `new struct { … }` stays on tyhpAnonymousStruct.

tyhpAnonymousStruct
    : StructKw=T_STRING {this.isStructKeyword($StructKw)}?
        (T_EXTENDS Extends=className)? (T_OPEN_ROUND_BRACE
        T_CLOSE_ROUND_BRACE)? FindDocComment=T_OPEN_CURLY_BRACE
        PropertyList=tyhpStructPropertyList T_CLOSE_CURLY_BRACE
    ;

// Type-position / alias-RHS struct shape: `struct { … }` or
// `struct extends Parent { … }`. No name, no empty `()`. `struct` is
// T_STRING (same as `object`); isStructKeyword / looksLikeStructShape
// keep `class struct { }` and `type Foo = struct;` as identifiers.
tyhpStructShape
    : StructKw=T_STRING {this.isStructKeyword($StructKw)}?
        (T_EXTENDS Extends=className)?
        FindDocComment=T_OPEN_CURLY_BRACE PropertyList=tyhpStructPropertyList
        T_CLOSE_CURLY_BRACE
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

// Block target: optional generic parameters on the name, then an optional
// header `extends typeExprWithoutStatic`. `extension Ops<T extends int|string>
// extends MyClass<T>` keeps the constraint `extends` inside the generic list;
// the second `extends` is the target. Nested groups are
// `extends type { members }` / `extends<T> type { members }`, one level only.
// A declaration may parse with both a header target and nested groups; the
// checker rejects that mix. `$this` is implied on members. Optional leading
// `&$this` is a by-ref receiver annotation (no type, not a caller parameter).
// Operators list every operand; there is no `<Type>` on the operator token.
// `use extension` adaptations still qualify with `operator +<Money>`
// (`traitMethodReferenceGrammarAddon`). Optional `attributes?` on members
// matches standalone tyhpdef members (e.g. `#[\Tyhp\Optimize\Pure]`;
// not `#[Inline]` — that is TYHP4176).
tyhpExtensionDeclarationStatement
    : IsInternal=T_TYHP_INTERNAL? T_TYHP_EXTENSION Identifier=T_STRING
        GenericParameters=tyhpGenericParameterDeclarations?
        (T_EXTENDS TargetType=typeExprWithoutStatic)?
        FindDocComment=T_OPEN_CURLY_BRACE FunctionList=tyhpExtensionFunctionList
        T_CLOSE_CURLY_BRACE
    ;

tyhpExtensionFunctionList
    : Items+=tyhpExtensionMember*
    ;

// Live extension members use method-style names
// (`tyhpOptionalGenericIdentifierWithoutConstructor` / `semiReserved`),
// not free-function `functionName` (T_STRING | T_READONLY). That lets
// `function match` / `fn and` / `fn or` parse the same way as class methods
// and tyhpdef standalone members. Do not widen `functionName` for this.
// Groups are a body item. Group bodies use `tyhpExtensionGroupMember` so an
// `extends` group cannot nest inside another group.
tyhpExtensionMember
    : Member=tyhpExtensionCallableMember                                        #tyhpExtensionCallableMemberAlt
    | Attributes=attributes? OperatorOverload=tyhpExtensionOperatorOverload     #tyhpExtensionOperatorMember
    | TargetGroup=tyhpExtensionTargetGroup                                      #tyhpExtensionTargetGroupMember
    ;

tyhpExtensionGroupMember
    : Member=tyhpExtensionCallableMember                                        #tyhpExtensionGroupCallableMember
    | Attributes=attributes? OperatorOverload=tyhpExtensionOperatorOverload     #tyhpExtensionGroupOperatorMember
    ;

tyhpExtensionGroupMemberList
    : Items+=tyhpExtensionGroupMember*
    ;

tyhpExtensionTargetGroup
    : T_EXTENDS GenericParameters=tyhpGenericParameterDeclarations?
        TargetType=typeExprWithoutStatic
        FindDocComment=T_OPEN_CURLY_BRACE
        FunctionList=tyhpExtensionGroupMemberList
        T_CLOSE_CURLY_BRACE
    ;

tyhpExtensionCallableMember
    : Attributes=attributes? functionModifiersGrammarAddon function
        ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE
        Parameters=tyhpExtensionCallableParameters
        T_CLOSE_ROUND_BRACE ReturnType=returnType
        StatementList=methodBody                                                #tyhpExtensionFunctionDecl
    | Attributes=attributes? functionModifiersGrammarAddon fn
        ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE
        Parameters=tyhpExtensionCallableParameters
        T_CLOSE_ROUND_BRACE
        OptionalReturnType=returnType T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON  #tyhpExtensionFunctionShortDecl
    ;

// Parameter prefix for extension-block callables (live Tyhp and standalone
// tyhpdef). `&$this` is only the receiver annotation. `extends Type $this`
// (optional `&`) is the old per-member receiver; the visitor reports TYHP4361.
// Anything else is an ordinary `parameterList` (`self` is just a type name).
tyhpExtensionCallableParameters
    : {this.extensionReceiverAhead()}?
        ReceiverAmp=T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG
        ReceiverVar=T_VARIABLE
        (T_SYM_COMMA RestParameters=nonEmptyParameterList possibleComma)?
    | {this.extensionLegacyReceiverAhead()}?
        LegacyExtends=T_EXTENDS LegacyTarget=typeExprWithoutStatic
        LegacyRef=T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG?
        LegacyVar=T_VARIABLE {this.isThisVariable($LegacyVar)}?
        (T_SYM_COMMA RestParameters=nonEmptyParameterList possibleComma)?
    | ParameterList=parameterList
    ;

// Old `operator op<Type>(...)` on an extension-block declaration. Not used by
// class operators or by `use extension` adaptation qualifiers.
tyhpExtensionOperatorLegacyTarget
    : T_SYM_LT TargetType=typeExprWithoutStatic T_SYM_GT
    ;

tyhpExtensionOperatorOverload
    : IsInternal=T_TYHP_INTERNAL? T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp
        LegacyTarget=tyhpExtensionOperatorLegacyTarget?
        T_OPEN_ROUND_BRACE LeftParameter=attributedParameter
        (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody |
        (T_DOUBLE_ARROW ShorthandExpr=expr T_SYM_SEMICOLON))
    ;

//#endregion Tyhp Extensions

//#region Tyhp Type Aliases

// Object shapes are alias-RHS only (`type Name = object { … };`). PHP `object`
// is T_STRING (there is no T_OBJECT token); `looksLikeObjectShape` gates the
// shape alternative so `type Foo = object;` stays a typeExpr and
// `class object { }` still lexes as T_STRING. Do not add `tyhpObjectShape` to
// typeExpr. A shape may also appear intersected with nominal types on the alias
// RHS only (nominal parents for a shape, e.g. `LoggerInterface & object { … }`
// ); that is `tyhpObjectShapeIntersection`, gated by
// `looksLikeObjectShapeIntersection` so plain nominal intersections (no shape
// item) keep parsing as `typeExpr`. Callable shapes (`callable(…): R`) and
// struct shapes (`struct { … }`) *are* typeExpr (inline in parameters, returns,
// bounds, unions).
tyhpTypeAlias
    : T_TYHP_TYPE_ALIAS Identifier=name
        GenericArguments=tyhpGenericParameterDeclarations? T_SYM_EQUAL
        ( {this.looksLikeObjectShapeIntersection()}?
            ObjectShapeIntersection=tyhpObjectShapeIntersection
        | {this.looksLikeObjectShape()}? ObjectShape=tyhpObjectShape
        | TypeExpr=typeExpr ) T_SYM_SEMICOLON
    ;

tyhpObjectShape
    : ObjectKw=T_STRING {this.isObjectKeyword($ObjectKw)}?
        T_OPEN_CURLY_BRACE StatementList=tyhpObjectShapeStatementList
        T_CLOSE_CURLY_BRACE
    ;

// Shape bodies reuse tyhpdefClassStatement (signature-only methods, properties,
// tyhpdef `const T NAME` / `const T NAME ?? expr`) and also accept PHP class
// const spelling (`const NAME = expr` / `const T NAME = expr`). The PHP `=`
// forms stay off tyhpdefClassStatement so `.tyhpdef` class bodies still reject
// `=`. Object shapes keep the PHP spelling because they appear in `.tyhp`
// (and `.tyhpdef` aliases) and the checker reads `PhpConstDeclListAst`.
tyhpObjectShapeStatementList
    : Items+=tyhpObjectShapeStatement*
    ;

tyhpObjectShapeStatement
    : Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        Modifiers=classConstModifiers T_CONST ConstList=classConstList
        T_SYM_SEMICOLON                                                         #tyhpObjectShapeClassConsts
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        Modifiers=classConstModifiers T_CONST TypeExpr=typeExprWithoutStatic
        ConstList=classConstList T_SYM_SEMICOLON                                #tyhpObjectShapeClassTypedConsts
    | Member=tyhpdefClassStatement                                              #tyhpObjectShapeTyhpdefMember
    ;

// `Items` mixes nominal types and object-shape items, e.g.
// `LoggerInterface & object { … }` or `object { … } & LoggerInterface`. How
// many of each, and in what order, is a checker concern, not a grammar one.
// Each item disambiguates the same way the alias RHS does: 
// `looksLikeObjectShape` gates the shape alternative so a nominal type falls
// through to `NominalType=type`.
tyhpObjectShapeIntersection
    : Items+=tyhpObjectShapeIntersectionItem
        (T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG
            Items+=tyhpObjectShapeIntersectionItem)+
    ;

tyhpObjectShapeIntersectionItem
    : {this.looksLikeObjectShape()}? ObjectShape=tyhpObjectShape
    | NominalType=type
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
    : Identifier=name (T_EXTENDS TypeExpr=typeExpr)?
        (T_SYM_EQUAL DefaultExpr=typeExpr)?
    ;

tyhpGenericParameterDeclarationList
    : Items+=tyhpGenericParameterDeclaration (T_SYM_COMMA
        Items+=tyhpGenericParameterDeclaration)*
    ;

tyhpGenericParameterDeclarations
    : T_SYM_LT GenericParametersList=tyhpGenericParameterDeclarationList
        T_SYM_GT
    ;

// Two `...` forms as generic type arguments (not on `callable`; callable shapes
// use `callable(…): R` / `callable(...): R` / `callable(T ...): R` instead):
// - Bare `...` is a wildcard type argument.
// - Postfix `typeExpr ...` is homogeneous `T...`. Not a pack splice.
tyhpGenericTypeArgument
    : TypeExpr=typeExpr PostfixEllipsis=T_ELLIPSIS?
    | WildcardEllipsis=T_ELLIPSIS
    ;

tyhpGenericTypeArgumentList
    : Items+=tyhpGenericTypeArgument
        (T_SYM_COMMA Items+=tyhpGenericTypeArgument)*
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
// #part

//#endregion Tyhp Expressions

//#region Tyhp Identifiers

tyhpReservedNonModifiers
    : TokenValue=T_TYHP_TYPE_ALIAS
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
// #part

tyhpSemiReserved
    : TokenValue=T_TYHP_ASYNC
    | TokenValue=T_TYHP_INTERNAL
    | TokenValue=T_TYHP_OPERATOR
    | TokenValue=T_TYHPDEF_DEPRECATED
    | TokenValue=T_TYHPDEF_OBSOLETE
    | TokenValue=T_TYHP_IS
    ;
// #part

tyhpTypedVarExpr
    : <assoc=right> TypeExpr=optionalTypeWithoutStatic Variable=simpleVariable
        (FindDocCommentCheck=T_SYM_EQUAL IsRef=ampersand? EqualsExpr=expr)?
    | <assoc=right> T_OPEN_ROUND_BRACE TypeExpr=optionalTypeWithoutStatic
        T_CLOSE_ROUND_BRACE Variable=simpleVariable
        (FindDocCommentCheck=T_SYM_EQUAL IsRef=ampersand? EqualsExpr=expr)?
    ;
// #part

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
// #part

tyhpUsingBlock
    : T_TYHP_USING IsAsync=T_TYHP_AWAIT?
      T_OPEN_ROUND_BRACE Resources=tyhpUsingResourceList T_CLOSE_ROUND_BRACE
      T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    ;

tyhpUsingResourceList
    : Items+=tyhpUsingResource (T_SYM_COMMA Items+=tyhpUsingResource)*
    ;

tyhpUsingResource
    : TypeExpr=typeExprWithoutStatic Variable=simpleVariable T_SYM_EQUAL
        Expr=expr                                                               #tyhpUsingResourceTyped
    | Variable=simpleVariable T_SYM_EQUAL Expr=expr                             #tyhpUsingResourceInferred
    | Expr=expr                                                                 #tyhpUsingResourceUnassigned
    ;
// #part

// ! OVERRIDE
tyhpCtorReturnType
    : T_SYM_COLON TokenValue=T_TYHP_VOID
    | T_SYM_COLON TokenValue=T_TYHP_PARENT ArgumentsList=argumentList
    ;
// #part

tyhpClassMethodDefinition
    // @ visitor will need to throw a parse error if it has ReturnsRef and is
    // @ the constructor method
    : function ReturnsRef=returnsRef tyhpMethodDefinition                       #tyhpClassMethod
    | fn ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE OptionalReturnType=returnType T_DOUBLE_ARROW
        Expr=expr T_SYM_SEMICOLON {this.isLanguageMode("tyhp")}?                #tyhpClassGenericMethodShort
    ;

tyhpMethodDefinition
    // @ we will throw an error during the visit if this has a return type or
    // @ generics and is not tyhp
    : Identifier=T_CONSTRUCT_METHOD
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=ctorParameterList
        T_CLOSE_ROUND_BRACE ReturnType=tyhpCtorReturnType?
        StatementList=methodBody                                                #tyhpClassCtorWithReturnType
    | GenericIdentifier=tyhpGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType StatementList=methodBody      #tyhpClassGenericMethod
    ;
// #part

// Operands reuse attributedParameter (same as parameterList) so #[…] is legal
// before each operand, matching method/function parameters. The binder already
// walks LeftParameter / RightParameter AstAttributes.
tyhpClassOperatorOverload
    : IsInternal=T_TYHP_INTERNAL? Modifier=(T_ABSTRACT | T_FINAL)?
        T_TYHP_OPERATOR Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=attributedParameter
        (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        (StatementList=methodBody | (T_DOUBLE_ARROW ShorthandExpr=expr
        T_SYM_SEMICOLON))
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
    | TokenValue=T_SYM_GT IsSR=T_SYM_GT // >> two GT tokens; must precede bare >
    | TokenValue=T_SYM_GT               // bare >
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
    | TokenValue=T_EMPTY // the `empty` word operator (lexed as the T_EMPTY)
    | TokenValue=T_STRING // for `convert` (and other word operators)

    ;
// #part

// `callable(…): R`. Return type is required. Nested callables are
// right-associative because the return is `typeExpr`. Bare `...` in the
// parameter list is unknown arity; `T ...` / `T ...$args` is a variadic
// parameter. `=` marks optional; the default `expr` is optional.
// PHP lexes `(int)` / `(string)` / … as a single cast token, so unnamed
// `callable(int): R` is the BuiltinCast alternative (same as typeof/default).
callableType
    : T_CALLABLE T_OPEN_ROUND_BRACE
        ( WildcardEllipsis=T_ELLIPSIS | Parameters=callableShapeParameterList )?
      T_CLOSE_ROUND_BRACE T_SYM_COLON ReturnType=typeExpr
    | T_CALLABLE BuiltinCast=(T_DOUBLE_CAST|T_OBJECT_CAST|T_INT_CAST|
        T_STRING_CAST|T_BOOL_CAST|T_ARRAY_CAST|T_DECIMAL_CAST|T_VOID_CAST)
      T_SYM_COLON ReturnType=typeExpr
    ;

callableShapeParameterList
    : Items+=callableShapeParameter (T_SYM_COMMA Items+=callableShapeParameter)*
        T_SYM_COMMA?
    ;

callableShapeParameter
    : TypeExpr=typeExpr
        IsRef=T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG?
        Ellipsis=T_ELLIPSIS?
        Variable=T_VARIABLE?
        (T_SYM_EQUAL (
            {this.callableShapeDefaultIsPresent()}? DefaultExpr=expr
        )?)?
    ;

groupedType
    : T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
    ;
// #part

tyhpScalarType
    : Scalar=T_LNUMBER                                                          #scalarTypeLNumber
    | T_SYM_MINUS Scalar=T_LNUMBER                                              #scalarTypeNegativeLNumber
    | T_SYM_PLUS Scalar=T_LNUMBER                                               #scalarTypePositiveLNumber
    | Scalar=T_DNUMBER                                                          #scalarTypeDNumber
    | T_SYM_MINUS Scalar=T_DNUMBER                                              #scalarTypeNegativeDNumber
    | T_SYM_PLUS Scalar=T_DNUMBER                                               #scalarTypePositiveDNumber
    | Scalar=T_ONUMBER                                                          #scalarTypeONumber
    | Scalar=T_HNUMBER                                                          #scalarTypeHNumber
    | Scalar=T_BNUMBER                                                          #scalarTypeBNumber
    | Scalar=T_CONSTANT_ENCAPSED_STRING                                         #scalarTypeSingleQuoteString
    | T_DOUBLE_QUOTE EncapsList=encapsList? T_DOUBLE_QUOTE                      #scalarTypeDoubleQuoteString
    ;
