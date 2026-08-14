using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpSrcFileAst : SrcFileAst
    {
        public static PhpSrcFileAst Create(
            string fileName,
            string fileHash,
            IEnumerable<ISrcElement?>? children)
        {
            return AbstractCreate<PhpSrcFileAst>(
                fileName,
                fileHash,
                (children ?? []).OfType<IBase2Ast>());
        }
    }
}
