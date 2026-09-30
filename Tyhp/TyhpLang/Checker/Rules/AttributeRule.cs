using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Validates attribute class usage, targets, repeatability, and arguments.
    /// </summary>
    public sealed class AttributeRule : ICheckerRule
    {
        // Attributes are validated through their declaration node (the attribute target),
        // not through the PhpAttributeAst node itself. The orchestrator visits attributes
        // separately via CheckAttributes(); handling PhpAttributeAst here as well would both
        // double-report target-independent diagnostics and validate target-specific rules
        // (e.g. #[Override]) with the attribute as its own (wrong) target.
        //
        // Class members are intentionally absent: CheckObjectBody calls CheckMethod / CheckProperty /
        // CheckEnumCase / CheckClassConstants directly (not context.CheckNode), so member attributes
        // are validated via ValidateDeclarationAttributes from those paths. Registering them here
        // would double-fire if members were ever routed through CheckNode while still calling those.
        //
        // Top-level / namespace <c>const</c> lists <em>are</em> registered: they go through
        // CheckNode (not CheckClassConstants), and PHP 8.5 attributes on them need TARGET_CONSTANT.
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpFunctionDeclAst),
            typeof(PhpObjectTypeDeclAst),
            typeof(PhpConstDeclListAst),
            typeof(PhpInlineFunctionAst),
            typeof(TyhpdefImportFunctionDeclAst),
        ];

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            ValidateDeclarationAttributes(node, state, context, diagnostics);
            if (node is PhpInlineFunctionAst closure)
            {
                ValidateParameterAttributes(closure.Parameters, state, context, diagnostics);
            }
            else if (node is TyhpdefImportFunctionDeclAst tyhpdefFunction)
            {
                ValidateParameterAttributes(tyhpdefFunction.Parameters, state, context, diagnostics);
            }
        }

        /// <summary>
        /// Validates attributes attached to a declaration target. Used by <see cref="Check"/> for
        /// nodes dispatched via <c>CheckNode</c>, and explicitly from the <c>CheckObjectBody</c>
        /// member paths for class members that bypass the dispatcher.
        /// </summary>
        public static void ValidateDeclarationAttributes(
            IBase2Ast target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            foreach (var attribute in target.AstAttributes)
            {
                if (attribute is PhpAttributeAst attr)
                {
                    ValidateAttribute(attr, target, state, context, diagnostics);
                }
            }
        }

        private static void ValidateAttribute(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            ValidateAttributeClass(attribute, state, diagnostics);
            if (PhpTypeAttributeSupport.IsPhpTypeAttribute(attribute))
            {
                ValidatePhpTypeAttribute(attribute, target, state, diagnostics);
                ValidateAttributeArguments(attribute, state, diagnostics);
                ValidateRepeatability(attribute, target, state, diagnostics);
                return;
            }

            if (NativeTypeTestAttributeSupport.IsNativeTypeTestAttribute(attribute))
            {
                if (!NativeTypeTestAttributeSupport.IsLegalAttributeHost(target))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerNativeTypeTestInvalidTarget,
                        DescribeTarget(target, state));
                }

                ValidateAttributeArguments(attribute, state, diagnostics);
                ValidateRepeatability(attribute, target, state, diagnostics);
                return;
            }

            if (EraseGenericAttributeSupport.IsEraseGenericAttribute(attribute))
            {
                if (!EraseGenericAttributeSupport.IsLegalTarget(target))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerEraseGenericInvalidTarget,
                        DescribeTarget(target, state));
                }

                ValidateAttributeArguments(attribute, state, diagnostics);
                ValidateRepeatability(attribute, target, state, diagnostics);
                return;
            }

            if (GenericRuntimeAttributeSupport.IsGenericRuntimeAttribute(attribute)
                && !IsTyhpdefAttributeHost(target, state))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerGenericRuntimeAuthorWritten);
            }

            ValidateAttributeTarget(attribute, target, state, context, diagnostics);
            ValidateAttributeArguments(attribute, state, diagnostics);
            ValidateRepeatability(attribute, target, state, diagnostics);
            ValidateOverride(attribute, target, state, context, diagnostics);
        }

        private static void ValidateParameterAttributes(
            PhpParameterListAst? parameters,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (parameters is null)
            {
                return;
            }

            foreach (var parameter in parameters.GetAllNotNull())
            {
                ValidateDeclarationAttributes(parameter, state, context, diagnostics);
            }
        }

        private static void ValidatePhpTypeAttribute(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!PhpTypeAttributeSupport.IsAllowedTarget(target))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerPhpTypeInvalidTarget,
                    DescribeTarget(target, state));
                return;
            }

            if (!PhpTypeAttributeSupport.TryReadTypeArgument(attribute, out var spelling)
                || !PhpTypeAttributeSupport.IsValidPhpTypeHint(spelling))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerPhpTypeInvalidSpelling,
                    string.IsNullOrEmpty(spelling) ? "<none>" : spelling);
            }
        }

        private static void ValidateAttributeClass(
            PhpAttributeAst attribute,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol attributeClass })
            {
                if (!IsAttributeClass(attributeClass)
                    && !IsBuiltInAttributeAllowingMissingMeta(attributeClass.Name)
                    && !IsBuiltInAttributeAllowingMissingMeta(attributeClass.FullyQualifiedName))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerNotAnAttributeClass,
                        attributeClass.Name);
                }

                return;
            }

            // `\Attribute` / `\Override` may not bind during package bootstrap (Override is also
            // absent from the 8.2 ExtCore stub). Once the binder has run, user attribute classes
            // take the BoundSymbol branch above; this fallback is only for those built-ins when
            // still unbound.
            var attributeName = GetAttributeName(attribute.Name);
            if (IsAttributeClassName(attributeName) || IsBuiltInAttributeAllowingMissingMeta(attributeName))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                attribute,
                MessageCode.CheckerNotAnAttributeClass,
                attributeName ?? "?");
        }

        private static void ValidateAttributeTarget(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var attributeName = GetAttributeName(attribute.Name);
            if (attributeName is null)
            {
                return;
            }

            // PHP 8.3+: methods. PHP 8.5+: properties (including promoted constructor params).
            // Below 8.5 a property is still a target mismatch, even though the Core stub's
            // `#[Attribute(12)]` already lists TARGET_PROPERTY.
            if (IsOverrideAttribute(attribute) && !IsLegalOverrideTarget(target, context))
            {
                ReportAttributeTargetMismatch(
                    attribute,
                    target,
                    state,
                    context,
                    diagnostics,
                    attributeName);
                return;
            }

            if (IsAllowUnsetAttribute(attribute)
                && target is not PhpPropertyDeclAst
                && target is not PhpParameterAst)
            {
                // \AllowUnset is Tyhp-only, not a PHP Core engine attribute — never skipped by
                // #[\DelayedTargetValidation].
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerAttributeTargetMismatch,
                    attributeName,
                    DescribeTarget(target, state));
                return;
            }

            // Built-in attributes without ExtCore meta (Override / AllowUnset / NoDiscard) rely on
            // the special cases above; skip symbol-driven TARGET_* when there is no BoundSymbol meta.
            if (attribute.Name is not PhpNameAst { BoundSymbol: ObjectDeclarationSymbol attributeClass })
            {
                return;
            }

            if (!TryGetDeclaredTargetFlags(attributeClass, out var allowedFlags))
            {
                // Unresolvable / missing meta (stub Attribute class, unbound flags expr): do not
                // invent a mismatch — repeatability / class checks already ran.
                return;
            }

            var requiredFlag = RequiredTargetFlag(target, state);
            if (requiredFlag is null)
            {
                return;
            }

            if ((allowedFlags & requiredFlag.Value) == 0)
            {
                ReportAttributeTargetMismatch(
                    attribute,
                    target,
                    state,
                    context,
                    diagnostics,
                    attributeName);
            }
        }

        /// <summary>
        /// TYHP4127 unless <c>#[\DelayedTargetValidation]</c> is on this declaration, the
        /// attribute is a PHP Core engine class, and <c>output.phpVersion</c> is ≥ 8.5.
        /// Functional checks (TYHP4129 / TYHP4165) are not gated here.
        /// </summary>
        private static void ReportAttributeTargetMismatch(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            string attributeName)
        {
            if (ShouldSkipCoreAttributeTargetMismatch(attribute, target, context))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                attribute,
                MessageCode.CheckerAttributeTargetMismatch,
                attributeName,
                DescribeTarget(target, state));
        }

        /// <summary>
        /// PHP 8.5 <c>#[\DelayedTargetValidation]</c> defers TARGET_* errors for <em>internal</em>
        /// (Core) attributes on the same declaration until <c>ReflectionAttribute::newInstance()</c>.
        /// Userland attributes are still checked. Below 8.5 the skip is ignored.
        /// </summary>
        private static bool ShouldSkipCoreAttributeTargetMismatch(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerRuleContext context)
            => IsPhpVersionAtLeast85(context.Options.PhpVersion)
                && IsPhpCoreEngineAttribute(attribute)
                && DeclarationHasCoreDelayedTargetValidation(target);

        private static bool DeclarationHasCoreDelayedTargetValidation(IBase2Ast target)
        {
            foreach (var node in target.AstAttributes)
            {
                if (node is PhpAttributeAst attr && IsCoreDelayedTargetValidationAttribute(attr))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCoreDelayedTargetValidationAttribute(PhpAttributeAst attribute)
            => MatchesGlobalEngineAttributeName(
                   GetBoundOrWrittenAttributeName(attribute),
                   "DelayedTargetValidation")
               && IsPhpCoreEngineAttribute(attribute);

        /// <summary>
        /// PHP Core engine attribute classes (<c>\Override</c>, <c>\Deprecated</c>, …). Namespaced
        /// or userland <c>.tyhp</c> shadows are not Core.
        /// </summary>
        private static bool IsPhpCoreEngineAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol obj })
            {
                if (!MatchesAnyGlobalEngineAttributeName(obj.FullyQualifiedName)
                    && !MatchesAnyGlobalEngineAttributeName(obj.Name))
                {
                    return false;
                }

                return IsEngineDeclarationSource(obj.SourceFile);
            }

            return MatchesAnyGlobalEngineAttributeName(GetAttributeName(attribute.Name));
        }

        private static string? GetBoundOrWrittenAttributeName(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol obj })
            {
                return obj.FullyQualifiedName ?? obj.Name;
            }

            return GetAttributeName(attribute.Name);
        }

        private static bool MatchesAnyGlobalEngineAttributeName(string? name)
            => MatchesGlobalEngineAttributeName(name, "Override")
                || MatchesGlobalEngineAttributeName(name, "Deprecated")
                || MatchesGlobalEngineAttributeName(name, "NoDiscard")
                || MatchesGlobalEngineAttributeName(name, "SensitiveParameter")
                || MatchesGlobalEngineAttributeName(name, "ReturnTypeWillChange")
                || MatchesGlobalEngineAttributeName(name, "DelayedTargetValidation")
                || MatchesGlobalEngineAttributeName(name, "Attribute")
                || MatchesGlobalEngineAttributeName(name, "AllowDynamicProperties");

        /// <summary>
        /// True when <paramref name="name"/> is exactly the global engine class
        /// (leading <c>\</c> optional). <c>App\Override</c> does not match <c>Override</c>.
        /// </summary>
        private static bool MatchesGlobalEngineAttributeName(string? name, string engineName)
        {
            if (name is null)
            {
                return false;
            }

            var bare = name.StartsWith('\\') ? name.AsSpan(1) : name.AsSpan();
            return bare.Equals(engineName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Engine / stub sources. A user <c>.tyhp</c> class that reuses a Core name is userland.
        /// Unbound names (empty source) are treated as the engine class — isolated tests have no
        /// ExtCore <c>Override</c> / <c>DelayedTargetValidation</c> stub. Paths under
        /// <c>runtime/packages/php/</c>, <c>/packages/php/</c>, or the resolved runtime-src
        /// <c>php</c> package are engine sources.
        /// </summary>
        private static bool IsEngineDeclarationSource(string? sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return true;
            }

            if (sourceFile.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || sourceFile.StartsWith("<tyhpdef:", StringComparison.OrdinalIgnoreCase)
                || sourceFile.StartsWith("<embedded>", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return RuntimePackagePaths.IsEnginePhpPackageSource(sourceFile);
        }

        /// <summary>
        /// What the author wrote the attribute on, in their vocabulary. The AST class name is only a
        /// last resort — it is the kind of internal detail that should not reach a diagnostic.
        /// </summary>
        private static string DescribeTarget(IBase2Ast target, CheckerState state) =>
            target switch
            {
                PhpMethodDeclAst => "method",
                PhpPropertyDeclAst => "property",
                PhpParameterAst => "parameter",
                PhpFunctionDeclAst => "function",
                // Top-level / namespace compile-time const vs class/enum const (TARGET_CONSTANT vs
                // TARGET_CLASS_CONSTANT). EnclosingObject is set only inside CheckObjectType.
                PhpConstDeclListAst =>
                    state.EnclosingObject is not null ? "class constant" : "constant",
                PhpEnumCaseAst => "enum case",
                PhpObjectTypeDeclAst objectType =>
                    objectType.DeclType?.ValueString?.ToLowerInvariant() ?? "class",
                PhpPropertyHookAst => "property hook",
                PhpCatchClauseAst => "catch",
                TyhpTypedVarExprAst => "local variable",
                PhpInlineFunctionAst => "function",
                TyhpStructDeclAst => "struct",
                TyhpTypeAliasAst => "type alias",
                _ => target.GetType().Name,
            };

        private static bool IsTyhpdefAttributeHost(IBase2Ast target, CheckerState state)
        {
            if (string.Equals(target.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
                || target.OwningFile is TyhpdefSrcFileAst)
            {
                return true;
            }

            var file = state.CurrentFileName ?? target.OwningFile?.Identifier ?? "";
            return file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// PHP <c>Attribute::TARGET_*</c> bit required for <paramref name="target"/>, or null when
        /// this rule does not validate that kind of declaration.
        /// Promoted constructor parameters are one declaration that is both a parameter and a
        /// property: an attribute is legal when it has either <c>TARGET_PARAMETER</c> or
        /// <c>TARGET_PROPERTY</c>. Version gating for <c>#[\Override]</c> on properties lives in
        /// <see cref="IsLegalOverrideTarget"/>, not here.
        /// </summary>
        private static long? RequiredTargetFlag(IBase2Ast target, CheckerState state) =>
            target switch
            {
                PhpMethodDeclAst => AttributeTargetMethod,
                PhpPropertyDeclAst => AttributeTargetProperty,
                PhpParameterAst parameter when IsPromotedConstructorParameter(parameter)
                    => AttributeTargetParameter | AttributeTargetProperty,
                PhpParameterAst => AttributeTargetParameter,
                PhpFunctionDeclAst => AttributeTargetFunction,
                PhpInlineFunctionAst => AttributeTargetFunction,
                PhpEnumCaseAst => AttributeTargetClassConstant,
                PhpConstDeclListAst =>
                    state.EnclosingObject is not null
                        ? AttributeTargetClassConstant
                        : AttributeTargetConstant,
                PhpObjectTypeDeclAst => AttributeTargetClass,
                TyhpTypeAliasAst => AttributeTargetClass,
                _ => null,
            };

        /// <summary>
        /// Reads the <c>#[Attribute(...)]</c> flags bitmask from the attribute class declaration.
        /// Returns false when there is no meta to read or the flags expression cannot be evaluated
        /// statically (caller skips TARGET_* checking rather than guessing).
        /// </summary>
        private static bool TryGetDeclaredTargetFlags(
            ObjectDeclarationSymbol attributeClass,
            out long flags)
        {
            flags = AttributeTargetAll;

            if (attributeClass.DeclaringAstNode is not IBase2Ast declaringNode)
            {
                // Stub / reflection-only attribute classes (e.g. ExtCore <c>Attribute</c>) have no
                // declaring AST. Built-in special cases already returned; treat as TARGET_ALL so
                // <c>#[\Attribute]</c> on a class is not rejected.
                return true;
            }

            PhpAttributeAst? meta = null;
            foreach (var attr in declaringNode.AstAttributes.OfType<PhpAttributeAst>())
            {
                if (IsAttributeMetaAttribute(attr))
                {
                    meta = attr;
                    break;
                }
            }

            if (meta is null)
            {
                // Passed IsAttributeClass via name allow-list without meta — permissive.
                return true;
            }

            if (meta.Arguments is null || !meta.Arguments.GetAllNotNull().Any())
            {
                // <c>#[Attribute]</c> / <c>#[Attribute()]</c> → constructor default TARGET_ALL.
                flags = AttributeTargetAll;
                return true;
            }

            // First constructor argument is the flags bitmask (named or positional).
            var flagsArgument = meta.Arguments.GetAllNotNull().First();
            if (!TryEvaluateAttributeFlagsExpression(flagsArgument.Expression, out flags))
            {
                return false;
            }

            return true;
        }

        private static bool TryEvaluateAttributeFlagsExpression(IExpression? expression, out long flags)
        {
            flags = 0;
            switch (expression)
            {
                case null:
                    flags = AttributeTargetAll;
                    return true;

                case PhpBinaryOpAst binary when IsBitwiseOr(binary):
                    if (!TryEvaluateAttributeFlagsExpression(binary.Left, out var left)
                        || !TryEvaluateAttributeFlagsExpression(binary.Right, out var right))
                    {
                        return false;
                    }

                    flags = left | right;
                    return true;

                case PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst access } deref
                    when IsAttributeClassName(GetAttributeName(deref.Base as IExpression)):
                {
                    var constName = GetAttributeName(access.Member);
                    if (constName is null || !TryMapAttributeFlagConstant(constName, out flags))
                    {
                        return false;
                    }

                    return true;
                }

                case PhpScalarAst { ValueInt64: { } numeric }:
                    flags = numeric;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryMapAttributeFlagConstant(string name, out long flags)
        {
            // PHP 8.5 bit layout (zend_attributes.h). Names are case-insensitive like PHP.
            if (string.Equals(name, "TARGET_CLASS", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetClass;
                return true;
            }

            if (string.Equals(name, "TARGET_FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetFunction;
                return true;
            }

            if (string.Equals(name, "TARGET_METHOD", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetMethod;
                return true;
            }

            if (string.Equals(name, "TARGET_PROPERTY", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetProperty;
                return true;
            }

            if (string.Equals(name, "TARGET_CLASS_CONSTANT", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetClassConstant;
                return true;
            }

            if (string.Equals(name, "TARGET_PARAMETER", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetParameter;
                return true;
            }

            if (string.Equals(name, "TARGET_CONSTANT", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetConstant;
                return true;
            }

            if (string.Equals(name, "TARGET_ALL", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeTargetAll;
                return true;
            }

            if (string.Equals(name, "IS_REPEATABLE", StringComparison.OrdinalIgnoreCase))
            {
                flags = AttributeIsRepeatableFlagValue;
                return true;
            }

            flags = 0;
            return false;
        }

        private static void ValidateAttributeArguments(
            PhpAttributeAst attribute,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (attribute.Arguments is null)
            {
                return;
            }

            foreach (var argument in attribute.Arguments.GetAllNotNull())
            {
                if (argument.Expression is not null
                    && !CheckerHelpers.IsConstantExpression(argument.Expression, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        argument.Expression,
                        MessageCode.CheckerNonConstantExpression);
                }
            }
        }

        private static void ValidateRepeatability(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var attributeName = GetAttributeName(attribute.Name);
            if (attributeName is null)
            {
                return;
            }

            var sameAttributes = target.AstAttributes
                .OfType<PhpAttributeAst>()
                .Where(a => string.Equals(GetAttributeName(a.Name), attributeName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sameAttributes.Count <= 1)
            {
                return;
            }

            // One diagnostic per repeated group, pointed at the first duplicate — not once per copy.
            if (!ReferenceEquals(attribute, sameAttributes[1]))
            {
                return;
            }

            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol attributeClass }
                && IsRepeatableAttributeClass(attributeClass))
            {
                return;
            }

            // Unbound built-ins never carry IS_REPEATABLE in ExtCore; treat them as non-repeatable.
            CheckerHelpers.ReportError(
                diagnostics, state, attribute, MessageCode.CheckerAttributeNotRepeatable, attributeName);
        }

        private static void ValidateOverride(
            PhpAttributeAst attribute,
            IBase2Ast target,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!IsOverrideAttribute(attribute))
            {
                return;
            }

            // Constructors are exempt from override semantics at every phpVersion. Report even
            // inside a trait (no composing-class walk is needed) and even when a parent
            // constructor exists — this is not TYHP4129.
            if (target is PhpMethodDeclAst constructorCandidate && IsConstructorMethod(constructorCandidate))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, attribute, MessageCode.CheckerOverrideOnConstructor);
                return;
            }

            // A non-method / non-property (at this phpVersion) target is already a mismatch.
            if (target is not PhpMethodDeclAst
                && target is not PhpPropertyDeclAst
                && !IsPromotedConstructorParameter(target))
            {
                return;
            }

            if (target is PhpPropertyDeclAst or PhpParameterAst
                && !IsPhpVersionAtLeast85(context.Options.PhpVersion))
            {
                return;
            }

            // PHP resolves a trait's `#[Override]` against the composing class, so the trait
            // declaration itself has nothing to compare against. A missing enclosing object
            // leaves nothing to walk either — stay quiet rather than guess.
            if (state.EnclosingObject is null
                || state.EnclosingObject.ObjectKind == PhpTypeDeclType.Trait)
            {
                return;
            }

            if (target is PhpMethodDeclAst method)
            {
                if (method.BoundSymbol is not ObjectMethodSymbol methodSymbol)
                {
                    return;
                }

                if (!OverridesInheritedMethod(methodSymbol, state.EnclosingObject, context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerOverrideNotOverriding,
                        methodSymbol.Name);
                }

                return;
            }

            foreach (var propertyName in EnumerateOverridePropertyNames(target))
            {
                if (!OverridesInheritedProperty(propertyName, state.EnclosingObject, context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerOverrideNotOverriding,
                        FormatPropertyName(propertyName));
                }
            }
        }

        private static bool IsConstructorMethod(PhpMethodDeclAst method) =>
            method.BoundSymbol is ObjectConstructorMethodSymbol
            || string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase);

        private static bool IsLegalOverrideTarget(IBase2Ast target, CheckerRuleContext context)
        {
            if (target is PhpMethodDeclAst)
            {
                return true;
            }

            if (!IsPhpVersionAtLeast85(context.Options.PhpVersion))
            {
                return false;
            }

            return target is PhpPropertyDeclAst || IsPromotedConstructorParameter(target);
        }

        private static bool IsPromotedConstructorParameter(IBase2Ast target) =>
            target is PhpParameterAst { Modifiers: not null };

        private static bool IsPhpVersionAtLeast85(string? phpVersion)
        {
            var text = string.IsNullOrWhiteSpace(phpVersion)
                ? CompilationOptions.DefaultPhpVersionWhenUnset
                : phpVersion;
            return PhpVersion.TryParse(text, out var parsed) && parsed >= new PhpVersion(8, 5, 0);
        }

        private static IEnumerable<string> EnumerateOverridePropertyNames(IBase2Ast target)
        {
            if (target is PhpPropertyDeclAst propertyDecl)
            {
                foreach (var prop in propertyDecl.Properties?.GetAllNotNull() ?? [])
                {
                    var name = prop.Identifier ?? "";
                    if (string.IsNullOrEmpty(name) || string.Equals(name, "<error>", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return name;
                }

                yield break;
            }

            if (target is PhpParameterAst parameter)
            {
                var name = parameter.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    yield return name;
                }
            }
        }

        private static string FormatPropertyName(string name) =>
            name.StartsWith('$') ? name : "$" + name;

        private static bool IsAttributeClass(ObjectDeclarationSymbol symbol)
        {
            if (IsAttributeClassName(symbol.Name) || IsAttributeClassName(symbol.FullyQualifiedName))
            {
                return true;
            }

            if (symbol.DeclaringAstNode is not IBase2Ast declaringNode)
            {
                return false;
            }

            return declaringNode.AstAttributes
                .OfType<PhpAttributeAst>()
                .Any(IsAttributeMetaAttribute);
        }

        private static bool IsAttributeMetaAttribute(PhpAttributeAst attribute)
            => IsAttributeClassName(GetAttributeName(attribute.Name));

        private static bool IsAttributeClassName(string? name)
            => name is not null
                && (string.Equals(name, "Attribute", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\Attribute", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Built-in attribute classes that are valid attribute targets even when the stub has no
        /// <c>#[Attribute]</c> meta (or the class is missing from ExtCore entirely). Kept separate
        /// from <see cref="IsAttributeClassName"/> so <c>#[Override]</c> on a user class does not
        /// make that class look like an attribute class.
        /// </summary>
        private static bool IsBuiltInAttributeAllowingMissingMeta(string? name)
            => name is not null
                && (string.Equals(name, "Override", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\Override", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "AllowUnset", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\AllowUnset", StringComparison.OrdinalIgnoreCase)
                    // PHP 8.5; ExtCore stub lands in Story 21 — allow unbound use until then.
                    || string.Equals(name, "NoDiscard", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\NoDiscard", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "DelayedTargetValidation", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\DelayedTargetValidation", StringComparison.OrdinalIgnoreCase)
                    // PHP 8.4; class stays gated in tyhpdef, but use-site TYHP4500 is not.
                    || string.Equals(name, "Deprecated", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\Deprecated", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "NativeTypeTest", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\NativeTypeTest", StringComparison.OrdinalIgnoreCase)
                    // Compile-time PHP version gate (Story 20.5). Omitted variants skip bind,
                    // so the attribute name may have no BoundSymbol on the checker walk.
                    || PhpVersionRule.IsTyhpPhpFullyQualifiedName(name));

        private static bool IsAllowUnsetAttribute(PhpAttributeAst attribute)
        {
            var name = GetAttributeName(attribute.Name);
            return name is not null
                && (string.Equals(name, "AllowUnset", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\AllowUnset", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRepeatableAttributeClass(ObjectDeclarationSymbol symbol)
        {
            if (symbol.DeclaringAstNode is not IBase2Ast declaringNode)
            {
                return false;
            }

            foreach (var attr in declaringNode.AstAttributes.OfType<PhpAttributeAst>())
            {
                if (!IsAttributeMetaAttribute(attr))
                {
                    continue;
                }

                if (MetaAttributeFlagsIncludeIsRepeatable(attr))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the <c>#[Attribute(...)]</c> meta-attribute's flags argument mentions
        /// <c>Attribute::IS_REPEATABLE</c> (alone, in a bitwise-or, or as a numeric literal with the
        /// flag's bit set).
        /// </summary>
        private static bool MetaAttributeFlagsIncludeIsRepeatable(PhpAttributeAst metaAttribute)
        {
            if (metaAttribute.Arguments is null)
            {
                return false;
            }

            foreach (var argument in metaAttribute.Arguments.GetAllNotNull())
            {
                if (ExpressionMentionsIsRepeatable(argument.Expression))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionMentionsIsRepeatable(IExpression? expression) =>
            expression switch
            {
                null => false,
                PhpBinaryOpAst binary when IsBitwiseOr(binary) =>
                    ExpressionMentionsIsRepeatable(binary.Left)
                    || ExpressionMentionsIsRepeatable(binary.Right),
                PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst access } deref =>
                    IsIsRepeatableConstantName(access.Member)
                    && IsAttributeClassName(GetAttributeName(deref.Base as IExpression)),
                // PHP also accepts the flags as a plain integer. PHP 8.5: IS_REPEATABLE === 128
                // (TARGET_CONSTANT took bit 64); TARGET_ALL|IS_REPEATABLE is often written 255.
                PhpScalarAst { ValueInt64: { } flags } => (flags & AttributeIsRepeatableFlagValue) != 0,
                _ => false,
            };

        // PHP 8.5 zend_attributes.h bit layout.
        private const long AttributeTargetClass = 1L << 0;
        private const long AttributeTargetFunction = 1L << 1;
        private const long AttributeTargetMethod = 1L << 2;
        private const long AttributeTargetProperty = 1L << 3;
        private const long AttributeTargetClassConstant = 1L << 4;
        private const long AttributeTargetParameter = 1L << 5;
        private const long AttributeTargetConstant = 1L << 6;
        private const long AttributeTargetAll = (1L << 7) - 1;
        private const long AttributeIsRepeatableFlagValue = 1L << 7;

        private static bool IsBitwiseOr(PhpBinaryOpAst binary) =>
            binary.Operator is not null
            && PhpBinaryOperatorExtensions.FromToken(binary.Operator.TokenValue, binary.Operator.ValueString)
                == PhpBinaryOperator.BitwiseOr;

        private static bool IsIsRepeatableConstantName(IExpression? member)
        {
            var name = GetAttributeName(member);
            return name is not null
                && string.Equals(name, "IS_REPEATABLE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOverrideAttribute(PhpAttributeAst attribute)
        {
            var name = GetAttributeName(attribute.Name);
            return name is not null
                && (string.Equals(name, "Override", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("\\Override", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// True when the method overrides a base-class method or implements an interface method. PHP
        /// accepts <c>#[Override]</c> for both, so this walks the whole ancestor graph rather than only
        /// the <c>extends</c> chain.
        /// </summary>
        private static bool OverridesInheritedMethod(
            ObjectMethodSymbol methodSymbol,
            ObjectDeclarationSymbol enclosingObject,
            CheckerRuleContext context)
            => WalksInheritedMember(
                enclosingObject,
                context,
                ancestor =>
                    ancestor.Members.TryGetValue(methodSymbol.Name, out var member)
                    && member is ObjectMethodSymbol ancestorMethod
                    && (ancestorMethod.Visibility & MemberModifier.Private) == 0);

        /// <summary>
        /// True when a same-name non-private property exists on a parent class or implemented
        /// (or, for an interface, extended) interface. PHP 8.5 <c>#[Override]</c> on properties
        /// uses the same parent + interface graph as methods.
        /// </summary>
        private static bool OverridesInheritedProperty(
            string propertyName,
            ObjectDeclarationSymbol enclosingObject,
            CheckerRuleContext context)
            => WalksInheritedMember(
                enclosingObject,
                context,
                ancestor => AncestorHasNonPrivateProperty(ancestor, propertyName));

        /// <summary>
        /// BFS over <c>extends</c> / <c>implements</c> (and interface <c>extends</c>). A cyclic
        /// hierarchy is reported elsewhere but still reaches this walk, so the visited set is
        /// what stops it from spinning forever.
        /// </summary>
        private static bool WalksInheritedMember(
            ObjectDeclarationSymbol enclosingObject,
            CheckerRuleContext context,
            Func<ObjectDeclarationSymbol, bool> foundOnAncestor)
        {
            var visited = new HashSet<ObjectDeclarationSymbol> { enclosingObject };
            var pending = new Queue<ObjectDeclarationSymbol>();
            pending.Enqueue(enclosingObject);

            while (pending.Count > 0)
            {
                foreach (var ancestor in TypeComparer.EnumerateDirectAncestors(
                             pending.Dequeue(), context.SymbolTree, context.GlobalScope))
                {
                    if (!visited.Add(ancestor))
                    {
                        continue;
                    }

                    pending.Enqueue(ancestor);

                    if (foundOnAncestor(ancestor))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool AncestorHasNonPrivateProperty(
            ObjectDeclarationSymbol ancestor,
            string propertyName)
        {
            var bare = propertyName.StartsWith('$') ? propertyName[1..] : propertyName;
            if (string.IsNullOrEmpty(bare))
            {
                return false;
            }

            if (TryGetNonPrivateProperty(ancestor, "$" + bare)
                || TryGetNonPrivateProperty(ancestor, bare))
            {
                return true;
            }

            foreach (var candidate in ancestor.Members.Values.OfType<ObjectPropertySymbol>())
            {
                var candidateBare = candidate.Name.StartsWith('$') ? candidate.Name[1..] : candidate.Name;
                if (string.Equals(candidateBare, bare, StringComparison.OrdinalIgnoreCase)
                    && (candidate.Visibility & MemberModifier.Private) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetNonPrivateProperty(ObjectDeclarationSymbol ancestor, string key) =>
            ancestor.Members.TryGetValue(key, out var member)
            && member is ObjectPropertySymbol property
            && (property.Visibility & MemberModifier.Private) == 0;

        private static string? GetAttributeName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };
    }
}
