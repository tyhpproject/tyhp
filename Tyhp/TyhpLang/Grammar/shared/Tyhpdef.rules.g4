tyhpdefSrcFile
    : TyhpdefBlock=tyhpdefBlock EOF                                             #tyhpdefFile
    ;

// #part
tyhpdefTaglessSrcFile
locals [_languageMode:string = ""]
    : {
        this._languageMode = "tyhp";
        _localctx._languageMode = this._languageMode;
      }
        T_TYHPDEF_OPEN_TAG?
        StatementList=tyhpdefTopStatementList
        EOF                                                                     #tyhpdefTaglessFile
    ;

// #part
tyhpdefBlock
locals [_languageMode:string = ""]
    : T_TYHPDEF_OPEN_TAG
        {
            this._languageMode = "tyhp";
            _localctx._languageMode = this._languageMode;
        }
        StatementList=tyhpdefTopStatementList
    ;

// #part
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
    | T_GLOBAL T_USE T_TYHP_EXTENSION UseDecl=useDeclarations
        Adaptations=traitAdaptations                                            #tyhpdefGlobalImportExtension
    | T_GLOBAL T_USE UseDecl=mixedGroupUseDeclaration T_SYM_SEMICOLON           #tyhpdefGlobalImportGroupDecls
    | T_GLOBAL T_USE UseType=useType UseDecl=groupUseDeclaration T_SYM_SEMICOLON#tyhpdefGlobalImportTypedGroupDecls
    | T_GLOBAL T_USE UseDecl=useDeclarations T_SYM_SEMICOLON                    #tyhpdefGlobalImportDecls
    | T_GLOBAL T_USE UseType=useType UseDecl=useDeclarations T_SYM_SEMICOLON    #tyhpdefGlobalImportType
    | Attributes=attributes? Statement=tyhpTypeAlias                            #tyhpdefTypeAliasDecl
    | Attributes=attributes?
        Statement=tyhpdefStandaloneExtensionDeclarationStatement                #tyhpdefStandaloneExtensionTopDecl
    ;

//#endregion Tyhpdef Top Statements

//#region Tyhpdef Statements

tyhpdefDeprecatedOrObsolete
    : TokenValue=(T_TYHPDEF_DEPRECATED|T_TYHPDEF_OBSOLETE|T_TYHPDEF_OMIT)
    ;

tyhpdefStatement
    : T_SYM_SEMICOLON                                                           #tyhpdefEmptyStatement
    | T_DECLARE T_OPEN_ROUND_BRACE DeclareList=constList T_CLOSE_ROUND_BRACE
        Body=tyhpdefDeclareBody                                                 #tyhpdefDeclare
    | Statement=tyhpdefImportConstStatement                                     #tyhpdefImportConst
    | Statement=tyhpdefImportVariableStatement                                  #tyhpdefImportVariable
    ;

// File-level `declare(php="…");`, block `declare(php="…") { … }`, and colon
// `declare(php="…"): … enddeclare;` — body is tyhpdef declarations, not PHP
// innerStatementList (functions, classes, extension, nested declare).
tyhpdefDeclareBody
    : T_SYM_SEMICOLON                                                           #tyhpdefDeclareEmpty
    | T_OPEN_CURLY_BRACE StatementList=tyhpdefTopStatementList
        T_CLOSE_CURLY_BRACE                                                     #tyhpdefDeclareBlock
    | T_SYM_COLON StatementList=tyhpdefTopStatementList T_ENDDECLARE
        T_SYM_SEMICOLON                                                         #tyhpdefDeclareColon
    ;

tyhpdefImportConstStatement
    : tyhpdefDeprecatedOrObsolete? IsFallback=T_TYHP_FALLBACK? T_CONST TypeExpr=typeExprWithoutStatic
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
    : Statement=tyhpdefExternClassDeclarationStatement                          #tyhpdefExternClassDecl
    | Statement=tyhpdefExternInterfaceDeclarationStatement                      #tyhpdefExternInterfaceDecl
    | Statement=tyhpdefExternEnumDeclarationStatement                           #tyhpdefExternEnumDecl
    | Statement=tyhpdefExternFunctionDeclarationStatement                       #tyhpdefExternFunctionDecl
    | Statement=tyhpdefExternConstDeclarationStatement                          #tyhpdefExternConstDecl
    | Statement=tyhpdefExternDeclarationStatement                               #tyhpdefExternDecl
    | Statement=tyhpdefPartialFunctionDeclarationStatement                      #tyhpdefPartialFunctionDecl
    | Statement=tyhpdefImportFunctionDeclarationStatement                       #tyhpdefImportFunctionDecl
    | Statement=tyhpdefImportClassDeclarationStatement                          #tyhpdefImportClassDecl
    | Statement=tyhpdefImportTraitDeclarationStatement                          #tyhpdefImportTraitDecl
    | Statement=tyhpdefImportInterfaceDeclarationStatement                      #tyhpdefImportInterfaceDecl
    | Statement=tyhpdefImportEnumDeclarationStatement                           #tyhpdefImportEnumDecl
    ;

