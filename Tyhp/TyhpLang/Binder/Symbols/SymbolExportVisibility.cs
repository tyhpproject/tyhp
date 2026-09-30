using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    /// <summary>
    /// Public compiled-library <c>package.tyhpdef</c> omission for <c>private</c> and <c>internal</c>.
    /// Call this from library tyhpdef generation in place of a private-only skip.
    /// </summary>
    public static class SymbolExportVisibility
    {
        public static bool OmitFromPublicTyhpdef(IBaseSymbol symbol)
        {
            if (symbol is not BaseSymbol bound)
            {
                return false;
            }

            return (bound.Visibility & MemberModifier.Private) != 0 || bound.IsInternal;
        }
    }
}
