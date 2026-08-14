using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class NamespaceSymbol :
        BaseSymbol
    {
        public NamespaceSymbol(string name)
            : base(name.TrimStart('\\').TrimEnd('\\'), SymbolType.Namespace)
        {
        }
    }
}