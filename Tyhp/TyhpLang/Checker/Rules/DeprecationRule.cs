using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Reports warnings/errors when deprecated or obsolete symbols are referenced.
    /// </summary>
    public sealed class DeprecationRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpNameAst),
            typeof(PhpDereferenceableAst),
        ];

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpNameAst name:
                    ReportSymbolDeprecation(
                        ResolveNameSymbol(name, state, context),
                        name,
                        state,
                        diagnostics);
                    break;
                case PhpDereferenceableAst deref:
                    ReportSymbolDeprecation(
                        ResolveDereferenceableSymbol(deref, state, context, diagnostics),
                        deref,
                        state,
                        diagnostics);
                    break;
            }
        }

        private static IBaseSymbol? ResolveNameSymbol(
            PhpNameAst name,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (name.BoundSymbol is not null)
            {
                return name.BoundSymbol;
            }

            // Call-site free constants are often unbound by the binder.
            return CheckerHelpers.ResolveFreeConstant(
                name, state, context.SymbolTree, context.GlobalScope);
        }

        private static IBaseSymbol? ResolveDereferenceableSymbol(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (deref.Suffix is PhpCallAst && deref.Base is PhpNameAst name)
            {
                return name.BoundSymbol;
            }

            if (deref.Suffix is PhpClassConstantAccessAst classConst)
            {
                var memberName = classConst.Member?.ValueString ?? classConst.Member?.Identifier;
                if (!string.IsNullOrEmpty(memberName))
                {
                    var receiverNode = deref.Base as IBase2Ast ?? deref;
                    var receiverType = CheckerHelpers.ResolveInstanceofTargetType(
                        receiverNode,
                        state,
                        context,
                        context.SymbolTree,
                        context.GlobalScope);
                    var owner = CheckerHelpers.TryGetObjectDeclaration(receiverType);
                    if (owner is not null
                        && context.SymbolTree.ResolveConstant(memberName, owner, diagnostics)
                            is ObjectConstantSymbol constant)
                    {
                        return constant;
                    }
                }

                if (classConst.Member?.BoundSymbol is not null)
                {
                    return classConst.Member.BoundSymbol;
                }
            }

            return deref.BoundSymbol ?? (deref.Base as PhpNameAst)?.BoundSymbol;
        }

        private static void ReportSymbolDeprecation(
            IBaseSymbol? symbol,
            IBase2Ast node,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (symbol is not BaseSymbol baseSymbol)
            {
                return;
            }

            if (baseSymbol.IsObsolete)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, node, MessageCode.CheckerObsoleteUsage, baseSymbol.Name);
                return;
            }

            if (baseSymbol.IsDeprecated)
            {
                // WARNING_TYHP4500 is `{0}` is deprecated{1}. {1} is empty when there is no
                // literal #[\Deprecated] $message so existing one-arg wording is preserved.
                var messageSuffix = string.IsNullOrEmpty(baseSymbol.DeprecatedMessage)
                    ? string.Empty
                    : ": " + baseSymbol.DeprecatedMessage;
                CheckerHelpers.ReportWarning(
                    diagnostics,
                    state,
                    node,
                    MessageCode.CheckerDeprecatedUsage,
                    baseSymbol.Name,
                    messageSuffix);
            }
        }
    }
}
