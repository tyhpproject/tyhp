using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates Tyhp struct declarations, including generic parameters.</summary>
    public sealed class StructRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(TyhpStructDeclAst)];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is not TyhpStructDeclAst structDecl)
            {
                return;
            }

            var structState = state;
            ObjectDeclarationSymbol? structSymbol = null;
            if (structDecl.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                structSymbol = bound;
                structState = state.Split(ScopeType.ObjectTypeDeclaration);
                structState.EnclosingObject = bound;
                structState.EnclosingObjectType = CheckedTypes.FromSymbol(bound);
                structState.ObjectGenerics = bound.GenericParameters;
                GenericConstraintResolver.ResolveAll(bound.GenericParameters, structState, context);
            }

            if (structDecl.Extends is not null)
            {
                CheckExtends(structDecl, structSymbol, structState, context, diagnostics);
            }

            // Non-nullable properties without defaults are allowed: they are required and must
            // be supplied at construction via `new Struct() with [...]` (see WithKeywordRule).
            foreach (var property in structDecl.PropertyList?.GetAllNotNull() ?? [])
            {
                if (property.TypeExpression is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, property, MessageCode.CheckerStructPropertyRequired, property.Identifier);
                    continue;
                }

                // Resolve under the struct's ObjectGenerics so `T` / constrained params type-check.
                context.ResolveTypeAnnotation(property.TypeExpression, structState);
                // StructRule suppresses child traversal, so property types are never CheckNode'd —
                // still count import usage for TYHP4130.
                context.MarkImportNames(property.TypeExpression, structState);
            }
        }

        private static void CheckExtends(
            TyhpStructDeclAst structDecl,
            ObjectDeclarationSymbol? structSymbol,
            CheckerState structState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var extends = structDecl.Extends!;
            ObjectDeclarationSymbol? parent = structSymbol is not null
                ? TypeComparer.TryGetParentDeclaration(structSymbol, context.SymbolTree, context.GlobalScope)
                : null;

            if (parent is { IsStruct: false }
                && !string.Equals(parent.Name, "struct", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    diagnostics, structState, extends, MessageCode.CheckerGenericConstraintNotSatisfied,
                    structDecl.Identifier, "struct");
                return;
            }

            if (parent is null)
            {
                // Unresolved parent — still try the old expression path for the built-in `struct` alias.
                var extendsType = context.ResolveExpressionType(extends, structState);
                if (CheckerHelpers.TryGetObjectDeclaration(extendsType) is { IsStruct: false }
                    && !CheckerHelpers.IsBuiltInName(extendsType, "struct"))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, structState, extends, MessageCode.CheckerGenericConstraintNotSatisfied,
                        structDecl.Identifier, "struct");
                }

                return;
            }

            var typeArgs = GenericInheritanceBindings.GetExtendsTypeArguments(structSymbol!);
            if (typeArgs is null || typeArgs.Count == 0)
            {
                return;
            }

            var resolvedArgs = typeArgs
                .Select(arg => context.ResolveTypeAnnotation(arg, structState))
                .ToList();

            GenericTypeArgumentValidator.ValidateInstantiation(
                CheckedTypes.FromSymbol(parent),
                resolvedArgs,
                extends,
                structState,
                context.SymbolTree,
                context.GlobalScope,
                diagnostics,
                (typeAst, st, isRet, isUser) =>
                    context.ResolveTypeAnnotation(typeAst, st, isRet, isUser));
        }
    }
}
