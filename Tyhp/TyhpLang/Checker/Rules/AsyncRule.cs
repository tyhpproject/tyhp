using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates async/await usage and async function declarations.</summary>
    public sealed class AsyncRule : ICheckerRule
    {
        // PhpMethodDeclAst is intentionally absent: CheckObjectBody calls CheckMethod directly
        // (not CheckNode), so async method checks run via ValidateAsyncMethod from that path.
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpUnaryOpAst),
            typeof(PhpFunctionDeclAst),
        ];

        public bool Handles(IBase2Ast node) =>
            node switch
            {
                PhpUnaryOpAst unary => IsAwaitOperator(unary),
                PhpFunctionDeclAst function => IsAsyncFunction(function),
                _ => false,
            };

        public bool SuppressChildTraversal(IBase2Ast node) => false;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpUnaryOpAst unary when IsAwaitOperator(unary):
                    CheckAwait(unary, state, diagnostics);
                    break;
                case PhpFunctionDeclAst function when IsAsyncFunction(function):
                    ValidateAsyncCallableReturnType(
                        function.ReturnType,
                        function,
                        StateWithFunctionGenerics(function.BoundSymbol, state),
                        context,
                        diagnostics);
                    break;
            }
        }

        /// <summary>
        /// Validates an async method's return-type shape. Invoked explicitly from
        /// <c>DeclarationRule.CheckMethod</c> because class members bypass <c>CheckNode</c>.
        /// </summary>
        public static void ValidateAsyncMethod(
            PhpMethodDeclAst method,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!IsAsyncMethod(method))
            {
                return;
            }

            ValidateAsyncCallableReturnType(method.ReturnType, method, state, context, diagnostics);
        }

        private static void CheckAwait(
            PhpUnaryOpAst unary,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!state.IsInAsyncContext && !state.IsTopLevelAwaitableScope())
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, unary, MessageCode.CheckerAwaitOutsideAsync);
            }
        }

        private static void ValidateAsyncCallableReturnType(
            ITypeExpression? returnTypeAst,
            IBase2Ast node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (returnTypeAst is null)
            {
                return;
            }

            var returnType = context.ResolveTypeAnnotation(returnTypeAst, state, isReturnTypePosition: true);
            if (IsVoidReturn(returnType))
            {
                return;
            }

            if (returnType.DisplayName.Contains("Generator", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, returnTypeAst, MessageCode.CheckerGeneratorInvalidReturnType);
            }
        }

        private static CheckerState StateWithFunctionGenerics(IBaseSymbol? symbol, CheckerState state)
        {
            if (symbol is FunctionDeclarationSymbol { GenericParameters.Count: > 0 } function)
            {
                var forked = state.Fork();
                forked.FunctionGenerics = function.GenericParameters;
                return forked;
            }

            return state;
        }

        private static bool IsAwaitOperator(PhpUnaryOpAst unary) =>
            string.Equals(unary.Operator?.ValueString, "await", StringComparison.OrdinalIgnoreCase)
            || GetTokenType(unary.Operator) == TyhpParser.T_TYHP_AWAIT;

        private static bool IsAsyncFunction(PhpFunctionDeclAst function) =>
            function.BoundSymbol is FunctionDeclarationSymbol { IsAsync: true }
            || HasAsyncModifierGrammarAddon(function);

        private static bool IsAsyncMethod(PhpMethodDeclAst method) =>
            method.BoundSymbol is ObjectMethodSymbol { IsAsync: true }
            || HasAsyncModifier(method.Modifiers)
            || method.AstGrammarAddons.ContainsKey("isAsync");

        private static bool HasAsyncModifierGrammarAddon(PhpFunctionDeclAst function)
        {
            if (!function.AstGrammarAddons.TryGetValue("modifiers", out var addon))
            {
                return false;
            }

            return addon switch
            {
                TokenValueListAst list => list.GetAllNotNull().Any(IsAsyncToken),
                TokenValueAst token => IsAsyncToken(token),
                _ => false,
            };
        }

        private static bool HasAsyncModifier(PhpModifierListAst? modifiers)
        {
            if (modifiers is null)
            {
                return false;
            }

            // Preferred path: VisitNonEmptyMemberModifiers attaches `isAsync` on the modifier list.
            if (modifiers.AstGrammarAddons.ContainsKey("isAsync"))
            {
                return true;
            }

            foreach (var child in modifiers.AstChildren)
            {
                if (child is TokenValueAst token && IsAsyncToken(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAsyncToken(TokenValueAst token) =>
            token.ValueInt64 == TyhpParser.T_TYHP_ASYNC
            || string.Equals(token.ValueString, "async", StringComparison.OrdinalIgnoreCase);

        private static bool IsVoidReturn(ICheckedType returnType) =>
            returnType.IsVoid
            || returnType.Kind == CheckedTypeKind.Void
            || string.Equals(returnType.DisplayName.TrimStart('\\'), "void", StringComparison.OrdinalIgnoreCase);

        private static int GetTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : -1;
    }
}
