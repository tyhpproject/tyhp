using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Static type compatibility utilities. All public methods are pure functions that receive
    /// <see cref="SymbolTree"/> and <see cref="GlobalScope"/> explicitly for hierarchy lookups.
    /// </summary>
    public static partial class TypeComparer
    {
        private static readonly DiagnosticBag SilentDiagnostics = new();
        private static int _templateStringMaxStates = 256;

        [ThreadStatic]
        private static int _templateStringCheckDepth;

        [ThreadStatic]
        private static bool _templateStringBudgetExceeded;

        /// <summary>Configures the template-string matcher step budget (default 256).</summary>
        public static void ConfigureTemplateStringMaxStates(int maxStates) =>
            _templateStringMaxStates = maxStates > 0 ? maxStates : 256;

        internal static int TemplateStringMaxStates => _templateStringMaxStates;

        internal static TemplateStringMatchBudget CreateTemplateStringBudget() =>
            new(_templateStringMaxStates);

        internal static bool TryConsumeTemplateStringBudgetExceeded()
        {
            if (!_templateStringBudgetExceeded)
            {
                return false;
            }

            _templateStringBudgetExceeded = false;
            return true;
        }

        private static void MarkTemplateStringBudgetExceeded() =>
            _templateStringBudgetExceeded = true;

        private static void EnterTemplateStringCheckScope()
        {
            if (_templateStringCheckDepth++ == 0)
            {
                _templateStringBudgetExceeded = false;
            }
        }

        private static void ExitTemplateStringCheckScope()
        {
            _templateStringCheckDepth--;
        }

        public static bool IsAssignableTo(
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            EnterTemplateStringCheckScope();
            try
            {
                return IsAssignableToCore(source, target, symbolTree, globalScope, new HashSet<(ICheckedType, ICheckedType)>());
            }
            finally
            {
                ExitTemplateStringCheckScope();
            }
        }

        public static bool IsSubtypeOf(
            ICheckedType child,
            ICheckedType parent,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            EnterTemplateStringCheckScope();
            try
            {
                return IsSubtypeOfCore(child, parent, symbolTree, globalScope, new HashSet<(ICheckedType, ICheckedType)>());
            }
            finally
            {
                ExitTemplateStringCheckScope();
            }
        }

        public static bool AreTypesEqual(ICheckedType a, ICheckedType b) =>
            AreTypesEqualCore(a, b, new HashSet<(ICheckedType, ICheckedType)>());

        public static ICheckedType UnionTypes(
            ICheckedType a,
            ICheckedType b,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            UnionTypesCore([a, b], symbolTree, globalScope);

        public static ICheckedType UnionTypes(
            IReadOnlyList<ICheckedType> members,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            UnionTypesCore(members, symbolTree, globalScope);

        public static ICheckedType IntersectTypes(
            ICheckedType a,
            ICheckedType b,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            IntersectTypesCore(a, b, symbolTree, globalScope);

        public static ICheckedType NarrowType(
            ICheckedType current,
            ICheckedType narrowTo,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            NarrowTypeCore(current, narrowTo, symbolTree, globalScope);

        public static ICheckedType NarrowTypeNegative(
            ICheckedType current,
            ICheckedType excludeType,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            NarrowTypeNegativeCore(current, excludeType, symbolTree, globalScope);

        public static ICheckedType ResolveGenericType(
            ICheckedType generic,
            Dictionary<string, ICheckedType> typeArguments,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            ResolveGenericTypeCore(generic, typeArguments, symbolTree, globalScope);

        internal static bool IsNullLiteral(ICheckedType type) =>
            type is LiteralCheckedType { Value: null };

        internal static bool IsNeverType(ICheckedType type) =>
            type.IsNever || type.Kind == CheckedTypeKind.Never;

        internal static bool IsVoidType(ICheckedType type) =>
            type.IsVoid || type.Kind == CheckedTypeKind.Void || IsBuiltInName(type, "void");

        internal static bool IsMixedType(ICheckedType type) =>
            type.IsMixed || IsBuiltInName(type, "mixed");

        internal static bool IsUnresolvedType(ICheckedType type) =>
            type.Kind == CheckedTypeKind.Unresolved;

        internal static bool IsBuiltInName(ICheckedType type, string name) =>
            TryGetBuiltInName(type, out var builtInName) &&
            string.Equals(builtInName, name, StringComparison.OrdinalIgnoreCase);

        internal static bool TryGetBuiltInName(ICheckedType type, out string name)
        {
            name = string.Empty;
            var symbol = TryGetNominalSymbol(type);
            if (symbol is BuiltInTypeSymbol builtIn)
            {
                name = builtIn.Name;
                return true;
            }

            return false;
        }

        internal static IBaseSymbol? TryGetNominalSymbol(ICheckedType type) =>
            type switch
            {
                SimpleCheckedType simple => simple.ResolvedSymbol,
                GenericCheckedType generic => TryGetNominalSymbol(generic.BaseType),
                _ => null,
            };

        internal static ObjectDeclarationSymbol? TryGetObjectDeclaration(ICheckedType type)
        {
            if (type is StaticCheckedType staticType)
            {
                return TryGetObjectDeclaration(staticType.DeclaringType);
            }

            var symbol = TryGetNominalSymbol(type);
            return symbol as ObjectDeclarationSymbol;
        }

        internal static bool SymbolsMatch(IBaseSymbol? left, IBaseSymbol? right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            var leftFqn = NormalizeFqn(left);
            var rightFqn = NormalizeFqn(right);
            return string.Equals(leftFqn, rightFqn, StringComparison.OrdinalIgnoreCase);
        }

        public static BuiltInTypeSymbol? ResolveBuiltIn(string name, GlobalScope globalScope) =>
            ((IBaseScope)globalScope).FindChildSymbolByName(name) as BuiltInTypeSymbol;

        internal static ObjectDeclarationSymbol? ResolveObjectType(
            string name,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            symbolTree.ResolveQualifiedName([name], globalScope, SilentDiagnostics) as ObjectDeclarationSymbol;

        private static string NormalizeFqn(IBaseSymbol symbol)
        {
            var fqn = !string.IsNullOrEmpty(symbol.FullyQualifiedName)
                ? symbol.FullyQualifiedName
                : symbol.Name;
            return fqn.TrimStart('\\');
        }
    }
}
