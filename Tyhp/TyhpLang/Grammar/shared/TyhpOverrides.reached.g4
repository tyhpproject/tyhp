
//#endregion Tyhp Generics

//#region Tyhp Expressions

// ! OVERRIDE
phpExprUnaryPreOpsGrammarAddon
    : TokenValue=T_DECIMAL_CAST
    | TokenValue=T_TYHP_AWAIT {this.isLanguageMode("tyhp")}?
    ;
// #part

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

// ! OVERRIDE
// Async block expression: `async { ... }` evaluates to Promise<T>, not a
// callable. Distinct from `async function () { }` / `async fn() =>` (those are
// inlineFunction / phpExprInlineFunctionShort, which are tried before this
// phpExprBase alternative).
phpExprPrecBaseGrammarAddon
    : T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
        T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
    ;
// #part

// ! OVERRIDE
reservedNonModifiersGrammarAddon
    : tyhpReservedNonModifiers {this.isLanguageMode("tyhp")}?
    ;
// #part

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
    | T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
    | T_TYHP_INTERNAL {this.isLanguageMode("tyhp")}?
    | T_TYHP_OPERATOR {this.isLanguageMode("tyhp")}?
    ;

// Required suffix on PHP `typeWithoutStatic` / `className` / member names.
// Empty `{!tyhp}?` keeps ordinary PHP types like `string $name` and
// `extends \ArrayObject` parseable. Built-in `callable` does **not** take
// these generic arguments (`callable<…>` is not a type); see `callableType`.
// ! OVERRIDE
typeNameGrammarAddon
    : {this.isLanguageMode("tyhp")}? GenericArguments=tyhpGenericTypeArguments?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
classNameIdentifierGrammarAddon
    : {this.isLanguageMode("tyhp")}? GenericArguments=tyhpGenericTypeArguments?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
memberNameIdentifierGrammarAddon
    : {this.isLanguageMode("tyhp")}? GenericArguments=tyhpGenericTypeArguments?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
optionalTypeWithoutStatic
    : TypeExpr=typeExprWithoutStatic?
    ;

//#endregion Tyhp Identifiers

//#region Tyhp Dereferencables

// ! OVERRIDE
// `new struct { … }` / `new struct() { … }` must win over `new struct` /
// `new struct()` class construction now that `struct` is T_STRING.
newDereferenceable
    : {!this.looksLikeAnonymousStruct()}?
        T_NEW Identifier=classNameReference Arguments=argumentList              #newClassInstance
    | T_NEW Attributes=attributes? AnonClassDecl=anonymousClass                 #newAnonClassInstance
    | newDereferenceableGrammarAddon                                            #newDereferenceableGrammarAddonHandler
    ;

// ! OVERRIDE
newDereferenceableGrammarAddon
    : {this.looksLikeAnonymousStruct()}?
        T_NEW AnonStructDecl=tyhpAnonymousStruct {this.isLanguageMode("tyhp")}? #tyhpNewAnonStructInstance
    ;

// ! OVERRIDE
// `new X<T>(args)` is ambiguous with the comparison chain
// `(new X) < T > (args)`, because classNameReference consumes the generic
// argument list here just as it does in newDereferenceable. The lookahead rules
// this alternative out whenever an argument list follows, so `new X<T>(args)`
// can only parse as a generic instantiation. `looksLikeAnonymousStruct` keeps
// `new struct { … }` from being consumed as argument-less `new struct`.
newNonDereferenceable
    : {!this.newIsFollowedByArgumentList()}? {!this.looksLikeAnonymousStruct()}?
        T_NEW Identifier=classNameReference                                     #newClassInstanceNonDereferenceable
    | newNonDereferenceableGrammarAddon                                         #newNonDereferenceableGrammarAddonHandler
    ;
// #part

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

