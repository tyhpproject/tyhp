using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// The callable value of a source overload set (<c>foo(...)</c>, <c>$obj-&gt;m(...)</c>,
    /// <c>C::m(...)</c>, an object with an overloaded <c>__invoke</c>): an
    /// <see cref="IntersectionCheckedType"/> flagged <see cref="IntersectionCheckedType.IsOverloadSet"/>
    /// holding one callable facet per declared signature, in declaration order. The implementation
    /// signature is not part of it. Calling the value selects the first facet that accepts the
    /// arguments; assigning it to a callable type needs one facet that satisfies the target (the
    /// intersection-source rule of <see cref="TypeComparer"/>).
    /// </summary>
    internal static class CallableOverloadSet
    {
        /// <summary>True when the callable value of <paramref name="function"/> is an overload set.</summary>
        public static bool AppliesTo(Binder.Symbols.FunctionDeclarationSymbol function) =>
            SourceOverloadSelector.IsSourceSet(function);

        /// <summary>True when the callable value of <paramref name="method"/> is an overload set.</summary>
        public static bool AppliesTo(Binder.Symbols.ObjectMethodSymbol method) =>
            SourceOverloadSelector.IsSourceSet(method);

        /// <summary>
        /// Combines the callable built for each declared signature (a facet, or an intersection of
        /// arity siblings when the signature has defaults) into one overload-set value. A single
        /// signature is returned as is.
        /// </summary>
        public static ICheckedType Combine(IReadOnlyList<ICheckedType> perSignature)
        {
            var facets = new List<ICheckedType>();
            foreach (var callable in perSignature)
            {
                if (callable is IntersectionCheckedType nested)
                {
                    facets.AddRange(nested.Members);
                }
                else
                {
                    facets.Add(callable);
                }
            }

            var distinct = new List<ICheckedType>();
            foreach (var facet in facets)
            {
                if (!distinct.Any(existing => CheckedTypes.AreTypesEqual(existing, facet)))
                {
                    distinct.Add(facet);
                }
            }

            return distinct.Count switch
            {
                0 => CheckedTypes.Unresolved,
                1 => distinct[0],
                _ => new IntersectionCheckedType(distinct) { IsOverloadSet = true },
            };
        }

        /// <summary>True when <paramref name="type"/> is (or is a Closure over) an overload-set value.</summary>
        public static bool IsOverloadSet(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type switch
            {
                IntersectionCheckedType { IsOverloadSet: true } => true,
                GenericCheckedType { TypeArguments.Count: > 0 } generic
                    when CallableArityFacetBuilder.IsClosureTypeName(generic.BaseType) =>
                    IsOverloadSet(generic.TypeArguments[0]),
                _ => false,
            };
        }

        /// <summary>
        /// The first facet, in declaration order, that accepts <paramref name="arguments"/> (arity,
        /// named arguments, spreads, and argument types). Null when none does.
        /// </summary>
        public static CallableCheckedType? Select(
            ICheckedType type,
            PhpArgumentListAst? arguments,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType)
        {
            var args = arguments?.GetAllNotNull().ToList() ?? [];
            foreach (var facet in CallableArityFacetBuilder.GetCallableFacets(type))
            {
                if (SourceOverloadSelector.FacetAccepts(
                        facet, args, state, symbolTree, globalScope, inferArgumentType))
                {
                    return facet;
                }
            }

            return null;
        }

        /// <summary>Human-readable facet list for the no-match diagnostic.</summary>
        public static string Describe(ICheckedType type) =>
            string.Join("; ", CallableArityFacetBuilder.GetCallableFacets(type).Select(f => f.DisplayName));
    }
}
