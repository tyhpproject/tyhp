using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class TypeCompatibilityRule
    {
        private static void CheckDereferenceable(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (deref.Suffix is PhpCallAst call)
            {
                CheckCall(deref, call, state, context, diagnostics);
            }
            else if (deref.Suffix is PhpInstanceMemberAccessAst memberAccess)
            {
                CheckInstanceMemberAccess(deref, memberAccess, state, context, diagnostics);
            }
            else if (deref.Suffix is PhpStaticMemberAccessAst staticAccess)
            {
                CheckStaticMemberAccess(deref, staticAccess, state, context, diagnostics);
            }
            else if (deref.Suffix is PhpClassConstantAccessAst classConst)
            {
                CheckClassConstantAccess(deref, classConst, state, context, diagnostics);
            }
            else if (deref.Suffix is PhpArrayAccessAst)
            {
                CheckArrayAccess(deref, state, context, diagnostics);
            }
        }

        private static void CheckCall(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            IReadOnlyList<ParameterInfo>? parameters = null;
            // When set, `self`/`parent`/`static` in the callee's parameter types resolve against
            // this receiver (the method's class), not the call-site enclosing type — mirrors
            // ResolveMethodReturnType's relative-name handling.
            ICheckedType? selfResolutionReceiver = null;
            ObjectMethodSymbol? calleeMethod = null;
            FunctionDeclarationSymbol? calleeFunction = null;
            // The callee name/member node, carrying any explicit call-site type arguments
            // (`Box::identity<U>(...)`) so parameter types can substitute them the same way
            // `ResolveMethodReturnType` already does for the return type.
            IDereferenceableBase? callBase = null;

            if (deref.Base is PhpNameAst nameAst)
            {
                var calleeName = nameAst.ValueString ?? nameAst.Identifier ?? nameAst.BoundSymbol?.Name;
                if (calleeName is not null)
                {
                    CheckRestrictedBuiltinCall(calleeName, deref, state, diagnostics);
                }

                // Call-site free-function names are not bound by the binder; resolve by name.
                if (CheckerHelpers.ResolveFreeFunction(
                        nameAst, state, context.SymbolTree, context.GlobalScope) is { } function)
                {
                    // Prefer a tyhpdef overload whose arity *and* argument types match
                    // (call_user_func_array Struct vs Tuple bags share arity 2).
                    var selected = FunctionOverloadSelector.Select(
                        function,
                        call,
                        new FunctionOverloadSelector.Context
                        {
                            State = state,
                            SymbolTree = context.SymbolTree,
                            GlobalScope = context.GlobalScope,
                            InferArgumentType = expr => context.ResolveExpressionType(expr, state),
                            ResolveParameterType = (fn, typeAst) =>
                                context.ResolveFunctionDeclaredType(typeAst, fn, state, nameAst),
                            InferBindings = (fn, c) =>
                            {
                                if (fn.GenericParameters.Count == 0)
                                {
                                    return null;
                                }

                                return context.TryInferGenericBindings(
                                    fn.GenericParameters, fn.Parameters, c, state, out var inferred)
                                    && inferred.Count > 0
                                    ? inferred
                                    : null;
                            },
                        });
                    parameters = selected.Parameters;
                    calleeFunction = selected;
                    callBase = nameAst;
                }
            }
            else if (deref.Base is PhpDereferenceableAst chain && chain.Base is not null)
            {
                // `$c->g(...)` is parsed as deref(suffix=call, base=deref(suffix=member g, base=$c)).
                // Resolve the method on the *receiver* (`chain.Base`), not on the member-access chain
                // itself (whose type is the method/callable, not the owning object).
                string? methodName;
                bool staticOnly;
                ICheckedType receiverType;
                switch (chain.Suffix)
                {
                    case PhpInstanceMemberAccessAst instanceAccess:
                        methodName = GetExpressionText(instanceAccess.MemberName);
                        staticOnly = false;
                        receiverType = context.ResolveExpressionType(chain.Base, state);
                        break;
                    case PhpStaticMemberAccessAst staticAccess:
                        methodName = GetExpressionText(staticAccess.Member);
                        staticOnly = true;
                        receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                            chain.Base, state, context, context.SymbolTree, context.GlobalScope);
                        break;
                    case PhpClassConstantAccessAst classConstAccess:
                        // `Class::method(...)` is parsed with a class-constant-access suffix.
                        methodName = GetExpressionText(classConstAccess.Member);
                        staticOnly = true;
                        receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                            chain.Base, state, context, context.SymbolTree, context.GlobalScope);
                        break;
                    default:
                        methodName = null;
                        staticOnly = false;
                        receiverType = CheckedTypes.Unresolved;
                        break;
                }

                // `chain.Base` (e.g. the `$a->b()` in `$a->b()->c()`) sits inside this call's
                // suppressed subtree (SuppressChildTraversal on PhpDereferenceableAst) and is
                // otherwise never independently visited, so a nested call/`new` in the receiver
                // chain would silently skip its own argument-validation / visibility checks.
                CheckNestedCalleeIfNeeded(chain.Base, state, context);

                if (methodName is not null)
                {
                    // Method call on unnarrowed `mixed` — reject before resolution fallback.
                    if (CheckerHelpers.ReportMixedRequiresNarrowing(
                            diagnostics, state, chain.Base ?? deref, receiverType))
                    {
                        return;
                    }

                    if (TryResolveMethod(receiverType, methodName, staticOnly, context, out var method))
                    {
                        parameters = method!.Parameters;
                        selfResolutionReceiver = receiverType;
                        calleeMethod = method;
                        callBase = chain;
                        CheckMemberVisibility(method, state, deref, diagnostics);
                    }
                }
            }
            else if (deref.Base is IExpression callableExpr)
            {
                // `$fn(...)` / `$obj(...)` — invoke a callable-typed expression.
                CheckNestedCalleeIfNeeded(callableExpr, state, context);
                var calleeType = context.ResolveExpressionType(callableExpr, state);
                if (CheckerHelpers.ReportMixedRequiresNarrowing(
                        diagnostics, state, callableExpr, calleeType))
                {
                    return;
                }

                if (call.Arguments is not null
                    && CallableArityFacetBuilder.IsCallableFacetType(calleeType))
                {
                    ValidateNamedArguments(call.Arguments, state, deref, diagnostics);
                    ValidateCallableFacetArguments(
                        call.Arguments,
                        calleeType,
                        state,
                        context,
                        diagnostics);
                    return;
                }
            }

            if (parameters is null)
            {
                return;
            }

            var arityCalleeName = calleeMethod?.Name
                ?? calleeFunction?.Name
                ?? GetExpressionText(deref.Base as IExpression)
                ?? "callable";
            ValidateArgumentArity(
                call.Arguments,
                parameters,
                state,
                diagnostics,
                deref,
                arityCalleeName);

            if (call.Arguments is null)
            {
                return;
            }

            ValidateNamedArguments(call.Arguments, state, deref, diagnostics);
            ValidateArgumentTypes(
                call.Arguments,
                parameters,
                state,
                context,
                diagnostics,
                selfResolutionReceiver,
                calleeMethod,
                callBase,
                calleeFunction,
                call,
                arityReportNode: deref);
        }

        /// <summary>
        /// Story 14.5: validate <c>exit(...)</c> / <c>die(...)</c> / <c>clone(...)</c> keyword
        /// call forms (operand is <see cref="PhpArgumentListAst"/>) against the ExtCore tyhpdef
        /// function symbols, reusing the same arity / named-arg / type pipeline as
        /// <see cref="CheckCall"/>. Returns <c>true</c> when the node is a keyword call form
        /// (whether or not a symbol was found), so unary clone object checks are skipped.
        /// </summary>
        private static bool TryCheckKeywordConstructCall(
            PhpUnaryOpAst unary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (unary.Operand is not PhpArgumentListAst arguments)
            {
                return false;
            }

            var name = unary.Operator?.ValueString;
            if (!CheckerHelpers.IsKeywordConstructName(name))
            {
                return false;
            }

            var function = unary.BoundSymbol as FunctionDeclarationSymbol
                ?? CheckerHelpers.ResolveKeywordConstructFunction(
                    name!, state, context.SymbolTree, context.GlobalScope);
            if (function is null)
            {
                return true;
            }

            var selected = CheckerHelpers.SelectFunctionOverloadForCall(function, arguments);
            ValidateArgumentArity(
                arguments,
                selected.Parameters,
                state,
                diagnostics,
                unary,
                selected.Name);
            ValidateNamedArguments(arguments, state, unary, diagnostics);
            ValidateArgumentTypes(
                arguments,
                selected.Parameters,
                state,
                context,
                diagnostics,
                selfResolutionReceiver: null,
                calleeMethod: null,
                callBase: null,
                calleeFunction: selected,
                call: null,
                arityReportNode: unary);
            return true;
        }

        /// <summary>
        /// Validates arguments against the callable facet whose arity matches the call's
        /// positional argument count (optional-arity intersections). When the facet still
        /// carries unbound type parameters (first-class callable from a generic function),
        /// bind them from the argument types before assignability checks — same policy as
        /// direct-call argument-driven inference.
        /// </summary>
        private static void ValidateCallableFacetArguments(
            PhpArgumentListAst arguments,
            ICheckedType calleeType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var positionalCount = CallableArityFacetBuilder.CountPositionalArguments(arguments);
            if (!CallableArityFacetBuilder.TrySelectCallableFacet(
                    calleeType, positionalCount, out var facet)
                || facet is null)
            {
                // No exact-arity facet: still type-check nested callees; skip param matching.
                foreach (var arg in arguments.GetAllNotNull())
                {
                    CheckNestedCalleeIfNeeded(arg.Expression, state, context);
                }

                return;
            }

            var positionalArgs = new List<(PhpArgumentAst Arg, ICheckedType ArgType)>();
            foreach (var arg in arguments.GetAllNotNull())
            {
                CheckNestedCalleeIfNeeded(arg.Expression, state, context);
                if (arg.IsVariadic || arg.Expression is null || arg.Name is not null)
                {
                    continue;
                }

                if (arg.Expression is PhpInlineFunctionAst)
                {
                    // Closure args need the (possibly still-open) expected type; resolve their
                    // type later against the effective facet without forcing InferExpressionType
                    // on the closure body here.
                    positionalArgs.Add((arg, CheckedTypes.Unresolved));
                    continue;
                }

                positionalArgs.Add((arg, context.ResolveExpressionType(arg.Expression, state)));
            }

            var effectiveFacet = facet;
            if (CallableGenericInference.FacetNeedsArgumentInference(facet))
            {
                // Align argument types with facet slots (unresolved for closures so binding
                // skips those positions via CollectGenericBindings' mixed/unresolved guards).
                var aligned = new List<ICheckedType>(facet.ParameterTypes.Count);
                for (var i = 0; i < facet.ParameterTypes.Count && i < positionalArgs.Count; i++)
                {
                    aligned.Add(positionalArgs[i].ArgType);
                }

                if (CallableGenericInference.TryInferFacetBindings(facet, aligned, out var bindings)
                    && bindings.Count > 0)
                {
                    effectiveFacet = CallableGenericInference.SubstituteFacet(
                        facet, bindings, context.SymbolTree, context.GlobalScope);
                }
            }

            for (var i = 0; i < positionalArgs.Count && i < effectiveFacet.ParameterTypes.Count; i++)
            {
                var (arg, argType) = positionalArgs[i];
                var paramType = effectiveFacet.ParameterTypes[i];

                // Leftover unbound generics (not constrained by any argument) are gradual —
                // same policy as ResolveCalleeParameterType for direct named calls.
                if (CallableGenericInference.ContainsUnboundGeneric(paramType))
                {
                    paramType = CheckedTypes.Mixed;
                }

                if (arg.Expression is PhpInlineFunctionAst closure)
                {
                    var ambient = state.SnapShot();
                    ClosureParameterInference.SetExpectedClosureTypeFromArgument(paramType, ambient);
                    context.CheckNode(closure, ambient);
                    continue;
                }

                // Plain assignability only: the callee here is an arbitrary runtime callable value
                // (`$fn(...)`), which `AliasConverter.TryResolveCalleeParameters` cannot statically
                // resolve to a declared parameter list, so it never inserts an implicit-convert
                // rewrite at this call form. Accepting convert here would let the checker pass while
                // emit still hands the unconverted object to PHP, throwing a TypeError at runtime.
                if (!context.IsAssignable(argType, paramType, state)
                    && !CheckerHelpers.IsArrayCallableLiteral(arg.Expression, paramType, context, state))
                {
                    if (!SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                            argType, paramType, state, context.SymbolTree, context.GlobalScope,
                            diagnostics, arg))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, arg, MessageCode.CheckerIncompatibleArgumentType,
                            argType.DisplayName, paramType.DisplayName);
                    }
                }
            }
        }

        private static void CheckInstanceMemberAccess(
            PhpDereferenceableAst deref,
            PhpInstanceMemberAccessAst memberAccess,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckNestedCalleeIfNeeded(deref.Base, state, context);
            var receiverType = context.ResolveExpressionType(deref.Base as IExpression ?? deref, state);
            if (CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, deref.Base ?? deref, receiverType))
            {
                return;
            }

            var memberName = GetExpressionText(memberAccess.MemberName);
            if (memberName is null)
            {
                return;
            }

            if (TryResolveProperty(receiverType, memberName, context, out var property))
            {
                CheckMemberVisibility(property!, state, deref, diagnostics);
            }
            else if (TryResolveMethod(receiverType, memberName, staticOnly: false, context, out var method))
            {
                CheckMemberVisibility(method!, state, deref, diagnostics);
            }
        }

        private static void CheckArrayAccess(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckNestedCalleeIfNeeded(deref.Base, state, context);
            if (deref.Base is null)
            {
                return;
            }

            var receiverType = context.ResolveExpressionType(deref.Base as IExpression ?? deref, state);
            CheckerHelpers.ReportMixedRequiresNarrowing(
                diagnostics, state, deref.Base, receiverType);
        }

        private static void CheckStaticMemberAccess(
            PhpDereferenceableAst deref,
            PhpStaticMemberAccessAst staticAccess,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckNestedCalleeIfNeeded(deref.Base, state, context);
            var receiverType = context.ResolveExpressionType(deref.Base as IExpression ?? deref, state);
            // `mixed::foo` is not a meaningful static access — require narrowing first.
            // Skip when the receiver is a class-name expression resolved as a declaration type
            // (those are not value-typed `mixed`).
            if (CheckerHelpers.IsUnnarrowedMixed(receiverType)
                && deref.Base is not (PhpNameAst or PhpBuiltinTypeAst or TyhpGenericIdentifierAst))
            {
                CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, deref.Base ?? deref, receiverType);
                return;
            }

            var memberName = GetExpressionText(staticAccess.Member);
            if (memberName is null)
            {
                return;
            }

            if (TryResolveMethod(receiverType, memberName, staticOnly: true, context, out var method))
            {
                CheckMemberVisibility(method!, state, deref, diagnostics);
            }
            else if (TryResolveProperty(receiverType, memberName, context, out var property))
            {
                CheckMemberVisibility(property!, state, deref, diagnostics);
            }
            else if (TryResolveConstant(receiverType, memberName, context, out var constant))
            {
                // Rare path: some rewrites may surface constants as static-member access. The
                // primary constant path is CheckClassConstantAccess (PhpClassConstantAccessAst).
                CheckMemberVisibility(constant!, state, deref, diagnostics);
            }
        }

        private static void CheckClassConstantAccess(
            PhpDereferenceableAst deref,
            PhpClassConstantAccessAst classConst,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckNestedCalleeIfNeeded(deref.Base, state, context);

            // Bare class names are not value expressions, so ResolveExpressionType yields
            // unresolved; resolve them as type receivers the same way instanceof targets do.
            var receiverNode = deref.Base as IBase2Ast ?? deref;
            var receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                receiverNode,
                state,
                context,
                context.SymbolTree,
                context.GlobalScope);
            var memberName = GetExpressionText(classConst.Member);
            if (memberName is null)
            {
                return;
            }

            if (TryResolveConstant(receiverType, memberName, context, out var constant))
            {
                CheckMemberVisibility(constant!, state, deref, diagnostics);
            }
        }

        private static void ValidateNamedArguments(
            PhpArgumentListAst arguments,
            CheckerState state,
            IBase2Ast node,
            DiagnosticBag diagnostics)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sawNamed = false;
            var sawUnpack = false;

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.IsVariadic)
                {
                    sawUnpack = true;
                }

                if (arg.Name is not null)
                {
                    if (sawUnpack)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, arg, MessageCode.CheckerNamedAfterUnpack, arg.Name.ValueString);
                    }

                    sawNamed = true;
                    if (!seenNames.Add(arg.Name.ValueString ?? string.Empty))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, arg, MessageCode.CheckerDuplicateNamedArgument, arg.Name.ValueString);
                    }
                }
                else if (sawNamed)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerPositionalAfterNamed);
                }
            }
        }

        private static void ValidateArgumentTypes(
            PhpArgumentListAst arguments,
            IReadOnlyList<ParameterInfo> parameters,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            ICheckedType? selfResolutionReceiver = null,
            ObjectMethodSymbol? calleeMethod = null,
            IDereferenceableBase? callBase = null,
            FunctionDeclarationSymbol? calleeFunction = null,
            PhpCallAst? call = null,
            IBase2Ast? arityReportNode = null)
        {
            // Parameter annotations may use `self`/`parent` relative to the *callee* class. When
            // the call site lives in a different type (e.g. `Type::is($v, $t)` inside a trait),
            // resolve those names against the method receiver — same rule as return-type `self`.
            // For generic receivers (`Box<string>`), also substitute class type parameters into
            // parameter types so `set(TValue $v)` becomes `set(string $v)`.
            CheckerState paramResolveState = state;
            if (selfResolutionReceiver is not null
                && UnwrapForMemberAccess(selfResolutionReceiver)
                    is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol receiverObj })
            {
                paramResolveState = state.SnapShot();
                paramResolveState.EnclosingObject = receiverObj;
                paramResolveState.EnclosingObjectType = CheckedTypes.FromSymbol(receiverObj);
                if (receiverObj.GenericParameters.Count > 0)
                {
                    paramResolveState.ObjectGenerics = receiverObj.GenericParameters;
                }
            }

            // Call-site bindings are computed at most once, and only when a parameter type
            // actually carries a deferred callable-signature utility (see ApplyInferredBindings).
            // Running inference up front would type closure arguments before the closure branch
            // below supplies their contextual parameter types.
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? inferredBindings = null;
            var inferenceAttempted = false;
            var calleeGenerics = calleeFunction?.GenericParameters
                ?? calleeMethod?.GenericParameters;

            Dictionary<GenericTypeParameterSymbol, ICheckedType>? InferBindings()
            {
                if (inferenceAttempted)
                {
                    return inferredBindings;
                }

                inferenceAttempted = true;
                if (call is not null
                    && calleeGenerics is { Count: > 0 }
                    && context.TryInferGenericBindings(
                        calleeGenerics,
                        parameters,
                        call,
                        state,
                        out var inferred,
                        selfResolutionReceiver,
                        calleeMethod)
                    && inferred.Count > 0)
                {
                    inferredBindings = inferred;
                }

                return inferredBindings;
            }

            var argsList = arguments.GetAllNotNull().ToList();
            var restParamIndex = parameters.Count > 0 && parameters[^1].IsVariadic
                ? parameters.Count - 1
                : -1;
            var restUnpackDone = false;
            var calleeDisplayName = calleeMethod?.Name ?? calleeFunction?.Name ?? "callable";
            var positionalIndex = 0;
            for (var argIndex = 0; argIndex < argsList.Count; argIndex++)
            {
                var arg = argsList[argIndex];
                // `PhpArgumentListAst` lives inside the call's suppressed subtree
                // (SuppressChildTraversal on PhpDereferenceableAst), so a nested call / `new`
                // used as an argument is otherwise never independently visited — silently
                // skipping its own argument validation and member-visibility checks. Re-enter the
                // normal check pipeline before any early `continue` below so this still runs
                // regardless of how the argument matches (or fails to match) a parameter.
                CheckNestedCalleeIfNeeded(arg.Expression, state, context);

                if (arg.IsVariadic)
                {
                    CheckSpreadIsIterable(arg, state, context, diagnostics);
                    // A spread that lands on the Rest slot starts unpack: PHP feeds `...$packed`
                    // into `...$args`. Do not leave it to the post-loop empty-rest check, and do
                    // not let a later positional (`invoke($cb, ...$packed, $x)`) be typed as
                    // inner parameter 0.
                    if (restParamIndex >= 0
                        && positionalIndex >= restParamIndex
                        && parameters[restParamIndex].DeclaredType is { } spreadRestDeclared)
                    {
                        var spreadRestType = ResolveCalleeParameterType(
                            spreadRestDeclared,
                            paramResolveState,
                            selfResolutionReceiver,
                            calleeMethod,
                            callBase,
                            state,
                            context,
                            calleeFunction,
                            InferBindings);
                        if (UtilityTypeResolver.TryGetCallableParametersRest(
                                spreadRestType, out var spreadRestCallable))
                        {
                            var restArgs = CollectRemainingPositionalRestArgs(
                                argsList, argIndex, parameters, state, context, diagnostics,
                                out var restHasSpread);
                            ValidateCallableParametersRestArguments(
                                restArgs,
                                spreadRestCallable,
                                state,
                                context,
                                diagnostics,
                                calleeDisplayName,
                                wrapperPrefixCount: restParamIndex,
                                arityReportNode ?? arg,
                                restHasSpread);
                            restUnpackDone = true;
                            break;
                        }
                    }

                    continue;
                }

                ParameterInfo? param = null;
                if (arg.Name?.ValueString is { } named)
                {
                    // Named-argument syntax uses the bare parameter name (no `$`); binder
                    // ParameterInfo.Name keeps the leading `$` from the declaration.
                    param = parameters.FirstOrDefault(p =>
                        string.Equals(
                            p.Name.TrimStart('$'),
                            named.TrimStart('$'),
                            StringComparison.OrdinalIgnoreCase));
                    if (param is null)
                    {
                        CheckerHelpers.ReportErrorWithDidYouMean(
                            diagnostics,
                            state,
                            arg,
                            MessageCode.CheckerUnknownNamedArgument,
                            named,
                            InScopeNameCandidates.CollectParameterNames(parameters),
                            named);
                        continue;
                    }
                }
                else if (positionalIndex >= parameters.Count)
                {
                    // Extra positionals bind to a trailing homogeneous variadic (`int ...$xs`).
                    if (restParamIndex < 0)
                    {
                        continue;
                    }

                    param = parameters[restParamIndex];
                }
                else
                {
                    param = parameters[positionalIndex++];
                }

                if (param.DeclaredType is null || arg.Expression is null)
                {
                    continue;
                }

                var expectedParamType = ResolveCalleeParameterType(
                    param.DeclaredType,
                    paramResolveState,
                    selfResolutionReceiver,
                    calleeMethod,
                    callBase,
                    state,
                    context,
                    calleeFunction,
                    InferBindings);

                if (param.IsVariadic
                    && UtilityTypeResolver.TryGetCallableParametersRest(
                        expectedParamType, out var restCallable))
                {
                    // Named `args: $x` packs one value into the variadic; it is not the start of
                    // positional unpack. PHP forwards that value as a single rest element.
                    if (arg.Name is not null)
                    {
                        continue;
                    }

                    var restArgs = CollectRemainingPositionalRestArgs(
                        argsList, argIndex, parameters, state, context, diagnostics, out var restHasSpread);
                    ValidateCallableParametersRestArguments(
                        restArgs,
                        restCallable,
                        state,
                        context,
                        diagnostics,
                        calleeDisplayName,
                        wrapperPrefixCount: restParamIndex,
                        arityReportNode ?? arg,
                        restHasSpread);
                    restUnpackDone = true;
                    break;
                }

                CheckArgumentAgainstParameterType(
                    arg,
                    expectedParamType,
                    state,
                    context,
                    diagnostics);
            }

            if (!restUnpackDone && restParamIndex >= 0
                && parameters[restParamIndex].DeclaredType is { } restDeclaredType)
            {
                var restParam = parameters[restParamIndex];
                var restFilledByName = argsList.Any(a =>
                    a.Name?.ValueString is { } named
                    && string.Equals(
                        restParam.Name.TrimStart('$'),
                        named.TrimStart('$'),
                        StringComparison.OrdinalIgnoreCase));
                var anySpread = argsList.Any(a => a.IsVariadic);
                // Spreads / a named pack into `$args` may supply rest values that are not
                // statically counted. Do not report TYHP4142 as if the rest list were empty.
                if (restFilledByName || anySpread)
                {
                    return;
                }

                var expectedRestType = ResolveCalleeParameterType(
                    restDeclaredType,
                    paramResolveState,
                    selfResolutionReceiver,
                    calleeMethod,
                    callBase,
                    state,
                    context,
                    calleeFunction,
                    InferBindings);
                if (UtilityTypeResolver.TryGetCallableParametersRest(expectedRestType, out var restCallable))
                {
                    ValidateCallableParametersRestArguments(
                        [],
                        restCallable,
                        state,
                        context,
                        diagnostics,
                        calleeDisplayName,
                        wrapperPrefixCount: restParamIndex,
                        arityReportNode ?? callBase as IBase2Ast ?? arguments,
                        hasSpread: false);
                }
            }
        }

        private static void CheckSpreadIsIterable(
            PhpArgumentAst arg,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (arg.Expression is null)
            {
                return;
            }

            var spreadType = context.ResolveExpressionType(arg.Expression, state);
            if (!CheckerHelpers.IsIterableType(spreadType, context.SymbolTree, context.GlobalScope))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, arg, MessageCode.CheckerSpreadNonIterable, spreadType.DisplayName);
            }
        }

        /// <summary>
        /// Remaining positional arguments from <paramref name="startIndex"/> for Rest unpack.
        /// Spreads are checked as iterable. Positionals after the first spread are omitted
        /// from the typed rest list — the spread's length is unknown, so those values are not
        /// inner parameter 0, 1, …. Named arguments are not rest slots; unknown names are
        /// reported here because the outer loop will not see them after the unpack <c>break</c>.
        /// </summary>
        private static List<PhpArgumentAst> CollectRemainingPositionalRestArgs(
            IReadOnlyList<PhpArgumentAst> argsList,
            int startIndex,
            IReadOnlyList<ParameterInfo> parameters,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            out bool hasSpread)
        {
            hasSpread = false;
            var restArgs = new List<PhpArgumentAst>();
            for (var restIndex = startIndex; restIndex < argsList.Count; restIndex++)
            {
                var restArg = argsList[restIndex];
                if (restIndex != startIndex)
                {
                    CheckNestedCalleeIfNeeded(restArg.Expression, state, context);
                }

                if (restArg.IsVariadic)
                {
                    hasSpread = true;
                    if (restIndex != startIndex)
                    {
                        CheckSpreadIsIterable(restArg, state, context, diagnostics);
                    }

                    continue;
                }

                // Values after a rest-region spread are not statically aligned with inner slots.
                if (hasSpread)
                {
                    continue;
                }

                if (restArg.Name?.ValueString is { } named)
                {
                    var namedParam = parameters.FirstOrDefault(p =>
                        string.Equals(
                            p.Name.TrimStart('$'),
                            named.TrimStart('$'),
                            StringComparison.OrdinalIgnoreCase));
                    if (namedParam is null)
                    {
                        CheckerHelpers.ReportErrorWithDidYouMean(
                            diagnostics,
                            state,
                            restArg,
                            MessageCode.CheckerUnknownNamedArgument,
                            named,
                            InScopeNameCandidates.CollectParameterNames(parameters),
                            named);
                    }

                    continue;
                }

                restArgs.Add(restArg);
            }

            return restArgs;
        }

        /// <summary>
        /// TypeScript <c>...args: Parameters&lt;T&gt;</c> analogue: trailing arguments of a
        /// <c>__CallableParametersRest&lt;TCallable&gt; ...$args</c> parameter are checked 1:1 against
        /// the callable's reflected parameter list. Opaque / unbound callables stay gradual.
        /// Plain assignability (no operator-convert) because emit forwards the values as-is.
        /// </summary>
        private static void ValidateCallableParametersRestArguments(
            IReadOnlyList<PhpArgumentAst> restArgs,
            ICheckedType callableType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            string calleeDisplayName,
            int wrapperPrefixCount,
            IBase2Ast arityReportNode,
            bool hasSpread)
        {
            if (CallableSignatureReflection.IsUnboundTypeParameter(callableType)
                || CallableSignatureReflection.IsOpaqueCallable(callableType)
                || !CallableSignatureReflection.TryReflect(callableType, out var signature)
                || signature is null)
            {
                return;
            }

            var nonVariadic = new List<CallableSignatureReflection.Parameter>();
            CallableSignatureReflection.Parameter? variadic = null;
            foreach (var parameter in signature.Parameters)
            {
                if (parameter.IsVariadic)
                {
                    variadic = parameter;
                }
                else
                {
                    nonVariadic.Add(parameter);
                }
            }

            if (!hasSpread)
            {
                var requiredCount = 0;
                foreach (var parameter in nonVariadic)
                {
                    if (!parameter.IsOptional)
                    {
                        requiredCount++;
                    }
                }

                if (restArgs.Count < requiredCount)
                {
                    CallableSignatureReflection.Parameter? missing = null;
                    for (var i = restArgs.Count; i < nonVariadic.Count; i++)
                    {
                        if (!nonVariadic[i].IsOptional)
                        {
                            missing = nonVariadic[i];
                            break;
                        }
                    }

                    var missingName = missing?.Name
                        ?? CallableSignatureReflection.PositionalPropertyName(restArgs.Count).TrimStart('$');
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arityReportNode,
                        MessageCode.CheckerMissingArgument,
                        missingName,
                        calleeDisplayName);
                }

                if (variadic is null && restArgs.Count > nonVariadic.Count)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arityReportNode,
                        MessageCode.CheckerTooManyArguments,
                        calleeDisplayName,
                        wrapperPrefixCount + nonVariadic.Count,
                        wrapperPrefixCount + restArgs.Count);
                }
            }

            var typedCount = variadic is null
                ? Math.Min(restArgs.Count, nonVariadic.Count)
                : restArgs.Count;
            for (var i = 0; i < typedCount; i++)
            {
                var arg = restArgs[i];
                if (arg.Expression is null)
                {
                    continue;
                }

                var expected = i < nonVariadic.Count
                    ? nonVariadic[i].Type
                    : variadic!.Type;
                CheckRestArgumentAgainstParameterType(arg, expected, state, context, diagnostics);
            }
        }

        /// <summary>
        /// Rest-unpack argument check. Operator-convert is not offered: emit cannot rewrite
        /// forwarded rest values against the inner callable's parameters.
        /// </summary>
        private static void CheckRestArgumentAgainstParameterType(
            PhpArgumentAst arg,
            ICheckedType expectedParamType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (arg.Expression is PhpInlineFunctionAst closure)
            {
                var ambient = state.SnapShot();
                ClosureParameterInference.SetExpectedClosureTypeFromArgument(expectedParamType, ambient);
                context.CheckNode(closure, ambient);
                return;
            }

            var argType = context.ResolveExpressionType(arg.Expression!, state);
            if (IsClosureTargetType(expectedParamType)
                && PropertyPathSupport.IsPropertyPathOrExpressionType(argType))
            {
                return;
            }

            if (StructBagLiteralChecker.TryCheck(
                    arg.Expression!, expectedParamType, state, context, diagnostics))
            {
                return;
            }

            if (!context.IsAssignable(argType, expectedParamType, state)
                && !CheckerHelpers.IsArrayCallableLiteral(arg.Expression, expectedParamType, context, state))
            {
                if (!SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                        argType, expectedParamType, state, context.SymbolTree, context.GlobalScope,
                        diagnostics, arg))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerIncompatibleArgumentType,
                        argType.DisplayName, expectedParamType.DisplayName);
                }
            }
        }

        private static void CheckArgumentAgainstParameterType(
            PhpArgumentAst arg,
            ICheckedType expectedParamType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (arg.Expression is null)
            {
                return;
            }
            // Story 16 Phase 1: an inline fn at a PropertyPath<T, R> parameter must be an arrow
            // fn whose body is a simple property-access chain. Already-built PropertyPath values
            // (forwarding a parameter, `null` for a nullable parameter) pass through normally.
            if (PropertyPathSupport.IsPropertyPathType(expectedParamType))
            {
                if (arg.Expression is PhpInlineFunctionAst propertyPathFn)
                {
                    CheckPropertyPathInlineFn(
                        arg,
                        propertyPathFn,
                        expectedParamType,
                        state,
                        context,
                        diagnostics);
                    return;
                }

                var propertyPathArgType = context.ResolveExpressionType(arg.Expression!, state);
                if (!context.IsAssignableAllowingOperatorConvert(
                        propertyPathArgType, expectedParamType, state))
                {
                    PropertyPathSupport.TryGetPropertyPathTypeArgs(
                        expectedParamType, out var expectedSource, out var expectedResult);
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arg,
                        MessageCode.CheckerPropertyPathRequiresInlineFn,
                        PropertyPathSupport.DisplayTypeArg(expectedSource),
                        PropertyPathSupport.DisplayTypeArg(expectedResult));
                }

                return;
            }

            // Story 16 Phase 2: Expression<T, R> accepts an arrow fn with a supported body,
            // or an already-built Expression / null for nullable parameters.
            if (ExpressionTreeSupport.IsExpressionType(expectedParamType))
            {
                if (arg.Expression is PhpInlineFunctionAst expressionFn)
                {
                    CheckExpressionInlineFn(
                        arg,
                        expressionFn,
                        expectedParamType,
                        state,
                        context,
                        diagnostics);
                    return;
                }

                var expressionArgType = context.ResolveExpressionType(arg.Expression!, state);
                if (!context.IsAssignableAllowingOperatorConvert(
                        expressionArgType, expectedParamType, state))
                {
                    ExpressionTreeSupport.TryGetExpressionTypeArgs(
                        expectedParamType, out var expectedParams, out var expectedReturn);
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arg,
                        MessageCode.CheckerExpressionRequiresInlineFn,
                        ExpressionTreeSupport.DisplayFirstParamArg(expectedParams),
                        ExpressionTreeSupport.DisplayReturnArg(expectedReturn));
                }

                return;
            }

            if (arg.Expression is PhpInlineFunctionAst closure)
            {
                // Pass the *caller* state as ambient so ClosureRule can clone `use ($x)`
                // captures from real outer locals. Splitting AnonymousFunctionDeclaration
                // here first emptied Variables, so captures were typed as unresolved/mixed
                // and nested calls like `self::_await($promise)` / `$generator->valid()`
                // falsely failed. ClosureRule splits its own body scope.
                var ambient = state.SnapShot();
                ClosureParameterInference.SetExpectedClosureTypeFromArgument(expectedParamType, ambient);
                context.CheckNode(closure, ambient);
                return;
            }

            var argType = context.ResolveExpressionType(arg.Expression!, state);
            // PropertyPath / Expression → \Closure is rewritten by the emitter to `->callable`.
            if (IsClosureTargetType(expectedParamType)
                && PropertyPathSupport.IsPropertyPathOrExpressionType(argType))
            {
                return;
            }

            if (StructBagLiteralChecker.TryCheck(
                    arg.Expression!, expectedParamType, state, context, diagnostics))
            {
                return;
            }

            if (!context.IsAssignableAllowingOperatorConvert(argType, expectedParamType, state)
                && !CheckerHelpers.IsArrayCallableLiteral(arg.Expression, expectedParamType, context, state))
            {
                if (!SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                        argType, expectedParamType, state, context.SymbolTree, context.GlobalScope,
                        diagnostics, arg))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerIncompatibleArgumentType,
                        argType.DisplayName, expectedParamType.DisplayName);
                }
            }
        }

        /// <summary>
        /// Story 16 Phase 1 — validate an inline function passed to a
        /// <c>PropertyPath&lt;T, R&gt;</c> parameter: arrow syntax only, property-chain body only.
        /// </summary>
        private static void CheckPropertyPathInlineFn(
            PhpArgumentAst arg,
            PhpInlineFunctionAst closure,
            ICheckedType propertyPathType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!closure.IsArrowFunction)
            {
                PropertyPathSupport.TryGetPropertyPathTypeArgs(
                    propertyPathType, out var sourceType, out var resultType);
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    arg,
                    MessageCode.CheckerPropertyPathRequiresInlineFn,
                    PropertyPathSupport.DisplayTypeArg(sourceType),
                    PropertyPathSupport.DisplayTypeArg(resultType));
                return;
            }

            var ambient = state.SnapShot();
            ClosureParameterInference.SetExpectedClosureTypeFromArgument(propertyPathType, ambient);
            context.CheckNode(closure, ambient);

            if (!PropertyPathSupport.TryGetArrowBodyExpression(closure, out var body))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    closure,
                    MessageCode.CheckerPropertyPathInvalidBody);
                return;
            }

            var paramName = PropertyPathSupport.GetSingleArrowParameterName(closure);
            if (paramName is null
                || !PropertyPathSupport.TryExtractPropertyChain(body, paramName, out var segments)
                || segments.Count == 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    body is IBase2Ast bodyNode ? bodyNode : closure,
                    MessageCode.CheckerPropertyPathInvalidBody);
                return;
            }

            // ClosureRule already checks the body return against the mapped callable's R
            // (including nullability when R is `?T` / the chain uses `?->`).
        }

        /// <summary>
        /// Story 16 Phase 2 — validate an inline function passed to an
        /// <c>Expression&lt;T, R&gt;</c> parameter: arrow syntax, supported body, assigned captures.
        /// </summary>
        private static void CheckExpressionInlineFn(
            PhpArgumentAst arg,
            PhpInlineFunctionAst closure,
            ICheckedType expressionType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!closure.IsArrowFunction)
            {
                ExpressionTreeSupport.TryGetExpressionTypeArgs(
                    expressionType, out var paramTypes, out var returnType);
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    arg,
                    MessageCode.CheckerExpressionRequiresInlineFn,
                    ExpressionTreeSupport.DisplayFirstParamArg(paramTypes),
                    ExpressionTreeSupport.DisplayReturnArg(returnType));
                return;
            }

            var ambient = state.SnapShot();
            ClosureParameterInference.SetExpectedClosureTypeFromArgument(expressionType, ambient);
            context.CheckNode(closure, ambient);

            if (!PropertyPathSupport.TryGetArrowBodyExpression(closure, out var body))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    closure,
                    MessageCode.CheckerExpressionUnsupportedNode,
                    "statement body");
                return;
            }

            if (!ExpressionTreeSupport.TryValidateSupportedBody(body, closure, out var unsupportedKind)
                && unsupportedKind is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    body is IBase2Ast bodyNode ? bodyNode : closure,
                    MessageCode.CheckerExpressionUnsupportedNode,
                    unsupportedKind);
                return;
            }

            var captures = ExpressionTreeSupport.CollectCapturedVariables(body, closure);
            if (!ExpressionTreeSupport.TryValidateCapturesAssigned(captures, state, out var undefinedName)
                && undefinedName is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    body is IBase2Ast captureSite ? captureSite : closure,
                    MessageCode.CheckerExpressionCapturedVarUndefined,
                    undefinedName);
            }
        }

        private static bool IsClosureTargetType(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is { } obj)
            {
                var fqn = (obj.FullyQualifiedName ?? obj.Name ?? "").TrimStart('\\');
                return string.Equals(obj.Name, "Closure", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fqn, "Closure", StringComparison.OrdinalIgnoreCase);
            }

            var display = type.DisplayName.TrimStart('\\');
            var angle = display.IndexOf('<');
            if (angle >= 0)
            {
                display = display[..angle];
            }

            return string.Equals(display, "Closure", StringComparison.OrdinalIgnoreCase)
                || string.Equals(display, "\\Closure", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reports TYHP4142 for required parameters with no matching argument and TYHP4143 when
        /// too many positional arguments are passed (variadic / <c>...</c> unpack calls are skipped
        /// because their contribution is not statically known).
        /// </summary>
        private static void ValidateArgumentArity(
            PhpArgumentListAst? arguments,
            IReadOnlyList<ParameterInfo> parameters,
            CheckerState state,
            DiagnosticBag diagnostics,
            IBase2Ast reportNode,
            string calleeDisplayName)
        {
            var args = arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Any(a => a.IsVariadic))
            {
                // `foo(...$xs)` may supply any number of values — do not guess missing/extra.
                return;
            }

            var positionalCount = 0;
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                if (arg.Name?.ValueString is { } namedRaw)
                {
                    named.Add(namedRaw.TrimStart('$'));
                }
                else
                {
                    positionalCount++;
                }
            }

            var hasTrailingVariadic = parameters.Count > 0 && parameters[^1].IsVariadic;
            var nonVariadicParams = hasTrailingVariadic
                ? parameters.Take(parameters.Count - 1).ToList()
                : parameters.ToList();

            var consumedPositionals = 0;
            foreach (var param in nonVariadicParams)
            {
                var bareName = param.Name.TrimStart('$');
                if (named.Contains(bareName))
                {
                    continue;
                }

                if (consumedPositionals < positionalCount)
                {
                    consumedPositionals++;
                    continue;
                }

                if (param.DefaultValue is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerMissingArgument,
                        bareName,
                        calleeDisplayName);
                }
            }

            var positionalSlots = nonVariadicParams.Count(p =>
                !named.Contains(p.Name.TrimStart('$')));
            if (!hasTrailingVariadic && positionalCount > positionalSlots)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerTooManyArguments,
                    calleeDisplayName,
                    positionalSlots,
                    positionalCount);
            }
        }

        /// <summary>
        /// Resolves a callee parameter type, applying receiver generic substitution when the call
        /// targets an instance/static method on a (possibly generic) receiver.
        /// </summary>
        private static ICheckedType ResolveCalleeParameterType(
            ITypeExpression declaredType,
            CheckerState paramResolveState,
            ICheckedType? selfResolutionReceiver,
            ObjectMethodSymbol? calleeMethod,
            IDereferenceableBase? callBase,
            CheckerState callerState,
            CheckerRuleContext context,
            FunctionDeclarationSymbol? calleeFunction = null,
            Func<Dictionary<GenericTypeParameterSymbol, ICheckedType>?>? inferBindings = null)
        {
            if (selfResolutionReceiver is not null)
            {
                var memberResolved = context.ResolveMemberDeclaredType(
                    declaredType,
                    selfResolutionReceiver,
                    callerState,
                    calleeMethod,
                    callBase);

                memberResolved = ApplyInferredBindings(memberResolved, inferBindings, context);

                // Unbound method type parameters are gradual for argument checking — same policy
                // as free functions (CHECKER_GAPS P1 #14). Return typing runs argument inference.
                if (calleeMethod is not null
                    && ContainsUnboundCalleeGeneric(memberResolved, calleeMethod.GenericParameters))
                {
                    // Keep PropertyPath / Expression wrappers: collapsing those to mixed drops
                    // Story 16 inline-fn conversion (`select<R>(Expression<T, R>)` + `fn ($u) => …`).
                    // Replace only the unbound method params (e.g. `R` → mixed) so the fn is still
                    // contextually typed from the class generic (`T` → User).
                    if (PropertyPathSupport.IsPropertyPathOrExpressionType(memberResolved))
                    {
                        return SubstituteUnboundCalleeGenericsWithMixed(
                            memberResolved, calleeMethod.GenericParameters);
                    }

                    return CheckedTypes.Mixed;
                }

                return memberResolved;
            }

            if (calleeFunction is not null)
            {
                var resolved = context.ResolveFunctionDeclaredType(
                    declaredType,
                    calleeFunction,
                    callerState,
                    callBase);

                resolved = ApplyInferredBindings(resolved, inferBindings, context);

                // When the annotation still mentions an unbound callee type parameter
                // (`array<TKey, TValue>` before inference fills them), treat it as gradually
                // accepting for argument checking — the return-type path performs inference.
                if (ContainsUnboundCalleeGeneric(resolved, calleeFunction.GenericParameters))
                {
                    if (PropertyPathSupport.IsPropertyPathOrExpressionType(resolved))
                    {
                        return SubstituteUnboundCalleeGenericsWithMixed(
                            resolved, calleeFunction.GenericParameters);
                    }

                    return CheckedTypes.Mixed;
                }

                return resolved;
            }

            return context.ResolveTypeAnnotation(declaredType, paramResolveState);
        }

        /// <summary>
        /// Fills call-site bindings into a parameter type, but only when it carries a deferred
        /// callable-signature utility (<c>__CallableParametersStruct&lt;TCallable&gt;</c>,
        /// Tuple, Rest, and return-type peers) whose whole purpose is to become concrete once
        /// <c>TCallable</c> is known.
        /// </summary>
        /// <remarks>
        /// Substituting into ordinary generic parameter types would narrow them past what argument
        /// checking can soundly demand: inference binds from argument *values*, so
        /// <c>run&lt;TValue&gt;(callable&lt;TValue, TValue&gt; $cb, TValue $seed)</c> called with
        /// <c>1</c> would bind <c>TValue</c> to the literal type <c>1</c> and then require the
        /// callback to return exactly <c>1</c>. Those parameters keep the gradual mixed policy.
        /// </remarks>
        private static ICheckedType ApplyInferredBindings(
            ICheckedType type,
            Func<Dictionary<GenericTypeParameterSymbol, ICheckedType>?>? inferBindings,
            CheckerRuleContext context)
        {
            if (inferBindings is null || !ContainsDeferredCallableUtility(type))
            {
                return type;
            }

            var bindings = inferBindings();
            if (bindings is not { Count: > 0 })
            {
                return type;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(
                type, bindings, context.SymbolTree, context.GlobalScope);
        }

        private static bool ContainsDeferredCallableUtility(ICheckedType type)
        {
            if (type is GenericCheckedType generic)
            {
                if (SymbolNameTypeHelper.TryGetUtilitySymbol(generic.BaseType, out var utility)
                    && utility.Behavior is UtilityBehavior.CallableParametersStruct
                        or UtilityBehavior.CallableParametersTuple
                        or UtilityBehavior.CallableParametersRest
                        or UtilityBehavior.CallableReturnType
                        or UtilityBehavior.ReturnType)
                {
                    return true;
                }

                return generic.TypeArguments.Any(ContainsDeferredCallableUtility);
            }

            return type switch
            {
                NullableCheckedType n => ContainsDeferredCallableUtility(n.InnerType),
                UnionCheckedType u => u.Members.Any(ContainsDeferredCallableUtility),
                IntersectionCheckedType i => i.Members.Any(ContainsDeferredCallableUtility),
                CallableCheckedType c =>
                    ContainsDeferredCallableUtility(c.ReturnType)
                    || c.ParameterTypes.Any(ContainsDeferredCallableUtility),
                StructCheckedType s => s.Properties.Values.Any(p => ContainsDeferredCallableUtility(p.Type)),
                _ => false,
            };
        }

        private static bool ContainsUnboundCalleeGeneric(
            ICheckedType type,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters)
        {
            if (genericParameters.Count == 0)
            {
                return false;
            }

            return type switch
            {
                SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol gp }
                    => genericParameters.Contains(gp),
                NullableCheckedType n => ContainsUnboundCalleeGeneric(n.InnerType, genericParameters),
                GenericCheckedType g =>
                    g.TypeArguments.Any(a => ContainsUnboundCalleeGeneric(a, genericParameters)),
                UnionCheckedType u =>
                    u.Members.Any(m => ContainsUnboundCalleeGeneric(m, genericParameters)),
                IntersectionCheckedType i =>
                    i.Members.Any(m => ContainsUnboundCalleeGeneric(m, genericParameters)),
                CallableCheckedType c =>
                    ContainsUnboundCalleeGeneric(c.ReturnType, genericParameters)
                    || c.ParameterTypes.Any(p => ContainsUnboundCalleeGeneric(p, genericParameters)),
                StructCheckedType s =>
                    s.Properties.Values.Any(p => ContainsUnboundCalleeGeneric(p.Type, genericParameters)),
                _ => false,
            };
        }

        /// <summary>
        /// Replaces unbound callee type parameters with <c>mixed</c> while keeping the surrounding
        /// type shape (so <c>Expression&lt;User, R&gt;</c> becomes <c>Expression&lt;User, mixed&gt;</c>).
        /// </summary>
        private static ICheckedType SubstituteUnboundCalleeGenericsWithMixed(
            ICheckedType type,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters)
        {
            if (genericParameters.Count == 0)
            {
                return type;
            }

            return type switch
            {
                SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol gp }
                    when genericParameters.Contains(gp) => CheckedTypes.Mixed,
                NullableCheckedType n => new NullableCheckedType(
                    SubstituteUnboundCalleeGenericsWithMixed(n.InnerType, genericParameters)),
                GenericCheckedType g => new GenericCheckedType(
                    g.BaseType,
                    g.TypeArguments
                        .Select(a => SubstituteUnboundCalleeGenericsWithMixed(a, genericParameters))
                        .ToList()),
                UnionCheckedType u => CheckedTypes.UnionTypes(
                    u.Members
                        .Select(m => SubstituteUnboundCalleeGenericsWithMixed(m, genericParameters))
                        .ToList()),
                IntersectionCheckedType i => new IntersectionCheckedType(
                    i.Members
                        .Select(m => SubstituteUnboundCalleeGenericsWithMixed(m, genericParameters))
                        .ToList()),
                CallableCheckedType c => c.MapTypes(p =>
                    SubstituteUnboundCalleeGenericsWithMixed(p, genericParameters)),
                StructCheckedType s => new StructCheckedType(
                    s.Properties.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.WithType(
                            SubstituteUnboundCalleeGenericsWithMixed(pair.Value.Type, genericParameters)))),
                _ => type,
            };
        }

        private static void CheckMemberVisibility(
            BaseSymbol member,
            CheckerState state,
            IBase2Ast node,
            DiagnosticBag diagnostics)
        {
            if ((member.Visibility & MemberModifier.Private) == 0)
            {
                // Protected is intentionally not enforced here yet: trait-flattened members keep the
                // trait as their declaring object, so a naive subclass/hierarchy check would reject
                // valid `protected` access from the using class. Same gap as properties/methods.
                return;
            }

            // A private member is accessible only from within the class that declares it. The
            // member's containing scope is the declaring class's object scope, so compare its
            // declaration symbol against the enclosing class rather than the (namespace) scope that
            // contains the enclosing class. Access from outside any class (file/function scope) is
            // also rejected.
            var declaringObject = (member.ContainingScope as ObjectDeclarationScope)?.DeclarationSymbol;
            if (ReferenceEquals(declaringObject, state.EnclosingObject) && state.EnclosingObject is not null)
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics, state, node, MessageCode.CheckerMemberNotAccessible,
                member.Name, "private", state.EnclosingObject?.Name ?? "global");
        }

        private static bool TryResolveMethod(
            ICheckedType ownerType,
            string methodName,
            bool staticOnly,
            CheckerRuleContext context,
            out ObjectMethodSymbol? method)
        {
            method = null;
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(UnwrapForMemberAccess(ownerType));
            if (objectDecl is null)
            {
                return false;
            }

            // Walk inheritance / traits the same way type inference does for method calls.
            if (context.SymbolTree.ResolveMember(methodName, objectDecl, new DiagnosticBag())
                    is ObjectMethodSymbol methodSymbol
                && (!staticOnly || methodSymbol.IsStatic))
            {
                method = methodSymbol;
                return true;
            }

            return false;
        }

        private static bool TryResolveProperty(
            ICheckedType ownerType,
            string propertyName,
            CheckerRuleContext context,
            out ObjectPropertySymbol? property)
        {
            property = null;
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(UnwrapForMemberAccess(ownerType));
            if (objectDecl is null)
            {
                return false;
            }

            // Properties are keyed in Members with their leading '$' to keep them distinct from
            // same-named methods; member access yields the bare name, so normalize before lookup.
            var propertyKey = propertyName.StartsWith('$') ? propertyName : "$" + propertyName;
            if (context.SymbolTree.ResolveMember(propertyKey, objectDecl, new DiagnosticBag())
                is ObjectPropertySymbol prop)
            {
                property = prop;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Peel nullability / generic wrappers so member lookup sees the underlying object symbol.
        /// </summary>
        private static ICheckedType UnwrapForMemberAccess(ICheckedType type)
        {
            while (true)
            {
                switch (type)
                {
                    case NullableCheckedType nullable:
                        type = nullable.InnerType;
                        break;
                    case StaticCheckedType staticType:
                        type = staticType.DeclaringType;
                        break;
                    case GenericCheckedType generic:
                        type = generic.BaseType;
                        break;
                    default:
                        return type;
                }
            }
        }

        private static bool TryResolveConstant(
            ICheckedType ownerType,
            string constantName,
            CheckerRuleContext context,
            out ObjectConstantSymbol? constant)
        {
            constant = null;
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(ownerType);
            if (objectDecl is null)
            {
                return false;
            }

            // Walk inheritance / traits the same way type inference does for `Class::CONST`.
            if (context.SymbolTree.ResolveConstant(constantName, objectDecl, context.Diagnostics)
                is ObjectConstantSymbol constSymbol)
            {
                constant = constSymbol;
                return true;
            }

            return false;
        }

        /// <summary>
        /// <see cref="PhpDereferenceableAst"/> suppresses the generic checker child-walk
        /// (<see cref="SuppressChildTraversal"/>), so any nested call / member-access chain /
        /// <c>new</c> expression that only appears as a receiver or argument inside another call
        /// is otherwise never independently visited — silently skipping its own argument
        /// validation, named-argument checks, and member-visibility checks. Manually re-enter the
        /// normal check pipeline for exactly those node kinds (not plain variables/literals, which
        /// are unaffected by this gap and out of scope here).
        /// </summary>
        private static void CheckNestedCalleeIfNeeded(
            IBase2Ast? node,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (node is PhpDereferenceableAst or PhpNewAst)
            {
                context.CheckNode(node, state);
            }
        }

        private static void CheckRestrictedBuiltinCall(
            string calleeName,
            PhpDereferenceableAst deref,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (string.Equals(calleeName, "compact", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, deref, MessageCode.CheckerCompactProhibited);
            }
            else if (string.Equals(calleeName, "extract", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, deref, MessageCode.CheckerExtractProhibited);
            }
        }
    }
}