// ! OVERRIDE
// `declare(php=…) { extension … }` and `declare(php=…) { type … }` parse as
// innerStatementList. Same optional attributes and `internal` as the top-level
// type-alias alternative. Distinct label so it does not collide with
// `#tyhpTypeAliasDecl`.
innerStatementGrammarAddon
    : Attributes=attributes? Statement=tyhpExtensionDeclarationStatement
        {this.isLanguageMode("tyhp")}?                                          #tyhpInnerExtensionDecl
    | Attributes=attributes? IsInternal=T_TYHP_INTERNAL? Statement=tyhpTypeAlias
        {this.isLanguageMode("tyhp")}?                                          #tyhpInnerTypeAliasDecl
    | Attributes=attributes? IsFallback=T_TYHP_FALLBACK
        Statement=attributedStatement {this.isLanguageMode("tyhp")}?            #tyhpInnerFallbackDecl
    ;

//#endregion Tyhp Top Statements

//#region Tyhp Statements

// ! OVERRIDE
// `Type<Arg> $var = ...` is ambiguous with the comparison chain
// `(Type < Arg) > $var = ...`. Statement prediction prefers phpTopExpr over the
// typed-local addon, so gate top-expr when lookahead matches a generic typed
// local (see looksLikeGenericTypedLocal).
phpTopExpr
locals [ isTopExpr:bool = true ]
    : {!this.isLanguageMode("tyhp") || !this.looksLikeGenericTypedLocal()}?
        phpExprPrec
    ;

// ! OVERRIDE
statementRequiringTerminalGrammarAddon
    : Statement=tyhpTypedVarExpr {this.isLanguageMode("tyhp")}?                 #tyhpStatementTypedVarExpr
    ;
// #part

// ! OVERRIDE
// Route the for-loop init clause through a Tyhp-aware list so typed-local
// declarations (e.g. `for (int $i = 0; ...)`) are accepted. Test/update keep
// the base expression-only form. PHP mode falls through to plain expressions
// because the typed-var alternative is gated by the language-mode predicate.
// The same generic-typed-local / comparison ambiguity as phpTopExpr is gated
// here so `for (Box<int> $i = ...; ...)` is not parsed as a comparison.
forSyntax
    : T_FOR T_OPEN_ROUND_BRACE InitExpr=tyhpForInitExprs? T_SYM_SEMICOLON
        TestExpr=forCondExprs T_SYM_SEMICOLON UpdateExpr=forExprs
        T_CLOSE_ROUND_BRACE
    ;
// #part

// ! OVERRIDE
// Tyhp `foreach ($xs as T $v)` / `foreach ($xs as K $k => V $v)` — types on
// the key, the value, or both. PHP has no typed foreach binding; the typed
// alternative is language-mode gated. Untyped `$var` / `&$var` / `list()`
// stay on the PHP alternatives. `simpleVariable` (not `variable`) so a type
// cannot attach to `[]` destructure.
foreachVariable
    : {this.isLanguageMode("tyhp")}? TypeExpr=typeExprWithoutStatic
        IsRef=ampersand? TypedVariable=simpleVariable
    | IsRef=ampersand? Variable=variable
    | T_LIST T_OPEN_ROUND_BRACE ArrayPairList=arrayPairList T_CLOSE_ROUND_BRACE
    ;

// ! OVERRIDE
// File-level `const int X = 1;` parses in Tyhp so the checker can reject it
// with a dedicated diagnostic (PHP has no typed file-level constants).
// `looksLikeTypedConstDecl` keeps `const FOO = 1;` on the untyped alternative.
// Do not repeat `locals` / `@after` — PhpParser already attaches
// `_findDocComment`.
constDecl
    : {this.isLanguageMode("tyhp") && this.looksLikeTypedConstDecl()}?
        TypeExpr=typeExprWithoutStatic Identifier=T_STRING T_SYM_EQUAL
        ValueExpr=expr
    | Identifier=T_STRING T_SYM_EQUAL ValueExpr=expr
    ;

//#endregion Tyhp Statements

//#region Tyhp Using Block

// ! OVERRIDE
statementWithoutTerminalGrammarAddon
    : Statement=tyhpUsingBlock {this.isLanguageMode("tyhp")}?                   #tyhpStatementUsingBlock
    ;
