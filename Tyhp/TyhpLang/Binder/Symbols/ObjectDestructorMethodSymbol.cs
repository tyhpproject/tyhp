using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectDestructorMethodSymbol :
        ObjectMethodSymbol
    {
        public override bool CanBeStatic => false;
        
        public ObjectDestructorMethodSymbol(string name, IBase2Ast? declaringNode = null, string sourceFile = "")
            : base(name, declaringNode: declaringNode, sourceFile: sourceFile, visibility: MemberModifier.None, symbolType: SymbolType.ObjectDestructor)
        {
        }
    }
}