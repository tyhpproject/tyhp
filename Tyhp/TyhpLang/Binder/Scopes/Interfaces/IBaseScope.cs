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

        /// <summary>
        /// Removes <paramref name="symbol"/> from this scope's child-symbol list and name indexes,
        /// plus any child scopes whose declaration symbol is that instance.
        /// </summary>
        bool TryRemoveChildSymbol(IBaseSymbol symbol);

        /// <summary>
        /// Registers an extra lookup name for an existing child without adding a second
        /// child or nested scope. Used for overlay <c>partial class Foo as Bar</c> so both
        /// Tyhp names resolve to the same type symbol.
        /// </summary>
        bool TryAddChildSymbolAlias(IBaseSymbol symbol, string aliasName);

        /// <summary>
        /// Removes a lookup name for a child without removing the child or its nested scopes.
        /// </summary>
        bool TryRemoveChildSymbolName(IBaseSymbol symbol, string name);

        /// <summary>
        /// Occupies this scope's PHP function namespace with <paramref name="symbol"/> so a later
        /// <c>function</c> of the same name collides. Used by source file-level type aliases
        /// (tyhpdef aliases stay type-only). Returns false when the name is already taken by a
        /// different symbol, including across sibling file/namespace-block scopes.
        /// </summary>
        bool TryOccupyFunctionNamespace(IBaseSymbol symbol);
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

        /// <summary>
        /// Same as <see cref="AddChildSymbol"/>, but on a name clash
        /// <paramref name="existing"/> is the symbol already occupying that name
        /// (including a cross-file hit). Null when insertion failed for another reason.
        /// </summary>
        bool TryAddChildSymbol(TChildSymbols child, out TChildSymbols? existing);
    }
}