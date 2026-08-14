using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// PHP &lt; 8.4 property-hook polyfill lowering: strip native hook syntax, inject
    /// <c>UsesPropertyAccessors</c> or piece traits, register <c>PropertyAccessor</c> instances
    /// on <c>$__tyhpPropertyHook</c> (<c>PropertyAccessorObject</c>), rewrite
    /// <c>$this-&gt;prop</c> inside hook bodies to bag backing helpers, and lower
    /// <c>parent::$prop::get()/set()</c>.
    /// </summary>
    public partial class TyhpEmitter
    {
        private const string UsesPropertyAccessorsTraitFq = "\\Tyhp\\Concerns\\UsesPropertyAccessors";
        private const string BootsTraitsTraitFq = "\\Tyhp\\Concerns\\BootsTraits";
        private const string HasPropertyAccessorsTraitFq = "\\Tyhp\\Concerns\\HasPropertyAccessors";
        private const string HandlesGetTraitFq = "\\Tyhp\\Concerns\\HandlesGet";
        private const string HandlesSetTraitFq = "\\Tyhp\\Concerns\\HandlesSet";
        private const string HandlesIssetTraitFq = "\\Tyhp\\Concerns\\HandlesIsset";
        private const string HandlesUnsetTraitFq = "\\Tyhp\\Concerns\\HandlesUnset";

        /// <summary>
        /// When non-null, <c>$this-&gt;{name}</c> reads/writes in expressions emit as backing helpers.
        /// </summary>
        private string? _hookBackingPropertyName;

        /// <summary>
        /// Magic method names (<c>__get</c> etc.) owned by the current class that need a
        /// <c>tyhpTry*</c> preamble when emitting the method body.
        /// </summary>
        private readonly HashSet<string> _pendingMagicTryInject =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly List<PendingHookedProperty> _pendingHookedProperties = new();

        /// <summary>
        /// Property names that receive a synthetic free-generic set check (native set hook on
        /// PHP ≥ 8.4, or a PropertyAccessor polyfill registration below 8.4).
        /// </summary>
        private readonly HashSet<string> _pendingGenericSetCheckPropertyNames =
            new(StringComparer.Ordinal);

        private sealed class PendingHookedProperty
        {
            public required string Name { get; init; }
            public required ITypeExpression? Type { get; init; }

            /// <summary>
            /// Author-written hooks. Null when this pending entry is a synthetic free-generic set
            /// check with no author hooks.
            /// </summary>
            public PhpPropertyHookListAst? Hooks { get; init; }

            /// <summary>
            /// When true, registration emits a set-only PropertyAccessor that calls
            /// <c>$this-&gt;__tyhpGeneric-&gt;checkProperty</c> (PHP &lt; 8.4 polyfill for free generic props).
            /// </summary>
            public bool SyntheticGenericSetCheck { get; init; }

            public IExpression? DefaultValue { get; init; }
            public bool HasDefault { get; init; }

            /// <summary>
            /// Property (get) visibility: <c>public</c>, <c>protected</c>, or <c>private</c>.
            /// </summary>
            public string Visibility { get; init; } = "public";

            /// <summary>
            /// Effective set visibility: asymmetric <c>*(set)</c> when authored, otherwise the
            /// same as <see cref="Visibility"/>.
            /// </summary>
            public string SetVisibility { get; init; } = "public";

            /// <summary>
            /// When set, the property came from a promoted ctor param; use this variable as the
            /// initial backing value after construction (e.g. <c>$name</c>).
            /// </summary>
            public string? PromotedParamVariable { get; init; }

            /// <summary>
            /// True when this hooked property redeclares a plain (non-hooked) ancestor property.
            /// PHP 8.4 treats such overrides as backed (inherited storage); the polyfill must
            /// force <c>backed: true</c>, seed the inherited default before shadowing, and emit
            /// <c>@property</c> (not read/write-only).
            /// </summary>
            public bool OverridesInheritedPlainProperty { get; init; }

            /// <summary>
            /// FQN of the ancestor class that declares the plain property being overridden
            /// (for <c>ReflectionProperty</c>), when <see cref="OverridesInheritedPlainProperty"/>.
            /// </summary>
            public string? InheritedPlainPropertyDeclaringClassFqn { get; init; }
        }

        private bool ShouldLowerPropertyAccessors()
            => !this._context.IsPhpVersionAtLeast(8, 4);

        private void BeginPropertyAccessorObjectScope(PhpObjectTypeDeclAst objectDecl)
        {
            this._pendingHookedProperties.Clear();
            this._pendingGenericSetCheckPropertyNames.Clear();
            this._pendingMagicTryInject.Clear();
            this._hookBackingPropertyName = null;
            this._currentObjectInPropertyHookChain = false;
            this._currentObjectParentInPropertyHookChain = false;

            if (!this.ShouldLowerPropertyAccessors())
            {
                return;
            }

            foreach (var member in objectDecl.Body?.GetAllNotNull() ?? [])
            {
                if (member is PhpPropertyDeclAst propertyDecl)
                {
                    var visibility = ResolvePropertyVisibility(propertyDecl.Modifiers);
                    var setVisibility = ResolveSetVisibility(propertyDecl.Modifiers, visibility);
                    foreach (var property in propertyDecl.Properties?.GetAllNotNull() ?? [])
                    {
                        if (property.Hooks is null || !property.Hooks.GetAllNotNull().Any())
                        {
                            continue;
                        }

                        var name = property.Identifier?.TrimStart('$') ?? "";
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        var inherited = this.TryFindInheritedPlainProperty(name);
                        this._pendingHookedProperties.Add(new PendingHookedProperty
                        {
                            Name = name,
                            Type = propertyDecl.Type,
                            Hooks = property.Hooks,
                            DefaultValue = property.DefaultValue,
                            HasDefault = property.DefaultValue != null,
                            Visibility = visibility,
                            SetVisibility = setVisibility,
                            OverridesInheritedPlainProperty = inherited is not null,
                            InheritedPlainPropertyDeclaringClassFqn = inherited?.DeclaringClassFqn,
                        });
                    }
                }
                else if (member is PhpMethodDeclAst method
                    && string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var parameter in method.Parameters?.GetAllNotNull() ?? [])
                    {
                        if (parameter.PropertyHooks is not PhpPropertyHookListAst hooks
                            || !hooks.GetAllNotNull().Any())
                        {
                            continue;
                        }

                        // Only promoted params (visibility modifiers) become properties.
                        if (!IsPromotedParameter(parameter))
                        {
                            continue;
                        }

                        var name = parameter.Name?.TrimStart('$') ?? "";
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        var visibility = ResolvePropertyVisibility(parameter.Modifiers);
                        var inherited = this.TryFindInheritedPlainProperty(name);
                        this._pendingHookedProperties.Add(new PendingHookedProperty
                        {
                            Name = name,
                            Type = parameter.Type,
                            Hooks = hooks,
                            DefaultValue = parameter.DefaultValue,
                            HasDefault = true, // promoted value always initializes backing
                            PromotedParamVariable = parameter.Name,
                            Visibility = visibility,
                            SetVisibility = ResolveSetVisibility(parameter.Modifiers, visibility),
                            OverridesInheritedPlainProperty = inherited is not null,
                            InheritedPlainPropertyDeclaringClassFqn = inherited?.DeclaringClassFqn,
                        });
                    }
                }
            }

            var parent = this.TryResolveEmitParent(this._currentObjectSymbol);
            this._currentObjectParentInPropertyHookChain = this.SymbolIsInPropertyHookChain(parent);
            this._currentObjectInPropertyHookChain =
                this.ObjectHasPendingHookedProperties() || this._currentObjectParentInPropertyHookChain;
        }

        /// <summary>
        /// Recompute property-hook chain flags after synthetic free-generic set-check properties
        /// are queued (those are collected after <see cref="BeginPropertyAccessorObjectScope"/>).
        /// </summary>
        private void RefreshPropertyHookChainFlags()
        {
            if (!this.ShouldLowerPropertyAccessors())
            {
                this._currentObjectInPropertyHookChain = false;
                this._currentObjectParentInPropertyHookChain = false;
                return;
            }

            var parent = this.TryResolveEmitParent(this._currentObjectSymbol);
            this._currentObjectParentInPropertyHookChain = this.SymbolIsInPropertyHookChain(parent);
            this._currentObjectInPropertyHookChain =
                this.ObjectHasPendingHookedProperties() || this._currentObjectParentInPropertyHookChain;
        }

        /// <summary>
        /// True when <paramref name="symbol"/> or any ancestor declares a hooked property, and
        /// therefore participates in the <c>__initPropertyHooks__tyhpPropertyHook</c> chain when
        /// lowering for PHP &lt; 8.4.
        /// </summary>
        private bool SymbolIsInPropertyHookChain(ObjectDeclarationSymbol? symbol)
        {
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (symbol is not null && seen.Add(symbol))
            {
                foreach (var member in symbol.Members.Values)
                {
                    if (member is ObjectPropertySymbol { HasAccessor: true })
                    {
                        return true;
                    }
                }

                symbol = this.TryResolveEmitParent(symbol);
            }

            return false;
        }

        /// <summary>
        /// Walks the emit-time inheritance chain for a same-named plain (non-hooked) property.
        /// Hooked ancestors are handled by the declaring-class-keyed bag + init chain (Critical #2).
        /// A <c>private</c> ancestor property is not inherited storage at all — PHP treats a
        /// same-named child declaration as a brand-new, unrelated property (the private slot stays
        /// exclusive to its declaring class) — so it stops the walk without matching, the same as
        /// finding no plain ancestor property.
        /// </summary>
        private (ObjectPropertySymbol Property, string DeclaringClassFqn)? TryFindInheritedPlainProperty(
            string propertyName)
        {
            var ancestor = this.TryResolveEmitParent(this._currentObjectSymbol);
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (ancestor is not null && seen.Add(ancestor))
            {
                var prop = WithKeywordHelper.TryGetProperty(ancestor, propertyName);
                if (prop is not null)
                {
                    if (prop.HasAccessor || (prop.Visibility & MemberModifier.Private) != 0)
                    {
                        return null;
                    }

                    var fqn = ancestor.FullyQualifiedName;
                    if (string.IsNullOrWhiteSpace(fqn))
                    {
                        fqn = ancestor.Name;
                    }

                    return (prop, "\\" + fqn.TrimStart('\\'));
                }

                ancestor = this.TryResolveEmitParent(ancestor);
            }

            return null;
        }

        /// <summary>
        /// Collects free object-generic properties (<c>T</c> / <c>?T</c>) that need always-on
        /// runtime set checks. Runs for both PHP versions; below 8.4 also queues a synthetic
        /// PropertyAccessor registration.
        /// </summary>
        private void CollectFreeGenericSetCheckProperties(PhpObjectTypeDeclAst objectDecl)
        {
            if (!this._currentObjectNeedsGenericTracking && !this._currentObjectInGenericChain)
            {
                return;
            }

            if (this._currentObjectGenericParamNames.Count == 0)
            {
                return;
            }

            foreach (var member in objectDecl.Body?.GetAllNotNull() ?? [])
            {
                if (member is PhpPropertyDeclAst propertyDecl)
                {
                    if (HasReadonlyModifier(propertyDecl.Modifiers))
                    {
                        continue;
                    }

                    if (propertyDecl.Type is null
                        || !this.IsFreeObjectGenericPropertyType(propertyDecl.Type))
                    {
                        continue;
                    }

                    var visibility = ResolvePropertyVisibility(propertyDecl.Modifiers);
                    var setVisibility = ResolveSetVisibility(propertyDecl.Modifiers, visibility);
                    foreach (var property in propertyDecl.Properties?.GetAllNotNull() ?? [])
                    {
                        if (property.Hooks is not null && property.Hooks.GetAllNotNull().Any())
                        {
                            continue;
                        }

                        var name = property.Identifier?.TrimStart('$') ?? "";
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        this.AddFreeGenericSetCheckProperty(
                            name,
                            propertyDecl.Type,
                            defaultValue: property.DefaultValue,
                            hasDefault: property.DefaultValue != null,
                            promotedParamVariable: null,
                            visibility: visibility,
                            setVisibility: setVisibility);
                    }
                }
                else if (member is PhpMethodDeclAst method
                    && string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var parameter in method.Parameters?.GetAllNotNull() ?? [])
                    {
                        if (!IsPromotedParameter(parameter)
                            || HasReadonlyModifier(parameter.Modifiers))
                        {
                            continue;
                        }

                        if (parameter.Type is null
                            || !this.IsFreeObjectGenericPropertyType(parameter.Type))
                        {
                            continue;
                        }

                        if (parameter.PropertyHooks is PhpPropertyHookListAst authorHooks
                            && authorHooks.GetAllNotNull().Any())
                        {
                            continue;
                        }

                        var name = parameter.Name?.TrimStart('$') ?? "";
                        if (name.Length == 0)
                        {
                            continue;
                        }

                        var visibility = ResolvePropertyVisibility(parameter.Modifiers);
                        this.AddFreeGenericSetCheckProperty(
                            name,
                            parameter.Type,
                            defaultValue: parameter.DefaultValue,
                            hasDefault: true,
                            promotedParamVariable: parameter.Name,
                            visibility: visibility,
                            setVisibility: ResolveSetVisibility(parameter.Modifiers, visibility));
                    }
                }
            }

            this.RefreshPropertyHookChainFlags();
        }

        private void AddFreeGenericSetCheckProperty(
            string name,
            ITypeExpression type,
            IExpression? defaultValue,
            bool hasDefault,
            string? promotedParamVariable,
            string visibility,
            string setVisibility)
        {
            this._pendingGenericSetCheckPropertyNames.Add(name);

            if (!this.ShouldLowerPropertyAccessors())
            {
                return;
            }

            if (this._pendingHookedProperties.Any(p =>
                    string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                return;
            }

            this._pendingHookedProperties.Add(new PendingHookedProperty
            {
                Name = name,
                Type = type,
                Hooks = null,
                SyntheticGenericSetCheck = true,
                DefaultValue = defaultValue,
                HasDefault = hasDefault,
                PromotedParamVariable = promotedParamVariable,
                Visibility = visibility,
                SetVisibility = setVisibility,
            });
        }

        private static bool IsPromotedParameter(PhpParameterAst parameter)
            => parameter.Modifiers is not null
                && parameter.Modifiers.Modifiers.Any(m =>
                    m is PhpModifier.Public or PhpModifier.Protected or PhpModifier.Private
                        or PhpModifier.PublicSet or PhpModifier.ProtectedSet or PhpModifier.PrivateSet
                        or PhpModifier.Readonly);

        private static bool HasReadonlyModifier(PhpModifierListAst? modifiers)
            => modifiers?.Modifiers.Contains(PhpModifier.Readonly) == true;

        /// <summary>
        /// Property (get) visibility from <c>public</c>/<c>protected</c>/<c>private</c>.
        /// Defaults to <c>public</c> when omitted (PHP default for properties).
        /// </summary>
        private static string ResolvePropertyVisibility(PhpModifierListAst? modifiers)
        {
            if (modifiers is null)
            {
                return "public";
            }

            foreach (var modifier in modifiers.Modifiers)
            {
                switch (modifier)
                {
                    case PhpModifier.Private:
                        return "private";
                    case PhpModifier.Protected:
                        return "protected";
                    case PhpModifier.Public:
                        return "public";
                }
            }

            return "public";
        }

        /// <summary>
        /// Effective set visibility: asymmetric <c>*(set)</c> when present, otherwise
        /// <paramref name="propertyVisibility"/>.
        /// </summary>
        private static string ResolveSetVisibility(
            PhpModifierListAst? modifiers,
            string propertyVisibility)
        {
            if (modifiers is null)
            {
                return propertyVisibility;
            }

            foreach (var modifier in modifiers.Modifiers)
            {
                switch (modifier)
                {
                    case PhpModifier.PrivateSet:
                        return "private";
                    case PhpModifier.ProtectedSet:
                        return "protected";
                    case PhpModifier.PublicSet:
                        return "public";
                }
            }

            return propertyVisibility;
        }

        private bool ObjectHasPendingHookedProperties()
            => this._pendingHookedProperties.Count > 0;

        private void EmitPropertyAccessorsTraitUseIfNeeded(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
        {
            // Trait (and bag) apply once at the topmost hooked level — descendants inherit it.
            if (!this.ObjectHasPendingHookedProperties() || this._currentObjectParentInPropertyHookChain)
            {
                return;
            }

            if (ObjectAlreadyUsesPropertyAccessorsTrait(objectDecl))
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            var owned = CollectOwnedMagicMethods(objectDecl);
            var ancestor = CollectAncestorMagicMethods(this._currentObjectSymbol);

            this._pendingMagicTryInject.Clear();
            foreach (var magic in owned)
            {
                this._pendingMagicTryInject.Add(magic);
            }

            var ownsNone = owned.Count == 0;
            var ancestorNone = !ancestor.Contains("__get")
                && !ancestor.Contains("__set")
                && !ancestor.Contains("__isset")
                && !ancestor.Contains("__unset");

            if (ownsNone && ancestorNone)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.ObjectTraitUse,
                    $"use {UsesPropertyAccessorsTraitFq};",
                    classBlock);
                return;
            }

            // Piece traits: skip Handles* only when an ancestor already provides that magic
            // (re-using the trait would conflict with final __get etc.). Class-owned magics
            // still need Handles* for tyhpTry*/tyhpRegister* — the class method overrides the
            // trait magic at composition time.
            var needsGet = !ancestor.Contains("__get");
            var needsSet = !ancestor.Contains("__set");
            var needsIsset = !ancestor.Contains("__isset");
            var needsUnset = !ancestor.Contains("__unset");

            var needsBoots = !AncestorProvidesMember(this._currentObjectSymbol, "tyhpBootTraits");
            if (needsBoots)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.ObjectTraitUse,
                    $"use {BootsTraitsTraitFq};",
                    classBlock);
            }

            if (needsGet)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.ObjectTraitUse,
                    $"use {HandlesGetTraitFq};",
                    classBlock);
            }

            if (needsSet)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.ObjectTraitUse,
                    $"use {HandlesSetTraitFq};",
                    classBlock);
            }

            if (needsIsset)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.ObjectTraitUse,
                    $"use {HandlesIssetTraitFq};",
                    classBlock);
            }

            if (needsUnset)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.ObjectTraitUse,
                    $"use {HandlesUnsetTraitFq};",
                    classBlock);
            }

            EmitItem.Line(
                objectDecl,
                EmitType.ObjectTraitUse,
                $"use {HasPropertyAccessorsTraitFq};",
                classBlock);
        }

        private static HashSet<string> CollectOwnedMagicMethods(PhpObjectTypeDeclAst objectDecl)
        {
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in objectDecl.Body?.GetAllNotNull() ?? [])
            {
                if (member is not PhpMethodDeclAst method)
                {
                    continue;
                }

                var name = method.Identifier ?? "";
                if (name is "__get" or "__set" or "__isset" or "__unset")
                {
                    owned.Add(name);
                }
            }

            return owned;
        }

        private HashSet<string> CollectAncestorMagicMethods(ObjectDeclarationSymbol? symbol)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parent = this.TryResolveEmitParent(symbol);
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (parent is not null && seen.Add(parent))
            {
                foreach (var magic in new[] { "__get", "__set", "__isset", "__unset" })
                {
                    if (parent.Members.ContainsKey(magic))
                    {
                        found.Add(magic);
                    }
                }

                parent = this.TryResolveEmitParent(parent);
            }

            return found;
        }

        private bool AncestorProvidesMember(ObjectDeclarationSymbol? symbol, string memberName)
        {
            var parent = this.TryResolveEmitParent(symbol);
            var seen = new HashSet<ObjectDeclarationSymbol>();
            while (parent is not null && seen.Add(parent))
            {
                if (parent.Members.ContainsKey(memberName))
                {
                    return true;
                }

                parent = this.TryResolveEmitParent(parent);
            }

            return false;
        }

        private static bool ObjectAlreadyUsesPropertyAccessorsTrait(PhpObjectTypeDeclAst objectDecl)
        {
            foreach (var member in objectDecl.Body?.GetAllNotNull() ?? [])
            {
                if (member is not PhpTraitUseAst traitUse)
                {
                    continue;
                }

                foreach (var traitName in traitUse.TraitNames?.GetAllNotNull() ?? [])
                {
                    var text = traitName.Identifier ?? (traitName as PhpNameAst)?.ValueString ?? "";
                    var normalized = text.TrimStart('\\');
                    if (string.Equals(normalized, "Tyhp\\Concerns\\UsesPropertyAccessors", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "Concerns\\UsesPropertyAccessors", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "UsesPropertyAccessors", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "Tyhp\\Concerns\\HasPropertyAccessors", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalized, "HasPropertyAccessors", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ShouldSkipEmittingHookedProperty(PhpPropertyAst property)
        {
            if (!this.ShouldLowerPropertyAccessors())
            {
                return false;
            }

            var name = property.Identifier?.TrimStart('$') ?? "";
            if (name.Length > 0 && this._pendingGenericSetCheckPropertyNames.Contains(name))
            {
                return true;
            }

            if (property.Hooks is null || !property.Hooks.GetAllNotNull().Any())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Native set-hook block that runs the free-generic property check then assigns.
        /// Multiline so PHPCS DisallowMultipleStatements / brace sniffs stay happy.
        /// </summary>
        private string BuildSyntheticGenericSetHookBlock(string propertyName)
        {
            var escaped = EscapePhpSingleQuoted(propertyName);
            return " {\n"
                + "    set(mixed $value) {\n"
                + $"        $this->__tyhpGeneric->checkProperty('{escaped}', $value);\n"
                + $"        $this->{propertyName} = $value;\n"
                + "    }\n"
                + "}";
        }

        private bool NeedsSyntheticGenericSetHook(string propertyName)
            => propertyName.Length > 0
                && this._pendingGenericSetCheckPropertyNames.Contains(propertyName);

        private void EmitGenericObjectEnablePropertyChecksIfNeeded(IBase2Ast provider, EmitItem methodBlock)
        {
            if (this._pendingGenericSetCheckPropertyNames.Count == 0)
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");
            EmitItem.Line(
                provider,
                EmitType.FunctionStatement,
                "$this->__tyhpGeneric->enablePropertyChecks();",
                methodBlock);
        }

        private void EmitPropertyAccessorConstructorPrologue(
            IBase2Ast provider,
            EmitItem methodBlock,
            EmitItem classBlock)
        {
            if (!this._currentObjectInPropertyHookChain)
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            // Generic-chain ctors already boot via EmitGenericObjectConstructorPrologue (always
            // called first). Emitting again would duplicate `$this->tyhpBootTraits();`.
            // The init hook also boots (factory / newInstanceWithoutConstructor path).
            if (!this._currentObjectInGenericChain)
            {
                EmitItem.Line(
                    provider,
                    EmitType.FunctionStatement,
                    "$this->tyhpBootTraits();",
                    methodBlock);
            }

            // `self::` pins the call to this level's own hook (same rationale as generics).
            EmitItem.MultiLine(
                provider,
                EmitType.FunctionStatement,
                [
                    "if ($this->__tyhpPropertyHook->needsInit()) {",
                    $"    self::{GeneratedNames.PropertyHookInitHook}();",
                    "}",
                ],
                methodBlock);

            // Promoted ctor params are not in scope inside the init hook — seed backing here.
            foreach (var pending in this._pendingHookedProperties)
            {
                if (pending.PromotedParamVariable is null)
                {
                    continue;
                }

                this.EmitPropertyAccessorRegistration(provider, methodBlock, classBlock, pending);
            }
        }

        /// <summary>
        /// Emits <c>__initPropertyHooks__tyhpPropertyHook</c>: boot traits, register this level's
        /// accessors (keyed by declaring class), then either chain to the parent init hook or
        /// <c>markBound()</c>. Independent of constructor chaining — mirrors Mechanism C generics.
        /// Promoted-parameter hooked properties are registered from the constructor instead (their
        /// values are not in scope here).
        /// </summary>
        private void EmitPropertyHookInitHook(PhpObjectTypeDeclAst objectDecl, EmitItem classBlock)
        {
            if (!this._currentObjectInPropertyHookChain)
            {
                return;
            }

            this._context.RequirePackage("tyhp/core");

            var block = EmitItem.BlockBraceNextLine(
                objectDecl,
                EmitType.ObjectInstanceMethods,
                $"protected function {GeneratedNames.PropertyHookInitHook}(): void",
                "}",
                classBlock);

            var lines = new List<string>
            {
                // Factories / newInstanceWithoutConstructor — boot traits (create bag) first.
                "$this->tyhpBootTraits();",
            };

            var nonPromoted = this._pendingHookedProperties
                .Where(p => p.PromotedParamVariable is null)
                .ToList();

            if (nonPromoted.Count > 0)
            {
                lines.Add("if ($this->__tyhpPropertyHook->isInitialized(self::class)) {");
                lines.Add("    return;");
                lines.Add("}");
            }

            EmitItem.MultiLine(objectDecl, EmitType.FunctionStatement, lines, block);

            foreach (var pending in nonPromoted)
            {
                this.EmitPropertyAccessorRegistration(objectDecl, block, classBlock, pending);
            }

            var tail = new List<string>();
            if (this._currentObjectParentInPropertyHookChain)
            {
                tail.Add($"parent::{GeneratedNames.PropertyHookInitHook}();");
            }
            else
            {
                // Topmost hooked level: the chain completes here.
                tail.Add("$this->__tyhpPropertyHook->markBound();");
            }

            EmitItem.MultiLine(objectDecl, EmitType.FunctionStatement, tail, block);
        }

        /// <summary>
        /// Synthesizes a constructor for a class in the property-hook chain that declares none —
        /// including a pass-through subclass that adds no hooks of its own but sits between two
        /// hooked levels. Forwards the inherited constructor's parameter list and calls it, the
        /// same as <see cref="EmitSynthesizedGenericConstructor"/> — a bare no-arg ctor here would
        /// silently drop an ancestor constructor's required parameters and body statements instead
        /// of the normal PHP "no override ⇒ inherit the constructor" behavior.
        /// </summary>
        private void EmitSynthesizedPropertyAccessorConstructorIfNeeded(
            PhpObjectTypeDeclAst objectDecl,
            EmitItem classBlock)
        {
            if (!this._currentObjectInPropertyHookChain || this._currentObjectEmittedConstructor)
            {
                return;
            }

            // Generic synthesis already creates a ctor when needed; if that ran, `_currentObjectEmittedConstructor`
            // is still false until EmitSynthesizedGenericConstructor runs. Call order: members first
            // (may emit author ctor), then generic synth, then this synth.
            this._context.RequirePackage("tyhp/core");
            this._currentObjectEmittedConstructor = true;

            var inheritedCtor = this.TryFindConstructorForEmit(out var isInherited);
            var parameters = isInherited ? inheritedCtor?.Parameters : null;

            var block = EmitItem.BlockBraceNextLine(
                objectDecl,
                EmitType.ObjectConstructor,
                $"public function __construct({this.BuildForwardingParameterList(parameters)})",
                "}",
                classBlock);

            this.EmitGenericObjectConstructorPrologue(objectDecl, block, includeUserParamChecks: false);
            this.EmitPropertyAccessorConstructorPrologue(objectDecl, block, classBlock);

            // Keyed on whether an ancestor declares a constructor at all, not on whether it takes
            // parameters: a no-argument ancestor constructor still has to run (same rationale as
            // EmitSynthesizedGenericConstructor).
            if (isInherited)
            {
                EmitItem.Line(
                    objectDecl,
                    EmitType.FunctionStatement,
                    $"parent::__construct({BuildForwardingArguments(parameters)});",
                    block);
            }

            this.EmitGenericObjectEnablePropertyChecksIfNeeded(objectDecl, block);
        }

        /// <summary>
        /// PHPDoc <c>@property</c> / <c>@property-read</c> / <c>@property-write</c> tags so Phan,
        /// PHPStan, and Psalm see polyfilled hooked properties (emitted via magic accessors, not
        /// real PHP properties). Always emitted when lowering — independent of IncludeComments.
        /// Non-public (<c>protected</c>/<c>private</c>) props are omitted so SA does not treat
        /// them as public magic API. On a generic host class, also emits <c>@template</c> tags
        /// (when missing) and spells property types with preserved type-parameter names
        /// (<c>TValue</c>, not erased <c>mixed</c>).
        /// </summary>
        private void AttachPropertyAccessorMagicPropertyDocs(
            PhpObjectTypeDeclAst objectDecl,
            EmitItem classBlock)
        {
            if (!this.ShouldLowerPropertyAccessors() || !this.ObjectHasPendingHookedProperties())
            {
                return;
            }

            var tagLines = new List<string>();
            foreach (var pending in this._pendingHookedProperties)
            {
                if (!string.Equals(pending.Visibility, "public", StringComparison.Ordinal))
                {
                    continue;
                }

                var backed = IsBackedHookedProperty(pending);
                var tag = ResolvePropertyPhpDocTag(pending, backed);
                var type = pending.Type != null
                    ? this.BuildPhpDocTypeExpression(pending.Type)
                    : "mixed";
                if (string.IsNullOrWhiteSpace(type))
                {
                    type = "mixed";
                }

                tagLines.Add($" * {tag} {type} ${pending.Name}");
            }

            if (tagLines.Count == 0)
            {
                return;
            }

            var templateLines = this.BuildGenericTemplatePhpDocTags();
            MergeMagicPropertyPhpDocTags(classBlock, templateLines, tagLines);
        }

        private string BuildPhpDocTypeExpression(ITypeExpression? typeExpression)
            => TypeSpellingHelper.SpellForPhpDoc(
                typeExpression,
                this._context.TypeAliasMap,
                this._context.GlobalScope,
                this._context.Config.NamespacePrefix);

        /// <summary>
        /// <c>@template</c> / <c>@template-covariant</c> / <c>@template-contravariant</c> lines for
        /// the current object type's own generic parameters (Mechanism C hosts).
        /// </summary>
        private List<string> BuildGenericTemplatePhpDocTags()
        {
            var ownParams = this._currentObjectGenericParams;
            if (ownParams.Count == 0)
            {
                return [];
            }

            var lines = new List<string>(ownParams.Count);
            foreach (var param in ownParams)
            {
                var tag = param.Variance switch
                {
                    TypeVariance.Covariant => "@template-covariant",
                    TypeVariance.Contravariant => "@template-contravariant",
                    _ => "@template",
                };
                var line = $" * {tag} {param.Name}";
                if (param.Constraint != null)
                {
                    // PHPDoc surface: a constraint that itself references another type parameter
                    // (e.g. `<T extends TValue>`) must keep that name, not erase to `mixed` — same
                    // rationale as property types (see BuildPhpDocTypeExpression).
                    var constraint = this.BuildPhpDocTypeExpression(param.Constraint);
                    if (!string.IsNullOrWhiteSpace(constraint) && constraint != "mixed")
                    {
                        line += " of " + constraint;
                    }
                }

                lines.Add(line);
            }

            return lines;
        }

        /// <summary>
        /// Matches PHP 8.4 backed vs virtual: a hooked property is backed when it has a default,
        /// an arrow <c>set =&gt; expr</c> (implicit write to storage), a self-reference
        /// (<c>$this-&gt;prop</c>) in any hook, a synthetic free-generic set check, or it
        /// redeclares a plain ancestor property (inherited storage — omitted get/set still
        /// default-read/write that storage).
        /// Omitting <c>get</c> or <c>set</c> alone does <em>not</em> make a property backed —
        /// on a virtual property the omitted operation simply does not exist.
        /// </summary>
        private static bool IsBackedHookedProperty(PendingHookedProperty pending)
        {
            if (pending.SyntheticGenericSetCheck
                || pending.HasDefault
                || pending.OverridesInheritedPlainProperty)
            {
                return true;
            }

            var setHook = FindHook(pending.Hooks, "set");
            if (setHook != null && TryGetSingleReturnOperand(setHook, out _))
            {
                return true;
            }

            return HookListReferencesProperty(pending.Hooks, pending.Name);
        }

        /// <summary>
        /// On backed properties, a missing hook still has default read/write. On virtual
        /// properties, a missing hook means that operation does not exist.
        /// </summary>
        private static string ResolvePropertyPhpDocTag(PendingHookedProperty pending, bool backed)
        {
            if (pending.SyntheticGenericSetCheck)
            {
                return "@property";
            }

            var getHook = FindHook(pending.Hooks, "get");
            var setHook = FindHook(pending.Hooks, "set");
            var canGet = backed || getHook != null;
            var canSet = backed || setHook != null;

            if (canGet && canSet)
            {
                return "@property";
            }

            if (canGet)
            {
                return "@property-read";
            }

            if (canSet)
            {
                return "@property-write";
            }

            return "@property";
        }

        private static void MergeMagicPropertyPhpDocTags(
            EmitItem classBlock,
            List<string> templateLines,
            List<string> propertyTagLines)
        {
            var start = classBlock.StartContent is List<string> list
                ? list
                : classBlock.StartContent.ToList();
            if (!ReferenceEquals(start, classBlock.StartContent))
            {
                classBlock.StartContent = start;
            }

            var docStart = -1;
            var docEnd = -1;
            for (var i = 0; i < start.Count; i++)
            {
                var trimmed = start[i].TrimStart();
                if (docStart < 0)
                {
                    if (trimmed.StartsWith("/**", StringComparison.Ordinal))
                    {
                        docStart = i;
                    }
                    else if (!string.IsNullOrWhiteSpace(start[i]))
                    {
                        break;
                    }
                }

                if (docStart >= 0 && trimmed.EndsWith("*/", StringComparison.Ordinal))
                {
                    docEnd = i;
                    break;
                }
            }

            if (docStart >= 0 && docEnd >= 0)
            {
                var existing = string.Join('\n', start.Skip(docStart).Take(docEnd - docStart + 1));
                var templatesToInsert = templateLines
                    .Where(tag => !PhpDocAlreadyHasTemplateTag(existing, tag))
                    .ToList();
                var propertiesToInsert = propertyTagLines
                    .Where(tag =>
                    {
                        // Avoid duplicating an author-written @property* for the same $name.
                        var dollar = tag.LastIndexOf('$');
                        if (dollar < 0)
                        {
                            return true;
                        }

                        var name = tag[(dollar + 1)..].Trim();
                        return !PhpDocAlreadyHasPropertyTagFor(existing, name);
                    })
                    .ToList();

                var toInsert = new List<string>(templatesToInsert.Count + propertiesToInsert.Count);
                toInsert.AddRange(templatesToInsert);
                toInsert.AddRange(propertiesToInsert);
                if (toInsert.Count == 0)
                {
                    return;
                }

                start.InsertRange(docEnd, toInsert);
                return;
            }

            var doc = new List<string> { "/**" };
            doc.AddRange(templateLines);
            doc.AddRange(propertyTagLines);
            doc.Add(" */");
            classBlock.StartContent = [.. doc, .. start];
        }

        /// <summary>
        /// True when the existing class docblock already declares the same <c>@template*</c>
        /// parameter name (author-written or prior emit).
        /// </summary>
        private static bool PhpDocAlreadyHasTemplateTag(string existingDoc, string templateTagLine)
        {
            // " * @template TValue" / " * @template-covariant TValue of Foo"
            var trimmed = templateTagLine.TrimStart();
            if (trimmed.StartsWith('*'))
            {
                trimmed = trimmed[1..].TrimStart();
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            var paramName = parts[1];
            // Match any variance form already present for this parameter name. A plain
            // substring check would let `T` false-positive-match an existing `TValue` (since
            // "@template T" is a prefix of "@template TValue"), so require the name to end at a
            // non-identifier boundary (whitespace, `*`, or end of doc).
            return DocContainsTagFor(existingDoc, "@template ", paramName)
                || DocContainsTagFor(existingDoc, "@template-covariant ", paramName)
                || DocContainsTagFor(existingDoc, "@template-contravariant ", paramName)
                || DocContainsTagFor(existingDoc, "@phpstan-template ", paramName)
                || DocContainsTagFor(existingDoc, "@psalm-template ", paramName);
        }

        /// <summary>
        /// True when the existing class docblock already declares an <c>@property*</c> tag for
        /// <c>$propertyName</c> (author-written or prior emit). Requires a non-identifier boundary
        /// after the name so <c>$value</c> does not false-positive-match an existing
        /// <c>$valueOverride</c>.
        /// </summary>
        private static bool PhpDocAlreadyHasPropertyTagFor(string existingDoc, string propertyName)
            => DocContainsTagFor(existingDoc, "$", propertyName);

        /// <summary>
        /// True when <paramref name="existingDoc"/> contains <paramref name="prefix"/> immediately
        /// followed by <paramref name="name"/>, where <paramref name="name"/> is not itself a
        /// prefix of a longer identifier at that position (next character, if any, is not a valid
        /// identifier continuation character).
        /// </summary>
        private static bool DocContainsTagFor(string existingDoc, string prefix, string name)
        {
            var needle = prefix + name;
            var searchFrom = 0;
            while (true)
            {
                var index = existingDoc.IndexOf(needle, searchFrom, StringComparison.Ordinal);
                if (index < 0)
                {
                    return false;
                }

                var nextCharIndex = index + needle.Length;
                var boundaryOk = nextCharIndex >= existingDoc.Length
                    || !IsIdentifierContinuationChar(existingDoc[nextCharIndex]);
                if (boundaryOk)
                {
                    return true;
                }

                searchFrom = index + 1;
            }
        }

        private static bool IsIdentifierContinuationChar(char c)
            => char.IsLetterOrDigit(c) || c == '_';

        private void EmitPropertyAccessorRegistration(
            IBase2Ast provider,
            EmitItem methodBlock,
            EmitItem classBlock,
            PendingHookedProperty pending)
        {
            if (pending.SyntheticGenericSetCheck)
            {
                this.EmitSyntheticGenericSetCheckRegistration(provider, methodBlock, classBlock, pending);
                return;
            }

            var typeExpr = this.BuildRuntimeTypeExpression(pending.Type, preferCtorLocals: false);
            var getHook = FindHook(pending.Hooks, "get");
            var setHook = FindHook(pending.Hooks, "set");
            var setIsArrow = setHook != null && TryGetSingleReturnOperand(setHook, out _);
            var backed = IsBackedHookedProperty(pending);

            // Capture inherited plain-parent value before register shadows via unset.
            // Promoted params / author defaults already seed backing; skip those.
            if (pending.OverridesInheritedPlainProperty
                && pending.PromotedParamVariable is null
                && !pending.HasDefault
                && !string.IsNullOrEmpty(pending.InheritedPlainPropertyDeclaringClassFqn))
            {
                this.EmitInheritedPlainPropertyDefaultCapture(provider, methodBlock, pending);
            }

            // Mechanism D: register<TType>(…) → register__tyhpGeneric(Type)(…). Builds
            // PropertyAccessor<TType> internally; declaringClass stays self::class (not $host::class).
            var binder = GeneratedNames.GenericVariantSuffix;
            var lines = new List<string>
            {
                $"$this->__tyhpPropertyHook->register{binder}({typeExpr})(",
                $"    '{EscapePhpSingleQuoted(pending.Name)}',",
                "    $this,",
            };

            if (getHook != null)
            {
                var getMethod = GeneratedNames.PropertyHookGetMethod(pending.Name);
                var getBody = this.BuildHookClosureBody(getHook, pending.Name, isSetHook: false);
                this.EmitNamedPropertyHookMethod(
                    provider,
                    classBlock,
                    getMethod,
                    parameters: "",
                    returnType: "mixed",
                    bodyWithBraces: getBody);
                lines.Add($"    get: $this->{getMethod}(...),");
            }

            ITypeExpression? setParamType = null;
            if (setHook != null)
            {
                var valueParam = ResolveSetParameterName(setHook);
                setParamType = ResolveSetParameterType(setHook);
                // PHP: untyped set param defaults to the property type. Spell that (not mixed)
                // so polyfill methods match authored / native-hook surface (Critical #7).
                var setParamSpell = this.SpellSetHookParameterType(setParamType, pending.Type);
                var setMethod = GeneratedNames.PropertyHookSetMethod(pending.Name);
                string setBody;
                if (setIsArrow && TryGetSingleReturnOperand(setHook, out var arrowExpr) && arrowExpr != null)
                {
                    // set => expr  →  assign expression result to backing
                    string exprText;
                    var previous = this._hookBackingPropertyName;
                    this._hookBackingPropertyName = pending.Name;
                    try
                    {
                        exprText = this.BuildExpression(arrowExpr);
                    }
                    finally
                    {
                        this._hookBackingPropertyName = previous;
                    }

                    setBody =
                        "{\n"
                        + $"    $this->__tyhpPropertyHook->setBacking('{EscapePhpSingleQuoted(pending.Name)}', {exprText}, self::class);\n"
                        + "}";
                }
                else
                {
                    setBody = this.BuildHookClosureBody(setHook, pending.Name, isSetHook: true);
                }

                this.EmitNamedPropertyHookMethod(
                    provider,
                    classBlock,
                    setMethod,
                    parameters: $"{setParamSpell} ${valueParam}",
                    returnType: "void",
                    bodyWithBraces: setBody);
                lines.Add($"    set: $this->{setMethod}(...),");
            }

            lines.Add($"    backed: {(backed ? "true" : "false")},");
            lines.Add("    declaringClass: self::class,");

            this.AppendPropertyAccessorDefaultArgs(lines, pending);

            if (!string.Equals(pending.Visibility, "public", StringComparison.Ordinal))
            {
                lines.Add($"    visibility: '{EscapePhpSingleQuoted(pending.Visibility)}',");
            }

            if (!string.Equals(pending.SetVisibility, "public", StringComparison.Ordinal))
            {
                lines.Add($"    setVisibility: '{EscapePhpSingleQuoted(pending.SetVisibility)}',");
            }

            // Wider / typed set params: PA checks setAcceptType before the hook runs; property
            // TValue is still enforced in setBacking after conversion (Critical #3).
            if (setParamType != null)
            {
                var setAcceptRuntime = this.BuildRuntimeTypeExpression(setParamType, preferCtorLocals: false);
                lines.Add($"    setAcceptType: {setAcceptRuntime},");
            }

            // PHP 8.4 `final get` / `final set`: polyfill rejects child re-registration (Medium #6).
            if (HookHasFinalModifier(getHook))
            {
                lines.Add("    finalGet: true,");
            }

            if (HookHasFinalModifier(setHook))
            {
                lines.Add("    finalSet: true,");
            }

            lines.Add(");");

            EmitItem.MultiLine(provider, EmitType.FunctionStatement, lines, methodBlock);

            this.EmitPromotedSetRouteIfNeeded(provider, methodBlock, pending);
        }

        /// <summary>
        /// True when a promoted ctor param's initial value must be routed through
        /// <c>PropertyAccessor::set()</c> instead of pre-seeded as <c>defaultValue</c> — i.e. it has
        /// an authored or synthetic (free-generic) <c>set</c> hook. PHP 8.4 native promotion runs
        /// promoted assignment through the property's set hook (transform, type check, side
        /// effects) exactly as if the constructor body started with <c>$this-&gt;prop = $prop;</c>;
        /// pre-seeding <c>defaultValue</c> instead bypasses the hook and every check it performs.
        /// </summary>
        private static bool RoutesPromotedValueThroughSet(PendingHookedProperty pending)
            => pending.PromotedParamVariable is not null
                && (pending.SyntheticGenericSetCheck || FindHook(pending.Hooks, "set") != null);

        /// <summary>
        /// Emits <c>$this-&gt;__tyhpPropertyHook-&gt;set('{name}', {ctorArg});</c> right after
        /// registration for a promoted property whose value must run through the set hook (see
        /// <see cref="RoutesPromotedValueThroughSet"/>). Backing is left unseeded by
        /// <see cref="AppendPropertyAccessorDefaultArgs"/> in that case — this call is what
        /// actually initializes it, via the same visibility / type-check path as any other write.
        /// </summary>
        private void EmitPromotedSetRouteIfNeeded(
            IBase2Ast provider,
            EmitItem methodBlock,
            PendingHookedProperty pending)
        {
            if (pending.PromotedParamVariable is not { } promotedVar || !RoutesPromotedValueThroughSet(pending))
            {
                return;
            }

            EmitItem.Line(
                provider,
                EmitType.FunctionStatement,
                $"$this->__tyhpPropertyHook->set('{EscapePhpSingleQuoted(pending.Name)}', {promotedVar});",
                methodBlock);
        }

        /// <summary>
        /// PHP &lt; 8.4 polyfill for free generic property set checks: PropertyAccessor with
        /// <c>Type::mixed()</c> (so PA's built-in check is a no-op) and a set method that calls
        /// <c>$this-&gt;__tyhpGeneric-&gt;checkProperty</c>.
        /// </summary>
        private void EmitSyntheticGenericSetCheckRegistration(
            IBase2Ast provider,
            EmitItem methodBlock,
            EmitItem classBlock,
            PendingHookedProperty pending)
        {
            var escaped = EscapePhpSingleQuoted(pending.Name);
            var setMethod = GeneratedNames.PropertyHookSetMethod(pending.Name);
            var setBody =
                "{\n"
                + $"    $this->__tyhpGeneric->checkProperty('{escaped}', $value);\n"
                + $"    $this->__tyhpPropertyHook->setBacking('{escaped}', $value, self::class);\n"
                + "}";
            this.EmitNamedPropertyHookMethod(
                provider,
                classBlock,
                setMethod,
                parameters: "mixed $value",
                returnType: "void",
                bodyWithBraces: setBody);

            var binder = GeneratedNames.GenericVariantSuffix;
            var lines = new List<string>
            {
                $"$this->__tyhpPropertyHook->register{binder}({RuntimeTypeClassFq}::mixed())(",
                $"    '{escaped}',",
                "    $this,",
                $"    set: $this->{setMethod}(...),",
                "    backed: true,",
                "    declaringClass: self::class,",
            };

            this.AppendPropertyAccessorDefaultArgs(lines, pending);

            if (!string.Equals(pending.Visibility, "public", StringComparison.Ordinal))
            {
                lines.Add($"    visibility: '{EscapePhpSingleQuoted(pending.Visibility)}',");
            }

            if (!string.Equals(pending.SetVisibility, "public", StringComparison.Ordinal))
            {
                lines.Add($"    setVisibility: '{EscapePhpSingleQuoted(pending.SetVisibility)}',");
            }

            lines.Add(");");

            EmitItem.MultiLine(provider, EmitType.FunctionStatement, lines, methodBlock);

            this.EmitPromotedSetRouteIfNeeded(provider, methodBlock, pending);
        }

        /// <summary>
        /// Emits <c>defaultValue</c> / <c>defaultValueIsNull</c> named args for register.
        /// Non-null <c>defaultValue</c> alone implies a default; <c>defaultValueIsNull: true</c>
        /// covers <c>= null</c>. Promoted params pass the ctor arg and
        /// <c>defaultValueIsNull: $arg === null</c> so a runtime null still initializes backing —
        /// unless the value must instead run through the set hook first (see
        /// <see cref="RoutesPromotedValueThroughSet"/> / <see cref="EmitPromotedSetRouteIfNeeded"/>),
        /// in which case backing is left unseeded here and that follow-up call initializes it.
        /// Child overrides of plain parent properties pass temps filled by
        /// <see cref="EmitInheritedPlainPropertyDefaultCapture"/>.
        /// </summary>
        private void AppendPropertyAccessorDefaultArgs(List<string> lines, PendingHookedProperty pending)
        {
            if (pending.PromotedParamVariable is { } promotedVar)
            {
                if (RoutesPromotedValueThroughSet(pending))
                {
                    return;
                }

                lines.Add($"    defaultValue: {promotedVar},");
                lines.Add($"    defaultValueIsNull: {promotedVar} === null,");
                return;
            }

            if (pending.HasDefault && pending.DefaultValue is not null)
            {
                if (IsNullLiteralExpression(pending.DefaultValue))
                {
                    lines.Add("    defaultValueIsNull: true,");
                    return;
                }

                lines.Add($"    defaultValue: {this.BuildExpression(pending.DefaultValue)},");
                return;
            }

            if (pending.OverridesInheritedPlainProperty
                && !string.IsNullOrEmpty(pending.InheritedPlainPropertyDeclaringClassFqn))
            {
                var valueVar = InheritedPlainPropertyValueTemp(pending.Name);
                var isNullVar = InheritedPlainPropertyIsNullTemp(pending.Name);
                lines.Add($"    defaultValue: {valueVar},");
                lines.Add($"    defaultValueIsNull: {isNullVar},");
            }
        }

        /// <summary>
        /// Emits Reflection capture of a plain parent property's instance value into temps used as
        /// <c>defaultValue</c> / <c>defaultValueIsNull</c> — must run before <c>register</c> shadows
        /// the inherited field (same pattern as <c>PropertyHookPolyfillSmokeTest</c>).
        /// </summary>
        private void EmitInheritedPlainPropertyDefaultCapture(
            IBase2Ast provider,
            EmitItem methodBlock,
            PendingHookedProperty pending)
        {
            var valueVar = InheritedPlainPropertyValueTemp(pending.Name);
            var isNullVar = InheritedPlainPropertyIsNullTemp(pending.Name);
            var rpVar = InheritedPlainPropertyReflectionTemp(pending.Name);
            var declaringClass = pending.InheritedPlainPropertyDeclaringClassFqn!;
            var escapedName = EscapePhpSingleQuoted(pending.Name);

            var lines = new List<string>
            {
                $"{valueVar} = null;",
                $"{isNullVar} = false;",
                "try {",
                $"    {rpVar} = new \\ReflectionProperty({declaringClass}::class, '{escapedName}');",
                $"    {rpVar}->setAccessible(true);",
                $"    if ({rpVar}->isInitialized($this)) {{",
                $"        {valueVar} = {rpVar}->getValue($this);",
                $"        {isNullVar} = {valueVar} === null;",
                "    }",
                "} catch (\\Throwable) {",
                "}",
            };

            EmitItem.MultiLine(provider, EmitType.FunctionStatement, lines, methodBlock);
        }

        private static string InheritedPlainPropertyValueTemp(string propertyName)
            => "$__tyhp_inherited_" + propertyName;

        private static string InheritedPlainPropertyIsNullTemp(string propertyName)
            => "$__tyhp_inherited_" + propertyName + "_isNull";

        private static string InheritedPlainPropertyReflectionTemp(string propertyName)
            => "$__tyhp_rp_" + propertyName;

        /// <summary>
        /// True when <paramref name="expression"/> is the literal <c>null</c> (property/param default).
        /// </summary>
        private static bool IsNullLiteralExpression(IExpression? expression) =>
            expression is PhpNameAst { ValueString: "null" };

        /// <summary>
        /// Emits a private polyfill hook method whose body is the lowered get/set hook.
        /// Constructors pass <c>$this-&gt;{method}(...)</c> first-class callables into
        /// <c>PropertyAccessor</c> registration so large hook bodies stay out of <c>__construct</c>.
        /// </summary>
        private void EmitNamedPropertyHookMethod(
            IBase2Ast provider,
            EmitItem classBlock,
            string methodName,
            string parameters,
            string returnType,
            string bodyWithBraces)
        {
            var signature = string.IsNullOrEmpty(parameters)
                ? $"private function {methodName}(): {returnType}"
                : $"private function {methodName}({parameters}): {returnType}";

            var methodEmit = EmitItem.BlockBraceNextLine(
                provider,
                EmitType.ObjectInstanceMethods,
                signature,
                "}",
                classBlock);

            var statements = ExtractBraceBodyStatements(bodyWithBraces);
            if (statements.Count > 0)
            {
                EmitItem.MultiLine(provider, EmitType.FunctionStatement, statements, methodEmit);
            }
        }

        /// <summary>
        /// Splits a <c>{ … }</c> body from <see cref="BuildHookClosureBody"/> into statement lines
        /// suitable for <see cref="EmitItem.MultiLine"/> inside a method block.
        /// </summary>
        private static List<string> ExtractBraceBodyStatements(string bodyWithBraces)
        {
            var normalized = bodyWithBraces.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (normalized.Length == 0 || normalized == "{}")
            {
                return [];
            }

            var lines = normalized.Split('\n');
            var result = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                var piece = lines[i];
                if (i == 0 && piece.Trim() == "{")
                {
                    continue;
                }

                if (i == lines.Length - 1 && piece.Trim() == "}")
                {
                    continue;
                }

                // Body from BuildMethodBodyInline is indented 4 spaces relative to `{`; method
                // MultiLine content is already nested under the method brace, so keep as-is when
                // it has the usual indent, else leave unchanged.
                result.Add(piece.StartsWith("    ", StringComparison.Ordinal)
                    ? piece[4..]
                    : piece);
            }

            return result;
        }

        private string BuildHookClosureBody(PhpPropertyHookAst hook, string propertyName, bool isSetHook)
        {
            if (hook.Body is not PhpStatementBlockAst block)
            {
                return "{}";
            }

            var previous = this._hookBackingPropertyName;
            this._hookBackingPropertyName = propertyName;
            try
            {
                // Never compact: PSR-12 forbids content after `{` / before `}` on the same line.
                return this.BuildMethodBodyInline(block, compact: false);
            }
            finally
            {
                this._hookBackingPropertyName = previous;
            }
        }

        private static PhpPropertyHookAst? FindHook(PhpPropertyHookListAst? hooks, string name)
            => hooks?.GetAllNotNull()
                .FirstOrDefault(h => string.Equals(h.Identifier, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// True when the hook was authored with <c>final</c> (the only legal hook modifier).
        /// Polyfill registration passes <c>finalGet</c> / <c>finalSet</c> so
        /// <c>PropertyAccessorObject::register</c> can reject child overrides.
        /// </summary>
        private static bool HookHasFinalModifier(PhpPropertyHookAst? hook)
            => hook?.Modifiers?.Modifiers.Contains(PhpModifier.Final) == true;

        private static string ResolveSetParameterName(PhpPropertyHookAst setHook)
        {
            var first = setHook.Parameters?.GetAllNotNull().FirstOrDefault();
            if (first?.Name is { Length: > 0 } name)
            {
                return name.TrimStart('$');
            }

            return "value";
        }

        /// <summary>
        /// Explicit set-hook parameter type, or null when the hook omits a typed parameter
        /// (PHP defaults the accept type to the property type).
        /// </summary>
        private static ITypeExpression? ResolveSetParameterType(PhpPropertyHookAst setHook)
            => setHook.Parameters?.GetAllNotNull().FirstOrDefault()?.Type;

        /// <summary>
        /// PHP typehint for a polyfill <c>__set_*__tyhpPropertyHook</c> parameter: authored set
        /// type when present, otherwise the property type (PHP's untyped-set default), else
        /// <c>mixed</c>.
        /// </summary>
        private string SpellSetHookParameterType(ITypeExpression? setParamType, ITypeExpression? propertyType)
        {
            var spelled = setParamType != null
                ? this.BuildTypeExpression(setParamType)
                : propertyType != null
                    ? this.BuildTypeExpression(propertyType)
                    : "mixed";
            return string.IsNullOrWhiteSpace(spelled) ? "mixed" : spelled;
        }

        private static bool TryGetSingleReturnOperand(PhpPropertyHookAst hook, out IExpression? operand)
        {
            operand = null;
            if (hook.Body is not PhpStatementBlockAst block)
            {
                return false;
            }

            var stmts = block.GetAllNotNull().ToList();
            if (stmts.Count != 1 || stmts[0] is not PhpUnaryOpAst unary)
            {
                return false;
            }

            if (!IsReturnOperator(unary.Operator))
            {
                return false;
            }

            operand = unary.Operand;
            return operand != null;
        }

        private static bool IsReturnOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 is long token && (int)token == TyhpParser.T_RETURN)
            {
                return true;
            }

            return string.Equals(op.ValueString, "return", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HookListReferencesProperty(PhpPropertyHookListAst? hooks, string propertyName)
        {
            if (hooks is null)
            {
                return false;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                if (hook.Body is IBase2Ast body && AstReferencesThisProperty(body, propertyName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AstReferencesThisProperty(IBase2Ast root, string propertyName)
        {
            var found = false;
            AstWalker.Walk(root, node =>
            {
                if (found)
                {
                    return;
                }

                if (node is PhpDereferenceableAst
                    {
                        Base: PhpVariableAst baseVar,
                        Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst member }
                    }
                    && IsThisVariable(baseVar)
                    && string.Equals(member.ValueString, propertyName, StringComparison.Ordinal))
                {
                    found = true;
                }
            });
            return found;
        }

        /// <summary>
        /// When emitting a hook body, rewrite <c>$this-&gt;prop</c> reads to
        /// <c>$this-&gt;__tyhpPropertyHook-&gt;getBacking('prop', self::class)</c>.
        /// </summary>
        private string? TryBuildHookBackingRead(PhpDereferenceableAst dereferenceable)
        {
            if (this._hookBackingPropertyName is not { } propName)
            {
                return null;
            }

            if (dereferenceable.Base is not PhpVariableAst baseVar
                || !IsThisVariable(baseVar)
                || dereferenceable.Suffix is not PhpInstanceMemberAccessAst
                {
                    MemberName: PhpNameAst member
                } memberAccess)
            {
                return null;
            }

            // Method calls / further suffixes are not plain property reads.
            if (!string.Equals(member.ValueString, propName, StringComparison.Ordinal))
            {
                return null;
            }

            var accessor = memberAccess.Accessor?.ValueString ?? "->";
            if (accessor.Contains('?', StringComparison.Ordinal))
            {
                // Rare in hooks; fall back to normal emit.
                return null;
            }

            return $"$this->__tyhpPropertyHook->getBacking('{EscapePhpSingleQuoted(propName)}', self::class)";
        }

        /// <summary>
        /// When emitting a hook body, rewrite <c>$this-&gt;prop = expr</c> and compound assigns to
        /// <c>$this-&gt;__tyhpPropertyHook-&gt;setBacking('prop', …, self::class)</c>.
        /// </summary>
        private string? TryBuildHookBackingWrite(PhpBinaryOpAst binary)
        {
            if (this._hookBackingPropertyName is not { } propName)
            {
                return null;
            }

            if (!TryGetAssignmentOperatorText(binary.Operator, out var opText))
            {
                return null;
            }

            if (binary.Left is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst baseVar,
                    Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst member }
                }
                || !IsThisVariable(baseVar)
                || !string.Equals(member.ValueString, propName, StringComparison.Ordinal))
            {
                return null;
            }

            var right = this.BuildExpression(binary.Right);
            var escaped = EscapePhpSingleQuoted(propName);
            if (opText == "=")
            {
                return $"$this->__tyhpPropertyHook->setBacking('{escaped}', {right}, self::class)";
            }

            // Compound: $this->prop += rhs → setBacking('prop', getBacking('prop') + rhs, self::class)
            var binaryOp = CompoundAssignToBinaryOp(opText);
            if (binaryOp is null)
            {
                return null;
            }

            return $"$this->__tyhpPropertyHook->setBacking('{escaped}', $this->__tyhpPropertyHook->getBacking('{escaped}', self::class) {binaryOp} {right}, self::class)";
        }

        private static bool TryGetAssignmentOperatorText(TokenValueAst? op, out string opText)
        {
            opText = "";
            if (op is null)
            {
                return false;
            }

            if (WithKeywordHelper.IsSimpleAssignmentOperator(op))
            {
                opText = "=";
                return true;
            }

            if (op.ValueString is { Length: > 0 } text
                && text is "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**="
                    or "&=" or "|=" or "^=" or "<<=" or ">>=")
            {
                opText = text;
                return true;
            }

            if (op.ValueInt64 is long token)
            {
                opText = (int)token switch
                {
                    TyhpParser.T_PLUS_EQUAL => "+=",
                    TyhpParser.T_MINUS_EQUAL => "-=",
                    TyhpParser.T_MUL_EQUAL => "*=",
                    TyhpParser.T_DIV_EQUAL => "/=",
                    TyhpParser.T_CONCAT_EQUAL => ".=",
                    TyhpParser.T_MOD_EQUAL => "%=",
                    TyhpParser.T_POW_EQUAL => "**=",
                    TyhpParser.T_AND_EQUAL => "&=",
                    TyhpParser.T_OR_EQUAL => "|=",
                    TyhpParser.T_XOR_EQUAL => "^=",
                    TyhpParser.T_SL_EQUAL => "<<=",
                    TyhpParser.T_SR_EQUAL => ">>=",
                    _ => "",
                };
                return opText.Length > 0;
            }

            return false;
        }

        private static string? CompoundAssignToBinaryOp(string compoundOp) => compoundOp switch
        {
            "+=" => "+",
            "-=" => "-",
            "*=" => "*",
            "/=" => "/",
            ".=" => ".",
            "%=" => "%",
            "**=" => "**",
            "&=" => "&",
            "|=" => "|",
            "^=" => "^",
            "<<=" => "<<",
            ">>=" => ">>",
            _ => null,
        };

        /// <summary>
        /// Rewrite <c>isset($this-&gt;prop)</c> inside a hook body to <c>tyhpIssetBacking</c>.
        /// </summary>
        private string? TryBuildHookBackingIsset(PhpIssetStatementAst isset)
        {
            if (this._hookBackingPropertyName is not { } propName)
            {
                return null;
            }

            var vars = isset.Variables?.GetAllNotNull().ToList() ?? [];
            if (vars.Count != 1)
            {
                return null;
            }

            if (!IsThisPropertyAccess(vars[0], propName))
            {
                return null;
            }

            return $"$this->__tyhpPropertyHook->issetBacking('{EscapePhpSingleQuoted(propName)}', self::class)";
        }

        /// <summary>
        /// Rewrite <c>unset($this-&gt;prop)</c> inside a hook body to
        /// <c>__tyhpPropertyHook-&gt;unsetBacking</c>.
        /// </summary>
        private string? TryBuildHookBackingUnset(PhpUnsetStatementAst unset)
        {
            if (this._hookBackingPropertyName is not { } propName)
            {
                return null;
            }

            var vars = unset.Variables?.GetAllNotNull().ToList() ?? [];
            if (vars.Count != 1)
            {
                return null;
            }

            if (!IsThisPropertyAccess(vars[0], propName))
            {
                return null;
            }

            return $"$this->__tyhpPropertyHook->unsetBacking('{EscapePhpSingleQuoted(propName)}', self::class);";
        }

        private static bool IsThisPropertyAccess(IExpression expr, string propName)
        {
            return expr is PhpDereferenceableAst
            {
                Base: PhpVariableAst baseVar,
                Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst member }
            }
            && IsThisVariable(baseVar)
            && string.Equals(member.ValueString, propName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Lower <c>parent::$prop::get()</c> / <c>::set($v)</c> to
        /// <c>__tyhpPropertyHook-&gt;parentGet/Set</c> when
        /// polyfilling; leave native syntax when targeting PHP 8.4+.
        /// </summary>
        private string? TryBuildParentPropertyHookAccess(PhpDereferenceableAst dereferenceable)
        {
            if (!this.ShouldLowerPropertyAccessors())
            {
                return null;
            }

            // parent::$prop::get()  or  parent::$prop::set($arg)
            // Structure: Call( ClassConstant( StaticMember( parent, $prop ), get|set ), args )
            if (dereferenceable.Suffix is not PhpCallAst call)
            {
                return null;
            }

            if (dereferenceable.Base is not PhpDereferenceableAst
                {
                    Base: PhpDereferenceableAst
                    {
                        Base: PhpNameAst { ValueString: var className },
                        Suffix: PhpStaticMemberAccessAst { Member: PhpVariableAst propVar }
                    },
                    Suffix: PhpClassConstantAccessAst { Member: PhpNameAst hookNameAst }
                })
            {
                return null;
            }

            if (!string.Equals(className, "parent", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var hookName = hookNameAst.ValueString ?? "";
            var propName = propVar.VariableToken?.ValueString?.TrimStart('$')
                ?? propVar.ValueString?.TrimStart('$')
                ?? "";
            if (propName.Length == 0)
            {
                return null;
            }

            var escaped = EscapePhpSingleQuoted(propName);
            if (string.Equals(hookName, "get", StringComparison.OrdinalIgnoreCase))
            {
                // self::class anchors the ancestor search to *this* level's parent — required so
                // a non-hooked pass-through subclass between here and $host's runtime class does
                // not make the search land back on this very accessor (infinite self-recursion).
                return $"$this->__tyhpPropertyHook->parentGet($this, '{escaped}', self::class)";
            }

            if (string.Equals(hookName, "set", StringComparison.OrdinalIgnoreCase))
            {
                var args = this.FormatArgumentList(call.Arguments);
                return $"$this->__tyhpPropertyHook->parentSet($this, '{escaped}', {args}, self::class)";
            }

            return null;
        }

        /// <summary>
        /// Emit <c>tyhpTry*</c> preamble lines at the start of a class-owned magic method.
        /// </summary>
        private void EmitMagicTryInjectPreambleIfNeeded(PhpMethodDeclAst method, EmitItem methodBlock)
        {
            var name = method.Identifier ?? "";
            if (!this._pendingMagicTryInject.Contains(name))
            {
                return;
            }

            var firstParam = method.Parameters?.GetAllNotNull().FirstOrDefault()?.Name?.TrimStart('$')
                ?? (name switch
                {
                    "__get" or "__isset" or "__unset" => "name",
                    "__set" => "name",
                    _ => "name",
                });
            var firstParamVar = "$" + firstParam;

            switch (name.ToLowerInvariant())
            {
                case "__get":
                    EmitItem.MultiLine(
                        method,
                        EmitType.FunctionStatement,
                        [
                            "$__tyhp_out = null;",
                            $"if ($this->tyhpTryGet({firstParamVar}, $__tyhp_out)) {{",
                            "    return $__tyhp_out;",
                            "}",
                        ],
                        methodBlock);
                    break;
                case "__set":
                {
                    var secondParam = method.Parameters?.GetAllNotNull().ElementAtOrDefault(1)?.Name?.TrimStart('$')
                        ?? "value";
                    EmitItem.MultiLine(
                        method,
                        EmitType.FunctionStatement,
                        [
                            $"if ($this->tyhpTrySet({firstParamVar}, ${secondParam})) {{",
                            "    return;",
                            "}",
                        ],
                        methodBlock);
                    break;
                }
                case "__isset":
                    EmitItem.MultiLine(
                        method,
                        EmitType.FunctionStatement,
                        [
                            "$__tyhp_out = false;",
                            $"if ($this->tyhpTryIsset({firstParamVar}, $__tyhp_out)) {{",
                            "    return $__tyhp_out;",
                            "}",
                        ],
                        methodBlock);
                    break;
                case "__unset":
                    EmitItem.MultiLine(
                        method,
                        EmitType.FunctionStatement,
                        [
                            $"if ($this->tyhpTryUnset({firstParamVar})) {{",
                            "    return;",
                            "}",
                        ],
                        methodBlock);
                    break;
            }
        }

        private static string EscapePhpSingleQuoted(string value)
            => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("'", "\\'", StringComparison.Ordinal);
    }
}
