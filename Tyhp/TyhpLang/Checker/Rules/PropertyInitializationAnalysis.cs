using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Helpers for Prop-init #7 — seeding and recording per-property initialization state.
    /// </summary>
    internal static class PropertyInitializationAnalysis
    {
        /// <summary>
        /// Builds the initial property-init map for a constructor: only declaration-level
        /// guarantees (initializer or promoted parameter). Direct body assignments are tracked
        /// during the walk. Inherited non-declaration-guaranteed properties start uninitialized
        /// (conservative across class boundaries — do not assume <c>parent::__construct()</c>).
        /// </summary>
        public static Dictionary<string, PropertyInitializationState> SeedForConstructor(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var result = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                result[prop.Name] = new PropertyInitializationState
                {
                    // Declaration guarantees hold at constructor entry; AllowUnset only affects
                    // post-construction / after an explicit unset (Prop-init #8).
                    IsDefinitelyInitialized = IsDeclarationGuaranteed(prop),
                };
            }

            return result;
        }

        /// <summary>
        /// Builds the property-init map for a non-constructor instance method, using post-construction
        /// guarantees recorded on each <see cref="ObjectPropertySymbol"/> (and, for inherited
        /// properties assigned in this class's constructor, the per-class credit set).
        /// </summary>
        public static Dictionary<string, PropertyInitializationState> SeedForInstanceMethod(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var result = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                // #[AllowUnset]: another method (or callback) may have unset the slot, so instance
                // methods never treat the property as definitely initialized at entry.
                var definitelyInitialized = !prop.AllowsUnset
                    && IsDefinitelyInitializedForInstanceMethod(objectSymbol, prop);
                result[prop.Name] = new PropertyInitializationState
                {
                    IsDefinitelyInitialized = definitelyInitialized,
                };
            }

            return result;
        }

        /// <summary>
        /// After the constructor body (or when no constructor exists), mark which tracked properties
        /// remain possibly uninitialized for subsequent instance-method reads.
        /// Own-declared properties update <see cref="ObjectPropertySymbol.MayBeUninitializedAfterConstruction"/>.
        /// Inherited properties that this constructor definitely assigned are credited on
        /// <see cref="ObjectDeclarationSymbol.InheritedPropertiesInitializedByConstruction"/>
        /// without mutating the shared base-class property flag.
        /// </summary>
        public static void RecordPostConstructionState(
            ObjectDeclarationSymbol objectSymbol,
            CheckerState? constructorFinalState,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            objectSymbol.InheritedPropertiesInitializedByConstruction?.Clear();

            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                var isOwn = IsDeclaredOn(objectSymbol, prop);

                if (prop.AllowsUnset)
                {
                    if (isOwn)
                    {
                        prop.MayBeUninitializedAfterConstruction = true;
                    }

                    continue;
                }

                if (IsDeclarationGuaranteed(prop))
                {
                    if (isOwn)
                    {
                        prop.MayBeUninitializedAfterConstruction = false;
                    }

                    continue;
                }

                if (constructorFinalState?.LookupPropertyInit(prop.Name) is { IsDefinitelyInitialized: true })
                {
                    if (isOwn)
                    {
                        prop.MayBeUninitializedAfterConstruction = false;
                    }
                    else
                    {
                        objectSymbol.InheritedPropertiesInitializedByConstruction ??=
                            new HashSet<string>(StringComparer.Ordinal);
                        objectSymbol.InheritedPropertiesInitializedByConstruction.Add(prop.Name);
                    }

                    continue;
                }

                if (isOwn)
                {
                    prop.MayBeUninitializedAfterConstruction = true;
                    continue;
                }

                // This class has no own constructor body (no declared `__construct`, or an
                // abstract/interface one) — PHP runs the nearest ancestor's constructor for
                // `new Thing()`. Propagate that ancestor's determination for this inherited slot
                // so the credit chains correctly through classes that don't declare their own
                // constructor (multi-level inheritance; Top-type #9 follow-up).
                if (constructorFinalState is null
                    && TypeComparer.TryGetParentDeclaration(objectSymbol, symbolTree, globalScope) is { } parent
                    && IsDefinitelyInitializedForInstanceMethod(parent, prop))
                {
                    objectSymbol.InheritedPropertiesInitializedByConstruction ??=
                        new HashSet<string>(StringComparer.Ordinal);
                    objectSymbol.InheritedPropertiesInitializedByConstruction.Add(prop.Name);
                }
            }
        }

        /// <summary>
        /// Instance storage properties visible on <paramref name="objectSymbol"/> for Prop-init
        /// tracking — own members plus inherited public/protected properties from the
        /// <c>extends</c> chain (subclass override wins). Private ancestor properties are skipped
        /// (not visible from subclass methods). Mirrors
        /// <see cref="TypeComparer.TryGetParentDeclaration"/> traversal used elsewhere.
        /// </summary>
        public static IEnumerable<ObjectPropertySymbol> EnumerateTrackedProperties(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            // Parent-first so a same-named child override replaces the ancestor entry.
            var byName = new Dictionary<string, ObjectPropertySymbol>(StringComparer.Ordinal);
            var visited = new HashSet<ObjectDeclarationSymbol>();
            CollectTrackedProperties(objectSymbol, objectSymbol, symbolTree, globalScope, byName, visited);
            return byName.Values;
        }

        private static void CollectTrackedProperties(
            ObjectDeclarationSymbol root,
            ObjectDeclarationSymbol current,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Dictionary<string, ObjectPropertySymbol> byName,
            HashSet<ObjectDeclarationSymbol> visited)
        {
            if (!visited.Add(current))
            {
                return;
            }

            if (TypeComparer.TryGetParentDeclaration(current, symbolTree, globalScope) is { } parent)
            {
                CollectTrackedProperties(root, parent, symbolTree, globalScope, byName, visited);
            }

            var isRoot = ReferenceEquals(current, root);
            foreach (var member in current.Members.Values)
            {
                if (member is not ObjectPropertySymbol prop)
                {
                    continue;
                }

                if (prop.SymbolType != SymbolType.InstanceObjectProperty)
                {
                    continue;
                }

                // Private to an ancestor — not visible as `$this->prop` from subclass methods.
                if (!isRoot && (prop.Visibility & MemberModifier.Private) != 0)
                {
                    continue;
                }

                // Virtual / hooked properties are not storage slots PHP leaves uninitialized.
                if (prop.HasAccessor)
                {
                    continue;
                }

                // Untyped properties have no "must initialize" rule in PHP; Tyhp still requires a
                // type on declarations, but guard anyway.
                if (prop.DeclaredType is null)
                {
                    continue;
                }

                byName[prop.Name] = prop;
            }
        }

        private static bool IsDefinitelyInitializedForInstanceMethod(
            ObjectDeclarationSymbol objectSymbol,
            ObjectPropertySymbol prop)
        {
            if (IsDeclarationGuaranteed(prop))
            {
                return true;
            }

            if (IsDeclaredOn(objectSymbol, prop))
            {
                return !prop.MayBeUninitializedAfterConstruction;
            }

            // Inherited: credit this class's constructor assignment without mutating the shared
            // base property flag; otherwise trust the declaring class's post-construction analysis.
            if (objectSymbol.InheritedPropertiesInitializedByConstruction is { } credited
                && credited.Contains(prop.Name))
            {
                return true;
            }

            return !prop.MayBeUninitializedAfterConstruction;
        }

        private static bool IsDeclaredOn(ObjectDeclarationSymbol objectSymbol, ObjectPropertySymbol prop) =>
            objectSymbol.Members.TryGetValue(prop.Name, out var member) && ReferenceEquals(member, prop);

        /// <summary>
        /// Property initializer or constructor property promotion — both guarantee the slot is set
        /// before any user statement in <c>__construct</c> runs (promotion when the parameter is
        /// received; initializer before the constructor body).
        /// </summary>
        public static bool IsDeclarationGuaranteed(ObjectPropertySymbol prop)
        {
            if (prop.DefaultValue is not null)
            {
                return true;
            }

            // Promoted properties are bound from a <see cref="PhpParameterAst"/> declaring node.
            return prop.DeclaringAstNode is PhpParameterAst;
        }
    }
}
