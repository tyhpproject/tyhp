using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class NoSymbol :
        BaseSymbol,
        INoScopeSymbols
    {
        public NoSymbol()
            : base("NoSymbol", SymbolType.Root)
        {
        }
    }
}