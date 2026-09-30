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
    /// <item><term>7000–7099</term><description>Shared CLI / generic errors; install action</description></item>
    /// <item><term>7100–7199</term><description>build action</description></item>
    /// <item><term>7200–7299</term><description>lint action</description></item>
    /// <item><term>7300–7399</term><description>language_server action</description></item>
    /// <item><term>7400–7499</term><description>xdebug_proxy action</description></item>
    /// <item><term>7500–7599</term><description>generate_tyhpdef action</description></item>
    /// <item><term>7600–7699</term><description>init action</description></item>
    /// <item><term>7700–7799</term><description>composer action</description></item>
    /// <item><term>7800–7899</term><description>debug / integrity_check actions</description></item>
    /// <item><term>7900–7999</term><description>overlay action</description></item>
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

            /// <summary>
            /// The <c>&lt;Type&gt;</c> target of an extension operator resolved to a
            /// non-instantiable builtin (<c>void</c>, <c>never</c>, <c>null</c>, <c>mixed</c>,
            /// <c>resource</c>, <c>true</c>, <c>false</c>).
            /// </summary>
            ExtensionOperatorTargetNotInstantiable = 3025,

            /// <summary>
            /// An <c>extends</c> target resolved to an <c>extern</c> placeholder
            /// (tyhpdef or <c>.tyhp</c>). Pair with <see cref="BinderExternImplementsType"/>.
            /// </summary>
            BinderExternExtendsType = 3026,

            /// <summary>
            /// An <c>implements</c> target resolved to an <c>extern</c> placeholder
            /// (tyhpdef or <c>.tyhp</c>). Pair with <see cref="BinderExternExtendsType"/>.
            /// </summary>
            BinderExternImplementsType = 3027,

            /// <summary>
            /// A file-level type alias occupies the function namespace, so it cannot share a
            /// name with a function in the same namespace (<c>type Foo</c> + <c>function Foo()</c>).
            /// </summary>
            BinderTypeAliasConflictsWithFunction = 3028,

            /// <summary>
            /// Type aliases form a cycle (<c>type A = B; type B = A</c>).
            /// </summary>
            BinderCircularTypeAlias = 3029,

            /// <summary>
            /// A file-level type alias uses a name that cannot be a PHP function
            /// (<c>echo</c>, <c>list</c>, <c>empty</c>, <c>int</c>, …).
            /// </summary>
            BinderTypeAliasReservedFunctionName = 3030,

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
            /// <summary>
            /// A <c>return</c> belongs to a <c>finally</c> block. A <c>return</c> inside a closure,
            /// arrow function, or <c>async</c> block written in that <c>finally</c> is the nested
            /// callable's return and is not this error.
            /// </summary>
            CheckerReturnInFinally = 4122,
            CheckerBreakInFinally = 4123,
            CheckerDuplicateCatch = 4124,
            CheckerCatchOrderBroadFirst = 4125,

            // Attribute errors
            CheckerNotAnAttributeClass = 4126,
            CheckerAttributeTargetMismatch = 4127,
            CheckerAttributeNotRepeatable = 4128,
            /// <summary>
            /// <c>#[Override]</c> on a method or property that does not override a non-private
            /// ancestor or interface member. Properties are only legal as a target when
            /// <c>output.phpVersion</c> is ≥ 8.5; below that, target mismatch (TYHP4127) fires
            /// instead. Constructors use <see cref="CheckerOverrideOnConstructor"/>, not this code.
            /// </summary>
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

            /// <summary>
            /// An extension declares members but has neither a header <c>extends Type</c>
            /// nor nested <c>extends</c> groups.
            /// </summary>
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
            /// <c>(void)</c>. Interface and abstract callees do not warn. A string-literal
            /// <c>$message</c> is <c>{1}</c>. Warning (matches PHP <c>E_WARNING</c> / <c>E_USER_WARNING</c>).
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

            /// <summary>
            /// A non-<c>global</c> <c>use</c> / <c>use function</c> / <c>use const</c> /
            /// <c>use extension</c> re-imports a symbol already in scope via <c>global use</c>
            /// without an alias or adaptations. The local import is not needed.
            /// </summary>
            CheckerRedundantGlobalImport = 4169,

            /// <summary>
            /// <c>use extension</c> adaptation tried to rename an operator with <c>as</c>.
            /// Operators cannot be renamed; use <c>hide</c> or <c>insteadof</c>.
            /// </summary>
            CheckerExtensionOperatorRenameForbidden = 4170,

            /// <summary>
            /// A user type's Tyhp name is or ends with the suffix reserved for standalone
            /// extension backer aliases (<c>__tyhpExtensionBacker</c>).
            /// </summary>
            CheckerReservedExtensionBackerSuffix = 4171,

            /// <summary>
            /// A standalone <c>extension Name { }</c> (Tyhp or tyhpdef) has no members.
            /// </summary>
            CheckerEmptyExtension = 4172,

            /// <summary>
            /// <c>use extension { Name hide; }</c> names a member that extension does not declare.
            /// </summary>
            CheckerExtensionHideUnknownMember = 4173,

            /// <summary>
            /// A spliced member's expression writes a parameter not declared <c>&amp;</c>, declares
            /// <c>&amp;</c> on a parameter it does not write, or mutates <c>$this</c>.
            /// </summary>
            CheckerInlineParameterMutation = 4174,

            /// <summary>
            /// A spliced member reduces to itself directly or transitively.
            /// </summary>
            CheckerInlineCycle = 4175,

            /// <summary>
            /// <c>#[\Tyhp\Optimize\Inline]</c> on an extension member (tyhpdef thin member or Tyhp
            /// <c>extension { }</c> member). Extension splicing is decided by form, not the attribute.
            /// </summary>
            CheckerInlineAttributeOnExtensionMember = 4176,

            /// <summary>
            /// User code declares a variable colliding with the generated inline temp prefix
            /// (<c>__tyhpInlineTemp</c>).
            /// </summary>
            CheckerReservedInlineTempPrefix = 4177,

            /// <summary>
            /// A spliced body references a member less accessible than the spliced member itself.
            /// Allocated by Story 23; enforced by the Story 20.6 splice engine.
            /// </summary>
            CheckerInlineInaccessibleMember = 4179,

            /// <summary>
            /// A call passes a non-referenceable expression to a by-reference parameter. General
            /// call-site rule (not inline-only), at every optimization level. Allocated by Story 23;
            /// enforced here. Reads <c>HasAccessor</c> / <c>GetHookReturnsRef</c>.
            /// </summary>
            CheckerNonReferenceableByRefArgument = 4180,

            /// <summary>
            /// An erased member's call site cannot be spliced faithfully, and has no PHP method to
            /// fall back to.
            /// </summary>
            CheckerErasedMemberUnsafeSplice = 4181,

            /// <summary>
            /// A free-function / namespaced-function call does not resolve to any declared function,
            /// tyhpdef stub, import, or compile-time builtin. Inactive PHP-version gates omit the
            /// symbol, so a call outside the gate is the same error.
            /// </summary>
            CheckerUndefinedFunction = 4182,

            /// <summary>
            /// Bare <c>...</c> as a generic type argument. Unknown-arity callables are
            /// <c>callable(...): TReturn</c> shapes, not type-argument wildcards.
            /// </summary>
            CheckerCallableEllipsisNotAllowed = 4183,

            /// <summary>
            /// Direct use of <c>callable(...): TReturn</c> as a parameter, property, or
            /// return type. The any-arity facet is a generic bound only.
            /// </summary>
            CheckerCallableAnyArityNotAValueType = 4184,

            /// <summary>
            /// Invoking a value whose type is the any-arity facet <c>callable(...): TReturn</c>
            /// (arity is unknown).
            /// </summary>
            CheckerCallableAnyArityNotInvokable = 4185,

            /// <summary>
            /// A type-alias factory is invoked with generic type arguments
            /// (<c>Optional&lt;int&gt;()</c>). The factory is not a generic function; write
            /// <c>typeof(Optional&lt;int&gt;)</c> or <c>Optional(typeof(int))</c>.
            /// </summary>
            CheckerTypeAliasFactoryTypeArguments = 4186,

            /// <summary>
            /// Postfix <c>...</c> on a pack used as a generic type argument. Packs splice as
            /// ordinary <c>callable(Rest&lt;T&gt; ...): R</c> shape parameters.
            /// </summary>
            CheckerCallablePackEllipsisNotAllowed = 4187,

            /// <summary>
            /// Postfix <c>T...</c> as a generic type argument, or glued to a value-parameter
            /// type (<c>int... $x</c>). Homogeneous variadic callables are
            /// <c>callable(T ...$args): R</c>.
            /// </summary>
            CheckerCallablePostfixEllipsisNotAllowed = 4188,

            /// <summary>
            /// Two <c>__CallableParametersSlice</c> uses of the same <c>TCallable</c> overlap.
            /// </summary>
            CheckerCallableSliceOverlap = 4189,

            /// <summary>
            /// Same-function slices of one <c>TCallable</c> leave a hole in required parameters.
            /// </summary>
            CheckerCallableSliceHole = 4190,

            /// <summary>
            /// <c>TMin</c> written on a non-variadic <c>__CallableParametersSlice</c> parameter.
            /// </summary>
            CheckerCallableSliceMinOnFixed = 4191,

            /// <summary>
            /// <c>TStart</c> / <c>TMin</c> of <c>__CallableParametersSlice</c> is not a
            /// non-negative int literal type.
            /// </summary>
            CheckerCallableSliceIndexNotIntLiteral = 4192,

            /// <summary>
            /// File-level <c>const int X = …</c> in Tyhp source. PHP allows typed constants only on
            /// classes, interfaces, traits, and enums.
            /// </summary>
            CheckerFileLevelTypedConstNotAllowed = 4193,

            /// <summary>
            /// A class/interface/trait/enum constant has no type and no typed ancestor to infer from.
            /// </summary>
            CheckerClassConstTypeRequired = 4194,

            /// <summary>
            /// A child class constant redeclares a parent, interface, or trait constant with a
            /// different type.
            /// </summary>
            CheckerClassConstTypeMismatch = 4195,

            /// <summary>
            /// A foreach key or value annotation is not a supertype of the iterated key/value type.
            /// The annotation does not narrow; the loop variable has the declared type.
            /// </summary>
            CheckerForeachBindingTypeMismatch = 4196,

            /// <summary>
            /// Member access, method call, extension lookup, or indexing on a receiver whose type
            /// could not be resolved. Suppressed when the receiver subtree already has an error
            /// (one report per failure). Unresolved assignability is unchanged. Mixed receivers
            /// use <see cref="CheckerMixedRequiresNarrowing"/> (TYHP4160).
            /// </summary>
            CheckerUnresolvedReceiver = 4197,

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

            // Feature-story checker diagnostics (4300–4399) — Stories 16, 20.5, 21.1, 25, 26, 27, 28
            // Story 20.5 — PHP version gating (`declare(php=…)` / `#[\Tyhp\Php]`)
            CheckerPhpVersionInvalidConstraint = 4300,
            CheckerPhpVersionDeclareNotAlone = 4301,
            CheckerPhpVersionUnreachable = 4302,
            CheckerPhpVersionDuplicateDeclaration = 4303,
            CheckerPhpVersionAttributeInvalidTarget = 4304,
            CheckerPhpVersionAttributeInvalidArgument = 4305,
            CheckerPhpVersionDefaulted = 4306,

            /// <summary>
            /// A <c>.tyhp</c> file used an <c>extern</c> name (type, function, or const:
            /// annotation, <c>new</c>, <c>instanceof</c>, <c>catch</c>, import, call,
            /// const fetch, inferred expression type, or union/intersection arm).
            /// </summary>
            CheckerExternTypeUsed = 4307,

            // 4308–4309 reserved for Story 20.5 follow-ups

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
            /// A <c>PropertyPath&lt;(callable(T): R)&gt;</c> parameter was given something other than an inline
            /// <c>fn</c> arrow expression.
            /// </summary>
            CheckerPropertyPathRequiresInlineFn = 4320,

            /// <summary>
            /// An inline <c>fn</c> passed to <c>PropertyPath&lt;(callable(T): R)&gt;</c> is not a simple property
            /// access chain from the lambda parameter (e.g. method call, binary op, nested call).
            /// </summary>
            CheckerPropertyPathInvalidBody = 4321,

            /// <summary>
            /// An inline <c>fn</c> passed to <c>Expression&lt;(callable(T): R)&gt;</c> contains a node kind
            /// that expression trees cannot represent (assignment, await, nested fn, etc.).
            /// </summary>
            CheckerExpressionUnsupportedNode = 4322,

            /// <summary>
            /// An <c>Expression&lt;(callable(T): R)&gt;</c> parameter was given something other than an inline
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

            /// <summary>
            /// A user class, enum, interface, or trait listed <c>\Traversable</c> in
            /// <c>extends</c> or <c>implements</c>. Only <c>\Iterator</c> and
            /// <c>\IteratorAggregate</c> may extend Traversable; other types implement or
            /// extend those instead. Harvested <c>.tyhpdef</c> shells are a different AST and
            /// are not diagnosed.
            /// </summary>
            CheckerTraversableCannotBeListed = 4326,

            /// <summary>
            /// <c>#[\Tyhp\PhpType]</c> was placed on a declaration that is not a PHP type-hint
            /// site (class / interface / trait / enum, enum case, catch, local, or property hook).
            /// </summary>
            CheckerPhpTypeInvalidTarget = 4327,

            /// <summary>
            /// Typed <c>yield</c> expression sites in a generator constrain <c>TSend</c> to the
            /// intersection of those targets; the intersection is empty (e.g. <c>int</c> vs
            /// <c>string</c>).
            /// </summary>
            CheckerGeneratorSendIntersectionEmpty = 4328,

            /// <summary>
            /// <c>#[\Tyhp\PhpType]</c> constructor argument is missing, not a string, or not a
            /// legal PHP type-hint spelling.
            /// </summary>
            CheckerPhpTypeInvalidSpelling = 4329,

            /// <summary>
            /// Homogeneous <c>ArrayAccess&lt;TKey, TValue&gt;</c> was instantiated with a
            /// <c>TKey</c> that is a struct or <c>array</c>. PHP offsets cannot be arrays;
            /// structs erase to arrays.
            /// </summary>
            CheckerArrayAccessKeyNotOffset = 4330,

            /// <summary>
            /// An <c>ArrayAccessShape</c> index used a wide <c>string</c>/<c>int</c>/<c>mixed</c>
            /// key instead of a struct-key literal (narrow or <c>as</c>).
            /// </summary>
            CheckerArrayAccessShapeWideKey = 4331,

            /// <summary>
            /// <c>$obj[] =</c> (append) on an <c>ArrayAccessShape</c>. Shapes have a closed key
            /// set; append is only legal on homogeneous <c>ArrayAccess</c>.
            /// </summary>
            CheckerArrayAccessShapeAppend = 4332,

            /// <summary>
            /// An <c>offsetGet</c>/<c>offsetSet</c> body does not cover a finite struct key.
            /// <c>throw</c> / <c>never</c> does not cover a key.
            /// </summary>
            CheckerArrayAccessShapeUnhandledKey = 4333,

            /// <summary>
            /// <c>bind</c> / <c>bindTo</c> / <c>call</c>: <c>$newThis</c> is not compatible with
            /// leftover <c>TScope</c> (or current <c>TThis</c>), object/name <c>$newScope</c> is
            /// not a legal scope for <c>TNewThis</c>, or <c>call</c> was given a non-object.
            /// </summary>
            CheckerClosureBindIncompatible = 4334,

            /// <summary>
            /// The bind sentinel <c>'static'</c> was written as a stored Closure <c>TScope</c>
            /// type argument. It is only a <c>bind</c> / <c>bindTo</c> argument meaning "keep the
            /// old scope".
            /// </summary>
            CheckerClosureStaticScopeStored = 4335,

            /// <summary>
            /// The closure cannot be rebound: arrow function, first-class callable /
            /// <c>fromCallable</c> of a non-Closure, or an internal-class <c>$newThis</c> /
            /// <c>$newScope</c>.
            /// </summary>
            CheckerClosureNonRebindable = 4336,

            /// <summary>
            /// A user class, enum, interface, or trait listed <c>\UnitEnum</c> or
            /// <c>\BackedEnum</c> in <c>extends</c> or <c>implements</c>. Only the engine
            /// <c>\BackedEnum</c> tyhpdef may extend <c>\UnitEnum</c>. Harvested
            /// <c>.tyhpdef</c> shells are a different AST and are not diagnosed.
            /// </summary>
            CheckerEngineEnumCannotBeListed = 4337,

            /// <summary>
            /// A user <c>.tyhp</c> enum redeclared <c>cases</c>, <c>from</c>, or
            /// <c>tryFrom</c>. The engine supplies those methods. Harvested
            /// <c>.tyhpdef</c> shells may keep the signatures and are not diagnosed.
            /// </summary>
            CheckerEnumEngineMethodRedeclared = 4338,

            /// <summary>
            /// <c>#[Override]</c> on <c>__construct</c>. PHP constructors are exempt from override
            /// semantics, so the attribute is invalid even when a parent constructor exists.
            /// Distinct from <see cref="CheckerOverrideNotOverriding"/> (missing parent member).
            /// </summary>
            CheckerOverrideOnConstructor = 4339,

            /// <summary>
            /// <c>#[\Tyhp\NativeTypeTest]</c> on a declaration that is not a free function
            /// or concrete static method (instance method, abstract/interface method,
            /// closure, class, …).
            /// </summary>
            CheckerNativeTypeTestInvalidTarget = 4340,

            /// <summary>
            /// <c>#[\Tyhp\NativeTypeTest]</c> on a function that does not declare
            /// <c>$firstParam is T</c> on its first parameter.
            /// </summary>
            CheckerNativeTypeTestRequiresTypeGuard = 4341,

            /// <summary>
            /// <c>#[\Tyhp\NativeTypeTest]</c> guard type is a union, intersection, nullable,
            /// generic application, or other non-single type.
            /// </summary>
            CheckerNativeTypeTestTypeNotSingle = 4342,

            /// <summary>
            /// <c>#[\Tyhp\NativeTypeTest]</c> function has a required parameter after the
            /// guarded first parameter, so emit cannot be <c>fqn($x)</c>.
            /// </summary>
            CheckerNativeTypeTestExtraParamsNeedDefaults = 4343,

            /// <summary>
            /// Two callables marked <c>#[\Tyhp\NativeTypeTest]</c> whose guarded types
            /// overlap (equal, subtype, or shared union members).
            /// </summary>
            CheckerNativeTypeTestDuplicate = 4344,

            /// <summary>
            /// <c>#[\Tyhp\EraseGeneric]</c> on a declaration that is not a class, property, or
            /// promoted constructor parameter (method, function, …).
            /// </summary>
            CheckerEraseGenericInvalidTarget = 4345,

            /// <summary>
            /// Author-written <c>#[\Tyhp\GenericRuntime]</c> on Tyhp source. The compiler
            /// restamps the attribute; authors do not write it.
            /// </summary>
            CheckerGenericRuntimeAuthorWritten = 4346,

            // Story 27 — object shapes and __New<T>
            /// <summary>
            /// An object-shape alias was used as a class: <c>new</c>, <c>::</c>,
            /// <c>extends</c>, <c>implements</c>, or <c>::class</c>.
            /// </summary>
            CheckerObjectShapeUsedAsClass = 4347,

            /// <summary>
            /// An <c>object { }</c> shape appeared somewhere other than the right-hand
            /// side of a <c>type</c> alias.
            /// </summary>
            CheckerObjectShapeNotTypeAlias = 4348,

            /// <summary>
            /// A <c>type</c> alias used an empty <c>object { }</c> body. The unshaped
            /// spelling is <c>object</c>.
            /// </summary>
            CheckerObjectShapeEmpty = 4349,

            /// <summary>
            /// An object-shape member was declared <c>protected</c> or <c>private</c>.
            /// Shape members are public only.
            /// </summary>
            CheckerObjectShapeNonPublicMember = 4350,

            /// <summary>
            /// The type argument of <c>__New&lt;T&gt;</c> is not an object-shape alias.
            /// </summary>
            CheckerNewTypeArgumentNotObjectShape = 4351,

            /// <summary>
            /// A type does not satisfy <c>__New&lt;Shape&gt;</c> because it is abstract,
            /// an interface, an enum, or a trait.
            /// </summary>
            CheckerNewConstraintNotConstructable = 4352,

            /// <summary>
            /// A type does not satisfy <c>__New&lt;Shape&gt;</c> because its constructor
            /// is not public.
            /// </summary>
            CheckerNewConstraintNonPublicConstructor = 4353,

            /// <summary>
            /// A type does not satisfy <c>__New&lt;Shape&gt;</c> because its constructor
            /// is not compatible with the shape (arity or parameter types).
            /// </summary>
            CheckerNewConstraintConstructorMismatch = 4354,

            /// <summary>
            /// <c>new T()</c> where <c>T</c> is not a <c>__New&lt;…&gt;</c> type parameter.
            /// </summary>
            CheckerNewTypeParameterRequiresNew = 4355,

            /// <summary>
            /// <c>new $cls()</c> where <c>$cls</c> is <c>__ClassName&lt;Shape&gt;</c>
            /// without <c>__New</c>.
            /// </summary>
            CheckerNewClassNameRequiresNew = 4356,

            /// <summary>
            /// Arguments of <c>new T(...)</c> / <c>new $cls(...)</c> do not match the
            /// object shape's constructor.
            /// </summary>
            CheckerObjectShapeConstructorArgumentMismatch = 4357,

            /// <summary>
            /// <c>object</c> or <c>mixed</c> assigned to an object shape without a
            /// shape guard.
            /// </summary>
            CheckerObjectShapeRequiresGuard = 4358,

            /// <summary>
            /// <c>$x-&gt;__construct()</c> on a value typed as an object shape.
            /// <c>__construct</c> on a shape is a constructability signature, not an
            /// instance method.
            /// </summary>
            CheckerObjectShapeConstructNotCallable = 4359,

            /// <summary>
            /// Bare <c>callable</c> or <c>mixed</c> assigned to a callable shape
            /// without a shape guard.
            /// </summary>
            CheckerCallableShapeRequiresGuard = 4360,

            /// <summary>
            /// An extension member still names its target with <c>extends Type $this</c>
            /// or <c>operator op&lt;Type&gt;</c>. The target belongs on the block
            /// (<c>extension Name extends Type</c> or a nested <c>extends Type { }</c> group).
            /// </summary>
            ParserExtensionLegacyMemberTarget = 4361,

            /// <summary>
            /// A header <c>extends Type</c> and a nested <c>extends</c> group are both present.
            /// </summary>
            CheckerExtensionHeaderAndNestedTargets = 4362,

            /// <summary>
            /// A nested <c>extends</c> group shares the extension body with a member that is not in a group.
            /// </summary>
            CheckerExtensionLooseMemberBesideGroup = 4363,

            /// <summary>
            /// An extension target is not one named class, interface, enum, alias, builtin, or generic application.
            /// </summary>
            CheckerExtensionTargetNotSingleType = 4364,

            /// <summary>
            /// A type parameter on <c>extension Name&lt;T&gt;</c> or <c>extends&lt;T&gt;</c> is never referenced.
            /// </summary>
            CheckerExtensionUnusedTypeParameter = 4365,

            /// <summary>
            /// A method type parameter uses the name of a type parameter already in scope on the extension or group.
            /// </summary>
            CheckerExtensionTypeParameterShadowed = 4366,

            /// <summary>
            /// Two groups in one extension can apply to the same receiver and declare the same method or operator.
            /// </summary>
            CheckerExtensionOverlappingMember = 4367,

            /// <summary>
            /// <c>static::</c> or <c>parent::</c> appears in an extension member.
            /// </summary>
            CheckerExtensionRelativeType = 4368,

            /// <summary>
            /// The member writes <c>$this</c> and does not declare the <c>&amp;$this</c> receiver annotation.
            /// </summary>
            CheckerExtensionByRefReceiverRequired = 4369,

            /// <summary>
            /// The member declares <c>&amp;$this</c> and never writes <c>$this</c>.
            /// </summary>
            CheckerExtensionByRefReceiverUnused = 4370,

            /// <summary>
            /// <c>#[\Tyhp\Php]</c> on a property, class constant, enum case, or interface method in
            /// Tyhp source. PHP cannot declare those conditionally, so the whole type has to be
            /// declared once per PHP version instead.
            /// </summary>
            CheckerPhpVersionAttributeInvalidMember = 4371,

            /// <summary>
            /// A Tyhp-facing name is one PHP 8.6 reserves: <c>let</c> or <c>is</c> as a class,
            /// interface, trait, enum, function, or constant; <c>namespace</c> as a constant;
            /// <c>readonly</c> as a function; or <c>_</c> as a constant or compile-time alias.
            /// The PHP original may keep that spelling when a tyhpdef <c>as</c> alias is the name
            /// Tyhp code uses. Methods and properties are not reserved.
            /// </summary>
            CheckerReservedPhp86Name = 4372,

            // 4373–4399 reserved for Stories 25, 26, 27 follow-ups

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

            /// <summary>Source map JSON could not be generated for an output file.</summary>
            EmitterSourceMapGenerationFailed = 5020,

            /// <summary>A <c>.map</c> file could not be written to disk.</summary>
            EmitterSourceMapWriteFailed = 5021,

            /// <summary>A mapping references an invalid original source position.</summary>
            EmitterSourceMapInvalidMapping = 5022,

            /// <summary>
            /// <c>#[\Tyhp\GenericRuntime]</c> <c>layouts</c> have no intersection with the
            /// layouts this compiler implements (currently <c>[1]</c>).
            /// </summary>
            EmitterGenericRuntimeLayoutUnsupported = 5023,

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

            /// <summary>A <c>{name}</c>-style placeholder in <c>tyhp.json</c> could not be expanded.</summary>
            ConfigInterpolationFailed = 6009,

            #endregion Configuration (6000–6999)

            #region CLI — Shared / Generic / install action (7000–7099)

            /// <summary>Unknown <c>tyhp install</c> target.</summary>
            InstallUnknownTarget = 7000,

            /// <summary>PHP is not on PATH; the Composer installer is a PHAR.</summary>
            InstallPhpNotFound = 7001,

            /// <summary><c>--local</c> and <c>--global</c> were both given.</summary>
            InstallConflictingLocationFlags = 7002,

            /// <summary>Composer installer download failed.</summary>
            InstallComposerDownloadFailed = 7003,

            /// <summary>Composer installer SHA-384 does not match the published signature.</summary>
            InstallComposerChecksumMismatch = 7004,

            /// <summary>Running <c>php composer-setup.php</c> failed.</summary>
            InstallComposerSetupFailed = 7005,

            /// <summary>Composer is not found at the destination after a successful-looking install.</summary>
            InstallComposerNotFoundAfterInstall = 7006,

            #endregion CLI — Shared / Generic / install action (7000–7099)

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

            /// <summary>Copying <c>output.publishContent</c> into the publish directory failed.</summary>
            BuildPublishContentFailed = 7106,

            /// <summary>An <c>output.publishContent</c> src pattern matched no files.</summary>
            BuildPublishContentUnmatched = 7107,

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

            /// <summary>Generic language server error.</summary>
            LspUnknownError = 7300,

            /// <summary>Failed to start the LSP server.</summary>
            LspServerStartupFailed = 7301,

            /// <summary>Error during document analysis.</summary>
            LspAnalysisError = 7302,

            /// <summary>Error loading a sourcemap for workspace analysis.</summary>
            LspSourceMapLoadError = 7303,

            #endregion CLI — language_server action (7300–7399)

            #region CLI — xdebug_proxy action (7400–7499)

            /// <summary>Generic XDebug proxy error.</summary>
            ProxyUnknownError = 7400,

            /// <summary>No sourcemap file found for a given PHP file (or none in the map directory).</summary>
            ProxySourceMapNotFound = 7401,

            /// <summary>Sourcemap JSON is invalid or malformed.</summary>
            ProxySourceMapParseError = 7402,

            /// <summary>TCP connection error.</summary>
            ProxyConnectionFailed = 7403,

            /// <summary>IDE or XDebug side did not connect within the pairing timeout.</summary>
            ProxySessionPairingTimeout = 7404,

            /// <summary>Error translating a DBGp message (non-fatal).</summary>
            ProxyTranslationError = 7405,

            /// <summary>Received a malformed DBGp message.</summary>
            ProxyInvalidDbgpMessage = 7406,

            /// <summary>Configured port is already bound by another process.</summary>
            ProxyPortInUse = 7407,

            #endregion CLI — xdebug_proxy action (7400–7499)

            #region CLI — generate_tyhpdef action (7500–7599)

            /// <summary>Error during tyhpdef generation.</summary>
            TyhpdefGenerationError = 7500,

            /// <summary>PHP runtime not found for <c>--ext-name</c>.</summary>
            TyhpdefPhpNotFound = 7501,

            /// <summary>PHP source file failed to parse during tyhpdef generation.</summary>
            TyhpdefSourceParseError = 7502,

            /// <summary>Failed to write tyhpdef output file.</summary>
            TyhpdefOutputWriteError = 7503,

            /// <summary>Failed to parse PHPDoc comment block.</summary>
            TyhpdefPhpDocParseError = 7504,

            /// <summary>Library project contains entrypoint file(s) with root-level side-effect statements.</summary>
            TyhpdefLibraryEntrypointDetected = 7505,

            /// <summary><c>--verify</c> found an existing tyhpdef that is not compatible with the generated golden PHP signature.</summary>
            TyhpdefVerifyIncompatible = 7506,

            /// <summary><c>--source</c> / <c>--package-path</c> collected a non-<c>.php</c> file (including <c>.tyhp</c> / <c>.tyhpdef</c>).</summary>
            TyhpdefSourceNotPhp = 7507,

            /// <summary>Managed PHP download failed (network, unsupported RID, empty cache offline).</summary>
            TyhpdefPhpRuntimeDownloadFailed = 7508,

            /// <summary>Managed PHP artifact failed checksum.</summary>
            TyhpdefPhpRuntimeChecksumMismatch = 7509,

            /// <summary><c>--php</c> combined with <c>--php-targets</c>.</summary>
            TyhpdefPhpUserBinaryWithTargets = 7510,

            /// <summary>Managed PHP cannot load <c>--ext-name</c> (use <c>--php</c> or a hand tyhpdef).</summary>
            TyhpdefPhpExtensionNotProvisioned = 7511,

            /// <summary>Patch auto-update failed; last-known-good still used (warning).</summary>
            TyhpdefPhpRuntimeUpdateFailed = 7512,

            /// <summary>Layer 2 stub cache is missing; harvest skipped (warning unless <c>--require-stubs</c>).</summary>
            TyhpdefStubCacheMissing = 7513,

            /// <summary>Layer 2 stub cache is required (<c>--require-stubs</c>) but was not found.</summary>
            TyhpdefStubCacheRequired = 7514,

            /// <summary>Psalm and PHPStan disagree on a stub type; Layer 1 is kept (warning).</summary>
            TyhpdefStubCorpusDisagreement = 7515,

            /// <summary>
            /// Layer 2 stub harvest used a conventional template name that is not declared
            /// on the enclosing type or function (overlay not written).
            /// </summary>
            TyhpdefStubUndeclaredTemplate = 7521,

            /// <summary>
            /// One PHP source file failed to parse during harvest; remaining files continue.
            /// </summary>
            TyhpdefSourceFileSkipped = 7522,

            /// <summary>
            /// An installed PHP package already has <c>extra.tyhp.package</c>, and
            /// <c>tyhpdef/&lt;vendor&gt;-&lt;name&gt;-impl</c> is also installed. Bind the PHP package.
            /// </summary>
            TyhpdefBundledPackagePreferred = 7523,

            /// <summary><c>vendor/composer/installed.json</c> is missing (<c>--vendor</c>).</summary>
            TyhpdefVendorInstalledJsonMissing = 7516,

            /// <summary><c>installed.json</c> is not valid Composer 2 JSON.</summary>
            TyhpdefVendorInstalledJsonInvalid = 7517,

            /// <summary>One package or extension failed during <c>--vendor</c> (warning; others continue).</summary>
            TyhpdefVendorCandidateFailed = 7518,

            /// <summary>Batched Composer require of companions / runtime packages failed (warning).</summary>
            TyhpdefVendorComposerRequireFailed = 7519,

            /// <summary>Every <c>--vendor</c> candidate failed.</summary>
            TyhpdefVendorAllCandidatesFailed = 7520,

            #endregion CLI — generate_tyhpdef action (7500–7599)

            #region CLI — init action (7600–7699)

            /// <summary>Existing root <c>composer.json</c> is not valid JSON.</summary>
            InitComposerJsonInvalid = 7600,

            /// <summary>Existing <c>require.php</c> cannot satisfy the chosen PHP version.</summary>
            InitPhpConstraintUnsatisfied = 7601,

            /// <summary>Existing root <c>composer.json</c> is JSON but not an object.</summary>
            InitComposerJsonNotObject = 7602,

            #endregion CLI — init action (7600–7699)

            #region CLI — composer action (7700–7799)

            /// <summary>Disjoint <c>extra.tyhp.require</c> constraint versus an existing root pin.</summary>
            ComposerDisjointExtraRequire = 7700,

            /// <summary>
            /// Stale extras: a named package from an installed <c>extra.tyhp.require</c> (or a
            /// root <c>tyhp/*</c> pin) is missing from root require-dev / is not installed.
            /// </summary>
            ComposerStaleExtraRequire = 7701,

            /// <summary>
            /// <c>tyhp/compiler</c> is missing from root require-dev (or not installed) while
            /// <c>tyhp/core</c> is in the graph. Warning on the native CLI; error under
            /// <c>--strict</c> / <c>build.strictMode</c>.
            /// </summary>
            ComposerMissingCompilerPin = 7702,

            /// <summary>Plugin or sync could not parse or write root <c>composer.json</c>.</summary>
            ComposerRootJsonWriteFailed = 7703,

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

            #region CLI — overlay action (7900–7999)

            /// <summary>Invalid <c>tyhp overlay</c> subcommand or missing required argument.</summary>
            OverlayInvalidArguments = 7900,

            /// <summary><c>tyhp overlay create</c> / <c>stamp</c> could not find the requested FQN.</summary>
            OverlayTargetNotFound = 7901,

            /// <summary>Failed to update <c>composer.json</c> <c>extra.tyhp.package</c> or <c>tyhp.json</c> overlay globs.</summary>
            OverlayManifestUpdateFailed = 7902,

            /// <summary>Failed to write an overlay <c>.tyhpdef</c> file.</summary>
            OverlayWriteFailed = 7903,

            /// <summary>
            /// <c>tyhp overlay create</c> targeted an <c>extern</c> type. Create copies a
            /// real declaration; it must not write a hollow <c>class { }</c> overlay.
            /// </summary>
            OverlayCreateOnExtern = 7904,

            /// <summary>
            /// <c>tyhp overlay stamp</c> could not obtain the managed PHP runtime for one
            /// supported version. That version's pass is skipped; its declarations are not
            /// stamped from a different PHP version.
            /// </summary>
            OverlayPhpRuntimeUnavailable = 7905,

            #endregion CLI — overlay action (7900–7999)

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
            /// A tyhpdef <c>extension operator</c> was declared without a thin <c>=&gt;</c>
            /// expression. Bodyless <c>operator …;</c> (no <c>extension</c>) means native PHP
            /// passthrough; mapped overloads require <c>extension operator … =&gt; expr;</c>.
            /// </summary>
            TyhpdefExtensionOperatorRequiresBody = 8013,

            /// <summary>
            /// Base / include <c>partial</c> names a type that is not already in this
            /// compilation's include set.
            /// </summary>
            TyhpdefPartialTargetNotFound = 8014,

            /// <summary>
            /// A tyhpdef property hook still has a body after parse (visitor regression;
            /// the grammar only admits bodyless <c>get;</c> / <c>set;</c>).
            /// </summary>
            TyhpdefPropertyHookBodyNotAllowed = 8015,

            /// <summary>
            /// <c>#[\Tyhp\Php]</c> was placed on an individual tyhpdef property hook
            /// (<c>get</c> / <c>set</c>). Version gates belong on the property or an
            /// enclosing <c>declare(php=…)</c>.
            /// </summary>
            TyhpdefPhpVersionGateOnPropertyHook = 8016,

            /// <summary><c>omit</c> was used in a non-overlay tyhpdef (include / baseline).</summary>
            TyhpdefOmitOutsideOverlay = 8017,

            /// <summary>
            /// <c>partial</c>, <c>omit</c>, <c>deprecated</c>, <c>obsolete</c>, and
            /// <c>extern</c> cannot be combined on the same declaration.
            /// <c>fallback</c> cannot be combined with <c>partial</c>, <c>omit</c>, or <c>extern</c>.
            /// </summary>
            TyhpdefIllegalKeywordCombination = 8018,

            /// <summary>
            /// Overlay <c>partial</c> names a type that is not already in the environment.
            /// Warning; the partial is skipped.
            /// </summary>
            TyhpdefOverlayPartialTargetNotFound = 8019,

            /// <summary>Overlay <c>omit</c> names a symbol that is not in the environment.</summary>
            TyhpdefOmitMissingSymbol = 8020,

            /// <summary>
            /// <c>// @overlay-against:</c> does not match the current Layer 1 baseline signature.
            /// Warning by default; error under <c>--strict</c> / <c>build.strictMode</c>.
            /// </summary>
            TyhpdefOverlayStampMismatch = 8021,

            /// <summary>
            /// An overlay replaces an existing symbol without a stamp and is not a
            /// compile-time-compatible rewrite of the Layer 1 baseline.
            /// </summary>
            TyhpdefOverlayIncompatibleReplace = 8022,

            /// <summary>The same fully-qualified type name is defined in more than one Composer package.</summary>
            TyhpdefDuplicateFqnAcrossPackages = 8025,

            /// <summary>The PHP extension Composer package for the configured PHP version was not found.</summary>
            TyhpdefPhpExtensionPackageNotFound = 8026,

            /// <summary>A Tyhp runtime Composer package was not found.</summary>
            TyhpdefRuntimePackageNotFound = 8027,

            /// <summary>
            /// An <c>extern</c> declaration is not name-only (body, members, generics,
            /// <c>extends</c> / <c>implements</c> on the placeholder, <c>as</c> alias,
            /// <c>extern trait</c>, or a signature/body on <c>extern function</c> / <c>const</c>).
            /// </summary>
            TyhpdefExternIllegalDeclaration = 8028,

            /// <summary>
            /// An <c>extern</c> type placeholder conflicts in kind with a real declaration
            /// or another <c>extern</c> of the same name (e.g. <c>extern class</c> vs
            /// <c>interface</c>). Functions, constants, and types occupy separate PHP name
            /// spaces, so <c>extern function</c> / <c>extern const</c> never conflict in kind —
            /// they only merge (real-wins) or coexist with a type of the same FQCN.
            /// </summary>
            TyhpdefExternKindMismatch = 8029,

            /// <summary>
            /// Overlay or include <c>partial</c> targeted an <c>extern</c> name.
            /// </summary>
            TyhpdefPartialOnExtern = 8030,

            /// <summary>
            /// Overlay <c>partial function</c> names a function or method that is not
            /// already in the environment. Warning; the partial is skipped.
            /// </summary>
            TyhpdefPartialFunctionTargetNotFound = 8031,

            /// <summary>
            /// <c>partial function</c> was used in a non-overlay tyhpdef (include / baseline).
            /// </summary>
            TyhpdefPartialFunctionOutsideOverlay = 8032,

            /// <summary>
            /// <c>partial function</c> as a member was used outside an overlay <c>partial</c> type.
            /// </summary>
            TyhpdefPartialFunctionMemberOutsidePartialType = 8033,

            /// <summary>
            /// Header-only <c>partial class Name;</c> (semicolon form) was used in a
            /// non-overlay tyhpdef (include / baseline).
            /// </summary>
            TyhpdefPartialTypeHeaderOutsideOverlay = 8034,

            /// <summary>
            /// Overlay <c>partial class Foo;</c> has no generics, inheritance, <c>as</c>,
            /// attributes, or matching keep-for-alias in the same overlay file.
            /// </summary>
            TyhpdefPartialTypeHeaderNoEffect = 8035,

            /// <summary>
            /// Two or more packages <c>fallback</c>-declare the same function and
            /// Composer <c>autoload.files</c> order cannot choose a winner.
            /// </summary>
            TyhpdefFallbackOrderUnknown = 8036,

            /// <summary>
            /// A losing <c>fallback function</c> signature differs from the one Composer
            /// file order keeps. Warning; the later declaration is ignored.
            /// </summary>
            TyhpdefFallbackSignatureMismatch = 8037,

            /// <summary>
            /// An ordinary function is loaded after a <c>fallback function</c> of the
            /// same name, which is a fatal redeclaration in PHP.
            /// </summary>
            TyhpdefFallbackRedeclare = 8038,

            /// <summary><c>declare(ext=…)</c> was combined with another directive.</summary>
            TyhpdefExtDeclareNotAlone = 8039,

            /// <summary><c>declare(ext=…)</c> is not <c>name</c> or <c>!name</c>.</summary>
            TyhpdefExtDeclareInvalid = 8040,

            /// <summary>Two packages <c>fallback const</c> the same name and autoload order cannot choose.</summary>
            TyhpdefFallbackConstOrderUnknown = 8041,

            /// <summary>A losing <c>fallback const</c> differs from the one Composer file order keeps.</summary>
            TyhpdefFallbackConstMismatch = 8042,

            /// <summary>An ordinary const is loaded after a <c>fallback const</c> of the same name.</summary>
            TyhpdefFallbackConstRedeclare = 8043,

            #endregion Tyhpdef
        }
}