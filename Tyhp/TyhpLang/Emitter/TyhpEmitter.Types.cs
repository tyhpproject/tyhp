using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private string BuildTypeExpression(ITypeExpression? typeExpression)
            => TypeSpellingHelper.Spell(
                typeExpression,
                this._context.TypeAliasMap,
                this._context.GlobalScope,
                this._context.Config.NamespacePrefix);

        private string BuildExpressionNameWithoutGenerics(IExpression? expr)
        {
            if (expr is TyhpGenericIdentifierAst generic)
            {
                return generic.ValueString ?? "";
            }

            return this.BuildExpression(expr);
        }

        private EmitItem EmitTypeExpression(ITypeExpression typeExpression, EmitItem parent)
            => EmitItem.Line(typeExpression, EmitType.SubBlockStatement, this.BuildTypeExpression(typeExpression), parent);
    }
}
