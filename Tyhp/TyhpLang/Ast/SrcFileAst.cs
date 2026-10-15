using Tyhp.Domain.Services;

namespace Tyhp.TyhpLang.Ast {
    public abstract class SrcFileAst : Base2Ast
    {
        public byte[] FileHash => [..(this.ValueString ?? "").Select(c => (byte)c)];

        // Snapshotted on first read. GetRelativePath follows Project.Singleton, which other
        // threads may publish; recomputing on every read made binder and checker file names
        // diverge and dropped extension/import matches.
        private string? _fileName;

        public string FileName => _fileName ??= AstCacheService.GetRelativePath(this.Identifier);

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