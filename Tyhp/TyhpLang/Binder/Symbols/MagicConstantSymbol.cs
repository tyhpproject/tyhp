using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class MagicConstantSymbol :
        BaseSymbol,
        IGlobalScopeSymbol
    {
        public MagicConstantSymbol(string name)
            : base(name, SymbolType.MagicConstant)
        {
        }
    }
}