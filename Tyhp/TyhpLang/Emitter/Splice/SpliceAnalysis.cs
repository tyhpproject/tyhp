using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter.Splice
{
    /// <summary>
    /// Write / use / order / accessibility / referenceability analysis for the splice engine.
    /// </summary>
    internal static class SpliceAnalysis
    {
        public static HashSet<string> WrittenParameterNames(
            IExpression body,
            IReadOnlyList<SpliceParameter> parameters,
            SpliceEngineContext context)
        {
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectWrites(body, parameters, context, written, shadowed: []);
            return written;
        }

        public static bool WritesThis(IExpression body, SpliceEngineContext context)
        {
            var dummyParams = Array.Empty<SpliceParameter>();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectWrites(body, dummyParams, context, written, shadowed: [], trackThis: true);
            return written.Contains("this");
        }

        /// <summary>
        /// The splice parameter literally named <c>$this</c>, if any. A by-reference
        /// <c>extends T &amp;$this</c> receiver is a written parameter like any other
        /// <c>&amp;</c> parameter — not an automatic TYHP4174.
        /// </summary>
        public static SpliceParameter? ThisByName(IReadOnlyList<SpliceParameter> parameters)
        {
            foreach (var param in parameters)
            {
                if (SpliceAst.IsThisName(param.Name))
                {
                    return param;
                }
            }

            return null;
        }

        private static void CollectWrites(
            IBase2Ast? node,
            IReadOnlyList<SpliceParameter> parameters,
            SpliceEngineContext context,
            HashSet<string> written,
            HashSet<string> shadowed,
            bool trackThis = true)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpInlineFunctionAst or PhpFunctionDeclAst)
            {
                var nestedShadow = new HashSet<string>(shadowed, StringComparer.OrdinalIgnoreCase);
                AddNestedParameterNames(node, nestedShadow);
                foreach (var child in node.AstChildren)
                {
                    CollectWrites(child, parameters, context, written, nestedShadow, trackThis);
                }

                return;
            }

            switch (node)
            {
                case PhpBinaryOpAst binary when SpliceAst.IsAssignmentOperator(binary.Operator):
                    CollectWriteTarget(binary.Left, parameters, written, shadowed, trackThis);
                    if (SpliceAst.IsCompoundAssignmentOperator(binary.Operator))
                    {
                        CollectWrites(binary.Left, parameters, context, written, shadowed, trackThis);
                    }

                    CollectWrites(binary.Right, parameters, context, written, shadowed, trackThis);
                    return;

                case PhpUnaryOpAst unary when SpliceAst.IsIncrementDecrement(unary.Operator):
                    CollectWriteTarget(unary.Operand, parameters, written, shadowed, trackThis);
                    CollectWrites(unary.Operand, parameters, context, written, shadowed, trackThis);
                    return;

                case PhpDereferenceableAst deref when deref.Suffix is PhpCallAst call:
                    CollectCallArgWrites(deref, call, parameters, context, written, shadowed, trackThis);
                    CollectWrites(deref.Base, parameters, context, written, shadowed, trackThis);
                    if (call.Arguments is not null)
                    {
                        foreach (var arg in call.Arguments.GetAllNotNull())
                        {
                            CollectWrites(arg, parameters, context, written, shadowed, trackThis);
                        }
                    }

                    return;
            }

            foreach (var child in node.AstChildren)
            {
                CollectWrites(child, parameters, context, written, shadowed, trackThis);
            }
        }

        private static void CollectWriteTarget(
            IExpression? target,
            IReadOnlyList<SpliceParameter> parameters,
            HashSet<string> written,
            HashSet<string> shadowed,
            bool trackThis)
        {
            target = SpliceAst.UnwrapParens(target);
            if (target is not PhpVariableAst variable)
            {
                return;
            }

            var name = CheckerHelpers.GetVariableName(variable);
            if (string.IsNullOrEmpty(name) || shadowed.Contains(name))
            {
                return;
            }

            if (trackThis && SpliceAst.IsThisName(name))
            {
                written.Add("this");
            }

            if (parameters.Any(p => SpliceAst.NamesEqual(p.Name, name)))
            {
                written.Add(SpliceAst.NormalizeName(name));
            }
        }

        private static void CollectCallArgWrites(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            IReadOnlyList<SpliceParameter> parameters,
            SpliceEngineContext context,
            HashSet<string> written,
            HashSet<string> shadowed,
            bool trackThis)
        {
            // A zero-argument call can never write a `&`-ref parameter through an argument slot,
            // so there is nothing for this check to find. Skip straight past
            // `ResolveCallParameters` in that case: for a splice-member pre-pass (see
            // `InlineSpliceRule.CheckMemberDeclaration`), that resolution walks the call's
            // *receiver* chain (`ResolveCallReceiverType`) using a `CheckerState` with no
            // `$this`/parameter bindings — for a receiver chain rooted at `$this`
            // (`$this->doubled()->doubled()`), that would infer (and permanently cache on the
            // inner call's AST node) an `Unresolved` type, corrupting the real checker pass's
            // later resolution of the same node even though this call has no arguments to check
            // in the first place (Story 21.12 Workstream C review).
            if (call.Arguments is null || !call.Arguments.GetAllNotNull().Any())
            {
                return;
            }

            var calleeParams = ResolveCallParameters(deref, context);
            if (calleeParams is null)
            {
                return;
            }

            var positional = 0;
            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                ParameterInfo? param = null;
                if (arg.Name?.ValueString is { } named)
                {
                    param = calleeParams.FirstOrDefault(p =>
                        SpliceAst.NamesEqual(p.Name, named));
                }
                else if (positional < calleeParams.Count)
                {
                    param = calleeParams[positional];
                    positional++;
                }

                if (param is { IsByReference: true })
                {
                    CollectWriteTarget(arg.Expression, parameters, written, shadowed, trackThis);
                }
            }
        }

        public static IReadOnlyList<ParameterInfo>? ResolveCallParameters(
            PhpDereferenceableAst deref,
            SpliceEngineContext context)
        {
            if (context.ResolveCallParameters?.Invoke(deref) is { } injected)
            {
                return injected;
            }

            if (deref.BoundSymbol is { } bound && SpliceAst.ParametersOf(bound) is { } fromBound)
            {
                return fromBound;
            }

            if (deref.Base is PhpNameAst name)
            {
                if (name.BoundSymbol is { } nameBound && SpliceAst.ParametersOf(nameBound) is { } fromName)
                {
                    return fromName;
                }

                return context.ResolveFreeFunction(name);
            }

            if (deref.Base is PhpDereferenceableAst inner)
            {
                if (inner.BoundSymbol is { } innerBound && SpliceAst.ParametersOf(innerBound) is { } fromInner)
                {
                    return fromInner;
                }

                if (inner.Suffix is PhpInstanceMemberAccessAst or PhpStaticMemberAccessAst or PhpClassConstantAccessAst
                    && inner.Suffix.BoundSymbol is { } memberBound)
                {
                    return SpliceAst.ParametersOf(memberBound);
                }
            }

            return null;
        }

        /// <summary>
        /// Eager (non-lazy) uses of each parameter / <c>$this</c>, in first-seen order.
        /// A use past a short-circuit is recorded separately as lazy.
        /// </summary>
        public static void CollectUses(
            IExpression body,
            IReadOnlyList<SpliceParameter> parameters,
            out Dictionary<string, int> eagerCounts,
            out HashSet<string> lazyNames,
            out List<string> eagerOrder)
        {
            eagerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            lazyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            eagerOrder = [];
            WalkUses(body, parameters, eagerCounts, lazyNames, eagerOrder, shadowed: [], lazy: false);
        }

        private static void WalkUses(
            IBase2Ast? node,
            IReadOnlyList<SpliceParameter> parameters,
            Dictionary<string, int> eagerCounts,
            HashSet<string> lazyNames,
            List<string> eagerOrder,
            HashSet<string> shadowed,
            bool lazy)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpInlineFunctionAst or PhpFunctionDeclAst)
            {
                var nestedShadow = new HashSet<string>(shadowed, StringComparer.OrdinalIgnoreCase);
                AddNestedParameterNames(node, nestedShadow);
                foreach (var child in node.AstChildren)
                {
                    WalkUses(child, parameters, eagerCounts, lazyNames, eagerOrder, nestedShadow, lazy);
                }

                return;
            }

            if (node is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (!string.IsNullOrEmpty(name)
                    && !shadowed.Contains(name)
                    && (parameters.Any(p => SpliceAst.NamesEqual(p.Name, name)) || SpliceAst.IsThisName(name)))
                {
                    var key = SpliceAst.NormalizeName(name);
                    if (lazy)
                    {
                        lazyNames.Add(key);
                    }
                    else
                    {
                        if (!eagerCounts.ContainsKey(key))
                        {
                            eagerOrder.Add(key);
                        }

                        eagerCounts[key] = eagerCounts.GetValueOrDefault(key) + 1;
                    }
                }
            }

            switch (node)
            {
                case PhpBinaryOpAst binary when SpliceAst.IsShortCircuitOperator(binary.Operator):
                    WalkUses(binary.Left, parameters, eagerCounts, lazyNames, eagerOrder, shadowed, lazy);
                    WalkUses(binary.Right, parameters, eagerCounts, lazyNames, eagerOrder, shadowed, lazy: true);
                    return;

                case PhpTernaryOpAst ternary:
                    WalkUses(ternary.Condition, parameters, eagerCounts, lazyNames, eagerOrder, shadowed, lazy);
                    WalkUses(ternary.TrueExpr, parameters, eagerCounts, lazyNames, eagerOrder, shadowed, lazy: true);
                    WalkUses(ternary.FalseExpr, parameters, eagerCounts, lazyNames, eagerOrder, shadowed, lazy: true);
                    return;
            }

            foreach (var child in node.AstChildren)
            {
                WalkUses(child, parameters, eagerCounts, lazyNames, eagerOrder, shadowed, lazy);
            }
        }

        public static SpliceDeclineReason EvaluateFidelity(
            IReadOnlyList<SpliceParameter> parameters,
            IExpression? receiver,
            IReadOnlyList<IExpression?> arguments,
            Dictionary<string, int> eagerCounts,
            HashSet<string> lazyNames,
            List<string> eagerOrder,
            bool needsRefHoist,
            bool hasStatementSlot)
        {
            var expectedOrder = new List<string>();
            foreach (var param in parameters)
            {
                var key = SpliceAst.NormalizeName(param.Name);
                var arg = ArgumentFor(param, parameters, receiver, arguments)
                    ?? param.DefaultValue;
                if (arg is null && param.DefaultValue is null && !param.IsThis)
                {
                    continue;
                }

                var isSimple = SpliceAst.IsSimpleArgument(arg);
                if (!eagerCounts.ContainsKey(key))
                {
                    if (lazyNames.Contains(key) && !isSimple)
                    {
                        return SpliceDeclineReason.ShortCircuitLazyArgument;
                    }

                    if (!isSimple && arg is not null && param.DefaultValue is null)
                    {
                        return SpliceDeclineReason.UnusedParameter;
                    }

                    continue;
                }

                if (!isSimple)
                {
                    expectedOrder.Add(key);
                }
            }

            var usedActual = eagerOrder
                .Where(n => expectedOrder.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (usedActual.Count != expectedOrder.Count
                || !usedActual.SequenceEqual(expectedOrder, StringComparer.OrdinalIgnoreCase))
            {
                return SpliceDeclineReason.ArgumentOrderMismatch;
            }

            if (needsRefHoist && !hasStatementSlot)
            {
                return SpliceDeclineReason.RefHoistNeedsStatementSlot;
            }

            return SpliceDeclineReason.None;
        }

        public static IExpression? ArgumentFor(
            SpliceParameter param,
            IReadOnlyList<SpliceParameter> parameters,
            IExpression? receiver,
            IReadOnlyList<IExpression?> arguments)
        {
            if (param.IsThis)
            {
                return receiver;
            }

            var index = 0;
            foreach (var p in parameters)
            {
                if (p.IsThis)
                {
                    continue;
                }

                if (SpliceAst.NamesEqual(p.Name, param.Name))
                {
                    return index < arguments.Count ? arguments[index] : null;
                }

                index++;
            }

            return null;
        }

        public static bool NeedsRefBoundHoist(
            SpliceParameter param,
            IExpression? argument,
            int eagerCount,
            bool hookedByValue)
        {
            if (eagerCount <= 1 && !hookedByValue)
            {
                return false;
            }

            // A simple variable (or literal/constant) never needs a ref-bound hoist, even for a
            // `&` parameter: substituting it directly at every occurrence already behaves like a
            // real by-reference bind (see CallSiteSpliceEngine.TrySpliceCore's matching skipLocal).
            if (argument is not null && SpliceAst.IsSimpleArgument(argument))
            {
                return false;
            }

            return param.IsByReference && eagerCount > 1;
        }

        public readonly record struct AccessibilityHit(IBaseSymbol Member, IBase2Ast Node);

        public static IEnumerable<AccessibilityHit> ReferencedMembers(
            IExpression body,
            CheckerState? state = null,
            CheckerRuleContext? context = null,
            ObjectDeclarationSymbol? thisType = null,
            bool hasThisParameter = false,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes = null)
        {
            var hits = new List<AccessibilityHit>();
            CollectReferencedMembers(
                body, hits, shadowed: [], state, context, thisType, hasThisParameter, parameterTypes);
            return hits;
        }

        private static void CollectReferencedMembers(
            IBase2Ast? node,
            List<AccessibilityHit> hits,
            HashSet<string> shadowed,
            CheckerState? state,
            CheckerRuleContext? context,
            ObjectDeclarationSymbol? thisType,
            bool hasThisParameter,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpInlineFunctionAst or PhpFunctionDeclAst)
            {
                var nested = new HashSet<string>(shadowed, StringComparer.OrdinalIgnoreCase);
                AddNestedParameterNames(node, nested);
                foreach (var child in node.AstChildren)
                {
                    CollectReferencedMembers(
                        child, hits, nested, state, context, thisType, hasThisParameter, parameterTypes);
                }

                return;
            }

            if (node.BoundSymbol is ObjectMethodSymbol or ObjectPropertySymbol or ObjectConstantSymbol
                && node.BoundSymbol is IBaseSymbol member)
            {
                hits.Add(new AccessibilityHit(member, node));
            }
            else if (node is PhpDereferenceableAst { Suffix: PhpCallAst } call
                && call.Base is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst } methodInstance
                && ResolveMethodSymbol(methodInstance, state, context, thisType, hasThisParameter, parameterTypes)
                    is { } calledMethod)
            {
                hits.Add(new AccessibilityHit(calledMethod, node));
            }
            else if (node is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst } instance
                && ResolvePropertySymbol(instance, state, context, thisType, hasThisParameter, parameterTypes)
                    is { } property)
            {
                hits.Add(new AccessibilityHit(property, node));
            }

            foreach (var child in node.AstChildren)
            {
                CollectReferencedMembers(
                    child, hits, shadowed, state, context, thisType, hasThisParameter, parameterTypes);
            }
        }

        public static bool IsAtLeastAsAccessible(MemberModifier referenced, MemberModifier callee)
        {
            var refVis = EffectiveVisibility(referenced);
            var calleeVis = EffectiveVisibility(callee);
            return calleeVis switch
            {
                MemberModifier.Public => refVis == MemberModifier.Public,
                MemberModifier.Protected => refVis is MemberModifier.Public or MemberModifier.Protected,
                MemberModifier.Private => true,
                _ => refVis == MemberModifier.Public,
            };
        }

        public static MemberModifier EffectiveVisibility(MemberModifier modifiers)
        {
            if ((modifiers & MemberModifier.Private) != 0)
            {
                return MemberModifier.Private;
            }

            if ((modifiers & MemberModifier.Protected) != 0)
            {
                return MemberModifier.Protected;
            }

            return MemberModifier.Public;
        }

        public static bool IsReferenceableArgument(
            IExpression? argument,
            CheckerState? state = null,
            CheckerRuleContext? context = null)
        {
            argument = SpliceAst.UnwrapParens(argument);
            if (argument is null)
            {
                return false;
            }

            if (argument is PhpVariableAst variable)
            {
                return SpliceAst.IsSimpleVariable(variable);
            }

            if (argument is PhpDereferenceableAst { Suffix: PhpArrayAccessAst } arrayAccess)
            {
                return IsReferenceableArrayAccess(arrayAccess);
            }

            if (argument is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst } instance)
            {
                return IsReferenceableProperty(instance, state, context);
            }

            if (argument is PhpDereferenceableAst { Suffix: PhpStaticMemberAccessAst } staticAccess)
            {
                return IsReferenceableStaticProperty(staticAccess);
            }

            return false;
        }

        private static bool IsReferenceableArrayAccess(PhpDereferenceableAst access)
        {
            if (access.Base is IExpression inner && !IsReferenceableArgument(inner, state: null, context: null))
            {
                if (access.Base is PhpVariableAst)
                {
                    return true;
                }

                if (access.Base is PhpDereferenceableAst)
                {
                    return IsReferenceableArgument(access.Base as IExpression, state: null, context: null);
                }
            }

            if (TryGetOffsetGet(access.Base as IBase2Ast) is { } offsetGet)
            {
                return SpliceAst.MethodReturnsRef(offsetGet);
            }

            return access.Base is PhpVariableAst or PhpDereferenceableAst;
        }

        private static IBaseSymbol? TryGetOffsetGet(IBase2Ast? receiver)
        {
            var type = receiver?.BoundSymbol as ObjectDeclarationSymbol
                ?? (receiver as IExpression)?.BoundSymbol as ObjectDeclarationSymbol;
            if (type is null)
            {
                return null;
            }

            return type.Members.TryGetValue("offsetGet", out var member) ? member : null;
        }

        private static bool IsReferenceableProperty(
            PhpDereferenceableAst instance,
            CheckerState? state,
            CheckerRuleContext? context)
        {
            var suffix = (PhpInstanceMemberAccessAst)instance.Suffix!;
            var symbol = suffix.BoundSymbol
                ?? suffix.MemberName?.BoundSymbol
                ?? instance.BoundSymbol;

            if (symbol is not ObjectPropertySymbol)
            {
                symbol = ResolvePropertySymbol(instance, state, context, thisType: null) ?? symbol;
            }

            if (symbol is ObjectPropertySymbol prop)
            {
                if ((prop.Visibility & MemberModifier.Readonly) != 0)
                {
                    return false;
                }

                if (HasInaccessibleSet(prop, state))
                {
                    return false;
                }

                if (prop.HasAccessor)
                {
                    return prop.GetHookReturnsRef;
                }

                return true;
            }

            if (symbol is ObjectMethodSymbol method
                && string.Equals(method.Name, "__get", StringComparison.OrdinalIgnoreCase))
            {
                return SpliceAst.MethodReturnsRef(method);
            }

            if (TryGetMagicGet(instance.Base as IBase2Ast) is { } magicGet)
            {
                return SpliceAst.MethodReturnsRef(magicGet);
            }

            return symbol is ObjectPropertySymbol;
        }

        private static bool IsReferenceableStaticProperty(PhpDereferenceableAst access)
        {
            var suffix = (PhpStaticMemberAccessAst)access.Suffix!;
            var symbol = suffix.BoundSymbol ?? suffix.Member?.BoundSymbol ?? access.BoundSymbol;
            if (symbol is ObjectPropertySymbol prop)
            {
                if ((prop.Visibility & MemberModifier.Readonly) != 0)
                {
                    return false;
                }

                if (prop.HasAccessor)
                {
                    return prop.GetHookReturnsRef;
                }

                return true;
            }

            return false;
        }

        private static IBaseSymbol? TryGetMagicGet(IBase2Ast? receiver)
        {
            var type = receiver?.BoundSymbol as ObjectDeclarationSymbol;
            if (type is null)
            {
                return null;
            }

            return type.Members.TryGetValue("__get", out var member) ? member : null;
        }

        private static bool HasInaccessibleSet(ObjectPropertySymbol prop, CheckerState? state)
        {
            var decl = FindPropertyDeclaration(prop);
            IEnumerable<PhpModifier>? modifiers = decl switch
            {
                PhpPropertyDeclAst propertyDecl => propertyDecl.Modifiers?.Modifiers,
                PhpParameterAst param => param.Modifiers?.Modifiers,
                _ => null,
            };

            if (modifiers is null)
            {
                return false;
            }

            var list = modifiers.ToList();
            var hasPrivateSet = list.Contains(PhpModifier.PrivateSet);
            var hasProtectedSet = list.Contains(PhpModifier.ProtectedSet);
            if (!hasPrivateSet && !hasProtectedSet)
            {
                return false;
            }

            if (state is null)
            {
                return hasPrivateSet || hasProtectedSet;
            }

            var owner = prop.ContainingScope is ObjectDeclarationScope objScope
                ? objScope.DeclarationSymbol as ObjectDeclarationSymbol
                : null;
            if (hasPrivateSet)
            {
                return !ReferenceEquals(state.EnclosingObject, owner);
            }

            if (hasProtectedSet)
            {
                return state.EnclosingObject is null
                    || (owner is not null && !IsSameOrSubclass(state.EnclosingObject, owner));
            }

            return false;
        }

        private static IBase2Ast? FindPropertyDeclaration(ObjectPropertySymbol prop)
        {
            if (prop.DeclaringAstNode is PhpParameterAst or PhpPropertyDeclAst)
            {
                return prop.DeclaringAstNode;
            }

            var owner = (prop.ContainingScope as ObjectDeclarationScope)?.DeclarationSymbol as ObjectDeclarationSymbol;
            if (owner?.DeclaringAstNode is not PhpObjectTypeDeclAst { Body: { } body })
            {
                return prop.DeclaringAstNode;
            }

            foreach (var member in body.GetAllNotNull())
            {
                if (member is PhpPropertyDeclAst decl
                    && decl.Properties?.GetAllNotNull()
                        .Any(p => SpliceAst.NamesEqual(p.Identifier, prop.Name)) == true)
                {
                    return decl;
                }
            }

            return prop.DeclaringAstNode;
        }

        private static bool IsSameOrSubclass(ObjectDeclarationSymbol? derived, ObjectDeclarationSymbol owner)
        {
            var current = derived;
            while (current is not null)
            {
                if (ReferenceEquals(current, owner)
                    || string.Equals(
                        current.FullyQualifiedName,
                        owner.FullyQualifiedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.ExtendsType?.BoundSymbol as ObjectDeclarationSymbol;
            }

            return false;
        }

        public static bool IsByValueHookedRead(IExpression? argument)
        {
            argument = SpliceAst.UnwrapParens(argument);
            if (argument is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst suffix } instance)
            {
                if (argument is PhpDereferenceableAst { Suffix: PhpStaticMemberAccessAst staticSuffix } staticAccess)
                {
                    var staticSym = staticSuffix.BoundSymbol
                        ?? staticSuffix.Member?.BoundSymbol
                        ?? staticAccess.BoundSymbol;
                    return staticSym is ObjectPropertySymbol sp
                        && sp.HasAccessor
                        && !sp.GetHookReturnsRef;
                }

                return false;
            }

            var symbol = suffix.BoundSymbol ?? suffix.MemberName?.BoundSymbol ?? instance.BoundSymbol;
            if (symbol is not ObjectPropertySymbol)
            {
                symbol = ResolvePropertySymbol(instance, state: null, context: null, thisType: null) ?? symbol;
            }

            return symbol is ObjectPropertySymbol prop && prop.HasAccessor && !prop.GetHookReturnsRef;
        }

        public static bool IsByRefHookedRead(IExpression? argument)
        {
            argument = SpliceAst.UnwrapParens(argument);
            if (argument is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst suffix } instance)
            {
                return false;
            }

            var symbol = suffix.BoundSymbol ?? suffix.MemberName?.BoundSymbol ?? instance.BoundSymbol;
            if (symbol is not ObjectPropertySymbol)
            {
                symbol = ResolvePropertySymbol(instance, state: null, context: null, thisType: null) ?? symbol;
            }

            return symbol is ObjectPropertySymbol hooked && hooked.GetHookReturnsRef;
        }

        /// <summary>
        /// Resolves the callee of <c>$receiver-&gt;method(...)</c> for accessibility analysis
        /// (4179) when the node was never visited by the main checker walk — e.g. a tyhpdef
        /// class-body thin mapping, which is bound into the global scope but excluded from
        /// <c>ParsedFiles</c> and so never gets <see cref="IBase2Ast.BoundSymbol"/> populated by
        /// <c>TypeInferrer</c>. Mirrors <see cref="ResolvePropertySymbol"/> but for methods.
        /// </summary>
        private static ObjectMethodSymbol? ResolveMethodSymbol(
            PhpDereferenceableAst instance,
            CheckerState? state,
            CheckerRuleContext? context,
            ObjectDeclarationSymbol? thisType,
            bool hasThisParameter = false,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes = null)
        {
            if (instance.Suffix is not PhpInstanceMemberAccessAst suffix || context is null)
            {
                return null;
            }

            var name = suffix.MemberName switch
            {
                PhpNameAst n => n.ValueString ?? n.Identifier,
                TokenValueAst t => t.ValueString,
                IExpression e => e.Identifier,
                _ => suffix.MemberName?.Identifier,
            };
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var owner = TypeOfReceiver(
                instance.Base as IExpression, state, context, thisType, hasThisParameter, parameterTypes);
            if (owner is null)
            {
                return null;
            }
            var member = context.SymbolTree.ResolveMember(name, owner, new DiagnosticBag());
            return member as ObjectMethodSymbol;
        }

        private static ObjectPropertySymbol? ResolvePropertySymbol(
            PhpDereferenceableAst instance,
            CheckerState? state,
            CheckerRuleContext? context,
            ObjectDeclarationSymbol? thisType,
            bool hasThisParameter = false,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes = null)
        {
            if (instance.Suffix is not PhpInstanceMemberAccessAst suffix)
            {
                return null;
            }

            var name = suffix.MemberName switch
            {
                PhpNameAst n => n.ValueString ?? n.Identifier,
                TokenValueAst t => t.ValueString,
                IExpression e => e.Identifier,
                _ => suffix.MemberName?.Identifier,
            };
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var owner = TypeOfReceiver(
                instance.Base as IExpression, state, context, thisType, hasThisParameter, parameterTypes);
            return owner is null ? null : WithKeywordHelper.TryGetProperty(owner, name);
        }

        /// <summary>
        /// Resolves the object type of a receiver expression for accessibility analysis (4179).
        /// Checked before falling back to full expression-type inference because splice-member
        /// checks run in a synthetic <see cref="CheckerState"/> (see
        /// <c>TyhpChecker.CheckThinMappingSymbolMembers</c>) that has no live scope/locals for a
        /// tyhpdef class-body thin mapping — <see cref="CheckerRuleContext.ResolveExpressionType"/>
        /// cannot resolve a plain parameter reference there, so a parameter's own declared type
        /// (already known from the member's signature) must be consulted directly.
        ///
        /// A receiver that the signature declares as <c>$this</c> or a named parameter, but whose
        /// declared type is not an object (e.g. an extension method's scalar
        /// <c>extends int $this</c>), must return <see langword="null"/> here *without* falling
        /// through to <see cref="CheckerRuleContext.ResolveExpressionType"/>: that fallback runs in
        /// the synthetic state above, which has no binding for the parameter, so it would resolve
        /// (and permanently cache on the AST node) an <c>Unresolved</c> type — corrupting the real
        /// checker pass's later, correctly-scoped resolution of the same node (Story 21.12
        /// Workstream C review).
        /// </summary>
        private static ObjectDeclarationSymbol? TypeOfReceiver(
            IExpression? receiver,
            CheckerState? state,
            CheckerRuleContext? context,
            ObjectDeclarationSymbol? thisType,
            bool hasThisParameter,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes)
        {
            receiver = SpliceAst.UnwrapParens(receiver);
            if (receiver is null)
            {
                return null;
            }

            if (receiver is PhpVariableAst thisVar
                && SpliceAst.IsThisName(CheckerHelpers.GetVariableName(thisVar))
                && (hasThisParameter || thisType is not null))
            {
                return thisType;
            }

            if (receiver is PhpVariableAst paramVar
                && parameterTypes is not null
                && parameterTypes.TryGetValue(
                    SpliceAst.NormalizeName(CheckerHelpers.GetVariableName(paramVar)) ?? "", out var paramType))
            {
                return paramType;
            }

            if (receiver.BoundSymbol is ObjectDeclarationSymbol obj)
            {
                return obj;
            }

            if (receiver.BoundSymbol is VariableSymbol { DeclaredType: { } declared })
            {
                if (declared.BoundSymbol is ObjectDeclarationSymbol fromDecl)
                {
                    return fromDecl;
                }

                if (declared is PhpNamedTypeAst named
                    && named.Name is PhpNameAst n
                    && n.BoundSymbol is ObjectDeclarationSymbol fromName)
                {
                    return fromName;
                }
            }

            // A receiver that is itself a nested member/method chain rooted at `$this` or a
            // parameter (e.g. `$this->doubled()->x` — a real Story 21.12 Workstream C regression)
            // must resolve structurally through the same lookups this function feeds, never
            // through `ResolveExpressionType`: that fallback would infer (and permanently cache
            // on this exact call/member AST node) the chain's type using the synthetic state
            // above, which has no binding for `$this`/parameters, so any dependency on them
            // resolves to `Unresolved` — corrupting the real checker pass's later, correctly-
            // scoped resolution of the same node. Resolving the callee's *declared* return/property
            // type via `ResolveTypeAnnotation` is safe here because it is a static declaration
            // lookup, not a live-scope expression inference (mirrors `ResolveThisParameterType`
            // resolving the receiver's own declared type through the same synthetic state).
            if (receiver is PhpDereferenceableAst { Suffix: PhpCallAst } nestedCall
                && nestedCall.Base is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst } nestedMethodInstance)
            {
                var method = ResolveMethodSymbol(
                    nestedMethodInstance, state, context, thisType, hasThisParameter, parameterTypes);
                return method?.ReturnType is null || context is null || state is null
                    ? null
                    : CheckerHelpers.TryGetObjectDeclaration(
                        context.ResolveTypeAnnotation(method.ReturnType, state));
            }

            if (receiver is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst } nestedProperty)
            {
                var property = ResolvePropertySymbol(
                    nestedProperty, state, context, thisType, hasThisParameter, parameterTypes);
                return property?.DeclaredType is null || context is null || state is null
                    ? null
                    : CheckerHelpers.TryGetObjectDeclaration(
                        context.ResolveTypeAnnotation(property.DeclaredType, state));
            }

            // Not rooted at `$this`/a declared parameter (e.g. a global function call, `new Foo()`,
            // or a local variable) — safe to infer normally.
            if (!hasThisParameter && context is not null && state is not null)
            {
                var inferred = context.ResolveExpressionType(receiver, state);
                return CheckerHelpers.TryGetObjectDeclaration(inferred);
            }

            return null;
        }

        private static void AddNestedParameterNames(IBase2Ast node, HashSet<string> shadowed)
        {
            PhpParameterListAst? list = node switch
            {
                PhpInlineFunctionAst fn => fn.Parameters,
                PhpFunctionDeclAst fn => fn.Parameters,
                _ => null,
            };

            if (list is null)
            {
                return;
            }

            foreach (var param in list.GetAllNotNull())
            {
                var name = SpliceAst.NormalizeName(param.Name);
                if (name.Length > 0)
                {
                    shadowed.Add(name);
                }
            }
        }
    }
}
