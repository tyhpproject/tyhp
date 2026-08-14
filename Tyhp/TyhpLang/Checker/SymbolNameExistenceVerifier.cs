using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Verifies that a string literal names a real in-scope symbol for Story 08.5 symbol-name types.
    /// </summary>
    internal static class SymbolNameExistenceVerifier
    {
        private static readonly DiagnosticBag SilentDiagnostics = new();

        public static bool VerifyLiteral(
            string literalValue,
            ICheckedType targetType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!SymbolNameTypeHelper.TryGetBehavior(targetType, out var behavior))
            {
                return true;
            }

            var fromScope = GetResolutionScope(state, globalScope);
            var resolver = new NameResolver(symbolTree, SilentDiagnostics);
            var typeArgs = SymbolNameTypeHelper.GetTypeArguments(targetType);

            return behavior switch
            {
                UtilityBehavior.ClassName => VerifyObjectBrandLiteral(
                    literalValue, typeArgs, fromScope, resolver, PhpTypeDeclType.Class),
                UtilityBehavior.EnumName => VerifyObjectBrandLiteral(
                    literalValue, typeArgs, fromScope, resolver, PhpTypeDeclType.Enum),
                UtilityBehavior.InterfaceName => VerifyObjectBrandLiteral(
                    literalValue, typeArgs, fromScope, resolver, PhpTypeDeclType.Interface),
                UtilityBehavior.TraitName => VerifyObjectBrandLiteral(
                    literalValue, typeArgs, fromScope, resolver, PhpTypeDeclType.Trait),
                UtilityBehavior.StructName => IsStructName(literalValue, fromScope, resolver),
                UtilityBehavior.FunctionName => IsFunction(literalValue, fromScope, resolver),
                UtilityBehavior.ConstName => IsConstant(literalValue, fromScope, resolver),
                UtilityBehavior.VarName or UtilityBehavior.TypedVarName =>
                    IsVariable(literalValue, fromScope, resolver),
                UtilityBehavior.PropertyName when typeArgs.Count > 0 =>
                    IsMemberName(literalValue, typeArgs[0], isProperty: true),
                UtilityBehavior.MethodName when typeArgs.Count > 0 =>
                    IsMemberName(literalValue, typeArgs[0], isProperty: false),
                UtilityBehavior.ObjectConstName when typeArgs.Count > 0 =>
                    IsObjectConstant(literalValue, typeArgs[0]),
                UtilityBehavior.EnumCaseName when typeArgs.Count > 0 =>
                    IsEnumCase(literalValue, typeArgs[0]),
                UtilityBehavior.CompatibleTypeName when typeArgs.Count > 0 =>
                    IsCompatibleTypeName(literalValue, typeArgs[0], fromScope, resolver, symbolTree, globalScope),
                UtilityBehavior.UsedTraitName when typeArgs.Count > 0 =>
                    IsUsedTrait(literalValue, typeArgs[0], fromScope, resolver, symbolTree, globalScope),
                _ => false,
            };
        }

        /// <summary>
        /// Existence check for optional-single object brands. When the type argument is a concrete
        /// declaration (e.g. <c>__ClassName&lt;User&gt;</c>), the literal must name that declaration —
        /// not merely some class of the right kind.
        /// </summary>
        private static bool VerifyObjectBrandLiteral(
            string literalValue,
            IReadOnlyList<ICheckedType> typeArgs,
            IBaseScope fromScope,
            NameResolver resolver,
            PhpTypeDeclType expectedKind)
        {
            if (!IsObjectKind(literalValue, fromScope, resolver, expectedKind))
            {
                return false;
            }

            if (typeArgs.Count == 0)
            {
                return true;
            }

            var typeArg = typeArgs[0];
            if (typeArg is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol { Name: var builtIn } }
                && string.Equals(builtIn, "object", StringComparison.OrdinalIgnoreCase))
            {
                // `__ClassName<object>` / normalized bare — any matching kind is enough.
                return true;
            }

            var expectedDecl = CheckerHelpers.TryGetObjectDeclaration(typeArg);
            if (expectedDecl is null)
            {
                // Generic type parameter or unresolved arg: kind existence only.
                return true;
            }

            var literalSymbol = ResolveName(literalValue, fromScope, resolver);
            return literalSymbol is ObjectDeclarationSymbol named
                && string.Equals(
                    named.FullyQualifiedName,
                    expectedDecl.FullyQualifiedName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static IBaseScope GetResolutionScope(CheckerState state, GlobalScope globalScope)
        {
            if (state.EnclosingFunction?.ContainingScope is IBaseScope functionScope)
            {
                return functionScope;
            }

            if (state.EnclosingObject?.ContainingScope is IBaseScope objectScope)
            {
                return objectScope;
            }

            return globalScope;
        }

        private static IBaseSymbol? ResolveName(
            string literalValue,
            IBaseScope fromScope,
            NameResolver resolver)
        {
            var segments = ParseQualifiedName(literalValue);
            if (segments.Length == 0)
            {
                return null;
            }

            if (segments.Length == 1)
            {
                return resolver.ResolveSymbol(segments[0], fromScope)
                    ?? resolver.ResolveRelativeName(segments, fromScope);
            }

            return resolver.ResolveQualifiedName(segments)
                ?? resolver.ResolveRelativeName(segments, fromScope);
        }

        private static string[] ParseQualifiedName(string literalValue)
        {
            var trimmed = literalValue.Trim();
            if (trimmed.StartsWith('\\'))
            {
                trimmed = trimmed[1..];
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                return [];
            }

            return trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool IsObjectKind(
            string literalValue,
            IBaseScope fromScope,
            NameResolver resolver,
            PhpTypeDeclType expectedKind)
        {
            var symbol = ResolveName(literalValue, fromScope, resolver);
            return symbol is ObjectDeclarationSymbol { ObjectKind: var kind } && kind == expectedKind;
        }

        private static bool IsStructName(string literalValue, IBaseScope fromScope, NameResolver resolver)
        {
            var symbol = ResolveName(literalValue, fromScope, resolver);
            return symbol is ObjectDeclarationSymbol { IsStruct: true };
        }

        private static bool IsFunction(string literalValue, IBaseScope fromScope, NameResolver resolver)
        {
            var symbol = ResolveName(literalValue, fromScope, resolver);
            return symbol is FunctionDeclarationSymbol;
        }

        private static bool IsConstant(string literalValue, IBaseScope fromScope, NameResolver resolver)
        {
            var symbol = ResolveName(literalValue, fromScope, resolver);
            return symbol is ConstantSymbol or ObjectConstantSymbol;
        }

        private static bool IsVariable(string literalValue, IBaseScope fromScope, NameResolver resolver)
        {
            var varName = literalValue.StartsWith('$') ? literalValue : "$" + literalValue;
            return resolver.ResolveSymbol(varName, fromScope) is VariableSymbol;
        }

        private static bool IsMemberName(string literalValue, ICheckedType ownerType, bool isProperty)
        {
            var owner = CheckerHelpers.TryGetObjectDeclaration(ownerType);
            if (owner is null)
            {
                return false;
            }

            if (isProperty)
            {
                // Property symbols are keyed with a leading `$` (binder Identifier), but
                // `__PropertyName` / `\property_exists` literals use the bare PHP name (`'name'`).
                // Accept either spelling so existence verification matches docs and PHP.
                var withDollar = literalValue.StartsWith('$') ? literalValue : "$" + literalValue;
                var withoutDollar = literalValue.StartsWith('$') ? literalValue[1..] : literalValue;
                return TryGetPropertyMember(owner, withDollar) is not null
                    || TryGetPropertyMember(owner, withoutDollar) is not null;
            }

            return owner.Members.TryGetValue(literalValue, out var member)
                && member is ObjectMethodSymbol or ObjectConstructorMethodSymbol or ObjectAccessorMethodSymbol;
        }

        private static ObjectPropertySymbol? TryGetPropertyMember(ObjectDeclarationSymbol owner, string key)
            => owner.Members.TryGetValue(key, out var member) && member is ObjectPropertySymbol property
                ? property
                : null;

        private static bool IsObjectConstant(string literalValue, ICheckedType ownerType)
        {
            var owner = CheckerHelpers.TryGetObjectDeclaration(ownerType);
            return owner is not null
                && owner.TryGetConstant(literalValue, out var member)
                && member is ObjectConstantSymbol;
        }

        private static bool IsEnumCase(string literalValue, ICheckedType ownerType)
        {
            var owner = CheckerHelpers.TryGetObjectDeclaration(ownerType);
            return owner is { ObjectKind: PhpTypeDeclType.Enum }
                && owner.TryGetConstant(literalValue, out var member)
                && member is ObjectConstantSymbol;
        }

        private static bool IsCompatibleTypeName(
            string literalValue,
            ICheckedType compatibleWith,
            IBaseScope fromScope,
            NameResolver resolver,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var symbol = ResolveName(literalValue, fromScope, resolver);
            if (symbol is not ObjectDeclarationSymbol candidate)
            {
                return false;
            }

            if (candidate.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Enum or PhpTypeDeclType.Interface))
            {
                return false;
            }

            var targetDecl = CheckerHelpers.TryGetObjectDeclaration(compatibleWith);
            if (targetDecl is null)
            {
                return false;
            }

            if (string.Equals(candidate.FullyQualifiedName, targetDecl.FullyQualifiedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var candidateType = CheckedTypes.FromSymbol(candidate);
            return TypeComparer.IsSubtypeOf(candidateType, compatibleWith, symbolTree, globalScope);
        }

        private static bool IsUsedTrait(
            string literalValue,
            ICheckedType ownerType,
            IBaseScope fromScope,
            NameResolver resolver,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var traitSymbol = ResolveName(literalValue, fromScope, resolver);
            if (traitSymbol is not ObjectDeclarationSymbol { ObjectKind: PhpTypeDeclType.Trait } trait)
            {
                return false;
            }

            var owner = CheckerHelpers.TryGetObjectDeclaration(ownerType);
            if (owner is null)
            {
                return false;
            }

            var scope = owner.ContainingScope ?? globalScope;
            foreach (var implType in owner.ImplementsTypes)
            {
                if (symbolTree.ResolveType(implType, scope, SilentDiagnostics) is ObjectDeclarationSymbol usedTrait
                    && usedTrait.ObjectKind == PhpTypeDeclType.Trait
                    && string.Equals(usedTrait.FullyQualifiedName, trait.FullyQualifiedName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
