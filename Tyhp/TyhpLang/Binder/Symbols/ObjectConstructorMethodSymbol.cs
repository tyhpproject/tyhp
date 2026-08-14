using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectConstructorMethodSymbol :
        ObjectMethodSymbol
    {
        public List<VariableSymbol> PromotedProperties { get; protected set; }

        public override bool CanBeStatic => false;

        public ObjectConstructorMethodSymbol(string name, string? sourceFile = null, IBase2Ast? declaringNode = null)
            : base(name, declaringNode: declaringNode, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectConstructor)
        {
            this.PromotedProperties = new List<VariableSymbol>();
        }
    }
}