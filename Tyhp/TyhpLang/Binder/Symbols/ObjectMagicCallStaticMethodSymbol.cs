using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectMagicCallStaticMethodSymbol :
        ObjectMethodSymbol
    {
        public override bool CanBeInstance => false;
        
        public ObjectMagicCallStaticMethodSymbol(string name, string? sourceFile = null)
            : base(name, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectMagicCallStaticMethod)
        {
        }
    }
}