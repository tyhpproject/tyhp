using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Resolves and caches <see cref="GenericTypeParameterSymbol.ResolvedConstraint"/> so
    /// <see cref="TypeComparer"/> can treat a constrained type parameter as a subtype of its bound.
    /// </summary>
    internal static class GenericConstraintResolver
    {
        public static void ResolveAll(
            IReadOnlyList<GenericTypeParameterSymbol> parameters,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (parameters.Count == 0)
            {
                return;
            }

            var visiting = new HashSet<GenericTypeParameterSymbol>();
            foreach (var parameter in parameters)
            {
                EnsureResolved(parameter, state, context, visiting);
            }
        }

        public static ICheckedType? EnsureResolved(
            GenericTypeParameterSymbol parameter,
            CheckerState state,
            CheckerRuleContext context,
            HashSet<GenericTypeParameterSymbol>? visiting = null)
        {
            if (parameter.Constraint is null)
            {
                return null;
            }

            if (parameter.ResolvedConstraint is not null)
            {
                return parameter.ResolvedConstraint;
            }

            visiting ??= [];
            // Cyclic constraints (`T extends U, U extends T`) erase to mixed for the upper bound.
            if (!visiting.Add(parameter))
            {
                return CheckedTypes.Mixed;
            }

            try
            {
                // Sibling type parameters in the constraint resolve as themselves (via Function/
                // ObjectGenerics), not as their bounds — substitution of the bound happens only when
                // TypeComparer asks whether the parameter is assignable to a target.
                parameter.ResolvedConstraint = context.ResolveTypeAnnotation(
                    parameter.Constraint, state);
                return parameter.ResolvedConstraint;
            }
            finally
            {
                visiting.Remove(parameter);
            }
        }
    }
}
