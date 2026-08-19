using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private ICheckedType InferDereferenceable(PhpDereferenceableAst ast, CheckerState state)
        {
            var current = InferDereferenceableBase(ast.Base, state);
            if (ast.Suffix is null)
            {
                return current;
            }

            return ApplySuffix(current, ast.Suffix, ast.Base, state);
        }

        private ICheckedType InferDereferenceableBase(IDereferenceableBase? baseNode, CheckerState state) =>
            baseNode switch
            {
                PhpDereferenceableAst chain => InferDereferenceable(chain, state),
                PhpVariableAst variable => InferVariable(variable, state),
                PhpNewAst newExpr => InferNew(newExpr, state),
                PhpNameAst name => InferNameBaseType(name, state),
                PhpDereferenceableExpressionAst wrapped => InferExpressionType(wrapped.Expression!, state),
                IBase2Ast ast when ast.BoundSymbol is not null => CheckedTypes.FromSymbol(ast.BoundSymbol),
                _ => CheckedTypes.Unresolved,
            };

        private ICheckedType ApplySuffix(
            ICheckedType current,
            IDereferenceableSuffix suffix,
            IDereferenceableBase? baseNode,
            CheckerState state)
        {
            return suffix switch
            {
                PhpCallAst call => InferCall(current, baseNode, call, state),
                PhpInstanceMemberAccessAst member => InferInstanceMember(current, member, baseNode, state),
                PhpStaticMemberAccessAst staticMember => InferStaticMember(current, staticMember, state),
                PhpArrayAccessAst arrayAccess => InferArrayAccess(current, arrayAccess, baseNode, state),
                PhpClassConstantAccessAst classConst => InferClassConstant(current, classConst, state),
                _ => CheckedTypes.Unresolved,
            };
        }

        private ICheckedType InferCall(
            ICheckedType current,
            IDereferenceableBase? baseNode,
            PhpCallAst call,
            CheckerState state)
        {
            RecordCalleeForVariantRouting(baseNode, call, state);

            // PHP 8.1 first-class callable syntax: `strval(...)`, `$obj->method(...)`,
            // `Class::method(...)` — a call whose only "argument" is a bare `...` denotes the
            // callable itself, not an invocation. Type it as the callee's signature.
            if (IsFirstClassCallableSyntax(call))
            {
                return InferFirstClassCallable(current, baseNode, state);
            }

            if (baseNode is PhpNameAst nameAst)
            {
                if (Rules.CheckerHelpers.ResolveFreeFunction(nameAst, state, _symbolTree, _globalScope)
                    is { } function)
                {
                    var selected = FunctionOverloadSelector.Select(
                        function,
                        call,
                        new FunctionOverloadSelector.Context
                        {
                            State = state,
                            SymbolTree = _symbolTree,
                            GlobalScope = _globalScope,
                            InferArgumentType = expr => InferExpressionType(expr, state),
                            ResolveParameterType = (fn, typeAst) =>
                                ResolveFunctionDeclaredType(typeAst, fn, state, baseNode),
                            InferBindings = (fn, c) =>
                            {
                                if (fn.GenericParameters.Count == 0)
                                {
                                    return null;
                                }

                                return TryInferGenericBindings(
                                    fn.GenericParameters, fn.Parameters, c, state, out var inferred)
                                    && inferred.Count > 0
                                    ? inferred
                                    : null;
                            },
                        });
                    return ResolveFunctionReturnType(selected, state, baseNode, call);
                }

                var fnName = SymbolNameTypeHelper.GetSimpleFunctionName(nameAst.ValueString);
                if (SymbolNameTypeHelper.IsBoolReturningGuard(fnName))
                {
                    return CheckedTypes.Bool;
                }
            }

            // Resolve named method callees before treating <c>current</c> as an opaque callable.
            // Instance member access types a method as <see cref="CallableCheckedType"/>; taking that
            // return type early would skip call-site generic substitution (FOUND_BUGS item 39).
            if (baseNode is PhpDereferenceableAst instanceChain &&
                instanceChain.Suffix is PhpInstanceMemberAccessAst memberAccess)
            {
                var methodName = GetExpressionText(memberAccess.MemberName);
                if (methodName is not null)
                {
                    var receiverType = InferDereferenceableBase(instanceChain.Base, state);
                    if (TryResolveMethodOnType(receiverType, methodName, staticOnly: false, state, out var instanceMethod))
                    {
                        return ResolveMethodReturnType(instanceMethod!, receiverType, state, baseNode, call);
                    }
                }

                // The member isn't a resolvable method — e.g. a property holding a `\Closure<...>`
                // invoked directly (`$this->formatter(5)`), where `current` already carries the
                // callable's signature from `InferInstanceMember`. Prefer that over the generic
                // fallback below so the call still types as the closure's return type.
                if (TryGetCallableReturnType(current, call, state, out var propertyReturn))
                {
                    return propertyReturn;
                }

                // An instance method call whose target can't be resolved — e.g. a trait method
                // supplied by a requirement (`extends`/`implements`) whose declaration isn't in this
                // compilation, or a `__call` magic target — is gradually typed as `unknown` rather
                // than `mixed`. `unknown` is universally assignable, so it does not cascade a spurious
                // return-/assignment-type error onto code that legitimately relies on a member not
                // visible here. (Unresolved member *access* already yields `unknown`.)
                return CheckedTypes.Unresolved;
            }

            if (baseNode is PhpDereferenceableAst staticChain &&
                staticChain.Suffix is PhpStaticMemberAccessAst staticAccess)
            {
                var methodName = GetExpressionText(staticAccess.Member);
                if (methodName is not null)
                {
                    var receiverType = InferDereferenceableBase(staticChain.Base, state);
                    if (TryResolveMethodOnType(receiverType, methodName, staticOnly: true, state, out var staticMethod))
                    {
                        return ResolveMethodReturnType(staticMethod!, receiverType, state, baseNode, call);
                    }
                }
            }

            // Static method calls (`Class::method(...)`) are parsed with a class-constant-access
            // suffix on the receiver dereferenceable, because `::member` is ambiguous between a
            // class constant and a static method at parse time. When such an access is invoked,
            // resolve it against the receiver type as a static method.
            if (baseNode is PhpDereferenceableAst classConstChain &&
                classConstChain.Suffix is PhpClassConstantAccessAst classConstAccess)
            {
                var methodName = GetExpressionText(classConstAccess.Member);
                if (methodName is not null)
                {
                    // PHP 8.4 property-hook call: `parent::$prop::get()` / `::set($v)` (also
                    // `self`/`static`/ClassName). Parsed as Call(ClassConstant(StaticMember($prop), get)).
                    if (TryInferPropertyHookAccessorCall(
                            classConstChain.Base, methodName, state, out var hookCallType))
                    {
                        return hookCallType;
                    }

                    var receiverType = InferDereferenceableBase(classConstChain.Base, state);
                    if (TryResolveMethodOnType(receiverType, methodName, staticOnly: true, state, out var classConstMethod))
                    {
                        return ResolveMethodReturnType(classConstMethod!, receiverType, state, baseNode, call);
                    }
                }
            }

            if (TryGetCallableReturnType(current, call, state, out var callableReturn))
            {
                return callableReturn;
            }

            return CheckedTypes.Mixed;
        }

        /// <summary>
        /// Types <c>Owner::$prop::get()</c> as the property type and <c>::set($v)</c> as void.
        /// </summary>
        private bool TryInferPropertyHookAccessorCall(
            IDereferenceableBase? propertyReceiver,
            string hookName,
            CheckerState state,
            out ICheckedType result)
        {
            result = CheckedTypes.Unresolved;
            if (!string.Equals(hookName, "get", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(hookName, "set", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Parsed as Call(ClassConstant(StaticMember(Owner, $prop), get|set)).
            if (propertyReceiver is not PhpDereferenceableAst
                {
                    Base: { } ownerBase,
                    Suffix: PhpStaticMemberAccessAst { Member: PhpVariableAst propVar }
                })
            {
                return false;
            }

            var propName = CheckerHelpers.GetVariableName(propVar);
            if (propName is null)
            {
                return false;
            }

            var ownerType = InferDereferenceableBase(ownerBase, state);
            if (!TryResolvePropertyOnType(ownerType, propName, state, out var property)
                || property?.DeclaredType is null)
            {
                return false;
            }

            if (string.Equals(hookName, "set", StringComparison.OrdinalIgnoreCase))
            {
                result = CheckedTypes.Void;
                return true;
            }

            result = ResolveDeclaredTypeOnReceiver(
                property.DeclaredType,
                ownerType,
                state,
                declaringClass: FindDeclaringClass(ownerType, property));
            return true;
        }

        /// <summary>
        /// Return type of invoking a callable. Synthesized arity facets share one return type, but a
        /// hand-written intersection need not, so prefer the facet matching the supplied argument
        /// count before falling back to the first facet. When the facet still carries unbound
        /// <see cref="GenericTypeParameterSymbol"/>s (e.g. <c>$fn = keep_keys(...); $fn($xs)</c>),
        /// bind them from the call arguments the same way direct named calls do. Invoking a
        /// type parameter constrained to <c>callable</c> yields deferred
        /// <c>__CallableReturnType&lt;T&gt;</c> until instantiation.
        /// </summary>
        private bool TryGetCallableReturnType(
            ICheckedType type,
            PhpCallAst? call,
            CheckerState state,
            out ICheckedType returnType)
        {
            var facets = CallableArityFacetBuilder.GetCallableFacets(type);
            if (facets.Count == 0)
            {
                // Invoking `TCallable extends callable` is the return of that callable, which
                // is still open inside the generic wrapper — keep `__CallableReturnType<T>` so
                // `return $cb()` matches a declared `__CallableReturnType<TCallable>`.
                if (CallableSignatureReflection.TryUnwrapCallableTypeParameter(type, out var parameterType))
                {
                    returnType = UtilityTypeResolver.MakeDeferredCallableReturnType(
                        parameterType, _globalScope);
                    return true;
                }

                returnType = CheckedTypes.Unresolved;
                return false;
            }

            CallableCheckedType facet;
            if (facets.Count > 1
                && CallableArityFacetBuilder.TrySelectCallableFacet(
                    type,
                    CallableArityFacetBuilder.CountPositionalArguments(call?.Arguments),
                    out var matched)
                && matched is not null)
            {
                facet = matched;
            }
            else
            {
                facet = facets[0];
            }

            returnType = ResolveCallableFacetReturnType(facet, call, state);
            return true;
        }

        /// <summary>
        /// Applies argument-driven generic inference to a callable facet's return type when the
        /// facet still mentions unbound type parameters.
        /// </summary>
        private ICheckedType ResolveCallableFacetReturnType(
            CallableCheckedType facet,
            PhpCallAst? call,
            CheckerState state)
        {
            if (!CallableGenericInference.FacetNeedsArgumentInference(facet)
                || call?.Arguments is null
                || !TryCollectPositionalArgumentTypes(call, state, out var argTypes)
                || !CallableGenericInference.TryInferFacetBindings(facet, argTypes, out var bindings)
                || bindings.Count == 0)
            {
                return facet.ReturnType;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(
                facet.ReturnType, bindings, _symbolTree, _globalScope);
        }

        private bool TryCollectPositionalArgumentTypes(
            PhpCallAst call,
            CheckerState state,
            out List<ICheckedType> argumentTypes)
        {
            argumentTypes = [];
            if (call.Arguments is null)
            {
                return false;
            }

            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                // Match ValidateCallableFacetArguments: only positional (non-named, non-spread).
                if (arg.IsVariadic || arg.Expression is null || arg.Name is not null)
                {
                    continue;
                }

                argumentTypes.Add(InferExpressionType(arg.Expression, state));
            }

            return argumentTypes.Count > 0;
        }

        /// <summary>
        /// Records every call written with explicit generic type arguments anywhere under
        /// <paramref name="root"/>, so the emitter can route each to its callee's Mechanism D binder.
        ///
        /// A subtree walk is needed because the checker's own traversal does not reach these calls in
        /// every position: <c>TypeCompatibilityRule</c> suppresses child traversal for a
        /// dereferenceable, and nothing re-resolves the argument expressions underneath, so a generic
        /// call nested in an argument list is otherwise never seen. Dropping the type arguments there
        /// would silently emit a call to the non-generic wrapper.
        ///
        /// Inline function bodies are entered too, because a closure passed as an argument is never
        /// reached by the checker at all. Their receivers are resolved against the enclosing state,
        /// which is correct for captured variables but not for the closure's own parameters, so a call
        /// whose receiver mentions a parameter name introduced by an enclosing closure is left
        /// unrecorded rather than risk binding it to a same-named method on an unrelated type.
        /// </summary>
        internal void RecordGenericCallTargetsIn(IBase2Ast root, CheckerState state) =>
            RecordGenericCallTargetsIn(root, state, shadowedVariables: null);

        private void RecordGenericCallTargetsIn(
            IBase2Ast node,
            CheckerState state,
            HashSet<string>? shadowedVariables)
        {
            if (node is ErrorAst)
            {
                return;
            }

            if (node is PhpInlineFunctionAst closure)
            {
                shadowedVariables = WithParameterNames(shadowedVariables, closure);
            }
            else if (node is PhpDereferenceableAst { Suffix: PhpCallAst call } deref
                && !ReferencesAny(deref.Base, shadowedVariables))
            {
                RecordCalleeForVariantRouting(deref.Base, call, state);
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    RecordGenericCallTargetsIn(child, state, shadowedVariables);
                }
            }
        }

        private static HashSet<string> WithParameterNames(
            HashSet<string>? shadowedVariables,
            PhpInlineFunctionAst closure)
        {
            var names = shadowedVariables is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(shadowedVariables, StringComparer.Ordinal);

            foreach (var parameter in closure.Parameters?.GetAllNotNull() ?? [])
            {
                if (parameter.Name is { } name)
                {
                    names.Add(name.TrimStart('$'));
                }
            }

            return names;
        }

        private static bool ReferencesAny(IBase2Ast? node, HashSet<string>? names)
        {
            if (node is null || names is null || names.Count == 0)
            {
                return false;
            }

            if (node is PhpVariableAst variable
                && Rules.CheckerHelpers.GetVariableName(variable) is { } variableName
                && names.Contains(variableName))
            {
                return true;
            }

            foreach (var child in node.AstChildren)
            {
                if (ReferencesAny(child, names))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Records the callee of a call that wrote explicit generic type arguments, so the emitter can
        /// route it to the Mechanism D binder (FOUND_BUGS Mechanism D lineage).
        ///
        /// Done before <see cref="InferCall"/> resolves anything, because the memoized
        /// <see cref="CallableCheckedType"/> path returns the return type without re-resolving the
        /// callee, which would leave every instance call unrecorded. Calls with no type arguments are
        /// skipped: they need no routing, and re-resolving a member here would duplicate any
        /// unresolved-member diagnostic.
        /// </summary>
        internal void RecordCalleeForVariantRouting(
            IDereferenceableBase? baseNode,
            PhpCallAst call,
            CheckerState state)
        {
            if (!HasCallSiteTypeArguments(baseNode))
            {
                return;
            }

            if (baseNode is PhpNameAst nameAst)
            {
                _checker.RecordGenericCallTarget(
                    call,
                    Rules.CheckerHelpers.ResolveFreeFunction(nameAst, state, _symbolTree, _globalScope));
                return;
            }

            if (baseNode is not PhpDereferenceableAst chain)
            {
                return;
            }

            var (memberName, staticOnly) = chain.Suffix switch
            {
                PhpInstanceMemberAccessAst instance => (GetExpressionText(instance.MemberName), false),
                PhpStaticMemberAccessAst staticAccess => (GetExpressionText(staticAccess.Member), true),
                PhpClassConstantAccessAst classConst => (GetExpressionText(classConst.Member), true),
                _ => (null, false),
            };

            if (memberName is null)
            {
                return;
            }

            var receiverType = InferDereferenceableBase(chain.Base, state);
            if (TryResolveMethodOnType(receiverType, memberName, staticOnly, state, out var method))
            {
                _checker.RecordGenericCallTarget(call, method);
            }
        }

        /// <summary>
        /// True when the callee name carries a generic type-argument list. They hang off the name, not
        /// the argument list: a free function under the <c>identifier</c> addon, a <c>::</c>/<c>-&gt;</c>
        /// member under <c>memberName</c>.
        /// </summary>
        private static bool HasCallSiteTypeArguments(IDereferenceableBase? baseNode) =>
            TryGetCallSiteTypeArgumentList(baseNode) is not null;

        private ICheckedType InferInstanceMember(
            ICheckedType current,
            PhpInstanceMemberAccessAst member,
            IDereferenceableBase? baseNode,
            CheckerState state)
        {
            // A dynamic member name (`$obj->{$expr}` / `$obj->$var`) computes the property at
            // runtime, so the specific member is unknown at compile time; the access yields `mixed`
            // (PHP routes it through `__get`/`__set`). Resolving the dynamic expression's variable
            // name as if it were a literal property name would mis-resolve via the magic-method
            // fallback, so short-circuit here.
            if (member.MemberName is not (PhpNameAst or TokenValueAst))
            {
                return CheckedTypes.Mixed;
            }

            var memberName = GetExpressionText(member.MemberName);
            if (memberName is null)
            {
                return CheckedTypes.Unresolved;
            }

            // `\Closure<...>`/`callable<...>` (including optional-arity intersections) carry their
            // signature as type arguments / facets. Resolving `->__invoke` to the class's declared
            // (mixed) method would lose the signature, so surface the callable facet type here.
            if (string.Equals(memberName, "__invoke", StringComparison.Ordinal)
                && TryNormalizeCallableFacets(current, out var invokeType))
            {
                return invokeType;
            }

            // Prefer control-flow narrowed type for `$this->prop` (null-check / instanceof / guards).
            if (baseNode is PhpVariableAst receiver
                && Rules.CheckerHelpers.IsThisVariable(receiver))
            {
                var propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
                if (state.LookupPropertyInit(propertyKey) is { NarrowedType: { } narrowed })
                {
                    return narrowed;
                }
            }

            // Control-flow narrowing for `$var->prop` (structural key, same idea as index-access).
            if (TypeNarrowingRule.TryGetMemberAccessKey(baseNode, member, out var memberKey)
                && state.LookupMemberAccess(memberKey!) is { } memberNarrowed)
            {
                return memberNarrowed;
            }

            if (TryResolvePropertyOnType(current, memberName, state, out var property) &&
                property?.DeclaredType is not null)
            {
                return ResolveDeclaredTypeOnReceiver(
                    property.DeclaredType,
                    current,
                    state,
                    declaringClass: FindDeclaringClass(current, property));
            }

            if (TryResolveMethodOnType(current, memberName, staticOnly: false, state, out _))
            {
                return InferCallableFromMethod(current, memberName, staticOnly: false, state);
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Normalizes annotated <c>callable</c>/<c>\Closure</c> (and optional-arity intersections)
        /// into <see cref="CallableCheckedType"/> facet form for <c>->__invoke</c> typing.
        /// </summary>
        private static bool TryNormalizeCallableFacets(ICheckedType type, out ICheckedType normalized)
        {
            var facets = CallableArityFacetBuilder.GetCallableFacets(type);
            if (facets.Count == 0)
            {
                normalized = CheckedTypes.Unresolved;
                return false;
            }

            if (facets.Count == 1)
            {
                normalized = facets[0];
                return true;
            }

            normalized = new IntersectionCheckedType(facets.Cast<ICheckedType>().ToList());
            return true;
        }

        private ICheckedType InferStaticMember(
            ICheckedType current,
            PhpStaticMemberAccessAst staticMember,
            CheckerState state)
        {
            var memberName = GetExpressionText(staticMember.Member);
            if (memberName is null)
            {
                return CheckedTypes.Unresolved;
            }

            if (TryResolveMethodOnType(current, memberName, staticOnly: true, state, out var method))
            {
                return ResolveMethodReturnType(method!, current, state);
            }

            if (TryResolvePropertyOnType(current, memberName, state, out var property) &&
                property?.DeclaredType is not null)
            {
                return ResolveDeclaredTypeOnReceiver(
                    property.DeclaredType,
                    current,
                    state,
                    declaringClass: FindDeclaringClass(current, property));
            }

            return CheckedTypes.Unresolved;
        }

        private ICheckedType InferArrayAccess(
            ICheckedType current,
            PhpArrayAccessAst arrayAccess,
            IDereferenceableBase? baseNode,
            CheckerState state)
        {
            // Control-flow narrowing from guards like `\is_string($callable[1])` — keyed by
            // structural `$var[literal]` so the use-site node (distinct AST from the guard
            // subject) still resolves to the narrowed type.
            if (TypeNarrowingRule.TryGetIndexAccessKey(baseNode, arrayAccess, out var indexKey)
                && state.LookupIndexAccess(indexKey!) is { } narrowed)
            {
                return narrowed;
            }

            // The base type's display name is namespace-qualified (e.g. "\array"), so compare the
            // trailing segment rather than an exact match. A generic array element type is the
            // last type argument: array<V> yields V, array<K, V> yields V.
            if (current is GenericCheckedType generic
                && generic.TypeArguments.Count >= 1
                && IsArrayBaseType(generic.BaseType))
            {
                return generic.TypeArguments[^1];
            }

            // `$str[$i]` — PHP string offset access always yields a (possibly empty) `string`, not
            // `mixed`. Without this, every string-index read defaulted to `mixed` and (post Top-type
            // #1) needed narrowing before any further use — a broad false positive for ordinary
            // string manipulation, not just this one call site.
            if (IsStringLikeType(current))
            {
                return CheckedTypes.String;
            }

            // Positional callable bags erase to int-keyed arrays; a constant int index maps to
            // `0 as $_1` / `1 as $_2` / … so `$args[0]` is the first parameter type.
            var unwrapped = current;
            while (unwrapped is NullableCheckedType nullable)
            {
                unwrapped = nullable.InnerType;
            }

            if (unwrapped is StructCheckedType structType
                && structType.HasIntegerKeyAliases
                && arrayAccess.IndexExpression is PhpScalarAst index
                && (index.ScalarType is PhpScalarType.Integer
                    or PhpScalarType.OctalNumber
                    or PhpScalarType.HexNumber
                    or PhpScalarType.BinaryNumber)
                && index.ValueInt64 is long indexValue
                && indexValue is >= int.MinValue and <= int.MaxValue
                && structType.TryGetPropertyByIntegerKey((int)indexValue, out var indexed)
                && indexed is not null)
            {
                return indexed.Type;
            }

            return CheckedTypes.Mixed;
        }

        /// <summary>
        /// True when every possible runtime value of <paramref name="type"/> is a PHP <c>string</c> —
        /// covers the plain built-in, string literals (e.g. after <c>?:</c>/ternary narrowing), and
        /// unions composed entirely of those (union subsumption does not always collapse a literal
        /// into its widened built-in — see FOUND_BUGS.md).
        /// </summary>
        private static bool IsStringLikeType(ICheckedType type) =>
            type switch
            {
                LiteralCheckedType literal => literal.Value is string,
                UnionCheckedType union => union.Members.Count > 0 && union.Members.All(IsStringLikeType),
                _ => CheckerHelpers.IsBuiltInName(type, "string"),
            };

        private static bool IsArrayBaseType(ICheckedType baseType)
        {
            var name = baseType.DisplayName;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lastSegment = name.TrimStart('?').TrimStart('\\');
            return string.Equals(lastSegment, "array", StringComparison.OrdinalIgnoreCase);
        }

        private ICheckedType InferClassConstant(
            ICheckedType current,
            PhpClassConstantAccessAst classConst,
            CheckerState state)
        {
            // `Expr::class` is the magic pseudo-constant (the reserved word `class` used as a member
            // name, parsed from `T_CLASS` via `VisitIdentifier` into a plain `PhpNameAst` — not a
            // `PhpMagicConstantAst`/`T_CLASS_C` like `__CLASS__`). Runtime value is always a string;
            // the checker brands it as `__ClassName<R>` (or interface/enum/trait sibling) from the
            // receiver's static type so `User::class` / `self<T>::class` / `$obj::class` feed
            // parametric `__ClassName<…>` parameters (FOUND_BUGS §4–§5).
            if (string.Equals(GetExpressionText(classConst.Member), "class", StringComparison.OrdinalIgnoreCase))
            {
                return InferClassMagicConstant(current);
            }

            if (classConst.Member?.BoundSymbol is ConstantSymbol constant &&
                constant.DeclaredType is not null)
            {
                return ResolveTypeExpression(constant.DeclaredType, state);
            }

            // Resolve the member (`Color::Red`) against the receiver type. The member's own
            // BoundSymbol isn't always populated for `::` constant access, so resolve it explicitly.
            var memberName = classConst.Member is null ? null : GetExpressionText(classConst.Member);
            if (memberName is not null
                && UnwrapForMemberAccess(current) is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol owner } ownerType
                && _symbolTree.ResolveConstant(memberName, owner, _diagnostics) is ObjectConstantSymbol objectConstant)
            {
                // An enum case (`Color::Red`) is a singleton instance of the enum, so its type is the
                // enum itself rather than the backing scalar type.
                if (objectConstant.IsEnumCase)
                {
                    return ownerType;
                }

                if (objectConstant.DeclaredType is not null)
                {
                    return ResolveTypeExpression(objectConstant.DeclaredType, state);
                }
            }

            _ = current;
            return CheckedTypes.Mixed;
        }

        /// <summary>
        /// Brands <c>::class</c> as a symbol-name type of the receiver. Keeps generic arguments
        /// (<c>static&lt;T&gt;::class</c> → <c>__ClassName&lt;Promise&lt;T&gt;&gt;</c>); only peels
        /// nullability. Non-object receivers fall back to <c>__ClassName&lt;object&gt;</c>.
        /// </summary>
        private ICheckedType InferClassMagicConstant(ICheckedType receiverType)
        {
            var receiver = receiverType;
            while (receiver is NullableCheckedType nullable)
            {
                receiver = nullable.InnerType;
            }

            if (TypeComparer.IsUnresolvedType(receiver)
                || CheckerHelpers.IsBuiltInName(receiver, "object"))
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.ClassName,
                    _globalScope,
                    [CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object"))]);
            }

            if (CheckerHelpers.TryGetObjectDeclaration(receiver) is { } obj)
            {
                var behavior = obj.ObjectKind switch
                {
                    PhpTypeDeclType.Interface => UtilityBehavior.InterfaceName,
                    PhpTypeDeclType.Enum => UtilityBehavior.EnumName,
                    PhpTypeDeclType.Trait => UtilityBehavior.TraitName,
                    _ => UtilityBehavior.ClassName,
                };
                return SymbolNameTypeHelper.MakeSymbolNameType(behavior, _globalScope, [receiver]);
            }

            // Generic type parameters (`T::class` / `$t::class`) and other object-ish checked forms.
            if (receiver is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
                || receiver is GenericCheckedType)
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.ClassName, _globalScope, [receiver]);
            }

            // Scalars / callables / etc. — PHP only allows ::class on objects at runtime.
            return SymbolNameTypeHelper.MakeSymbolNameType(
                UtilityBehavior.ClassName,
                _globalScope,
                [CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object"))]);
        }

        private ICheckedType ResolveFunctionReturnType(
            FunctionDeclarationSymbol function,
            CheckerState state,
            IDereferenceableBase? callBase = null,
            PhpCallAst? call = null)
        {
            ICheckedType resolved;
            switch (function.ReturnType)
            {
                case null:
                    resolved = CheckedTypes.Mixed;
                    break;
                case TyhpReturnTypeGuardAst:
                    resolved = CheckedTypes.Bool;
                    break;
                default:
                {
                    // Bind the declared return type in the callee's generic scope (FOUND_BUGS item 39
                    // / Story 11 audit item 5), then substitute explicit call-site type arguments.
                    var resolveState = state;
                    if (function.GenericParameters.Count > 0)
                    {
                        // Fork (mutable): ResolveTypeExpression may SnapShot for cross-file
                        // annotations; SnapShot() here would lock and throw on the nested call.
                        resolveState = state.Fork();
                        resolveState.FunctionGenerics = function.GenericParameters;
                    }

                    resolved = ResolveTypeExpression(
                        function.ReturnType, resolveState, isReturnTypePosition: true);
                    break;
                }
            }

            resolved = ApplyCallSiteGenericSubstitution(
                resolved, function.GenericParameters, callBase, state);

            // Minimal argument-driven inference when the call omitted explicit type arguments
            // (Story 11 audit #4). Covers `max(1, $n): T`, `array_reverse($xs): array<…>`,
            // `str_replace(..., $s): TSubject` so return types are not left as unbound params.
            if (function.GenericParameters.Count > 0
                && call is not null
                && !HasCallSiteTypeArguments(callBase)
                && TryInferGenericBindings(
                    function.GenericParameters, function.Parameters, call, state, out var inferred)
                && inferred.Count > 0)
            {
                resolved = TypeComparer.ResolveGenericTypeBySymbol(
                    resolved, inferred, _symbolTree, _globalScope);
            }

            return WrapIfAsyncCall(resolved, function.IsAsync);
        }

        /// <summary>
        /// Infers type-argument bindings by structurally matching each parameter's declared
        /// annotation (resolved in the callee generic scope) against the argument type.
        /// Shared by free functions and methods (Story 11 §4 / CHECKER_GAPS P1 #14).
        /// </summary>
        /// <param name="receiverType">
        /// Method receiver when inferring a generic method. Parameter annotations that mention
        /// class type parameters (<c>select&lt;R&gt;(Expression&lt;T, R&gt;)</c>) must resolve in
        /// the declaring class's generic scope — the call-site <paramref name="state"/> has no
        /// <c>ObjectGenerics</c>, and without this, chaining off the method's return type
        /// (<c>-&gt;select(...)-&gt;sortBy(...)</c>) reports TYHP3003 on <c>T</c>.
        /// </param>
        /// <param name="method">Declaring method, used with <paramref name="receiverType"/>.</param>
        internal bool TryInferGenericBindings(
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IReadOnlyList<ParameterInfo> parameters,
            PhpCallAst call,
            CheckerState state,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            ICheckedType? receiverType = null,
            ObjectMethodSymbol? method = null)
        {
            bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            if (call.Arguments is null || parameters.Count == 0 || genericParameters.Count == 0)
            {
                return false;
            }

            var resolveState = state.Fork();
            resolveState.FunctionGenerics = genericParameters;

            var positionalIndex = 0;
            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                if (arg.IsVariadic || arg.Expression is null)
                {
                    continue;
                }

                ParameterInfo? param = null;
                if (arg.Name?.ValueString is { } named)
                {
                    param = parameters.FirstOrDefault(p =>
                        string.Equals(
                            p.Name.TrimStart('$'),
                            named.TrimStart('$'),
                            StringComparison.OrdinalIgnoreCase));
                }
                else if (positionalIndex < parameters.Count)
                {
                    param = parameters[positionalIndex++];
                }

                if (param?.DeclaredType is null)
                {
                    continue;
                }

                // Method parameters that mention class generics must use the declaring class
                // scope (and receiver substitution) — FunctionGenerics alone is not enough.
                var declared = receiverType is not null
                    ? ResolveDeclaredTypeOnReceiver(
                        param.DeclaredType,
                        receiverType,
                        state,
                        declaringClass: method is not null
                            ? FindDeclaringClass(receiverType, method)
                                ?? TryGetOwningObjectDeclaration(method)
                            : null,
                        functionGenerics: genericParameters)
                    : ResolveTypeExpression(param.DeclaredType, resolveState);
                var actual = InferExpressionType(arg.Expression, state);
                CallableGenericInference.CollectGenericBindings(declared, actual, bindings);
            }

            return bindings.Count > 0;
        }

        private bool TryInferFunctionGenericBindings(
            FunctionDeclarationSymbol function,
            PhpCallAst call,
            CheckerState state,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings) =>
            TryInferGenericBindings(
                function.GenericParameters, function.Parameters, call, state, out bindings);

        private ICheckedType ResolveMethodReturnType(
            ObjectMethodSymbol method,
            ICheckedType receiverType,
            CheckerState state,
            IDereferenceableBase? callBase = null,
            PhpCallAst? call = null)
        {
            switch (method.ReturnType)
            {
                case null:
                    return CheckedTypes.Mixed;
                case TyhpReturnTypeGuardAst:
                    return CheckedTypes.Bool;
            }

            // Resolve the declared return type in the callee's generic scope so a method parameter
            // (e.g. Fiber::suspend's own TResume) shadows a same-named class parameter, then apply
            // explicit call-site type arguments (FOUND_BUGS item 39).
            ICheckedType resolved;

            // Relative names (`self`/`static`/`parent`) must resolve against the correct class:
            // bare `static` → late-bound receiver; `self`/`parent` → declaring class.
            // Parameterized `self<…>` / `parent<…>` use the same declaring-class base.
            if (ReturnTypeReferencesRelativeName(method.ReturnType)
                && TryGetObjectDeclarationFromReceiver(receiverType, out var receiverObj))
            {
                var declaringClass = FindDeclaringClass(receiverType, method) ?? receiverObj;
                var relativeKind = GetRelativeReturnKind(method.ReturnType);
                var scopeOwner = relativeKind == RelativeReturnKind.Static
                    ? receiverObj
                    : declaringClass;

                var receiverState = state.Fork();
                receiverState.EnclosingObject = scopeOwner;
                receiverState.EnclosingObjectType = CheckedTypes.FromSymbol(scopeOwner);
                // Clear call-site function scope so `self`/`parent` resolve via EnclosingObject
                // rather than binder ResolveType walking a foreign NameResolutionScope.
                receiverState.EnclosingFunction = null;
                if (scopeOwner.ContainingScope is { } declaringScope)
                {
                    receiverState.NameResolutionScope = declaringScope;
                }

                if (scopeOwner.GenericParameters.Count > 0)
                {
                    receiverState.ObjectGenerics = scopeOwner.GenericParameters;
                }

                if (method.GenericParameters.Count > 0)
                {
                    receiverState.FunctionGenerics = method.GenericParameters;
                }

                resolved = ApplyReceiverGenericSubstitution(
                    receiverType,
                    ResolveTypeExpression(method.ReturnType, receiverState, isReturnTypePosition: true),
                    state);

                // Bare `static` expands to the call-site receiver (instance) or call-site class
                // reference (static), including that reference's type arguments.
                if (relativeKind == RelativeReturnKind.Static)
                {
                    resolved = ExpandLateStaticType(resolved, receiverType);
                }
            }
            else
            {
                resolved = ResolveDeclaredTypeOnReceiver(
                    method.ReturnType,
                    receiverType,
                    state,
                    isReturnTypePosition: true,
                    declaringClass: FindDeclaringClass(receiverType, method),
                    functionGenerics: method.GenericParameters);
                resolved = ExpandLateStaticType(resolved, receiverType);
            }

            resolved = ApplyCallSiteGenericSubstitution(
                resolved, method.GenericParameters, callBase, state);

            // Argument-driven inference when the call omitted explicit type arguments (parity with
            // free functions — CHECKER_GAPS P1 #14 / Story 11 §4 residual).
            if (method.GenericParameters.Count > 0
                && call is not null
                && !HasCallSiteTypeArguments(callBase)
                && TryInferGenericBindings(
                    method.GenericParameters, method.Parameters, call, state, out var inferred,
                    receiverType, method)
                && inferred.Count > 0)
            {
                resolved = TypeComparer.ResolveGenericTypeBySymbol(
                    resolved, inferred, _symbolTree, _globalScope);
            }

            return WrapIfAsyncCall(resolved, method.IsAsync);
        }

        private enum RelativeReturnKind
        {
            None,
            Self,
            Static,
            Parent,
            Mixed,
        }

        private static RelativeReturnKind GetRelativeReturnKind(Ast.Interfaces.ITypeExpression typeAst) =>
            typeAst switch
            {
                PhpNamedTypeAst named when IsRelativeTypeName(GetExpressionText(named.Name)) =>
                    ClassifyRelativeName(GetExpressionText(named.Name)),
                PhpBuiltinTypeAst builtin when IsRelativeTypeName(builtin.Identifier) =>
                    ClassifyRelativeName(builtin.Identifier),
                PhpTypeExpressionAst composite =>
                    FoldRelativeKinds(
                        composite.Types?.GetAllNotNull().Select(GetRelativeReturnKind) ?? []),
                _ => RelativeReturnKind.None,
            };

        private static RelativeReturnKind ClassifyRelativeName(string? name)
        {
            if (string.Equals(name, "static", StringComparison.OrdinalIgnoreCase))
            {
                return RelativeReturnKind.Static;
            }

            if (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase))
            {
                return RelativeReturnKind.Self;
            }

            if (string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase))
            {
                return RelativeReturnKind.Parent;
            }

            return RelativeReturnKind.None;
        }

        private static RelativeReturnKind FoldRelativeKinds(IEnumerable<RelativeReturnKind> kinds)
        {
            RelativeReturnKind acc = RelativeReturnKind.None;
            foreach (var kind in kinds)
            {
                if (kind == RelativeReturnKind.None)
                {
                    continue;
                }

                if (acc == RelativeReturnKind.None)
                {
                    acc = kind;
                }
                else if (acc != kind)
                {
                    return RelativeReturnKind.Mixed;
                }
            }

            return acc;
        }

        /// <summary>
        /// Replaces late-bound <c>static</c> (including nested in generics/unions) with the
        /// call-site receiver / class reference type.
        /// </summary>
        private static ICheckedType ExpandLateStaticType(ICheckedType type, ICheckedType replacement)
        {
            return type switch
            {
                StaticCheckedType => replacement,
                NullableCheckedType nullable =>
                    new NullableCheckedType(ExpandLateStaticType(nullable.InnerType, replacement)),
                UnionCheckedType union =>
                    CheckedTypes.UnionTypes(
                        union.Members.Select(m => ExpandLateStaticType(m, replacement)).ToList()),
                IntersectionCheckedType intersection =>
                    new IntersectionCheckedType(
                        intersection.Members.Select(m => ExpandLateStaticType(m, replacement)).ToList()),
                GenericCheckedType generic =>
                    new GenericCheckedType(
                        ExpandLateStaticType(generic.BaseType, replacement),
                        generic.TypeArguments
                            .Select(arg => ExpandLateStaticType(arg, replacement))
                            .ToList()),
                CallableCheckedType callable =>
                    callable.MapTypes(p => ExpandLateStaticType(p, replacement)),
                StructCheckedType structType => new StructCheckedType(
                    structType.Properties.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.WithType(
                            ExpandLateStaticType(pair.Value.Type, replacement)))),
                _ => type,
            };
        }

        /// <summary>
        /// Rewrites a callee return type through the explicit type arguments written at the call site
        /// (<c>Fiber::suspend&lt;T&gt;(…)</c> → substitute the method's <c>TResume</c> with <c>T</c>).
        /// Arguments are resolved in the caller's scope; bindings are keyed by parameter symbol so a
        /// shadowed class parameter of the same name is left alone.
        /// </summary>
        /// <remarks>
        /// Declared defaults (<c>T = void</c> / <c>T = object</c>) are <em>not</em> applied here.
        /// Parameter checking relies on unbound callee generics staying unbound so
        /// <c>ContainsUnboundFunctionGeneric</c> can treat them as gradual (<c>mixed</c>) until
        /// argument-driven inference fills them. Eager defaults would turn
        /// <c>call_user_func&lt;TReturn = void&gt;(callable&lt;TReturn&gt;)</c> into
        /// <c>callable(): void</c> and reject real callbacks. Type-guard narrowing applies
        /// defaults separately in <c>ResolveUserDefinedGuardTargetType</c>.
        /// </remarks>
        private ICheckedType ApplyCallSiteGenericSubstitution(
            ICheckedType returnType,
            IReadOnlyList<GenericTypeParameterSymbol> calleeGenerics,
            IDereferenceableBase? callBase,
            CheckerState callerState)
        {
            if (calleeGenerics.Count == 0
                || TryGetCallSiteTypeArgumentList(callBase) is not { } typeArgList)
            {
                return returnType;
            }

            var typeArgs = typeArgList.GetAllNotNull().ToList();
            if (typeArgs.Count == 0)
            {
                return returnType;
            }

            var bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            var count = Math.Min(calleeGenerics.Count, typeArgs.Count);
            for (var i = 0; i < count; i++)
            {
                var argType = ResolveTypeExpression(typeArgs[i], callerState);
                if (!TypeComparer.IsUnresolvedType(argType))
                {
                    bindings[calleeGenerics[i]] = argType;
                }
            }

            if (bindings.Count == 0)
            {
                return returnType;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(
                returnType, bindings, _symbolTree, _globalScope);
        }

        /// <summary>
        /// Explicit generic type arguments hang off the callee name: free functions under
        /// <c>identifier</c>, <c>::</c>/<c>-&gt;</c> members under <c>memberName</c> (or
        /// <c>identifier</c> on older paths).
        /// </summary>
        private static PhpTypeExpressionListAst? TryGetCallSiteTypeArgumentList(
            IDereferenceableBase? baseNode)
        {
            IBase2Ast? nameNode = baseNode switch
            {
                PhpNameAst name => name,
                PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst instance } => instance.MemberName,
                PhpDereferenceableAst { Suffix: PhpStaticMemberAccessAst staticAccess } => staticAccess.Member,
                PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst classConst } => classConst.Member,
                _ => null,
            };

            if (nameNode is null)
            {
                return null;
            }

            if (nameNode.AstGrammarAddons.TryGetValue("memberName", out var member)
                && member is PhpTypeExpressionListAst memberList)
            {
                return memberList;
            }

            if (nameNode.AstGrammarAddons.TryGetValue("identifier", out var identifier)
                && identifier is PhpTypeExpressionListAst identifierList)
            {
                return identifierList;
            }

            return null;
        }

        /// <summary>
        /// Resolves a member's declared type annotation against a receiver instance, applying the
        /// receiver's generic type-argument substitutions (e.g. <c>Box&lt;string&gt;::set</c>'s
        /// <c>TValue</c> parameter becomes <c>string</c>), then explicit call-site type arguments
        /// on the method itself (e.g. <c>Box::identity&lt;U&gt;($value)</c>'s own <c>T</c> becomes
        /// <c>U</c>) — mirrors <see cref="ResolveMethodReturnType"/>'s handling for return types.
        /// </summary>
        internal ICheckedType ResolveMemberDeclaredType(
            Ast.Interfaces.ITypeExpression declaredType,
            ICheckedType receiverType,
            CheckerState state,
            ObjectMethodSymbol? method = null,
            IDereferenceableBase? callBase = null)
        {
            var resolved = ResolveDeclaredTypeOnReceiver(
                declaredType,
                receiverType,
                state,
                isReturnTypePosition: false,
                declaringClass: method is not null
                    ? FindDeclaringClass(receiverType, method) ?? TryGetOwningObjectDeclaration(method)
                    : null,
                functionGenerics: method?.GenericParameters);

            return method is null
                ? resolved
                : ApplyCallSiteGenericSubstitution(resolved, method.GenericParameters, callBase, state);
        }

        /// <summary>
        /// Resolves a free-function parameter annotation in the callee's generic scope (so
        /// <c>array_values&lt;TKey, TValue&gt;(array&lt;TKey, TValue&gt;)</c> does not collapse
        /// <c>TKey</c> to <c>unresolved</c> at the call site — Story 11 audit #5), then applies
        /// explicit call-site type arguments.
        /// </summary>
        internal ICheckedType ResolveFunctionDeclaredType(
            Ast.Interfaces.ITypeExpression declaredType,
            FunctionDeclarationSymbol function,
            CheckerState callerState,
            IDereferenceableBase? callBase = null)
        {
            var resolveState = callerState;
            if (function.GenericParameters.Count > 0)
            {
                // Fork (mutable): callee params are often tyhpdef annotations resolved via a
                // nested SnapShot for declaring-file scope (see ResolveTypeExpression).
                resolveState = callerState.Fork();
                resolveState.FunctionGenerics = function.GenericParameters;
            }

            var resolved = ResolveTypeExpression(declaredType, resolveState, isReturnTypePosition: false);
            return ApplyCallSiteGenericSubstitution(
                resolved, function.GenericParameters, callBase, callerState);
        }

        /// <summary>
        /// Resolves a member's declared type in the generic scope of the class that <em>declared</em> it,
        /// then substitutes the receiver's type arguments (e.g. <c>Promise&lt;T&gt;::$value</c> as
        /// <c>TReturn</c> becomes <c>T</c>).
        /// </summary>
        /// <remarks>
        /// The declaring class rather than the receiver's class is what makes an inherited member bind to
        /// the right parameter: <c>class Derived&lt;T&gt; extends Base&lt;string&gt;</c> has a <c>T</c> of
        /// its own, and resolving Base's <c>?T</c> in Derived's scope silently retargets it.
        /// </remarks>
        private ICheckedType ResolveDeclaredTypeOnReceiver(
            Ast.Interfaces.ITypeExpression declaredType,
            ICheckedType receiverType,
            CheckerState state,
            bool isReturnTypePosition = false,
            ObjectDeclarationSymbol? declaringClass = null,
            IReadOnlyList<GenericTypeParameterSymbol>? functionGenerics = null)
        {
            var resolveState = state;
            var scopeOwner = declaringClass;
            if (scopeOwner is null && TryGetObjectDeclarationFromReceiver(receiverType, out var objectDecl))
            {
                scopeOwner = objectDecl;
            }

            var needsObjectGenerics = scopeOwner is not null && scopeOwner.GenericParameters.Count > 0;
            var needsFunctionGenerics = functionGenerics is { Count: > 0 };
            // Always rebind to the declaring type's scope for member annotations — not only when
            // generics are involved. Short names and `use` imports in the declaring file must win
            // over the access site's namespace/imports (PathNode::$body as ExpressionNode).
            var needsDeclaringScope = scopeOwner is not null;
            if (needsDeclaringScope || needsFunctionGenerics)
            {
                // Class type parameters on the property/return AST (e.g. <c>TReturn</c>) must bind
                // even when the access site is outside that class or uses different function generics.
                // Method/function parameters (FOUND_BUGS item 39) go into FunctionGenerics so they
                // shadow a same-named class parameter when resolving the member's declared type.
                // Fork (mutable): ResolveTypeExpression may SnapShot for cross-file rebinding.
                resolveState = state.Fork();
                if (scopeOwner is not null)
                {
                    resolveState.EnclosingObject = scopeOwner;
                    resolveState.EnclosingObjectType = CheckedTypes.FromSymbol(scopeOwner);
                    // GetResolutionScope prefers EnclosingFunction; clear it so the declaring
                    // object's ContainingScope (and that file's `use` imports) is used.
                    resolveState.EnclosingFunction = null;
                    if (scopeOwner.ContainingScope is { } declaringScope)
                    {
                        resolveState.NameResolutionScope = declaringScope;
                    }

                    if (needsObjectGenerics)
                    {
                        resolveState.ObjectGenerics = scopeOwner.GenericParameters;
                    }
                }

                if (needsFunctionGenerics)
                {
                    resolveState.FunctionGenerics = functionGenerics!;
                }
            }

            return ApplyReceiverGenericSubstitution(
                receiverType,
                ResolveTypeExpression(declaredType, resolveState, isReturnTypePosition),
                state);
        }

        /// <summary>
        /// The class in the receiver's hierarchy that declares <paramref name="member"/>. Members are
        /// registered only on their own declaration, so the first level whose table holds this exact
        /// symbol is the one that declared it.
        /// </summary>
        private ObjectDeclarationSymbol? FindDeclaringClass(ICheckedType receiverType, IBaseSymbol? member)
        {
            if (member is null)
            {
                return null;
            }

            if (TryGetObjectDeclarationFromReceiver(receiverType, out var level))
            {
                var visited = new HashSet<ObjectDeclarationSymbol>();
                ObjectDeclarationSymbol? current = level;
                while (current is not null && visited.Add(current))
                {
                    if (current.Members.Values.Any(candidate => ReferenceEquals(candidate, member))
                        || current.Constants.Values.Any(candidate => ReferenceEquals(candidate, member)))
                    {
                        return current;
                    }

                    current = TypeComparer.TryGetParentDeclaration(current, _symbolTree, _globalScope);
                }
            }

            // Chained `: static` / unresolved receivers still belong to the method's class.
            // Without this, `select<R>(Expression<T, R>)` after `->where(...)` reports TYHP3003
            // on the class parameter `T` because ObjectGenerics were never copied from QueryBuilder.
            return TryGetOwningObjectDeclaration(member);
        }

        private static ObjectDeclarationSymbol? TryGetOwningObjectDeclaration(IBaseSymbol member)
        {
            for (var scope = member.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol objectDecl)
                {
                    return objectDecl;
                }
            }

            return null;
        }

        private ICheckedType ApplyReceiverGenericSubstitution(
            ICheckedType receiverType,
            ICheckedType memberType,
            CheckerState state)
        {
            if (!TryBuildReceiverGenericBindings(receiverType, state, out var bindings))
            {
                return memberType;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(
                memberType, bindings, _symbolTree, _globalScope);
        }

        /// <summary>
        /// Binds every generic parameter reachable from the receiver — its own and each generic
        /// ancestor's — to a concrete type argument, keyed by parameter symbol so that same-named
        /// parameters at different levels stay distinct. See FOUND_BUGS.md item 11.
        /// </summary>
        private bool TryBuildReceiverGenericBindings(
            ICheckedType receiverType,
            CheckerState state,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings) =>
            GenericInheritanceBindings.TryBuild(
                receiverType,
                state,
                _symbolTree,
                _globalScope,
                ResolveTypeExpressionCore,
                out bindings);

        private static bool TryGetObjectDeclarationFromReceiver(
            ICheckedType receiverType,
            out ObjectDeclarationSymbol objectDecl)
        {
            objectDecl = null!;
            var unwrapped = UnwrapForMemberAccess(receiverType);
            if (unwrapped is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                objectDecl = obj;
                return true;
            }

            if (Rules.CheckerHelpers.TryGetObjectDeclaration(receiverType) is { } fromHelper)
            {
                objectDecl = fromHelper;
                return true;
            }

            return false;
        }

        private static bool ReturnTypeReferencesRelativeName(Ast.Interfaces.ITypeExpression typeAst) =>
            typeAst switch
            {
                PhpNamedTypeAst named => IsRelativeTypeName(GetExpressionText(named.Name)),
                // `static`/`self`/`parent` (including parameterized forms like `self<T>`) are parsed
                // as builtin or named types; they too must be resolved against the correct class.
                PhpBuiltinTypeAst builtin => IsRelativeTypeName(builtin.Identifier),
                PhpTypeExpressionAst composite => composite.Types?.GetAllNotNull()
                    .Any(ReturnTypeReferencesRelativeName) ?? false,
                _ => false,
            };

        private ICheckedType InferCallableFromMethod(
            ICheckedType ownerType,
            string methodName,
            bool staticOnly,
            CheckerState state)
        {
            if (!TryResolveMethodOnType(ownerType, methodName, staticOnly, state, out var method) || method is null)
            {
                return CheckedTypes.Unresolved;
            }

            var declaringClass = FindDeclaringClass(ownerType, method);
            var paramTypes = method.Parameters
                .Select(param => param.DeclaredType is not null
                    ? ResolveDeclaredTypeOnReceiver(
                        param.DeclaredType,
                        ownerType,
                        state,
                        declaringClass: declaringClass,
                        functionGenerics: method.GenericParameters)
                    : CheckedTypes.Unresolved)
                .ToList();

            return CallableArityFacetBuilder.BuildFromParameterInfos(
                method.Parameters,
                paramTypes,
                ResolveMethodReturnType(method, ownerType, state));
        }

        private static bool IsFirstClassCallableSyntax(PhpCallAst call) =>
            Rules.CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments);

        private ICheckedType InferFirstClassCallable(
            ICheckedType current,
            IDereferenceableBase? baseNode,
            CheckerState state)
        {
            if (baseNode is PhpNameAst nameAst
                && Rules.CheckerHelpers.ResolveFreeFunction(nameAst, state, _symbolTree, _globalScope)
                    is { } function)
            {
                return InferCallableFromFunction(function, state);
            }

            if (baseNode is PhpDereferenceableAst chain)
            {
                string? methodName = null;
                var staticOnly = false;
                switch (chain.Suffix)
                {
                    case PhpInstanceMemberAccessAst instanceAccess:
                        methodName = GetExpressionText(instanceAccess.MemberName);
                        staticOnly = false;
                        break;
                    case PhpStaticMemberAccessAst staticAccess:
                        methodName = GetExpressionText(staticAccess.Member);
                        staticOnly = true;
                        break;
                    case PhpClassConstantAccessAst classConstAccess:
                        methodName = GetExpressionText(classConstAccess.Member);
                        staticOnly = true;
                        break;
                }

                if (methodName is not null)
                {
                    var receiverType = InferDereferenceableBase(chain.Base, state);
                    return InferCallableFromMethod(receiverType, methodName, staticOnly, state);
                }
            }

            // Already-resolved callable type on the base (e.g. a Closure property), including an
            // intersection of optional-arity facets.
            if (CallableArityFacetBuilder.IsCallableFacetType(current))
            {
                return current;
            }

            return CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable"));
        }

        private ICheckedType InferCallableFromFunction(
            FunctionDeclarationSymbol function,
            CheckerState state)
        {
            var resolveState = state;
            if (function.GenericParameters.Count > 0)
            {
                resolveState = state.Fork();
                resolveState.FunctionGenerics = function.GenericParameters;
            }

            var paramTypes = function.Parameters
                .Select(param => param.DeclaredType is not null
                    ? ResolveTypeExpression(param.DeclaredType, resolveState)
                    : CheckedTypes.Unresolved)
                .ToList();

            ICheckedType returnType = function.ReturnType switch
            {
                null => CheckedTypes.Mixed,
                TyhpReturnTypeGuardAst => CheckedTypes.Bool,
                { } rt => ResolveTypeExpression(rt, resolveState, isReturnTypePosition: true),
            };

            return CallableArityFacetBuilder.BuildFromParameterInfos(
                function.Parameters,
                paramTypes,
                returnType);
        }

        private bool TryResolveMethodOnType(
            ICheckedType type,
            string memberName,
            bool staticOnly,
            CheckerState state,
            out ObjectMethodSymbol? method)
        {
            method = null;
            if (UnwrapForMemberAccess(type) is not SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return false;
            }

            var fromScope = GetResolutionScope(state);
            var resolved = _symbolTree.ResolveMember(memberName, obj, _diagnostics);
            if (resolved is ObjectMethodSymbol methodSymbol && (!staticOnly || methodSymbol.IsStatic))
            {
                method = methodSymbol;
                return true;
            }

            _ = fromScope;
            return false;
        }

        private bool TryResolvePropertyOnType(
            ICheckedType type,
            string memberName,
            CheckerState state,
            out ObjectPropertySymbol? property)
        {
            property = null;
            if (UnwrapForMemberAccess(type) is not SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return false;
            }

            // Properties are registered in the member table under their declared name including
            // the leading '$' (keeping the property namespace distinct from the method namespace).
            // Member access yields the bare name, so normalize before resolving.
            var propertyName = memberName.StartsWith('$') ? memberName : "$" + memberName;
            var resolved = _symbolTree.ResolveMember(propertyName, obj, _diagnostics);
            if (resolved is ObjectPropertySymbol propertySymbol)
            {
                property = propertySymbol;
                return true;
            }

            _ = state;
            return false;
        }

        // Resolves a bare name used as a dereferenceable base. When the name carries call-site
        // type arguments (`self<T>::`, `Box<int>::`), prefer the parameterized class type so
        // `self<T>::class` brands as `__ClassName<Promise<T>>`. Otherwise, when the name is bound
        // to a symbol use that; else the name is a class reference in a static-access position
        // (`Class::`, `self::`, `static::`, `parent::`) that the binder does not bind.
        private ICheckedType InferNameBaseType(PhpNameAst name, CheckerState state)
        {
            if (TryResolveNewClassNameWithTypeArguments(name, state, out var parameterized))
            {
                return parameterized;
            }

            if (name.BoundSymbol is not null)
            {
                return CheckedTypes.FromSymbol(name.BoundSymbol);
            }

            return ResolveClassReceiverType(name, state) ?? CheckedTypes.Unresolved;
        }

        // Resolves a class-name reference (including the `self`/`static`/`parent` pseudo-types) to
        // its object type, or null when the name does not denote a class.
        private ICheckedType? ResolveClassReceiverType(PhpNameAst nameAst, CheckerState state)
        {
            var rawName = nameAst.ValueString;
            if (string.IsNullOrEmpty(rawName))
            {
                return null;
            }

            var simpleName = rawName.TrimStart('\\');
            if (string.Equals(simpleName, "self", StringComparison.OrdinalIgnoreCase))
            {
                return state.EnclosingObjectType
                    ?? (state.EnclosingObject is not null
                        ? CheckedTypes.FromSymbol(state.EnclosingObject)
                        : null);
            }

            if (string.Equals(simpleName, "static", StringComparison.OrdinalIgnoreCase))
            {
                var declaring = state.EnclosingObjectType
                    ?? (state.EnclosingObject is not null
                        ? CheckedTypes.FromSymbol(state.EnclosingObject)
                        : null);
                return declaring is null || TypeComparer.IsUnresolvedType(declaring)
                    ? declaring
                    : new StaticCheckedType(declaring);
            }

            // `extends` is usually a raw IClassName, so ExtendsType is often null — resolve via
            // TryGetParentDeclaration (AST fallback), same as override / subtyping walks.
            if (string.Equals(simpleName, "parent", StringComparison.OrdinalIgnoreCase)
                && state.EnclosingObject is { } enclosing)
            {
                if (enclosing.ExtendsType is { } extendsType)
                {
                    return ResolveTypeExpression(extendsType, state);
                }

                if (TypeComparer.TryGetParentDeclaration(enclosing, _symbolTree, _globalScope)
                    is { } parentObj)
                {
                    return CheckedTypes.FromSymbol(parentObj);
                }

                return null;
            }

            var scope = GetResolutionScope(state);

            // Qualified names cannot be found by a simple lexical-scope walk. Fully-qualified
            // (`\Foo\Bar`) resolve from the global root; relative qualified (`Foo\Bar`) resolve
            // against the enclosing namespace / leading `use` alias. A bare name falls back to
            // namespace-relative resolution, which (unlike the lexical walk) finds a class declared
            // in the same namespace but a different source file.
            IBaseSymbol? symbol;
            if (rawName.StartsWith('\\'))
            {
                symbol = _symbolTree.ResolveQualifiedName(
                    simpleName.Split('\\'), scope, _diagnostics);
            }
            else if (rawName.Contains('\\'))
            {
                symbol = _symbolTree.ResolveRelativeName(
                    simpleName.Split('\\'), scope, _diagnostics);
            }
            else
            {
                symbol = _symbolTree.ResolveSymbol(rawName, scope, _diagnostics)
                    ?? _symbolTree.ResolveRelativeName([simpleName], scope, _diagnostics);
            }

            // Record on the AST so the emitter can spell the true FQN for relative-qualified /
            // use-aliased names (Prop-init #17). Unambiguous bare names keep BoundSymbol for
            // resolution but TrackAndBuildName still emits the written short name.
            if (symbol is ObjectDeclarationSymbol objSymbol)
            {
                nameAst.BoundSymbol = objSymbol;
                return CheckedTypes.FromSymbol(objSymbol);
            }

            return null;
        }

        // Member access (`->`/`::`) resolves against the underlying object type. A nullable
        // receiver (`?T`) still exposes the members of `T` (the receiver may have been narrowed
        // to non-null by a prior check, or null-safety is enforced separately), so unwrap any
        // nullable layers before looking up members.
        private static ICheckedType UnwrapForMemberAccess(ICheckedType type)
        {
            // Members are declared on the underlying object symbol, so peel away nullability, the
            // late-bound `static` wrapper, and the generic instantiation wrapper (`Foo<T>` exposes
            // the same members as `Foo`) before resolving methods/properties.
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
    }
}
