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
        /// <summary>
        /// TYHP4016 argument for a missing function/method return type. The message template
        /// wraps <c>{0}</c> in backticks and must not prefix <c>$</c> or the word "variable".
        /// </summary>
        internal const string ReturnTypeSubject = "return type";

        // PhpMethodDeclAst is intentionally absent: CheckObjectBody calls CheckMethod directly
        // (not CheckNode), so method return-type checks run via CheckMethodReturnType from that path.
        // Registering methods here would double-fire if members were ever routed through CheckNode.
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(TyhpTypedVarExprAst),
            typeof(PhpFunctionDeclAst),
            typeof(PhpParameterAst),
            typeof(PhpConstDeclListAst),
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
                case PhpConstDeclListAst constList:
                    CheckFileLevelTypedConsts(constList, state, diagnostics);
                    break;
            }
        }

        /// <summary>
        /// File-level <c>const int X = 1;</c> parses so this diagnostic can fire; PHP (and Tyhp
        /// source) allow types only on class-level constants. Class const lists are checked from
        /// <c>CheckClassConstants</c> (object-body traversal is suppressed). Object-shape member
        /// consts reuse <see cref="PhpConstDeclListAst"/> but are not file-level — the visitor
        /// stamps <c>objectShapeMember</c> so they skip TYHP4193 the same way
        /// <c>EnclosingObject</c> skips class consts.
        /// </summary>
        private static void CheckFileLevelTypedConsts(
            PhpConstDeclListAst constList,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            // `fallback const int X = 1;` is the one file-level const that carries a type: the type is
            // a Tyhp fact and the emitted `\define(…)` has no PHP type.
            if (state.EnclosingObject is not null
                || constList.AstGrammarAddons.ContainsKey("objectShapeMember")
                || FallbackDeclaration.IsFallback(constList))
            {
                return;
            }

            foreach (var constant in constList.GetAllNotNull())
            {
                if (constant.Type is null
                    || !string.Equals(constant.LanguageMode, "tyhp", StringComparison.Ordinal))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    constant.Type,
                    MessageCode.CheckerFileLevelTypedConstNotAllowed);
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

            AttributeRule.ValidateDeclarationAttributes(typedVar, state, context, diagnostics);

            ICheckedType? declaredType = null;
            if (typedVar.TypeExpression is not null)
            {
                context.CheckNode(typedVar.TypeExpression, state);
                declaredType = context.ResolveTypeAnnotation(typedVar.TypeExpression, state);
            }

            var previousExpected = state.ExpectedExpressionType;
            if (ContextualNewInference.IsUsableExpectedType(declaredType))
            {
                state.ExpectedExpressionType = declaredType;
            }

            try
            {
                if (typedVar.AssignedExpression is PhpInlineFunctionAst closure)
                {
                    if (declaredType is not null)
                    {
                        ClosureParameterInference.SetExpectedClosureTypeFromAnnotation(declaredType, state);
                    }

                    context.CheckNode(closure, state);
                }
                else if (typedVar.AssignedExpression is not null)
                {
                    context.CheckNode(typedVar.AssignedExpression, state);
                }
            }
            finally
            {
                state.ExpectedExpressionType = previousExpected;
            }

            if (typedVar.TypeExpression is null)
            {
                if (typedVar.AssignedExpression is not null)
                {
                    declaredType = context.ResolveExpressionType(typedVar.AssignedExpression, state);
                    if (declaredType.Kind == CheckedTypeKind.Unresolved)
                    {
                        CheckerHelpers.ReportError(
                            context, state, typedVar, MessageCode.CheckerVariableTypeRequired,
                            CheckerHelpers.FormatTypeRequiredName(varName));
                        return;
                    }
                }
                else
                {
                    CheckerHelpers.ReportError(
                        context, state, typedVar, MessageCode.CheckerVariableTypeRequired,
                        CheckerHelpers.FormatTypeRequiredName(varName));
                    return;
                }
            }

            if (typedVar.AssignedExpression is not null)
            {
                var sourceType = context.ResolveExpressionType(typedVar.AssignedExpression, state);
                if (declaredType is not null
                    && GeneratorBodyInference.TryHandleYieldSendAssignment(
                        typedVar.AssignedExpression, declaredType, state))
                {
                    // Unpinned TSend: yield currently types as mixed; the declared target
                    // constrains TSend instead of reporting mixed↛T.
                }
                else
                {
                    var bagChecked = declaredType is not null
                        && StructBagLiteralChecker.TryCheck(
                            typedVar.AssignedExpression, declaredType, state, context, diagnostics);
                    var expressionCapture = typedVar.AssignedExpression is PhpInlineFunctionAst inlineFn
                        && declaredType is not null
                        && (ExpressionTreeSupport.TryValidateInlineFnCapture(
                                inlineFn, declaredType, state, diagnostics, typedVar)
                            || PropertyPathSupport.TryValidateInlineFnCapture(
                                inlineFn, declaredType, state, diagnostics, typedVar));
                    if (!bagChecked
                        && !expressionCapture
                        && !context.IsAssignable(sourceType, declaredType, state)
                        && !CheckerHelpers.IsArrayCallableLiteral(
                            typedVar.AssignedExpression, declaredType!, context, state))
                    {
                        if (!CheckerHelpers.TryReportObjectShapeRequiresGuard(
                                diagnostics, state, typedVar, sourceType, declaredType!)
                            && !CheckerHelpers.TryReportCallableShapeRequiresGuard(
                                diagnostics, state, typedVar, sourceType, declaredType!)
                            && !CheckerHelpers.TryReportNewConstraintFailure(
                                diagnostics, state, typedVar, sourceType, declaredType!,
                                context.SymbolTree, context.GlobalScope)
                            && !SymbolNameTypeAssignability.TryReportLiteralExistenceFailure(
                                sourceType, declaredType!, state, context.SymbolTree, context.GlobalScope,
                                diagnostics, typedVar)
                            && !context.TryReportTemplateStringBudgetExceeded(typedVar, state))
                        {
                            CheckerHelpers.ReportError(
                                context, state, typedVar, MessageCode.CheckerTypeMismatch,
                                sourceType.DisplayName, declaredType!.DisplayName);
                        }
                    }
                }

                state.DeclareVariable(
                    varName,
                    new Binder.Symbols.VariableSymbol(
                        varName,
                        (IBase2Ast?)typedVar.Variable ?? typedVar,
                        CheckerHelpers.ResolveDiagnosticFileName(state, typedVar)),
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
                    new Binder.Symbols.VariableSymbol(
                        varName,
                        (IBase2Ast?)typedVar.Variable ?? typedVar,
                        CheckerHelpers.ResolveDiagnosticFileName(state, typedVar)),
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
                    context, state, function, MessageCode.CheckerVariableTypeRequired, ReturnTypeSubject);
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
                    context, state, method, MessageCode.CheckerVariableTypeRequired, ReturnTypeSubject);
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
                    context, state, parameter, MessageCode.CheckerVariableTypeRequired,
                    CheckerHelpers.FormatTypeRequiredName(parameter.Name));
            }
        }
    }
}
