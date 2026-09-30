using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 21.6 Phase 4 — wrap producer signatures as
    /// <c>\Closure&lt;TCallableShape, TThis, TScope&gt;</c> and classify
    /// <c>fromCallable</c> / array-callable / string-callable arguments.
    /// </summary>
    internal static class ClosureProducerInference
    {
        public static ICheckedType WrapAsClosure(
            ICheckedType callableShape,
            ICheckedType thisType,
            ICheckedType scopeType,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            bool nonRebindable = false)
        {
            var closure = LookupClosureClass(symbolTree, globalScope);
            if (closure.Kind == CheckedTypeKind.Unresolved)
            {
                return callableShape;
            }

            return new GenericCheckedType(
                closure,
                [callableShape, UnwrapLateStatic(thisType), UnwrapLateStatic(scopeType)])
            {
                IsNonRebindableClosure = nonRebindable,
            };
        }

        public static bool IsNonRebindable(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type is GenericCheckedType { IsNonRebindableClosure: true };
        }

        public static ICheckedType UnwrapLateStatic(ICheckedType type) =>
            type is StaticCheckedType staticType ? staticType.DeclaringType : type;

        public static ICheckedType LookupClosureClass(SymbolTree symbolTree, GlobalScope globalScope)
        {
            var closure = CheckerHelpers.ResolveNamedType("Closure", symbolTree, globalScope);
            if (closure.Kind == CheckedTypeKind.Unresolved)
            {
                closure = CheckerHelpers.ResolveNamedType("\\Closure", symbolTree, globalScope);
            }

            return closure;
        }

        public static ICheckedType EnclosingThisType(CheckerState state, bool isStatic)
        {
            if (isStatic)
            {
                return CheckedTypes.Null;
            }

            return EnclosingScopeType(state);
        }

        public static ICheckedType EnclosingScopeType(CheckerState state)
        {
            if (state.EnclosingObjectType is { } objectType
                && !TypeComparer.IsUnresolvedType(objectType))
            {
                return UnwrapLateStatic(objectType);
            }

            return state.EnclosingObject is { } enclosing
                ? CheckedTypes.FromSymbol(enclosing)
                : CheckedTypes.Null;
        }

        public static bool IsFromCallableMethod(ObjectMethodSymbol method)
        {
            if (!string.Equals(method.Name, "fromCallable", StringComparison.Ordinal)
                || !method.IsStatic)
            {
                return false;
            }

            return CallableArityFacetBuilder.IsClosureDeclaration(
                TryGetOwningObject(method));
        }

        public static ObjectDeclarationSymbol? TryGetOwningObject(IBaseSymbol member)
        {
            for (var scope = member.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol objectDecl)
                {
                    return objectDecl;
                }
            }

            return null;
        }

        public static bool TryGetClosureSlots(
            ICheckedType type,
            out ICheckedType callableShape,
            out ICheckedType thisType,
            out ICheckedType scopeType) =>
            TryGetClosureSlots(type, out callableShape, out thisType, out scopeType, out _, out _);

        public static bool TryGetClosureSlots(
            ICheckedType type,
            out ICheckedType callableShape,
            out ICheckedType thisType,
            out ICheckedType scopeType,
            out bool thisSpecified,
            out bool scopeSpecified)
        {
            callableShape = CheckedTypes.Unresolved;
            thisType = CheckedTypes.Null;
            scopeType = CheckedTypes.Null;
            thisSpecified = false;
            scopeSpecified = false;

            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is GenericCheckedType generic
                && CallableArityFacetBuilder.IsClosureTypeName(generic.BaseType)
                && generic.TypeArguments.Count > 0)
            {
                callableShape = generic.TypeArguments[0];
                if (generic.TypeArguments.Count > 1)
                {
                    thisType = generic.TypeArguments[1];
                    thisSpecified = true;
                }

                if (generic.TypeArguments.Count > 2)
                {
                    scopeType = generic.TypeArguments[2];
                    scopeSpecified = true;
                }

                return true;
            }

            if (CallableArityFacetBuilder.IsClosureTypeName(type))
            {
                callableShape = type;
                return true;
            }

            return false;
        }

        public static bool TryGetArrayCallableParts(
            IExpression? expression,
            out IExpression receiverExpr,
            out IExpression methodNameExpr)
        {
            receiverExpr = null!;
            methodNameExpr = null!;
            IReadOnlyList<PhpArrayPairAst> pairs = expression switch
            {
                PhpArrayAst arrayAst => arrayAst.ArrayPairs?.GetAllExcludingSkippedSlots().ToList() ?? [],
                PhpArrayPairListAst pairList => pairList.GetAllExcludingSkippedSlots().ToList(),
                _ => [],
            };

            if (pairs.Count != 2
                || pairs.Any(pair => pair.IsExpansion || pair.KeyExpr is not null || pair.ValueExpr is null))
            {
                return false;
            }

            receiverExpr = pairs[0].ValueExpr!;
            methodNameExpr = pairs[1].ValueExpr!;
            return true;
        }

        public static bool TryGetStringCallableName(ICheckedType type, out string name)
        {
            if (SymbolNameTypeHelper.TryGetStringLiteral(type, out name)
                && !string.IsNullOrEmpty(name))
            {
                return true;
            }

            name = string.Empty;
            return false;
        }

        public static ICheckedType? TryClassFromClassNameBrand(ICheckedType type)
        {
            if (!SymbolNameTypeHelper.TryGetBehavior(type, out var behavior))
            {
                return null;
            }

            if (behavior is not (UtilityBehavior.ClassName
                or UtilityBehavior.InterfaceName
                or UtilityBehavior.EnumName
                or UtilityBehavior.TraitName))
            {
                return null;
            }

            var args = SymbolNameTypeHelper.GetTypeArguments(type);
            return args.Count > 0 ? args[0] : null;
        }

        public static FunctionDeclarationSymbol? ResolveFunctionByName(
            string name,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var trimmed = name.TrimStart('\\');
            if (string.IsNullOrEmpty(trimmed) || trimmed.Contains("::", StringComparison.Ordinal))
            {
                return null;
            }

            var fromScope = (IBaseScope?)state.NameResolutionScope ?? globalScope;
            return symbolTree.ResolveSymbol(trimmed, fromScope, SilentBag)
                    as FunctionDeclarationSymbol
                ?? symbolTree.ResolveQualifiedName(
                    trimmed.Split('\\'), globalScope, SilentBag)
                    as FunctionDeclarationSymbol;
        }

        public static ObjectDeclarationSymbol? ResolveClassByName(
            string name,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var trimmed = name.TrimStart('\\');
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            return symbolTree.ResolveQualifiedName(
                    trimmed.Split('\\'), globalScope, SilentBag)
                as ObjectDeclarationSymbol
                ?? TypeComparer.ResolveObjectType(trimmed, symbolTree, globalScope);
        }

        private static readonly DiagnosticBag SilentBag = new();
    }
}