// #part

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

// Optional Tyhp extras on PHP productions. The empty `{!tyhp}?` alternative is
// required so `phpSrcFile` can parse ordinary PHP `function`/`class`/calls:
// a trailing `{isLanguageMode("tyhp")}?` on an otherwise-empty match fails in
// PHP mode and makes `T_FUNCTION` / `T_EXTENDS` look unexpected.
// `internal` and `async` are independent modifiers (Story 25 design principle:
// `internal` combines with non-visibility modifiers) so either order is legal —
// `internal async function` and `async internal function` both parse.
// ! OVERRIDE
functionModifiersGrammarAddon
    : {this.isLanguageMode("tyhp")}?
        ( IsInternal=T_TYHP_INTERNAL IsAsync=T_TYHP_ASYNC?
        | IsAsync=T_TYHP_ASYNC IsInternal=T_TYHP_INTERNAL?
        )?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
functionNameGrammarAddon
    : {this.isLanguageMode("tyhp")}?
        GenericParameters=tyhpGenericParameterDeclarations?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
functionCallGrammarAddon
    : {this.isLanguageMode("tyhp")}? GenericArguments=tyhpGenericTypeArguments?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
// Optional leading `extends` on productions that still reference this addon
// (free functions, class and trait methods, class operators, class-body
// tyhpdef `extension fn` / `extension operator`). Extension-block members do
// not use it. Their target is the header `extends Type` or a nested
// `extends Type { }` group. A leading `extends Type $this` on those members
// is `tyhpExtensionCallableParameters`.
functionParametersGrammarAddon
    : {this.isLanguageMode("tyhp")}? IsExtension=T_EXTENDS?
    | {!this.isLanguageMode("tyhp")}?
    ;

//#endregion Tyhp Functions

//#region Tyhp Objects

// ! OVERRIDE
classNameGrammarAddon
    : {this.isLanguageMode("tyhp")}?
        GenericArguments=tyhpGenericParameterDeclarations?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
traitNameGrammarAddon
    : {this.isLanguageMode("tyhp")}?
        GenericArguments=tyhpGenericParameterDeclarations?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
interfaceNameGrammarAddon
    : {this.isLanguageMode("tyhp")}?
        GenericArguments=tyhpGenericParameterDeclarations?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
enumNameGrammarAddon
    : {this.isLanguageMode("tyhp")}?
        GenericArguments=tyhpGenericParameterDeclarations?
    | {!this.isLanguageMode("tyhp")}?
    ;
// #part

// ! OVERRIDE
attributedClassStatementGrammarAddon
    : Modifiers=methodModifiers tyhpClassMethodDefinition
        {this.isLanguageMode("tyhp")}?
    ;
// #part

// ! OVERRIDE
classStatementGrammarAddon
    : Attributes=attributes? Modifier=nonEmptyMemberModifiers?
        TypeAlias=tyhpTypeAlias {this.isLanguageMode("tyhp")}?                  #tyhpClassTypeAlias
    | OperatorOverload=tyhpClassOperatorOverload {this.isLanguageMode("tyhp")}? #tyhpClassOperatorOverloadDecl
    | Modifiers=nonEmptyMemberModifiers EnumCase=enumCase
        {this.isLanguageMode("tyhp")}?                                          #tyhpModifiedEnumCase
    ;
// #part

// ! OVERRIDE
traitAliasGrammarAddon
    : AliasOf=traitPropertyReference T_AS AliasString=T_VARIABLE
        {this.isLanguageMode("tyhp")}?                                          #tyhpTraitAliasPropertyRename
    | AliasOf=traitMethodReference HideIdent=T_STRING
        {this.isLanguageMode("tyhp") && this.isHideKeyword($HideIdent)}?        #tyhpTraitAliasHide
    ;

