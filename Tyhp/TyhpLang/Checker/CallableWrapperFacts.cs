using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// How one parameter of a first-class callable accepts more than its declared type: the
    /// sources that convert to it through <c>operator convert</c>.
    /// </summary>
    internal sealed class ConvertibleParameterPlan
    {
        /// <summary>Position among the callable's parameters (an extension receiver is excluded).</summary>
        public required int Index { get; init; }

        /// <summary>The declared parameter type, as the checker resolved it.</summary>
        public required ICheckedType Target { get; init; }

        /// <summary>The types that convert to <see cref="Target"/> (never empty).</summary>
        public required IReadOnlyList<ConvertibleSource> Sources { get; init; }

        /// <summary><see cref="Target"/> widened by <see cref="Sources"/>; <c>?T</c> stays nullable.</summary>
        public required ICheckedType WidenedType { get; init; }
    }

    /// <summary>
    /// What a first-class callable needs beyond a native <c>f(...)</c>: the parameters that accept
    /// a convertible source. The checker records it per call, the emitter wraps the callable when
    /// it is present, so both agree on which first-class callables are wrapped.
    /// </summary>
    internal sealed class CallableWrapperPlan
    {
        /// <summary>The function or method the callable refers to.</summary>
        public required IBaseSymbol Callee { get; init; }

        /// <summary>The callee's call-site parameters (an extension receiver is excluded).</summary>
        public required IReadOnlyList<ParameterInfo> CallSiteParameters { get; init; }

        /// <summary>The resolved type of every call-site parameter, same order.</summary>
        public required IReadOnlyList<ICheckedType> ParameterTypes { get; init; }

        /// <summary>
        /// The return type the wrapper declares, or null to declare none (an async or generator
        /// callee, a <c>static</c> return, an unresolved type).
        /// </summary>
        public ICheckedType? ReturnType { get; init; }

        /// <summary>The return type of the callable's shape (<c>mixed</c> when the callee declares none).</summary>
        public required ICheckedType ShapeReturnType { get; init; }

        /// <summary>The callee returns <c>void</c> or <c>never</c>: the wrapper is a full closure.</summary>
        public bool ReturnsNothing { get; init; }

        /// <summary>The callable is an extension method called on a receiver (no native first-class callable).</summary>
        public bool IsExtensionMethod { get; init; }

        /// <summary>The parameters that accept a convertible source (may be empty for an extension method).</summary>
        public required IReadOnlyList<ConvertibleParameterPlan> Parameters { get; init; }

        /// <summary>
        /// The type the wrapper declares for every call-site parameter, same order; null leaves
        /// the parameter untyped (its declared type mentions a type parameter the wrapper cannot spell).
        /// </summary>
        public required IReadOnlyList<ICheckedType?> WrapperParameterTypes { get; init; }

        /// <summary>
        /// The callee is a generic function or method: the wrapper is registered with
        /// <c>\Tyhp\Generic::wrapCallable</c> so binding the callable's type arguments still reaches the callee.
        /// </summary>
        public bool IsGenericCallee { get; init; }

        /// <summary>The callable is an invokable object, <c>$object(...)</c>: the callee is its <c>__invoke</c>.</summary>
        public bool IsInvokeCall { get; init; }

        /// <summary>
        /// The callable value's type when the callee is a source overload set: one facet per declared
        /// signature, each widened by the types that convert to its parameters. Null for a single signature.
        /// </summary>
        public ICheckedType? OverloadSetType { get; init; }

        public ConvertibleParameterPlan? ForIndex(int index)
        {
            foreach (var parameter in Parameters)
            {
                if (parameter.Index == index)
                {
                    return parameter;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Side table from a first-class-callable call node (the <c>(...)</c> suffix) to its
    /// <see cref="CallableWrapperPlan"/>. Set by the checker each pass; read by the emitter.
    /// </summary>
    internal static class CallableWrapperFacts
    {
        private static readonly ConditionalWeakTable<IBase2Ast, CallableWrapperPlan> Plans = new();

        /// <summary>Records <paramref name="plan"/> for the call, or clears a stale one when null.</summary>
        public static void Record(IBase2Ast call, CallableWrapperPlan? plan)
        {
            Plans.Remove(call);
            if (plan is { Parameters.Count: > 0 } or { IsExtensionMethod: true })
            {
                Plans.Add(call, plan);
            }
        }

        public static CallableWrapperPlan? Get(IBase2Ast? call) =>
            call is not null && Plans.TryGetValue(call, out var plan) ? plan : null;
    }
}
