using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Scopes.Interfaces {

    public interface IBaseScope
    {
        IBaseScope? ParentScope { get; }
        IBaseSymbol? DeclarationSymbol { get; }

        /// <summary>
        /// Looks up a child symbol by name (case-insensitive). Returns null if not found.
        /// </summary>
        IBaseSymbol? FindChildSymbolByName(string name);

        /// <summary>
        /// Returns all child symbols in this scope.
        /// </summary>
        IEnumerable<IBaseSymbol> GetAllChildSymbols();

        /// <summary>
        /// Returns all child scopes in this scope as non-generic <see cref="IBaseScope"/> references.
        /// </summary>
        IEnumerable<IBaseScope> GetAllChildScopes();
    }

    public interface IBaseScope<TParent> : IBaseScope
    {
        TParent? Parent { get; set; }
    }

    public interface IBaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf> : IBaseScope<TParent>
        where TDeclarationSymbol : IBaseSymbol
        where TParent : class?, IBaseScope?
        where TSelf : class, IBaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>
        where TChildScopes : class, IBaseScope<TSelf>
        where TChildSymbols : IBaseSymbol
    {
        new TDeclarationSymbol? DeclarationSymbol { get; }
        IReadOnlyList<TChildScopes> ChildScopes { get; }
        IReadOnlyList<TChildSymbols> ChildSymbols { get; }

        TSelf AddChildScope(TChildScopes child);
        bool AddChildSymbol(TChildSymbols child);
    }
}