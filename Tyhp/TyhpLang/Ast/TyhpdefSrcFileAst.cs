
namespace Tyhp.TyhpLang.Ast {
    public class TyhpdefSrcFileAst : SrcFileAst
    {
        public static TyhpdefSrcFileAst Create(
            string fileName,
            string fileHash,
            IEnumerable<Interfaces.IBase2Ast>? startingInlineOutput = null,
            IEnumerable<Interfaces.IBase2Ast>? codeBlocks = null,
            IEnumerable<Interfaces.IBase2Ast>? endingInlineOutput = null
        )
        {
            return AbstractCreate<TyhpdefSrcFileAst>(
                fileName,
                fileHash,
                [
                    .. startingInlineOutput ?? [],
                    .. codeBlocks ?? [],
                    .. endingInlineOutput ?? [],
                ]
            );
        }
    }
}