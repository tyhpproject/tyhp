using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Enforces required type annotations on declarations and variable inference.
    /// </summary>
    public sealed class TypeAnnotationRule : ICheckerRule
    {
        // PhpMethodDeclAst is intentionally absent: CheckObjectBody calls CheckMethod directly
        // (not CheckNode), so method return-type checks run via CheckMethodReturnType from that path.
        // Registering methods here would double-fire if members were ever routed through CheckNode.
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(TyhpTypedVarExprAst),
            typeof(PhpFunctionDeclAst),
            typeof(PhpParameterAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) =>
            node is TyhpTypedVarExprAst or PhpFunctionDeclAst;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case TyhpTypedVarExprAst typedVar:
                    CheckTypedVariable(typedVar, state, context, diagnostics);
                    break;
                case PhpFunctionDeclAst function:
                    CheckFunctionReturnType(function, state, context);
                    break;
                case PhpParameterAst parameter:
                    CheckParameterType(parameter, state, context);
                    break;
            }
        }

        private static void CheckTypedVariable(
            TyhpTypedVarExprAst typedVar,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var varName = CheckerHelpers.GetVariableName(typedVar.Variable);
            if (varName is null)
            {
                return;
            }

            ICheckedType? declaredType = null;

            if (typedVar.AssignedExpression is PhpInlineFunctionAst closure)
            {
                if (typedVar.TypeExpression is not null)
                {
                    ClosureParameterInference.SetExpectedClosureTypeFromAnnotation(
                        context.ResolveTypeAnnotation(typedVar.TypeExpression, state), state);
                }

                context.CheckNode(closure, state);
            }
            else if (typedVar.AssignedExpression is PhpNewAst newExpr)
            {
                context.CheckNode(newExpr, state);
            }
            else if (typedVar.AssignedExpression is not null)
            {
                context.CheckNode(typedVar.AssignedExpression, state);
            }

            if (typedVar.TypeExpression is not null)
            {
                context.CheckNode(typedVar.TypeExpression, state);
                declaredType = context.ResolveTypeAnnotation(typedVar.TypeExpression, state);
            }
            else if (typedVar.AssignedExpression is not null)
            {
                declaredType = context.ResolveExpressionType(typedVar.AssignedExpression, state);
                if (declaredType.Kind == CheckedTypeKind.Unresolved)
                {
                    CheckerHelpers.ReportError(
                        context, state, typedVar, MessageCode.CheckerVariableTypeRequired, varName);
                    return;
                }
            }
            else
            {
                CheckerHelpers.ReportError(
                    context, state, typedVar, MessageCode.CheckerVariableTypeRequired, varName);
                return;
            }

            if (typedVar.AssignedExpression is not null)
            {
                var sourceType = context.ResolveExpressionType(typedVar.AssignedExpression, state);
                var bagChecked = declaredType is not null
                    && StructBagLiteralChecker.TryCheck(
                        typedVar.AssignedExpression, declaredType, state, context, diagnostics);
                if (!bagChecked
                    && !context.IsAssignable(sourceType, declaredType, state)
                    && !CheckerHelpers.IsArrayCallableLiteral(
                        typedVar.AssignedExpression, declaredType!, context, state))
                {
                    if (!SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                            sourceType, declaredType!, state, context.SymbolTree, context.GlobalScope,
                            diagnostics, typedVar)
                        && !context.TryReportTemplateStringBudgetExceeded(typedVar, state))
                    {
                        CheckerHelpers.ReportError(
                            context, state, typedVar, MessageCode.CheckerTypeMismatch,
                            sourceType.DisplayName, declaredType!.DisplayName);
                    }
                }

                state.DeclareVariable(
                    varName,
                    new Binder.Symbols.VariableSymbol(varName),
                    declaredType,
                    isAssigned: true,
                    diagnostics);

                // Only carry the source's nullability onto the variable when the declared type
                // actually permits null. A non-nullable declaration can never hold null: if the
                // source were genuinely nullable the assignability check above already reported a
                // type mismatch. Guarding on the declared type avoids false "possibly null"
                // reports for non-nullable values whose inferred source type merely *contains*
                // null — e.g. an array literal whose element type is `mixed`/`unknown`, which
                // makes `array $x = []` look nullable even though the array itself never is.
                if ((declaredType?.IsNullable ?? false)
                    && (sourceType.IsNullable
                        || sourceType.Kind == CheckedTypeKind.Literal
                            && sourceType is LiteralCheckedType { Value: null }))
                {
                    if (state.LookupVariable(varName) is { } varState)
                    {
                        varState.IsPossiblyNull = true;
                    }
                }
            }
            else
            {
                state.DeclareVariable(
                    varName,
                    new Binder.Symbols.VariableSymbol(varName),
                    declaredType,
                    isAssigned: false,
                    diagnostics);
            }
        }

        private static void CheckFunctionReturnType(
            PhpFunctionDeclAst function,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (function.ReturnType is null && function.BoundSymbol is Binder.Symbols.FunctionDeclarationSymbol sym
                && sym.ReturnType is null)
            {
                CheckerHelpers.ReportError(
                    context, state, function, MessageCode.CheckerVariableTypeRequired, "return type");
            }
        }

        /// <summary>
        /// Validates a method's return-type annotation. Invoked explicitly from
        /// <c>DeclarationRule.CheckMethod</c> because class members bypass <c>CheckNode</c>.
        /// </summary>
        public static void CheckMethodReturnType(
            PhpMethodDeclAst method,
            CheckerState state,
            CheckerRuleContext context)
        {
            // `__construct` / `__destruct` cannot declare a return type in PHP at all, so they are
            // exempt rather than merely defaulted — unlike an ordinary method, there is no annotation
            // the author could add to satisfy this rule.
            if (method.BoundSymbol is Binder.Symbols.ObjectConstructorMethodSymbol
                or Binder.Symbols.ObjectDestructorMethodSymbol)
            {
                return;
            }

            if (method.ReturnType is null && method.BoundSymbol is Binder.Symbols.ObjectMethodSymbol sym
                && sym.ReturnType is null
                && !sym.IsAbstract)
            {
                CheckerHelpers.ReportError(
                    context, state, method, MessageCode.CheckerVariableTypeRequired, "return type");
            }
        }

        private static void CheckParameterType(
            PhpParameterAst parameter,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.ScopeType == ScopeType.AnonymousFunctionDeclaration)
            {
                return;
            }

            if (state.EnclosingFunction is null)
            {
                return;
            }

            if (parameter.Type is null)
            {
                CheckerHelpers.ReportError(
                    context, state, parameter, MessageCode.CheckerVariableTypeRequired, parameter.Name);
            }
        }
    }
}