// Name-only tyhpdef placeholders: `extern class \Foo\Bar;` (semicolon, no body,
// no members, no generics, no `extends` / `implements`, no `abstract` / `final`
// / `partial` / `deprecated` / `obsolete` / `omit`, no `as` alias). Identifier
// is `name` so FQCN and qualified names parse;
// `tyhpdefClassNameWithOptionalAlias` is intentionally not reused (that rule
// allows `as` and generics). Bare `extern \Foo\Bar;` is kind-unspecified
// (class, interface, or enum — decided when a real declaration replaces it).
// `extern function \bcadd;` / `extern const \GMP_ROUND_PLUSINF;` occupy the
// function / const PHP name spaces (not the type table). Function/const
// alternatives sit before the bare form so `function` / `const` are not eaten
// as a `name`. No signature, body, generics, `as` alias, or
// `tyhpdefDeprecatedOrObsolete` / `partial` on the same declaration.
// The brace and extra-header alternatives exist only so illegal
// `extern class Foo { … }` / `extends` / `implements` / `as` / generics report
// a syntax error without ANTLR recovering into
// `tyhpdefImportClassDeclarationStatement` (single-token deletion of `extern`).
tyhpdefExternClassDeclarationStatement
    : T_TYHPDEF_EXTERN T_CLASS Identifier=name T_SYM_SEMICOLON
    | T_TYHPDEF_EXTERN T_CLASS Identifier=name T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
        {this.reportExternClassCannotHaveBody();}
    | T_TYHPDEF_EXTERN T_CLASS Identifier=name
        {this.reportExternClassCannotHaveBody();}
        tyhpdefExternClassIllegalHeader T_SYM_SEMICOLON
    ;

// Tokens after the name that are not `;` or `{` — heritage, `as`, generics,
// etc.
tyhpdefExternClassIllegalHeader
    : ~(T_SYM_SEMICOLON | T_OPEN_CURLY_BRACE)+
    ;

tyhpdefExternInterfaceDeclarationStatement
    : T_TYHPDEF_EXTERN T_INTERFACE Identifier=name T_SYM_SEMICOLON
    ;

tyhpdefExternEnumDeclarationStatement
    : T_TYHPDEF_EXTERN T_ENUM Identifier=name T_SYM_SEMICOLON
    ;

tyhpdefExternFunctionDeclarationStatement
    : T_TYHPDEF_EXTERN function Identifier=name T_SYM_SEMICOLON
    ;

tyhpdefExternConstDeclarationStatement
    : T_TYHPDEF_EXTERN T_CONST Identifier=name T_SYM_SEMICOLON
    ;

tyhpdefExternDeclarationStatement
    : T_TYHPDEF_EXTERN Identifier=name T_SYM_SEMICOLON
    ;

// Name-only overlay merge: `partial function array_count_values;`
// Optional `as` alias: `partial function call_user_func as
// call_user_func_unsafe;` No parameter list, return type, generics, `async`, or
// `omit`. A signature after `partial function` is a parse error, not a full
// replace.
tyhpdefPartialFunctionDeclarationStatement
    : T_TYHPDEF_PARTIAL function Identifier=tyhpdefPartialFunctionName
        (T_AS AliasedAs=T_STRING)? T_SYM_SEMICOLON
    ;

tyhpdefPartialFunctionName
    : Identifier=name
    | SemiReserved=semiReserved
    ;

tyhpdefImportFunctionDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? IsFallback=T_TYHP_FALLBACK? IsAsync=T_TYHP_ASYNC? function
        ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
        FindDocComment=T_OPEN_ROUND_BRACE IsExtension=T_EXTENDS?
        ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
        T_SYM_SEMICOLON
    ;

