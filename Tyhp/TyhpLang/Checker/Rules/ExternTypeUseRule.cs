using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Reports TYHP4307 when a <c>.tyhp</c> file uses an <c>extern</c> name: type
    /// annotations that are CheckNode'd, <c>new</c>, <c>instanceof</c>, <c>use</c> imports,
    /// free-function calls, const fetches, and call expressions whose inferred return type
    /// is extern. Parameter/return/catch/argument sites that skip CheckNode call
    /// <see cref="ExternTypeUse"/> from the owning rules.
    /// </summary>
    public sealed class ExternTypeUseRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpTypeExpressionAst),
            typeof(PhpNewAst),
            typeof(PhpBinaryOpAst),
            typeof(PhpImportDeclAst),
            typeof(PhpDereferenceableAst),
            typeof(PhpNameAst),
        ];

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpTypeExpressionAst typeExpr:
                    ExternTypeUse.ReportIfTypeAnnotation(typeExpr, state, context, diagnostics);
                    break;
                case PhpNewAst newExpr:
                    CheckNew(newExpr, state, context, diagnostics);
                    break;
                case PhpBinaryOpAst binary
                    when CheckerHelpers.IsInstanceofLikeOperator(binary) && binary.Right is not null:
                    CheckInstanceof(binary, state, context, diagnostics);
                    break;
                case PhpImportDeclAst import:
                    ExternTypeUse.ReportIfImport(import, state, context, diagnostics);
                    break;
                case PhpDereferenceableAst { Suffix: PhpCallAst } deref:
                    CheckCallReturn(deref, state, context, diagnostics);
                    break;
                case PhpNameAst name:
                    CheckName(name, state, context, diagnostics);
                    break;
            }
        }

        private static void CheckNew(
            PhpNewAst newExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!ExternTypeUse.IsTyhpSource(state, newExpr))
            {
                return;
            }

            var classRef = newExpr.ClassName;
            if (classRef is null)
            {
                return;
            }

            if (classRef.BoundSymbol is ObjectDeclarationSymbol { IsExtern: true } boundExtern)
            {
                ExternTypeUse.Report(diagnostics, state, classRef, boundExtern);
            }
            else
            {
                ExternTypeUse.ReportIfCheckedType(
                    context.ResolveExpressionType(newExpr, state),
                    classRef,
                    state,
                    diagnostics);
            }

            ExternTypeUse.ReportIfTypeArgumentAddons(classRef, state, context, diagnostics);
        }

        private static void CheckInstanceof(
            PhpBinaryOpAst binary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var right = binary.Right!;
            var targetType = CheckerHelpers.ResolveInstanceofTargetType(
                right, state, context, context.SymbolTree, context.GlobalScope);
            ExternTypeUse.ReportIfCheckedType(targetType, right, state, diagnostics);
            ExternTypeUse.ReportIfTypeArgumentAddons(right, state, context, diagnostics);
        }

        private static void CheckCallReturn(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!ExternTypeUse.IsTyhpSource(state, deref))
            {
                return;
            }

            if (deref.Base is PhpNameAst calleeName
                && CheckerHelpers.ResolveFreeFunction(
                    calleeName, state, context.SymbolTree, context.GlobalScope) is { } function
                && ExternTypeUse.TryGetExternSymbol(function, out var externFunction))
            {
                ExternTypeUse.Report(diagnostics, state, deref, externFunction);
            }

            ExternTypeUse.ReportIfCheckedType(
                context.ResolveExpressionType(deref, state),
                deref,
                state,
                diagnostics);

            ExternTypeUse.ReportIfTypeArgumentAddons(deref.Base, state, context, diagnostics);
            if (deref.Base is PhpDereferenceableAst chain)
            {
                ExternTypeUse.ReportIfTypeArgumentAddons(chain.Suffix, state, context, diagnostics);
            }
        }

        private static void CheckName(
            PhpNameAst name,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!ExternTypeUse.IsTyhpSource(state, name))
            {
                return;
            }

            var constant = CheckerHelpers.ResolveFreeConstant(
                name, state, context.SymbolTree, context.GlobalScope);
            if (ExternTypeUse.TryGetExternSymbol(constant, out var externConstant))
            {
                ExternTypeUse.Report(diagnostics, state, name, externConstant);
            }
        }
    }
}
