using System.Collections.Generic;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class TypeAliasSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol,
        ICodeBlockScopeSymbol
    {
        public ITypeExpression? AliasedType { get; internal set; }

        public List<GenericTypeParameterSymbol> GenericParameters { get; internal set; }

        /// <summary>
        /// Creates a source-level type alias with file metadata only.
        /// </summary>
        /// <param name="name">Declared type alias name.</param>
        /// <param name="sourceFile">Source filename for the alias declaration.</param>
        /// <param name="symbolType">Resolved symbol type.</param>
        public TypeAliasSymbol(
            string name,
            string? sourceFile = null,
            SymbolType symbolType = SymbolType.TypeAlias
        )
            : this(name, declaringNode: null, sourceFile: sourceFile, symbolType: symbolType)
        {
        }

        /// <summary>
        /// Creates a source-level type alias with full AST metadata.
        /// </summary>
        /// <param name="name">Declared type alias name.</param>
        /// <param name="declaringNode">Optional AST node that declared this alias.</param>
        /// <param name="sourceFile">Source filename for the alias declaration.</param>
        /// <param name="visibility">Visibility modifier applied to the alias.</param>
        /// <param name="symbolType">Resolved symbol type.</param>
        public TypeAliasSymbol(
            string name,
            IBase2Ast? declaringNode,
            string? sourceFile = null,
            MemberModifier visibility = MemberModifier.None,
            SymbolType symbolType = SymbolType.TypeAlias
        )
            : base(name, symbolType, declaringNode, sourceFile: sourceFile ?? string.Empty, visibility: visibility)
        {
            this.GenericParameters = new List<GenericTypeParameterSymbol>();
        }
    }
}