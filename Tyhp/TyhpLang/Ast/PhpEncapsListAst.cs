using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpEncapsListAst : NodeListAst<IEncapsVarOrString, PhpEncapsListAst>, IDereferenceableBase, IScalar
    {
        private const short STRING_TYPE_OFFSET = 5000;

        public virtual PhpStringType StringType => GetEnumFlags<PhpStringType>(STRING_TYPE_OFFSET).FirstOrDefault();

        public virtual void SetStringType(PhpStringType stringType)
            => SetFlag(STRING_TYPE_OFFSET, stringType);
    }

    public static class PhpEncapsListAstExtensions
    {
        public static TPhpEncapsListAstType WithStringType<TPhpEncapsListAstType>(this TPhpEncapsListAstType astNode, TokenValueAst tokenValue)
            where TPhpEncapsListAstType : PhpEncapsListAst, new()
            => astNode.WithStringType<TPhpEncapsListAstType>(PhpStringTypeExtensions.FromToken(Convert.ToInt32(tokenValue.ValueInt64 ?? -1)) ?? PhpStringType.SingleQuoted);

        public static TPhpEncapsListAstType WithStringType<TPhpEncapsListAstType>(this TPhpEncapsListAstType astNode, PhpStringType stringType)
            where TPhpEncapsListAstType : PhpEncapsListAst, new()
        {
            astNode.SetStringType(stringType);
            return astNode;
        }
    }
} 