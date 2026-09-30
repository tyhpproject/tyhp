using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter.Splice;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Tracks pass-by-reference assignments and call-site reference arguments.</summary>
    public sealed class ReferenceTrackingRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpBinaryOpAst),
            typeof(PhpDereferenceableAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => false;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpBinaryOpAst binary:
                    CheckReferenceAssignment(binary, state, diagnostics);
                    break;
                case PhpDereferenceableAst deref when deref.Suffix is PhpCallAst call:
                    CheckReferenceArguments(deref, call, state, context, diagnostics);
                    break;
            }
        }

        private static void CheckReferenceAssignment(
            PhpBinaryOpAst binary,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!string.Equals(binary.Operator?.ValueString, "=", StringComparison.Ordinal))
            {
                return;
            }

            if (binary.Left is not PhpVariableAst leftVar || binary.Right is not PhpVariableAst rightVar)
            {
                return;
            }

            var leftName = CheckerHelpers.GetVariableName(leftVar);
            var rightName = CheckerHelpers.GetVariableName(rightVar);
            if (leftName is null || rightName is null)
            {
                return;
            }

            if (!IsReferenceAssignment(binary.Right))
            {
                return;
            }

            var leftState = state.LookupVariable(leftName);
            var rightState = state.LookupVariable(rightName);
            if (leftState is not null && rightState is not null)
            {
                leftState.JoinReferenceGroup(rightState, leftName, rightName);
            }
        }

        private static bool IsReferenceAssignment(IExpression expression) =>
            expression is PhpUnaryOpAst { Operator.ValueString: "&" };

        /// <summary>
        /// An extension member's raw <see cref="ParameterInfo"/> list carries an implicit receiver
        /// as its first entry whenever the owning type is an extension (Tyhp <c>extension { }</c>
        /// declares it explicitly as <c>extends T $this</c>; a tyhpdef class-body thin mapping has
        /// none). Call-site arguments never include that receiver, so it must be excluded before
        /// matching positionally — mirrors <c>CallSiteSpliceEngine.TryBuildRequestFromCallee</c>.
        /// </summary>
        private static IReadOnlyList<ParameterInfo> ExtensionCallArguments(IBaseSymbol callee)
        {
            var rawParameters = SpliceAst.ParametersOf(callee) ?? [];
            var owner = CallSiteSpliceEngine.OwnerOf(callee);
            var firstIsThis = owner is { IsExtension: true }
                || (rawParameters.Count > 0 && SpliceAst.IsThisName(rawParameters[0].Name));
            return firstIsThis ? rawParameters.Skip(1).ToList() : rawParameters;
        }

        /// <summary>
        /// <c>extends T &amp;$this</c> splices the receiver into a PHP <c>&amp;</c> parameter.
        /// The receiver must be referenceable, same as any other by-reference argument (TYHP4180).
        /// </summary>
        private static void CheckByRefExtensionReceiver(
            IBaseSymbol callee,
            IExpression? receiver,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var rawParameters = SpliceAst.ParametersOf(callee) ?? [];
            var owner = CallSiteSpliceEngine.OwnerOf(callee);
            var firstIsThis = owner is { IsExtension: true }
                || (rawParameters.Count > 0 && SpliceAst.IsThisName(rawParameters[0].Name));
            if (!firstIsThis || rawParameters.Count == 0 || !rawParameters[0].IsByReference)
            {
                return;
            }

            if (SpliceAnalysis.IsReferenceableArgument(receiver, state, context))
            {
                return;
            }

            var report = receiver as IBase2Ast ?? SpliceAst.DeclaringNodeOf(callee);
            if (report is null)
            {
                return;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                report,
                MessageCode.CheckerNonReferenceableByRefArgument,
                SpliceAst.NormalizeName(rawParameters[0].Name));
        }

        private static void CheckReferenceArguments(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            IReadOnlyList<ParameterInfo>? parameters = null;
            if (deref.Base is PhpNameAst nameAst)
            {
                // Same call-site resolution as TypeCompatibilityRule.CheckCall: BoundSymbol is
                // null for free-function names inside bodies.
                if (CheckerHelpers.ResolveFreeFunction(
                        nameAst, state, context.SymbolTree, context.GlobalScope) is { } function)
                {
                    parameters = function.Parameters;
                }
            }
            else if (deref.Base is PhpDereferenceableAst chain
                && chain.Base is not null
                && chain.Suffix is PhpInstanceMemberAccessAst memberAccess)
            {
                var receiverType = context.ResolveExpressionType(chain.Base, state);
                var methodName = memberAccess.MemberName switch
                {
                    PhpNameAst name => name.ValueString,
                    TokenValueAst token => token.ValueString,
                    IExpression expr => expr.Identifier,
                    _ => memberAccess.MemberName?.Identifier,
                };
                if (methodName is not null
                    && CheckerHelpers.TryGetObjectDeclaration(receiverType) is { } objectDecl
                    && context.SymbolTree.ResolveMember(methodName, objectDecl, new DiagnosticBag())
                        is ObjectMethodSymbol method)
                {
                    parameters = method.Parameters;
                }
                else if (methodName is not null
                    // Not a plain member of the receiver's own type: the method may still be an
                    // extension (Tyhp `extension { }` or a tyhpdef class-body thin mapping), which
                    // this by-reference check must cover too (splicing a `&` parameter behaves
                    // exactly like a real by-reference call).
                    && InlineSpliceRule.ResolveExtensionCallee(deref, state, context) is { } extensionCallee)
                {
                    CheckByRefExtensionReceiver(
                        extensionCallee, chain.Base as IExpression, state, context, diagnostics);
                    parameters = ExtensionCallArguments(extensionCallee);
                }
            }

            if (parameters is null || call.Arguments is null)
            {
                return;
            }

            var positionalIndex = 0;
            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                if (arg.IsVariadic)
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
                    param = parameters[positionalIndex];
                    positionalIndex++;
                }

                if (param is not { IsByReference: true })
                {
                    continue;
                }

                if (!SpliceAnalysis.IsReferenceableArgument(arg.Expression, state, context))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        arg,
                        MessageCode.CheckerNonReferenceableByRefArgument,
                        SpliceAst.NormalizeName(param.Name));
                }
            }
        }
    }
}
