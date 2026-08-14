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
    /// Control-flow type narrowing (smart casts) for instanceof, null checks, and type guards.
    /// </summary>
    internal static class TypeNarrowingRule
    {
        private readonly record struct SymbolNameGuardSpec(
            UtilityBehavior Behavior,
            int NarrowedArgIndex,
            bool CaptureReceiverType);

        private static readonly Dictionary<string, string> BuiltInTypeGuards = new(StringComparer.OrdinalIgnoreCase)
        {
            ["is_string"] = "string",
            ["is_int"] = "int",
            ["is_float"] = "float",
            ["is_bool"] = "bool",
            ["is_array"] = "array",
            ["is_null"] = "null",
            ["is_object"] = "object",
            ["is_callable"] = "callable",
            ["is_numeric"] = "int|float|string",
        };

        private static readonly Dictionary<string, SymbolNameGuardSpec> SymbolNameGuards = new(StringComparer.OrdinalIgnoreCase)
        {
            ["function_exists"] = new(UtilityBehavior.FunctionName, 0, false),
            ["class_exists"] = new(UtilityBehavior.ClassName, 0, false),
            ["interface_exists"] = new(UtilityBehavior.InterfaceName, 0, false),
            ["trait_exists"] = new(UtilityBehavior.TraitName, 0, false),
            ["enum_exists"] = new(UtilityBehavior.EnumName, 0, false),
            ["property_exists"] = new(UtilityBehavior.PropertyName, 1, true),
            ["method_exists"] = new(UtilityBehavior.MethodName, 1, true),
            ["is_a"] = new(UtilityBehavior.CompatibleTypeName, 1, true),
            ["is_subclass_of"] = new(UtilityBehavior.CompatibleTypeName, 1, true),
        };

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

            // Compound boolean conditions narrow component-wise. In the positive (then) branch of
            // `a && b` both operands hold, so narrow each. By De Morgan, in the negative (else)
            // branch of `a || b` neither operand held, so narrow each negatively. The other two
            // combinations (`a || b` positive, `a && b` negative) cannot soundly narrow a single
            // variable because either operand alone may be responsible.
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

            if (TryApplyNullNarrowing(condition, branchState, context, symbolTree, positive))
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

            if (TryGetThisPropertyKey(binary.Left, out var propertyKey)
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

        private static bool TryApplyNullNarrowing(
            IExpression condition,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            bool positive)
        {
            if (condition is not PhpBinaryOpAst binary)
            {
                return false;
            }

            var op = binary.Operator?.ValueString;
            var leftIsNull = IsNullLiteral(binary.Left);
            var rightIsNull = IsNullLiteral(binary.Right);

            IExpression? subject = null;
            if (rightIsNull && binary.Left is not null)
            {
                subject = binary.Left;
            }
            else if (leftIsNull && binary.Right is not null)
            {
                subject = binary.Right;
            }

            if (subject is null)
            {
                return false;
            }

            var isStrictNotNull = string.Equals(op, "!==", StringComparison.Ordinal);
            var isStrictNull = string.Equals(op, "===", StringComparison.Ordinal);
            if (!isStrictNotNull && !isStrictNull)
            {
                return false;
            }

            var expectNonNull = (isStrictNotNull && positive) || (isStrictNull && !positive);

            if (subject is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (name is null || branchState.LookupVariable(name) is not { } varState)
                {
                    return false;
                }

                if (expectNonNull)
                {
                    // Control-flow merges (try/catch, loops) often leave EffectiveType as a union that
                    // still contains `?T` / `null` members (MergeVariable clears NarrowedType and unions
                    // EffectiveTypes). Unwrap those so `!== null` yields a throwable / non-null payload.
                    var narrowed = RemoveNullability(varState.EffectiveType);
                    branchState.NarrowVariable(name, narrowed);
                    varState.IsPossiblyNull = false;
                }
                else
                {
                    branchState.NarrowVariable(name, CheckedTypes.Null);
                    varState.IsPossiblyNull = true;
                }

                return true;
            }

            if (TryGetThisPropertyKey(subject, out var propertyKey)
                && TryGetPropertyEffectiveType(branchState, propertyKey!, context, symbolTree, out var propEffective))
            {
                if (expectNonNull)
                {
                    branchState.NarrowProperty(propertyKey!, RemoveNullability(propEffective));
                }
                else
                {
                    branchState.NarrowProperty(propertyKey!, CheckedTypes.Null);
                }

                return true;
            }

            if (TryGetMemberAccessKey(subject, out var memberKey))
            {
                var prior = branchState.LookupMemberAccess(memberKey!)
                    ?? context.ResolveExpressionType(subject, branchState);
                if (expectNonNull)
                {
                    branchState.NarrowMemberAccess(memberKey!, RemoveNullability(prior));
                }
                else
                {
                    branchState.NarrowMemberAccess(memberKey!, CheckedTypes.Null);
                }

                return true;
            }

            return false;
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
            if (!TryGetGuardFunctionCall(condition, out var call, out var fnName))
            {
                return false;
            }

            // Free-function built-ins / symbol-name guards only apply when the callee is a bare name.
            // Static/instance method calls (`Type::isType<T>($x)`) fall through to user-defined guards.
            if (fnName.Length > 0)
            {
                if (BuiltInTypeGuards.TryGetValue(fnName, out var targetTypeName))
                {
                    return ApplyBuiltInGuardNarrowing(
                        call, branchState, context, symbolTree, globalScope, targetTypeName, positive);
                }

                // Prefer tyhpdef / user `$param is Type` return guards on both polarities so ExtCore
                // signatures (e.g. class_exists<T>: $class is __ClassName<T>) control narrowing —
                // including call-site generics — instead of the hardcoded SymbolNameGuards map.
                if (TryApplyUserDefinedGuardNarrowing(
                        condition, call, branchState, context, symbolTree, globalScope, positive))
                {
                    return true;
                }

                // Fallback for stubs that still return plain bool (property_exists / method_exists /
                // is_a / is_subclass_of capture receiver type as a brand type arg).
                if (positive)
                {
                    if (SymbolNameGuards.TryGetValue(fnName, out var symbolGuard))
                    {
                        return ApplySymbolNameGuardNarrowing(
                            call, symbolGuard, branchState, context, globalScope);
                    }

                    if (string.Equals(fnName, "variable_exists", StringComparison.OrdinalIgnoreCase))
                    {
                        return ApplyVariableNameGuardNarrowing(call, branchState, globalScope);
                    }
                }

                return false;
            }

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

            var guardVarName = guard.GuardVariable?.ValueString?.TrimStart('$');
            var guardedIndex = guardVarName is null
                ? 0
                : parameters.FindIndex(p =>
                    string.Equals(p.Name.TrimStart('$'), guardVarName, StringComparison.Ordinal));
            if (guardedIndex < 0)
            {
                guardedIndex = 0;
            }

            if (args.Count <= guardedIndex)
            {
                return false;
            }

            var subject = args[guardedIndex];
            var guardType = ResolveUserDefinedGuardTargetType(
                guard, condition, call, genericParameters, branchState, context, symbolTree, globalScope);

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
                return true;
            }

            if (TryGetThisPropertyKey(subject, out var propertyKey)
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
                        is not { } func
                    || func.ReturnType is not TyhpReturnTypeGuardAst freeGuard
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
        /// Resolves the guard's target type, substituting call-site generic arguments
        /// (<c>isType&lt;TValue&gt;($x)</c> / <c>Type::isType&lt;TValue&gt;($x)</c>) so method/function
        /// type parameters map to caller types. Omitted trailing type arguments use each
        /// parameter's default (<c>T extends object = object</c> → <c>object</c>).
        /// </summary>
        private static ICheckedType ResolveUserDefinedGuardTargetType(
            TyhpReturnTypeGuardAst guard,
            IExpression condition,
            PhpCallAst call,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
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

        private static bool ApplySymbolNameGuardNarrowing(
            PhpCallAst call,
            SymbolNameGuardSpec spec,
            CheckerState branchState,
            INarrowingResolution context,
            GlobalScope globalScope)
        {
            var args = GetCallArguments(call);
            if (args.Count <= spec.NarrowedArgIndex || args[spec.NarrowedArgIndex] is not PhpVariableAst argVar)
            {
                return false;
            }

            var argName = CheckerHelpers.GetVariableName(argVar);
            if (argName is null)
            {
                return false;
            }

            IReadOnlyList<ICheckedType>? typeArgs = null;
            if (spec.CaptureReceiverType && args.Count > 0)
            {
                var receiverType = context.ResolveExpressionType(args[0], branchState);
                typeArgs = [receiverType];
            }

            var narrowed = SymbolNameTypeHelper.MakeSymbolNameType(spec.Behavior, globalScope, typeArgs);
            branchState.NarrowVariable(argName, narrowed);
            return true;
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
            if (!TryGetThisPropertyKey(expression, out var propertyKey)
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
        /// Resolves the current effective type of a tracked <c>$this->prop</c>: any control-flow
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

        private static bool ApplyBuiltInGuardNarrowing(
            PhpCallAst call,
            CheckerState branchState,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            string targetTypeName,
            bool positive)
        {
            var args = GetCallArguments(call);
            if (args.Count == 0)
            {
                return false;
            }

            var subject = args[0];
            if (subject is PhpVariableAst argVar)
            {
                var argName = CheckerHelpers.GetVariableName(argVar);
                if (argName is null || branchState.LookupVariable(argName) is not { } varState)
                {
                    return false;
                }

                if (positive)
                {
                    var guardType = ResolveBuiltInGuardType(targetTypeName);
                    var narrowed = TypeComparer.NarrowType(
                        varState.EffectiveType, guardType, symbolTree, globalScope);
                    branchState.NarrowVariable(argName, narrowed);
                    varState.IsPossiblyNull = narrowed.IsNullable
                        || string.Equals(targetTypeName, "null", StringComparison.OrdinalIgnoreCase);
                    return true;
                }

                var excludeType = ResolveBuiltInGuardType(targetTypeName);
                ICheckedType negative;
                if (string.Equals(targetTypeName, "null", StringComparison.OrdinalIgnoreCase)
                    && varState.EffectiveType is NullableCheckedType nullable)
                {
                    negative = nullable.InnerType;
                }
                else
                {
                    negative = TypeComparer.NarrowTypeNegative(
                        varState.EffectiveType, excludeType, symbolTree, globalScope);
                }

                if (TypeComparer.AreTypesEqual(negative, varState.EffectiveType))
                {
                    return false;
                }

                branchState.NarrowVariable(argName, negative);
                if (string.Equals(targetTypeName, "null", StringComparison.OrdinalIgnoreCase))
                {
                    varState.IsPossiblyNull = false;
                }

                return true;
            }

            if (TryGetThisPropertyKey(subject, out var propertyKey)
                && TryGetPropertyEffectiveType(branchState, propertyKey!, context, symbolTree, out var propEffective))
            {
                if (positive)
                {
                    var guardType = ResolveBuiltInGuardType(targetTypeName);
                    var narrowed = TypeComparer.NarrowType(
                        propEffective, guardType, symbolTree, globalScope);
                    branchState.NarrowProperty(propertyKey!, narrowed);
                    return true;
                }

                var excludeType = ResolveBuiltInGuardType(targetTypeName);
                ICheckedType negative;
                if (string.Equals(targetTypeName, "null", StringComparison.OrdinalIgnoreCase)
                    && propEffective is NullableCheckedType nullable)
                {
                    negative = nullable.InnerType;
                }
                else
                {
                    negative = TypeComparer.NarrowTypeNegative(
                        propEffective, excludeType, symbolTree, globalScope);
                }

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
                    var guardType = ResolveBuiltInGuardType(targetTypeName);
                    var narrowed = TypeComparer.NarrowType(
                        prior, guardType, symbolTree, globalScope);
                    branchState.NarrowMemberAccess(memberKey!, narrowed);
                    return true;
                }

                var excludeType = ResolveBuiltInGuardType(targetTypeName);
                ICheckedType negative;
                if (string.Equals(targetTypeName, "null", StringComparison.OrdinalIgnoreCase)
                    && prior is NullableCheckedType nullable)
                {
                    negative = nullable.InnerType;
                }
                else
                {
                    negative = TypeComparer.NarrowTypeNegative(
                        prior, excludeType, symbolTree, globalScope);
                }

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
                    var guardType = ResolveBuiltInGuardType(targetTypeName);
                    var narrowed = TypeComparer.NarrowType(
                        prior, guardType, symbolTree, globalScope);
                    branchState.NarrowIndexAccess(indexKey!, narrowed);
                    return true;
                }

                var excludeType = ResolveBuiltInGuardType(targetTypeName);
                var negative = TypeComparer.NarrowTypeNegative(
                    prior, excludeType, symbolTree, globalScope);
                if (TypeComparer.AreTypesEqual(negative, prior))
                {
                    return false;
                }

                branchState.NarrowIndexAccess(indexKey!, negative);
                return true;
            }

            return false;
        }

        private static ICheckedType ResolveBuiltInGuardType(string typeName)
        {
            if (typeName.Contains('|', StringComparison.Ordinal))
            {
                var parts = typeName.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return CheckedTypes.UnionTypes(parts.Select(ResolveBuiltInGuardType).ToList());
            }

            // Keep true/false/null as precise literal types (same shape as ResolveNamedType).
            return typeName.ToLowerInvariant() switch
            {
                "null" => CheckedTypes.Null,
                "true" => new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true"))),
                "false" => new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false"))),
                _ => CheckedTypes.FromSymbol(new BuiltInTypeSymbol(typeName)),
            };
        }

        internal static bool IsLogicalAnd(string op) =>
            string.Equals(op, "&&", StringComparison.Ordinal)
            || string.Equals(op, "and", StringComparison.OrdinalIgnoreCase);

        internal static bool IsLogicalOr(string op) =>
            string.Equals(op, "||", StringComparison.Ordinal)
            || string.Equals(op, "or", StringComparison.OrdinalIgnoreCase);

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
        /// Builds a structural key for <c>$var[literal]</c> index-access narrowing. Only constant
        /// int/string indices are supported; dynamic indices return false.
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

            var varName = CheckerHelpers.GetVariableName(variable);
            if (varName is null || !TryFormatConstantIndex(index, out var indexLit))
            {
                return false;
            }

            indexKey = "$" + varName + "[" + indexLit + "]";
            return true;
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
            if (baseNode is not PhpVariableAst variable
                || arrayAccess.IndexExpression is null
                || !TryFormatConstantIndex(arrayAccess.IndexExpression, out var indexLit))
            {
                return false;
            }

            var varName = CheckerHelpers.GetVariableName(variable);
            if (varName is null)
            {
                return false;
            }

            indexKey = "$" + varName + "[" + indexLit + "]";
            return true;
        }

        private static bool TryFormatConstantIndex(IExpression index, out string literal)
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

            // Fall back to text comparison for the `instanceof` keyword and its Tyhp aliases.
            var opText = binary.Operator?.ValueString;
            return string.Equals(opText, "instanceof", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_an", StringComparison.OrdinalIgnoreCase);
        }

        private static List<IExpression> GetCallArguments(PhpCallAst call) =>
            call.Arguments?.GetAllNotNull()
                .Select(arg => arg.Expression)
                .OfType<IExpression>()
                .ToList() ?? [];
    }
}
