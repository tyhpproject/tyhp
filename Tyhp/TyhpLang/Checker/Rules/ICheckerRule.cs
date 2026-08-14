using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// A single category of checker validation. Rules are registered with
    /// <see cref="CheckerRuleRegistry"/> and invoked during AST traversal.
    /// </summary>
    public interface ICheckerRule
    {
        /// <summary>AST node types this rule handles (registry dispatch index).</summary>
        IEnumerable<Type> HandledNodeTypes { get; }

        /// <summary>Optional per-node refinement before <see cref="Check"/> is invoked.</summary>
        bool Handles(IBase2Ast node) => true;

        /// <summary>
        /// When true, the default child traversal is suppressed because this rule manages scope/flow itself.
        /// </summary>
        bool SuppressChildTraversal(IBase2Ast node) => false;

        void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics);
    }
}
