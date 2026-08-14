using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class DeclarationRule
    {
        private void CheckFunction(
            PhpFunctionDeclAst function,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (function.BoundSymbol is not FunctionDeclarationSymbol functionSymbol)
            {
                return;
            }

            // A named function declared inside another named function/method's body (however
            // deep — an intervening if/loop/try block does not change this). `state.EnclosingCallable`
            // is cleared whenever a File/Namespace/ObjectTypeDeclaration scope is entered, so a
            // function guarded by `if (!function_exists(...))` at file scope, or a method of a class
            // declared inside a function, is unaffected — only a literal function-in-function or
            // function-in-method nesting is rejected. Closures/arrow functions are unnamed and are not
            // affected by this rule. Reported alongside (not instead of) the normal checks below so a
            // deeper nested declaration inside the rejected function's own body is still flagged.
            if (state.EnclosingCallable is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, function, MessageCode.CheckerNestedNamedFunctionNotAllowed, functionSymbol.Name);
            }

            var funcState = state.Split(ScopeType.FunctionDeclaration);
            funcState.EnclosingFunction = functionSymbol;
            funcState.EnclosingCallable = functionSymbol;
            funcState.FunctionGenerics = functionSymbol.GenericParameters;
            GenericConstraintResolver.ResolveAll(functionSymbol.GenericParameters, funcState, context);
            var returnTypeAst = function.ReturnType ?? functionSymbol.ReturnType;
            if (returnTypeAst is TyhpReturnTypeGuardAst typeGuard)
            {
                TypeGuardValidation.ValidateGuardParameter(
                    typeGuard, function.Parameters, functionSymbol.Parameters, function, state, diagnostics);
                funcState.IsTypeGuardFunction = true;
            }

            funcState.ExpectedReturnType = TypeGuardValidation.ResolveExpectedReturnType(
                returnTypeAst, funcState, context);
            funcState.IsInAsyncContext = functionSymbol.IsAsync;
            funcState.IsInGeneratorContext = functionSymbol.IsGenerator;

            if (function.ReturnType is not null)
            {
                context.MarkImportNames(function.ReturnType, state);
            }

            RegisterParameters(function.Parameters, functionSymbol.Parameters, funcState, state, context, diagnostics);
            ValidateMagicMethodIfNeeded(function.Identifier, function.Parameters, function.ReturnType, false, function, state, context, diagnostics);
            RejectReservedGenericVariantName(function.Identifier, function, state, diagnostics);
            RejectReservedPropertyHookMethodName(function.Identifier, function, state, diagnostics);
            FlagGenericVariantIfNeeded(function.Body, functionSymbol, functionSymbol.GenericParameters, context);

            if (function.Body is not null)
            {
                funcState.HasReturnedOnAllPaths = false;
                context.CheckStatementBlock(function.Body, funcState);

                if (funcState.IsTypeGuardFunction && !funcState.HasReturnedOnAllPaths)
                {
                    TypeGuardValidation.ReportMustReturnBool(function, state, diagnostics);
                }
                else if (!IsEffectivelyVoid(funcState.ExpectedReturnType) && !funcState.HasReturnedOnAllPaths)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, function, MessageCode.CheckerMissingReturnStatement, function.Identifier);
                }

                context.RecordGenericCallTargetsIn(function.Body, funcState);
            }
        }

        /// <summary>
        /// Flags a callable for Mechanism D binder emission when its body uses one of its own generic
        /// parameters in a construct that needs the bound type at runtime. Driven from the declaration
        /// visit, which every function and method reaches, rather than from
        /// <see cref="CompileTimeRule"/>, whose coverage of expression positions is partial.
        /// </summary>
        private static void FlagGenericVariantIfNeeded(
            PhpStatementBlockAst? body,
            IBaseSymbol symbol,
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            CheckerRuleContext context)
        {
            if (generics.Count > 0 && CheckerHelpers.UsesGenericAtRuntime(body, generics))
            {
                context.MarkRequiresGenericVariant(symbol);
            }
        }

        /// <summary>
        /// Flags the enclosing class for <c>GenericObject</c> tracking when a method body reads one of
        /// the *class's* generic parameters at runtime. Driven from the declaration for the same reason
        /// as <see cref="FlagGenericVariantIfNeeded"/>: a position <see cref="CompileTimeRule"/> does
        /// not visit would emit a <c>tyhpGenericObjectGetGenericType</c> call on a class that never
        /// received the trait, which is a fatal error on first call rather than a wrong answer.
        ///
        /// A parameter the method declares itself shadows the class's, and is served by the Mechanism D
        /// binder instead, so those names are excluded. A static method has no instance to read the
        /// class generic's binding from — <c>typeof(T)</c>/<c>default(T)</c> reject that shape via
        /// TYHP4148/TYHP4152 in <see cref="CompileTimeRule"/>; <c>instanceof T</c>/<c>is T</c> get the
        /// same treatment here (TYHP4156) since they reify through the identical instance lookup
        /// (Prop-init #37) rather than through a dedicated AST node <c>CompileTimeRule</c> visits.
        /// </summary>
        private static void FlagRuntimeGenericTrackingIfNeeded(
            PhpStatementBlockAst? body,
            ObjectMethodSymbol methodSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (state.EnclosingObject is not { } owner)
            {
                return;
            }

            var shadowed = methodSymbol.GenericParameters.Select(gp => gp.Name).ToHashSet(StringComparer.Ordinal);
            var classGenerics = owner.GenericParameters.Where(gp => !shadowed.Contains(gp.Name)).ToList();

            if (methodSymbol.IsStatic)
            {
                CheckerHelpers.ForEachStaticContextGenericInstanceof(
                    body,
                    classGenerics,
                    (binary, genericName) => CheckerHelpers.ReportError(
                        diagnostics, state, binary, MessageCode.CheckerGenericInstanceofInStaticContext, genericName));
                return;
            }

            if (CheckerHelpers.UsesGenericAtRuntime(body, classGenerics))
            {
                context.MarkRequiresRuntimeGenericTracking(owner);
            }
        }

        /// <summary>
        /// Rejects a declared name that would collide with the generic variant emitted for a callable
        /// one suffix shorter. The collision is silent otherwise: two PHP symbols of the same name in
        /// one file, where the generated one wins.
        /// </summary>
        private static void RejectReservedGenericVariantName(
            string? identifier,
            IBase2Ast node,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (GeneratedNames.EndsWithGenericVariantSuffix(identifier))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerReservedGenericVariantSuffix,
                    identifier!, GeneratedNames.GenericVariantSuffix);
            }
        }

        private static void RejectReservedPropertyHookMethodName(
            string? identifier,
            IBase2Ast node,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (GeneratedNames.EndsWithPropertyHookMethodSuffix(identifier))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerReservedPropertyHookMethodSuffix,
                    identifier!, GeneratedNames.PropertyHookMethodSuffix);
            }
        }

        private void CheckMethod(
            PhpMethodDeclAst method,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Class members bypass CheckNode (CheckObjectBody calls us directly), so rules that
            // register PhpMethodDeclAst for free functions never dispatch here — invoke them
            // explicitly. Do not route through CheckNode: DeclarationRule would double-fire.
            AttributeRule.ValidateDeclarationAttributes(method, state, context, diagnostics);
            TypeAnnotationRule.CheckMethodReturnType(method, state, context);
            AsyncRule.ValidateAsyncMethod(method, state, context, diagnostics);
            CodeQualityRule.CheckMethodBody(method, state, diagnostics);
            DisposableRule.AnalyzeMethodBody(method, state, context, diagnostics);
            // Walk AstAttributes so ImportRule (and any name-based rules) see member attribute
            // names — the same CheckAttributes path free functions get via CheckNode.
            context.CheckAttributes(method, state);

            if (method.BoundSymbol is not ObjectMethodSymbol methodSymbol)
            {
                return;
            }

            var modifiers = CheckerHelpers.ToMemberModifiers(method.Modifiers);
            if (CheckerHelpers.CountVisibilityModifiers(modifiers) > 1)
            {
                CheckerHelpers.ReportError(
                    context, state, method, MessageCode.CheckerMultipleVisibilities, method.Identifier);
            }

            if ((modifiers & MemberModifier.Abstract) != 0 && (modifiers & MemberModifier.Final) != 0)
            {
                CheckerHelpers.ReportError(context, state, method, MessageCode.CheckerMemberModifierConflict, "abstract", "final");
            }

            if (state.EnclosingObject?.ObjectKind == PhpTypeDeclType.Enum
                && string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, method, MessageCode.CheckerEnumMethodNotAllowed, "__construct");
            }

            // `__construct`/`__destruct` can never declare a return type and PHP forbids `return <value>;`
            // inside them, so the missing-return-statement check further below (which treats an absent
            // annotation as `mixed`, not `void`) must not apply to them.
            var isConstructorOrDestructor =
                methodSymbol is ObjectConstructorMethodSymbol or ObjectDestructorMethodSymbol;

            // PHP does not require (or even allow enforcing) `__construct` to keep a signature
            // compatible with its parent's — a subclass is free to add, drop, retype, or reorder
            // constructor parameters, unlike every other overridden method. Applying the ordinary
            // override-compatibility check here would reject perfectly valid PHP such as
            // `Middle::__construct()` overriding `Base::__construct(bool $x = true)`. `final` is still
            // enforced: a parent may forbid overriding its constructor at all.
            CheckMethodOverride(
                method, methodSymbol, state, context, diagnostics, checkSignature: !isConstructorOrDestructor);
            if (!isConstructorOrDestructor)
            {
                ValidateGenericOverride(method, methodSymbol, state, context, diagnostics);
            }

            var scopeType = methodSymbol.IsStatic
                ? ScopeType.StaticMethodDeclaration
                : ScopeType.InstanceMethodDeclaration;
            var methodState = state.Split(scopeType);
            methodState.EnclosingCallable = methodSymbol;
            methodState.FunctionGenerics = methodSymbol.GenericParameters;
            // Split copies ObjectGenerics, but generic methods must keep the class parameters
            // visible alongside method parameters (`select<R>(Expression<T, R>)`).
            methodState.ObjectGenerics = state.ObjectGenerics;
            // Class + method generics both need resolved bounds so `T extends object` (etc.) is
            // assignable to constraint members inside the body.
            if (state.ObjectGenerics.Count > 0)
            {
                GenericConstraintResolver.ResolveAll(state.ObjectGenerics, methodState, context);
            }

            GenericConstraintResolver.ResolveAll(methodSymbol.GenericParameters, methodState, context);
            var methodReturnTypeAst = method.ReturnType ?? methodSymbol.ReturnType;
            if (methodReturnTypeAst is TyhpReturnTypeGuardAst methodTypeGuard)
            {
                TypeGuardValidation.ValidateGuardParameter(
                    methodTypeGuard, method.Parameters, methodSymbol.Parameters, method, state, diagnostics);
                methodState.IsTypeGuardFunction = true;
            }

            // PHP forbids `return <value>;` in `__construct`/`__destruct` (fatal at runtime). Their
            // ordinary ReturnType slot is null (`: void` on `__construct` lives on TyhpCtorReturnTypeAst;
            // `__destruct` may omit an annotation entirely), so ResolveExpectedReturnType would fall
            // back to `mixed` and silently accept any value return. Force void so bare `return;` stays
            // legal and value-carrying returns are rejected (ControlFlowRule also emits a dedicated
            // diagnostic for the ctor/dtor case).
            methodState.ExpectedReturnType = isConstructorOrDestructor
                ? CheckedTypes.Void
                : TypeGuardValidation.ResolveExpectedReturnType(
                    methodReturnTypeAst, methodState, context);
            methodState.IsInAsyncContext = methodSymbol.IsAsync;
            methodState.IsInGeneratorContext = methodSymbol.IsGenerator;
            methodState.Modifiers = modifiers;

            if (method.ReturnType is not null)
            {
                context.MarkImportNames(method.ReturnType, state);
            }

            if (!methodSymbol.IsStatic && state.EnclosingObjectType is not null)
            {
                // `$this` is verifiably the late-bound type — type it as `static` so it satisfies
                // `: static` returns without widening arbitrary `self` instances into LSB.
                var thisType = new StaticCheckedType(state.EnclosingObjectType);
                methodState.Variables["this"] = VariableState.ForParameter(
                    new VariableSymbol("this") { IsParameter = true },
                    thisType,
                    isReference: false);
            }

            // Prop-init #7: seed `$this->prop` initialization state (includes inherited members).
            if (!methodSymbol.IsStatic && state.EnclosingObject is { } enclosingObject)
            {
                var seeded = methodSymbol is ObjectConstructorMethodSymbol
                    ? PropertyInitializationAnalysis.SeedForConstructor(
                        enclosingObject, context.SymbolTree, context.GlobalScope)
                    : PropertyInitializationAnalysis.SeedForInstanceMethod(
                        enclosingObject, context.SymbolTree, context.GlobalScope);
                methodState.ReplacePropertyInit(seeded);
            }

            RegisterParameters(method.Parameters, methodSymbol.Parameters, methodState, state, context, diagnostics);
            ValidateMagicMethodIfNeeded(method.Identifier, method.Parameters, method.ReturnType, methodSymbol.IsStatic, method, state, context, diagnostics);
            RejectReservedGenericVariantName(method.Identifier, method, state, diagnostics);
            RejectReservedPropertyHookMethodName(method.Identifier, method, state, diagnostics);
            FlagGenericVariantIfNeeded(method.Body, methodSymbol, methodSymbol.GenericParameters, context);
            FlagRuntimeGenericTrackingIfNeeded(method.Body, methodSymbol, state, context, diagnostics);
            context.RecordDeclaredMethod(methodSymbol, state.EnclosingObject);

            if (method.Body is not null)
            {
                if (methodSymbol.IsAbstract)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, method, MessageCode.CheckerMemberModifierConflict, "abstract", "body");
                }
                else
                {
                    methodState.HasReturnedOnAllPaths = false;
                    context.CheckStatementBlock(method.Body, methodState);

                    if (methodSymbol is ObjectConstructorMethodSymbol && state.EnclosingObject is { } ctorOwner)
                    {
                        PropertyInitializationAnalysis.RecordPostConstructionState(
                            ctorOwner, methodState, context.SymbolTree, context.GlobalScope);
                    }

                    if (methodState.IsTypeGuardFunction && !methodState.HasReturnedOnAllPaths)
                    {
                        TypeGuardValidation.ReportMustReturnBool(method, state, diagnostics);
                    }
                    else if (!isConstructorOrDestructor
                        && !IsEffectivelyVoid(methodState.ExpectedReturnType) && !methodState.HasReturnedOnAllPaths)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, method, MessageCode.CheckerMissingReturnStatement, method.Identifier);
                    }

                    context.RecordGenericCallTargetsIn(method.Body, methodState);
                }
            }
            else if (methodSymbol is ObjectConstructorMethodSymbol && state.EnclosingObject is { } emptyCtorOwner)
            {
                // Abstract/interface constructors have no body; treat as declaration-seed only.
                PropertyInitializationAnalysis.RecordPostConstructionState(
                    emptyCtorOwner,
                    constructorFinalState: null,
                    context.SymbolTree,
                    context.GlobalScope);
            }
            else if (!methodSymbol.IsAbstract
                && !isConstructorOrDestructor
                && state.EnclosingObject?.ObjectKind != PhpTypeDeclType.Interface
                && !IsEffectivelyVoid(methodState.ExpectedReturnType))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, method, MessageCode.CheckerMissingReturnStatement, method.Identifier);
            }
        }

        private static void CheckMethodOverride(
            PhpMethodDeclAst method,
            ObjectMethodSymbol methodSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            bool checkSignature = true)
        {
            if (TryFindOverriddenMethod(methodSymbol.Name, state, context) is not { } parentMethod)
            {
                return;
            }

            if ((parentMethod.Visibility & MemberModifier.Final) != 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, method, MessageCode.CheckerFinalMethodOverridden, methodSymbol.Name);
            }

            if (checkSignature)
            {
                ValidateOverrideSignature(method, methodSymbol, parentMethod, state, context, diagnostics);
            }
        }

        /// <summary>
        /// The nearest inherited method of <paramref name="methodName"/> that a member of the enclosing
        /// object actually overrides, or null when nothing is overridden.
        ///
        /// Resolves each base through <see cref="TypeComparer.TryGetParentDeclaration"/> rather than the
        /// symbol's <c>ExtendsType</c>: `extends` is parsed as a raw <c>IClassName</c>, so
        /// <c>ExtendsType</c> is usually null and the walk would never leave the child.
        ///
        /// A <c>private</c> base method is skipped rather than taken as the target. PHP keeps private
        /// methods out of the inheritance slot, so a same-named child method neither overrides one nor has
        /// to stay compatible with it — though it may still override a visible declaration further up.
        /// </summary>
        private static ObjectMethodSymbol? TryFindOverriddenMethod(
            string methodName,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.EnclosingObject is null)
            {
                return null;
            }

            var visited = new HashSet<ObjectDeclarationSymbol>();
            var parent = TypeComparer.TryGetParentDeclaration(
                state.EnclosingObject, context.SymbolTree, context.GlobalScope);

            while (parent is not null && visited.Add(parent))
            {
                if (parent.Members.TryGetValue(methodName, out var parentMember)
                    && parentMember is ObjectMethodSymbol parentMethod
                    && (parentMethod.Visibility & MemberModifier.Private) == 0)
                {
                    return parentMethod;
                }

                parent = TypeComparer.TryGetParentDeclaration(parent, context.SymbolTree, context.GlobalScope);
            }

            return null;
        }

        private static void ValidateOverrideSignature(
            PhpMethodDeclAst method,
            ObjectMethodSymbol methodSymbol,
            ObjectMethodSymbol parentMethod,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // An override may add parameters as long as a call written against the base signature still
            // binds, so the extra ones have to be optional or variadic. Dropping a parameter is never
            // allowed — the base signature would stop binding.
            if (methodSymbol.Parameters.Count < parentMethod.Parameters.Count
                || HasRequiredParameterBeyond(methodSymbol, parentMethod.Parameters.Count))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, method, MessageCode.CheckerOverloadSignatureIncompatible, methodSymbol.Name);
                return;
            }

            // Parent parameter/return annotations live in the base class's generic scope
            // (`Expression<TSource, TReturn>`), not the override's (`ExpressionBuilder<T>`). Resolve
            // them as inherited member types on the child receiver so base type parameters bind and
            // then substitute through the extends chain (`extends Expression<T, bool>` → TSource=T).
            // Using the child state alone left `TSource` unresolved and reported TYHP3003 against the
            // wrong file (parent line/col + child CurrentFileName).
            var receiverType = state.EnclosingObjectType
                ?? (state.EnclosingObject is not null
                    ? CheckedTypes.FromSymbol(state.EnclosingObject)
                    : CheckedTypes.Unresolved);

            for (var i = 0; i < parentMethod.Parameters.Count; i++)
            {
                var childParam = methodSymbol.Parameters[i];
                var parentParam = parentMethod.Parameters[i];
                if (childParam.DeclaredType is null || parentParam.DeclaredType is null)
                {
                    continue;
                }

                var childType = context.ResolveTypeAnnotation(childParam.DeclaredType, state);
                var parentType = context.ResolveMemberDeclaredType(
                    parentParam.DeclaredType, receiverType, state, parentMethod);
                if (!TypeComparer.IsAssignableTo(parentType, childType, context.SymbolTree, context.GlobalScope))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, method, MessageCode.CheckerOverloadSignatureIncompatible, methodSymbol.Name);
                }
            }

            if (methodSymbol.ReturnType is not null && parentMethod.ReturnType is not null)
            {
                var childReturn = context.ResolveTypeAnnotation(methodSymbol.ReturnType, state, isReturnTypePosition: true);
                var parentReturn = context.ResolveMemberDeclaredType(
                    parentMethod.ReturnType, receiverType, state, parentMethod);
                if (!TypeComparer.IsAssignableTo(childReturn, parentReturn, context.SymbolTree, context.GlobalScope))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, method, MessageCode.CheckerOverloadSignatureIncompatible, methodSymbol.Name);
                }
            }
        }

        private static bool HasRequiredParameterBeyond(ObjectMethodSymbol methodSymbol, int inheritedCount)
        {
            for (var i = inheritedCount; i < methodSymbol.Parameters.Count; i++)
            {
                var parameter = methodSymbol.Parameters[i];
                if (parameter.DefaultValue is null && !parameter.IsVariadic)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Requires an override of a generic method to declare the same generic parameters, in the same
        /// order and under the same names.
        ///
        /// A call site binds type arguments against the statically known method, so a virtual call
        /// through the base type reaches the override carrying the base method's type arguments. If the
        /// override renamed or dropped them, its generic variant would take different hidden parameters
        /// than the caller passes — or not exist at all, leaving the call to land on the plain wrapper
        /// and silently see every type argument as unbound.
        ///
        /// Shares <see cref="TryFindOverriddenMethod"/> with <see cref="CheckMethodOverride"/> so both
        /// rules agree on which base declaration is being overridden.
        /// </summary>
        private static void ValidateGenericOverride(
            PhpMethodDeclAst method,
            ObjectMethodSymbol methodSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (TryFindOverriddenMethod(methodSymbol.Name, state, context) is { } parentMethod)
            {
                ReportGenericOverrideMismatch(method, methodSymbol, parentMethod, state, diagnostics);
            }
        }

        private static void ReportGenericOverrideMismatch(
            PhpMethodDeclAst method,
            ObjectMethodSymbol methodSymbol,
            ObjectMethodSymbol parentMethod,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var expected = parentMethod.GenericParameters;
            if (expected.Count == 0)
            {
                return;
            }

            var actual = methodSymbol.GenericParameters;
            if (actual.Count == expected.Count
                && expected.Select((parameter, i) =>
                        string.Equals(parameter.Name, actual[i].Name, StringComparison.Ordinal))
                    .All(matches => matches))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics, state, method, MessageCode.CheckerGenericOverrideParameterMismatch,
                methodSymbol.Name,
                string.Join(", ", expected.Select(parameter => parameter.Name)));
        }

        private static bool IsEffectivelyVoid(ICheckedType? returnType) =>
            returnType is null or { IsVoid: true }
            || returnType.Kind == CheckedTypeKind.Void
            || string.Equals(NormalizeTypeName(returnType.DisplayName), "void", StringComparison.OrdinalIgnoreCase);

        private static bool IsEffectivelyNever(ICheckedType? returnType) =>
            returnType is { IsNever: true }
            || returnType?.Kind == CheckedTypeKind.Never
            || string.Equals(NormalizeTypeName(returnType?.DisplayName), "never", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeTypeName(string? name) =>
            (name ?? string.Empty).TrimStart('\\');

        private static bool IsVoidReturnType(ICheckedType? returnType, ITypeExpression? returnTypeAst)
        {
            if (IsVoidTypeExpression(returnTypeAst))
            {
                return true;
            }

            return returnType is null
                || returnType.IsVoid
                || returnType.Kind == CheckedTypeKind.Void
                || string.Equals(returnType.DisplayName, "void", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVoidTypeExpression(ITypeExpression? returnTypeAst) =>
            returnTypeAst switch
            {
                PhpBuiltinTypeAst builtin =>
                    string.Equals(builtin.Identifier, "void", StringComparison.OrdinalIgnoreCase),
                PhpTypeExpressionAst composite =>
                    composite.Types?.GetAllNotNull().Any(IsVoidTypeExpression) == true,
                _ => false,
            };
    }
}
