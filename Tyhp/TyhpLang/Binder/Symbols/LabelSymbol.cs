using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class LabelSymbol :
        BaseSymbol
    {
        public LabelSymbol(
            string name,
            string? sourceFile = null
        )
            : base(name, SymbolType.Label, sourceFile: sourceFile ?? string.Empty)
        {
        }
    }
}