// Brace form: optional `partial`; member merge when `partial`. Written header
// clauses on the brace form are ignored (member merge only).
// Semicolon form: `partial` required. Overlay header merge: written generics /
// extends / implements / `as` / attributes replace those clauses; omitted
// clauses are kept. `partial class Foo;` with no clauses and no matching
// keep-for-alias is a warning (binder).
tyhpdefImportClassDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? Modifiers=classModifiers?
        IsPartial=T_TYHPDEF_PARTIAL? T_CLASS
        Identifier=tyhpdefClassNameWithOptionalAlias Extends=extendsFrom
        Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    | tyhpdefDeprecatedOrObsolete? Modifiers=classModifiers?
        IsPartial=T_TYHPDEF_PARTIAL T_CLASS
        Identifier=tyhpdefClassNameWithOptionalAlias Extends=extendsFrom
        Implements=implementsList FindDocComment=T_SYM_SEMICOLON
    ;

tyhpdefImportTraitDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? IsPartial=T_TYHPDEF_PARTIAL? T_TRAIT
        Identifier=tyhpdefIdentifierWithOptionalAlias
         FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    | tyhpdefDeprecatedOrObsolete? IsPartial=T_TYHPDEF_PARTIAL T_TRAIT
        Identifier=tyhpdefIdentifierWithOptionalAlias
        FindDocComment=T_SYM_SEMICOLON
    ;

tyhpdefImportInterfaceDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? IsPartial=T_TYHPDEF_PARTIAL? T_INTERFACE
        Identifier=tyhpdefIdentifierWithOptionalAlias
        Extends=interfaceExtendsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    | tyhpdefDeprecatedOrObsolete? IsPartial=T_TYHPDEF_PARTIAL T_INTERFACE
        Identifier=tyhpdefIdentifierWithOptionalAlias
        Extends=interfaceExtendsList FindDocComment=T_SYM_SEMICOLON
    ;

tyhpdefImportEnumDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? IsPartial=T_TYHPDEF_PARTIAL? T_ENUM
        Identifier=tyhpdefIdentifierWithOptionalAlias 
        BackingType=enumBackingType Implements=implementsList
        FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefClassStatementList T_CLOSE_CURLY_BRACE
    | tyhpdefDeprecatedOrObsolete? IsPartial=T_TYHPDEF_PARTIAL T_ENUM
        Identifier=tyhpdefIdentifierWithOptionalAlias
        BackingType=enumBackingType Implements=implementsList
        FindDocComment=T_SYM_SEMICOLON
    ;

//#endregion Tyhpdef Attributed Statements

//#region Tyhpdef Standalone Extension

// Standalone tyhpdef `extension Name { ... }` uses the same block target as
// live Tyhp. Optional generic parameters on the name, then an optional header
// `extends typeExprWithoutStatic`. The first `extends` inside
// `tyhpGenericParameterDeclarations` is a constraint (`T extends int|string`);
// the header `extends` after that list is the target.
// A body may also contain nested `extends Type { }` / `extends<T> Type { }`
// groups (one level). Header-versus-groups is a checker rule; both shapes
// parse. Short members use `fn` only (not `function`) so they match live Tyhp
// `fn … =>` / PHP arrow syntax. Every member uses the short `=>` form only
// (no brace bodies) and is implicitly inline. `$this` is implied. An optional
// leading `&$this` is a by-ref receiver annotation, not a parameter.
// `tyhpExtensionCallableParameters` / `tyhpExtensionOperatorLegacyTarget`
// recover the old per-member target spellings so the visitor can report
// TYHP4361 instead of an unexpected-token error.
// Optional `attributes?` on members is for `#[\Tyhp\Optimize\Pure]`
// (not `#[Inline]` — that is TYHP4176). Grammar allows an empty member list;
// rejecting `extension Name { }` is a checker concern, not a parse error.
tyhpdefStandaloneExtensionDeclarationStatement
    : tyhpdefDeprecatedOrObsolete? T_TYHP_EXTENSION Identifier=T_STRING
        GenericParameters=tyhpGenericParameterDeclarations?
        (T_EXTENDS TargetType=typeExprWithoutStatic)?
        FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefStandaloneExtensionMemberList
        T_CLOSE_CURLY_BRACE
    ;

tyhpdefStandaloneExtensionMemberList
    : Items+=tyhpdefStandaloneExtensionMember*
    ;

