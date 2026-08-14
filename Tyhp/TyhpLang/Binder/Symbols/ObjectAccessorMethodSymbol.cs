using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectAccessorMethodSymbol :
        ObjectMethodSymbol
    {
        public AccessorType AccessorKind { get; internal set; }

        public ObjectPropertySymbol AssociatedProperty { get; internal set; }

        public ObjectAccessorMethodSymbol(
            string name,
            ObjectPropertySymbol associatedProperty,
            AccessorType accessorKind,
            IBase2Ast? declaringNode = null,
            string? sourceFile = null,
            MemberModifier visibility = MemberModifier.None,
            SymbolType symbolType = SymbolType.InstanceObjectAccessorMethod
        )
            : base(name, declaringNode: declaringNode, sourceFile: sourceFile ?? string.Empty, visibility: visibility, symbolType: symbolType)
        {
            this.AssociatedProperty = associatedProperty;
            this.AccessorKind = accessorKind;
        }
    }
}