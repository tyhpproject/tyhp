using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Assignability rules for symbol-name types, including literal existence verification (Story 08.5).
    /// </summary>
    internal static class SymbolNameTypeAssignability
    {
        public static bool IsAssignableTo(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            CheckerState? state = null)
        {
            if (SymbolNameTypeHelper.IsSymbolNameType(target) && source.Kind == CheckedTypeKind.Unresolved)
            {
                return false;
            }

            if (TypeComparer.IsAssignableTo(source, target, symbolTree, globalScope))
            {
                return true;
            }

            if (SymbolNameTypeHelper.IsTyhpInternal(target))
            {
                return false;
            }

            if (SymbolNameTypeHelper.TryGetStringLiteral(source, out var literal)
                && SymbolNameTypeHelper.IsSymbolNameType(target)
                && state is not null
                && SymbolNameExistenceVerifier.VerifyLiteral(literal, target, state, symbolTree, globalScope))
            {
                return true;
            }

            if (SymbolNameTypeHelper.IsSymbolNameType(source))
            {
                if (SymbolNameTypeHelper.IsErasureAssignable(source, target, globalScope))
                {
                    return true;
                }

                var erasure = SymbolNameTypeHelper.GetFullErasure(source, globalScope);
                if (TypeComparer.IsAssignableTo(erasure, target, symbolTree, globalScope))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryReportLiteralExistenceFailure(
            ICheckedType source,
            ICheckedType target,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            IBase2Ast reportNode)
        {
            if (!SymbolNameTypeHelper.TryGetStringLiteral(source, out var literal)
                || !SymbolNameTypeHelper.IsSymbolNameType(target))
            {
                return false;
            }

            if (SymbolNameExistenceVerifier.VerifyLiteral(literal, target, state, symbolTree, globalScope))
            {
                return false;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                reportNode,
                MessageCode.CheckerSymbolNameNotFound,
                literal,
                target.DisplayName);
            return true;
        }
    }
}
