using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class DeclareBlockSymbol :
        CodeBlockSymbol
    {
        public Dictionary<string, string> Directives { get; protected set; }

        public DeclareBlockSymbol(
            string name,
            string? sourceFile = null
        )
            : base(name, blockType: ScopeType.DeclareBlock, sourceFile: sourceFile ?? string.Empty)
        {
            this.Directives = new Dictionary<string, string>();
        }
    }
}