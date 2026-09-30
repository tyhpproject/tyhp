using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Marks a <c>.tyhp</c> declaration written with the <c>fallback</c> keyword. The declaration
    /// stays an ordinary function / class / interface / trait / enum / const list AST; the marker
    /// only tells the emitter to wrap it in a runtime existence check.
    /// </summary>
    public static class FallbackDeclaration
    {
        private const string GrammarAddonKey = "isFallback";

        public static bool IsFallback(IBase2Ast? node)
            => node is not null && node.AstGrammarAddons.ContainsKey(GrammarAddonKey);

        public static TAst MarkFallback<TAst>(this TAst node, IToken token, ParserRuleContext context)
            where TAst : IBase2Ast
        {
            node.AddGrammarAddon(GrammarAddonKey, TokenValueAst.Create(token, context));
            return node;
        }
    }
}
