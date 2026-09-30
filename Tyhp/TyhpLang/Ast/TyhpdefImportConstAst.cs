using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef constant import declaration, or name-only
    /// <c>extern const \GMP_ROUND_PLUSINF;</c> (<see cref="IsExtern"/>).
    ///
    /// Grammar:
    ///   tyhpdefImportConstStatement
    ///     : tyhpdefDeprecatedOrObsolete? IsFallback=T_TYHP_FALLBACK? T_CONST TypeExpr=typeExprWithoutStatic
    ///         (AliasedIdentifier=tyhpdefIdentifierWithOptionalAlias | Identifier=name)
    ///         (T_COALESCE CoalesceExpr=expr)? FindDocComment=T_SYM_SEMICOLON
    ///     ;
    ///   tyhpdefExternConstDeclarationStatement
    ///     : T_TYHPDEF_EXTERN T_CONST Identifier=name T_SYM_SEMICOLON
    ///     ;
    /// </summary>
    public class TyhpdefImportConstAst : Base2Ast, IAttributedStatement
    {
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;
        private const short IS_OMIT_FLAG = -22;
        private const short IS_EXTERN_FLAG = -23;
        private const short IS_FALLBACK_FLAG = -24;

        public ITypeExpression? TypeExpr => Children.ElementAtOrDefault(0) as ITypeExpression;
        public IBase2Ast? NameOrAlias => Children.ElementAtOrDefault(1);
        public IExpression? CoalesceExpr => Children.ElementAtOrDefault(2) as IExpression;

        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);
        public bool IsOmit => HasFlag(IS_OMIT_FLAG);
        public bool IsExtern => HasFlag(IS_EXTERN_FLAG);
        public bool IsFallback => HasFlag(IS_FALLBACK_FLAG);

        /// <summary>
        /// Optional wrapper package from the immediately preceding
        /// <c>// @provided-by:</c> comment (trimmed). Null when absent.
        /// </summary>
        public string? ProvidedBy
            => AstGrammarAddons.TryGetValue(TyhpdefImportObjectDeclAst.ProvidedByAddonKey, out var node)
                && !string.IsNullOrEmpty(node.ValueString)
                    ? node.ValueString
                    : null;

        public static TyhpdefImportConstAst Create(
            ITypeExpression? typeExpr,
            IBase2Ast nameOrAlias,
            IExpression? coalesceExpr,
            bool isDeprecated,
            bool isObsolete,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null,
            bool isOmit = false,
            bool isExtern = false,
            bool isFallback = false)
        {
            var result = new TyhpdefImportConstAst
            {
                Identifier = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                    ? nameOrAlias.Identifier
                    : (nameOrAlias.ValueString ?? ""),
                Children = [typeExpr, nameOrAlias, coalesceExpr],
                DocComment = docComment,
            };

            result.SetFlag(IS_DEPRECATED_FLAG, isDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, isObsolete);
            result.SetFlag(IS_OMIT_FLAG, isOmit);
            result.SetFlag(IS_EXTERN_FLAG, isExtern);
            result.SetFlag(IS_FALLBACK_FLAG, isFallback);
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
