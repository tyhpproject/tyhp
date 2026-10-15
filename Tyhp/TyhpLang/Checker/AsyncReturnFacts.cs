using System.Runtime.CompilerServices;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Facts the checker learns about an <c>async</c> callable's body that the emitter needs. An
    /// async callable hands the runtime a closure whose PHP return type is the settled type
    /// <c>__Awaited&lt;R&gt;</c>; when a <c>return</c> supplies a promise (adopted by the runtime) that
    /// typehint would reject the promise, so the emitter leaves the inner closure untyped.
    /// Keyed weakly by the declaring AST node, so entries live exactly as long as the tree.
    /// </summary>
    internal static class AsyncReturnFacts
    {
        private static readonly ConditionalWeakTable<IBase2Ast, object> ReturnsPromiseNodes = new();

        private static readonly object Marker = new();

        public static void MarkReturnsPromise(IBase2Ast owner) =>
            ReturnsPromiseNodes.AddOrUpdate(owner, Marker);

        /// <summary>True when a <c>return</c> in the async callable <paramref name="owner"/> supplies a promise.</summary>
        public static bool ReturnsPromise(IBase2Ast owner) =>
            ReturnsPromiseNodes.TryGetValue(owner, out _);
    }
}
