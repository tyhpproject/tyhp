using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectMagicSetMethodSymbol :
        ObjectMethodSymbol
    {
        public override bool CanBeStatic => false;
        
        public ObjectMagicSetMethodSymbol(string name, string? sourceFile = null)
            : base(name, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectMagicSetMethod)
        {
        }
    }
}