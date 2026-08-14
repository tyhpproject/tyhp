using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder.Symbols.Interfaces {
    /// <summary>
    /// Core contract for symbols in the binder symbol table.
    /// </summary>
    public interface IBaseSymbol
    {
        /// <summary>
        /// The declared symbol name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The namespace-qualified symbol name.
        /// </summary>
        string FullyQualifiedName { get; }

        /// <summary>
        /// Runtime discriminator of symbol kind.
        /// </summary>
        SymbolType SymbolType { get; }

        /// <summary>
        /// Parent scope that owns this symbol. Assigned only by <see cref="Tyhp.TyhpLang.Binder.Scopes.BaseScope.AddChildSymbol"/> or <see cref="Tyhp.TyhpLang.Binder.Scopes.BaseScope.AddChildScope"/>.
        /// </summary>
        IBaseScope? ContainingScope { get; }

        /// <summary>
        /// Source file that declared this symbol.
        /// </summary>
        string SourceFile { get; }

        /// <summary>
        /// Source line of declaration.
        /// </summary>
        int Line { get; }

        /// <summary>
        /// Source column of declaration.
        /// </summary>
        int Column { get; }
    }
}