using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter.Splice
{
    /// <summary>
    /// Shared call-site splice engine: substitute receiver/arguments into a single-return
    /// expression, parenthesize, reduce nested <see cref="IsSpliceOwnedCallee"/> splices to a
    /// fixpoint, and hoist repeated evaluation into by-value or ref-bound temps.
    /// </summary>
    public sealed class CallSiteSpliceEngine
    {
        private readonly SpliceEngineContext _context;

        public CallSiteSpliceEngine(SpliceEngineContext context)
        {
            _context = context;
        }

        public SpliceResult TrySplice(SpliceRequest request)
        {
            if (!_context.ReductionStack.Add(request.Callee))
            {
                return SpliceResult.Decline(SpliceDeclineReason.Cycle, request.HasPhpBacker);
            }

            try
            {
                return TrySpliceCore(request);
            }
            finally
            {
                _context.ReductionStack.Remove(request.Callee);
            }
        }

        private SpliceResult TrySpliceCore(SpliceRequest request)
        {
            SpliceAnalysis.CollectUses(
                request.Body,
                request.Parameters,
                out var eagerCounts,
                out var lazyNames,
                out var eagerOrder);

            var hoists = new List<Hoist>();
            var substitutions = new Dictionary<string, IExpression>(StringComparer.OrdinalIgnoreCase);

            foreach (var param in request.Parameters)
            {
                var key = SpliceAst.NormalizeName(param.Name);
                var arg = SpliceAnalysis.ArgumentFor(
                    param, request.Parameters, request.Receiver, request.Arguments)
                    ?? param.DefaultValue;
                if (arg is null)
                {
                    continue;
                }

                var count = eagerCounts.GetValueOrDefault(key);
                var hookedByValue = SpliceAnalysis.IsByValueHookedRead(arg);
                var hookedByRef = SpliceAnalysis.IsByRefHookedRead(arg);
                // A simple variable (or literal/constant) skips the local even for a `&`
                // parameter: substituting the same variable at every occurrence is exactly what a
                // real by-reference bind would do, so no ref-bound temp is needed. Only a
                // non-simple argument (property read, array element, call result) needs hoisting
                // to avoid re-evaluating it or losing the reference.
                var skipLocal = SpliceAst.IsSimpleArgument(arg) && !hookedByValue && !hookedByRef;

                if (count > 1 && !skipLocal)
                {
                    var tempName = _context.AllocateTempName();
                    var byRef = param.IsByReference || hookedByRef;
                    hoists.Add(new Hoist(tempName, arg, byRef, request.CallSite));
                    substitutions[key] = PhpVariableAst.CreateFromContext(
                        tempName,
                        (Base2Ast)request.CallSite);
                }
                else
                {
                    substitutions[key] = arg;
                }
            }

            if (request.Receiver is not null
                && !request.Parameters.Any(p => p.IsThis)
                && eagerCounts.GetValueOrDefault("this") > 0)
            {
                var count = eagerCounts.GetValueOrDefault("this");
                if (count > 1 && !SpliceAst.IsSimpleArgument(request.Receiver))
                {
                    var tempName = _context.AllocateTempName();
                    hoists.Add(new Hoist(tempName, request.Receiver, ByRef: false, request.CallSite));
                    substitutions["this"] = PhpVariableAst.CreateFromContext(
                        tempName,
                        (Base2Ast)request.CallSite);
                }
                else
                {
                    substitutions["this"] = request.Receiver;
                }
            }

            var needsRefHoist = hoists.Any(h => h.ByRef);
            var fidelity = SpliceAnalysis.EvaluateFidelity(
                request.Parameters,
                request.Receiver,
                request.Arguments,
                eagerCounts,
                lazyNames,
                eagerOrder,
                needsRefHoist,
                request.HasStatementSlot);
            if (fidelity != SpliceDeclineReason.None)
            {
                return SpliceResult.Decline(fidelity, request.HasPhpBacker);
            }

            if (needsRefHoist && !request.HasStatementSlot)
            {
                return SpliceResult.Decline(
                    SpliceDeclineReason.RefHoistNeedsStatementSlot,
                    request.HasPhpBacker);
            }

            var cloned = SpliceAst.Clone(request.Body);
            var rewritten = Substitute(cloned, substitutions, request);
            rewritten = ReduceNested(rewritten, request);
            var parenthesized = SpliceAst.Parenthesize(rewritten, (Base2Ast)request.CallSite);
            SpliceAst.StampOriginalAst(parenthesized, request.CallSite);

            IReadOnlyList<IExpression> hoistStatements = [];
            IExpression expression = parenthesized;
            if (hoists.Count > 0)
            {
                var statements = new List<IExpression>();
                IExpression current = parenthesized;
                for (var i = hoists.Count - 1; i >= 0; i--)
                {
                    var hoist = hoists[i];
                    var tempVar = PhpVariableAst.CreateFromContext(hoist.Name, (Base2Ast)request.CallSite);
                    if (hoist.ByRef)
                    {
                        var refTemp = PhpVariableAst.CreateFromContext(
                            hoist.Name, (Base2Ast)request.CallSite, isRef: true);
                        statements.Insert(0, CreateRefBind(refTemp, hoist.Argument, request.CallSite));
                    }
                    else
                    {
                        var bind = WithKeywordHelper.CreateAssignment(tempVar, hoist.Argument, (Base2Ast)request.CallSite);
                        current = ReplaceFirstUseWithBind(current, hoist.Name, bind);
                    }

                    SpliceAst.StampOriginalAst(tempVar, request.CallSite);
                    SpliceAst.StampOriginalAst(hoist.Argument, request.CallSite);
                }

                hoistStatements = statements;
                expression = current;
                SpliceAst.StampOriginalAst(expression, request.CallSite);
            }

            return SpliceResult.Spliced(expression, hoistStatements);
        }

        private IExpression Substitute(
            IExpression cloned,
            Dictionary<string, IExpression> substitutions,
            SpliceRequest request)
        {
            if (cloned is not Base2Ast)
            {
                return cloned;
            }

            return (IExpression)AstWalker.TransformTree(
                cloned,
                node => TransformSubstitution(node, substitutions, request))!;
        }

        private IBase2Ast? TransformSubstitution(
            IBase2Ast node,
            Dictionary<string, IExpression> substitutions,
            SpliceRequest request)
        {
            if (node is PhpInlineFunctionAst or PhpFunctionDeclAst)
            {
                return node;
            }

            if (node is PhpVariableAst variable)
            {
                var name = CheckerHelpers.GetVariableName(variable);
                if (!string.IsNullOrEmpty(name)
                    && substitutions.TryGetValue(SpliceAst.NormalizeName(name), out var replacement)
                    && replacement is IBase2Ast)
                {
                    var clone = SpliceAst.Clone(replacement);
                    SpliceAst.StampOriginalAst(clone, request.CallSite);
                    return clone;
                }
            }

            if (node is PhpDereferenceableAst deref
                && deref.Base is PhpNameAst className
                && (deref.Suffix is PhpStaticMemberAccessAst or PhpClassConstantAccessAst))
            {
                var rewrittenBase = RewriteClassContextKeyword(
                    className, request, substitutions);
                if (rewrittenBase is not null && !ReferenceEquals(rewrittenBase, className)
                    && rewrittenBase is IDereferenceableBase newBase)
                {
                    var replacement = PhpDereferenceableAst.CreateFromContext(
                        newBase, deref.Suffix, (Base2Ast)request.CallSite);
                    replacement.BoundSymbol = deref.BoundSymbol;
                    SpliceAst.StampOriginalAst(replacement, request.CallSite);
                    return replacement;
                }
            }

            return node;
        }

        private IExpression? RewriteClassContextKeyword(
            PhpNameAst className,
            SpliceRequest request,
            Dictionary<string, IExpression> substitutions)
        {
            var text = className.ValueString ?? className.Identifier ?? "";
            if (string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(request.DeclaringClassFqn))
            {
                // `self` bound to a different object (a nested anonymous class, or any
                // nearer declaration) stays `self`. Block-target extension `self` is
                // rewritten to the target spelling before splice, so it no longer says `self`.
                if (className.BoundSymbol is { } bound
                    && request.DeclaringClass is { } declaring
                    && !ReferenceEquals(bound, declaring))
                {
                    return className;
                }

                return PhpNameAst.CreateFromContext(request.DeclaringClassFqn, (Base2Ast)request.CallSite);
            }

            if (string.Equals(text, "parent", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(request.ParentClassFqn))
            {
                return PhpNameAst.CreateFromContext(request.ParentClassFqn, (Base2Ast)request.CallSite);
            }

            if (string.Equals(text, "static", StringComparison.OrdinalIgnoreCase))
            {
                if (substitutions.TryGetValue("this", out var receiver) && receiver is IExpression recv)
                {
                    return SpliceAst.Parenthesize(SpliceAst.Clone(recv), (Base2Ast)request.CallSite);
                }

                if (request.Receiver is IExpression explicitRecv)
                {
                    return SpliceAst.Parenthesize(
                        SpliceAst.Clone(explicitRecv), (Base2Ast)request.CallSite);
                }
            }

            return className;
        }

        private IExpression ReduceNested(IExpression expression, SpliceRequest outer)
        {
            const int maxIterations = 32;
            var current = expression;
            for (var i = 0; i < maxIterations; i++)
            {
                var spliced = false;
                current = (IExpression)AstWalker.TransformTree(
                    current,
                    node =>
                    {
                        // Nested reduction only inlines splice-owned callees (extension methods /
                        // operators / tyhpdef thin mappings). An ordinary class method with a
                        // coincidental single-`return` body — e.g. `plus` in `$left->plus($right)`
                        // after an extension `operator +` splice — must stay a real call so unused
                        // arguments are not dropped.
                        if (!TryGetSpliceableCall(node, out var nestedRequest))
                        {
                            return node;
                        }

                        nestedRequest = WithCallSite(nestedRequest, outer.CallSite, outer.HasStatementSlot);
                        var nested = TrySplice(nestedRequest);
                        if (!nested.Success || nested.Expression is null)
                        {
                            return node;
                        }

                        spliced = true;
                        return nested.Expression;
                    })!;

                if (!spliced)
                {
                    break;
                }
            }

            return current;
        }

        private static SpliceRequest WithCallSite(
            SpliceRequest nested,
            IBase2Ast callSite,
            bool hasStatementSlot) =>
            new()
            {
                Callee = nested.Callee,
                CallSite = callSite,
                Body = nested.Body,
                Parameters = nested.Parameters,
                Receiver = nested.Receiver,
                Arguments = nested.Arguments,
                HasPhpBacker = nested.HasPhpBacker,
                HasStatementSlot = hasStatementSlot,
                DeclaringClassFqn = nested.DeclaringClassFqn,
                ParentClassFqn = nested.ParentClassFqn,
                CalleeVisibility = nested.CalleeVisibility,
                DeclaringClass = nested.DeclaringClass,
            };

        private bool TryGetSpliceableCall(IBase2Ast node, out SpliceRequest request)
        {
            request = null!;
            if (node is not PhpDereferenceableAst deref || deref.Suffix is not PhpCallAst call)
            {
                return false;
            }

            var callee = deref.BoundSymbol
                ?? (deref.Base as PhpDereferenceableAst)?.BoundSymbol
                ?? (deref.Base as PhpNameAst)?.BoundSymbol;
            if (callee is null || !TryBuildRequestFromCallee(callee, deref, call, out request))
            {
                return false;
            }

            return true;
        }

        public static bool TryBuildRequestFromCallee(
            IBaseSymbol callee,
            PhpDereferenceableAst deref,
            PhpCallAst call,
            out SpliceRequest request)
        {
            request = null!;
            var spliceCallee = SpliceAst.ImplementationForSplice(callee);
            if (!IsSpliceOwnedCallee(spliceCallee))
            {
                return false;
            }

            var declaring = SpliceAst.DeclaringNodeOf(spliceCallee);
            if (declaring is null
                || !SpliceAst.TryGetSingleReturnExpression(declaring, out var body))
            {
                return false;
            }

            var owner = OwnerOf(spliceCallee);
            // `self` / `parent` / `static` in the spliced body mean the real enclosing type, not a
            // compiler-generated synthetic extension wrapper (tyhpdef class-body thin mapping or
            // standalone tyhpdef `extension Name { }`). See EffectiveOwnerOf.
            var spliceOwner = EffectiveOwnerOf(spliceCallee);
            IReadOnlyList<ParameterInfo> symbolParams = SpliceAst.ParametersOf(spliceCallee) ?? [];
            var firstIsThis = owner is { IsExtension: true }
                || symbolParams.FirstOrDefault() is { } first && SpliceAst.IsThisName(first.Name);
            var parameters = SpliceAst.ParametersFromSymbol(symbolParams, firstIsThis);

            IExpression? receiver = null;
            var args = new List<IExpression?>();
            if (deref.Base is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst } member
                && member.Base is IExpression recv)
            {
                receiver = recv;
            }

            if (call.Arguments is not null)
            {
                foreach (var arg in call.Arguments.GetAllNotNull())
                {
                    args.Add(arg.Expression);
                }
            }

            request = new SpliceRequest
            {
                Callee = spliceCallee,
                CallSite = deref,
                Body = body,
                Parameters = parameters,
                Receiver = receiver,
                Arguments = args,
                HasPhpBacker = SpliceAst.MemberHasPhpBacker(declaring, owner),
                HasStatementSlot = false,
                DeclaringClassFqn = spliceOwner?.FullyQualifiedName,
                ParentClassFqn = ParentFqn(spliceOwner),
                CalleeVisibility = SpliceAst.VisibilityOf(spliceCallee),
                DeclaringClass = spliceOwner,
            };
            return true;
        }

        /// <summary>
        /// True when the splice engine is allowed to own this callee: an extension method,
        /// tyhpdef class-body thin mapping, or extension operator. Ordinary class methods that
        /// happen to have a single <c>return</c> body are not splice-owned — nested reduction
        /// must not inline them.
        /// </summary>
        public static bool IsSpliceOwnedCallee(IBaseSymbol callee)
        {
            var spliceCallee = SpliceAst.ImplementationForSplice(callee);
            var declaring = SpliceAst.DeclaringNodeOf(spliceCallee);
            if (declaring is TyhpdefInlineExtensionFunctionAst)
            {
                return true;
            }

            if (declaring is TyhpOperatorOverloadAst op
                && (op.IsInlineExtension || op.IsExtensionOperator))
            {
                return true;
            }

            if (spliceCallee is ObjectOperatorOverloadMethodSymbol { IsExtensionOperator: true })
            {
                return true;
            }

            return OwnerOf(spliceCallee) is { IsExtension: true };
        }

        public static ObjectDeclarationSymbol? OwnerOf(IBaseSymbol symbol)
        {
            var scope = symbol.ContainingScope;
            while (scope is not null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        /// <summary>
        /// The type <c>self</c> / <c>parent</c> / <c>static</c> mean inside a spliced member's
        /// body. A class-body tyhpdef thin mapping (or standalone tyhpdef <c>extension Name { }</c>)
        /// lives on a compiler-generated synthetic extension symbol
        /// (<see cref="ObjectDeclarationSymbol.IsCompilerGenerated"/>) whose
        /// <see cref="ObjectDeclarationSymbol.InlineExtensionReceiverClass"/> is the real enclosing
        /// tyhpdef type — <c>self</c> must resolve to that receiver, never to the synthetic wrapper
        /// (see Phase 4 decision: "$this / self on a tyhpdef class-body thin member still mean the
        /// enclosing tyhpdef type").
        /// </summary>
        public static ObjectDeclarationSymbol? EffectiveOwnerOf(IBaseSymbol symbol)
        {
            var owner = OwnerOf(symbol);
            return owner is { IsCompilerGenerated: true, InlineExtensionReceiverClass: { } receiver }
                ? receiver
                : owner;
        }

        public static string? ParentFqn(ObjectDeclarationSymbol? owner)
        {
            var extends = owner?.ExtendsType;
            if (extends?.BoundSymbol is ObjectDeclarationSymbol parent)
            {
                return parent.FullyQualifiedName;
            }

            return extends switch
            {
                PhpNameAst name => name.ValueString,
                PhpNamedTypeAst named when named.Name is PhpNameAst n => n.ValueString,
                _ => extends?.Identifier,
            };
        }

        private static PhpBinaryOpAst CreateRefBind(
            PhpVariableAst temp,
            IExpression argument,
            IBase2Ast callSite)
        {
            var amp = TokenValueAst.CreateFromContext(
                "&", TyhpParser.T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG, (Base2Ast)callSite);
            var byRef = PhpUnaryOpAst.CreateFromContext(amp, argument, (Base2Ast)callSite);
            var assign = WithKeywordHelper.CreateAssignment(temp, byRef, (Base2Ast)callSite);
            SpliceAst.StampOriginalAst(assign, callSite);
            return assign;
        }

        private static IExpression ReplaceFirstUseWithBind(
            IExpression expression,
            string tempName,
            PhpBinaryOpAst bind)
        {
            var replaced = false;
            return (IExpression)AstWalker.TransformTree(
                expression,
                node =>
                {
                    if (replaced || node is not PhpVariableAst variable)
                    {
                        return node;
                    }

                    var name = CheckerHelpers.GetVariableName(variable);
                    if (!SpliceAst.NamesEqual(name, tempName))
                    {
                        return node;
                    }

                    replaced = true;
                    return SpliceAst.Parenthesize(bind, (Base2Ast)variable);
                })!;
        }

        private readonly record struct Hoist(
            string Name,
            IExpression Argument,
            bool ByRef,
            IBase2Ast CallSite);
    }
}
