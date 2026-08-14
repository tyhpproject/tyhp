using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpImportDeclAst : Base2Ast
    {
        public TokenValueAst? UseType => Children.ElementAtOrDefault(0) as TokenValueAst;
        
        public string? NamespaceName => ValueString;
                
        public static PhpImportDeclAst Create(TokenValueAst? useType, string? namespaceName, string? aliasedAs, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpImportDeclAst
            {
                Children = [useType],
                Identifier = aliasedAs ?? "",
                ValueString = namespaceName,
            };

            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpImportDeclAst CreateFromContext(
            string namespaceName,
            string? alias,
            TokenValueAst? useType,
            Base2Ast context)
        {
            var result = new PhpImportDeclAst
            {
                Children = [useType],
                Identifier = alias ?? "",
                ValueString = namespaceName,
            };
            result.SetContext(context);
            return result;
        }

        public void SetUseType(TokenValueAst? useType)
        {
            Children[0] = useType;
        }
    }
} 