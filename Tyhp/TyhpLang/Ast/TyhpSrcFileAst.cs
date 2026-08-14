
namespace Tyhp.TyhpLang.Ast {
    public class TyhpSrcFileAst : SrcFileAst
    {
        public static TyhpSrcFileAst Create(
            string fileName,
            string fileHash,
            IEnumerable<Interfaces.ISrcElement?>? startingInlineOutput = null,
            IEnumerable<Interfaces.ISrcElement?>? codeBlocks = null,
            IEnumerable<Interfaces.ISrcElement?>? endingInlineOutput = null
        )
        {
            return AbstractCreate<TyhpSrcFileAst>(
                fileName,
                fileHash,
                [
                    .. (startingInlineOutput ?? []).OfType<Interfaces.IBase2Ast>(),
                    .. (codeBlocks ?? []).OfType<Interfaces.IBase2Ast>(),
                    .. (endingInlineOutput ?? []).OfType<Interfaces.IBase2Ast>(),
                ]
            );
        }
    }
}