tyhpdefStandaloneExtensionMember
    : FunctionDecl=tyhpdefStandaloneExtensionFunction                           #tyhpdefStandaloneExtensionFunctionMember
    | OperatorDecl=tyhpdefStandaloneExtensionOperator                           #tyhpdefStandaloneExtensionOperatorMember
    | TargetGroup=tyhpdefStandaloneExtensionTargetGroup                         #tyhpdefStandaloneExtensionTargetGroupMember
    ;

// Group bodies are members only. A nested `extends` group here is a parse
// error.
tyhpdefStandaloneExtensionGroupMember
    : FunctionDecl=tyhpdefStandaloneExtensionFunction                           #tyhpdefStandaloneExtensionGroupFunctionMember
    | OperatorDecl=tyhpdefStandaloneExtensionOperator                           #tyhpdefStandaloneExtensionGroupOperatorMember
    ;

tyhpdefStandaloneExtensionGroupMemberList
    : Items+=tyhpdefStandaloneExtensionGroupMember*
    ;

tyhpdefStandaloneExtensionTargetGroup
    : T_EXTENDS GenericParameters=tyhpGenericParameterDeclarations?
        TargetType=typeExprWithoutStatic
        FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=tyhpdefStandaloneExtensionGroupMemberList
        T_CLOSE_CURLY_BRACE
    ;

tyhpdefStandaloneExtensionFunction
    : Attributes=attributes? tyhpdefDeprecatedOrObsolete? fn
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE
        Parameters=tyhpExtensionCallableParameters
        T_CLOSE_ROUND_BRACE ReturnType=returnType T_DOUBLE_ARROW
        Expr=expr T_SYM_SEMICOLON
    ;