// ! OVERRIDE
// Tyhp/tyhpdef `operator *<string>` (and unqualified `operator *`) inside
// `use extension { … }` adaptations (`hide` / `insteadof`). PHP
// `traitMethodReference` / `absoluteTraitMethodReference` stay unchanged;
// only these addon stubs grow new alternatives.
traitMethodReferenceGrammarAddon
    : {this.isLanguageMode("tyhp")}? T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp
        (T_SYM_LT TargetType=typeExprWithoutStatic T_SYM_GT)?                   #tyhpTraitOperatorMethodReference
    ;

// ! OVERRIDE
absoluteTraitMethodReferenceGrammarAddon
    : {this.isLanguageMode("tyhp")}? ClassName=className T_DOUBLE_COLON
        T_TYHP_OPERATOR Op=tyhpClassOperatorOverloadOp
        (T_SYM_LT TargetType=typeExprWithoutStatic T_SYM_GT)?
                                                                                #tyhpAbsoluteTraitOperatorMethodReference
    ;

// ! OVERRIDE
traitAliasNameGrammarAddon
    : {this.isLanguageMode("tyhp")}? GenericArguments=tyhpGenericTypeArguments?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
traitMethodIdentifierGrammarAddon
    : (GenericIdentifier=tyhpGenericIdentifier {this.isLanguageMode("tyhp")}?)?
    ;

