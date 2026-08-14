using System;

using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class CodeBlockSymbol :
        BaseSymbol
    {
        public ScopeType BlockType { get; internal set; }

        public CodeBlockSymbol(
            string name,
            ScopeType blockType = ScopeType.CodeBlock,
            string? sourceFile = null
        )
            : base(name, ResolveSymbolType(blockType), sourceFile: sourceFile ?? string.Empty)
        {
            this.BlockType = blockType;
        }

        private static SymbolType ResolveSymbolType(ScopeType blockType)
        {
            return blockType switch
            {
                ScopeType.CodeBlock => SymbolType.CodeBlock,
                ScopeType.DeclareBlock => SymbolType.DeclareBlock,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(blockType),
                    blockType,
                    "Unsupported block type for CodeBlockSymbol."
                ),
            };
        }
    }
}