using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpDereferenceableAst : Base2Ast, IDereferenceableBase
    {
        
        public IDereferenceableBase? Base => Children.ElementAtOrDefault(0) as IDereferenceableBase;
        public IDereferenceableSuffix? Suffix => Children.ElementAtOrDefault(1) as IDereferenceableSuffix;
        
        public static PhpDereferenceableAst Create(IDereferenceableBase dereferenceableBase, IDereferenceableSuffix? dereferenceableSuffix, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpDereferenceableAst
            {
                Children = [ dereferenceableBase, dereferenceableSuffix ],
            };

            result.SetContext(context, languageMode);

            return result;
        }

        internal static PhpDereferenceableAst CreateFromContext(
            IDereferenceableBase dereferenceableBase,
            IDereferenceableSuffix? dereferenceableSuffix,
            Base2Ast context)
        {
            var result = new PhpDereferenceableAst
            {
                Children = [dereferenceableBase, dereferenceableSuffix],
            };
            result.SetContext(context);
            return result;
        }
    }
} 