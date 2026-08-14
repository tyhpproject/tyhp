using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTypeExpressionAst : Base2Ast, ITypeExpression
    {
        private const short TYPE_KIND_OFFSET = 11000;
        private const short IS_NULLABLE_FLAG = -12;
        private const short IS_STATIC_FLAG = -3;
        
        // All type expressions as children
        public PhpTypeExpressionListAst? Types => Children.ElementAtOrDefault(0) as PhpTypeExpressionListAst;
        
        public PhpTypeKind TypeKind => GetEnumFlags<PhpTypeKind>(TYPE_KIND_OFFSET).FirstOrDefault();
        
        public bool IsNullable => HasFlag(IS_NULLABLE_FLAG);
        
        public bool IsStatic => HasFlag(IS_STATIC_FLAG);
        
        public static PhpTypeExpressionAst Create(
            PhpTypeExpressionListAst types,
            PhpTypeKind typeKind,
            bool isNullable,
            bool isStatic,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new PhpTypeExpressionAst
            {
                Children = [types],
            };

            result.SetContext(context, languageMode);

            result.SetFlag(TYPE_KIND_OFFSET, typeKind);
            result.SetFlag(IS_NULLABLE_FLAG, isNullable);
            result.SetFlag(IS_STATIC_FLAG, isStatic);

            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpTypeExpressionAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpTypeExpressionAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTypeExpressionAst
            {
                Children = [null],
            };
            result.SetContext(context, languageMode);
            result.SetFlag(TYPE_KIND_OFFSET, PhpTypeKind.Invalid);
            return result;
        }
    }
} 