using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast {
    public class UnexpectedNodeAst : Base2Ast, IExpression, IAttributedStatement, ISrcElement, ITypeExpression
    {
        public Antlr4.Runtime.ParserRuleContext? Context { get; protected set; }

        public static UnexpectedNodeAst Create(
            Antlr4.Runtime.ParserRuleContext context
        )
        {
            return new UnexpectedNodeAst()
            {
                Context = context,
            };
        }
    }
}