tyhpdefStandaloneExtensionOperator
    : Attributes=attributes? tyhpdefDeprecatedOrObsolete? T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp
        LegacyTarget=tyhpExtensionOperatorLegacyTarget?
        T_OPEN_ROUND_BRACE LeftParameter=attributedParameter
        (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType T_DOUBLE_ARROW Expr=expr
        T_SYM_SEMICOLON
    ;

//#endregion Tyhpdef Standalone Extension

//#region Tyhpdef Object Members

tyhpdefClassStatementList
    : Items+=tyhpdefClassStatement*
    ;

tyhpdefClassStatement
    : Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        Modifiers=propertyModifiers TypeExpr=typeExprWithoutStatic
        PropertyList=tyhpdefPropertyList T_SYM_SEMICOLON                        #tyhpdefClassProperty
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        Modifiers=propertyModifiers TypeExpr=typeExprWithoutStatic
        PropertyAccessors=tyhpdefHookedProperty                                 #tyhpdefClassPropertyAccessors
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        Modifiers=methodModifiers T_CONST TypeExpr=typeExprWithoutStatic
        ConstList=tyhpdefImportClassConstList T_SYM_SEMICOLON                   #tyhpdefImportClassConst
    | Attributes=attributes? T_TYHPDEF_PARTIAL function
        Identifier=tyhpdefPartialFunctionName (T_AS AliasedAs=T_STRING)?
        T_SYM_SEMICOLON                                                         #tyhpdefPartialClassMethod
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC?
        Modifiers=methodModifiers function ReturnsRef=returnsRef
        Identifier=tyhpdefFunctionNameWithOptionalAlias
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType T_SYM_SEMICOLON               #tyhpdefImportClassMethod
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete? EnumCase=enumCase     #tyhpdefEnumCase
    | tyhpdefDeprecatedOrObsolete? T_USE TraitNameList=classNameList
        Adaptations=traitAdaptations                                            #tyhpdefTraitUse
    | tyhpdefDeprecatedOrObsolete? T_USE T_TYHP_EXTENSION
        UseDecl=useDeclarations Adaptations=traitAdaptations                    #tyhpdefClassUseExtension
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        tyhpdefExtensionFunction                                                #tyhpdefExtensionFunctionDecl
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        tyhpdefExtensionOperator                                                #tyhpdefExtensionOperatorDecl
    | tyhpdefClassOperator T_SYM_SEMICOLON                                      #tyhpdefClassOperatorDecl
    | Attributes=attributes? tyhpdefDeprecatedOrObsolete?
        Modifier=nonEmptyMemberModifiers? TypeAlias=tyhpTypeAlias               #tyhpdefClassTypeAlias
    ;

// Class-body tyhpdef extension members are thin `=>` mappings only (no brace
// `methodBody`). Standalone `extension Name { fn … => …; }` and Tyhp
// `extension { fn … => …; }` / `{ function … { … } }` are separate rules
// and keep their own forms.
tyhpdefExtensionFunction
    : T_TYHP_EXTENSION fn ReturnsRef=returnsRef
        GenericIdentifier=tyhpOptionalGenericIdentifierWithoutConstructor
        FindDocComment=T_OPEN_ROUND_BRACE ParameterList=parameterList
        T_CLOSE_ROUND_BRACE ReturnType=returnType T_DOUBLE_ARROW
        Expr=expr T_SYM_SEMICOLON                                               #tyhpdefExtensionFunctionShortDecl
    ;

tyhpdefExtensionOperator
    : T_TYHP_EXTENSION T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=attributedParameter
        (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType T_SYM_SEMICOLON                            #tyhpdefExtensionOperatorSignatureDecl
    | T_TYHP_EXTENSION T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=attributedParameter
        (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
        T_DOUBLE_ARROW Expr=expr T_SYM_SEMICOLON                                #tyhpdefExtensionOperatorFullDecl
    ;

tyhpdefClassOperator
    : T_TYHP_OPERATOR
        Op=tyhpClassOperatorOverloadOp T_OPEN_ROUND_BRACE
        functionParametersGrammarAddon LeftParameter=attributedParameter
        (T_SYM_COMMA RightParameter=attributedParameter)? T_CLOSE_ROUND_BRACE
        ConvertReturnType=returnType
    ;

// #part
tyhpdefImportClassConstDecl
    : Identifier=tyhpdefIdentifierWithOptionalAlias
        (T_COALESCE CoalesceExpr=expr)?
    ;

tyhpdefImportClassConstList
    : Items+=tyhpdefImportClassConstDecl
        (T_SYM_COMMA Items+=tyhpdefImportClassConstDecl)*
    ;

tyhpdefProperty
    : Variable=T_VARIABLE (T_COALESCE CoalesceExpr=expr)?
    ;

tyhpdefPropertyList
    : Items+=tyhpdefProperty (T_SYM_COMMA Items+=tyhpdefProperty)*
    ;

// Bodyless hooked property (single name). Distinct from PHP hookedProperty,
// which allows brace / => bodies and `= default { hooks }`. Optional `?? expr`
// is the tyhpdef expected-default form (same as unhooked tyhpdefProperty /
// consts);it sits before `{` the way PHP `= expr { hooks }` does.
tyhpdefHookedProperty
    : Variable=T_VARIABLE (T_COALESCE CoalesceExpr=expr)? T_OPEN_CURLY_BRACE
        Accessors=tyhpdefPropertyHookList T_CLOSE_CURLY_BRACE
    ;

tyhpdefPropertyHookList
    : Items+=tyhpdefPropertyHook+
    ;

tyhpdefPropertyHook
    : Attributes=attributes? Modifiers=propertyHookModifiers
        ReturnsRef=returnsRef Identifier=identifier T_SYM_SEMICOLON
    ;

//#endregion Tyhpdef Object Members

//#region Tyhpdef Identifiers

tyhpdefIdentifierWithOptionalAlias
    : Identifier=name GenericParameters=tyhpGenericParameterDeclarations?
    | SemiReserved=semiReserved
        GenericParameters=tyhpGenericParameterDeclarations?
    | AliasedIdentifier=tyhpdefIdentifierWithAlias
    ;

tyhpdefClassNameWithOptionalAlias
    : (AliasOf=className T_AS)?
        Identifier=name GenericParameters=tyhpGenericParameterDeclarations?
    ;

tyhpdefFunctionNameWithOptionalAlias
    : Identifier=name GenericArguments=tyhpGenericParameterDeclarations
        (T_AS AliasedAs=tyhpStringWithOptionalGeneric)?                         #tyhpdefFunctionNameGenericAlias
    | Identifier=name (T_AS AliasedAs=T_STRING)?                                #tyhpdefFunctionNameAlias
    | SemiReserved=semiReserved
        GenericArguments=tyhpGenericParameterDeclarations?
        (T_AS AliasedAs=T_STRING)?                                              #tyhpdefFunctionNameSemiReservedAlias
    ;

tyhpdefIdentifierWithAlias
    : Identifier=name T_AS AliasedAs=tyhpOptionalGenericIdentifier              #tyhpdefIdentifierAlias
    | ClassName=className T_DOUBLE_COLON
        Identifier=tyhpOptionalGenericIdentifier
        T_AS AliasedAs=tyhpOptionalGenericIdentifier                            #tyhpdefClassMemberIdentifierAlias
    ;
