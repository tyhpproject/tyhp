using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates Tyhp extension declarations and extension imports.</summary>
    public sealed class ExtensionRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(TyhpExtensionDeclAst),
            typeof(TyhpImportExtensionAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case TyhpExtensionDeclAst extension:
                    CheckExtensionDeclaration(extension, state, context, diagnostics);
                    break;
                case TyhpImportExtensionAst importExtension:
                    CheckImportExtension(importExtension, state, diagnostics);
                    break;
            }
        }

        private static void CheckExtensionDeclaration(
            TyhpExtensionDeclAst extension,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (extension.Extends is ITypeExpression extendsType)
            {
                context.ResolveTypeAnnotation(extendsType, state);
                // ExtensionRule suppresses child traversal, so the extended type is never
                // CheckNode'd — still count import usage for TYHP4130.
                context.MarkImportNames(extendsType, state);
            }

            var extensionSymbol = extension.BoundSymbol as ObjectDeclarationSymbol;

            foreach (var member in extension.FunctionList?.GetAllNotNull() ?? [])
            {
                switch (member)
                {
                    case PhpFunctionDeclAst function:
                        CheckExtensionFunction(function, extensionSymbol, state, context, diagnostics);
                        break;
                    case TyhpOperatorOverloadAst operatorOverload:
                        CheckExtensionOperatorOverload(operatorOverload, extensionSymbol, state, context);
                        break;
                }
            }
        }

        /// <summary>
        /// Seeds <see cref="CheckerState.EnclosingObject"/> (and the <c>&lt;Type&gt;</c> target as
        /// <see cref="CheckerState.EnclosingObjectType"/>) before <see cref="OperatorOverloadRule"/>
        /// runs, so <c>self</c>/<c>static</c> resolve to the extended type and
        /// <see cref="CheckerHelpers.IsExtensionReceiverThis"/> can see <c>IsExtension</c>.
        /// Without this, every standalone <c>extension { operator +&lt;T&gt;(self …): self }</c>
        /// fails with <c>CheckerRelativeTypeOutsideClass</c> (4064).
        /// </summary>
        private static void CheckExtensionOperatorOverload(
            TyhpOperatorOverloadAst operatorOverload,
            ObjectDeclarationSymbol? extensionSymbol,
            CheckerState state,
            CheckerRuleContext context)
        {
            var opState = state.Split(ScopeType.ObjectTypeDeclaration);
            opState.EnclosingObject = extensionSymbol;

            if (operatorOverload.ExtensionTargetType is not null)
            {
                // Resolve the target against the outer state (concrete type name / builtin — no
                // relative keywords). Seed EnclosingObjectType so `self` means Money / string / …,
                // not the extension declaration symbol itself.
                var targetType = context.ResolveTypeAnnotation(operatorOverload.ExtensionTargetType, state);
                if (!TypeComparer.IsUnresolvedType(targetType))
                {
                    opState.EnclosingObjectType = targetType;
                }

                // ExtensionRule suppresses child traversal, so the <Type> target is never
                // CheckNode'd — still count import usage for TYHP4130.
                context.MarkImportNames(operatorOverload.ExtensionTargetType, state);
            }
            else if (extensionSymbol is not null)
            {
                opState.EnclosingObjectType = CheckedTypes.FromSymbol(extensionSymbol);
            }

            context.CheckNode(operatorOverload, opState);
        }

        private static void CheckExtensionFunction(
            PhpFunctionDeclAst function,
            ObjectDeclarationSymbol? extensionSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var name = function.Identifier ?? string.Empty;

            // Extension functions carry no visibility/static modifiers: the Tyhp grammar override of
            // functionModifiersGrammarAddon only exposes an optional `async`, so public/protected/private/static
            // cannot be written here. They are always emitted as `public static`; nothing to validate.
            if (!HasExtendsKeyword(function))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    function,
                    MessageCode.CheckerExtensionMissingExtends,
                    name);
            }

            if (function.Body is null)
            {
                return;
            }

            // Extension methods lower to static PHP methods, but `extends T $this` makes `$this` a
            // real receiver parameter — not PHP's special instance `$this`. Seed EnclosingObject /
            // parameters so CheckVariable can allow that receiver and type inference sees its type.
            var methodSymbol = function.BoundSymbol as ObjectMethodSymbol
                ?? FindExtensionMethod(extensionSymbol, name);
            var owningExtension = extensionSymbol
                ?? (methodSymbol?.ContainingScope as ObjectDeclarationScope)?.DeclarationSymbol;

            var funcState = state.Split(ScopeType.StaticMethodDeclaration);
            funcState.EnclosingObject = owningExtension;
            funcState.EnclosingCallable = methodSymbol;
            if (methodSymbol is not null)
            {
                funcState.FunctionGenerics = methodSymbol.GenericParameters;
                GenericConstraintResolver.ResolveAll(methodSymbol.GenericParameters, funcState, context);
                funcState.IsInAsyncContext = methodSymbol.IsAsync;
                funcState.IsInGeneratorContext = methodSymbol.IsGenerator;
            }

            var returnTypeAst = function.ReturnType ?? methodSymbol?.ReturnType;
            funcState.ExpectedReturnType = TypeGuardValidation.ResolveExpectedReturnType(
                returnTypeAst, funcState, context);
            // ExtensionRule suppresses child traversal, so the return type is never CheckNode'd —
            // still count import usage for TYHP4130.
            context.MarkImportNames(returnTypeAst, state);

            RegisterExtensionParameters(
                function.Parameters,
                funcState,
                state,
                context,
                diagnostics);

            funcState.HasReturnedOnAllPaths = false;
            context.CheckStatementBlock(function.Body, funcState);

            if (!IsEffectivelyVoid(funcState.ExpectedReturnType) && !funcState.HasReturnedOnAllPaths)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, function, MessageCode.CheckerMissingReturnStatement, name);
            }

            context.RecordGenericCallTargetsIn(function.Body, funcState);
        }

        private static ObjectMethodSymbol? FindExtensionMethod(
            ObjectDeclarationSymbol? extensionSymbol,
            string name)
        {
            if (extensionSymbol is null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            return extensionSymbol.Members.TryGetValue(name, out var member)
                ? member as ObjectMethodSymbol
                : null;
        }

        private static void RegisterExtensionParameters(
            PhpParameterListAst? parameterList,
            CheckerState funcState,
            CheckerState outerState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (parameterList is null)
            {
                return;
            }

            foreach (var paramAst in parameterList.GetAllNotNull())
            {
                ICheckedType paramType = CheckedTypes.Mixed;
                if (paramAst.Type is not null)
                {
                    funcState.IsParameterTypePosition = true;
                    paramType = context.ResolveTypeAnnotation(paramAst.Type, funcState);
                    funcState.IsParameterTypePosition = false;
                    // ExtensionRule suppresses child traversal, so parameter types are never
                    // CheckNode'd — still count import usage for TYHP4130.
                    context.MarkImportNames(paramAst.Type, outerState);
                }
                else
                {
                    CheckerHelpers.ReportError(
                        diagnostics, outerState, paramAst, MessageCode.CheckerVariableTypeRequired, paramAst.Name);
                }

                var variable = new VariableSymbol(paramAst.Name) { IsParameter = true, IsRef = paramAst.IsRef };
                var variableType = paramAst.IsVariadic
                    ? CallableSignatureReflection.VariadicParameterStorageType(paramType)
                    : paramType;

                funcState.Variables[paramAst.Name.TrimStart('$')] =
                    VariableState.ForParameter(variable, variableType, paramAst.IsRef);
            }
        }

        private static bool IsEffectivelyVoid(ICheckedType? type) =>
            type is null
            || type.Kind == CheckedTypeKind.Void
            || CheckerHelpers.IsBuiltInName(type, "void");

        private static void CheckImportExtension(
            TyhpImportExtensionAst importExtension,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            foreach (var adaptation in importExtension.Adaptations?.GetAllNotNull() ?? [])
            {
                if (adaptation is not PhpTraitAliasAst alias || alias.NewModifier is null)
                {
                    continue;
                }

                var originalName = alias.MethodReference?.MemberName?.Identifier
                    ?? alias.MethodReference?.MemberName?.ValueString
                    ?? string.Empty;
                var aliasName = alias.Identifier;

                if (string.IsNullOrEmpty(aliasName)
                    || string.Equals(aliasName, originalName, StringComparison.OrdinalIgnoreCase))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        alias,
                        MessageCode.CheckerExtensionVisibilityNotAllowed);
                }
            }
        }

        private static bool HasExtendsKeyword(PhpFunctionDeclAst function)
        {
            if (function.AstGrammarAddons.TryGetValue("parameters", out var addon)
                && addon is TokenValueAst token
                && (token.ValueInt64 == TyhpLang.Parser.TyhpParser.T_EXTENDS
                    || string.Equals(token.ValueString, "extends", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }
    }
}
