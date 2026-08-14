using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class NamespaceBlockSymbol :
        BaseSymbol
    {
        /// <summary>
        /// True when this namespace block is anonymous (no block namespace name).
        /// </summary>
        public bool IsAnonymous { get; protected set; }

        /// <summary>
        /// File-level scope that owns this namespace block.
        /// </summary>
        public FileScope? OwningFileScope { get; set; }

        public NamespaceBlockSymbol(string name = "", FileScope? owningFileScope = null)
            : base(name, SymbolType.NamespaceBlock)
        {
            this.IsAnonymous = string.IsNullOrWhiteSpace(name);
            this.OwningFileScope = owningFileScope;
        }

        public NamespaceBlockSymbol SetOwningFileScope(FileScope? owningFileScope)
        {
            this.OwningFileScope = owningFileScope;
            return this;
        }
    }
}