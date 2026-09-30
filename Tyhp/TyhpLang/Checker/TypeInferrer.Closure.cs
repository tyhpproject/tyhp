using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 21.6 Phase 4 — Closure producer inference (literals, FCC, <c>fromCallable</c>).
    /// </summary>
    public sealed partial class TypeInferrer
    {
        private ICheckedType WrapFunctionCallableAsClosure(ICheckedType shape, bool nonRebindable = true) =>
            ClosureProducerInference.WrapAsClosure(
                shape,
                CheckedTypes.Null,
                CheckedTypes.Null,
                _symbolTree,
                _globalScope,
                nonRebindable);

        private ICheckedType WrapMethodCallableAsClosure(
            ICheckedType shape,
            ICheckedType receiverType,
            string methodName,
            bool staticOnly,
            CheckerState state)
        {
            var thisType = staticOnly ? CheckedTypes.Null : receiverType;
            // TScope is always the class that physically declares the method (PHP
            // ReflectionFunction::getClosureScopeClass()), not the class name referenced at the
            // call site — `Foo::inherited(...)` where `inherited` lives on `Base` reports scope
            // `Base`, for both static and instance FCC/fromCallable.
            var scopeType = receiverType;
            if (TryResolveMethodOnType(receiverType, methodName, staticOnly, state, out var method)
                && method is not null
                && FindDeclaringClass(receiverType, method) is { } declaring)
            {
                scopeType = CheckedTypes.FromSymbol(declaring);
            }

            return ClosureProducerInference.WrapAsClosure(
                shape, thisType, scopeType, _symbolTree, _globalScope, nonRebindable: true);
        }

        /// <summary>
        /// <c>Closure::fromCallable($cb)</c> result: <c>TCallableShape</c> from <c>$cb</c>,
        /// <c>TThis</c>/<c>TScope</c> from <c>__CallableThis</c>/<c>__CallableScope</c> of the
        /// original callback (not the caller's <c>__CurrentScope</c>). Invokable objects store
        /// the <c>__invoke</c> arity intersection and drop the class from <c>TCallableShape</c>.
        /// </summary>
        private ICheckedType? TryInferFromCallableResult(PhpCallAst call, CheckerState state)
        {
            var argument = GetFirstValueArgument(call);
            if (argument is null)
            {
                return null;
            }

            if (ClosureProducerInference.TryGetArrayCallableParts(
                    argument, out var receiverExpr, out var methodNameExpr)
                && TryInferFromArrayCallable(receiverExpr, methodNameExpr, state, out var arrayResult))
            {
                return arrayResult;
            }

            var callbackType = InferExpressionType(argument, state);
            if (TryInferFromStringCallable(callbackType, state, out var stringResult))
            {
                return stringResult;
            }

            if (ClosureProducerInference.TryGetClosureSlots(
                    callbackType, out var innerShape, out var innerThis, out var innerScope)
                && innerShape.Kind != CheckedTypeKind.Unresolved)
            {
                return ClosureProducerInference.WrapAsClosure(
                    innerShape,
                    innerThis,
                    innerScope,
                    _symbolTree,
                    _globalScope,
                    nonRebindable: ClosureProducerInference.IsNonRebindable(callbackType));
            }

            if (TryInferInvokableFromCallable(callbackType, state, out var invokableResult))
            {
                return invokableResult;
            }

            if (CallableArityFacetBuilder.IsCallableFacetType(callbackType)
                && !CallableArityFacetBuilder.IsClosureTypeName(callbackType))
            {
                var thisType = MagicUtilityTypeResolver.ResolveCallableThisCore(
                    callbackType, _symbolTree, _globalScope);
                var scopeType = MagicUtilityTypeResolver.ResolveCallableScopeCore(
                    callbackType, _symbolTree, _globalScope);
                return ClosureProducerInference.WrapAsClosure(
                    callbackType, thisType, scopeType, _symbolTree, _globalScope, nonRebindable: true);
            }

            return null;
        }

        private bool TryInferFromArrayCallable(
            IExpression receiverExpr,
            IExpression methodNameExpr,
            CheckerState state,
            out ICheckedType result)
        {
            result = CheckedTypes.Unresolved;
            var receiverType = InferExpressionType(receiverExpr, state);
            var methodNameType = InferExpressionType(methodNameExpr, state);
            if (!ClosureProducerInference.TryGetStringCallableName(methodNameType, out var methodName))
            {
                return false;
            }

            var classFromBrand = ClosureProducerInference.TryClassFromClassNameBrand(receiverType);
            if (classFromBrand is not null)
            {
                var shape = InferCallableFromMethod(classFromBrand, methodName, staticOnly: true, state);
                if (shape.Kind == CheckedTypeKind.Unresolved)
                {
                    return false;
                }

                result = WrapMethodCallableAsClosure(
                    shape, classFromBrand, methodName, staticOnly: true, state);
                return true;
            }

            if (ClosureProducerInference.TryGetStringCallableName(receiverType, out var className)
                && ClosureProducerInference.ResolveClassByName(className, _symbolTree, _globalScope)
                    is { } namedClass)
            {
                var classType = CheckedTypes.FromSymbol(namedClass);
                var shape = InferCallableFromMethod(classType, methodName, staticOnly: true, state);
                if (shape.Kind == CheckedTypeKind.Unresolved)
                {
                    return false;
                }

                result = WrapMethodCallableAsClosure(
                    shape, classType, methodName, staticOnly: true, state);
                return true;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(receiverType) is not null)
            {
                var instanceShape = InferCallableFromMethod(
                    receiverType, methodName, staticOnly: false, state);
                if (instanceShape.Kind != CheckedTypeKind.Unresolved)
                {
                    result = WrapMethodCallableAsClosure(
                        instanceShape, receiverType, methodName, staticOnly: false, state);
                    return true;
                }

                var staticShape = InferCallableFromMethod(
                    receiverType, methodName, staticOnly: true, state);
                if (staticShape.Kind != CheckedTypeKind.Unresolved)
                {
                    result = WrapMethodCallableAsClosure(
                        staticShape, receiverType, methodName, staticOnly: true, state);
                    return true;
                }
            }

            return false;
        }

        private bool TryInferFromStringCallable(
            ICheckedType callbackType,
            CheckerState state,
            out ICheckedType result)
        {
            result = CheckedTypes.Unresolved;
            if (!ClosureProducerInference.TryGetStringCallableName(callbackType, out var name))
            {
                return false;
            }

            var split = name.Split(["::"], 2, StringSplitOptions.None);
            if (split.Length == 2)
            {
                var classType = ClosureProducerInference.ResolveClassByName(
                    split[0], _symbolTree, _globalScope);
                if (classType is null)
                {
                    return false;
                }

                var owner = CheckedTypes.FromSymbol(classType);
                var shape = InferCallableFromMethod(owner, split[1], staticOnly: true, state);
                if (shape.Kind == CheckedTypeKind.Unresolved)
                {
                    return false;
                }

                result = WrapMethodCallableAsClosure(
                    shape, owner, split[1], staticOnly: true, state);
                return true;
            }

            var function = ClosureProducerInference.ResolveFunctionByName(
                name, state, _symbolTree, _globalScope);
            if (function is null)
            {
                return false;
            }

            result = WrapFunctionCallableAsClosure(InferCallableFromFunction(function, state));
            return true;
        }

        private bool TryInferInvokableFromCallable(
            ICheckedType callbackType,
            CheckerState state,
            out ICheckedType result)
        {
            result = CheckedTypes.Unresolved;
            while (callbackType is NullableCheckedType nullable)
            {
                callbackType = nullable.InnerType;
            }

            if (CallableArityFacetBuilder.IsClosureTypeName(callbackType)
                || CheckerHelpers.TryGetObjectDeclaration(callbackType) is null)
            {
                return false;
            }

            var invoke = InferCallableFromMethod(callbackType, "__invoke", staticOnly: false, state);
            if (invoke.Kind == CheckedTypeKind.Unresolved
                || !CallableArityFacetBuilder.IsCallableFacetType(invoke))
            {
                return false;
            }

            var thisType = MagicUtilityTypeResolver.ResolveCallableThisCore(
                callbackType, _symbolTree, _globalScope);
            var scopeType = MagicUtilityTypeResolver.ResolveCallableScopeCore(
                callbackType, _symbolTree, _globalScope);
            result = ClosureProducerInference.WrapAsClosure(
                invoke, thisType, scopeType, _symbolTree, _globalScope, nonRebindable: true);
            return true;
        }

        private static IExpression? GetFirstValueArgument(PhpCallAst call)
        {
            if (call.Arguments is null)
            {
                return null;
            }

            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                if (arg.IsVariadic || arg.Expression is null)
                {
                    continue;
                }

                return arg.Expression;
            }

            return null;
        }
    }
}
