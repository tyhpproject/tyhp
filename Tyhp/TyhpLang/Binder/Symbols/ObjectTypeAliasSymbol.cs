using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectTypeAliasSymbol :
        BaseSymbol,
        IObjectDeclarationScopeSymbol
    {
        public ITypeExpression? AliasedType { get; internal set; }

        public List<GenericTypeParameterSymbol> GenericParameters { get; internal set; }

        public ObjectTypeAliasSymbol(
            string name,
            IBase2Ast? declaringNode = null,
            string? sourceFile = null,
            MemberModifier visibility = MemberModifier.None
        )
            : base(name, SymbolType.ObjectTypeAlias, declaringNode, sourceFile: sourceFile ?? string.Empty, visibility: visibility)
        {
            this.GenericParameters = new List<GenericTypeParameterSymbol>();
        }
    }
}