using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectMagicUnserializeMethodSymbol :
        ObjectMethodSymbol
    {
        public override bool CanBeStatic => false;
        
        public ObjectMagicUnserializeMethodSymbol(string name, string? sourceFile = null)
            : base(name, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectMagicUnserializeMethod)
        {
        }
    }
}