using Tyhp.Domain.Services;

namespace Tyhp.TyhpLang.Ast {
    public abstract class SrcFileAst : Base2Ast
    {
        public byte[] FileHash => [..(this.ValueString ?? "").Select(c => (byte)c)];
        public string FileName => AstCacheService.GetRelativePath(this.Identifier);

        protected static TType AbstractCreate<TType>(
            string fileName,
            string fileHash,
            IEnumerable<Interfaces.IBase2Ast> children
        ) where TType : SrcFileAst, new()
        {
            return new TType()
            {
                ValueString = fileHash,
                LanguageMode = "",
                Identifier = (fileName != "_" && !String.IsNullOrWhiteSpace(fileName)) ? Path.GetFullPath(fileName) : fileName,
                Line = -1,
                Column = -1,
                StartIndex = -1,
                Children = [.. children],
            };
        }
    }
}