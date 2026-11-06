using System.Collections.Generic;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Resets the state the binder and checker write onto AST nodes, so a tree that is bound
    /// again (a cached tyhpdef AST, or a language-server document that was not re-parsed) starts
    /// from the same state as a freshly parsed one.
    /// </summary>
    /// <remarks>
    /// Rebinding does not overwrite every node: a node the new bind skips or fails to resolve
    /// keeps its old <c>BoundSymbol</c>, which keeps the previous compilation's symbol graph
    /// reachable and lets the new bind pick up symbols from it.
    /// </remarks>
    internal static class AstBindState
    {
        /// <summary>
        /// Clears <c>BoundSymbol</c>, <c>SelectedOverload</c>, and <c>OwningFile</c> on
        /// <paramref name="root"/> and every node below it (children, attributes, and grammar
        /// addons).
        /// </summary>
        public static void Clear(IBase2Ast root)
        {
            var pending = new Stack<IBase2Ast>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                node.BoundSymbol = null;
                node.OwningFile = null;
                if (node is Base2Ast concrete)
                {
                    concrete.SelectedOverload = null;
                }

                foreach (var child in node.AstChildren)
                {
                    if (child != null)
                    {
                        pending.Push(child);
                    }
                }

                foreach (var attribute in node.AstAttributes)
                {
                    if (attribute != null)
                    {
                        pending.Push(attribute);
                    }
                }

                foreach (var addon in node.AstGrammarAddons.Values)
                {
                    if (addon != null)
                    {
                        pending.Push(addon);
                    }
                }
            }
        }
    }
}
