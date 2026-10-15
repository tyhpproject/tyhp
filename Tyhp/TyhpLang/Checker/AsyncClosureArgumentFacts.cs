using System.Runtime.CompilerServices;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// A call argument whose value is a variable currently holding an <c>async</c> closure.
    /// The emitter binds <c>callable(): T</c> from the closure's declared return type instead of
    /// <c>Type::fromCallableReturn</c>, which would see <c>\Tyhp\Promise</c>.
    /// </summary>
    internal static class AsyncClosureArgumentFacts
    {
        private static readonly ConditionalWeakTable<IBase2Ast, Entry> Arguments = new();

        public static void Remember(IBase2Ast argument, ITypeExpression? returnType) =>
            Arguments.AddOrUpdate(argument, new Entry(returnType));

        public static bool TryGet(IBase2Ast argument, out ITypeExpression? returnType)
        {
            if (Arguments.TryGetValue(argument, out var entry))
            {
                returnType = entry.ReturnType;
                return true;
            }

            returnType = null;
            return false;
        }

        private sealed class Entry
        {
            public Entry(ITypeExpression? returnType) => ReturnType = returnType;

            public ITypeExpression? ReturnType { get; }
        }
    }
}
