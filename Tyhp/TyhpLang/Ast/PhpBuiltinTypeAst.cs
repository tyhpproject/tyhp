using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpBuiltinTypeAst : Base2Ast, ITypeExpression
    {
        public static PhpBuiltinTypeAst Create(string typeName, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpBuiltinTypeAst
            {
                Identifier = typeName
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>Creates a builtin type anchored to an existing AST node (for synthesized type expressions).</summary>
        public static PhpBuiltinTypeAst Create(string typeName, IBase2Ast anchor, string? languageMode = null)
        {
            var result = new PhpBuiltinTypeAst
            {
                Identifier = typeName
            };
            if (anchor is Base2Ast baseAnchor)
            {
                result.SetContext(baseAnchor);
                result.LanguageMode = languageMode ?? baseAnchor.LanguageMode;
            }

            return result;
        }
    }
} 