using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Helpers for instance-property initialization: seeding, recording, and crediting
    /// <c>parent::__construct</c> across the extends chain.
    /// </summary>
    internal static class PropertyInitializationAnalysis
    {
        /// <summary>
        /// Builds the initial property-init map for a constructor. A declaration initializer
        /// is already applied. A promoted parameter of <paramref name="objectSymbol"/> is
        /// applied because this constructor is the one running. An inherited promotion or
        /// an assignment that lives in an ancestor constructor is not applied until that
        /// constructor runs (<c>parent::__construct</c>, or inheriting it by declaring none).
        /// <paramref name="constructorOwner"/> is the declaration whose constructor body runs:
        /// <paramref name="objectSymbol"/> itself, or a used trait when the class declares no
        /// constructor and PHP imports the trait's.
        /// </summary>
        public static Dictionary<string, PropertyInitializationState> SeedForConstructor(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            ObjectDeclarationSymbol? constructorOwner = null)
        {
            var owner = constructorOwner ?? objectSymbol;
            var result = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                result[prop.Name] = new PropertyInitializationState
                {
                    IsDefinitelyInitialized = IsInitializedAtConstructorEntry(owner, prop),
                };
            }

            AddTrackedStaticProperties(result, objectSymbol, symbolTree, globalScope);
            return result;
        }

        /// <summary>
        /// Builds the property-init map for a non-constructor instance method, using post-construction
        /// guarantees recorded on each <see cref="ObjectPropertySymbol"/> (and, for inherited
        /// properties assigned in this class's constructor, the per-class credit set).
        /// Property hook bodies use this map and are checked only after those guarantees are recorded.
        /// </summary>
        public static Dictionary<string, PropertyInitializationState> SeedForInstanceMethod(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var result = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                result[prop.Name] = new PropertyInitializationState
                {
                    IsDefinitelyInitialized = IsDefinitelyInitializedForInstanceMethod(objectSymbol, prop),
                };
            }

            AddTrackedStaticProperties(result, objectSymbol, symbolTree, globalScope);
            return result;
        }

        /// <summary>
        /// Builds the property-init map for a static method: enclosing-class static properties
        /// only (no <c>$this</c>). Used so <c>self::$x === null</c> / <c>self::$x = …</c> share
        /// the same <see cref="PropertyInitializationState"/> narrowing as <c>$this->prop</c>.
        /// </summary>
        public static Dictionary<string, PropertyInitializationState> SeedForStaticMethod(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var result = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            AddTrackedStaticProperties(result, objectSymbol, symbolTree, globalScope);
            return result;
        }

        /// <summary>
        /// <c>: parent(…)</c> on a constructor. The emitter inserts
        /// <c>parent::__construct(…)</c> before the body, so the call dominates every path.
        /// </summary>
        public static bool HasUnconditionalParentDelegation(PhpMethodDeclAst method)
        {
            if (!method.AstGrammarAddons.TryGetValue("ctorReturnType", out var addon)
                || addon is not TyhpCtorReturnTypeAst ctorReturn)
            {
                return false;
            }

            return string.Equals(ctorReturn.TypeToken?.ValueString, "parent", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="deref"/> is a direct <c>parent::__construct(…)</c> call.
        /// </summary>
        public static bool IsParentConstructorCall(PhpDereferenceableAst deref)
        {
            if (deref.Suffix is not PhpCallAst || deref.Base is not PhpDereferenceableAst chain)
            {
                return false;
            }

            var methodName = chain.Suffix switch
            {
                PhpStaticMemberAccessAst staticAccess => ExpressionText(staticAccess.Member),
                PhpClassConstantAccessAst classConst => ExpressionText(classConst.Member),
                _ => null,
            };
            if (!string.Equals(methodName, "__construct", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(ExpressionText(chain.Base as IExpression), "parent", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Credits a dominating <c>parent::__construct</c> on the current constructor path:
        /// every inherited property that the parent constructor initializes becomes initialized
        /// here. Does not prove anything about the arguments. A call inside a closure or a
        /// helper is not this constructor's call.
        /// </summary>
        public static void CreditParentConstructorCall(
            CheckerState state,
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (state.EnclosingCallable is not ObjectConstructorMethodSymbol || state.IsInsideClosure)
            {
                return;
            }

            state.NoteParentConstructorInvoked();
            if (TypeComparer.TryGetParentDeclaration(objectSymbol, symbolTree, globalScope) is not { } parent)
            {
                return;
            }

            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                if (ParentConstructorInitializes(parent, prop))
                {
                    state.AssignProperty(prop.Name);
                }
            }
        }

        /// <summary>
        /// Records what a <c>return</c> inside the constructor leaves initialized. A return ends
        /// construction on that path, so each one must satisfy the same rule as the end of the
        /// body. A <c>return</c> inside a closure belongs to the closure.
        /// </summary>
        public static void NoteConstructorReturn(
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (state.ConstructorExits is not { } collector
                || state.IsInsideClosure
                || !ReferenceEquals(state.EnclosingCallable, collector.Owner)
                || state.EnclosingObject is not { } owner)
            {
                return;
            }

            var initialized = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in EnumerateTrackedProperties(owner, symbolTree, globalScope))
            {
                if (state.LookupPropertyInit(prop.Name) is { IsDefinitelyInitialized: true })
                {
                    initialized.Add(prop.Name);
                }
            }

            collector.Exits.Add(new ConstructorExit(initialized, state.ParentConstructorInvoked));
        }

        /// <summary>
        /// The constructor PHP imports from a used trait when <paramref name="objectSymbol"/>
        /// declares none. A method declared on the class replaces a trait method, so this applies
        /// only to a class without its own constructor, and the trait constructor replaces the
        /// inherited one.
        /// </summary>
        public static (ObjectDeclarationSymbol Trait, PhpMethodDeclAst Method)? FindTraitConstructor(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (objectSymbol.ObjectKind != PhpTypeDeclType.Class || DeclaresConstructor(objectSymbol))
            {
                return null;
            }

            foreach (var trait in TypeComparer.ResolveUsedTraits(objectSymbol, symbolTree, globalScope, out _))
            {
                foreach (var member in trait.Members.Values)
                {
                    if (member is ObjectConstructorMethodSymbol { DeclaringAstNode: PhpMethodDeclAst { Body: not null } method })
                    {
                        return (trait, method);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="prop"/> comes from <paramref name="objectSymbol"/> itself or
        /// from a trait it uses. A property inherited from an ancestor is the ancestor's to
        /// initialize and report.
        /// </summary>
        public static bool IsIntroducedBy(
            ObjectDeclarationSymbol objectSymbol,
            ObjectPropertySymbol prop,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            IsDeclaredOn(objectSymbol, prop) || IsDirectTraitProperty(objectSymbol, prop, symbolTree, globalScope);

        /// <summary>
        /// After the constructor body (or when no constructor exists), mark which tracked properties
        /// remain possibly uninitialized for subsequent instance-method reads.
        /// Own-declared properties update <see cref="ObjectPropertySymbol.MayBeUninitializedAfterConstruction"/>.
        /// Inherited properties that this constructor definitely assigned, or that a dominating
        /// <c>parent::__construct</c> initialized, are credited on
        /// <see cref="ObjectDeclarationSymbol.InheritedPropertiesInitializedByConstruction"/>
        /// without mutating the shared base-class property flag.
        /// <paramref name="inheritAncestorConstructor"/> is true only when this type declares no
        /// constructor, so PHP runs the nearest ancestor constructor.
        /// </summary>
        public static void RecordPostConstructionState(
            ObjectDeclarationSymbol objectSymbol,
            CheckerState? constructorFinalState,
            bool inheritAncestorConstructor,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (IsTyhpdefDeclaration(objectSymbol))
            {
                RecordTyhpdefConstruction(objectSymbol, symbolTree, globalScope);
                return;
            }

            objectSymbol.InheritedPropertiesInitializedByConstruction?.Clear();
            objectSymbol.PrivatePropertiesInitializedByConstruction = null;
            objectSymbol.UninitializedAncestorPrivateProperties = null;

            if (objectSymbol.ObjectKind == PhpTypeDeclType.Trait)
            {
                // The using class initializes trait properties. A trait method reads them
                // on that instance; the trait itself is not required to declare a constructor.
                foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
                {
                    if (IsDeclaredOn(objectSymbol, prop))
                    {
                        prop.MayBeUninitializedAfterConstruction = false;
                    }
                }

                objectSymbol.InstanceConstructionRecorded = true;
                return;
            }

            var parent = TypeComparer.TryGetParentDeclaration(objectSymbol, symbolTree, globalScope);

            // A `return` inside the constructor ends construction on that path, so a property
            // or the parent call only counts when it holds at the end of the body and at every
            // `return` before it. A `throw` leaves no constructed object and is not an exit.
            var exits = constructorFinalState?.ConstructorExits?.Exits;
            var parentRuns = (constructorFinalState?.ParentConstructorInvoked == true
                    && (exits is null || exits.TrueForAll(exit => exit.ParentConstructorInvoked)))
                || (constructorFinalState is null && inheritAncestorConstructor);
            var privateSet = new HashSet<ObjectPropertySymbol>();

            bool InitializedOnEveryExit(ObjectPropertySymbol prop)
            {
                if (constructorFinalState?.LookupPropertyInit(prop.Name) is not { IsDefinitelyInitialized: true })
                {
                    return false;
                }

                return exits is null
                    || exits.TrueForAll(exit => exit.InitializedProperties.Contains(prop.Name));
            }

            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                var isOwn = IsDeclaredOn(objectSymbol, prop);
                var isDirectTrait = !isOwn && IsDirectTraitProperty(objectSymbol, prop, symbolTree, globalScope);

                if (HasDeclarationInitializer(prop))
                {
                    if (isOwn)
                    {
                        prop.MayBeUninitializedAfterConstruction = false;
                    }

                    continue;
                }

                if (isOwn && IsPromoted(prop))
                {
                    prop.MayBeUninitializedAfterConstruction = false;
                    if (IsPrivate(prop))
                    {
                        privateSet.Add(prop);
                    }

                    continue;
                }

                if (InitializedOnEveryExit(prop))
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

                    if (IsPrivate(prop) && (isOwn || isDirectTrait))
                    {
                        privateSet.Add(prop);
                    }

                    continue;
                }

                if (isOwn)
                {
                    prop.MayBeUninitializedAfterConstruction = true;
                    continue;
                }

                if (IsFilledByStub(prop, symbolTree, globalScope))
                {
                    objectSymbol.InheritedPropertiesInitializedByConstruction ??=
                        new HashSet<string>(StringComparer.Ordinal);
                    objectSymbol.InheritedPropertiesInitializedByConstruction.Add(prop.Name);
                    continue;
                }

                // No constructor of our own: PHP runs the nearest ancestor constructor for
                // `new This()`. Credit the properties that constructor initializes, including
                // through descendants that also declare none.
                if (constructorFinalState is null
                    && inheritAncestorConstructor
                    && parent is not null
                    && IsDefinitelyInitializedForInstanceMethod(parent, prop))
                {
                    objectSymbol.InheritedPropertiesInitializedByConstruction ??=
                        new HashSet<string>(StringComparer.Ordinal);
                    objectSymbol.InheritedPropertiesInitializedByConstruction.Add(prop.Name);
                }
            }

            if (parentRuns)
            {
                if (parent?.PrivatePropertiesInitializedByConstruction is { } inheritedPrivate)
                {
                    foreach (var prop in inheritedPrivate)
                    {
                        privateSet.Add(prop);
                    }
                }
            }
            else if (parent?.PrivatePropertiesInitializedByConstruction is { Count: > 0 } missing)
            {
                objectSymbol.UninitializedAncestorPrivateProperties = missing.ToList();
            }

            foreach (var prop in EnumerateOwnPrivateStorage(objectSymbol, symbolTree, globalScope))
            {
                if (HasDeclarationInitializer(prop))
                {
                    continue;
                }

                if ((IsPromoted(prop) && IsDeclaredOn(objectSymbol, prop)) || InitializedOnEveryExit(prop))
                {
                    privateSet.Add(prop);
                }
            }

            objectSymbol.PrivatePropertiesInitializedByConstruction = privateSet.Count == 0 ? null : privateSet;
            objectSymbol.InstanceConstructionRecorded = true;
        }

        /// <summary>
        /// Typed instance properties that are still uninitialized once this type's constructor
        /// (or its inherited constructor) has finished. Includes private ancestor properties
        /// the type cannot assign.
        /// </summary>
        public static List<ObjectPropertySymbol> CollectUninitializedAfterConstruction(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var result = new List<ObjectPropertySymbol>();
            var seen = new HashSet<ObjectPropertySymbol>();
            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                if (IsDefinitelyInitializedForInstanceMethod(objectSymbol, prop))
                {
                    continue;
                }

                if (seen.Add(prop))
                {
                    result.Add(prop);
                }
            }

            if (objectSymbol.UninitializedAncestorPrivateProperties is { } privates)
            {
                foreach (var prop in privates)
                {
                    if (seen.Add(prop))
                    {
                        result.Add(prop);
                    }
                }
            }

            return result;
        }

        public static string PropertyDiagnosticName(ObjectPropertySymbol prop)
        {
            var name = prop.Name ?? "";
            return name.StartsWith('$') ? name[1..] : name;
        }

        public static string DeclaringTypeName(ObjectPropertySymbol prop, ObjectDeclarationSymbol fallback)
        {
            if (prop.ContainingScope?.DeclarationSymbol is ObjectDeclarationSymbol owner
                && !string.IsNullOrEmpty(owner.Name))
            {
                return DisplayName(owner);
            }

            return DisplayName(fallback);
        }

        /// <summary>
        /// True for a property declared on a tyhpdef type whose stub chain declares no constructor.
        /// There is no constructor for a subclass to call, and the stub does not say how the
        /// property is filled (an extension fills it natively), so the user's class is not asked
        /// to initialize it.
        /// </summary>
        private static bool IsFilledByStub(
            ObjectPropertySymbol prop,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (prop.ContainingScope?.DeclarationSymbol is not ObjectDeclarationSymbol owner
                || !IsTyhpdefDeclaration(owner))
            {
                return false;
            }

            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = owner;
                current is not null && visited.Add(current) && IsTyhpdefDeclaration(current);
                current = TypeComparer.TryGetParentDeclaration(current, symbolTree, globalScope))
            {
                if (DeclaresConstructor(current))
                {
                    return false;
                }
            }

            return true;
        }

        private static string DisplayName(ObjectDeclarationSymbol symbol)
        {
            if (symbol.DeclaringAstNode is PhpObjectTypeDeclAst { IsAnonymousClass: true })
            {
                return "class@anonymous";
            }

            return string.IsNullOrEmpty(symbol.Name) ? "class" : symbol.Name;
        }

        /// <summary>
        /// Instance storage properties visible on <paramref name="objectSymbol"/> —
        /// own members, properties introduced by traits this type <c>use</c>s, and inherited
        /// public/protected properties (subclass override wins). Private ancestor properties
        /// are skipped (not visible as <c>$this->prop</c>). Statics are not included.
        /// </summary>
        public static IEnumerable<ObjectPropertySymbol> EnumerateTrackedProperties(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var byName = new Dictionary<string, ObjectPropertySymbol>(StringComparer.Ordinal);
            var visited = new HashSet<ObjectDeclarationSymbol>();
            CollectTrackedProperties(
                objectSymbol,
                objectSymbol,
                symbolTree,
                globalScope,
                byName,
                visited,
                SymbolType.InstanceObjectProperty);
            return byName.Values;
        }

        /// <summary>
        /// Static storage properties visible on <paramref name="objectSymbol"/> for control-flow
        /// narrowing of <c>self::$prop</c> / <c>static::$prop</c> / <c>ClassName::$prop</c>.
        /// Same visibility / hook / typed-slot filters as instance tracking. Trait statics are
        /// not folded in; a typed static's initializer is a separate declaration check.
        /// </summary>
        public static IEnumerable<ObjectPropertySymbol> EnumerateTrackedStaticProperties(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var byName = new Dictionary<string, ObjectPropertySymbol>(StringComparer.Ordinal);
            var visited = new HashSet<ObjectDeclarationSymbol>();
            CollectTrackedProperties(
                objectSymbol,
                objectSymbol,
                symbolTree,
                globalScope,
                byName,
                visited,
                SymbolType.StaticObjectProperty);
            return byName.Values;
        }

        /// <summary>
        /// A property initializer on the declaration. A default on a promoted parameter is a
        /// parameter default: it is written only when that constructor runs, so it is not one.
        /// </summary>
        public static bool HasDeclarationInitializer(ObjectPropertySymbol prop) =>
            prop.DefaultValue is not null && prop.DeclaringAstNode is not PhpParameterAst;

        /// <summary>
        /// True when the property is a constructor promotion. The write happens only when
        /// the constructor that declares the parameter runs.
        /// </summary>
        public static bool IsPromoted(ObjectPropertySymbol prop) =>
            prop.DeclaringAstNode is PhpParameterAst;

        private static void RecordTyhpdefConstruction(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            objectSymbol.InheritedPropertiesInitializedByConstruction?.Clear();
            objectSymbol.UninitializedAncestorPrivateProperties = null;

            var hasCtor = DeclaresConstructor(objectSymbol);
            var parent = TypeComparer.TryGetParentDeclaration(objectSymbol, symbolTree, globalScope);
            var privateSet = new HashSet<ObjectPropertySymbol>();

            foreach (var prop in EnumerateTrackedProperties(objectSymbol, symbolTree, globalScope))
            {
                var isOwn = IsDeclaredOn(objectSymbol, prop);
                if (HasDeclarationInitializer(prop))
                {
                    if (isOwn)
                    {
                        prop.MayBeUninitializedAfterConstruction = false;
                    }

                    continue;
                }

                // A stub constructor has no body to walk. Invoking it is credited with the
                // stub's typed instance properties. With no constructor, PHP inherits the
                // parent's. A stub chain with no constructor at all gives the user's subclass
                // nothing to call, and the stub does not say how such a property is filled
                // (an extension fills it natively), so it is credited rather than reported on
                // a type the user cannot change.
                var initialized = hasCtor
                    || isOwn
                    || parent is null
                    || IsDefinitelyInitializedForInstanceMethod(parent, prop);
                if (!initialized)
                {
                    if (isOwn)
                    {
                        prop.MayBeUninitializedAfterConstruction = true;
                    }

                    continue;
                }

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
            }

            if (hasCtor)
            {
                foreach (var prop in EnumerateOwnPrivateStorage(objectSymbol, symbolTree, globalScope))
                {
                    if (!HasDeclarationInitializer(prop))
                    {
                        privateSet.Add(prop);
                    }
                }
            }
            else if (parent?.PrivatePropertiesInitializedByConstruction is { } inheritedPrivate)
            {
                foreach (var prop in inheritedPrivate)
                {
                    privateSet.Add(prop);
                }
            }

            objectSymbol.PrivatePropertiesInitializedByConstruction = privateSet.Count == 0 ? null : privateSet;
            objectSymbol.InstanceConstructionRecorded = true;
        }

        private static void AddTrackedStaticProperties(
            Dictionary<string, PropertyInitializationState> result,
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            foreach (var prop in EnumerateTrackedStaticProperties(objectSymbol, symbolTree, globalScope))
            {
                result[prop.Name] = new PropertyInitializationState
                {
                    IsDefinitelyInitialized = HasDeclarationInitializer(prop),
                };
            }
        }

        private static void CollectTrackedProperties(
            ObjectDeclarationSymbol root,
            ObjectDeclarationSymbol current,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Dictionary<string, ObjectPropertySymbol> byName,
            HashSet<ObjectDeclarationSymbol> visited,
            SymbolType wantedKind)
        {
            if (!visited.Add(current))
            {
                return;
            }

            if (TypeComparer.TryGetParentDeclaration(current, symbolTree, globalScope) is { } parent)
            {
                CollectTrackedProperties(
                    root, parent, symbolTree, globalScope, byName, visited, wantedKind);
            }

            var isRoot = ReferenceEquals(current, root);
            if (wantedKind == SymbolType.InstanceObjectProperty)
            {
                foreach (var trait in TypeComparer.ResolveUsedTraits(current, symbolTree, globalScope, out _))
                {
                    AddVisibleProperties(trait, isRoot, byName, wantedKind);
                }
            }

            AddVisibleProperties(current, isRoot, byName, wantedKind);
        }

        private static void AddVisibleProperties(
            ObjectDeclarationSymbol source,
            bool privateVisible,
            Dictionary<string, ObjectPropertySymbol> byName,
            SymbolType wantedKind)
        {
            foreach (var member in source.Members.Values)
            {
                if (!IsTrackableStorage(member, wantedKind, privateVisible, out var prop))
                {
                    continue;
                }

                byName[prop.Name] = prop;
            }
        }

        private static bool IsTrackableStorage(
            IBaseSymbol member,
            SymbolType wantedKind,
            bool privateVisible,
            out ObjectPropertySymbol prop)
        {
            if (member is not ObjectPropertySymbol property
                || property.SymbolType != wantedKind
                || property.HasAccessor
                || property.DeclaredType is null
                || (!privateVisible && IsPrivate(property)))
            {
                prop = null!;
                return false;
            }

            prop = property;
            return true;
        }

        private static IEnumerable<ObjectPropertySymbol> EnumerateOwnPrivateStorage(
            ObjectDeclarationSymbol objectSymbol,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            foreach (var member in objectSymbol.Members.Values)
            {
                if (IsTrackableStorage(member, SymbolType.InstanceObjectProperty, privateVisible: true, out var prop)
                    && IsPrivate(prop))
                {
                    yield return prop;
                }
            }

            foreach (var trait in TypeComparer.ResolveUsedTraits(objectSymbol, symbolTree, globalScope, out _))
            {
                foreach (var member in trait.Members.Values)
                {
                    if (IsTrackableStorage(member, SymbolType.InstanceObjectProperty, privateVisible: true, out var prop)
                        && IsPrivate(prop))
                    {
                        yield return prop;
                    }
                }
            }
        }

        private static bool IsInitializedAtConstructorEntry(
            ObjectDeclarationSymbol constructorOwner,
            ObjectPropertySymbol prop)
        {
            if (HasDeclarationInitializer(prop))
            {
                return true;
            }

            // Promotion of this constructor's own parameter. An ancestor promotion has not
            // run yet; the ancestor constructor is a later `parent::__construct` or is inherited
            // only when this type declares no constructor.
            return IsPromoted(prop) && IsDeclaredOn(constructorOwner, prop);
        }

        private static bool IsDefinitelyInitializedForInstanceMethod(
            ObjectDeclarationSymbol objectSymbol,
            ObjectPropertySymbol prop)
        {
            if (HasDeclarationInitializer(prop))
            {
                return true;
            }

            if (IsDeclaredOn(objectSymbol, prop))
            {
                if (IsPromoted(prop))
                {
                    return true;
                }

                if (!objectSymbol.InstanceConstructionRecorded)
                {
                    return false;
                }

                return !prop.MayBeUninitializedAfterConstruction;
            }

            return objectSymbol.InheritedPropertiesInitializedByConstruction is { } credited
                && credited.Contains(prop.Name);
        }

        private static bool ParentConstructorInitializes(
            ObjectDeclarationSymbol parent,
            ObjectPropertySymbol prop)
        {
            if (IsDefinitelyInitializedForInstanceMethod(parent, prop))
            {
                return true;
            }

            // The child redeclared the same name. The parent constructor still writes that slot
            // when the parent's own property is initialized by the parent constructor.
            return parent.Members.TryGetValue(prop.Name, out var member)
                && member is ObjectPropertySymbol declared
                && !ReferenceEquals(declared, prop)
                && IsDefinitelyInitializedForInstanceMethod(parent, declared);
        }

        private static bool IsDeclaredOn(ObjectDeclarationSymbol objectSymbol, ObjectPropertySymbol prop) =>
            objectSymbol.Members.TryGetValue(prop.Name, out var member) && ReferenceEquals(member, prop);

        private static bool IsDirectTraitProperty(
            ObjectDeclarationSymbol objectSymbol,
            ObjectPropertySymbol prop,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (prop.ContainingScope?.DeclarationSymbol is not ObjectDeclarationSymbol owner
                || owner.ObjectKind != PhpTypeDeclType.Trait)
            {
                return false;
            }

            foreach (var trait in TypeComparer.ResolveUsedTraits(objectSymbol, symbolTree, globalScope, out _))
            {
                if (ReferenceEquals(trait, owner))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPrivate(ObjectPropertySymbol prop) =>
            (prop.Visibility & MemberModifier.Private) != 0;

        private static bool DeclaresConstructor(ObjectDeclarationSymbol symbol)
        {
            foreach (var member in symbol.Members.Values)
            {
                if (member is ObjectConstructorMethodSymbol)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsTyhpdefDeclaration(ObjectDeclarationSymbol symbol)
        {
            if (symbol.DeclaringAstNode is TyhpdefImportObjectDeclAst)
            {
                return true;
            }

            if (symbol.DeclaringAstNode is PhpObjectTypeDeclAst decl
                && string.Equals(decl.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return symbol.SourceFile.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The <c>$name</c> key of a direct <c>$this-&gt;name</c> access, or null.
        /// </summary>
        public static string? ThisMemberKey(PhpDereferenceableAst access)
        {
            if (access.Suffix is not PhpInstanceMemberAccessAst memberAccess
                || access.Base is not PhpVariableAst receiver
                || !CheckerHelpers.IsThisVariable(receiver))
            {
                return null;
            }

            var name = NullSafetyRule.GetMemberName(memberAccess.MemberName);
            if (string.IsNullOrEmpty(name) || name.StartsWith('{'))
            {
                return null;
            }

            return name.StartsWith('$') ? name : "$" + name;
        }

        /// <summary>
        /// The instance property with a hook that <paramref name="owner"/> sees under
        /// <paramref name="key"/>: its own, a used trait's, or an ancestor's.
        /// </summary>
        public static ObjectPropertySymbol? FindHookedInstanceProperty(
            ObjectDeclarationSymbol owner,
            string key,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = owner;
                current is not null && visited.Add(current);
                current = TypeComparer.TryGetParentDeclaration(current, symbolTree, globalScope))
            {
                if (HookedMember(current, key) is { } own)
                {
                    return own;
                }

                foreach (var trait in TypeComparer.ResolveUsedTraits(current, symbolTree, globalScope, out _))
                {
                    if (HookedMember(trait, key) is { } fromTrait)
                    {
                        return fromTrait;
                    }
                }

                if (current.Members.ContainsKey(key))
                {
                    return null;
                }
            }

            return null;
        }

        private static ObjectPropertySymbol? HookedMember(ObjectDeclarationSymbol declaration, string key) =>
            declaration.Members.TryGetValue(key, out var member)
                && member is ObjectPropertySymbol
                {
                    SymbolType: SymbolType.InstanceObjectProperty,
                    HasAccessor: true,
                } property
                    ? property
                    : null;

        /// <summary>
        /// The <c>$name</c> keys of properties that the <c>get</c> (or <c>set</c>) hook of
        /// <paramref name="hooked"/> reads through <c>$this-&gt;name</c>, including reads made by
        /// another hooked property that hook reads. A key the hook assigns is left out, and so is a
        /// read that only probes existence (<c>isset</c>, <c>empty</c>, <c>??</c>).
        /// </summary>
        public static List<string> HookReadsOfProperties(
            ObjectPropertySymbol hooked,
            bool setHook,
            ObjectDeclarationSymbol owner,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var reads = new HashSet<string>(StringComparer.Ordinal);
            CollectHookReads(
                hooked, setHook, owner, symbolTree, globalScope, reads,
                new HashSet<(ObjectPropertySymbol, bool)>());
            var ordered = reads.ToList();
            ordered.Sort(StringComparer.Ordinal);
            return ordered;
        }

        private static void CollectHookReads(
            ObjectPropertySymbol hooked,
            bool setHook,
            ObjectDeclarationSymbol owner,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            HashSet<string> reads,
            HashSet<(ObjectPropertySymbol, bool)> visited)
        {
            if ((setHook ? !hooked.HasSetHook : !hooked.HasGetHook) || !visited.Add((hooked, setHook)))
            {
                return;
            }

            var hooks = hooked.DeclaringAstNode switch
            {
                PhpPropertyAst property => property.Hooks,
                PhpParameterAst parameter => parameter.PropertyHooks as PhpPropertyHookListAst,
                _ => null,
            };
            if (hooks is null)
            {
                return;
            }

            var hookName = setHook ? "set" : "get";
            foreach (var hook in hooks.GetAllNotNull())
            {
                if (hook.Body is not IBase2Ast body
                    || !string.Equals(hook.Identifier?.Trim(), hookName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var read = new HashSet<string>(StringComparer.Ordinal);
                var written = new HashSet<string>(StringComparer.Ordinal);
                ScanHookBody(body, read, written);
                foreach (var key in read)
                {
                    if (written.Contains(key))
                    {
                        continue;
                    }

                    reads.Add(key);
                    if (FindHookedInstanceProperty(owner, key, symbolTree, globalScope) is { } nested)
                    {
                        CollectHookReads(nested, false, owner, symbolTree, globalScope, reads, visited);
                    }
                }
            }
        }

        private static void ScanHookBody(IBase2Ast node, HashSet<string> reads, HashSet<string> written)
        {
            switch (node)
            {
                case PhpIssetStatementAst:
                case PhpEmptyStatementAst:
                case TyhpVariableExistsAst:
                    return;
                case PhpBinaryOpAst binary
                    when NullSafetyRule.IsCoalesce(binary) || NullSafetyRule.IsCoalesceAssign(binary):
                    if (binary.Right is IBase2Ast coalesceRight)
                    {
                        ScanHookBody(coalesceRight, reads, written);
                    }

                    return;
                case PhpBinaryOpAst binary when NullSafetyRule.IsSimpleAssignWrite(binary):
                    if (binary.Left is PhpDereferenceableAst target && ThisMemberKey(target) is { } assigned)
                    {
                        written.Add(assigned);
                    }
                    else if (binary.Left is IBase2Ast left)
                    {
                        ScanHookBody(left, reads, written);
                    }

                    foreach (var child in binary.AstChildren)
                    {
                        if (child is not null && !ReferenceEquals(child, binary.Left))
                        {
                            ScanHookBody(child, reads, written);
                        }
                    }

                    return;
                case PhpDereferenceableAst { Suffix: PhpCallAst call, Base: PhpDereferenceableAst method }
                    when ThisMemberKey(method) is not null:
                    // `$this->name(…)` calls a method; only its arguments are reads.
                    ScanHookBody(call, reads, written);
                    return;
                case PhpDereferenceableAst access when ThisMemberKey(access) is { } key:
                    reads.Add(key);
                    return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    ScanHookBody(child, reads, written);
                }
            }
        }

        private static string? ExpressionText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                TokenValueAst token => token.ValueString,
                _ => expression?.Identifier,
            };
    }
}
