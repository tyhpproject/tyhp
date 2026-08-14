using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectMagicWakeupMethodSymbol :
        ObjectMethodSymbol
    {
        public override bool CanBeStatic => false;
        
        public ObjectMagicWakeupMethodSymbol(string name, string? sourceFile = null)
            : base(name, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectMagicWakeupMethod)
        {
        }
    }
}