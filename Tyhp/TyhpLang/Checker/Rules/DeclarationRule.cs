using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Declaration-level validation for namespaces, types, functions, methods, and properties.</summary>
    public sealed partial class DeclarationRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpNamespaceDeclAst),
            typeof(PhpBlockNamespaceDeclAst),
            typeof(PhpObjectTypeDeclAst),
            typeof(PhpFunctionDeclAst),
            typeof(PhpMethodDeclAst),
            typeof(PhpPropertyDeclAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpNamespaceDeclAst ns:
                    CheckNamespace(ns, state, context, diagnostics);
                    break;
                case PhpBlockNamespaceDeclAst blockNs:
                    CheckBlockNamespace(blockNs, state, context, diagnostics);
                    break;
                case PhpObjectTypeDeclAst objectType:
                    CheckObjectType(objectType, state, context, diagnostics);
                    break;
                case PhpFunctionDeclAst function:
                    CheckFunction(function, state, context, diagnostics);
                    break;
                case PhpMethodDeclAst method:
                    CheckMethod(method, state, context, diagnostics);
                    break;
                case PhpPropertyDeclAst property:
                    CheckProperty(property, state, context, diagnostics);
                    break;
            }
        }

        private static void CheckNamespace(
            PhpNamespaceDeclAst ns,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Statement-style `namespace Foo;` applies to subsequent file-level siblings.
            state.CurrentNamespaceName = ns.Identifier ?? "";

            if (ns.TopStatements is null)
            {
                return;
            }

            var nsState = state.Split(ScopeType.Namespace);
            nsState.CurrentNamespaceName = ns.Identifier ?? "";
            context.CheckNodes(ns.TopStatements.GetAllNotNull().Cast<IBase2Ast>(), nsState);
        }

        private static void CheckBlockNamespace(
            PhpBlockNamespaceDeclAst ns,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var nsState = state.Split(ScopeType.NamespaceBlock);
            nsState.CurrentNamespaceName = ns.Identifier ?? "";
            context.CheckNodes(ns.TopStatements?.GetAllNotNull().Cast<IBase2Ast>() ?? [], nsState);
        }
    }
}
