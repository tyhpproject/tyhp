using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectMagicSetStateMethodSymbol :
        ObjectMethodSymbol
    {
        public override bool CanBeInstance => false;
        
        public ObjectMagicSetStateMethodSymbol(string name, string? sourceFile = null)
            : base(name, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectMagicSetStateMethod)
        {
        }
    }
}