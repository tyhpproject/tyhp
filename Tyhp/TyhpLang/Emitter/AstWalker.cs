using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Emitter
{
    public static class AstWalker
    {
        public static void WalkStatements(IEnumerable<ITopStatement> statements, Action<IBase2Ast> visit)
        {
            foreach (var statement in statements)
            {
                if (statement is IBase2Ast ast)
                {
                    Walk(ast, visit);
                }
            }
        }

        public static void Walk(IBase2Ast root, Action<IBase2Ast> visit)
        {
            visit(root);
            foreach (var child in root.AstChildren)
            {
                if (child != null)
                {
                    Walk(child, visit);
                }
            }
        }

        public static IBase2Ast TransformTree(
            IBase2Ast node,
            Func<IBase2Ast, IBase2Ast?> transform,
            Func<IBase2Ast, IBase2Ast?>? preTransform = null)
        {
            if (node is not Base2Ast baseNode)
            {
                return node;
            }

            if (preTransform?.Invoke(node) is IBase2Ast preRewritten)
            {
                return preRewritten;
            }

            for (var i = 0; i < baseNode.AstChildren.Count; i++)
            {
                var child = baseNode.AstChildren[i];
                if (child == null)
                {
                    continue;
                }

                var transformedChild = TransformTree(child, transform, preTransform);
                baseNode.ReplaceChild(child, transformedChild);
            }

            return transform(node) ?? node;
        }
    }
}
