using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Turns the <c>declare(php=…)</c> / <c>#[\Tyhp\Php]</c> / <c>declare(ext=…)</c> gates the binder
    /// recorded on a declaration into PHP conditions. <c>output.phpVersion</c> is the oldest PHP the
    /// build runs on, so a php gate that holds for every version at or above it needs no check, and
    /// one that holds for only some versions becomes a <c>\PHP_VERSION_ID</c> comparison. Extension
    /// gates always become <c>\extension_loaded</c> checks.
    /// </summary>
    internal static class RuntimeGateEmission
    {
        /// <summary>
        /// Conditions that must all hold at runtime for <paramref name="node"/> to be declared,
        /// outermost first. Empty when the declaration is unconditional.
        /// </summary>
        public static IReadOnlyList<string> GetConditions(IBase2Ast node, string? minimumPhpVersion)
        {
            if (node.BoundSymbol is not BaseSymbol symbol)
            {
                return [];
            }

            var conditions = new List<string>();

            if (!symbol.HasUncompiledVersionVariants)
            {
                var gate = PhpVersionConstraint.ClassifyAtOrAbove(symbol.EffectivePhpVersionConstraints, minimumPhpVersion);
                if (gate.Kind == PhpRuntimeGateKind.Conditional && gate.Expression is { Length: > 0 } expression)
                {
                    conditions.Add(expression);
                }
            }

            foreach (var spec in symbol.EffectiveExtGates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddExtCondition(conditions, spec);
            }

            return conditions;
        }

        /// <summary>
        /// Conditions for the statements of one <c>declare(php=…)</c> / <c>declare(ext=…)</c> block
        /// inside a function body. Only the block's own gates count: the enclosing declaration already
        /// carries the outer ones.
        /// </summary>
        public static IReadOnlyList<string> GetBlockConditions(DeclareBlockSymbol block, string? minimumPhpVersion)
        {
            var conditions = new List<string>();

            if (block.PhpVersionConstraint is { Length: > 0 } constraint)
            {
                var gate = PhpVersionConstraint.ClassifyAtOrAbove([constraint], minimumPhpVersion);
                if (gate.Kind == PhpRuntimeGateKind.Conditional && gate.Expression is { Length: > 0 } expression)
                {
                    conditions.Add(expression);
                }
            }

            if (block.ExtGate is { Length: > 0 } spec)
            {
                AddExtCondition(conditions, spec);
            }

            return conditions;
        }

        /// <summary>
        /// True when <paramref name="first"/> is gated by <c>ext="x"</c> and <paramref name="second"/>
        /// by <c>ext="!x"</c> (or the reverse): exactly one of them holds at runtime, so adjacent
        /// blocks can share one <c>if … else …</c>.
        /// </summary>
        public static bool AreComplementaryExtGates(DeclareBlockSymbol first, DeclareBlockSymbol second)
        {
            if (first.ExtGate is not { Length: > 0 } a
                || second.ExtGate is not { Length: > 0 } b
                || first.PhpVersionConstraint is not null
                || second.PhpVersionConstraint is not null)
            {
                return false;
            }

            var negatedA = a.StartsWith('!');
            var negatedB = b.StartsWith('!');
            if (negatedA == negatedB)
            {
                return false;
            }

            var nameA = (negatedA ? a[1..] : a).Trim();
            var nameB = (negatedB ? b[1..] : b).Trim();
            return string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddExtCondition(List<string> conditions, string spec)
        {
            var negative = spec.StartsWith('!');
            var name = (negative ? spec[1..] : spec).Trim();
            if (name.Length == 0)
            {
                return;
            }

            var call = $"\\extension_loaded('{name}')";
            conditions.Add(negative ? "!" + call : call);
        }
    }
}
