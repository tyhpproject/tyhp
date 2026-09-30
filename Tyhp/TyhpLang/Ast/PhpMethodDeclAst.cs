using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpMethodDeclAst : Base2Ast, IClassMember
    {
        private const short RETURNS_REF_FLAG = -14;
        private const short IS_SHORT_SYNTAX_FLAG = -15;
        private const short IS_OMIT_FLAG = -16;
        private const short IS_PARTIAL_FLAG = -17;

        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);
        public bool IsOmit => HasFlag(IS_OMIT_FLAG);
        public bool IsPartial => HasFlag(IS_PARTIAL_FLAG);
        /// <summary>
        /// True when the method was authored as <c>fn name(...) => expr;</c>
        /// (desugared at visit time to a <c>{ return expr; }</c> body), including
        /// tyhpdef class-body <c>extension fn</c> lowering.
        /// </summary>
        public bool IsShortSyntax => HasFlag(IS_SHORT_SYNTAX_FLAG);
        
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(0) as PhpModifierListAst;
        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(1) as PhpParameterListAst;
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(2) as ITypeExpression;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(3) as PhpStatementBlockAst;
        
        public static PhpMethodDeclAst Create(string? name, bool returnsRef, PhpModifierListAst? modifiers, PhpParameterListAst? parameters, ITypeExpression? returnType, PhpStatementBlockAst? body, string? docComment, ParserRuleContext context, string? languageMode = null, bool isShortSyntax = false, bool isOmit = false, bool isPartial = false)
        {
            var result = new PhpMethodDeclAst
            {
                Identifier = name ?? "",
                Children = [modifiers, parameters, returnType, body],
                DocComment = docComment,
            };

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);
            result.SetFlag(IS_SHORT_SYNTAX_FLAG, isShortSyntax);
            result.SetFlag(IS_OMIT_FLAG, isOmit);
            result.SetFlag(IS_PARTIAL_FLAG, isPartial);
            result.SetContext(context, languageMode);

            return result;
        }

        /// <summary>
        /// Shallow copy of this signature (children and addons shared) so overlay
        /// attribute merge does not mutate a cached Layer 1 AST.
        /// </summary>
        internal PhpMethodDeclAst CloneSignature()
        {
            var result = new PhpMethodDeclAst
            {
                Identifier = Identifier,
                Children = [Modifiers, Parameters, ReturnType, Body],
                DocComment = DocComment,
            };
            result.SetFlag(RETURNS_REF_FLAG, ReturnsRef);
            result.SetFlag(IS_SHORT_SYNTAX_FLAG, IsShortSyntax);
            result.SetContext(this);
            foreach (var addon in AstGrammarAddons)
            {
                result.AddGrammarAddon(addon.Key, addon.Value);
            }

            result.CopyAttributesFrom(this);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpMethodDeclAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpMethodDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpMethodDeclAst
            {
                Identifier = "<error>",
                Children = [null, null, null, null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 