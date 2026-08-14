using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class SuperGlobalSymbol :
        BaseSymbol,
        IGlobalScopeSymbol
    {
        public SuperGlobalSymbol(string name)
            : base(name, SymbolType.Variable)
        {
        }
    }
}