// ! OVERRIDE
memberModifierGrammarAddon
    : TokenValue=T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
    | TokenValue=T_TYHP_INTERNAL {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
classModifierGrammarAddon
    : TokenValue=T_TYHP_INTERNAL {this.isLanguageMode("tyhp")}?
    | TokenValue=T_PUBLIC {this.isLanguageMode("tyhp")}?
    | TokenValue=T_PROTECTED {this.isLanguageMode("tyhp")}?
    | TokenValue=T_PRIVATE {this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
traitModifiersGrammarAddon
    : {this.isLanguageMode("tyhp")}? IsInternal=T_TYHP_INTERNAL?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
interfaceModifiersGrammarAddon
    : {this.isLanguageMode("tyhp")}? IsInternal=T_TYHP_INTERNAL?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
enumModifiersGrammarAddon
    : {this.isLanguageMode("tyhp")}? IsInternal=T_TYHP_INTERNAL?
    | {!this.isLanguageMode("tyhp")}?
    ;

// ! OVERRIDE
parameterTypeExpressionGrammarAddon
    : optionalTypeWithoutStatic
    ;

//#endregion Tyhp Objects

//#region Tyhp Types

// Callable shapes are a typeExpr (not a type atom) so
// `callable(int $x): int | null` is return `int|null`, and a function type that
// is an arm of `|` / `&` must be grouped: `(callable(int $x): int) | null`.
// `T_CALLABLE` `(` is the shape; bare `callable` stays typeWithoutStatic.
// `?` prefixes one `type` (including a grouped `(typeExpr)`) or a callable
// shape. `?(string|int)` and `?(A&B)` are that prefix on a grouped union or
// intersection. The visitor records them as that union or intersection with
// nullability set, one nullable compound type.
// ! OVERRIDE
typeExpr
    : {this.isLanguageMode("tyhp")}? IsNullable=T_SYM_QUESTION?
        CallableType=callableType
    | IsNullable=T_SYM_QUESTION? BaseType=type
    | UnionType=unionType
    | IntersectionType=intersectionType
    | typeExprGrammarAddon
    ;

// ! OVERRIDE
typeExprWithoutStatic
    : {this.isLanguageMode("tyhp")}? IsNullable=T_SYM_QUESTION?
        CallableType=callableType
    | IsNullable=T_SYM_QUESTION? BaseType=typeWithoutStatic
    | UnionType=unionTypeWithoutStatic
    | IntersectionType=intersectionTypeWithoutStatic
    | typeExprWithoutStaticGrammarAddon
    ;

// Tyhp: `callable` does not take `<>` arguments. `callable (` is a shape
// (`callableType`), not this alternative.
// `struct {` / `struct extends` is `tyhpStructShape` (addon), not
// Identifier=name. SLL otherwise takes the name alternative and leaves `{`
// unexpected.
// ! OVERRIDE
typeWithoutStatic
    : {!this.looksLikeStructShape()}?
        (ArrayType=T_ARRAY | Identifier=name) typeNameGrammarAddon
    | {this.isLanguageMode("tyhp")}? {!this.callableIsFollowedByOpenParen()}?
        CallableType=T_CALLABLE
    | {!this.isLanguageMode("tyhp")}? CallableType=T_CALLABLE 
        typeNameGrammarAddon
    | typeWithoutStaticGrammarAddon
    ;

// In Tyhp, `( typeExpr )` is a type atom (`groupedType`), including
// `(A & B)` and `(callable(…): R)`. PHP keeps the DNF-only parenthesized
// intersection alternative.
// ! OVERRIDE
unionTypeElement
    : BaseType=type
    | {!this.isLanguageMode("tyhp")}? T_OPEN_ROUND_BRACE
        IntersectionType=intersectionType T_CLOSE_ROUND_BRACE
    ;

// ! OVERRIDE
unionTypeWithoutStaticElement
    : BaseType=typeWithoutStatic
    | {!this.isLanguageMode("tyhp")}? T_OPEN_ROUND_BRACE
        IntersectionType=intersectionTypeWithoutStatic T_CLOSE_ROUND_BRACE
    ;
// #part

// ! OVERRIDE
typeWithoutStaticGrammarAddon
    : ScalarType=tyhpScalarType {this.isLanguageMode("tyhp")}?
    | {this.isLanguageMode("tyhp")}? {this.looksLikeStructShape()}?
        StructShape=tyhpStructShape
    | {this.isLanguageMode("tyhp")}? GroupedType=groupedType
    ;
// #part

//#endregion Tyhp Types

//#region Tyhp Return Types

// ! OVERRIDE
// Guard subject is `$param` or one index read `$array[$key]` / `$array[0]` /
// `$array['k']`. Full `expr` is rejected here so `is`/`instanceof` stay the
// guard operator rather than part of the subject.
returnTypeGrammarAddon
    : T_SYM_COLON GuardVariable=T_VARIABLE
        (T_OPEN_SQUARE_BRACE
            (GuardIndexVariable=T_VARIABLE
            | GuardIndexInt=T_LNUMBER
            | GuardIndexHex=T_HNUMBER
            | GuardIndexOct=T_ONUMBER
            | GuardIndexBin=T_BNUMBER
            | GuardIndexString=T_CONSTANT_ENCAPSED_STRING)
         T_CLOSE_SQUARE_BRACE)?
        (T_INSTANCEOF|T_TYHP_IS)
        TypeExpr=typeExpr {this.isLanguageMode("tyhp")}?                        #tyhpReturnTypeGuard
    ;

//#endregion Tyhp Return Types


//#region Tyhp Internal Functions

// ! OVERRIDE
internalFunctionsGrammarAddon
    : T_TYHP_VARIABLE_EXISTS T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionVariableExists
    | T_TYHP_TYPEOF T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionTypeof
    | T_TYHP_TYPEOF BuiltinCast=(T_DOUBLE_CAST|T_OBJECT_CAST|T_INT_CAST|
        T_STRING_CAST|T_BOOL_CAST|T_ARRAY_CAST|T_DECIMAL_CAST)
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionTypeofBuiltinCast
    | T_DEFAULT T_OPEN_ROUND_BRACE TypeExpr=typeExpr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionDefault
    | T_DEFAULT BuiltinCast=(T_DOUBLE_CAST|T_OBJECT_CAST|T_INT_CAST|
        T_STRING_CAST|T_BOOL_CAST|T_ARRAY_CAST|T_DECIMAL_CAST)
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionDefaultBuiltinCast
    | T_TYHP_NAMEOF T_OPEN_ROUND_BRACE Expr=expr T_CLOSE_ROUND_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalFunctionNameof
    ;

//#endregion Tyhp Internal Functions