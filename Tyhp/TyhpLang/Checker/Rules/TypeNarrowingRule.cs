using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Control-flow type narrowing (smart casts) for instanceof, null/true/false identity
    /// checks, and type guards. Subjects include locals, `$this->prop`, enclosing-class
    /// `self::$prop`, `$var->prop`, and constant/simple-variable index access.
    /// </summary>
    internal static class TypeNarrowingRule
    {
        public static void ApplyConditionNarrowing(
            IExpression? condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool positive)
        {
            if (condition is null)
            {
                return;
            }

            // Unwrap parentheses and leading logical-not (`!` / `not`). Each `!` flips the
            // positive/negative polarity so `if (!\is_string($x))` applies negative narrowing
            // in the then-branch (and positive in the else / early-exit fall-through). Bare
            // `(expr)` is PhpDereferenceableExpressionAst — same shape as ternary conditions.
            while (true)
            {
                while (condition is PhpDereferenceableExpressionAst { Expression: IExpression inner })
                {
                    condition = inner;
                }

                if (condition is PhpUnaryOpAst unary
                    && IsLogicalNot(unary)
                    && unary.Operand is IExpression notOperand)
                {
                    positive = !positive;
                    condition = notOperand;
                    continue;
                }

                break;
            }

            // Compound boolean conditions narrow component-wise *for a branch body*. In the
            // positive (then) branch of `a && b` both operands hold, so narrow each. By De Morgan,
            // in the negative (else) branch of `a || b` neither operand held, so narrow each
            // negatively. The other two combinations (`a || b` positive, `a && b` negative) cannot
            // soundly narrow a single variable in the branch because either operand alone may be
            // responsible. Short-circuit checking of later *operands* (right of `&&` after a
            // proven left, right of `||` after a disproven left) is a separate walk in
            // `TypeCompatibilityRule.CheckBinaryOp` / `CheckerHelpers.CheckCompileTimeConstructsInTree`.
            if (condition is PhpBinaryOpAst { Operator.ValueString: { } logicalOp } logical
                && logical.Left is not null && logical.Right is not null)
            {
                if (positive && IsLogicalAnd(logicalOp))
                {
                    ApplyConditionNarrowing(logical.Left, branchState, context, symbolTree, globalScope, positive: true);
                    ApplyConditionNarrowing(logical.Right, branchState, context, symbolTree, globalScope, positive: true);
                    return;
                }

                if (!positive && IsLogicalOr(logicalOp))
                {
                    ApplyConditionNarrowing(logical.Left, branchState, context, symbolTree, globalScope, positive: false);
                    ApplyConditionNarrowing(logical.Right, branchState, context, symbolTree, globalScope, positive: false);
                    return;
                }
            }

            if (TryApplyInstanceofNarrowing(condition, branchState, context, symbolTree, globalScope, positive))
            {
                return;
            }

            if (TryApplyIdentityLiteralNarrowing(
                    condition, branchState, context, symbolTree, globalScope, positive))
            {
                return;
            }

            if (TryApplyIssetNarrowing(condition, branchState, context, symbolTree, globalScope, positive))
            {
                return;
            }

            if (TryApplyVariableExistsNarrowing(condition, branchState, globalScope, positive))
            {
                return;
            }

            TryApplyTypeGuardCallNarrowing(condition, branchState, context, symbolTree, globalScope, positive);
        }

        public static void ResetNarrowingOnAssignment(string variableName, CheckerState state)
        {
            state.ResetNarrowing(variableName);
            state.ResetIndexAccessNarrowingForVariable(variableName);
            state.ResetMemberAccessNarrowingForVariable(variableName);
            if (state.LookupVariable(variableName) is { } varState)
            {
                varState.IsPossiblyNull = varState.DeclaredType?.IsNullable ?? false;
            }
        }

        private static bool TryApplyInstanceofNarrowing(
            IExpression condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool positive)
        {
            if (condition is not PhpBinaryOpAst binary
                || !IsInstanceofOperator(binary)
                || binary.Right is null)
            {
                return false;
            }

            var narrowedType = CheckerHelpers.ResolveInstanceofTargetType(
                binary.Right, branchState, context, symbolTree, globalScope);

            if (binary.Left is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is null || branchState.LookupVariable(name) is not { } varState)
                {
                    return false;
                }

                if (positive)
                {
                    // Intersect with the current effective type so a redundant guard on an already
                    // precise variable (e.g. `array $x` + `instanceof array|\Traversable`) does not
                    // widen to the guard's full union.
                    var narrowed = TypeComparer.NarrowType(
                        varState.EffectiveType, narrowedType, symbolTree, globalScope);
                    branchState.NarrowVariable(name, narrowed);
                    // `instanceof` never matches null, so a positive match clears possibly-null even
                    // when the pre-narrowing type was nullable (e.g. `?Foo $x` + `$x instanceof Foo`).
                    // This was previously missing here (unlike the null/built-in/user-guard narrowing
                    // paths below), so `Foo $y = $x;` right after the guard spuriously reported 4015.
                    varState.IsPossiblyNull = narrowed.IsNullable;
                }
                else
                {
                    var negative = TypeComparer.NarrowTypeNegative(
                        varState.EffectiveType, narrowedType, symbolTree, globalScope);
                    branchState.NarrowVariable(name, negative);
                }

                return true;
            }

            if (TryGetTrackedPropertyKey(binary.Left, branchState, out var propertyKey)
                && TryGetPropertyEffectiveType(branchState, propertyKey!, context, symbolTree, out var propEffective))
            {
                if (positive)
                {
                    var narrowed = TypeComparer.NarrowType(
                        propEffective, narrowedType, symbolTree, globalScope);
                    branchState.NarrowProperty(propertyKey!, narrowed);
                }
                else
                {
                    var negative = TypeComparer.NarrowTypeNegative(
                        propEffective, narrowedType, symbolTree, globalScope);
                    branchState.NarrowProperty(propertyKey!, negative);
                }

                return true;
            }

            if (TryGetMemberAccessKey(binary.Left, out var memberKey))
            {
                var prior = branchState.LookupMemberAccess(memberKey!)
                    ?? context.ResolveExpressionType(binary.Left!, branchState);
                if (positive)
                {
                    var narrowed = TypeComparer.NarrowType(
                        prior, narrowedType, symbolTree, globalScope);
                    branchState.NarrowMemberAccess(memberKey!, narrowed);
                }
                else
                {
                    var negative = TypeComparer.NarrowTypeNegative(
                        prior, narrowedType, symbolTree, globalScope);
                    branchState.NarrowMemberAccess(memberKey!, negative);
                }

                return true;
            }

            return false;
        }

        private static bool TryApplyIdentityLiteralNarrowing(
            IExpression condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool positive)
        {
            if (condition is not PhpBinaryOpAst binary)
            {
                return false;
            }

            var op = binary.Operator?.ValueString;
            var isStrictNot = string.Equals(op, "!==", StringComparison.Ordinal);
            var isStrict = string.Equals(op, "===", StringComparison.Ordinal);
            if (!isStrictNot && !isStrict)
            {
                return false;
            }

            IExpression? subject;
            ICheckedType? sentinelType;
            if (TryGetIdentitySentinelType(binary.Right, out sentinelType) && binary.Left is not null)
            {
                subject = binary.Left;
            }
            else if (TryGetIdentitySentinelType(binary.Left, out sentinelType) && binary.Right is not null)
            {
                subject = binary.Right;
            }
            else
            {
                return false;
            }

            var expectSentinel = (isStrict && positive) || (isStrictNot && !positive);
            var narrowed = expectSentinel
                ? TypeComparer.NarrowType(
                    ResolveSubjectType(subject, branchState, context, symbolTree),
                    sentinelType!,
                    symbolTree,
                    globalScope)
                : ExcludeIdentitySentinel(
                    ResolveSubjectType(subject, branchState, context, symbolTree),
                    sentinelType!,
                    symbolTree,
                    globalScope);

            return TryNarrowSubject(subject, narrowed, branchState, context, symbolTree, sentinelType!);
        }

        private static ICheckedType ResolveSubjectType(
            IExpression subject,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree)
        {
            if (subject is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is not null && branchState.LookupVariable(name) is { } varState)
                {
                    return varState.EffectiveType;
                }
            }

            if (TryGetTrackedPropertyKey(subject, branchState, out var propertyKey)
                && TryGetPropertyEffectiveType(branchState, propertyKey!, context, symbolTree, out var propEffective))
            {
                return propEffective;
            }

            if (TryGetMemberAccessKey(subject, out var memberKey))
            {
                return branchState.LookupMemberAccess(memberKey!)
                    ?? context.ResolveExpressionType(subject, branchState);
            }

            return context.ResolveExpressionType(subject, branchState);
        }

        private static bool TryNarrowSubject(
            IExpression subject,
            ICheckedType narrowed,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            ICheckedType sentinelType)
        {
            var excludingNull = TypeComparer.IsNullLiteral(sentinelType)
                || TypeComparer.IsBuiltInName(sentinelType, "null");

            if (subject is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is null || branchState.LookupVariable(name) is not { } varState)
                {
                    return false;
                }

                branchState.NarrowVariable(name, narrowed);
                if (excludingNull)
                {
                    varState.IsPossiblyNull = TypeComparer.IsNullLiteral(narrowed)
                        || TypeComparer.IsBuiltInName(narrowed, "null");
                }

                return true;
            }

            if (TryGetTrackedPropertyKey(subject, branchState, out var propertyKey)
                && TryGetPropertyEffectiveType(branchState, propertyKey!, context, symbolTree, out _))
            {
                branchState.NarrowProperty(propertyKey!, narrowed);
                return true;
            }

            if (TryGetMemberAccessKey(subject, out var memberKey))
            {
                branchState.NarrowMemberAccess(memberKey!, narrowed);
                return true;
            }

            return false;
        }

        /// <summary>
        /// <c>null</c> uses <see cref="RemoveNullability"/> so <c>?T</c> unwraps. <c>true</c>/<c>false</c>
        /// drop that arm of a union (and <c>bool !== false</c> becomes <c>true</c>).
        /// </summary>
        private static ICheckedType ExcludeIdentitySentinel(
            ICheckedType current,
            ICheckedType sentinel,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (TypeComparer.IsNullLiteral(sentinel) || TypeComparer.IsBuiltInName(sentinel, "null"))
            {
                return RemoveNullability(current);
            }

            if (TypeComparer.IsFalseType(sentinel) && TypeComparer.IsBuiltInName(current, "bool"))
            {
                return IdentitySentinelType(false);
            }

            if (TypeComparer.IsTrueType(sentinel) && TypeComparer.IsBuiltInName(current, "bool"))
            {
                return IdentitySentinelType(true);
            }

            return TypeComparer.NarrowTypeNegative(current, sentinel, symbolTree, globalScope);
        }

        private static bool TryGetIdentitySentinelType(IExpression? expression, out ICheckedType? sentinel)
        {
            sentinel = null;
            if (IsNullLiteral(expression))
            {
                sentinel = CheckedTypes.Null;
                return true;
            }

            if (IsBoolKeyword(expression, true))
            {
                sentinel = IdentitySentinelType(true);
                return true;
            }

            if (IsBoolKeyword(expression, false))
            {
                sentinel = IdentitySentinelType(false);
                return true;
            }

            return false;
        }

        private static ICheckedType IdentitySentinelType(bool value) =>
            new LiteralCheckedType(
                value,
                new SimpleCheckedType(new BuiltInTypeSymbol(value ? "true" : "false")));

        private static bool IsBoolKeyword(IExpression? expression, bool value)
        {
            var expected = value ? "true" : "false";
            return expression switch
            {
                PhpNameAst name =>
                    string.Equals(name.ValueString?.TrimStart('\\'), expected, StringComparison.OrdinalIgnoreCase),
                PhpScalarAst { ValueString: { } text } =>
                    string.Equals(text, expected, StringComparison.OrdinalIgnoreCase),
                PhpMagicConstantAst magic =>
                    string.Equals(magic.ValueString, expected, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        /// <summary>
        /// Strips null / nullability from a type for positive <c>!== null</c> / negative
        /// <c>=== null</c> narrowing. Handles <c>?T</c>, null literals, and unions that mix
        /// <c>?T</c> with other members (common after <see cref="CheckerState.Merge"/>).
        /// </summary>
        private static ICheckedType RemoveNullability(ICheckedType type)
        {
            if (type is NullableCheckedType nullable)
            {
                return nullable.InnerType;
            }

            if (type is LiteralCheckedType { Value: null })
            {
                return CheckedTypes.Never;
            }

            if (type is UnionCheckedType union)
            {
                var members = new List<ICheckedType>();
                foreach (var member in union.Members)
                {
                    if (member is LiteralCheckedType { Value: null })
                    {
                        continue;
                    }

                    if (member.Kind == CheckedTypeKind.Unresolved)
                    {
                        continue;
                    }

                    members.Add(member is NullableCheckedType nested ? nested.InnerType : member);
                }

                return members.Count switch
                {
                    0 => CheckedTypes.Never,
                    1 => members[0],
                    _ => CheckedTypes.UnionTypes(members),
                };
            }

            return type;
        }

        // The `null` literal can reach the checker either as a scalar token or as a bareword
        // constant (`PhpNameAst`), depending on the parse context. Recognize both so null-check
        // narrowing (`$x !== null`) fires regardless of representation.
        private static bool IsNullLiteral(IExpression? expression) =>
            expression switch
            {
                PhpScalarAst { ValueString: "null" } => true,
                PhpNameAst name => string.Equals(name.ValueString?.TrimStart('\\'), "null", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

        private static bool TryApplyTypeGuardCallNarrowing(
            IExpression condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool positive)
        {
            if (!TryGetGuardFunctionCall(condition, out var call, out _))
            {
                return false;
            }

            // Narrowing comes only from the callee's `$param is` / `$param instanceof`
            // return (tyhp or tyhpdef). There is no name-based override.
            return TryApplyUserDefinedGuardNarrowing(
                condition, call, branchState, context, symbolTree, globalScope, positive);
        }

        private static bool TryApplyUserDefinedGuardNarrowing(
            IExpression condition,
            PhpCallAst call,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool positive)
        {
            if (!TryResolveUserDefinedGuardCallee(
                    condition,
                    branchState,
                    context,
                    symbolTree,
                    globalScope,
                    out var parameters,
                    out var guard,
                    out var genericParameters))
            {
                return false;
            }

            var args = GetCallArguments(call);

            if (!TryResolveCallSiteGuardSubject(guard, parameters, args, call, out var subject)
                || subject is null)
            {
                return false;
            }

            var guardType = ResolveUserDefinedGuardTargetType(
                guard,
                condition,
                call,
                genericParameters,
                parameters,
                branchState,
                context,
                symbolTree,
                globalScope);

            if (subject is PhpVariableAst argVar)
            {
                var argName = CheckerHelpers.GetVariableName(argVar);
                if (argName is null || branchState.LookupVariable(argName) is not { } varState)
                {
                    return false;
                }

                if (positive)
                {
                    // Intersect rather than replace — keeps a narrower declared type under a
                    // broader union guard (e.g. `array $x` + `\is_iterable($x)`).
                    var narrowed = TypeComparer.NarrowType(
                        varState.EffectiveType, guardType, symbolTree, globalScope);
                    branchState.NarrowVariable(argName, narrowed);
                    varState.IsPossiblyNull = narrowed.IsNullable;
                    return true;
                }

                var negative = TypeComparer.NarrowTypeNegative(
                    varState.EffectiveType, guardType, symbolTree, globalScope);
                if (TypeComparer.AreTypesEqual(negative, varState.EffectiveType))
                {
                    return false;
                }

                branchState.NarrowVariable(argName, negative);
                varState.IsPossiblyNull = negative.IsNullable;
                return true;
            }

            if (TryGetTrackedPropertyKey(subject, branchState, out var propertyKey)
                && TryGetPropertyEffectiveType(branchState, propertyKey!, context, symbolTree, out var propEffective))
            {
                if (positive)
                {
                    var narrowed = TypeComparer.NarrowType(
                        propEffective, guardType, symbolTree, globalScope);
                    branchState.NarrowProperty(propertyKey!, narrowed);
                    return true;
                }

                var negative = TypeComparer.NarrowTypeNegative(
                    propEffective, guardType, symbolTree, globalScope);
                if (TypeComparer.AreTypesEqual(negative, propEffective))
                {
                    return false;
                }

                branchState.NarrowProperty(propertyKey!, negative);
                return true;
            }

            if (TryGetMemberAccessKey(subject, out var memberKey))
            {
                var prior = branchState.LookupMemberAccess(memberKey!)
                    ?? context.ResolveExpressionType(subject, branchState);
                if (positive)
                {
                    var narrowed = TypeComparer.NarrowType(
                        prior, guardType, symbolTree, globalScope);
                    branchState.NarrowMemberAccess(memberKey!, narrowed);
                    return true;
                }

                var negative = TypeComparer.NarrowTypeNegative(
                    prior, guardType, symbolTree, globalScope);
                if (TypeComparer.AreTypesEqual(negative, prior))
                {
                    return false;
                }

                branchState.NarrowMemberAccess(memberKey!, negative);
                return true;
            }

            if (TryGetIndexAccessKey(subject, out var indexKey))
            {
                var prior = branchState.LookupIndexAccess(indexKey!)
                    ?? context.ResolveExpressionType(subject, branchState);
                if (positive)
                {
                    var narrowed = TypeComparer.NarrowType(
                        prior, guardType, symbolTree, globalScope);
                    branchState.NarrowIndexAccess(indexKey!, narrowed);
                    return true;
                }

                var negative = TypeComparer.NarrowTypeNegative(
                    prior, guardType, symbolTree, globalScope);
                if (TypeComparer.AreTypesEqual(negative, prior))
                {
                    return false;
                }

                branchState.NarrowIndexAccess(indexKey!, negative);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves a free-function or method callee whose return type is a <c>$param is Type</c>
        /// guard. Static calls are parsed as <c>Class::name</c> via
        /// <see cref="PhpClassConstantAccessAst"/> (or emitter <see cref="PhpStaticMemberAccessAst"/>).
        /// </summary>
        private static bool TryResolveUserDefinedGuardCallee(
            IExpression condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out List<ParameterInfo> parameters,
            out TyhpReturnTypeGuardAst guard,
            out IReadOnlyList<GenericTypeParameterSymbol> genericParameters)
        {
            parameters = null!;
            guard = null!;
            genericParameters = [];

            if (condition is not PhpDereferenceableAst deref)
            {
                return false;
            }

            if (deref.Base is PhpNameAst nameAst)
            {
                if (CheckerHelpers.ResolveFreeFunction(nameAst, branchState, symbolTree, globalScope)
                        is not { } func)
                {
                    return false;
                }

                if (condition is PhpDereferenceableAst { Suffix: PhpCallAst guardCall })
                {
                    // Arity alone cannot disambiguate same-arity overloads whose guard differs
                    // by argument *type* (`is_a`'s object-instance vs `$allow_string = true`
                    // class-name-string overloads both take 3 args). Score by argument
                    // compatibility, same as ordinary call-site overload resolution, so the
                    // guard picked here always matches the overload the call itself resolves to.
                    func = FunctionOverloadSelector.Select(
                        func,
                        guardCall,
                        new FunctionOverloadSelector.Context
                        {
                            State = branchState,
                            SymbolTree = symbolTree,
                            GlobalScope = globalScope,
                            InferArgumentType = expr => context.ResolveExpressionType(expr, branchState),
                            ResolveParameterType = (candidate, typeAst) =>
                            {
                                var candidateState = branchState.Fork();
                                candidateState.FunctionGenerics = candidate.GenericParameters;
                                return context.ResolveTypeAnnotation(typeAst, candidateState);
                            },
                            InferBindings = (candidate, inferCall) =>
                                context.TryInferGenericBindings(
                                    candidate.GenericParameters,
                                    candidate.Parameters,
                                    inferCall,
                                    branchState,
                                    out var inferredBindings)
                                    ? inferredBindings
                                    : null,
                        });
                }

                if (func.ReturnType is not TyhpReturnTypeGuardAst freeGuard
                    || freeGuard.TypeExpression is null)
                {
                    return false;
                }

                parameters = func.Parameters;
                guard = freeGuard;
                genericParameters = func.GenericParameters;
                return true;
            }

            if (deref.Base is not PhpDereferenceableAst chain || chain.Base is null)
            {
                return false;
            }

            string? methodName;
            bool staticOnly;
            ICheckedType receiverType;
            switch (chain.Suffix)
            {
                case PhpStaticMemberAccessAst staticAccess:
                    methodName = GetMemberNameText(staticAccess.Member);
                    staticOnly = true;
                    receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                        chain.Base, branchState, context, symbolTree, globalScope);
                    break;
                case PhpClassConstantAccessAst classConstAccess:
                    methodName = GetMemberNameText(classConstAccess.Member);
                    staticOnly = true;
                    receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                        chain.Base, branchState, context, symbolTree, globalScope);
                    break;
                case PhpInstanceMemberAccessAst instanceAccess:
                    methodName = GetMemberNameText(instanceAccess.MemberName);
                    staticOnly = false;
                    receiverType = context.ResolveExpressionType(chain.Base, branchState);
                    break;
                default:
                    return false;
            }

            if (methodName is null
                || CheckerHelpers.TryGetObjectDeclaration(receiverType) is not { } objectDecl)
            {
                return false;
            }

            if (symbolTree.ResolveMember(methodName, objectDecl, new DiagnosticBag())
                    is not ObjectMethodSymbol method
                || (staticOnly && !method.IsStatic)
                || method.ReturnType is not TyhpReturnTypeGuardAst methodGuard
                || methodGuard.TypeExpression is null)
            {
                return false;
            }

            parameters = method.Parameters;
            guard = methodGuard;
            genericParameters = method.GenericParameters;
            return true;
        }

        /// <summary>
        /// Resolves the guard's target type. Order: explicit call-site type arguments, then
        /// argument-driven inference (<c>TryInferGenericBindings</c>), then omitted generic
        /// defaults. Inference runs before resolving <c>__PropertyName&lt;T&gt;</c> so T is
        /// bound (a class or <c>Foo::class</c> brand argument) rather than unconstrained.
        /// </summary>
        private static ICheckedType ResolveUserDefinedGuardTargetType(
            TyhpReturnTypeGuardAst guard,
            IExpression condition,
            PhpCallAst call,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IReadOnlyList<ParameterInfo> parameters,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            Dictionary<string, ICheckedType>? substitutions = null;
            if (genericParameters.Count > 0)
            {
                var typeArgs = TryGetCallSiteGenericTypeArguments(condition, call)
                    ?.GetAllNotNull()
                    .ToList()
                    ?? [];

                substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                var unbound = new List<GenericTypeParameterSymbol>();
                for (var i = 0; i < genericParameters.Count; i++)
                {
                    var param = genericParameters[i];
                    if (i < typeArgs.Count)
                    {
                        // Call-site args resolve in the caller's scope (e.g. class generic TValue).
                        substitutions[param.Name] =
                            context.ResolveTypeAnnotation(typeArgs[i], branchState);
                        continue;
                    }

                    unbound.Add(param);
                }

                if (unbound.Count > 0
                    && context.TryInferGenericBindings(
                        genericParameters,
                        parameters,
                        call,
                        branchState,
                        out var inferred)
                    && inferred.Count > 0)
                {
                    foreach (var pair in inferred)
                    {
                        if (!substitutions.ContainsKey(pair.Key.Name))
                        {
                            substitutions[pair.Key.Name] = pair.Value;
                            unbound.Remove(pair.Key);
                        }
                    }
                }

                foreach (var param in unbound)
                {
                    if (param.DefaultType is null)
                    {
                        continue;
                    }

                    // Defaults resolve in the callee's generic scope so they can mention earlier
                    // parameters; already-bound substitutions are applied afterward.
                    var defaultState = branchState.Fork();
                    defaultState.FunctionGenerics = genericParameters;
                    var defaultType = context.ResolveTypeAnnotation(param.DefaultType, defaultState);
                    if (substitutions.Count > 0)
                    {
                        defaultType = TypeComparer.ResolveGenericType(
                            defaultType, substitutions, symbolTree, globalScope);
                    }

                    if (!TypeComparer.IsUnresolvedType(defaultType))
                    {
                        substitutions[param.Name] = defaultType;
                    }
                }

                if (substitutions.Count == 0)
                {
                    substitutions = null;
                }
            }

            var previousGenerics = branchState.FunctionGenerics;
            branchState.FunctionGenerics = genericParameters;
            ICheckedType guardType;
            try
            {
                guardType = context.ResolveTypeAnnotation(guard.TypeExpression!, branchState);
            }
            finally
            {
                branchState.FunctionGenerics = previousGenerics;
            }

            if (substitutions is null || substitutions.Count == 0)
            {
                return guardType;
            }

            return TypeComparer.ResolveGenericType(guardType, substitutions, symbolTree, globalScope);
        }

        /// <summary>
        /// Call-site generics are attached to the callee name, not the argument list:
        /// free functions use the name's <c>identifier</c> addon; <c>::</c>/<c>-&gt;</c> members use
        /// <c>memberName</c>. Older/alternate paths may still put them on the call as
        /// <c>genericTypeArguments</c>.
        /// </summary>
        private static PhpTypeExpressionListAst? TryGetCallSiteGenericTypeArguments(
            IExpression condition,
            PhpCallAst call)
        {
            if (call.AstGrammarAddons.TryGetValue("genericTypeArguments", out var callAddon)
                && callAddon is PhpTypeExpressionListAst callList)
            {
                return callList;
            }

            if (condition is not PhpDereferenceableAst deref)
            {
                return null;
            }

            if (deref.Base is PhpNameAst freeName
                && TryGetTypeArgumentListAddon(freeName, "identifier") is { } freeList)
            {
                return freeList;
            }

            if (deref.Base is not PhpDereferenceableAst chain)
            {
                return null;
            }

            IExpression? memberExpr = chain.Suffix switch
            {
                PhpStaticMemberAccessAst staticAccess => staticAccess.Member,
                PhpClassConstantAccessAst classConst => classConst.Member,
                PhpInstanceMemberAccessAst instanceAccess => instanceAccess.MemberName,
                _ => null,
            };

            return memberExpr is IBase2Ast memberNode
                ? TryGetTypeArgumentListAddon(memberNode, "memberName")
                    ?? TryGetTypeArgumentListAddon(memberNode, "identifier")
                : null;
        }

        private static PhpTypeExpressionListAst? TryGetTypeArgumentListAddon(IBase2Ast node, string key)
        {
            if (!node.AstGrammarAddons.TryGetValue(key, out var addon))
            {
                return null;
            }

            return addon as PhpTypeExpressionListAst;
        }

        private static string? GetMemberNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => expression?.ValueString,
            };

        private static bool TryGetGuardFunctionCall(
            IExpression condition,
            out PhpCallAst call,
            out string fnName)
        {
            call = null!;
            fnName = string.Empty;
            if (condition is not PhpDereferenceableAst deref || deref.Suffix is not PhpCallAst guardCall)
            {
                return false;
            }

            call = guardCall;
            fnName = SymbolNameTypeHelper.GetSimpleFunctionName(
                deref.Base switch
                {
                    PhpNameAst name => name.ValueString,
                    PhpDereferenceableExpressionAst { Expression: PhpNameAst name } => name.ValueString,
                    _ => null,
                });
            if (fnName.Length > 0)
            {
                return true;
            }

            // Method call shape: (receiver::|->member)(...). Empty fnName routes to user-defined guards.
            return deref.Base is PhpDereferenceableAst chain
                && chain.Suffix is PhpStaticMemberAccessAst
                    or PhpClassConstantAccessAst
                    or PhpInstanceMemberAccessAst;
        }

        private static bool TryApplyIssetNarrowing(
            IExpression condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool positive)
        {
            if (!positive || condition is not PhpIssetStatementAst isset)
            {
                return false;
            }

            var applied = false;
            foreach (var expr in isset.Variables?.GetAllNotNull() ?? [])
            {
                if (TryNarrowVariableNameHolder(expr, branchState, globalScope))
                {
                    applied = true;
                    continue;
                }

                // Prop-init #8: isset($this->prop) guarantees the slot is initialized on the
                // positive arm (PHP isset does not throw on uninitialized typed properties).
                // isset is also false for null, so strip nullability like `!== null`.
                if (TryNarrowThisPropertyInitialized(expr, branchState, context, symbolTree))
                {
                    applied = true;
                    continue;
                }

                // Same null-strip for `$var->prop` via MemberAccessNarrowing.
                if (TryNarrowMemberAccessNonNull(expr, branchState, context))
                {
                    applied = true;
                }
            }

            return applied;
        }

        private static bool TryNarrowThisPropertyInitialized(
            IExpression expression,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree)
        {
            if (!TryGetTrackedPropertyKey(expression, branchState, out var propertyKey)
                || branchState.LookupPropertyInit(propertyKey!) is null)
            {
                return false;
            }

            // Capture effective type before AssignProperty clears NarrowedType.
            var hasEffective = TryGetPropertyEffectiveType(
                branchState, propertyKey!, context, symbolTree, out var effective);
            branchState.AssignProperty(propertyKey!);
            if (hasEffective)
            {
                branchState.NarrowProperty(propertyKey!, RemoveNullability(effective));
            }

            return true;
        }

        /// <summary>
        /// Positive <c>isset($var->prop)</c> strips nullability on the member-access key
        /// (mirrors <see cref="TryNarrowThisPropertyInitialized"/> for non-<c>$this</c> receivers).
        /// </summary>
        private static bool TryNarrowMemberAccessNonNull(
            IExpression expression,
            CheckerState branchState,
            INarrowingResolution context)
        {
            if (!TryGetMemberAccessKey(expression, out var memberKey))
            {
                return false;
            }

            var prior = branchState.LookupMemberAccess(memberKey!)
                ?? context.ResolveExpressionType(expression, branchState);
            branchState.NarrowMemberAccess(memberKey!, RemoveNullability(prior));
            return true;
        }

        /// <summary>
        /// True when <paramref name="expression"/> is a tracked property access:
        /// <c>$this->prop</c> or an enclosing-class static property
        /// (<c>self::$prop</c> / <c>static::$prop</c> / <c>ClassName::$prop</c>).
        /// </summary>
        internal static bool TryGetTrackedPropertyKey(
            IExpression? expression,
            CheckerState state,
            out string? propertyKey)
        {
            if (TryGetThisPropertyKey(expression, out propertyKey))
            {
                return true;
            }

            return TryGetEnclosingStaticPropertyKey(expression, state, out propertyKey);
        }

        /// <summary>
        /// True when <paramref name="expression"/> is a plain <c>$this->prop</c> access.
        /// </summary>
        private static bool TryGetThisPropertyKey(IExpression? expression, out string? propertyKey)
        {
            propertyKey = null;
            if (expression is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst receiver,
                    Suffix: PhpInstanceMemberAccessAst memberAccess,
                }
                || !CheckerHelpers.IsThisVariable(receiver))
            {
                return false;
            }

            var memberName = memberAccess.MemberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString ?? scalar.ValueInt64?.ToString(),
                PhpVariableAst variable => CheckerHelpers.GetVariableName(variable),
                IExpression expr => expr.Identifier,
                _ => null,
            };

            if (memberName is null || memberName.StartsWith('{'))
            {
                return false;
            }

            propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            return true;
        }

        /// <summary>
        /// True when <paramref name="expression"/> is <c>self::$prop</c>, <c>static::$prop</c>,
        /// or <c>ClassName::$prop</c> for the enclosing class. <c>parent::$prop</c> and other
        /// classes are excluded so a distinct static slot is not mixed into this class's map.
        /// Dynamic names (<c>self::$$var</c>) are ignored.
        /// </summary>
        private static bool TryGetEnclosingStaticPropertyKey(
            IExpression? expression,
            CheckerState state,
            out string? propertyKey)
        {
            propertyKey = null;
            if (expression is not PhpDereferenceableAst
                {
                    Base: PhpNameAst receiver,
                    Suffix: PhpStaticMemberAccessAst staticAccess,
                }
                || !IsEnclosingClassStaticReceiver(receiver, state))
            {
                return false;
            }

            // `self::$$var` nests another variable; GetVariableName would mis-key it as `$var`.
            if (staticAccess.Member is PhpVariableAst { VariableExpression: PhpVariableAst })
            {
                return false;
            }

            var memberName = staticAccess.Member switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpVariableAst variable => CheckerHelpers.GetVariableName(variable),
                _ => staticAccess.Member?.Identifier,
            };

            if (memberName is null || memberName.StartsWith('{'))
            {
                return false;
            }

            propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            return true;
        }

        private static bool IsEnclosingClassStaticReceiver(PhpNameAst receiver, CheckerState state)
        {
            if (state.EnclosingObject is not { } enclosing)
            {
                return false;
            }

            var raw = receiver.ValueString ?? receiver.Identifier;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            var simple = raw.TrimStart('\\');
            if (string.Equals(simple, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(simple, "static", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (receiver.BoundSymbol is ObjectDeclarationSymbol bound
                && ReferenceEquals(bound, enclosing))
            {
                return true;
            }

            if (string.Equals(simple, enclosing.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fqn = (enclosing.FullyQualifiedName ?? enclosing.Name).TrimStart('\\');
            return string.Equals(simple, fqn, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves the current effective type of a tracked <c>$this->prop</c> /
        /// <c>self::$prop</c>: any control-flow
        /// <see cref="PropertyInitializationState.NarrowedType"/>, else the declared property type.
        /// Looks the property up via <see cref="SymbolTree.ResolveMember"/> (not
        /// <c>EnclosingObject.Members</c> directly) so properties declared on a base class are
        /// found too — <c>Members</c> only holds symbols declared directly on that class.
        /// </summary>
        private static bool TryGetPropertyEffectiveType(
            CheckerState state,
            string propertyKey,
            INarrowingResolution context,
            SymbolTree symbolTree,
            out ICheckedType effective)
        {
            if (state.LookupPropertyInit(propertyKey) is null)
            {
                effective = CheckedTypes.Unresolved;
                return false;
            }

            if (state.LookupPropertyInit(propertyKey) is { NarrowedType: { } narrowed })
            {
                effective = narrowed;
                return true;
            }

            if (state.EnclosingObject is { } enclosingObject
                && symbolTree.ResolveMember(propertyKey, enclosingObject, new DiagnosticBag())
                    is ObjectPropertySymbol { DeclaredType: { } declaredAst })
            {
                effective = context.ResolveTypeAnnotation(declaredAst, state);
                return true;
            }

            effective = CheckedTypes.Unresolved;
            return false;
        }

        private static bool TryApplyVariableExistsNarrowing(
            IExpression condition,
            CheckerState branchState,
            GlobalScope globalScope,
            bool positive)
        {
            if (!positive)
            {
                return false;
            }

            return condition switch
            {
                TyhpVariableExistsAst { Expression: { } expr } =>
                    TryNarrowVariableNameHolder(expr, branchState, globalScope),
                PhpDereferenceableAst { Base: PhpNameAst { ValueString: "variable_exists" }, Suffix: PhpCallAst call } =>
                    ApplyVariableNameGuardNarrowing(call, branchState, globalScope),
                _ => false,
            };
        }

        private static bool ApplyVariableNameGuardNarrowing(
            PhpCallAst call,
            CheckerState branchState,
            GlobalScope globalScope)
        {
            var args = GetCallArguments(call);
            if (args.Count == 0 || args[0] is not PhpVariableAst argVar)
            {
                return false;
            }

            return TryNarrowVariableNameHolder(argVar, branchState, globalScope);
        }

        private static bool TryNarrowVariableNameHolder(
            IExpression expression,
            CheckerState branchState,
            GlobalScope globalScope)
        {
            PhpVariableAst? holder = expression switch
            {
                PhpVariableAst { VariableExpression: PhpVariableAst inner } => inner,
                PhpVariableAst direct => direct,
                _ => null,
            };

            if (holder is null)
            {
                return false;
            }

            var name = CheckerHelpers.GetVariableName(holder);
            if (name is null)
            {
                return false;
            }

            var narrowed = BuildVarNameType(holder, branchState, globalScope);
            branchState.NarrowVariable(name, narrowed);
            // isset / variable_exists positive arm: the variable is defined on this path.
            if (branchState.LookupVariable(name) is { } varState)
            {
                varState.IsPossiblyUndefined = false;
                varState.IsDefinitelyAssigned = true;
            }

            return true;
        }

        private static ICheckedType BuildVarNameType(
            PhpVariableAst holder,
            CheckerState branchState,
            GlobalScope globalScope)
        {
            var holderName = CheckerHelpers.GetVariableName(holder);
            if (holderName is null
                || branchState.LookupVariable(holderName) is not { } holderState
                || !SymbolNameTypeHelper.TryGetStringLiteral(holderState.EffectiveType, out var literal))
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.VarName, globalScope);
            }

            var targetName = literal.StartsWith('$') ? literal : "$" + literal;
            if (branchState.LookupVariable(targetName.TrimStart('$')) is { DeclaredType: { } declaredType })
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.TypedVarName, globalScope, [declaredType]);
            }

            return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.VarName, globalScope);
        }

        internal static bool IsLogicalAnd(string op) =>
            string.Equals(op, "&&", StringComparison.Ordinal)
            || string.Equals(op, "and", StringComparison.OrdinalIgnoreCase);

        internal static bool IsLogicalOr(string op) =>
            string.Equals(op, "||", StringComparison.Ordinal)
            || string.Equals(op, "or", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// PHP short-circuits <c>&&</c>/<c>and</c> and <c>||</c>/<c>or</c>. When checking the
        /// right operand, the left is already proven (<c>&&</c>) or disproven (<c>||</c>), so
        /// later operands should see that polarity of <see cref="ApplyConditionNarrowing"/>.
        /// </summary>
        internal static bool IsShortCircuitLogical(string op) =>
            IsLogicalAnd(op) || IsLogicalOr(op);

        /// <summary>
        /// Builds a structural key for <c>$var->prop</c> member-access narrowing. Only static
        /// member names are supported; dynamic names and <c>$this->prop</c> (tracked via
        /// <see cref="CheckerState.PropertyInit"/>) return false.
        /// </summary>
        internal static bool TryGetMemberAccessKey(IExpression? expression, out string? memberKey)
        {
            memberKey = null;
            if (expression is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst variable,
                    Suffix: PhpInstanceMemberAccessAst memberAccess,
                })
            {
                return false;
            }

            return TryGetMemberAccessKey(variable, memberAccess, out memberKey);
        }

        /// <summary>
        /// Same as <see cref="TryGetMemberAccessKey(IExpression?, out string?)"/> but for a
        /// dereferenceable base + instance-member suffix pair (used by type inference).
        /// </summary>
        internal static bool TryGetMemberAccessKey(
            IDereferenceableBase? baseNode,
            PhpInstanceMemberAccessAst memberAccess,
            out string? memberKey)
        {
            memberKey = null;
            if (baseNode is not PhpVariableAst variable
                || CheckerHelpers.IsThisVariable(variable)
                || memberAccess.MemberName is not (PhpNameAst or TokenValueAst))
            {
                return false;
            }

            var memberName = memberAccess.MemberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => null,
            };

            if (memberName is null || memberName.StartsWith('{'))
            {
                return false;
            }

            var varName = CheckerHelpers.GetVariableName(variable);
            if (varName is null)
            {
                return false;
            }

            var prop = memberName.StartsWith('$') ? memberName[1..] : memberName;
            memberKey = "$" + varName + "->" + prop;
            return true;
        }

        /// <summary>
        /// Maps a guard subject onto the call-site argument(s). A bare <c>$param</c> becomes
        /// that argument. <c>$array[$key]</c> substitutes both parameters (or keeps a
        /// signature-literal index) and rebuilds an index-read expression so later
        /// <see cref="TryGetIndexAccessKey"/> uses the call-site names.
        /// </summary>
        private static bool TryResolveCallSiteGuardSubject(
            TyhpReturnTypeGuardAst guard,
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<IExpression> args,
            Base2Ast contextNode,
            out IExpression? subject)
        {
            subject = null;

            if (guard.GuardSubject is PhpDereferenceableAst
                {
                    Base: PhpVariableAst arrayParam,
                    Suffix: PhpArrayAccessAst { IndexExpression: { } indexExpr }
                })
            {
                var arrayArg = FindArgumentByParameterName(
                    parameters, args, CheckerHelpers.GetVariableName(arrayParam));
                if (AsSimpleVariable(arrayArg) is not { } arrayArgVar)
                {
                    return false;
                }

                IExpression? callIndex = indexExpr is PhpVariableAst indexParam
                    ? FindArgumentByParameterName(
                        parameters, args, CheckerHelpers.GetVariableName(indexParam))
                    : indexExpr;
                if (callIndex is null)
                {
                    return false;
                }

                subject = PhpDereferenceableAst.CreateFromContext(
                    arrayArgVar,
                    PhpArrayAccessAst.CreateFromContext(callIndex, contextNode),
                    contextNode);
                return true;
            }

            var guardVarName = guard.GuardSubject is PhpVariableAst guardVar
                ? CheckerHelpers.GetVariableName(guardVar)
                : guard.GuardVariable?.ValueString?.TrimStart('$');
            var guardedIndex = guardVarName is null
                ? 0
                : IndexOfParameter(parameters, guardVarName);
            if (guardedIndex < 0)
            {
                guardedIndex = 0;
            }

            if (args.Count <= guardedIndex)
            {
                return false;
            }

            subject = args[guardedIndex];
            return true;
        }

        private static IExpression? FindArgumentByParameterName(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<IExpression> args,
            string? paramName)
        {
            if (paramName is null)
            {
                return null;
            }

            var index = IndexOfParameter(parameters, paramName);
            if (index < 0 || index >= args.Count)
            {
                return null;
            }

            return args[index];
        }

        private static int IndexOfParameter(IReadOnlyList<ParameterInfo> parameters, string name)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                if (string.Equals(parameters[i].Name.TrimStart('$'), name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static PhpVariableAst? AsSimpleVariable(IExpression? expression) =>
            expression switch
            {
                PhpVariableAst variable => variable,
                PhpDereferenceableAst { Base: PhpVariableAst variable, Suffix: null } => variable,
                _ => null,
            };

        /// <summary>
        /// Builds a structural key for <c>$var[index]</c> index-access narrowing. Constant
        /// int/string indices (<c>$arr[0]</c>, <c>$arr['k']</c>) and simple variable indices
        /// (<c>$arr[$k]</c>) are supported; other dynamic indices return false.
        /// </summary>
        internal static bool TryGetIndexAccessKey(IExpression? expression, out string? indexKey)
        {
            indexKey = null;
            if (expression is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst variable,
                    Suffix: PhpArrayAccessAst { IndexExpression: { } index }
                })
            {
                return false;
            }

            return TryBuildIndexAccessKey(variable, index, out indexKey);
        }

        /// <summary>
        /// Same as <see cref="TryGetIndexAccessKey(IExpression?, out string?)"/> but for a
        /// dereferenceable base + array-access suffix pair (used by type inference).
        /// </summary>
        internal static bool TryGetIndexAccessKey(
            IDereferenceableBase? baseNode,
            PhpArrayAccessAst arrayAccess,
            out string? indexKey)
        {
            indexKey = null;
            if (baseNode is not PhpVariableAst variable || arrayAccess.IndexExpression is null)
            {
                return false;
            }

            return TryBuildIndexAccessKey(variable, arrayAccess.IndexExpression, out indexKey);
        }

        private static bool TryBuildIndexAccessKey(
            PhpVariableAst variable,
            IExpression index,
            out string? indexKey)
        {
            indexKey = null;
            var varName = CheckerHelpers.GetVariableName(variable);
            if (varName is null || !TryFormatIndex(index, out var indexLit))
            {
                return false;
            }

            indexKey = "$" + varName + "[" + indexLit + "]";
            return true;
        }

        private static bool TryFormatIndex(IExpression index, out string literal)
        {
            literal = string.Empty;
            switch (index)
            {
                case PhpScalarAst scalar
                    when (scalar.ScalarType is PhpScalarType.Integer
                            or PhpScalarType.OctalNumber
                            or PhpScalarType.HexNumber
                            or PhpScalarType.BinaryNumber)
                        && scalar.ValueInt64 is long intValue:
                    literal = intValue.ToString();
                    return true;
                case PhpScalarAst { ScalarType: PhpScalarType.String, ValueString: { } s }:
                    literal = "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
                    return true;
                case PhpEncapsListAst encaps
                    when PhpStringLiteralHelper.TryGetStaticLiteral(encaps, out var encapsValue):
                    literal = "'" + encapsValue.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
                    return true;
                case PhpVariableAst variable:
                {
                    var name = CheckerHelpers.GetVariableName(variable);
                    if (name is null)
                    {
                        return false;
                    }

                    literal = "$" + name;
                    return true;
                }
                case PhpMagicConstantAst magic
                    when string.Equals(magic.ValueString, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(magic.ValueString, "false", StringComparison.OrdinalIgnoreCase):
                    // Bool indices coerce to 0/1 in PHP; not useful for narrowing keys.
                    return false;
                default:
                    return false;
            }
        }

        private static bool IsLogicalNot(PhpUnaryOpAst unary)
        {
            var op = unary.Operator?.ValueString;
            return string.Equals(op, "!", StringComparison.Ordinal)
                || string.Equals(op, "not", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInstanceofOperator(PhpBinaryOpAst binary)
        {
            var opToken = binary.Operator?.ValueInt64;
            if (opToken == TyhpParser.T_INSTANCEOF || opToken == TyhpParser.T_TYHP_IS)
            {
                return true;
            }

            // Fall back to text comparison for `instanceof` and Tyhp `is`.
            var opText = binary.Operator?.ValueString;
            return string.Equals(opText, "instanceof", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is", StringComparison.OrdinalIgnoreCase);
        }

        private static List<IExpression> GetCallArguments(PhpCallAst call) =>
            call.Arguments?.GetAllNotNull()
                .Select(arg => arg.Expression)
                .OfType<IExpression>()
                .ToList() ?? [];
    }
}
