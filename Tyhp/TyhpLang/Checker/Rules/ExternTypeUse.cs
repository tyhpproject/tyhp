using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Story 21.1 / 21.10: <c>.tyhp</c> use of a tyhpdef <c>extern</c> name (type, function, or
    /// const) is TYHP4307. Tyhpdef signatures and mapping bodies may name externs; inheritance
    /// of an extern type is binder TYHP3026/3027.
    /// </summary>
    internal static class ExternTypeUse
    {
        public static bool IsTyhpSource(CheckerState state, IBase2Ast node)
        {
            if (string.Equals(node.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, node);
            return !fileName.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase);
        }

        public static void ReportIfCheckedType(
            ICheckedType? type,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (type is null || !IsTyhpSource(state, reportNode))
            {
                return;
            }

            foreach (var externSymbol in CollectExternSymbols(type))
            {
                Report(diagnostics, state, reportNode, externSymbol);
            }
        }

        public static void ReportIfTypeAnnotation(
            ITypeExpression? typeAst,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            bool isReturnTypePosition = false)
        {
            if (typeAst is null || !IsTyhpSource(state, typeAst))
            {
                return;
            }

            ReportIfCheckedType(
                context.ResolveTypeAnnotation(typeAst, state, isReturnTypePosition),
                typeAst,
                state,
                diagnostics);
        }

        public static void ReportIfSymbol(
            IBaseSymbol? symbol,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (TryGetExternSymbol(symbol, out var externSymbol))
            {
                Report(diagnostics, state, reportNode, externSymbol);
            }
        }

        public static void ReportIfImport(
            PhpImportDeclAst import,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!IsTyhpSource(state, import))
            {
                return;
            }

            var imported = (import.NamespaceName ?? string.Empty).Trim().TrimStart('\\');
            if (string.IsNullOrEmpty(imported))
            {
                return;
            }

            var resolver = new NameResolver(context.SymbolTree, new DiagnosticBag());
            var resolved = resolver.ResolveQualifiedName(
                imported.Split('\\', StringSplitOptions.RemoveEmptyEntries));
            var useType = import.UseType?.ValueString?.ToLowerInvariant();
            if (useType == "function")
            {
                ReportIfSymbol(AsImportedFunction(resolved), import, state, diagnostics);
                return;
            }

            if (useType == "const")
            {
                ReportIfSymbol(AsImportedConstant(resolved), import, state, diagnostics);
                return;
            }

            ReportIfSymbol(resolved, import, state, diagnostics);
        }

        public static void ReportIfTypeArgumentAddons(
            IBase2Ast? node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (node is null || !IsTyhpSource(state, node))
            {
                return;
            }

            foreach (var key in (string[])["typeName", "identifier"])
            {
                if (!node.AstGrammarAddons.TryGetValue(key, out var addon)
                    || addon is not PhpTypeExpressionListAst list)
                {
                    continue;
                }

                foreach (var arg in list.GetAllNotNull())
                {
                    if (arg is ITypeExpression typeArg)
                    {
                        ReportIfTypeAnnotation(typeArg, state, context, diagnostics);
                    }
                    else
                    {
                        ReportIfTypeArgumentAddons(arg, state, context, diagnostics);
                    }
                }
            }
        }

        /// <summary>
        /// Bound-symbol walk used by the emitter ICE when a <c>.tyhp</c> type position still
        /// names an <c>extern</c> placeholder (checker should have rejected it).
        /// </summary>
        public static ObjectDeclarationSymbol? FindExternSymbol(IBase2Ast? node)
        {
            if (node is null)
            {
                return null;
            }

            var visited = new HashSet<IBase2Ast>();
            return FindExternSymbolCore(node, visited);
        }

        public static ObjectDeclarationSymbol? FindExternSymbol(ICheckedType? type)
        {
            if (type is null)
            {
                return null;
            }

            foreach (var symbol in CollectExternSymbols(type))
            {
                return symbol;
            }

            return null;
        }

        public static IEnumerable<ObjectDeclarationSymbol> CollectExternSymbols(ICheckedType type)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            CollectExternSymbolsCore(type, seen);
            return seen;
        }

        public static void Report(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            BaseSymbol externSymbol)
        {
            if (!IsTyhpSource(state, node))
            {
                return;
            }

            var displayName = string.IsNullOrEmpty(externSymbol.FullyQualifiedName)
                ? externSymbol.Name
                : externSymbol.FullyQualifiedName;

            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            var diagnostic = Diagnostic.Error(
                MessageCode.CheckerExternTypeUsed,
                CheckerHelpers.ResolveDiagnosticFileName(state, node),
                node.Line,
                node.Column,
                [displayName],
                endLine,
                endColumn);

            if (externSymbol.DeclaringAstNode != null)
            {
                diagnostic = diagnostic.WithLabels(
                    DiagnosticExtensions.LabelFromAst(
                        externSymbol.DeclaringAstNode,
                        externSymbol.SourceFile
                            ?? CheckerHelpers.ResolveDiagnosticFileName(state, node),
                        Message.Localize("CLI_DiagnosticLabelDeclaredHere")));
            }

            if (!string.IsNullOrEmpty(externSymbol.ProvidedBy))
            {
                diagnostic = diagnostic.WithHelp(externSymbol.ProvidedBy);
            }

            diagnostics.Add(diagnostic);
        }

        internal static bool TryGetExternSymbol(IBaseSymbol? symbol, out BaseSymbol externSymbol)
        {
            if (symbol is BaseSymbol { IsExtern: true } candidate)
            {
                externSymbol = candidate;
                return true;
            }

            externSymbol = null!;
            return false;
        }

        private static IBaseSymbol? AsImportedFunction(IBaseSymbol? resolved)
        {
            if (resolved is FunctionDeclarationSymbol function)
            {
                return function;
            }

            if (resolved?.ContainingScope is not { } scope || string.IsNullOrEmpty(resolved.Name))
            {
                return null;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is FunctionDeclarationSymbol sibling
                    && string.Equals(sibling.Name, resolved.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return sibling;
                }
            }

            return null;
        }

        private static IBaseSymbol? AsImportedConstant(IBaseSymbol? resolved)
        {
            if (resolved is ConstantSymbol constant)
            {
                return constant;
            }

            if (resolved?.ContainingScope is not { } scope || string.IsNullOrEmpty(resolved.Name))
            {
                return null;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ConstantSymbol sibling
                    && string.Equals(sibling.Name, resolved.Name, StringComparison.Ordinal))
                {
                    return sibling;
                }
            }

            return null;
        }

        private static void CollectExternSymbolsCore(
            ICheckedType type,
            HashSet<ObjectDeclarationSymbol> seen)
        {
            switch (type)
            {
                case SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol { IsExtern: true } obj }:
                    seen.Add(obj);
                    break;
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        CollectExternSymbolsCore(member, seen);
                    }

                    break;
                case IntersectionCheckedType intersection:
                    foreach (var member in intersection.Members)
                    {
                        CollectExternSymbolsCore(member, seen);
                    }

                    break;
                case NullableCheckedType nullable:
                    CollectExternSymbolsCore(nullable.InnerType, seen);
                    break;
                case GenericCheckedType generic:
                    CollectExternSymbolsCore(generic.BaseType, seen);
                    foreach (var arg in generic.TypeArguments)
                    {
                        CollectExternSymbolsCore(arg, seen);
                    }

                    break;
                case StaticCheckedType staticType:
                    CollectExternSymbolsCore(staticType.DeclaringType, seen);
                    break;
                case CallableCheckedType callable:
                    foreach (var param in callable.ParameterTypes)
                    {
                        CollectExternSymbolsCore(param, seen);
                    }

                    CollectExternSymbolsCore(callable.ReturnType, seen);
                    break;
                case LiteralCheckedType literal:
                    CollectExternSymbolsCore(literal.UnderlyingType, seen);
                    break;
            }
        }

        private static ObjectDeclarationSymbol? FindExternSymbolCore(
            IBase2Ast node,
            HashSet<IBase2Ast> visited)
        {
            if (!visited.Add(node))
            {
                return null;
            }

            if (node.BoundSymbol is ObjectDeclarationSymbol { IsExtern: true } bound)
            {
                return bound;
            }

            if (node is PhpNamedTypeAst { Name: IBase2Ast nameChild })
            {
                var fromName = FindExternSymbolCore(nameChild, visited);
                if (fromName is not null)
                {
                    return fromName;
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child is null)
                {
                    continue;
                }

                var fromChild = FindExternSymbolCore(child, visited);
                if (fromChild is not null)
                {
                    return fromChild;
                }
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                if (addon is null)
                {
                    continue;
                }

                var fromAddon = FindExternSymbolCore(addon, visited);
                if (fromAddon is not null)
                {
                    return fromAddon;
                }
            }

            return null;
        }
    }
}
