using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Binder {
    public class SymbolIdentifier
    {
        public List<string> NamespacePath { get; set; } = new();
        public string? Name { get; set; }

        public SymbolIdentifier(List<string> nsPath, string? name = null)
        {
            this.NamespacePath = nsPath;
            this.Name = name;
        }
    }
}