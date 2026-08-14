using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private ICheckedType ResolveTemplateStringType(
            TyhpTemplateStringTypeAst templateAst,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            if (templateAst.EncapsList is null)
            {
                return new TemplateStringCheckedType(
                    TemplateStringPatternReader.CreateFromSegments([], "\"\""));
            }

            var fileName = state.CurrentFileName ?? templateAst.OwningFile?.FileName ?? string.Empty;
            var pattern = TemplateStringPatternReader.TryRead(
                templateAst.EncapsList,
                holeExpr => TemplateStringHoleResolver.Resolve(
                    holeExpr,
                    templateAst,
                    state,
                    _symbolTree,
                    _globalScope,
                    ResolveTypeExpressionCore),
                templateAst,
                fileName,
                _diagnostics);

            return pattern is null
                ? CheckedTypes.Unresolved
                : new TemplateStringCheckedType(pattern);
        }

        internal ICheckedType ResolveHoleExpression(
            IExpression holeExpr,
            IBase2Ast contextNode,
            CheckerState state)
        {
            return TemplateStringHoleResolver.Resolve(
                holeExpr,
                contextNode,
                state,
                _symbolTree,
                _globalScope,
                ResolveTypeExpressionCore);
        }
    }
}
