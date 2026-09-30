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
            typeof(TyhpdefStandaloneExtensionDeclAst),
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
                case TyhpdefStandaloneExtensionDeclAst tyhpdefExt:
                    CheckStandaloneTyhpdefExtension(tyhpdefExt, state, context, diagnostics);
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
            var extensionSymbol = extension.BoundSymbol as ObjectDeclarationSymbol;
            var members = extension.FunctionList?.GetAllNotNull().ToList() ?? [];
            CheckAuthoredExtension(
                extension,
                extension.Identifier ?? "",
                extension.TargetType,
                extension.GenericParameters,
                extensionSymbol,
                members,
                ExtensionMemberKind.Source,
                state,
                context,
                diagnostics);
        }

        private static void CheckStandaloneTyhpdefExtension(
            TyhpdefStandaloneExtensionDeclAst extension,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var extensionSymbol = extension.BoundSymbol as ObjectDeclarationSymbol;
            var members = extension.FunctionList?.GetAllNotNull().ToList() ?? [];
            CheckAuthoredExtension(
                extension,
                extension.Identifier ?? "",
                extension.TargetType,
                extension.GenericParameters,
                extensionSymbol,
                members,
                ExtensionMemberKind.Tyhpdef,
                state,
                context,
                diagnostics);
        }

        private enum ExtensionMemberKind
        {
            Source,
            Tyhpdef,
        }

        private static void CheckAuthoredExtension(
            IBase2Ast declaration,
            string name,
            ITypeExpression? headerTarget,
            TyhpGenericsTypeArgumentListAst? headerGenerics,
            ObjectDeclarationSymbol? extensionSymbol,
            IReadOnlyList<IBase2Ast> members,
            ExtensionMemberKind kind,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (members.Count == 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    declaration,
                    MessageCode.CheckerEmptyExtension,
                    name);
            }

            ExtensionBlockTargetChecks.CheckSurface(
                declaration,
                name,
                headerTarget,
                headerGenerics,
                extensionSymbol,
                members,
                state,
                context,
                diagnostics);

            var memberGatingState = PhpVersionRule.PushExtensionContainer(state, name, extensionSymbol);
            CheckMemberList(
                members,
                extensionSymbol,
                extensionSymbol,
                memberGatingState,
                kind,
                state,
                context,
                diagnostics);
        }

        private static void CheckMemberList(
            IReadOnlyList<IBase2Ast> members,
            ObjectDeclarationSymbol? blockSymbol,
            ObjectDeclarationSymbol? extensionSymbol,
            CheckerState memberGatingState,
            ExtensionMemberKind kind,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var implementedFunctionNames = OverloadSignatureHelper.CollectImplementedExtensionFunctionNames(
                members.OfType<IExtensionMemberAst>());
            foreach (var member in members)
            {
                switch (member)
                {
                    case PhpFunctionDeclAst function
                        when OverloadSignatureHelper.IsExtensionFunctionOverloadSignature(
                            function, implementedFunctionNames):
                        break;

                    case PhpFunctionDeclAst function:
                        context.ValidatePhpVersionMember(function, memberGatingState);
                        if (kind == ExtensionMemberKind.Source)
                        {
                            CheckExtensionFunction(
                                function, extensionSymbol, blockSymbol, state, context, diagnostics);
                        }
                        else
                        {
                            InlineSpliceRule.CheckMemberDeclaration(function, state, context, diagnostics);
                            ExtensionBlockTargetChecks.CheckMember(
                                function, blockSymbol, extensionSymbol, state, context, diagnostics);
                        }

                        break;

                    case TyhpdefInlineExtensionFunctionAst inline:
                        context.ValidatePhpVersionMember(inline, memberGatingState);
                        InlineSpliceRule.CheckMemberDeclaration(inline, state, context, diagnostics);
                        ExtensionBlockTargetChecks.CheckMember(
                            inline, blockSymbol, extensionSymbol, state, context, diagnostics);
                        break;

                    case TyhpOperatorOverloadAst operatorOverload:
                        // `.tyhp` and standalone `.tyhpdef` operators share this seed.
                        // A thin tyhpdef signature (`operator convert(self $value): int => …`)
                        // is not inside a class, so `self` is CheckerRelativeTypeOutsideClass
                        // unless EnclosingObject / EnclosingObjectType are set to the block
                        // target before OperatorOverloadRule and splice checks resolve it.
                        CheckExtensionOperatorOverload(
                            operatorOverload, extensionSymbol, blockSymbol, state, context, diagnostics);
                        break;

                    case TyhpExtensionDeclAst { IsTargetGroup: true } group:
                        CheckMemberList(
                            group.FunctionList?.GetAllNotNull().ToList() ?? [],
                            group.BoundSymbol as ObjectDeclarationSymbol,
                            extensionSymbol,
                            memberGatingState,
                            kind,
                            state,
                            context,
                            diagnostics);
                        break;
                }
            }
        }

        /// <summary>
        /// Seeds <see cref="CheckerState.EnclosingObject"/> (the extension) and
        /// <see cref="CheckerState.EnclosingObjectType"/> (the block target) before
        /// <see cref="OperatorOverloadRule"/> and splice-member checks run, so
        /// <c>self</c> in a <c>.tyhp</c> or standalone <c>.tyhpdef</c> extension
        /// operator signature means the block target.
        /// </summary>
        private static void CheckExtensionOperatorOverload(
            TyhpOperatorOverloadAst operatorOverload,
            ObjectDeclarationSymbol? extensionSymbol,
            ObjectDeclarationSymbol? blockSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var opState = state.Split(ScopeType.ObjectTypeDeclaration);
            opState.EnclosingObject = extensionSymbol;
            opState.ObjectGenerics = ExtensionBlockTargetChecks.InScopeTypeParameters(blockSymbol, extensionSymbol);
            GenericConstraintResolver.ResolveAll(opState.ObjectGenerics, opState, context);

            var legacyTarget = operatorOverload.ExtensionTargetType;
            var targetAst = legacyTarget ?? blockSymbol?.PendingExtensionBlockTarget;
            if (targetAst is not null)
            {
                var targetType = context.ResolveTypeAnnotation(targetAst, opState);
                if (legacyTarget is not null && IsNonInstantiableExtensionOperatorTarget(targetType))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        targetAst,
                        MessageCode.ExtensionOperatorTargetNotInstantiable,
                        targetType.DisplayName);
                }
                else if (!TypeComparer.IsUnresolvedType(targetType)
                    && !IsNonInstantiableExtensionOperatorTarget(targetType))
                {
                    opState.EnclosingObjectType = targetType;
                }

                if (legacyTarget is not null)
                {
                    context.CheckNode(targetAst, state);
                    context.MarkImportNames(targetAst, state);
                }
            }
            else if (extensionSymbol is not null)
            {
                opState.EnclosingObjectType = CheckedTypes.FromSymbol(extensionSymbol);
            }

            context.CheckNode(operatorOverload, opState);
            ExtensionBlockTargetChecks.CheckMember(
                operatorOverload, blockSymbol, extensionSymbol, opState, context, diagnostics);
        }

        private static void CheckExtensionFunction(
            PhpFunctionDeclAst function,
            ObjectDeclarationSymbol? extensionSymbol,
            ObjectDeclarationSymbol? blockSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var name = function.Identifier ?? string.Empty;

            // Extension functions carry no visibility/static modifiers: the Tyhp grammar override of
            // functionModifiersGrammarAddon only exposes an optional `async`, so public/protected/private/static
            // cannot be written here. They are always emitted as `public static`; nothing to validate.
            InlineSpliceRule.CheckMemberDeclaration(function, state, context, diagnostics);

            var methodSymbol = function.BoundSymbol as ObjectMethodSymbol
                ?? FindExtensionMethod(extensionSymbol, name);
            var owningExtension = extensionSymbol
                ?? (methodSymbol?.ContainingScope as ObjectDeclarationScope)?.DeclarationSymbol;

            var funcState = state.Split(ScopeType.StaticMethodDeclaration);
            funcState.EnclosingObject = owningExtension;
            funcState.EnclosingCallable = methodSymbol;
            funcState.ObjectGenerics = ExtensionBlockTargetChecks.InScopeTypeParameters(
                blockSymbol, extensionSymbol);
            GenericConstraintResolver.ResolveAll(funcState.ObjectGenerics, funcState, context);
            if (blockSymbol?.PendingExtensionBlockTarget is { } blockTarget)
            {
                var targetType = context.ResolveTypeAnnotation(blockTarget, funcState);
                if (!TypeComparer.IsUnresolvedType(targetType)
                    && !IsNonInstantiableExtensionOperatorTarget(targetType))
                {
                    funcState.EnclosingObjectType = targetType;
                }
            }

            if (methodSymbol is not null)
            {
                funcState.FunctionGenerics = methodSymbol.GenericParameters;
                GenericConstraintResolver.ResolveAll(methodSymbol.GenericParameters, funcState, context);
                funcState.IsInAsyncContext = methodSymbol.IsAsync;
                funcState.IsInGeneratorContext = methodSymbol.IsGenerator;
                if (methodSymbol.IsGenerator)
                {
                    funcState.GeneratorInference = GeneratorBodyCollector.ForDeclaredReturn(
                        function.ReturnType ?? methodSymbol.ReturnType);
                }

                SeedExtensionReceiver(methodSymbol, funcState, context);
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

            if (function.Body is null)
            {
                ExtensionBlockTargetChecks.CheckMember(
                    function, blockSymbol, extensionSymbol, funcState, context, diagnostics);
                return;
            }

            if (methodSymbol is not null)
            {
                CheckerHelpers.FlagGenericVariantIfNeeded(
                    function.Body, methodSymbol, methodSymbol.GenericParameters, context);
            }

            funcState.HasReturnedOnAllPaths = false;
            context.CheckStatementBlock(function.Body, funcState);

            if (methodSymbol?.IsGenerator != true
                && !IsEffectivelyVoid(funcState.ExpectedReturnType) && !funcState.HasReturnedOnAllPaths)
            {
                // See the matching generator exemption in DeclarationRule.Callable.CheckFunction —
                // falling off the end of a generator extension method is not a missing return.
                CheckerHelpers.ReportError(
                    diagnostics, state, function, MessageCode.CheckerMissingReturnStatement, name);
            }

            context.RecordGenericCallTargetsIn(function.Body, funcState);

            if (methodSymbol?.IsGenerator == true)
            {
                GeneratorBodyInference.FinishAfterBody(
                    function,
                    function.ReturnType ?? methodSymbol.ReturnType,
                    methodSymbol,
                    closure: null,
                    funcState,
                    context,
                    diagnostics);
            }

            ExtensionBlockTargetChecks.CheckMember(
                function, blockSymbol, extensionSymbol, funcState, context, diagnostics);
        }

        private static void SeedExtensionReceiver(
            ObjectMethodSymbol methodSymbol,
            CheckerState funcState,
            CheckerRuleContext context)
        {
            var receiver = methodSymbol.Parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name, "$this", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.Name, "this", StringComparison.OrdinalIgnoreCase));
            if (receiver is null)
            {
                return;
            }

            var receiverType = receiver.DeclaredType is not null
                ? context.ResolveTypeAnnotation(receiver.DeclaredType, funcState)
                : funcState.EnclosingObjectType ?? CheckedTypes.Mixed;
            funcState.Variables["this"] = VariableState.ForParameter(
                new VariableSymbol("this") { IsParameter = true, IsRef = receiver.IsByReference },
                receiverType,
                receiver.IsByReference);
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
                        diagnostics, outerState, paramAst, MessageCode.CheckerVariableTypeRequired,
                        CheckerHelpers.FormatTypeRequiredName(paramAst.Name));
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

        /// <summary>
        /// Builtins that cannot be a value-bearing extension-operator <c>self</c>. Matches
        /// <see cref="BuiltInTypeSymbol.IsNonInstantiableExtensionOperatorTarget"/>.
        /// </summary>
        private static bool IsNonInstantiableExtensionOperatorTarget(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TypeComparer.IsVoidType(type)
                || TypeComparer.IsNeverType(type)
                || TypeComparer.IsMixedType(type)
                || TypeComparer.IsNullLiteral(type))
            {
                return true;
            }

            return CheckerHelpers.IsBuiltInName(type, "void")
                || CheckerHelpers.IsBuiltInName(type, "never")
                || CheckerHelpers.IsBuiltInName(type, "null")
                || CheckerHelpers.IsBuiltInName(type, "mixed")
                || CheckerHelpers.IsBuiltInName(type, "resource")
                || CheckerHelpers.IsBuiltInName(type, "true")
                || CheckerHelpers.IsBuiltInName(type, "false");
        }

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

                var memberName = alias.MethodReference?.MemberName;
                var originalName = (string.IsNullOrEmpty(memberName?.Identifier)
                    ? memberName?.ValueString
                    : memberName.Identifier) ?? string.Empty;
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

    }
}
