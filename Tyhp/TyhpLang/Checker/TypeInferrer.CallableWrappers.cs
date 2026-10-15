using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        /// <summary>One declared signature of a source overload set, as the wrapper planning sees it.</summary>
        private sealed record OverloadSignature(
            IReadOnlyList<ParameterInfo> Parameters,
            IReadOnlyList<ICheckedType> Types,
            ICheckedType ReturnType);

        private List<ICheckedType> ResolveFunctionParameterTypes(
            FunctionDeclarationSymbol function,
            CheckerState resolveState) =>
            function.Parameters
                .Select(param => param.DeclaredType is not null
                    ? ResolveTypeExpression(param.DeclaredType, resolveState)
                    : CheckedTypes.Unresolved)
                .ToList();

        private List<ICheckedType> ResolveMethodParameterTypes(
            IReadOnlyList<ParameterInfo> callSiteParameters,
            ObjectMethodSymbol method,
            ICheckedType ownerType,
            ObjectDeclarationSymbol? declaringClass,
            CheckerState state) =>
            callSiteParameters
                .Select(param => param.DeclaredType is not null
                    ? ResolveDeclaredTypeOnReceiver(
                        param.DeclaredType,
                        ownerType,
                        state,
                        declaringClass: declaringClass,
                        functionGenerics: method.GenericParameters)
                    : CheckedTypes.Unresolved)
                .ToList();

        private static CheckerState WithFunctionGenerics(
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            CheckerState state)
        {
            if (generics.Count == 0)
            {
                return state;
            }

            var forked = state.Fork();
            forked.FunctionGenerics = generics;
            return forked;
        }

        private ICheckedType ResolveFunctionReturnType(FunctionDeclarationSymbol function, CheckerState resolveState) =>
            function.ReturnType switch
            {
                null => CheckedTypes.Mixed,
                TyhpReturnTypeGuardAst => CheckedTypes.Bool,
                { } declared => ResolveTypeExpression(declared, resolveState, isReturnTypePosition: true),
            };

        /// <summary>
        /// The wrapper plan of the first-class callable <c>f(...)</c>: which parameters accept a
        /// type that converts to the declared one. For a source overload set the positions are
        /// planned across its declared signatures (<see cref="BuildOverloadSetWrapperPlan"/>).
        /// Null when no parameter has a convertible source.
        /// </summary>
        private CallableWrapperPlan? BuildFunctionWrapperPlan(
            FunctionDeclarationSymbol function,
            CheckerState state,
            PhpCallAst call)
        {
            var resolveState = WithFunctionGenerics(function.GenericParameters, state);
            var parameterTypes = ResolveFunctionParameterTypes(function, resolveState);
            var returnType = function.ReturnType is null
                ? null
                : ResolveFunctionReturnType(function, resolveState);
            var shapeReturnType = returnType ?? CheckedTypes.Mixed;

            if (CallableOverloadSet.AppliesTo(function))
            {
                var signatures = Rules.CheckerHelpers.EnumerateFunctionSignatures(function)
                    .Select(signature =>
                    {
                        var signatureState = WithFunctionGenerics(signature.GenericParameters, state);
                        return new OverloadSignature(
                            signature.Parameters,
                            ResolveFunctionParameterTypes(signature, signatureState),
                            ResolveFunctionReturnType(signature, signatureState));
                    })
                    .ToList();
                return BuildOverloadSetWrapperPlan(
                    function.Name,
                    call,
                    state,
                    function,
                    function.Parameters,
                    parameterTypes,
                    signatures,
                    isExtensionMethod: false,
                    declaredReturn: function.ReturnType,
                    returnType: returnType,
                    shapeReturnType: shapeReturnType,
                    isAsync: function.IsAsync,
                    isGenerator: function.IsGenerator,
                    isGenericCallee: function.GenericParameters.Count > 0,
                    isInvokeCall: false);
            }

            return FinishWrapperPlan(
                function,
                function.Parameters,
                parameterTypes,
                PlanConvertibleParameters(function.Parameters, parameterTypes, state),
                isExtensionMethod: false,
                declaredReturn: function.ReturnType,
                returnType: returnType,
                shapeReturnType: shapeReturnType,
                isAsync: function.IsAsync,
                isGenerator: function.IsGenerator,
                isGenericCallee: function.GenericParameters.Count > 0,
                isInvokeCall: false,
                overloadSetType: null);
        }

        private CallableWrapperPlan? BuildMethodWrapperPlan(
            ICheckedType ownerType,
            string methodName,
            bool staticOnly,
            CheckerState state,
            PhpCallAst call,
            bool isInvokeCall = false)
        {
            if (TypeComparer.TryFindShapeInstanceMethod(
                    ownerType, methodName, _symbolTree, _globalScope, out _, out _)
                || !TryResolveMethodOnType(ownerType, methodName, staticOnly, state, out var method)
                || method is null)
            {
                return null;
            }

            var declaringClass = FindDeclaringClass(ownerType, method);
            var callSiteParameters = Rules.CheckerHelpers.ExcludeExtensionReceiver(method.Parameters, method);
            var parameterTypes = ResolveMethodParameterTypes(callSiteParameters, method, ownerType, declaringClass, state);
            var shapeReturnType = ResolveMethodReturnType(method, ownerType, state);
            var returnType = method.ReturnType is null ? null : shapeReturnType;
            var isExtensionMethod = callSiteParameters.Count != method.Parameters.Count;

            if (CallableOverloadSet.AppliesTo(method))
            {
                var signatures = Rules.CheckerHelpers.EnumerateMethodSignatures(method)
                    .Select(signature =>
                    {
                        var signatureParameters = Rules.CheckerHelpers.ExcludeExtensionReceiver(
                            signature.Parameters, signature);
                        return new OverloadSignature(
                            signatureParameters,
                            ResolveMethodParameterTypes(
                                signatureParameters, signature, ownerType, declaringClass, state),
                            ResolveMethodReturnType(signature, ownerType, state));
                    })
                    .ToList();
                return BuildOverloadSetWrapperPlan(
                    methodName,
                    call,
                    state,
                    method,
                    callSiteParameters,
                    parameterTypes,
                    signatures,
                    isExtensionMethod: isExtensionMethod,
                    declaredReturn: method.ReturnType,
                    returnType: returnType,
                    shapeReturnType: shapeReturnType,
                    isAsync: method.IsAsync,
                    isGenerator: method.IsGenerator,
                    isGenericCallee: method.GenericParameters.Count > 0,
                    isInvokeCall: isInvokeCall);
            }

            return FinishWrapperPlan(
                method,
                callSiteParameters,
                parameterTypes,
                PlanConvertibleParameters(callSiteParameters, parameterTypes, state),
                isExtensionMethod: isExtensionMethod,
                declaredReturn: method.ReturnType,
                returnType: returnType,
                shapeReturnType: shapeReturnType,
                isAsync: method.IsAsync,
                isGenerator: method.IsGenerator,
                isGenericCallee: method.GenericParameters.Count > 0,
                isInvokeCall: isInvokeCall,
                overloadSetType: null);
        }

        /// <summary>The parameters of one signature that accept a convertible source.</summary>
        private List<ConvertibleParameterPlan>? PlanConvertibleParameters(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            CheckerState state)
        {
            List<ConvertibleParameterPlan>? planned = null;
            for (var index = 0; index < parameters.Count && index < parameterTypes.Count; index++)
            {
                var parameter = parameters[index];
                var target = parameterTypes[index];

                // A by-reference parameter cannot be converted before the call, and a variadic
                // one is forwarded as is: neither is widened.
                if (parameter.IsVariadic
                    || parameter.IsByReference
                    || ContainsGenericParameter(target))
                {
                    continue;
                }

                var sources = TypeComparer.EnumerateConvertibleSources(
                    target,
                    _globalScope,
                    declared => ResolveTypeExpression(declared, state));
                if (sources.Count == 0)
                {
                    continue;
                }

                planned ??= [];
                planned.Add(new ConvertibleParameterPlan
                {
                    Index = index,
                    Target = target,
                    Sources = sources,
                    WidenedType = WidenWithSources(target, sources),
                });
            }

            return planned;
        }

        /// <summary>
        /// The plan of a first-class callable of a source overload set. The wrapper calls the
        /// implementation, which takes the union of the declared signatures' parameter types, so
        /// conversions are decided <b>per parameter position</b> across the signatures:
        /// <list type="bullet">
        /// <item>a type some signature accepts at that position as written is never converted
        /// (the exact match wins, as in the first pass of an ordinary call);</item>
        /// <item>any other type that converts to a signature's parameter is converted to it, and the
        /// signature's facet in the callable value accepts it;</item>
        /// <item>a type that would convert to different types in different signatures is an error
        /// (TYHP4376) and is not widened.</item>
        /// </list>
        /// Null when no position converts anything (and the callee is not an extension method).
        /// </summary>
        private CallableWrapperPlan? BuildOverloadSetWrapperPlan(
            string calleeName,
            PhpCallAst call,
            CheckerState state,
            IBaseSymbol callee,
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            IReadOnlyList<OverloadSignature> signatures,
            bool isExtensionMethod,
            Ast.Interfaces.ITypeExpression? declaredReturn,
            ICheckedType? returnType,
            ICheckedType shapeReturnType,
            bool isAsync,
            bool isGenerator,
            bool isGenericCallee,
            bool isInvokeCall)
        {
            List<ConvertibleParameterPlan>? planned = null;
            var widenedPerSignature = signatures.Select(s => s.Types.ToList()).ToList();

            for (var index = 0; index < parameters.Count && index < parameterTypes.Count; index++)
            {
                if (parameters[index].IsVariadic
                    || parameters[index].IsByReference
                    || ContainsGenericParameter(parameterTypes[index]))
                {
                    continue;
                }

                var atPosition = new List<(int Signature, ICheckedType Type)>();
                var usable = true;
                for (var s = 0; s < signatures.Count && usable; s++)
                {
                    var signature = signatures[s];
                    if (index >= signature.Parameters.Count)
                    {
                        continue;
                    }

                    if (signature.Parameters[index].IsVariadic
                        || signature.Parameters[index].IsByReference
                        || index >= signature.Types.Count
                        || ContainsGenericParameter(signature.Types[index]))
                    {
                        usable = false;
                        break;
                    }

                    atPosition.Add((s, signature.Types[index]));
                }

                if (!usable || atPosition.Count == 0)
                {
                    continue;
                }

                var exactTypes = atPosition.Select(a => a.Type).Append(parameterTypes[index]).ToList();
                var found = new List<(int Signature, ConvertibleSource Source)>();
                foreach (var (signatureIndex, type) in atPosition)
                {
                    foreach (var source in TypeComparer.EnumerateConvertibleSources(
                        type,
                        _globalScope,
                        declared => ResolveTypeExpression(declared, state)))
                    {
                        if (exactTypes.Any(t => TypeComparer.IsAssignableTo(
                                source.SourceType, t, _symbolTree, _globalScope)))
                        {
                            continue;
                        }

                        found.Add((signatureIndex, source));
                    }
                }

                var accepted = new List<(int Signature, ConvertibleSource Source)>();
                foreach (var group in found.GroupBy(f => f.Source.SourceType.DisplayName))
                {
                    var targets = group
                        .Select(g => g.Source.TargetKey)
                        .Distinct(System.StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (targets.Count > 1)
                    {
                        ReportDiagnostic(
                            call,
                            state,
                            MessageCode.CheckerOverloadCallableAmbiguousConversion,
                            calleeName,
                            group.Key,
                            index + 1,
                            string.Join(", ", targets));
                        continue;
                    }

                    accepted.AddRange(group);
                }

                if (accepted.Count == 0)
                {
                    continue;
                }

                foreach (var perSignature in accepted.GroupBy(a => a.Signature))
                {
                    widenedPerSignature[perSignature.Key][index] = WidenWithSources(
                        signatures[perSignature.Key].Types[index],
                        perSignature.Select(a => a.Source).ToList());
                }

                var sources = accepted
                    .Select(a => a.Source)
                    .GroupBy(s => (s.Kind, s.SourceType.DisplayName, s.TargetKey))
                    .Select(g => g.First())
                    .ToList();
                planned ??= [];
                planned.Add(new ConvertibleParameterPlan
                {
                    Index = index,
                    Target = parameterTypes[index],
                    Sources = sources,
                    WidenedType = WidenWithSources(parameterTypes[index], sources),
                });
            }

            ICheckedType? overloadSetType = null;
            if (planned is not null)
            {
                overloadSetType = CallableOverloadSet.Combine(
                    signatures
                        .Select((signature, s) => CallableArityFacetBuilder.BuildFromParameterInfos(
                            signature.Parameters, widenedPerSignature[s], signature.ReturnType))
                        .ToList());
            }

            return FinishWrapperPlan(
                callee,
                parameters,
                parameterTypes,
                planned,
                isExtensionMethod,
                declaredReturn,
                returnType,
                shapeReturnType,
                isAsync,
                isGenerator,
                isGenericCallee,
                isInvokeCall,
                overloadSetType);
        }

        private CallableWrapperPlan? FinishWrapperPlan(
            IBaseSymbol callee,
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<ICheckedType> parameterTypes,
            List<ConvertibleParameterPlan>? planned,
            bool isExtensionMethod,
            Ast.Interfaces.ITypeExpression? declaredReturn,
            ICheckedType? returnType,
            ICheckedType shapeReturnType,
            bool isAsync,
            bool isGenerator,
            bool isGenericCallee,
            bool isInvokeCall,
            ICheckedType? overloadSetType)
        {
            if (planned is null && !isExtensionMethod)
            {
                return null;
            }

            var returnsNothing = returnType is { Kind: CheckedTypeKind.Void or CheckedTypeKind.Never };
            var wrapperReturn = returnType is null
                || returnType.Kind == CheckedTypeKind.Unresolved
                || isAsync
                || isGenerator
                || DeclaresStaticReturn(declaredReturn)
                || ContainsGenericParameter(returnType)
                    ? null
                    : returnType;

            return new CallableWrapperPlan
            {
                Callee = callee,
                CallSiteParameters = parameters,
                ParameterTypes = parameterTypes,
                WrapperParameterTypes = parameterTypes
                    .Select(t => ContainsGenericParameter(t) ? null : t)
                    .ToList(),
                ReturnType = wrapperReturn,
                ShapeReturnType = shapeReturnType,
                ReturnsNothing = returnsNothing && !isAsync && !isGenerator,
                IsExtensionMethod = isExtensionMethod,
                IsGenericCallee = isGenericCallee,
                IsInvokeCall = isInvokeCall,
                OverloadSetType = overloadSetType,
                Parameters = planned ?? [],
            };
        }

        /// <summary>
        /// True when a declared return type names <c>static</c>: PHP rejects it on a closure
        /// outside the class, so the wrapper declares no return type.
        /// </summary>
        private static bool DeclaresStaticReturn(Ast.Interfaces.ITypeExpression? type) =>
            type switch
            {
                PhpBuiltinTypeAst builtin => string.Equals(builtin.Identifier, "static", System.StringComparison.OrdinalIgnoreCase),
                PhpNamedTypeAst named => string.Equals(
                    GetExpressionText(named.Name), "static", System.StringComparison.OrdinalIgnoreCase),
                PhpTypeExpressionAst composite => composite.Types?.GetAllNotNull().Any(DeclaresStaticReturn) ?? false,
                _ => false,
            };

        /// <summary>
        /// The callable shape of a wrapped first-class callable: the callee's parameters, each
        /// widened by the types that convert to it, so a call through the value type-checks the
        /// way the wrapper behaves at runtime. A source overload set keeps one facet per signature.
        /// </summary>
        private static ICheckedType BuildWidenedCallableShape(CallableWrapperPlan plan)
        {
            if (plan.OverloadSetType is { } overloadSet)
            {
                return overloadSet;
            }

            var parameterTypes = new List<ICheckedType>(plan.ParameterTypes.Count);
            for (var index = 0; index < plan.ParameterTypes.Count; index++)
            {
                parameterTypes.Add(plan.ForIndex(index)?.WidenedType ?? plan.ParameterTypes[index]);
            }

            return CallableArityFacetBuilder.BuildFromParameterInfos(
                plan.CallSiteParameters,
                parameterTypes,
                plan.ShapeReturnType);
        }

        private static ICheckedType WidenWithSources(
            ICheckedType target,
            IReadOnlyList<ConvertibleSource> sources)
        {
            var members = new List<ICheckedType>();
            var nullable = target is NullableCheckedType;
            members.Add(target is NullableCheckedType wrapped ? wrapped.InnerType : target);
            members.AddRange(sources.Select(s => s.SourceType));
            var widened = CheckedTypes.UnionTypes(members);
            return nullable ? new NullableCheckedType(widened) : widened;
        }

        private static bool ContainsGenericParameter(ICheckedType type) =>
            type switch
            {
                SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol } => true,
                NullableCheckedType nullable => ContainsGenericParameter(nullable.InnerType),
                UnionCheckedType union => union.Members.Any(ContainsGenericParameter),
                GenericCheckedType generic => ContainsGenericParameter(generic.BaseType)
                    || generic.TypeArguments.Any(ContainsGenericParameter),
                _ => false,
            };
    }
}
