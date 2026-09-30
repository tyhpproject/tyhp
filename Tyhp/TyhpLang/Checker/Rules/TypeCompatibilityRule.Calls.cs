using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
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
            else if (deref.Suffix is PhpArrayAccessAst arrayAccess)
            {
                CheckArrayAccess(deref, arrayAccess, state, context, diagnostics);
            }
        }

        private static void CheckCall(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // PhpDereferenceableAst suppresses the generic child walk, so nested expression
            // rules (yield / await / unary / binary / …) never see a bare call-argument
            // statement unless we CheckNode the arguments here. Inline functions are skipped
            // — CheckArgumentAgainstParameterType CheckNodes them with contextual types.
            CheckCallArgumentExpressions(call.Arguments, state, context);

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
                    nameAst.BoundSymbol = selected;
                }
                else if (CheckerHelpers.ResolveTypeAliasFactory(
                    nameAst, state, context.SymbolTree, context.GlobalScope) is { } aliasFactory)
                {
                    CheckTypeAliasFactoryCall(
                        aliasFactory,
                        call,
                        nameAst,
                        deref,
                        state,
                        context,
                        diagnostics);
                }
                else if (!CheckerHelpers.FreeFunctionCallResolves(
                    nameAst, state, context.SymbolTree, context.GlobalScope)
                    && !string.IsNullOrEmpty(calleeName))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        deref,
                        MessageCode.CheckerUndefinedFunction,
                        calleeName);
                }
            }
            else if (deref.Base is PhpDereferenceableAst chain && chain.Base is not null)
            {
                // `$c->g(...)` is parsed as deref(suffix=call, base=deref(suffix=member g, base=$c)).
                // Resolve the method on the *receiver* (`chain.Base`), not on the member-access chain
                // itself (whose type is the method/callable, not the owning object).
                string? methodName;
                bool staticOnly;
                var allowInstanceForwarding = false;
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
                        allowInstanceForwarding = CheckerHelpers.IsRelativeClassKeyword(chain.Base);
                        if (chain.Base is IBase2Ast staticBase
                            && TryReportObjectShapeUsedAsClass(staticBase, state, context, diagnostics))
                        {
                            return;
                        }

                        receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                            chain.Base, state, context, context.SymbolTree, context.GlobalScope);
                        break;
                    case PhpClassConstantAccessAst classConstAccess:
                        // `Class::method(...)` is parsed with a class-constant-access suffix.
                        methodName = GetExpressionText(classConstAccess.Member);
                        staticOnly = true;
                        allowInstanceForwarding = CheckerHelpers.IsRelativeClassKeyword(chain.Base);
                        if (chain.Base is IBase2Ast callBaseNode
                            && TryReportObjectShapeUsedAsClass(callBaseNode, state, context, diagnostics))
                        {
                            return;
                        }

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

                    if (CheckerHelpers.ReportUnresolvedReceiver(
                            diagnostics, state, chain.Base ?? deref, receiverType))
                    {
                        return;
                    }

                    if (TryResolveMethod(
                            receiverType,
                            methodName,
                            staticOnly,
                            state,
                            context,
                            out var method,
                            allowInstanceForwarding))
                    {
                        var selected = FunctionOverloadSelector.SelectMethod(
                            method!,
                            call,
                            new FunctionOverloadSelector.MethodContext
                            {
                                State = state,
                                SymbolTree = context.SymbolTree,
                                GlobalScope = context.GlobalScope,
                                InferArgumentType = expr => context.ResolveExpressionType(expr, state),
                                ResolveParameterType = (m, typeAst) =>
                                    context.ResolveMemberDeclaredType(
                                        typeAst, receiverType, state, m, chain),
                                InferBindings = (m, c) =>
                                {
                                    if (m.GenericParameters.Count == 0)
                                    {
                                        return null;
                                    }

                                    return context.TryInferGenericBindings(
                                        m.GenericParameters,
                                        m.Parameters,
                                        c,
                                        state,
                                        out var inferred,
                                        receiverType,
                                        m)
                                        && inferred.Count > 0
                                        ? inferred
                                        : null;
                                },
                            });
                        parameters = selected.Parameters;
                        selfResolutionReceiver = receiverType;
                        calleeMethod = selected;
                        callBase = chain;
                        deref.BoundSymbol = selected;
                        CheckMemberVisibility(selected, state, deref, diagnostics);
                        CheckFromCallableVisibility(selected, call, state, context, diagnostics);
                        ClosureBindSupport.Check(
                            selected, receiverType, call, deref, state, context, diagnostics);
                    }
                    else if (!staticOnly
                        && TryCheckObjectShapeInstanceCall(
                            receiverType,
                            methodName,
                            call,
                            deref,
                            state,
                            context,
                            diagnostics))
                    {
                        return;
                    }
                    else if (staticOnly
                        && CheckerHelpers.TryResolveObjectTypeAliasFactory(
                            receiverType, methodName, context.SymbolTree) is { } objectAliasFactory)
                    {
                        CheckTypeAliasFactoryCall(
                            objectAliasFactory,
                            call,
                            chain,
                            deref,
                            state,
                            context,
                            diagnostics);
                    }
                    else if (!staticOnly
                        && CheckerHelpers.IsClosedMemberReceiver(receiverType))
                    {
                        // Scalars / `\Closure` / structs cannot grow real PHP methods. Missing or
                        // `hide`d extension members are compile errors (TYHP4101), not gradual
                        // `unknown`.
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            deref,
                            MessageCode.CheckerSymbolNameNotFound,
                            methodName,
                            receiverType.DisplayName);
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

                if (CheckerHelpers.ReportUnresolvedReceiver(
                        diagnostics, state, callableExpr, calleeType))
                {
                    return;
                }

                if (CallableArityFacetBuilder.IsAnyArityCallee(calleeType))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        deref,
                        MessageCode.CheckerCallableAnyArityNotInvokable,
                        calleeType.DisplayName);
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

                // `$obj(...)` on a non-Closure `__invoke` class: check arguments against
                // `__invoke` the same way `$obj->__invoke(...)` does.
                if (!CallableArityFacetBuilder.IsClosureClassType(calleeType)
                    && TryResolveMethod(
                        calleeType, "__invoke", staticOnly: false, state, context, out var invokeMethod)
                    && invokeMethod is { IsStatic: false })
                {
                    var selected = FunctionOverloadSelector.SelectMethod(
                        invokeMethod,
                        call,
                        new FunctionOverloadSelector.MethodContext
                        {
                            State = state,
                            SymbolTree = context.SymbolTree,
                            GlobalScope = context.GlobalScope,
                            InferArgumentType = expr => context.ResolveExpressionType(expr, state),
                            ResolveParameterType = (m, typeAst) =>
                                context.ResolveMemberDeclaredType(
                                    typeAst, calleeType, state, m, deref.Base as IDereferenceableBase),
                            InferBindings = (m, c) =>
                            {
                                if (m.GenericParameters.Count == 0)
                                {
                                    return null;
                                }

                                return context.TryInferGenericBindings(
                                    m.GenericParameters,
                                    m.Parameters,
                                    c,
                                    state,
                                    out var inferred,
                                    calleeType,
                                    m)
                                    && inferred.Count > 0
                                    ? inferred
                                    : null;
                            },
                        });
                    parameters = selected.Parameters;
                    selfResolutionReceiver = calleeType;
                    calleeMethod = selected;
                    callBase = deref.Base as IDereferenceableBase;
                    CheckMemberVisibility(selected, state, deref, diagnostics);
                }
            }

            if (parameters is null)
            {
                return;
            }

            parameters = CheckerHelpers.ExcludeExtensionReceiver(parameters, calleeMethod);

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

            ValidateCallSiteGenericConstraints(
                call,
                callBase,
                deref,
                parameters,
                calleeFunction,
                calleeMethod,
                selfResolutionReceiver,
                state,
                context,
                diagnostics);

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
        /// <c>UserId()</c> / <c>Optional(typeof(int))</c> / <c>UserService::NameType()</c> — the
        /// factory is not a generic function. Type arguments on the call
        /// (<c>Optional&lt;int&gt;()</c>) are TYHP4186. Value arguments are <c>\Tyhp\Type</c>
        /// and match the alias's generic parameter arity (all optional, matching PHP emit).
        /// </summary>
        private static void CheckTypeAliasFactoryCall(
            IBaseSymbol alias,
            PhpCallAst call,
            IDereferenceableBase? callBase,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (CheckerHelpers.TypeAliasFactoryCallHasTypeArguments(callBase, call))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerTypeAliasFactoryTypeArguments,
                    alias.Name);
                return;
            }

            if (alias is ObjectTypeAliasSymbol objectAlias)
            {
                CheckMemberVisibility(objectAlias, state, reportNode, diagnostics);
            }

            var generics = CheckerHelpers.GetAliasFactoryGenericParameters(alias);
            var args = call.Arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Any(static a => a.IsVariadic))
            {
                return;
            }

            var positionalCount = args.Count(static a => a.Name is null);
            if (positionalCount > generics.Count)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerTooManyArguments,
                    alias.Name,
                    generics.Count,
                    positionalCount);
            }

            if (call.Arguments is null)
            {
                return;
            }

            ValidateNamedArguments(call.Arguments, state, reportNode, diagnostics);

            var paramNames = new HashSet<string>(
                generics.Select(gp => gp.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                var named = arg.Name?.ValueString?.TrimStart('$');
                if (!string.IsNullOrEmpty(named) && !paramNames.Contains(named))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arg,
                        MessageCode.CheckerUnknownNamedArgument,
                        named,
                        alias.Name);
                }
            }

            var expected = CheckerHelpers.ResolveNamedType(
                "Tyhp\\Type", context.SymbolTree, context.GlobalScope);
            foreach (var arg in args)
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                var argType = context.ResolveExpressionType(arg.Expression, state);
                if (!context.IsAssignableAllowingOperatorConvert(argType, expected, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arg,
                        MessageCode.CheckerIncompatibleArgumentType,
                        argType.DisplayName,
                        expected.DisplayName);
                }
            }
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
        /// Validates arguments against the callable facet whose arity matches the call.
        /// Named arguments bind to stored <see cref="CallableCheckedType.ParameterNames"/>
        /// when present; unnamed slots are not targeted by invented names. When the facet
        /// still carries unbound type parameters (first-class callable from a generic
        /// function), bind them from the argument types before assignability checks —
        /// same policy as direct-call argument-driven inference.
        /// </summary>
        private static void ValidateCallableFacetArguments(
            PhpArgumentListAst arguments,
            ICheckedType calleeType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Bare `callable` (no declared shape) is gradual — "invokable, signature unknown."
            // `CallableArityFacetBuilder.GetCallableFacets` stands in a synthetic 0-arg/mixed
            // facet for it (so `__CallableReturnType<callable>` resolves to `mixed`), but that
            // stand-in is not a real contract: it must not drive arity or argument-type checks
            // here, or every `callable $c` call site with arguments would misreport
            // TYHP-too-many-arguments. Only enforce facet checks against an actual shape.
            if (!TypeComparer.IsCallableShapeType(calleeType))
            {
                return;
            }

            var positionalCount = CallableArityFacetBuilder.CountPositionalArguments(arguments);
            var namedNames = CallableArityFacetBuilder.CollectNamedArgumentNames(arguments);
            if (!CallableArityFacetBuilder.TrySelectCallableFacetForCall(
                    calleeType, positionalCount, namedNames, out var facet)
                || facet is null)
            {
                // No exact-arity facet: nested argument expressions were already CheckNode'd
                // by CheckCall. Still report a missing/too-many-arguments diagnostic when the
                // callee has a concrete (non any-arity) shape whose arity this call cannot
                // satisfy — e.g. invoking a spliced `callable(Rest<callable(int, string): bool> ...): // bool`-typed value with too few positional arguments.
                ReportCallableValueArityMismatch(
                    calleeType, positionalCount, arguments, state, diagnostics);
                return;
            }

            var positionalArgs = new List<(PhpArgumentAst Arg, ICheckedType ArgType)>();
            foreach (var arg in arguments.GetAllNotNull())
            {
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

            var filled = new bool[effectiveFacet.ParameterTypes.Count];
            var positionalIndex = 0;
            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.IsVariadic || arg.Expression is null)
                {
                    continue;
                }

                int slot;
                if (arg.Name is not null)
                {
                    slot = CallableArityFacetBuilder.FindParameterIndexByName(
                        effectiveFacet, arg.Name.ValueString);
                    if (slot < 0)
                    {
                        if (effectiveFacet.ParameterNames is not null)
                        {
                            CheckerHelpers.ReportErrorWithDidYouMean(
                                diagnostics,
                                state,
                                arg,
                                MessageCode.CheckerUnknownNamedArgument,
                                arg.Name.ValueString ?? string.Empty,
                                CallableArityFacetBuilder.CollectFacetParameterNames(effectiveFacet),
                                arg.Name.ValueString ?? string.Empty);
                        }

                        continue;
                    }
                }
                else if (positionalIndex < effectiveFacet.ParameterTypes.Count)
                {
                    slot = positionalIndex++;
                }
                else if (effectiveFacet.LastParameterIsVariadic && effectiveFacet.ParameterTypes.Count > 0)
                {
                    slot = effectiveFacet.ParameterTypes.Count - 1;
                }
                else
                {
                    break;
                }

                if (slot >= 0 && slot < filled.Length)
                {
                    filled[slot] = true;
                }

                CheckCallableFacetArgumentSlot(
                    arg, effectiveFacet.ParameterTypes[slot], state, context, diagnostics);
            }

            if (effectiveFacet.ParameterNames is null || effectiveFacet.LastParameterIsVariadic)
            {
                return;
            }

            for (var i = 0; i < filled.Length; i++)
            {
                if (filled[i])
                {
                    continue;
                }

                var missingName = effectiveFacet.ParameterNames is { } names
                    && i < names.Count
                    && names[i] is { } named
                        ? named.TrimStart('$')
                        : CallableSignatureReflection.PositionalPropertyName(i).TrimStart('$');

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    arguments,
                    MessageCode.CheckerMissingArgument,
                    missingName,
                    calleeType.DisplayName);
            }
        }

        private static void CheckCallableFacetArgumentSlot(
            PhpArgumentAst arg,
            ICheckedType paramType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Leftover unbound generics (not constrained by any argument) are gradual —
            // same policy as ResolveCalleeParameterType for direct named calls, including
            // keeping a symbol-name brand (`__ClassName<T>` → `__ClassName<object>`).
            if (CallableGenericInference.ContainsUnboundGeneric(paramType))
            {
                paramType = GradualizeUnboundCallableParameter(paramType);
            }

            if (arg.Expression is PhpInlineFunctionAst closure)
            {
                var ambient = state.SnapShot();
                ClosureParameterInference.SetExpectedClosureTypeFromArgument(paramType, ambient);
                context.CheckNode(closure, ambient);
                return;
            }

            var previousExpected = state.ExpectedExpressionType;
            if (ContextualNewInference.IsUsableExpectedType(paramType))
            {
                state.ExpectedExpressionType = paramType;
            }

            try
            {
                if (ContextualNewInference.IsBareGenericConstruction(arg.Expression))
                {
                    context.CheckNode(arg.Expression!, state);
                }

            // Plain assignability only: the callee here is an arbitrary runtime callable value
            // (`$fn(...)`), which `AliasConverter.TryResolveCalleeParameters` cannot statically
            // resolve to a declared parameter list, so it never inserts an implicit-convert
            // rewrite at this call form. Accepting convert here would let the checker pass while
            // emit still hands the unconverted object to PHP, throwing a TypeError at runtime.
            if (GeneratorBodyInference.TryHandleYieldSendAssignment(
                    arg.Expression, paramType, state))
            {
                return;
            }

            var argType = context.ResolveExpressionType(arg.Expression!, state);
            if (!context.IsAssignable(argType, paramType, state)
                && !CheckerHelpers.IsArrayCallableLiteral(arg.Expression, paramType, context, state))
            {
                if (!CheckerHelpers.TryReportObjectShapeRequiresGuard(
                        diagnostics, state, arg, argType, paramType)
                    && !CheckerHelpers.TryReportCallableShapeRequiresGuard(
                        diagnostics, state, arg, argType, paramType)
                    && !CheckerHelpers.TryReportNewConstraintFailure(
                        diagnostics, state, arg, argType, paramType,
                        context.SymbolTree, context.GlobalScope)
                    && !SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                        argType, paramType, state, context.SymbolTree, context.GlobalScope,
                        diagnostics, arg))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerIncompatibleArgumentType,
                        argType.DisplayName, paramType.DisplayName);
                }
            }
            }
            finally
            {
                state.ExpectedExpressionType = previousExpected;
            }
        }

        /// <summary>
        /// Reports <see cref="MessageCode.CheckerMissingArgument"/> /
        /// <see cref="MessageCode.CheckerTooManyArguments"/> for an <c>$fn(...)</c> value call
        /// whose positional argument count matches no facet of <paramref name="calleeType"/>.
        /// Any-arity facets (<c>callable(...): R</c>) accept every count and are skipped;
        /// callees with no reflectable facet at all (e.g. still-unbound generics) are left to
        /// whatever diagnostics the argument expressions themselves already produced.
        /// </summary>
        private static void ReportCallableValueArityMismatch(
            ICheckedType calleeType,
            int positionalCount,
            PhpArgumentListAst arguments,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var facets = CallableArityFacetBuilder.GetCallableFacets(calleeType);
            if (facets.Count == 0 || facets.Any(f => f.IsAnyArity))
            {
                return;
            }

            // Cite the facet whose arity is closest to what was actually passed, mirroring
            // SelectFunctionOverloadForCall's widest/narrowest-candidate rule for overloads.
            var closest = facets
                .OrderBy(f => Math.Abs(
                    (f.LastParameterIsVariadic ? f.ParameterTypes.Count - 1 : f.ParameterTypes.Count)
                    - positionalCount))
                .First();

            var requiredCount = closest.LastParameterIsVariadic
                ? closest.ParameterTypes.Count - 1
                : closest.ParameterTypes.Count;

            if (positionalCount < requiredCount)
            {
                var missingName =
                    closest.ParameterNames is { } names
                    && positionalCount < names.Count
                    && names[positionalCount] is { } named
                        ? named.TrimStart('$')
                        : CallableSignatureReflection.PositionalPropertyName(positionalCount).TrimStart('$');

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    arguments,
                    MessageCode.CheckerMissingArgument,
                    missingName,
                    calleeType.DisplayName);
                return;
            }

            if (!closest.LastParameterIsVariadic && positionalCount > closest.ParameterTypes.Count)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    arguments,
                    MessageCode.CheckerTooManyArguments,
                    calleeType.DisplayName,
                    closest.ParameterTypes.Count,
                    positionalCount);
            }
        }

        /// <summary>
        /// Instance call on a shape-typed receiver: validate against the shape method, or
        /// report TYHP4359 for <c>__construct</c>. Returns true when the call was fully handled.
        /// </summary>
        private static bool TryCheckObjectShapeInstanceCall(
            ICheckedType receiverType,
            string methodName,
            PhpCallAst call,
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (TypeComparer.TryFindShapeInstanceMethod(
                    receiverType,
                    methodName,
                    context.SymbolTree,
                    context.GlobalScope,
                    out var shape,
                    out var method))
            {
                if (ObjectShapeMemberBuilder.IsConstructabilityName(methodName))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        deref,
                        MessageCode.CheckerObjectShapeConstructNotCallable,
                        shape.DisplayName);
                    return true;
                }

                if (call.Arguments is not null
                    && CallableArityFacetBuilder.IsCallableFacetType(method.CallableType))
                {
                    ValidateNamedArguments(call.Arguments, state, deref, diagnostics);
                    ValidateCallableFacetArguments(
                        call.Arguments,
                        method.CallableType,
                        state,
                        context,
                        diagnostics);
                }

                return true;
            }

            if (!TypeComparer.TryGetObjectShapeFromType(receiverType, out shape))
            {
                return false;
            }

            if (ObjectShapeMemberBuilder.IsConstructabilityName(methodName))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerObjectShapeConstructNotCallable,
                    shape.DisplayName);
                return true;
            }

            // Nominal ∩ shape intersections may still resolve members on the class arm
            // through a later path; only close missing members on a shape-only receiver.
            if (receiverType is IntersectionCheckedType)
            {
                return false;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                deref,
                MessageCode.CheckerSymbolNameNotFound,
                methodName,
                receiverType.DisplayName);
            return true;
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

            if (CheckerHelpers.ReportUnresolvedReceiver(
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
            else if (TryResolveMethod(receiverType, memberName, staticOnly: false, state, context, out var method))
            {
                CheckMemberVisibility(method!, state, deref, diagnostics);
            }
            else if (TypeComparer.TryFindShapeInstanceProperty(
                receiverType,
                memberName,
                context.SymbolTree,
                context.GlobalScope,
                out _,
                out _))
            {
                // Shape properties are public by construction.
            }
            else if (StructShapeTypeBuilder.TryGetProperty(
                receiverType,
                memberName,
                context.SymbolTree,
                context.GlobalScope,
                out _))
            {
                // Inline / alias struct-shape fields are public by construction.
            }
            else if (TypeComparer.TryFindShapeInstanceMethod(
                receiverType,
                memberName,
                context.SymbolTree,
                context.GlobalScope,
                out var shape,
                out _))
            {
                if (ObjectShapeMemberBuilder.IsConstructabilityName(memberName))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        deref,
                        MessageCode.CheckerObjectShapeConstructNotCallable,
                        shape.DisplayName);
                }
            }
            else if (TypeComparer.IsObjectShapeReceiver(receiverType)
                && ObjectShapeMemberBuilder.IsConstructabilityName(memberName)
                && TypeComparer.TryGetObjectShapeFromType(receiverType, out var constructShape))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerObjectShapeConstructNotCallable,
                    constructShape.DisplayName);
            }
            else if ((TypeComparer.IsObjectShapeReceiver(receiverType)
                    && receiverType is not IntersectionCheckedType)
                || CheckerHelpers.IsClosedMemberReceiver(receiverType))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerSymbolNameNotFound,
                    memberName,
                    receiverType.DisplayName);
            }
        }

        private static void CheckArrayAccess(
            PhpDereferenceableAst deref,
            PhpArrayAccessAst arrayAccess,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckNestedCalleeIfNeeded(deref.Base, state, context);
            if (deref.Base is null)
            {
                return;
            }

            if (arrayAccess.IndexExpression is IBase2Ast indexNode)
            {
                context.CheckNode(indexNode, state);
            }

            var receiverType = context.ResolveExpressionType(deref.Base as IExpression ?? deref, state);
            if (CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, deref.Base, receiverType))
            {
                return;
            }

            if (CheckerHelpers.ReportUnresolvedReceiver(
                    diagnostics, state, deref.Base, receiverType))
            {
                return;
            }

            if (CheckerHelpers.IsUnnarrowedMixed(receiverType))
            {
                return;
            }

            if (!TryDescribeIndexableReceiver(
                    receiverType,
                    state,
                    context,
                    out var keyType,
                    out _))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerInvalidArrayAccess,
                    receiverType.DisplayName);
                return;
            }

            if (GenericInheritanceBindings.IsIllegalArrayAccessKeyType(keyType))
            {
                // Direct `\ArrayAccess<Config, mixed>` already reported TYHP4330 at the
                // type-expression site. Re-reporting on `$o["x"]` is the same finding on a
                // different span. Substituted implementors (`Box<Config> implements
                // ArrayAccess<T, mixed>`) still need the usage-site diagnostic.
                if (!GenericInheritanceBindings.IsDirectArrayAccessInstantiation(receiverType))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        deref,
                        MessageCode.CheckerArrayAccessKeyNotOffset,
                        keyType.DisplayName);
                }

                // TKey is already invalid (a struct/array cannot be a PHP offset). Comparing the
                // actual index expression against it would only produce a confusing cascade
                // (e.g. a spurious 4008 "cannot assign string to \Config" on top of the 4330 the
                // receiver's own generic annotation already reported).
                return;
            }

            if (ArrayAccessShapeSupport.TryGetStructType(
                    receiverType,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out var shapeStruct))
            {
                CheckArrayAccessShape(
                    deref,
                    arrayAccess,
                    shapeStruct,
                    state,
                    context,
                    diagnostics);
                return;
            }

            // Int-alias structs (`__CallableParametersTuple`, `CallableArgs*`) are indexed by
            // field key, not as homogeneous ArrayAccess. `$args[0]` must not be checked as
            // "literal 0 assignable to string / the first field type".
            if (IsIntegerAliasStruct(receiverType))
            {
                return;
            }

            // `$obj[] = $v` lowers to `offsetSet(null, $v)`. Shapes already rejected append
            // above. Homogeneous ArrayAccess rejects it when null is not a legal offsetSet key
            // (WeakMap `offsetSet(TKey)` forbids null; ArrayAccess `offsetSet(null|TKey)` allows it).
            // Plain `array` / string-like receivers reach this same branch (there is no index
            // expression on `$arr[] = $v` either) and must stay unchecked — only a genuine
            // `ArrayAccess`-implementing object has an `offsetSet` contract to enforce.
            if (arrayAccess.IndexExpression is not IExpression indexExpr)
            {
                if (IsArrayAccessObjectReceiver(receiverType))
                {
                    CheckHomogeneousArrayAccessAppend(
                        deref, receiverType, keyType, state, context, diagnostics);
                }

                return;
            }

            var indexType = context.ResolveExpressionType(indexExpr, state);
            if (CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, indexExpr, indexType))
            {
                return;
            }

            if (indexType is UnresolvedCheckedType
                || TypeComparer.IsMixedType(keyType)
                || context.IsAssignable(indexType, keyType, state))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                indexExpr,
                MessageCode.CheckerTypeMismatch,
                indexType.DisplayName,
                keyType.DisplayName);
        }

        /// <summary>
        /// <c>$obj[] =</c> is <c>offsetSet(null, $v)</c>. When the implementor's <c>offsetSet</c>
        /// key parameter does not accept null (WeakMap <c>TKey extends object</c>), report a type
        /// error. Fall back to the ArrayAccess <c>TKey</c> when <c>offsetSet</c> cannot be resolved.
        /// </summary>
        private static void CheckHomogeneousArrayAccessAppend(
            PhpDereferenceableAst deref,
            ICheckedType receiverType,
            ICheckedType keyType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var offsetType = keyType;
            if (TryResolveMethod(
                    receiverType,
                    "offsetSet",
                    staticOnly: false,
                    state,
                    context,
                    out var offsetSet)
                && offsetSet is { Parameters.Count: > 0 } method
                && method.Parameters[0].DeclaredType is { } declared)
            {
                offsetType = context.ResolveMemberDeclaredType(
                    declared, receiverType, state, method);
            }

            if (TypeComparer.IsMixedType(offsetType)
                || TypeComparer.IsUnresolvedType(offsetType)
                || context.IsAssignable(CheckedTypes.Null, offsetType, state))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                deref,
                MessageCode.CheckerTypeMismatch,
                CheckedTypes.Null.DisplayName,
                offsetType.DisplayName);
        }

        private static bool IsIntegerAliasStruct(ICheckedType receiverType)
        {
            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type is StructCheckedType { HasIntegerKeyAliases: true };
        }

        /// <summary>
        /// True only for a receiver that is a genuine <c>ArrayAccess</c>-implementing object
        /// (e.g. <c>WeakMap</c>, <c>SplObjectStorage</c>, a user <c>ArrayAccess&lt;TKey, TValue&gt;</c>
        /// implementor). Plain PHP <c>array</c>, string-like types, and both struct
        /// representations (<see cref="StructCheckedType"/> and a declared <c>struct</c>
        /// <see cref="ObjectDeclarationSymbol"/>) reach the same append branch but have no
        /// <c>offsetSet</c> contract to enforce — by the time control reaches this check,
        /// <c>TryDescribeIndexableReceiver</c> has already confirmed non-object receivers via an
        /// earlier branch, so anything left that resolves to a non-struct object declaration must
        /// have matched <see cref="GenericInheritanceBindings.TryGetArrayAccessTypes"/>.
        /// </summary>
        private static bool IsArrayAccessObjectReceiver(ICheckedType receiverType)
        {
            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return CheckerHelpers.TryGetObjectDeclaration(type) is { IsStruct: false };
        }

        private static void CheckArrayAccessShape(
            PhpDereferenceableAst deref,
            PhpArrayAccessAst arrayAccess,
            ICheckedType shapeStruct,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (arrayAccess.IndexExpression is not IExpression indexExpr)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerArrayAccessShapeAppend);
                return;
            }

            var keys = MagicUtilityTypeResolver.IndexKeysOf(
                shapeStruct,
                state,
                context.SymbolTree,
                context.GlobalScope,
                context.ResolveTypeAnnotation);
            var indexType = context.ResolveExpressionType(indexExpr, state);
            var finiteKeys = ArrayAccessShapeSupport.TryGetFiniteLiteralInhabitants(keys, out _);

            if (finiteKeys
                && (CheckerHelpers.IsUnnarrowedMixed(indexType)
                    || ArrayAccessShapeSupport.IsWideOffsetType(indexType))
                && !context.IsAssignable(indexType, keys, state))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    indexExpr,
                    MessageCode.CheckerArrayAccessShapeWideKey,
                    indexType.DisplayName);
                return;
            }

            if (CheckerHelpers.ReportMixedRequiresNarrowing(
                    diagnostics, state, indexExpr, indexType))
            {
                return;
            }

            if (indexType is UnresolvedCheckedType
                || TypeComparer.IsMixedType(keys)
                || context.IsAssignable(indexType, keys, state))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                indexExpr,
                MessageCode.CheckerTypeMismatch,
                indexType.DisplayName,
                keys.DisplayName);
        }

        private static bool TryDescribeIndexableReceiver(
            ICheckedType receiverType,
            CheckerState state,
            CheckerRuleContext context,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Mixed;
            valueType = CheckedTypes.Mixed;

            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                if (union.Members.Count == 0)
                {
                    return false;
                }

                ICheckedType? unionKey = null;
                ICheckedType? unionValue = null;
                foreach (var member in union.Members)
                {
                    if (!TryDescribeIndexableReceiver(member, state, context, out var memberKey, out var memberValue))
                    {
                        return false;
                    }

                    unionKey = unionKey is null
                        ? memberKey
                        : TypeComparer.UnionTypes(unionKey, memberKey, context.SymbolTree, context.GlobalScope);
                    unionValue = unionValue is null
                        ? memberValue
                        : TypeComparer.UnionTypes(unionValue, memberValue, context.SymbolTree, context.GlobalScope);
                }

                keyType = unionKey ?? CheckedTypes.Mixed;
                valueType = unionValue ?? CheckedTypes.Mixed;
                return true;
            }

            if (type is GenericCheckedType generic && IsArrayBaseTypeName(generic.BaseType))
            {
                if (generic.TypeArguments.Count >= 2)
                {
                    keyType = generic.TypeArguments[0];
                    valueType = generic.TypeArguments[^1];
                }
                else if (generic.TypeArguments.Count == 1)
                {
                    keyType = CheckedTypes.PhpArrayKey;
                    valueType = generic.TypeArguments[0];
                }

                return true;
            }

            if (TypeInferrerStringLike(type))
            {
                keyType = CheckedTypes.Int;
                valueType = CheckedTypes.String;
                return true;
            }

            if (IsArrayBaseTypeName(type))
            {
                keyType = CheckedTypes.PhpArrayKey;
                valueType = CheckedTypes.Mixed;
                return true;
            }

            if (type is StructCheckedType structType)
            {
                // Positional bags expose both int aliases (`$args[0]`) and `$_1` string names.
                keyType = structType.HasIntegerKeyAliases
                    ? TypeComparer.UnionTypes(
                        CheckedTypes.Int, CheckedTypes.String, context.SymbolTree, context.GlobalScope)
                    : CheckedTypes.String;
                valueType = CheckedTypes.Mixed;
                return true;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is { IsStruct: true })
            {
                keyType = CheckedTypes.String;
                valueType = CheckedTypes.Mixed;
                return true;
            }

            return GenericInheritanceBindings.TryGetArrayAccessTypes(
                receiverType,
                state,
                context.SymbolTree,
                context.GlobalScope,
                context.ResolveTypeAnnotation,
                out keyType,
                out valueType);
        }

        private static bool IsArrayBaseTypeName(ICheckedType baseType)
        {
            var name = baseType.DisplayName;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lastSegment = name.TrimStart('?').TrimStart('\\');
            var angle = lastSegment.IndexOf('<');
            if (angle >= 0)
            {
                lastSegment = lastSegment[..angle];
            }

            return string.Equals(lastSegment, "array", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TypeInferrerStringLike(ICheckedType type) =>
            type switch
            {
                LiteralCheckedType literal => literal.Value is string,
                UnionCheckedType union => union.Members.Count > 0 && union.Members.All(TypeInferrerStringLike),
                _ => CheckerHelpers.IsBuiltInName(type, "string"),
            };

        private static void CheckStaticMemberAccess(
            PhpDereferenceableAst deref,
            PhpStaticMemberAccessAst staticAccess,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckNestedCalleeIfNeeded(deref.Base, state, context);
            if (deref.Base is IBase2Ast staticBase
                && TryReportObjectShapeUsedAsClass(staticBase, state, context, diagnostics))
            {
                return;
            }

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

            // Class-name receivers (`Point::$x`) resolve as Unresolved value types; skip those
            // the same way mixed does. `$hole::foo` is a real Unresolved value receiver.
            if (deref.Base is not (PhpNameAst or PhpBuiltinTypeAst or TyhpGenericIdentifierAst)
                && CheckerHelpers.ReportUnresolvedReceiver(
                    diagnostics, state, deref.Base ?? deref, receiverType))
            {
                return;
            }

            var memberName = GetExpressionText(staticAccess.Member);
            if (memberName is null)
            {
                return;
            }

            if (TryResolveMethod(receiverType, memberName, staticOnly: true, state, context, out var method))
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

            if (deref.Base is IBase2Ast constBase
                && TryReportObjectShapeUsedAsClass(constBase, state, context, diagnostics))
            {
                return;
            }

            // Bare class names are not value expressions, so ResolveExpressionType yields
            // unresolved; resolve them as type receivers the same way instanceof targets do.
            var receiverNode = deref.Base as IBase2Ast ?? deref;
            var receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                receiverNode,
                state,
                context,
                context.SymbolTree,
                context.GlobalScope);

            // Bare class names are resolved above; `$hole::FOO` is an Unresolved value receiver.
            if (deref.Base is not (PhpNameAst or PhpBuiltinTypeAst or TyhpGenericIdentifierAst)
                && CheckerHelpers.ReportUnresolvedReceiver(
                    diagnostics, state, deref.Base ?? deref, receiverType))
            {
                return;
            }

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
            var seenNames = new Dictionary<string, PhpArgumentAst>(StringComparer.OrdinalIgnoreCase);
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
                            diagnostics, state, arg, MessageCode.CheckerNamedAfterUnpack, arg.Name.ValueString ?? string.Empty);
                    }

                    sawNamed = true;
                    var named = arg.Name.ValueString ?? string.Empty;
                    if (seenNames.TryGetValue(named, out var firstArg))
                    {
                        var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, arg);
                        diagnostics.AddDuplicateFromAst(
                            MessageCode.CheckerDuplicateNamedArgument,
                            arg,
                            fileName,
                            firstArg,
                            fileName,
                            arg.Name.ValueString ?? string.Empty);
                    }
                    else
                    {
                        seenNames[named] = arg;
                    }
                }
                else if (sawNamed)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerPositionalAfterNamed);
                }
            }
        }

        /// <summary>
        /// After overload selection, check inferred (or explicit) type arguments against each
        /// parameter's <c>extends</c> bound — the same <see cref="GenericTypeArgumentValidator"/>
        /// path as <c>Box&lt;Bad&gt;</c>. Overload scoring must not report these; it only infers.
        /// </summary>
        private static void ValidateCallSiteGenericConstraints(
            PhpCallAst call,
            IDereferenceableBase? callBase,
            IBase2Ast reportNode,
            IReadOnlyList<ParameterInfo> parameters,
            FunctionDeclarationSymbol? calleeFunction,
            ObjectMethodSymbol? calleeMethod,
            ICheckedType? receiverType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var genericParameters = calleeFunction?.GenericParameters
                ?? calleeMethod?.GenericParameters;
            if (genericParameters is not { Count: > 0 })
            {
                return;
            }

            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            var typeArgList = TypeInferrer.TryGetCallSiteTypeArgumentList(callBase);
            if (typeArgList is not null)
            {
                bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
                var typeArgs = typeArgList.GetAllNotNull().ToList();
                var count = Math.Min(genericParameters.Count, typeArgs.Count);
                for (var i = 0; i < count; i++)
                {
                    var argType = context.ResolveTypeAnnotation(typeArgs[i], state, isUserTypeDeclaration: false);

                    // Bare `...` is not a real type argument (unknown-arity callables are
                    // `callable(...): TReturn` shapes). It never reaches
                    // `GenericTypeArgumentValidator.ValidateInstantiation` here (explicit
                    // call-site type arguments bind straight to the callee's generic
                    // parameters), so reject it directly instead of silently binding the
                    // generic parameter to the sentinel type.
                    if (argType is CallableArityWildcardCheckedType)
                    {
                        CheckerHelpers.ReportError(
                            diagnostics, state, typeArgs[i], MessageCode.CheckerCallableEllipsisNotAllowed);
                        continue;
                    }

                    if (!TypeComparer.IsUnresolvedType(argType))
                    {
                        bindings[genericParameters[i]] = argType;
                    }
                }
            }
            else if (context.TryInferGenericBindings(
                    genericParameters,
                    parameters,
                    call,
                    state,
                    out var inferred,
                    receiverType,
                    calleeMethod)
                && inferred.Count > 0)
            {
                bindings = inferred;
            }

            if (bindings is not { Count: > 0 })
            {
                return;
            }

            GenericTypeArgumentValidator.ValidateInferredBindings(
                bindings,
                genericParameters,
                reportNode,
                state,
                context.SymbolTree,
                context.GlobalScope,
                diagnostics,
                (typeAst, resolveState, isReturn, isUser) =>
                    context.ResolveTypeAnnotation(typeAst, resolveState, isReturn, isUser),
                parameters);
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
            // actually carries a deferred expanding utility (see ApplyInferredBindings).
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
                    && (calleeGenerics is { Count: > 0 } || calleeMethod is not null)
                    && context.TryInferGenericBindings(
                        calleeGenerics ?? [],
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

                        if (ParameterPack.TryGetSlice(spreadRestType, out var spreadSlice))
                        {
                            var restArgs = CollectRemainingPositionalRestArgs(
                                argsList, argIndex, parameters, state, context, diagnostics,
                                out var restHasSpread);
                            ValidateCallableParametersRestArguments(
                                restArgs,
                                spreadSlice.CallableArg,
                                state,
                                context,
                                diagnostics,
                                calleeDisplayName,
                                wrapperPrefixCount: restParamIndex,
                                arityReportNode ?? arg,
                                restHasSpread,
                                spreadSlice.Start);
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

                if (param.IsVariadic
                    && arg.Name is null
                    && ParameterPack.TryGetArraySlice(expectedParamType, out var arraySlice))
                {
                    var sliceArrayArgs = CollectRemainingPositionalRestArgs(
                        argsList, argIndex, parameters, state, context, diagnostics, out _);
                    ValidateCallableParametersSliceArrayArguments(
                        sliceArrayArgs,
                        arraySlice,
                        state,
                        context,
                        diagnostics);
                    restUnpackDone = true;
                    break;
                }

                if (param.IsVariadic
                    && arg.Name is null
                    && ParameterPack.TryGetSlice(expectedParamType, out var valueSlice))
                {
                    var restArgs = CollectRemainingPositionalRestArgs(
                        argsList, argIndex, parameters, state, context, diagnostics, out var restHasSpread);
                    ValidateCallableParametersRestArguments(
                        restArgs,
                        valueSlice.CallableArg,
                        state,
                        context,
                        diagnostics,
                        calleeDisplayName,
                        wrapperPrefixCount: restParamIndex,
                        arityReportNode ?? arg,
                        restHasSpread,
                        valueSlice.Start);
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
                    return;
                }

                if (ParameterPack.TryGetSlice(expectedRestType, out var emptySlice))
                {
                    ValidateCallableParametersRestArguments(
                        [],
                        emptySlice.CallableArg,
                        state,
                        context,
                        diagnostics,
                        calleeDisplayName,
                        wrapperPrefixCount: restParamIndex,
                        arityReportNode ?? callBase as IBase2Ast ?? arguments,
                        hasSpread: false,
                        emptySlice.Start);
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
            bool hasSpread,
            int startIndex = 0)
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

            if (startIndex > 0)
            {
                nonVariadic = startIndex >= nonVariadic.Count
                    ? []
                    : nonVariadic.Skip(startIndex).ToList();
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
        /// Each extra zip array is <c>array&lt;P_{TStart+i}&gt;</c>. <c>TMin</c> is the minimum
        /// number of such arrays.
        /// </summary>
        private static void ValidateCallableParametersSliceArrayArguments(
            IReadOnlyList<PhpArgumentAst> arrayArgs,
            ParameterPack.SliceInfo slice,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (arrayArgs.Count < slice.Min)
            {
                var reportNode = arrayArgs.Count > 0 ? (IBase2Ast)arrayArgs[0] : null;
                if (reportNode is not null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerMissingArgument,
                        slice.Start.ToString(),
                        "array_map");
                }
            }

            for (var i = 0; i < arrayArgs.Count; i++)
            {
                var arg = arrayArgs[i];
                if (arg.Expression is null)
                {
                    continue;
                }

                if (!ParameterPack.TryGetSliceParameterAt(slice, i, out var elementType))
                {
                    continue;
                }

                // One-arg `array<V>` is list shorthand for `array<int|string, V>` — match
                // `GenericTypeArgumentValidator.NormalizeArrayLikeArguments` so a typed
                // `array<string>` argument is not rejected as `array<int|string, string>`.
                var expectedArray = new GenericCheckedType(
                    CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                    [CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.String), elementType]);
                CheckArgumentAgainstParameterType(arg, expectedArray, state, context, diagnostics);
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
            if (GeneratorBodyInference.TryHandleYieldSendAssignment(
                    arg.Expression, expectedParamType, state))
            {
                return;
            }

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
                if (!CheckerHelpers.TryReportObjectShapeRequiresGuard(
                        diagnostics, state, arg, argType, expectedParamType)
                    && !CheckerHelpers.TryReportCallableShapeRequiresGuard(
                        diagnostics, state, arg, argType, expectedParamType)
                    && !CheckerHelpers.TryReportNewConstraintFailure(
                        diagnostics, state, arg, argType, expectedParamType,
                        context.SymbolTree, context.GlobalScope)
                    && !SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
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

            var previousExpected = state.ExpectedExpressionType;
            if (ContextualNewInference.IsUsableExpectedType(expectedParamType))
            {
                state.ExpectedExpressionType = expectedParamType;
            }

            try
            {
                if (ContextualNewInference.IsBareGenericConstruction(arg.Expression))
                {
                    context.CheckNode(arg.Expression, state);
                }

            if (ExternTypeUse.IsTyhpSource(state, arg))
            {
                var externParams = ExternTypeUse.CollectExternSymbols(expectedParamType);
                var reported = false;
                foreach (var externParam in externParams)
                {
                    ExternTypeUse.Report(diagnostics, state, arg, externParam);
                    reported = true;
                }

                if (reported)
                {
                    return;
                }
            }

            // Story 16 Phase 1: an inline fn at a PropertyPath<TCallableShape> parameter must be an arrow
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

            // Story 16 Phase 2: Expression<TCallableShape> accepts an arrow fn with a supported body,
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

            if (GeneratorBodyInference.TryHandleYieldSendAssignment(
                    arg.Expression, expectedParamType, state))
            {
                return;
            }

            if (!context.IsAssignableAllowingOperatorConvert(argType, expectedParamType, state)
                && !CheckerHelpers.IsArrayCallableLiteral(arg.Expression, expectedParamType, context, state))
            {
                if (!CheckerHelpers.TryReportObjectShapeRequiresGuard(
                        diagnostics, state, arg, argType, expectedParamType)
                    && !CheckerHelpers.TryReportCallableShapeRequiresGuard(
                        diagnostics, state, arg, argType, expectedParamType)
                    && !CheckerHelpers.TryReportNewConstraintFailure(
                        diagnostics, state, arg, argType, expectedParamType,
                        context.SymbolTree, context.GlobalScope)
                    && !SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                        argType, expectedParamType, state, context.SymbolTree, context.GlobalScope,
                        diagnostics, arg))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerIncompatibleArgumentType,
                        argType.DisplayName, expectedParamType.DisplayName);
                }
            }
            }
            finally
            {
                state.ExpectedExpressionType = previousExpected;
            }
        }

        /// <summary>
        /// Story 16 Phase 1 — validate an inline function passed to a
        /// <c>PropertyPath&lt;TCallableShape&gt;</c> parameter: arrow syntax only, property-chain body only.
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
        /// <c>Expression&lt;TCallableShape&gt;</c> parameter: arrow syntax, supported body, assigned captures.
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
            ExpressionTreeSupport.TryValidateInlineFnCapture(
                closure, expressionType, state, diagnostics, arg);
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
                memberResolved = ApplyExtensionBlockBindings(
                    memberResolved, calleeMethod, inferBindings, context);

                // Unbound method type parameters are gradual for argument checking — same policy
                // as free functions (CHECKER_GAPS P1 #14). Return typing runs argument inference.
                if (calleeMethod is not null
                    && ContainsUnboundCalleeGeneric(memberResolved, calleeMethod.GenericParameters))
                {
                    return PreserveShapeForUnboundCalleeGenerics(
                        memberResolved, calleeMethod.GenericParameters);
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
                    return PreserveShapeForUnboundCalleeGenerics(
                        resolved, calleeFunction.GenericParameters);
                }

                return resolved;
            }

            return context.ResolveTypeAnnotation(declaredType, paramResolveState);
        }

        /// <summary>
        /// Fills call-site bindings into a parameter type, but only when it carries a deferred
        /// expanding utility (<c>__CallableParametersStruct&lt;TCallable&gt;</c>, Tuple, Rest,
        /// return-type peers, and <c>__Properties&lt;T&gt;</c>) whose whole purpose is to become
        /// concrete once the sibling type argument is known.
        /// </summary>
        /// <remarks>
        /// Substituting into ordinary generic parameter types would narrow them past what argument
        /// checking can soundly demand: inference binds from argument *values*, so
        /// <c>run&lt;TValue&gt;(callable(TValue): TValue $cb, TValue $seed)</c> called with
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

        /// <summary>
        /// Substitutes block-target type parameters (<c>TThisCallable</c> on
        /// <c>Closure::then</c>) into a parameter type. Method type parameters stay
        /// gradual — substituting them here would lock a callback's return to a value
        /// argument seen earlier in the same call.
        /// </summary>
        private static ICheckedType ApplyExtensionBlockBindings(
            ICheckedType type,
            ObjectMethodSymbol? method,
            Func<Dictionary<GenericTypeParameterSymbol, ICheckedType>?>? inferBindings,
            CheckerRuleContext context)
        {
            if (method is null
                || inferBindings is null
                || !ContainsForeignGenericParameter(type, method.GenericParameters))
            {
                return type;
            }

            var bindings = inferBindings();
            if (bindings is not { Count: > 0 })
            {
                return type;
            }

            var blockOnly = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            foreach (var pair in bindings)
            {
                if (!method.GenericParameters.Contains(pair.Key))
                {
                    blockOnly[pair.Key] = pair.Value;
                }
            }

            if (blockOnly.Count == 0)
            {
                return type;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(
                type, blockOnly, context.SymbolTree, context.GlobalScope);
        }

        private static bool ContainsForeignGenericParameter(
            ICheckedType type,
            IReadOnlyList<GenericTypeParameterSymbol> methodParameters)
        {
            switch (type)
            {
                case SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol parameter }:
                    return !methodParameters.Contains(parameter);
                case NullableCheckedType nullable:
                    return ContainsForeignGenericParameter(nullable.InnerType, methodParameters);
                case GenericCheckedType generic:
                    return ContainsForeignGenericParameter(generic.BaseType, methodParameters)
                        || generic.TypeArguments.Any(argument =>
                            ContainsForeignGenericParameter(argument, methodParameters));
                case UnionCheckedType union:
                    return union.Members.Any(member =>
                        ContainsForeignGenericParameter(member, methodParameters));
                case IntersectionCheckedType intersection:
                    return intersection.Members.Any(member =>
                        ContainsForeignGenericParameter(member, methodParameters));
                case CallableCheckedType callable:
                    return ContainsForeignGenericParameter(callable.ReturnType, methodParameters)
                        || callable.ParameterTypes.Any(parameterType =>
                            ContainsForeignGenericParameter(parameterType, methodParameters));
                default:
                    return false;
            }
        }

        private static bool ContainsDeferredCallableUtility(ICheckedType type)
        {
            if (type is GenericCheckedType generic)
            {
                if (SymbolNameTypeHelper.TryGetUtilitySymbol(generic.BaseType, out var utility)
                    && UtilityTypeResolver.IsDeferredExpandingUtility(utility.Behavior))
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
                ParameterPackCheckedType p =>
                    (p.SourceCallable is not null && ContainsDeferredCallableUtility(p.SourceCallable))
                    || p.Members.Any(ContainsDeferredCallableUtility),
                HomogeneousVariadicCheckedType h => ContainsDeferredCallableUtility(h.ElementType),
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
                ParameterPackCheckedType p =>
                    (p.SourceCallable is not null
                        && ContainsUnboundCalleeGeneric(p.SourceCallable, genericParameters))
                    || p.Members.Any(m => ContainsUnboundCalleeGeneric(m, genericParameters)),
                HomogeneousVariadicCheckedType h =>
                    ContainsUnboundCalleeGeneric(h.ElementType, genericParameters),
                StructCheckedType s =>
                    s.Properties.Values.Any(p => ContainsUnboundCalleeGeneric(p.Type, genericParameters)),
                _ => false,
            };
        }

        /// <summary>
        /// Callable-facet and <c>|&gt;</c> leftover generics: a symbol-name brand keeps its shape
        /// (unbound <c>__EnumName&lt;T&gt;</c> uses the unbound-enum placeholder, not a written
        /// <c>object</c>). Any other leftover generic collapses to <c>mixed</c>.
        /// </summary>
        private static ICheckedType GradualizeUnboundCallableParameter(ICheckedType type)
        {
            if (!ContainsSymbolNameBrand(type))
            {
                return CheckedTypes.Mixed;
            }

            var parameters = new List<GenericTypeParameterSymbol>();
            CollectGenericTypeParameters(type, parameters);
            return PreserveShapeForUnboundCalleeGenerics(type, parameters);
        }

        private static void CollectGenericTypeParameters(
            ICheckedType type,
            List<GenericTypeParameterSymbol> parameters)
        {
            switch (type)
            {
                case SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol parameter }:
                    if (!parameters.Contains(parameter))
                    {
                        parameters.Add(parameter);
                    }

                    break;
                case NullableCheckedType nullable:
                    CollectGenericTypeParameters(nullable.InnerType, parameters);
                    break;
                case GenericCheckedType generic:
                    CollectGenericTypeParameters(generic.BaseType, parameters);
                    foreach (var argument in generic.TypeArguments)
                    {
                        CollectGenericTypeParameters(argument, parameters);
                    }

                    break;
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        CollectGenericTypeParameters(member, parameters);
                    }

                    break;
                case IntersectionCheckedType intersection:
                    foreach (var member in intersection.Members)
                    {
                        CollectGenericTypeParameters(member, parameters);
                    }

                    break;
                case CallableCheckedType callable:
                    CollectGenericTypeParameters(callable.ReturnType, parameters);
                    foreach (var parameterType in callable.ParameterTypes)
                    {
                        CollectGenericTypeParameters(parameterType, parameters);
                    }

                    break;
                case ParameterPackCheckedType pack:
                    if (pack.SourceCallable is not null)
                    {
                        CollectGenericTypeParameters(pack.SourceCallable, parameters);
                    }

                    foreach (var member in pack.Members)
                    {
                        CollectGenericTypeParameters(member, parameters);
                    }

                    break;
                case HomogeneousVariadicCheckedType variadic:
                    CollectGenericTypeParameters(variadic.ElementType, parameters);
                    break;
                case StructCheckedType structure:
                    foreach (var property in structure.Properties.Values)
                    {
                        CollectGenericTypeParameters(property.Type, parameters);
                    }

                    break;
            }
        }

        /// <summary>
        /// When a parameter still mentions unbound callee generics, keep wrappers that must stay
        /// structural for argument checking instead of collapsing the whole type to <c>mixed</c>.
        /// PropertyPath / Expression keep contextual inline-fn typing; symbol-name brands
        /// (<c>__ClassName&lt;T&gt;</c>, …) keep the brand with <c>T</c> defaulted to <c>object</c>
        /// so a non-name like <c>1</c> is still rejected.
        /// </summary>
        private static ICheckedType PreserveShapeForUnboundCalleeGenerics(
            ICheckedType type,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters)
        {
            // Keep PropertyPath / Expression wrappers: collapsing those to mixed drops
            // Story 16 inline-fn conversion (`select<R>(Expression<callable(T): R>)` + `fn ($u) => …`).
            // Replace only the unbound method params (e.g. `R` → mixed) so the fn is still
            // contextually typed from the class generic (`T` → User).
            if (PropertyPathSupport.IsPropertyPathOrExpressionType(type))
            {
                return SubstituteUnboundCalleeGenerics(
                    type, genericParameters, CheckedTypes.Mixed);
            }

            // `__ClassName<T>|__InterfaceName<T>` (assertInstanceOf) must not become mixed:
            // the brand is what rejects non-names; default T to object (bare-brand equivalence).
            if (ContainsSymbolNameBrand(type))
            {
                return SubstituteUnboundCalleeGenerics(
                    type,
                    genericParameters,
                    CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object")));
            }

            return CheckedTypes.Mixed;
        }

        private static bool ContainsSymbolNameBrand(ICheckedType type) =>
            type switch
            {
                _ when SymbolNameTypeHelper.IsSymbolNameType(type) => true,
                NullableCheckedType n => ContainsSymbolNameBrand(n.InnerType),
                GenericCheckedType g => g.TypeArguments.Any(ContainsSymbolNameBrand),
                UnionCheckedType u => u.Members.Any(ContainsSymbolNameBrand),
                IntersectionCheckedType i => i.Members.Any(ContainsSymbolNameBrand),
                CallableCheckedType c =>
                    ContainsSymbolNameBrand(c.ReturnType)
                    || c.ParameterTypes.Any(ContainsSymbolNameBrand),
                ParameterPackCheckedType p =>
                    (p.SourceCallable is not null && ContainsSymbolNameBrand(p.SourceCallable))
                    || p.Members.Any(ContainsSymbolNameBrand),
                HomogeneousVariadicCheckedType h => ContainsSymbolNameBrand(h.ElementType),
                StructCheckedType s =>
                    s.Properties.Values.Any(prop => ContainsSymbolNameBrand(prop.Type)),
                _ => false,
            };

        /// <summary>
        /// Replaces unbound callee type parameters with <paramref name="replacement"/> while
        /// keeping the surrounding type shape (so
        /// <c>Expression&lt;(callable(User): R)&gt;</c> becomes
        /// <c>Expression&lt;(callable(User): mixed)&gt;</c>, and
        /// <c>__ClassName&lt;T&gt;</c> becomes <c>__ClassName&lt;object&gt;</c>).
        /// </summary>
        private static ICheckedType SubstituteUnboundCalleeGenerics(
            ICheckedType type,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            ICheckedType replacement)
        {
            if (genericParameters.Count == 0)
            {
                return type;
            }

            return type switch
            {
                SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol gp }
                    when genericParameters.Contains(gp) => replacement,
                NullableCheckedType n => new NullableCheckedType(
                    SubstituteUnboundCalleeGenerics(n.InnerType, genericParameters, replacement)),
                GenericCheckedType g => g.With(
                    g.BaseType,
                    SubstituteGenericTypeArguments(g, genericParameters, replacement)),
                UnionCheckedType u => CheckedTypes.UnionTypes(
                    u.Members
                        .Select(m => SubstituteUnboundCalleeGenerics(m, genericParameters, replacement))
                        .ToList()),
                IntersectionCheckedType i => new IntersectionCheckedType(
                    i.Members
                        .Select(m => SubstituteUnboundCalleeGenerics(m, genericParameters, replacement))
                        .ToList()),
                CallableCheckedType c => c.MapTypes(p =>
                    SubstituteUnboundCalleeGenerics(p, genericParameters, replacement)),
                StructCheckedType s => new StructCheckedType(
                    s.Properties.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.WithType(
                            SubstituteUnboundCalleeGenerics(
                                pair.Value.Type, genericParameters, replacement)))),
                _ => type,
            };
        }

        private static List<ICheckedType> SubstituteGenericTypeArguments(
            GenericCheckedType generic,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            ICheckedType replacement)
        {
            var enumBrand = SymbolNameTypeHelper.TryGetBehavior(generic, out var behavior)
                && behavior == UtilityBehavior.EnumName;
            var args = new List<ICheckedType>(generic.TypeArguments.Count);
            foreach (var argument in generic.TypeArguments)
            {
                if (enumBrand
                    && argument is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol parameter }
                    && genericParameters.Contains(parameter))
                {
                    args.Add(SymbolNameTypeHelper.UnboundEnumBrandArgument);
                    continue;
                }

                args.Add(SubstituteUnboundCalleeGenerics(argument, genericParameters, replacement));
            }

            return args;
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

        /// <summary>
        /// <c>fromCallable</c> visibility (Decision 7–8): if the checker can see that
        /// <c>$callback</c> is not callable from <c>__CurrentScope</c> (another class's
        /// private method), emit TYHP4025. <c>__CurrentScope</c> is an access check, not
        /// result <c>TThis</c>. Array / string callables are the knowable forms; FCC of a
        /// private method is already diagnosed on the member access.
        /// </summary>
        private static void CheckFromCallableVisibility(
            ObjectMethodSymbol method,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!ClosureProducerInference.IsFromCallableMethod(method) || call.Arguments is null)
            {
                return;
            }

            IExpression? argument = null;
            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                if (!arg.IsVariadic && arg.Expression is not null)
                {
                    argument = arg.Expression;
                    break;
                }
            }

            if (argument is null)
            {
                return;
            }

            if (ClosureProducerInference.TryGetArrayCallableParts(
                    argument, out var receiverExpr, out var methodNameExpr))
            {
                var methodNameType = context.ResolveExpressionType(methodNameExpr, state);
                if (!ClosureProducerInference.TryGetStringCallableName(methodNameType, out var methodName))
                {
                    return;
                }

                var receiverType = context.ResolveExpressionType(receiverExpr, state);
                var classFromBrand = ClosureProducerInference.TryClassFromClassNameBrand(receiverType);
                if (classFromBrand is not null)
                {
                    TryCheckFromCallableMethodVisibility(
                        classFromBrand, methodName, staticOnly: true, argument, state, context, diagnostics);
                    return;
                }

                if (ClosureProducerInference.TryGetStringCallableName(receiverType, out var className)
                    && ClosureProducerInference.ResolveClassByName(
                        className, context.SymbolTree, context.GlobalScope) is { } namedClass)
                {
                    TryCheckFromCallableMethodVisibility(
                        CheckedTypes.FromSymbol(namedClass),
                        methodName,
                        staticOnly: true,
                        argument,
                        state,
                        context,
                        diagnostics);
                    return;
                }

                TryCheckFromCallableMethodVisibility(
                    receiverType, methodName, staticOnly: false, argument, state, context, diagnostics);
                return;
            }

            var callbackType = context.ResolveExpressionType(argument, state);
            if (!ClosureProducerInference.TryGetStringCallableName(callbackType, out var stringName))
            {
                return;
            }

            var split = stringName.Split(["::"], 2, StringSplitOptions.None);
            if (split.Length != 2)
            {
                return;
            }

            var owner = ClosureProducerInference.ResolveClassByName(
                split[0], context.SymbolTree, context.GlobalScope);
            if (owner is null)
            {
                return;
            }

            TryCheckFromCallableMethodVisibility(
                CheckedTypes.FromSymbol(owner),
                split[1],
                staticOnly: true,
                argument,
                state,
                context,
                diagnostics);
        }

        private static void TryCheckFromCallableMethodVisibility(
            ICheckedType ownerType,
            string methodName,
            bool staticOnly,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!TryResolveMethod(ownerType, methodName, staticOnly, state, context, out var target)
                || target is null)
            {
                if (staticOnly)
                {
                    return;
                }

                if (!TryResolveMethod(ownerType, methodName, staticOnly: true, state, context, out target)
                    || target is null)
                {
                    return;
                }
            }

            CheckMemberVisibility(target, state, reportNode, diagnostics);
        }

        private static bool TryResolveMethod(
            ICheckedType ownerType,
            string methodName,
            bool staticOnly,
            CheckerState state,
            CheckerRuleContext context,
            out ObjectMethodSymbol? method,
            bool allowInstanceForwarding = false) =>
            CheckerHelpers.TryResolveInstanceOrExtensionMethod(
                ownerType,
                methodName,
                staticOnly,
                state,
                context.SymbolTree,
                context.GlobalScope,
                out method,
                allowInstanceForwarding);

        private static bool TryResolveProperty(
            ICheckedType ownerType,
            string propertyName,
            CheckerRuleContext context,
            out ObjectPropertySymbol? property)
        {
            property = null;
            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(
                CheckerHelpers.UnwrapMemberAccessReceiver(ownerType));
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
        private static ICheckedType UnwrapForMemberAccess(ICheckedType type) =>
            CheckerHelpers.UnwrapMemberAccessReceiver(type);

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
        /// Dispatches the full checker pipeline on each call-argument expression.
        /// <see cref="PhpDereferenceableAst"/> suppresses the generic child walk, and
        /// <see cref="CheckNestedCalleeIfNeeded"/> only re-enters nested calls / <c>new</c>,
        /// so a yield (or any other non-call expression) used as a bare call argument would
        /// otherwise never run <c>ControlFlowRule.CheckYield</c> (TYHP4086 / 4088 / 4089).
        /// Inline functions are left for the contextual <c>CheckNode</c> in
        /// <see cref="CheckArgumentAgainstParameterType"/> / callable-facet matching.
        /// </summary>
        private static void CheckCallArgumentExpressions(
            PhpArgumentListAst? arguments,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (arguments is null)
            {
                return;
            }

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.Expression is null or PhpInlineFunctionAst)
                {
                    continue;
                }

                // Bare `new Generic()` / `new Generic() with` wait until the parameter type is
                // known so ExpectedExpressionType can instantiate omitted type arguments.
                if (ContextualNewInference.IsBareGenericConstruction(arg.Expression))
                {
                    continue;
                }

                context.CheckNode(arg.Expression, state);
            }
        }

        /// <summary>
        /// <see cref="PhpDereferenceableAst"/> suppresses the generic checker child-walk
        /// (<see cref="SuppressChildTraversal"/>), so any nested call / member-access chain /
        /// <c>new</c> expression that only appears as a receiver inside another call
        /// is otherwise never independently visited — silently skipping its own argument
        /// validation, named-argument checks, and member-visibility checks. Manually re-enter the
        /// normal check pipeline for exactly those node kinds (not plain variables/literals, which
        /// are unaffected by this gap and out of scope here). Call arguments are handled by
        /// <see cref="CheckCallArgumentExpressions"/>.
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
