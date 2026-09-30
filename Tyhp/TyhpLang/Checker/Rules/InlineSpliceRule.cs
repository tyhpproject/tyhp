using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Emitter.Splice;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Story 20.6 splice-engine checker: 4174 / 4175 / 4176 / 4177 / 4179 / 4181.
    /// 4180 lives in <see cref="ReferenceTrackingRule"/> (general call-site rule).
    /// </summary>
    public sealed class InlineSpliceRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpFunctionDeclAst),
            typeof(PhpMethodDeclAst),
            typeof(TyhpOperatorOverloadAst),
            typeof(TyhpdefInlineExtensionFunctionAst),
            typeof(PhpDereferenceableAst),
            typeof(PhpJumpStatementAst),
            typeof(PhpReturnStatementAst),
            typeof(PhpVariableAst),
            typeof(TyhpTypedVarExprAst),
            typeof(PhpParameterAst),
        ];

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpFunctionDeclAst function:
                    CheckMember(function, function, state, context, diagnostics);
                    break;
                case PhpMethodDeclAst method:
                    CheckMember(method, method, state, context, diagnostics);
                    break;
                case TyhpOperatorOverloadAst op:
                    CheckMember(op, op, state, context, diagnostics);
                    break;
                case TyhpdefInlineExtensionFunctionAst inline:
                    CheckMember(inline, SpliceAst.UnwrapDeclaringNode(inline), state, context, diagnostics);
                    break;
                case PhpDereferenceableAst deref when deref.Suffix is PhpCallAst call:
                    CheckCallSite(deref, call, state, context, diagnostics);
                    break;
                case PhpJumpStatementAst jump:
                    WalkCallSites(jump.Expression, state, context, diagnostics);
                    break;
                case PhpReturnStatementAst ret:
                    WalkCallSites(ret.Expression, state, context, diagnostics);
                    break;
                case PhpVariableAst variable:
                    RejectReservedTemp(variable, CheckerHelpers.GetVariableName(variable), state, diagnostics);
                    break;
                case TyhpTypedVarExprAst typed:
                    if (typed.Variable is { } typedVar)
                    {
                        RejectReservedTemp(
                            typedVar, CheckerHelpers.GetVariableName(typedVar), state, diagnostics);
                    }

                    break;
                case PhpParameterAst param:
                    RejectReservedTemp(param, SpliceAst.NormalizeName(param.Name), state, diagnostics);
                    break;
            }
        }

        internal static void CheckMemberDeclaration(
            IBase2Ast node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckMember(node, node, state, context, diagnostics);
        }

        private static void CheckMember(
            IBase2Ast reportNode,
            IBase2Ast declaringNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (SpliceAst.DeclaresOptimizeInline(reportNode) || SpliceAst.DeclaresOptimizeInline(declaringNode))
            {
                if (IsExtensionMember(reportNode, declaringNode, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerInlineAttributeOnExtensionMember);
                }
            }

            // BoundSymbol (and authored `&$this`) live on the inner method, not the
            // TyhpdefInlineExtensionFunctionAst wrapper. CheckMemberDeclaration walks the
            // wrapper for included tyhpdefs that never appear in ParsedFiles.
            var bodyNode = SpliceAst.UnwrapDeclaringNode(declaringNode);
            if (!SpliceAst.TryGetSingleReturnExpression(bodyNode, out var body)
                && !SpliceAst.TryGetSingleReturnExpression(declaringNode, out body))
            {
                return;
            }

            if (!IsSpliceCandidate(reportNode, declaringNode, state)
                && !IsSpliceCandidate(reportNode, bodyNode, state))
            {
                return;
            }

            var callee = bodyNode.BoundSymbol ?? declaringNode.BoundSymbol ?? reportNode.BoundSymbol;
            var parameters = ParametersOf(bodyNode, callee);

            // Splice declaration checks often run on the outer extension/object state
            // (ExtensionRule suppresses child traversal; CheckBoundTyhpdefThinMappings
            // builds a fresh CheckerState). Fork so method generics (`TKey`/`TValue` on
            // `array<TKey, TValue>`) resolve instead of TYHP3003. Computed before
            // `CreateEngineContext` so `ResolveCallReceiverType` can resolve a `$this`/parameter
            // receiver (and chains rooted at one) without falling back to
            // `CheckerRuleContext.ResolveExpressionType` against a state that has no live binding
            // for either (Story 21.12 Workstream C review — see `ResolveCallReceiverType`).
            var resolveState = StateWithCalleeGenerics(state, callee);
            var thisType = ResolveThisParameterType(callee, resolveState, context, out var hasThisParameter);
            var parameterTypes = ResolveParameterTypes(callee, resolveState, context);

            var engineContext = CreateEngineContext(
                resolveState, context, thisType, hasThisParameter, parameterTypes);
            var written = SpliceAnalysis.WrittenParameterNames(body, parameters, engineContext);
            var writesThis = SpliceAnalysis.WritesThis(body, engineContext);
            var memberName = callee?.Name
                ?? reportNode.Identifier
                ?? declaringNode.Identifier
                ?? "";

            // Block-target extensions report a missing or unused `&$this` annotation
            // themselves. Class-body thin mappings and other splice parameters still use
            // TYHP4174.
            var blockTargetReceiver = ExtensionBlockTargetChecks.IsUserExtensionBlock(callee);
            var thisParam = SpliceAnalysis.ThisByName(parameters);
            if (!blockTargetReceiver && writesThis && thisParam is not { IsByReference: true })
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, reportNode, MessageCode.CheckerInlineParameterMutation, memberName);
            }

            foreach (var param in parameters)
            {
                if (blockTargetReceiver && param.IsThis)
                {
                    continue;
                }

                var isWritten = written.Contains(param.Name);
                if (isWritten != param.IsByReference)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, reportNode, MessageCode.CheckerInlineParameterMutation, memberName);
                    break;
                }
            }

            var calleeVisibility = SpliceAst.VisibilityOf(callee);
            if (IsExtensionMember(reportNode, declaringNode, state))
            {
                calleeVisibility = MemberModifier.Public;
            }

            foreach (var hit in SpliceAnalysis.ReferencedMembers(
                body, resolveState, context, thisType, hasThisParameter, parameterTypes))
            {
                if (!SpliceAnalysis.IsAtLeastAsAccessible(SpliceAst.VisibilityOf(hit.Member), calleeVisibility))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        hit.Node,
                        MessageCode.CheckerInlineInaccessibleMember,
                        memberName,
                        hit.Member.Name);
                }
            }

            if (callee is not null && HasSpliceCycle(callee, engineContext, new HashSet<IBaseSymbol>(ReferenceEqualityComparer.Instance)))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, reportNode, MessageCode.CheckerInlineCycle, memberName);
            }
        }

        /// <summary>
        /// <see cref="ControlFlowRule"/> suppresses child traversal on <c>return</c>, so call
        /// sites in <c>return $recv-&gt;ext();</c> never reach <see cref="Check"/> via the default
        /// walk. Visit splice call sites in that expression directly.
        /// </summary>
        private static void WalkCallSites(
            IBase2Ast? node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (node is null || node is PhpInlineFunctionAst or PhpFunctionDeclAst or PhpMethodDeclAst)
            {
                return;
            }

            if (node is PhpDereferenceableAst { Suffix: PhpCallAst call } deref)
            {
                CheckCallSite(deref, call, state, context, diagnostics);
            }

            foreach (var child in node.AstChildren)
            {
                WalkCallSites(child, state, context, diagnostics);
            }
        }

        private static void CheckCallSite(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var callee = deref.BoundSymbol
                ?? (deref.Base as PhpDereferenceableAst)?.BoundSymbol
                ?? (deref.Base as PhpNameAst)?.BoundSymbol
                ?? ResolveExtensionCallee(deref, state, context);
            if (callee is null)
            {
                return;
            }

            var declaring = SpliceAst.DeclaringNodeOf(callee);
            if (SpliceAst.CalleeDeclaresOptimizeInline(callee)
                && (declaring is null || IsExtensionMember(declaring, declaring, state)
                    || CallSiteSpliceEngine.OwnerOf(callee) is { IsExtension: true }))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerInlineAttributeOnExtensionMember);
            }

            if (!CallSiteSpliceEngine.TryBuildRequestFromCallee(callee, deref, call, out var request))
            {
                return;
            }

            if (request.HasPhpBacker)
            {
                return;
            }

            var engineContext = CreateEngineContext(state, context);
            SpliceAnalysis.CollectUses(
                request.Body,
                request.Parameters,
                out var eagerCounts,
                out var lazyNames,
                out var eagerOrder);

            var needsRefHoist = false;
            foreach (var param in request.Parameters)
            {
                var arg = SpliceAnalysis.ArgumentFor(
                    param, request.Parameters, request.Receiver, request.Arguments);
                var count = eagerCounts.GetValueOrDefault(param.Name);
                if (SpliceAnalysis.NeedsRefBoundHoist(
                    param, arg, count, SpliceAnalysis.IsByValueHookedRead(arg)))
                {
                    needsRefHoist = true;
                }
            }

            var reason = SpliceAnalysis.EvaluateFidelity(
                request.Parameters,
                request.Receiver,
                request.Arguments,
                eagerCounts,
                lazyNames,
                eagerOrder,
                needsRefHoist,
                hasStatementSlot: true);
            if (reason == SpliceDeclineReason.None)
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                deref,
                MessageCode.CheckerErasedMemberUnsafeSplice,
                SpliceAst.MemberDisplayName(callee));
        }

        private static void RejectReservedTemp(
            IBase2Ast node,
            string? name,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!GeneratedNames.StartsWithInlineTempPrefix(name))
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                node,
                MessageCode.CheckerReservedInlineTempPrefix,
                SpliceAst.NormalizeName(name),
                GeneratedNames.InlineTempVariablePrefix);
        }

        private static bool IsExtensionMember(IBase2Ast reportNode, IBase2Ast declaringNode, CheckerState state)
        {
            if (reportNode is TyhpdefInlineExtensionFunctionAst
                || declaringNode is TyhpdefInlineExtensionFunctionAst)
            {
                return true;
            }

            if (declaringNode is TyhpOperatorOverloadAst opDecl
                && (opDecl.IsInlineExtension || opDecl.IsExtensionOperator))
            {
                return true;
            }

            if (reportNode is TyhpOperatorOverloadAst opReport
                && (opReport.IsInlineExtension || opReport.IsExtensionOperator))
            {
                return true;
            }

            if (state.EnclosingObject is { IsExtension: true })
            {
                return true;
            }

            var symbol = declaringNode.BoundSymbol ?? reportNode.BoundSymbol;
            if (symbol is null)
            {
                return false;
            }

            // `CallSiteSpliceEngine.OwnerOf` returns the *nearest* enclosing
            // `ObjectDeclarationSymbol`, which for a nested `extends<T> …` group member is the
            // compiler-generated group symbol (`IsExtensionTargetGroup: true`, `IsExtension:
            // false`), not the real extension header one level up. That mismatch used to make
            // every nested-group member invisible to this rule — 4174 (`&$this` mutation), 4179
            // (accessibility), and 4181 (splice cycle) all silently skipped, and the block target's
            // own generics (`extends<T> …`) never seeded before resolving a synthetic `$this`
            // parameter's declared type. `IsUserExtensionBlock` walks past target-group scopes to
            // find the owning header, matching how `blockTargetReceiver` above already does it.
            return ExtensionBlockTargetChecks.IsUserExtensionBlock(symbol);
        }

        private static bool IsSpliceCandidate(IBase2Ast reportNode, IBase2Ast declaringNode, CheckerState state)
        {
            if (IsExtensionMember(reportNode, declaringNode, state))
            {
                return true;
            }

            return SpliceAst.DeclaresOptimizeInline(reportNode)
                || SpliceAst.DeclaresOptimizeInline(declaringNode);
        }

        /// <summary>
        /// Cycle detection follows only members the splice engine would reduce — not every
        /// single-return function in the program. <c>#[\Tyhp\Optimize\Inline]</c> is included for
        /// checker cycles even though emit nested reduction does not yet splice those members.
        /// </summary>
        private static bool IsSpliceableCallee(IBaseSymbol callee)
        {
            var declaring = SpliceAst.DeclaringNodeOf(callee);
            if (declaring is null || !SpliceAst.TryGetSingleReturnExpression(declaring, out _))
            {
                return false;
            }

            if (SpliceAst.DeclaresOptimizeInline(declaring))
            {
                return true;
            }

            return CallSiteSpliceEngine.IsSpliceOwnedCallee(callee);
        }

        private static IReadOnlyList<SpliceParameter> ParametersOf(IBase2Ast declaringNode, IBaseSymbol? callee)
        {
            if (SpliceAst.ParametersOf(callee) is { Count: > 0 } fromSymbol)
            {
                var owner = callee is null ? null : CallSiteSpliceEngine.OwnerOf(callee);
                var firstIsThis = owner is { IsExtension: true }
                    || SpliceAst.IsThisName(fromSymbol[0].Name);
                return SpliceAst.ParametersFromSymbol(fromSymbol, firstIsThis);
            }

            PhpParameterListAst? list = declaringNode switch
            {
                PhpFunctionDeclAst fn => fn.Parameters,
                PhpMethodDeclAst method => method.Parameters,
                TyhpdefInlineExtensionFunctionAst inline => inline.Method?.Parameters,
                TyhpOperatorOverloadAst op => null,
                _ => null,
            };

            if (declaringNode is TyhpOperatorOverloadAst overload)
            {
                var ops = new List<SpliceParameter>();
                if (overload.LeftParameter is { } left)
                {
                    ops.Add(ToSpliceParameter(left, isThis: SpliceAst.IsThisName(left.Name)));
                }

                if (overload.RightParameter is { } right)
                {
                    ops.Add(ToSpliceParameter(right, isThis: false));
                }

                return ops;
            }

            if (list is null)
            {
                return [];
            }

            return list.GetAllNotNull()
                .Select(p => ToSpliceParameter(p, SpliceAst.IsThisName(p.Name)))
                .ToList();
        }

        private static SpliceParameter ToSpliceParameter(PhpParameterAst param, bool isThis) =>
            new(
                SpliceAst.NormalizeName(param.Name),
                param.IsRef,
                param.DefaultValue,
                isThis);

        private static SpliceEngineContext CreateEngineContext(
            CheckerState state,
            CheckerRuleContext context,
            ObjectDeclarationSymbol? thisType = null,
            bool hasThisParameter = false,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes = null) =>
            new()
            {
                ResolveFreeFunction = name =>
                    CheckerHelpers.ResolveFreeFunction(
                        name, state, context.SymbolTree, context.GlobalScope)?.Parameters,
                ResolveCallParameters = node =>
                    ResolveSpliceCallParameters(node, state, context, thisType, hasThisParameter, parameterTypes),
            };

        /// <summary>
        /// Splice-member 4174 runs before the body is <c>CheckNode</c>'d, so
        /// <c>Json::tryDecode($this, $out)</c> has no <c>BoundSymbol</c> yet. Free functions
        /// still resolve by name; static/instance methods need a SymbolTree lookup so passing a
        /// parameter into a callee <c>&amp;</c> slot counts as a write.
        /// </summary>
        private static IReadOnlyList<ParameterInfo>? ResolveSpliceCallParameters(
            IBase2Ast node,
            CheckerState state,
            CheckerRuleContext context,
            ObjectDeclarationSymbol? thisType,
            bool hasThisParameter,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes)
        {
            if (node is not PhpDereferenceableAst deref
                || deref.Suffix is not PhpCallAst
                || deref.Base is not PhpDereferenceableAst chain)
            {
                return null;
            }

            var (memberName, staticOnly) = chain.Suffix switch
            {
                PhpInstanceMemberAccessAst instance => (CallMemberName(instance.MemberName), false),
                PhpStaticMemberAccessAst staticAccess => (CallMemberName(staticAccess.Member), true),
                PhpClassConstantAccessAst classConst => (CallMemberName(classConst.Member), true),
                _ => ((string?)null, false),
            };
            if (string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var receiverType = ResolveCallReceiverType(
                chain.Base, state, context, thisType, hasThisParameter, parameterTypes);
            if (receiverType is null || TypeComparer.IsUnresolvedType(receiverType))
            {
                return null;
            }

            if (!CheckerHelpers.TryResolveInstanceOrExtensionMethod(
                    receiverType,
                    memberName,
                    staticOnly,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    out var method)
                || method is null)
            {
                return null;
            }

            return method.Parameters;
        }

        private static string? CallMemberName(IBase2Ast? member) =>
            member switch
            {
                PhpNameAst n => n.ValueString ?? n.Identifier,
                TokenValueAst t => t.ValueString,
                IExpression e => e.Identifier,
                _ => member?.Identifier,
            };

        private static ICheckedType? ResolveCallReceiverType(
            IDereferenceableBase? receiver,
            CheckerState state,
            CheckerRuleContext context,
            ObjectDeclarationSymbol? thisType,
            bool hasThisParameter,
            IReadOnlyDictionary<string, ObjectDeclarationSymbol?>? parameterTypes)
        {
            if (receiver is PhpNameAst name)
            {
                return ResolveClassReceiverType(name, state, context);
            }

            if (receiver is PhpVariableAst variable && CheckerHelpers.IsThisVariable(variable))
            {
                return state.EnclosingObjectType
                    ?? (state.EnclosingObject is not null
                        ? CheckedTypes.FromSymbol(state.EnclosingObject)
                        // A splice-member pre-pass has no `EnclosingObject` for an extension's
                        // `$this` (it is a declared parameter, not a real class receiver — see
                        // `ExtensionRule.CheckExtensionFunction`). `thisType` is only populated for
                        // an *object* receiver; a scalar receiver (`extends int $this`) falls
                        // through to `null` here rather than `ResolveExpressionType`, which would
                        // need a live `$this` binding this synthetic state does not have (Story
                        // 21.12 Workstream C review).
                        : (thisType is not null ? CheckedTypes.FromSymbol(thisType) : null));
            }

            if (receiver is PhpVariableAst paramVar
                && parameterTypes is not null
                && parameterTypes.TryGetValue(
                    SpliceAst.NormalizeName(CheckerHelpers.GetVariableName(paramVar)) ?? "", out var paramType))
            {
                return paramType is not null ? CheckedTypes.FromSymbol(paramType) : null;
            }

            // A receiver rooted at `$this` or a declared parameter but not resolved above (e.g. a
            // nested `$this->doubled()->x` chain) must not fall through to
            // `ResolveExpressionType`: that runs in this same synthetic state with no
            // `$this`/parameter bindings, so it would infer (and permanently cache on this exact
            // AST node) an `Unresolved` type for the chain — corrupting the real checker pass's
            // later, correctly-scoped resolution of the same node. Returning `null` here only
            // means this call's parameter types are unknown to the `&`-ref mutation check below;
            // it does not affect the (separately state-independent) TYHP4179 accessibility walk.
            if (hasThisParameter)
            {
                return null;
            }

            if (receiver is IExpression expr)
            {
                return context.ResolveExpressionType(expr, state);
            }

            return null;
        }

        private static ICheckedType? ResolveClassReceiverType(
            PhpNameAst nameAst,
            CheckerState state,
            CheckerRuleContext context)
        {
            var rawName = nameAst.ValueString;
            if (string.IsNullOrEmpty(rawName))
            {
                return null;
            }

            var simpleName = rawName.TrimStart('\\');
            if (string.Equals(simpleName, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(simpleName, "static", StringComparison.OrdinalIgnoreCase))
            {
                return state.EnclosingObjectType
                    ?? (state.EnclosingObject is not null
                        ? CheckedTypes.FromSymbol(state.EnclosingObject)
                        : null);
            }

            if (string.Equals(simpleName, "parent", StringComparison.OrdinalIgnoreCase)
                && state.EnclosingObject is { } enclosing
                && TypeComparer.TryGetParentDeclaration(
                    enclosing, context.SymbolTree, context.GlobalScope) is { } parent)
            {
                return CheckedTypes.FromSymbol(parent);
            }

            var scope = CheckerHelpers.GetCallSiteScope(state, context.GlobalScope);
            var silent = new DiagnosticBag();
            IBaseSymbol? symbol;
            if (rawName.StartsWith('\\'))
            {
                symbol = context.SymbolTree.ResolveQualifiedName(
                    simpleName.Split('\\'), scope, silent);
            }
            else if (rawName.Contains('\\'))
            {
                symbol = context.SymbolTree.ResolveRelativeName(
                    simpleName.Split('\\'), scope, silent);
            }
            else
            {
                symbol = context.SymbolTree.ResolveSymbol(rawName, scope, silent)
                    ?? context.SymbolTree.ResolveRelativeName([simpleName], scope, silent);

                // CheckMemberDeclaration for extensions runs on namespace state before the
                // method's EnclosingCallable is set, so GetCallSiteScope is often GlobalScope.
                // Bare `Json::tryDecode` in `namespace Tyhp` must still find `\Tyhp\Json`.
                if (symbol is not ObjectDeclarationSymbol
                    && !string.IsNullOrEmpty(state.CurrentNamespaceName))
                {
                    var qualified = state.CurrentNamespaceName
                        .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                        .Append(simpleName)
                        .ToArray();
                    symbol = context.SymbolTree.ResolveQualifiedName(qualified, scope, silent);
                }
            }

            return symbol is ObjectDeclarationSymbol obj
                ? CheckedTypes.FromSymbol(obj)
                : null;
        }

        private static bool HasSpliceCycle(
            IBaseSymbol callee,
            SpliceEngineContext engineContext,
            HashSet<IBaseSymbol> visiting)
        {
            if (!visiting.Add(callee))
            {
                return true;
            }

            var declaring = SpliceAst.DeclaringNodeOf(callee);
            if (declaring is null || !SpliceAst.TryGetSingleReturnExpression(declaring, out var body))
            {
                visiting.Remove(callee);
                return false;
            }

            var found = false;
            WalkCalls(body, callee, callCallee =>
            {
                if (found)
                {
                    return;
                }

                if (!IsSpliceableCallee(callCallee))
                {
                    return;
                }

                if (ReferenceEquals(callCallee, callee) || visiting.Contains(callCallee))
                {
                    found = true;
                    return;
                }

                if (HasSpliceCycle(callCallee, engineContext, visiting))
                {
                    found = true;
                }
            });

            visiting.Remove(callee);
            return found;
        }

        private static void WalkCalls(IBase2Ast? node, IBaseSymbol current, Action<IBaseSymbol> onCallee)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpDereferenceableAst { Suffix: PhpCallAst } deref)
            {
                var callee = deref.BoundSymbol
                    ?? (deref.Base as PhpDereferenceableAst)?.BoundSymbol
                    ?? (deref.Base as PhpNameAst)?.BoundSymbol
                    ?? ResolveSiblingExtensionCall(deref, current);
                if (callee is ObjectMethodSymbol or FunctionDeclarationSymbol)
                {
                    onCallee(callee);
                }
            }

            foreach (var child in node.AstChildren)
            {
                WalkCalls(child, current, onCallee);
            }
        }

        private static IBaseSymbol? ResolveSiblingExtensionCall(PhpDereferenceableAst deref, IBaseSymbol current)
        {
            var name = InstanceCallMethodName(deref);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var owner = CallSiteSpliceEngine.OwnerOf(current);
            if (owner is null)
            {
                return null;
            }

            if (owner.Members.TryGetValue(name, out var member)
                && member is ObjectMethodSymbol or FunctionDeclarationSymbol)
            {
                return member;
            }

            foreach (var candidate in owner.Members.Values.OfType<ObjectMethodSymbol>())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves the callee of a <c>$recv-&gt;method(...)</c> call site to an extension member
        /// (Tyhp <c>extension { }</c> or a tyhpdef class-body thin mapping) when it is not already
        /// a plain member of the receiver's own type. Shared with <see cref="ReferenceTrackingRule"/>
        /// so 4180 (non-referenceable by-reference argument) also covers extension calls, not just
        /// free functions and plain methods.
        /// </summary>
        internal static IBaseSymbol? ResolveExtensionCallee(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context)
        {
            var name = InstanceCallMethodName(deref);
            if (string.IsNullOrEmpty(name)
                || deref.Base is not PhpDereferenceableAst { Base: IExpression receiver })
            {
                return null;
            }

            var receiverType = context.ResolveExpressionType(receiver, state);
            IBaseSymbol? onType = CheckerHelpers.TryGetObjectDeclaration(receiverType)
                ?? (receiverType as SimpleCheckedType)?.ResolvedSymbol;
            if (onType is null)
            {
                return null;
            }

            if (onType is ObjectDeclarationSymbol obj
                && SpliceAst.TryFindSyntheticInlineMember(obj, name) is { } synthetic)
            {
                return synthetic;
            }

            var resolver = new NameResolver(context.SymbolTree, context.Diagnostics);
            var lexical = state.NameResolutionScope
                ?? state.EnclosingFunction?.ContainingScope
                ?? state.EnclosingCallable?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope;
            return resolver.ResolveExtensionMethod(
                name, onType, resolver.FindCallSiteScope(deref, lexical));
        }

        private static CheckerState StateWithCalleeGenerics(CheckerState state, IBaseSymbol? callee)
        {
            var generics = callee switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => null,
            };

            // A block-target extension method's own generic parameters (`extension Ops<T>
            // extends MyClass<T>`, or a nested `extends<T> …` group) are not on the method
            // symbol — they live on the enclosing extension/group `ObjectDeclarationSymbol`.
            // Without these, resolving the synthetic `$this` parameter's declared type
            // (`MyClass<T>`) below via `ResolveThisParameterType`/`ResolveParameterTypes`
            // reports a spurious "Symbol `T` is not found" (TYHP3003) the first time this
            // splice-member pre-pass actually reaches a generic block target.
            var blockGenerics = callee is ObjectMethodSymbol blockMethod
                ? BlockTypeParametersOf(blockMethod)
                : null;

            if (generics is not { Count: > 0 } && blockGenerics is not { Count: > 0 })
            {
                return state;
            }

            var forked = state.Fork();
            forked.FunctionGenerics = generics ?? [];
            forked.EnclosingCallable = callee;
            if (blockGenerics is { Count: > 0 })
            {
                forked.ObjectGenerics = blockGenerics;
            }

            return forked;
        }

        /// <summary>
        /// In-scope generic type parameters for the nearest enclosing extension header or
        /// nested <c>extends</c> group that declares <paramref name="method"/>, combining the
        /// group's own parameters with the enclosing extension's when they differ (mirrors
        /// <see cref="ExtensionBlockTargetChecks.InScopeTypeParameters"/>). Null when
        /// <paramref name="method"/> is not a block-target extension member.
        /// </summary>
        private static IReadOnlyList<GenericTypeParameterSymbol>? BlockTypeParametersOf(ObjectMethodSymbol method)
        {
            ObjectDeclarationSymbol? block = null;
            ObjectDeclarationSymbol? extension = null;
            for (var scope = method.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is not ObjectDeclarationSymbol obj)
                {
                    continue;
                }

                if (!obj.IsExtension && !obj.IsExtensionTargetGroup)
                {
                    return null;
                }

                block = obj;
                if (obj.IsExtension && !obj.IsExtensionTargetGroup)
                {
                    extension = obj;
                    break;
                }

                for (var outer = scope.ParentScope; outer != null; outer = outer.ParentScope)
                {
                    if (outer.DeclarationSymbol is ObjectDeclarationSymbol { IsExtension: true, IsExtensionTargetGroup: false } outerExtension)
                    {
                        extension = outerExtension;
                        break;
                    }
                }

                break;
            }

            return block is null ? null : ExtensionBlockTargetChecks.InScopeTypeParameters(block, extension);
        }

        /// <summary>
        /// Object type of the callee's declared <c>$this</c> receiver, if any.
        /// <paramref name="hasThisParameter"/> reports whether the signature declares a
        /// <c>$this</c> parameter at all (extension / operator-overload receiver), independent of
        /// whether that receiver happens to be an object type — a scalar receiver
        /// (<c>extends int $this</c>) has <c>hasThisParameter = true</c> and a <see langword="null"/>
        /// return, which callers must not treat as "unresolved" (see
        /// <see cref="SpliceAnalysis.ReferencedMembers"/>).
        /// </summary>
        private static ObjectDeclarationSymbol? ResolveThisParameterType(
            IBaseSymbol? callee,
            CheckerState state,
            CheckerRuleContext context,
            out bool hasThisParameter)
        {
            hasThisParameter = false;
            var infos = SpliceAst.ParametersOf(callee);
            if (infos is null || infos.Count == 0)
            {
                return null;
            }

            var thisParam = infos.FirstOrDefault(p => SpliceAst.IsThisName(p.Name));
            if (thisParam is null)
            {
                return null;
            }

            hasThisParameter = true;
            return thisParam.DeclaredType is null
                ? null
                : CheckerHelpers.TryGetObjectDeclaration(
                    context.ResolveTypeAnnotation(thisParam.DeclaredType, state));
        }

        /// <summary>
        /// Maps every declared, named parameter of <paramref name="callee"/> to its resolved object
        /// type (or <see langword="null"/> when the declared type is not an object — e.g. a scalar
        /// extension receiver), keyed by normalized name (e.g. the operator overload receiver
        /// <c>self $l</c> maps <c>"l"</c> to the enclosing tyhpdef type). 4179 accessibility analysis
        /// on a tyhpdef thin mapping runs in a synthetic <see cref="CheckerState"/> with no live
        /// parameter scope, so <see cref="SpliceAnalysis.ReferencedMembers"/> cannot infer a bare
        /// parameter reference's type through normal expression-type inference; this gives it the
        /// answer directly from the signature instead. A key present with a <see langword="null"/>
        /// value means "declared, not an object" — callers must not fall back to expression-type
        /// inference for it (that runs in a state with no parameter scope and would cache a bogus
        /// <c>Unresolved</c> type for the node). A key that is entirely absent means "not one of
        /// this callee's declared parameters at all."
        /// </summary>
        private static IReadOnlyDictionary<string, ObjectDeclarationSymbol?> ResolveParameterTypes(
            IBaseSymbol? callee,
            CheckerState state,
            CheckerRuleContext context)
        {
            var infos = SpliceAst.ParametersOf(callee);
            if (infos is null || infos.Count == 0)
            {
                return new Dictionary<string, ObjectDeclarationSymbol?>();
            }

            var map = new Dictionary<string, ObjectDeclarationSymbol?>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in infos)
            {
                var name = SpliceAst.NormalizeName(info.Name);
                if (string.IsNullOrEmpty(name) || SpliceAst.IsThisName(info.Name))
                {
                    continue;
                }

                map[name] = info.DeclaredType is null
                    ? null
                    : CheckerHelpers.TryGetObjectDeclaration(
                        context.ResolveTypeAnnotation(info.DeclaredType, state));
            }

            return map;
        }

        private static string? InstanceCallMethodName(PhpDereferenceableAst deref)
        {
            if (deref.Base is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst member })
            {
                return null;
            }

            return member.MemberName switch
            {
                PhpNameAst n => n.ValueString ?? n.Identifier,
                TokenValueAst t => t.ValueString,
                IExpression e => e.Identifier,
                _ => member.MemberName?.Identifier,
            };
        }
    }
}
