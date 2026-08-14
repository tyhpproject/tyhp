namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Emitter-synthesized PHP expression fragment (not produced by the parser).
    /// Used when a rewrite cannot be expressed cleanly as ordinary AST nodes
    /// (e.g. anonymous-class <c>with</c> wrappers).
    /// </summary>
    public sealed class EmittedPhpExprAst : Base2Ast, Interfaces.IExpression
    {
        public string PhpText { get; }

        private EmittedPhpExprAst(string phpText)
        {
            this.PhpText = phpText;
        }

        internal static EmittedPhpExprAst Create(string phpText, Base2Ast context)
        {
            var result = new EmittedPhpExprAst(phpText);
            result.SetContext(context);
            return result;
        }
    }
}
