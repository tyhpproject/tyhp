using System.Collections.Generic;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class BuiltInTypeSymbol :
        BaseSymbol,
        IGlobalScopeSymbol,
        IObjectDeclarationScopeSymbol
    {
        // Lazy initialization via ??= is not thread-safe; the binder is assumed to be single-threaded.
        private List<ObjectOperatorOverloadMethodSymbol>? _extensionContributedOperators;

        /// <summary>
        /// When set, this built-in type is a Tyhp alias backed by another scalar or built-in type at runtime.
        /// For example, <c>decimal</c> is backed by <c>float</c> and <c>struct</c> is backed by <c>array</c>.
        /// </summary>
        public string? BackingTypeName { get; }

        /// <summary>
        /// Generic parameter metadata for built-in generic types such as <c>array</c>, <c>iterable</c>, and <c>callable</c>.
        /// </summary>
        public GenericParameterRequirements? GenericParameterRequirements { get; }

        /// <summary>
        /// Operator overload symbols contributed by standalone extensions for this built-in
        /// (e.g. <c>operator *&lt;string&gt;(self $left, int $right)</c>).
        /// </summary>
        public List<ObjectOperatorOverloadMethodSymbol> ExtensionContributedOperators
        {
            get => this._extensionContributedOperators ??= new List<ObjectOperatorOverloadMethodSymbol>();
            internal set => this._extensionContributedOperators = value;
        }

        public BuiltInTypeSymbol(string name)
            : this(name, backingTypeName: null, genericParameterRequirements: null)
        {
        }

        public BuiltInTypeSymbol(
            string name,
            string? backingTypeName,
            GenericParameterRequirements? genericParameterRequirements
        )
            : base(name, SymbolType.BuiltInType)
        {
            this.BackingTypeName = backingTypeName;
            this.GenericParameterRequirements = genericParameterRequirements;
        }
    }
}
