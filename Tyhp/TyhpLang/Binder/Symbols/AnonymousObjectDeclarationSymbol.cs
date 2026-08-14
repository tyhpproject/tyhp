using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using System;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class AnonymousObjectDeclarationSymbol :
        BaseSymbol
    {
        private List<GenericTypeParameterSymbol>? _genericParameters;
        private List<ITypeExpression>? _implementsTypes;
        private Dictionary<string, IBaseSymbol>? _members;

        public PhpTypeDeclType ObjectKind { get; protected set; }

        public bool IsStruct { get; protected set; }

        public bool IsExtension { get; protected set; }

        public List<GenericTypeParameterSymbol> GenericParameters
        {
            get => this._genericParameters ??= new List<GenericTypeParameterSymbol>();
            protected set => this._genericParameters = value;
        }

        public ITypeExpression? ExtendsType { get; protected set; }

        public List<ITypeExpression> ImplementsTypes
        {
            get => this._implementsTypes ??= new List<ITypeExpression>();
            protected set => this._implementsTypes = value;
        }

        /// <summary>
        /// Fast member lookup for name resolution inside an anonymous object declaration.
        /// Uses the same case-insensitive policy as named object declarations.
        /// </summary>
        public Dictionary<string, IBaseSymbol> Members
        {
            get => this._members ??= new Dictionary<string, IBaseSymbol>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);
            protected set => this._members = value;
        }

        public AnonymousObjectDeclarationSymbol(
            string name = "",
            IBase2Ast? declaringNode = null,
            string sourceFile = "",
            MemberModifier visibility = MemberModifier.None
        )
            : base(name, SymbolType.AnonymousObjectDeclaration, declaringNode, sourceFile, visibility)
        {
            this.ObjectKind = PhpTypeDeclType.Class;
        }
    }
}