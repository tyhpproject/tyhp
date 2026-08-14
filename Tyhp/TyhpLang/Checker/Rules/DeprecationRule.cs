using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
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
                    ReportSymbolDeprecation(name.BoundSymbol, name, state, diagnostics);
                    break;
                case PhpDereferenceableAst deref:
                    ReportSymbolDeprecation(ResolveDereferenceableSymbol(deref), deref, state, diagnostics);
                    break;
            }
        }

        private static IBaseSymbol? ResolveDereferenceableSymbol(PhpDereferenceableAst deref)
        {
            if (deref.Suffix is PhpCallAst && deref.Base is PhpNameAst name)
            {
                return name.BoundSymbol;
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
                CheckerHelpers.ReportWarning(
                    diagnostics, state, node, MessageCode.CheckerDeprecatedUsage, baseSymbol.Name);
            }
        }
    }
}
