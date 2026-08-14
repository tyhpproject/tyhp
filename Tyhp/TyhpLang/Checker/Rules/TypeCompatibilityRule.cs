using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Expression-level type compatibility checks.</summary>
    public sealed partial class TypeCompatibilityRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpBinaryOpAst),
            typeof(PhpNewAst),
            typeof(PhpDereferenceableAst),
            typeof(PhpUnaryOpAst),
            typeof(PhpVariableAst),
            typeof(PhpArrayAst),
            typeof(PhpArrayPairListAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) =>
            node is PhpDereferenceableAst
            || (node is PhpBinaryOpAst binary
                && TypeNarrowingRule.IsLogicalAnd(binary.Operator?.ValueString ?? string.Empty));

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpBinaryOpAst binary:
                    CheckBinaryOp(binary, state, context, diagnostics);
                    break;
                case PhpNewAst newExpr:
                    CheckNew(newExpr, state, context, diagnostics);
                    break;
                case PhpDereferenceableAst deref:
                    // Child traversal is suppressed for dereferenceables; still mark import
                    // usage on the receiver / static name (Widget::make(), Imported::CONST).
                    context.MarkImportNames(deref, state);
                    CheckDereferenceable(deref, state, context, diagnostics);
                    break;
                case PhpUnaryOpAst unary:
                    CheckUnaryOp(unary, state, context, diagnostics);
                    break;
                case PhpVariableAst variable:
                    CheckVariable(variable, state, context, diagnostics);
                    break;
                case PhpArrayAst array:
                    CheckArray(array, state, context, diagnostics);
                    break;
                case PhpArrayPairListAst pairList:
                    CheckArrayPairList(pairList, state, context, diagnostics);
                    break;
            }
        }
    }
}
