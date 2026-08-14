using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast {
    public static class Base2AstExtensions
    {
        public static TAst WithAttributes<TAst>(this TAst ast, PhpAttributeListAst? attributes)
            where TAst : IBase2Ast
        {
            ast.AddAttributes(attributes);
            return ast;
        }

        public static TAst WithGrammarAddon<TAst>(this TAst ast, string key, IBase2Ast? addon)
            where TAst : IBase2Ast
        {
            ast.AddGrammarAddon(key, addon);
            return ast;
        }
    }
}