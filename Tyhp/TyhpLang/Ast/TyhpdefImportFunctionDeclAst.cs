using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a tyhpdef function import declaration (function signature without body).
    ///
    /// Grammar:
    ///   tyhpdefImportFunctionDeclarationStatement
    ///     : tyhpdefDeprecatedOrObsolete? IsAsync=T_TYHP_ASYNC? function
    ///         ReturnsRef=returnsRef Identifier=tyhpdefFunctionNameWithOptionalAlias
    ///         FindDocComment=T_OPEN_ROUND_BRACE IsExtension=T_EXTENDS?
    ///         ParameterList=parameterList T_CLOSE_ROUND_BRACE ReturnType=returnType
    ///         T_SYM_SEMICOLON
    ///     ;
    /// </summary>
    public class TyhpdefImportFunctionDeclAst : Base2Ast, IAttributedStatement
    {
        private const short RETURNS_REF_FLAG = -10;
        private const short IS_ASYNC_FLAG = -11;
        private const short IS_EXTENSION_FLAG = -12;
        private const short IS_DEPRECATED_FLAG = -20;
        private const short IS_OBSOLETE_FLAG = -21;

        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);
        public bool IsAsync => HasFlag(IS_ASYNC_FLAG);
        public bool IsExtension => HasFlag(IS_EXTENSION_FLAG);
        public bool IsDeprecated => HasFlag(IS_DEPRECATED_FLAG);
        public bool IsObsolete => HasFlag(IS_OBSOLETE_FLAG);

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
            string? languageMode = null)
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
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
