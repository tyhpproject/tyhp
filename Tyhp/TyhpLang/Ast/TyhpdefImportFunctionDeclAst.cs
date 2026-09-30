using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef function import declaration (function signature without body),
    /// overlay name-only <c>partial function name;</c> (attributes only; <see cref="IsPartial"/>),
    /// or name-only <c>extern function \bcadd;</c> (<see cref="IsExtern"/>).
    ///
    /// Grammar:
    ///   tyhpdefImportFunctionDeclarationStatement
    ///     : tyhpdefDeprecatedOrObsolete? IsFallback=T_TYHP_FALLBACK? IsAsync=T_TYHP_ASYNC? function
    ///         ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
    ///         FindDocComment=T_OPEN_ROUND_BRACE IsExtension=T_EXTENDS?
    ///         ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
    ///         T_SYM_SEMICOLON
    ///     ;
    ///   tyhpdefExternFunctionDeclarationStatement
    ///     : T_TYHPDEF_EXTERN function Identifier=name T_SYM_SEMICOLON
    ///     ;
    /// </summary>
    public class TyhpdefImportFunctionDeclAst : Base2Ast, IAttributedStatement
    {
        private const short RETURNS_REF_FLAG = -10;
        private const short IS_ASYNC_FLAG = -11;
        private const short IS_EXTENSION_FLAG = -12;
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;
        private const short IS_OMIT_FLAG = -22;
        private const short IS_PARTIAL_FLAG = -23;
        private const short IS_EXTERN_FLAG = -24;
        private const short IS_FALLBACK_FLAG = -25;

        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);
        public bool IsAsync => HasFlag(IS_ASYNC_FLAG);
        public bool IsExtension => HasFlag(IS_EXTENSION_FLAG);
        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);
        public bool IsOmit => HasFlag(IS_OMIT_FLAG);
        public bool IsPartial => HasFlag(IS_PARTIAL_FLAG);
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

        /// <summary>
        /// The function name (possibly with alias and/or generics).
        /// </summary>
        public IBase2Ast? NameOrAlias => Children.ElementAtOrDefault(0);

        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(1) as PhpParameterListAst;
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(2) as ITypeExpression;

        public static TyhpdefImportFunctionDeclAst Create(
            IBase2Ast nameOrAlias,
            bool returnsRef,
            bool isAsync,
            bool isExtension,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            bool isDeprecated,
            bool isObsolete,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null,
            bool isOmit = false,
            bool isPartial = false,
            bool isExtern = false,
            bool isFallback = false)
        {
            var result = new TyhpdefImportFunctionDeclAst
            {
                Identifier = !string.IsNullOrEmpty(nameOrAlias.Identifier)
                    ? nameOrAlias.Identifier
                    : (nameOrAlias.ValueString ?? ""),
                Children = [nameOrAlias, parameters, returnType],
                DocComment = docComment,
            };

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);
            result.SetFlag(IS_ASYNC_FLAG, isAsync);
            result.SetFlag(IS_EXTENSION_FLAG, isExtension);
            result.SetFlag(IS_DEPRECATED_FLAG, isDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, isObsolete);
            result.SetFlag(IS_OMIT_FLAG, isOmit);
            result.SetFlag(IS_PARTIAL_FLAG, isPartial);
            result.SetFlag(IS_EXTERN_FLAG, isExtern);
            result.SetFlag(IS_FALLBACK_FLAG, isFallback);
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Shallow copy of this signature (children and addons shared) so overlay
        /// attribute merge does not mutate a cached Layer 1 AST.
        /// </summary>
        internal TyhpdefImportFunctionDeclAst CloneSignature()
        {
            var result = new TyhpdefImportFunctionDeclAst
            {
                Identifier = Identifier,
                Children = [NameOrAlias, Parameters, ReturnType],
                DocComment = DocComment,
            };
            result.SetFlag(RETURNS_REF_FLAG, ReturnsRef);
            result.SetFlag(IS_ASYNC_FLAG, IsAsync);
            result.SetFlag(IS_EXTENSION_FLAG, IsExtension);
            result.SetFlag(IS_DEPRECATED_FLAG, IsDeprecated);
            result.SetFlag(IS_OBSOLETE_FLAG, IsObsolete);
            result.SetFlag(IS_OMIT_FLAG, IsOmit);
            result.SetFlag(IS_PARTIAL_FLAG, IsPartial);
            result.SetFlag(IS_EXTERN_FLAG, IsExtern);
            result.SetFlag(IS_FALLBACK_FLAG, IsFallback);
            result.SetContext(this);
            foreach (var addon in AstGrammarAddons)
            {
                result.AddGrammarAddon(addon.Key, addon.Value);
            }

            result.CopyAttributesFrom(this);
            return result;
        }
    }
}
