using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpBlockNamespaceDeclAst : Base2Ast, ITopStatement
    {
        public PhpTopStatementListAst? TopStatements => Children.ElementAtOrDefault(0) as PhpTopStatementListAst;
        
        public static PhpBlockNamespaceDeclAst Create(string? namespaceName, PhpTopStatementListAst topStatements, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpBlockNamespaceDeclAst
            {
                Identifier = namespaceName ?? "",
                Children = [topStatements],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpBlockNamespaceDeclAst CreateFromContext(string? namespaceName, Base2Ast context)
        {
            var result = new PhpBlockNamespaceDeclAst
            {
                Identifier = namespaceName ?? "",
                Children = [],
            };
            result.SetContext(context);
            return result;
        }
    }
} 