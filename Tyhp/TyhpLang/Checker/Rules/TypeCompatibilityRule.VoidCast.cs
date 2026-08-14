using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class TypeCompatibilityRule
    {
        /// <summary>
        /// PHP 8.5 <c>(void) expr</c>: type-checks the operand; the cast itself is void /
        /// non-value (inferred in <see cref="TypeInferrer"/>). Grammar keeps void cast out of
        /// value-producing expression positions; assignability still rejects void if it appears.
        /// </summary>
        private static void CheckVoidCast(
            PhpUnaryOpAst unary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (unary.Operand is null)
            {
                return;
            }

            // Force operand typing (call args, member access, etc.). Child CheckNode still walks
            // the operand; this ensures ResolveExpressionType has run even if a parent only
            // queried the void-cast node's type.
            _ = context.ResolveExpressionType(unary.Operand, state);
        }
    }
}
