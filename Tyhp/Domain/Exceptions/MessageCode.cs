namespace Tyhp.Domain.Exceptions
{
    /// <summary>
    /// Message codes for all compiler diagnostics (errors, warnings, info messages).
    /// Each code uniquely identifies a specific diagnostic type across all compiler phases.
    /// </summary>
    /// <remarks>
    /// <para><b>MessageCode Numbering Scheme:</b></para>
    /// <list type="table">
    /// <listheader>
    ///   <term>Range</term>
    ///   <description>Component</description>
    /// </listheader>
    /// <item>
    ///   <term>1000-1999</term>
    ///   <description>Parser/Lexer/Grammar errors (ANTLR parsing phase)</description>
    /// </item>
    /// <item>
    ///   <term>2000-2999</term>
    ///   <description>Visitor/AST generation errors (parse tree to AST conversion)</description>
    /// </item>
    /// <item>
    ///   <term>3000-3999</term>
    ///   <description>Binder errors (symbol resolution, scope management)</description>
    /// </item>
    /// <item>
    ///   <term>4000-4999</term>
    ///   <description>Checker errors (type checking, semantic analysis)</description>
    /// </item>
    /// <item>
    ///   <term>5000-5999</term>
    ///   <description>Emitter errors (code generation, PHP output)</description>
    /// </item>
    /// <item>
    ///   <term>6000-6999</term>
    ///   <description>Configuration errors (reserved for future use)</description>
    /// </item>
    /// <item>
    ///   <term>7000-7999</term>
    ///   <description>CLI action errors (subdivided per action — see CLI region below)</description>
    /// </item>
    /// <item>
    ///   <term>8000-8999</term>
    ///   <description>Tyhpdef errors (reserved for future use)</description>
    /// </item>
    /// <item>
    ///   <term>9000-9999</term>
    ///   <description>Internal compiler errors (reserved for future use)</description>
    /// </item>
    /// </list>
    /// <para><b>Adding New Codes:</b></para>
    /// <para>
    /// When adding a new MessageCode enum value:
    /// <list type="number">
    ///   <item>Choose a code number from the appropriate range above</item>
    ///   <item>Add the enum value to the appropriate region in this file</item>
    ///   <item>Add localized message strings to all .resx files in Resources/ folder:</item>
    ///   <item>  - ERROR_TYHP#### for errors (e.g., ERROR_TYHP1001)</item>
    ///   <item>  - WARNING_TYHP#### for warnings (e.g., WARNING_TYHP1001)</item>
    ///   <item>  - INFO_TYHP#### for info messages (e.g., INFO_TYHP1001)</item>
    ///   <item>Message strings support {0}, {1}, etc. placeholders for format parameters</item>
    /// </list>
    /// </para>
    /// <para><b>CLI Action Error Code Subdivision (7000–7999):</b></para>
    /// <list type="table">
    /// <listheader>
    ///   <term>Range</term>
    ///   <description>CLI Action</description>
    /// </listheader>
    /// <item><term>7000–7099</term><description>Shared CLI / generic errors</description></item>
    /// <item><term>7100–7199</term><description>build action</description></item>
    /// <item><term>7200–7299</term><description>lint action</description></item>
    /// <item><term>7300–7399</term><description>language_server action</description></item>
    /// <item><term>7400–7499</term><description>xdebug_proxy action</description></item>
    /// <item><term>7500–7599</term><description>generate_tyhpdef action</description></item>
    /// <item><term>7600–7699</term><description>init action</description></item>
    /// <item><term>7700–7799</term><description>composer action</description></item>
    /// <item><term>7800–7899</term><description>debug / integrity_check actions</description></item>
    /// <item><term>7900–7999</term><description>Reserved for future CLI actions</description></item>
    /// </list>
    /// <para><b>Reserved Ranges:</b></para>
    /// <para>
    /// Range 6000–6999 is reserved for configuration errors.
    /// Range 9000–9999 is reserved for internal compiler errors.
    /// </para>
    /// </remarks>
    public enum MessageCode {

            NoError = 0,


            #region Lexer/Parser/Grammar

            ParserUnknownError = 1001,
            ParserUnexpectedError = 1002,
            ParserCompileAborted = 1003,

            /// <summary>Closing tag <c>?&gt;</c> is not allowed when <c>source.tagless</c> is enabled.</summary>
            LexerCloseTagNotAllowedInTaglessMode = 1004,

            #endregion Lexer/Parser/Grammar

            #region Visitor / AST Tree Generation

            VisitorUnknownError = 2001,
            VisitorUnexpectedAlternative = 2002,
            VisitorMissingRequiredNode = 2003,
            VisitorUnsupportedConstruct = 2004,

            #endregion Visitor / AST Tree Generation

            #region Binder

            BinderUnknownError = 3001,
            BinderDuplicateSymbolDeclaration = 3002,
            BinderSymbolNotFound = 3003,

            // for when a symbol is added to the symbol tree in a place it should not belong
            BinderInvalidSymbolTypeForParent = 3004,

            BinderInvalidFileScopeArgument = 3005,
            BinderCircularInheritance = 3006,
            BinderTraitConflict = 3007,
            BinderDuplicateUseAlias = 3008,
            BinderInvalidSelfReference = 3009,
            BinderInvalidParentReference = 3010,
            BinderDuplicateGenericParameter = 3011,
            BinderGenericParameterShadow = 3012,
            BinderMultipleConstructors = 3013,

            /// <summary>Operator in an extension body is missing the required <c>&lt;Type&gt;</c> target.</summary>
            ExtensionOperatorMissingTarget = 3014,

            /// <summary><c>&lt;Type&gt;</c> on an operator overload is only allowed inside an extension declaration.</summary>
            ExtensionOperatorTargetNotAllowed = 3015,

            /// <summary>The <c>&lt;Type&gt;</c> target of an extension operator could not be resolved to a class.</summary>
            ExtensionOperatorTargetNotFound = 3016,

            /// <summary>An <c>extends</c> type reference could not be resolved.</summary>
            BinderUnresolvedExtendsType = 3017,

            /// <summary>An <c>implements</c> type reference could not be resolved.</summary>
            BinderUnresolvedImplementsType = 3018,

            /// <summary>A function or method return type could not be resolved.</summary>
            BinderUnresolvedReturnType = 3019,

            /// <summary>A function or method parameter type could not be resolved.</summary>
            BinderUnresolvedParameterType = 3020,

            /// <summary>A generic parameter constraint type could not be resolved.</summary>
            BinderUnresolvedGenericConstraintType = 3021,

            /// <summary>A generic parameter default type could not be resolved.</summary>
            BinderUnresolvedGenericDefaultType = 3022,

            /// <summary>
            /// An <c>extends</c> target resolved, but to the wrong declaration kind
            /// (e.g. a class extending an interface, or an interface extending a class).
            /// </summary>
            BinderInvalidExtendsTypeKind = 3023,

            /// <summary>
            /// An <c>implements</c> target resolved, but is not an interface
            /// (e.g. a class implementing another class).
            /// </summary>
            BinderInvalidImplementsTypeKind = 3024,

            #endregion Binder

            #region Checker

            CheckerUnknownError = 4001,
            CheckerMultipleVisibilities = 4002,
            CheckerNotAllowedMemberModifier = 4003,
            CheckerAccessorVisibilityCannotBeMoreVisibleThanProperty = 4004,
            CheckerMemberModifierConflict = 4005,
            CheckerInvalidPropertyAccessorType = 4006,
            CheckerParameterNotAllowedOnPropertyAccessorType = 4007,

            // Type compatibility errors
            CheckerTypeMismatch = 4008,
            CheckerIncompatibleReturnType = 4009,
            CheckerIncompatibleArgumentType = 4010,
            CheckerMissingReturnStatement = 4011,
            CheckerUnreachableCode = 4012,

            // Variable errors
            CheckerVariableUsedBeforeAssignment = 4013,
            CheckerVariablePossiblyUndefined = 4014,
            CheckerVariablePossiblyNull = 4015,
            CheckerVariableTypeRequired = 4016,

            // Class/interface validation errors
            CheckerAbstractMethodNotImplemented = 4017,
            CheckerInterfaceMethodNotImplemented = 4018,
            CheckerFinalClassExtended = 4019,
            CheckerFinalMethodOverridden = 4020,
            CheckerReadonlyPropertyReassigned = 4021,
            CheckerAbstractClassInstantiated = 4022,

            // Enum validation errors
            CheckerEnumCaseTypeMismatch = 4023,
            CheckerEnumMethodNotAllowed = 4024,

            // Visibility errors
            CheckerMemberNotAccessible = 4025,

            // Control flow errors
            CheckerBreakOutsideLoop = 4026,
            CheckerContinueOutsideLoop = 4027,
            CheckerAwaitOutsideAsync = 4028,

            // Operator errors
            CheckerInvalidOperatorForType = 4029,

            // Tyhp-specific errors
            CheckerDisposableRequiresInterface = 4030,
            CheckerWithKeywordInvalidProperty = 4031,
            CheckerTypeGuardInvalidReturn = 4032,

            // 4033 / 4034 retired: extension functions cannot carry visibility/static modifiers in the
            // Tyhp grammar (see CheckerExtensionMissingExtends below), so the old modifier checks were dead.
            CheckerGenericConstraintNotSatisfied = 4035,
            CheckerGenericArgumentCountMismatch = 4036,

            // Struct-specific errors
            CheckerStructPropertyRequired = 4037,

            /// <summary>Visibility adaptation is not allowed on extension members (extensions are always public).</summary>
            CheckerExtensionVisibilityNotAllowed = 4038,

            // Throwable constraint
            CheckerThrowNotThrowable = 4039,
            CheckerCatchNotThrowable = 4040,
            CheckerCatchNoIntersection = 4041,
            CheckerCatchNoScalar = 4042,

            // Logical condition type
            CheckerConditionNotBool = 4043,

            // Trait errors
            CheckerTraitRequirementNotMet = 4044,
            CheckerTraitRequirementImplNotMet = 4045,

            // Async iteration errors
            CheckerAsyncIterableMissingAwait = 4046,
            CheckerAwaitNonAsyncIterable = 4047,

            // Restricted type in generic position errors
            CheckerVoidInNonReturnPosition = 4048,
            CheckerNeverInNonReturnPosition = 4049,

            // Utility-type and reference errors
            CheckerUtilityTypeInvalidKey = 4050,
            CheckerUtilityTypeInvalidArgument = 4051,
            CheckerReferenceTypeChanged = 4052,

            // Composite (union/intersection) type errors
            CheckerDuplicateTypeInComposite = 4053,
            CheckerMixedInComposite = 4054,
            CheckerRedundantTypeInUnion = 4055,
            CheckerUseBoolInsteadOfTrueFalse = 4056,
            CheckerNonClassInIntersection = 4057,
            CheckerCallableNotAllowedOnProperty = 4058,
            CheckerVoidNotAllowedHere = 4059,
            CheckerVoidRefReturn = 4060,
            CheckerNeverNotAllowedHere = 4061,
            CheckerResourceNotAllowed = 4062,
            CheckerRefArgMustBeVariable = 4063,

            // Relative-type (self/parent/static) errors
            CheckerRelativeTypeOutsideClass = 4064,
            CheckerParentWithoutParent = 4065,
            CheckerStaticNotReturnType = 4066,
            CheckerDnfRedundantIntersection = 4067,

            // Instantiation / clone errors
            CheckerNeverMustNotReturn = 4068,
            CheckerCannotInstantiateNonClass = 4069,
            CheckerCannotInstantiateTrait = 4070,
            CheckerCannotInstantiateInterface = 4071,
            CheckerCannotInstantiateEnum = 4072,
            CheckerCloneNonObject = 4073,

            // Magic-method and parameter errors
            CheckerMagicMethodSignature = 4074,
            CheckerDuplicateParameter = 4075,
            CheckerRequiredAfterOptional = 4076,
            CheckerVariadicNotLast = 4077,
            CheckerVariadicWithDefault = 4078,

            // Argument errors
            CheckerDuplicateNamedArgument = 4079,
            CheckerPositionalAfterNamed = 4080,
            CheckerUnknownNamedArgument = 4081,
            CheckerNamedAfterUnpack = 4082,

            // Closure errors
            CheckerClosureUseUndefined = 4083,
            CheckerClosureUseThis = 4084,
            CheckerStaticClosureThis = 4085,

            // Generator / yield errors
            CheckerYieldOutsideGenerator = 4086,
            CheckerGeneratorInvalidReturnType = 4087,
            CheckerYieldInFinally = 4088,
            CheckerYieldFromNonIterable = 4089,

            // Constant-expression / array errors
            CheckerNonConstantExpression = 4090,
            CheckerDivisionByZero = 4091,
            CheckerDuplicateArrayKey = 4092,
            CheckerInvalidArrayAccess = 4093,
            CheckerDestructuringNonArray = 4094,
            CheckerDestructuringSpread = 4095,
            CheckerSpreadNonIterable = 4096,

            // Static / instance context errors
            CheckerThisInStaticContext = 4097,
            CheckerNonStaticCalledStatically = 4098,
            CheckerStaticCalledOnInstance = 4099,
            CheckerStaticOutsideClass = 4100,

            CheckerSymbolNameNotFound = 4101,

            CheckerGotoProhibited = 4104,

            // Promoted-property / readonly-class errors
            CheckerPromotedPropertyNoType = 4105,
            CheckerPromotedPropertyInAbstract = 4106,
            CheckerPromotedVariadic = 4107,
            CheckerReadonlyClassMutableProperty = 4108,
            CheckerReadonlyClassStaticProperty = 4109,

            // Enum / interface / trait errors
            CheckerEnumCaseMissingValue = 4110,
            CheckerEnumCaseValueOnNonBacked = 4111,
            CheckerEnumCaseDuplicateValue = 4112,
            CheckerEnumPropertyNotAllowed = 4113,
            CheckerInterfacePropertyInitializer = 4114,
            CheckerInterfacePropertyNotAllowed = 4115,
            CheckerTraitConflict = 4116,
            CheckerCircularTraitUse = 4117,
            CheckerOverloadSignatureIncompatible = 4118,
            CheckerIncomparableTypes = 4119,
            CheckerConcatNonStringable = 4120,

            // finally / catch quality errors
            CheckerEmptyCatch = 4121,
            CheckerReturnInFinally = 4122,
            CheckerBreakInFinally = 4123,
            CheckerDuplicateCatch = 4124,
            CheckerCatchOrderBroadFirst = 4125,

            // Attribute errors
            CheckerNotAnAttributeClass = 4126,
            CheckerAttributeTargetMismatch = 4127,
            CheckerAttributeNotRepeatable = 4128,
            CheckerOverrideNotOverriding = 4129,

            // Import errors
            CheckerUnusedImport = 4130,
            CheckerDuplicateImport = 4131,
            CheckerConflictingImportAlias = 4132,

            // Restricted-feature errors
            CheckerVariableVariableProhibited = 4133,
            CheckerDynamicPropertyProhibited = 4134,
            CheckerCompactProhibited = 4135,
            CheckerExtractProhibited = 4136,
            CheckerGlobalVariableWarning = 4137,

            // Closure parameter inference errors
            CheckerClosureParameterTypeRequired = 4138,

            // with keyword — readonly restrictions
            CheckerCloneWithReadonlyRequiresConfig = 4139,
            CheckerWithReadonlyFinalClass = 4140,
            CheckerWithReadonlyInPlace = 4141,

            // Function-call argument-count errors
            CheckerMissingArgument = 4142,
            CheckerTooManyArguments = 4143,

            // Template-string types (Story 08.5 Phase 6)
            CheckerTemplateStringUnknownEscape = 4144,
            CheckerTemplateStringInvalidQuantifierRange = 4145,
            CheckerTemplateStringMaxStatesExceeded = 4146,

            // Extension declaration errors
            CheckerExtensionMissingExtends = 4147,

            // Runtime generic tracking errors
            /// <summary>
            /// <c>typeof(T)</c> names a class generic parameter inside a <c>static</c> member. The
            /// binding lives on the instance, so there is nothing to read it from.
            /// </summary>
            CheckerGenericTypeofInStaticContext = 4148,

            // 4149 was CheckerGenericVariadicConstructorUnsupported: the interim rejection of a
            // variadic constructor on a runtime-tracked generic class. Retired when Mechanism C moved
            // type arguments out of the constructor signature entirely, so the two no longer contend
            // for a parameter position. Do not reuse the number.

            /// <summary>
            /// A function or method name ends with the suffix reserved for the generic variant a
            /// generic callable is emitted alongside, which would collide with the generated symbol.
            /// </summary>
            CheckerReservedGenericVariantSuffix = 4150,

            /// <summary>
            /// An override of a generic method drops or renames the generic parameters it inherits, so
            /// the base call site cannot supply type arguments the override can read.
            /// </summary>
            CheckerGenericOverrideParameterMismatch = 4151,

            /// <summary>
            /// <c>default(T)</c> names a class generic parameter inside a <c>static</c> member. The
            /// binding lives on the instance, so the zero value of the bound type cannot be read.
            /// </summary>
            CheckerGenericDefaultInStaticContext = 4152,

            /// <summary>
            /// A <c>return &lt;expr&gt;;</c> appears inside <c>__construct</c> or <c>__destruct</c>.
            /// PHP raises a fatal error for value-carrying returns from either magic method; bare
            /// <c>return;</c> remains legal.
            /// </summary>
            CheckerConstructorDestructorCannotReturnValue = 4153,

            /// <summary>
            /// A property hook carries a modifier other than <c>final</c>. PHP 8.4+ only allows
            /// <c>final</c> on a hook; visibility/static/abstract/readonly/var/asymmetric-visibility
            /// are fatal parse errors.
            /// </summary>
            CheckerPropertyHookInvalidModifier = 4154,

            /// <summary>
            /// A property (or promoted constructor parameter) declares both <c>readonly</c> and a
            /// hook block. PHP 8.4+ fatals with "Hooked properties cannot be readonly" — a hook already
            /// controls read/write access, so the modifier is redundant and rejected outright.
            /// </summary>
            CheckerHookedPropertyReadonly = 4155,

            /// <summary>
            /// <c>instanceof T</c> / <c>is T</c> (and aliases) names a class generic parameter inside a
            /// <c>static</c> member. The binding lives on the instance, so there is nothing to reify the
            /// check against — same shape as <see cref="CheckerGenericTypeofInStaticContext"/> /
            /// <see cref="CheckerGenericDefaultInStaticContext"/>, for the emitter reify-not-reject path
            /// added for Prop-init #37.
            /// </summary>
            CheckerGenericInstanceofInStaticContext = 4156,

            /// <summary>
            /// Typed instance property may be unreadably uninitialized (no initializer, not
            /// promoted, and not definitely assigned on all constructor paths — or read in the
            /// constructor before a definite assignment). Prefer declaring <c>?T $prop = null</c>,
            /// adding an initializer, or guarding the read with <c>??</c>/<c>isset</c>.
            /// </summary>
            CheckerPropertyPossiblyUninitialized = 4157,

            /// <summary>
            /// <c>unset($this->prop)</c> on a declared typed property without
            /// <c>#[\Tyhp\AllowUnset]</c>. PHP returns the slot to the uninitialized state, which
            /// would invalidate property-init guarantees (Prop-init #8). Prefer <c>?T = null</c>,
            /// or opt in with the attribute when distinguishing "no value" from null is required.
            /// </summary>
            CheckerUnsetTypedPropertyWithoutAllowUnset = 4158,

            /// <summary>
            /// A non-nullable struct property without a default was not set in
            /// <c>new Struct() with [...]</c> (or was constructed with bare <c>new Struct()</c>).
            /// Required struct properties must be supplied via <c>with</c> at construction.
            /// </summary>
            CheckerStructRequiredPropertyNotSet = 4159,

            /// <summary>
            /// A <c>mixed</c> value is used in a type-specific operation (member access, call,
            /// indexing, arithmetic, etc.) without prior narrowing. Assignment/return already
            /// reject <c>mixed</c> sources; this covers the remaining use sites. Comparison and
            /// <c>instanceof</c>/<c>is</c> are allowed (they enable narrowing).
            /// </summary>
            CheckerMixedRequiresNarrowing = 4160,

            /// <summary>
            /// A function or method name ends with the suffix reserved for polyfill property-hook
            /// get/set methods (<c>__get_&lt;prop&gt;__tyhpPropertyHook</c>), which would collide with
            /// a generated symbol.
            /// </summary>
            CheckerReservedPropertyHookMethodSuffix = 4161,

            /// <summary>
            /// PHP 8.5 pipe <c>|&gt;</c>: the right-hand side does not type as a callable
            /// (closure, first-class callable, <c>callable</c>/<c>\Closure</c>, or <c>__invoke</c>).
            /// </summary>
            CheckerPipeRhsNotCallable = 4162,

            /// <summary>
            /// PHP 8.5 pipe <c>|&gt;</c>: the right-hand side is callable but cannot accept exactly
            /// one argument (e.g. more than one required parameter, or zero parameters).
            /// </summary>
            CheckerPipeRhsInvalidArity = 4163,

            /// <summary>
            /// PHP 8.5 pipe <c>|&gt;</c>: the right-hand side takes its first parameter by reference.
            /// Piped values are temporaries, so by-ref callables are rejected (prefer-ref stdlib
            /// exceptions are not modeled).
            /// </summary>
            CheckerPipeRhsByRefParameter = 4164,

            /// <summary>
            /// PHP 8.5 <c>#[\NoDiscard]</c>: the return value of a marked function/method was
            /// discarded (expression statement / discarded for-list item). Suppress with
            /// <c>(void)</c>. Warning (matches PHP <c>E_WARNING</c> / <c>E_USER_WARNING</c>).
            /// </summary>
            CheckerNoDiscardReturnUnused = 4165,

            /// <summary>
            /// A property hook redeclares a <c>get</c>/<c>set</c> that an ancestor already marked
            /// <c>final</c>. Matches PHP 8.4+ class-declaration fatal
            /// <c>Cannot override final property hook Class::$prop::get()</c> (independent per hook).
            /// </summary>
            CheckerFinalPropertyHookOverridden = 4166,

            /// <summary>
            /// Authored <c>&amp;get</c> (by-ref get hook) when targeting PHP &lt; 8.4. Native hooks
            /// preserve by-ref on PHP ≥ 8.4; the &lt; 8.4 polyfill cannot (<c>__get</c> cannot return
            /// by reference), so Tyhp rejects the construct instead of silently lowering to by-value.
            /// </summary>
            CheckerByRefPropertyGetHookRequiresPhp84 = 4167,

            /// <summary>
            /// Parameterized <c>static&lt;…&gt;</c> is forbidden in all scopes. Late-static binding
            /// must not invent or rebind type arguments; use bare <c>static</c>, <c>self&lt;…&gt;</c>,
            /// <c>parent&lt;…&gt;</c>, or an explicit class name instead.
            /// </summary>
            CheckerParameterizedStaticForbidden = 4168,

            // Code-quality warnings (4200+ range)
            CheckerUnusedVariable = 4200,
            CheckerUnusedParameter = 4201,
            CheckerUnusedPrivateMember = 4202,
            CheckerAssignmentInCondition = 4203,
            CheckerConditionAlwaysTrueFalse = 4204,
            CheckerRedundantCast = 4205,
            CheckerDeadStore = 4206,
            CheckerUnnecessaryNullCheck = 4207,
            CheckerUnreachableArm = 4208,
            CheckerLossyCast = 4209,
            CheckerErrorThresholdReached = 4210,
            CheckerStaticReturnSelfInNonFinal = 4211,

            /// <summary>
            /// Disposable scope contains unresolvable circular references between disposable objects;
            /// emitter will fall back to try/finally instead of DisposableScope.
            /// </summary>
            CheckerDisposableCircularReference = 4212,

            /// <summary>
            /// <c>if (!*_exists(...))</c> gate argument does not name the gated declaration
            /// (must be a fully-qualified name or <c>__NAMESPACE__.'\\Name'</c>).
            /// </summary>
            CheckerExistenceGateInvalidName = 4213,

            // Feature-story checker diagnostics (4300–4399) — Stories 16, 20.5, 25, 26, 27, 28
            // Story 20.5 — PHP version gating (`declare(php=…)` / `#[\Tyhp\Php]`)
            CheckerPhpVersionInvalidConstraint = 4300,
            CheckerPhpVersionDeclareNotAlone = 4301,
            CheckerPhpVersionUnreachable = 4302,
            CheckerPhpVersionDuplicateDeclaration = 4303,
            CheckerPhpVersionAttributeInvalidTarget = 4304,
            CheckerPhpVersionAttributeInvalidArgument = 4305,
            CheckerPhpVersionDefaulted = 4306,
            // 4307–4309 reserved for Story 20.5 follow-ups

            // Story 28 — generic type parameter defaults
            /// <summary>
            /// A generic parameter's default type is not assignable to its constraint
            /// (e.g. <c>T extends Countable = string</c>).
            /// </summary>
            CheckerGenericDefaultDoesNotSatisfyConstraint = 4310,

            /// <summary>
            /// A non-defaulted generic parameter follows a defaulted one (defaults must be trailing).
            /// </summary>
            CheckerGenericNonDefaultAfterDefault = 4311,

            /// <summary>
            /// A generic parameter's default type refers to itself (directly or through other
            /// parameters' defaults), forming a cycle.
            /// </summary>
            CheckerGenericDefaultCircularReference = 4312,

            // 4313–4319 reserved for Story 28 follow-ups

            // Story 16 — parsable lambdas / PropertyPath (Phase 1)
            /// <summary>
            /// A <c>PropertyPath&lt;T, R&gt;</c> parameter was given something other than an inline
            /// <c>fn</c> arrow expression.
            /// </summary>
            CheckerPropertyPathRequiresInlineFn = 4320,

            /// <summary>
            /// An inline <c>fn</c> passed to <c>PropertyPath&lt;T, R&gt;</c> is not a simple property
            /// access chain from the lambda parameter (e.g. method call, binary op, nested call).
            /// </summary>
            CheckerPropertyPathInvalidBody = 4321,

            /// <summary>
            /// An inline <c>fn</c> passed to <c>Expression&lt;T, R&gt;</c> contains a node kind
            /// that expression trees cannot represent (assignment, await, nested fn, etc.).
            /// </summary>
            CheckerExpressionUnsupportedNode = 4322,

            /// <summary>
            /// An <c>Expression&lt;T, R&gt;</c> parameter was given something other than an inline
            /// <c>fn</c> arrow expression.
            /// </summary>
            CheckerExpressionRequiresInlineFn = 4323,

            /// <summary>
            /// A captured outer-scope variable in an expression-tree <c>fn</c> is not definitely
            /// assigned at the construction site.
            /// </summary>
            CheckerExpressionCapturedVarUndefined = 4324,

            /// <summary>
            /// A required (non-optional) field of a synthetic struct bag is missing from an array
            /// literal — typically a <c>__CallableParametersStruct</c> / Tuple bag omitting a
            /// required callable parameter. Defaulted parameters are optional fields and may be
            /// omitted (required-key assignability; no exponential subset intersection).
            /// </summary>
            CheckerStructRequiredKeyMissing = 4325,

            // 4326–4399 reserved for Stories 25, 26, 27 (allocate when those stories land)

            // Deprecation warnings (4500+ range)
            CheckerDeprecatedUsage = 4500,
            CheckerObsoleteUsage = 4501,

            // Informational (4800+ range)
            CheckerEvalUsage = 4800,
            CheckerIncludeNotAllowed = 4801,

            /// <summary>
            /// A named function or method is declared inside the body of another named function or
            /// method. PHP nested functions become global once the enclosing callable runs (they do
            /// not close over the enclosing scope like a closure), which does not fit Tyhp's static,
            /// per-file symbol model, so Tyhp rejects the declaration instead of emitting it.
            /// </summary>
            CheckerNestedNamedFunctionNotAllowed = 4802,

            #endregion Checker

            #region Emitter

            EmitterUnknownError = 5001,
            EmitterUnsupportedAstNode = 5002,
            EmitterOutputPathConflict = 5003,
            EmitterNamespaceMismatch = 5004,
            EmitterInvalidOutputPath = 5005,
            EmitterTypeErasureWarning = 5006,
            EmitterWriteError = 5007,
            EmitterTyhpConstructNotImplemented = 5008,
            EmitterInvalidDeclareDirective = 5009,
            EmitterEmptyOutputFile = 5010,
            EmitterMergeConflict = 5011,

            /// <summary>A Tyhp construct that cannot be emitted to PHP.</summary>
            EmitterUnsupportedConstruct = 5012,

            /// <summary>A generated method name conflicts with an existing method.</summary>
            EmitterNameConflict = 5013,

            /// <summary>The TyhpLib runtime is required but not configured.</summary>
            EmitterMissingRuntime = 5014,

            /// <summary>A configured struct backing class was not found.</summary>
            EmitterStructBackingError = 5015,

            /// <summary>A disposable variable's type does not implement IsDisposable.</summary>
            EmitterDisposableError = 5016,

            /// <summary>
            /// An attribute was stripped because the target PHP version cannot represent it on that
            /// construct (e.g. top-level <c>const</c> attributes need PHP ≥ 8.5; property-hook
            /// attributes need native hooks on PHP ≥ 8.4). Stripping changes Reflection semantics.
            /// </summary>
            EmitterAttributeStrippedForPhpVersion = 5017,

            /// <summary>
            /// A required runtime package's <c>extra.tyhp.interopContractVersion</c> is missing or
            /// does not match <see cref="TyhpLang.Interop.InteropContract.CurrentVersion"/>.
            /// </summary>
            EmitterInteropContractMismatch = 5018,

            /// <summary>
            /// Overloaded postfix <c>++</c>/<c>--</c> appears where the emitter cannot statement-split
            /// to capture the prior value (e.g. short-circuit or loop-condition expressions).
            /// </summary>
            EmitterPostfixOperatorOverloadRequiresStatementSplit = 5019,

            #endregion Emitter

            #region Configuration (6000–6999)

            /// <summary>Generic configuration error.</summary>
            ConfigUnknownError = 6001,

            /// <summary>A required configuration field is missing.</summary>
            ConfigMissingRequiredField = 6002,

            /// <summary>A configuration value is out of range or invalid type.</summary>
            ConfigInvalidValue = 6003,

            /// <summary>A glob pattern is malformed.</summary>
            ConfigInvalidGlobPattern = 6004,

            /// <summary>The output path is not writable.</summary>
            ConfigOutputPathNotWritable = 6005,

            /// <summary>The target PHP version is not recognized.</summary>
            ConfigInvalidPhpVersion = 6006,

            /// <summary>A PSR-4 mapping is invalid.</summary>
            ConfigPsr4InvalidMapping = 6007,

            /// <summary>The <c>type</c> field has an unrecognized value.</summary>
            ConfigInvalidProjectType = 6008,

            #endregion Configuration (6000–6999)

            #region CLI — Shared / Generic (7000–7099)

            // Shared CLI error codes used across multiple actions.

            #endregion CLI — Shared / Generic (7000–7099)

            #region CLI — build action (7100–7199)

            /// <summary>Generic build error.</summary>
            BuildUnknownError = 7100,

            /// <summary>No source files found matching include patterns.</summary>
            BuildNoSourceFiles = 7101,

            /// <summary>Multiple declarations write to the same output file.</summary>
            BuildOutputPathConflict = 7102,

            /// <summary>Failed to write an output file to disk.</summary>
            BuildFileWriteError = 7103,

            /// <summary>Failed to clean the output directory.</summary>
            BuildCleanFailed = 7104,

            /// <summary>Tyhp runtime Composer package not available for installation.</summary>
            BuildRuntimePackageNotAvailable = 7105,

            #endregion CLI — build action (7100–7199)

            #region CLI — lint action (7200–7299)

            /// <summary>The <c>--file</c> target does not exist.</summary>
            LintFileNotFound = 7200,

            /// <summary>An explicit lint path does not exist.</summary>
            LintPathNotFound = 7201,

            /// <summary>An explicit lint path is invalid.</summary>
            LintInvalidPath = 7202,

            /// <summary>Access was denied during lint.</summary>
            LintAccessDenied = 7203,

            /// <summary>An I/O error occurred during lint.</summary>
            LintIoError = 7204,

            /// <summary>An unexpected error occurred during lint.</summary>
            LintUnexpectedError = 7205,

            /// <summary>No source files were found to lint.</summary>
            LintNoSourceFiles = 7206,

            /// <summary>Lint was cancelled.</summary>
            LintCancelled = 7207,

            /// <summary>The <c>--file</c> target is not within project include paths.</summary>
            LintFileNotInProject = 7208,

            /// <summary>An auto-fix was applied.</summary>
            LintFixApplied = 7209,

            /// <summary>An auto-fix could not be applied.</summary>
            LintFixFailed = 7210,

            /// <summary>The <c>--format</c> value is not recognized.</summary>
            LintUnsupportedFormat = 7211,

            #endregion CLI — lint action (7200–7299)

            #region CLI — language_server action (7300–7399)

            // Language server error codes are added by the language-server story.

            #endregion CLI — language_server action (7300–7399)

            #region CLI — xdebug_proxy action (7400–7499)

            // XDebug proxy error codes are added by Story 14.

            #endregion CLI — xdebug_proxy action (7400–7499)

            #region CLI — generate_tyhpdef action (7500–7599)

            /// <summary>Library project contains entrypoint file(s) with root-level side-effect statements.</summary>
            TyhpdefLibraryEntrypointDetected = 7505,

            #endregion CLI — generate_tyhpdef action (7500–7599)

            #region CLI — init action (7600–7699)

            // Init action error codes are added by Story 13.

            #endregion CLI — init action (7600–7699)

            #region CLI — composer action (7700–7799)

            // Composer action error codes are added by Story 13.

            #endregion CLI — composer action (7700–7799)

            #region CLI — debug / integrity_check actions (7800–7899)

            /// <summary>Project configuration failed an integrity check.</summary>
            IntegrityCheckConfigInvalid = 7800,

            /// <summary>One or more tyhpdef files failed to parse during integrity check.</summary>
            IntegrityCheckTyhpdefError = 7801,

            /// <summary>AST cache entries were corrupted or unreadable.</summary>
            IntegrityCheckCacheCorrupted = 7802,

            /// <summary>Runtime environment failed an integrity check (e.g. critically low disk space).</summary>
            IntegrityCheckEnvironmentError = 7803,

            #endregion CLI — debug / integrity_check actions (7800–7899)

            #region Tyhpdef

            /// <summary>A tyhpdef file failed to parse.</summary>
            TyhpdefParseError = 8001,

            /// <summary>A tyhpdef declares a symbol that already exists.</summary>
            TyhpdefDuplicateDeclaration = 8002,

            /// <summary>A configured tyhpdef path doesn't exist.</summary>
            TyhpdefFileNotFound = 8003,

            /// <summary>A tyhpdef file has an unexpected structure.</summary>
            TyhpdefInvalidFormat = 8004,

            /// <summary>A tyhpdef symbol failed during binding (semantic analysis), as opposed to parsing.</summary>
            TyhpdefBindError = 8005,

            /// <summary>An extension member conflicts with a declared member on the same tyhpdef class.</summary>
            TyhpdefExtensionConflict = 8010,

            /// <summary>A <c>use extension</c> reference in tyhpdef could not be resolved to an extension declaration.</summary>
            TyhpdefExtensionNotFound = 8011,

            /// <summary>Invalid member with the <c>extension</c> qualifier in tyhpdef.</summary>
            TyhpdefInlineExtensionInvalidMember = 8012,

            /// <summary>
            /// A tyhpdef <c>extension operator</c> was declared without a body. Bodyless
            /// <c>operator …;</c> (no <c>extension</c>) means native PHP passthrough; mapped
            /// overloads must use <c>extension operator</c> with a brace or <c>=&gt;</c> body.
            /// </summary>
            TyhpdefExtensionOperatorRequiresBody = 8013,

            /// <summary>The same fully-qualified type name is defined in more than one Composer package.</summary>
            TyhpdefDuplicateFqnAcrossPackages = 8025,

            /// <summary>The PHP extension Composer package for the configured PHP version was not found.</summary>
            TyhpdefPhpExtensionPackageNotFound = 8026,

            /// <summary>A Tyhp runtime Composer package was not found.</summary>
            TyhpdefRuntimePackageNotFound = 8027,

            #endregion Tyhpdef
        }
}