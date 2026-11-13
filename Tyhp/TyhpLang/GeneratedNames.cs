namespace Tyhp.TyhpLang
{
    /// <summary>
    /// Names the compiler generates into emitted PHP. They live here rather than in the emitter
    /// because the checker has to reserve them: user code declaring a colliding name would be
    /// overwritten by, or silently shadow, a generated symbol.
    /// </summary>
    public static class GeneratedNames
    {
        /// <summary>
        /// Suffix on Mechanism C class bag / factory names, on a layout-1 callable binder, and on
        /// the companion a later <c>#[\Tyhp\InferGeneric]</c> method keeps beside its plain method.
        /// A layout-2 callable is the binder under its declared name and does not use this suffix.
        /// The <c>__tyhpGeneric</c> spelling is shared ABI legacy — do not rename casually.
        /// See <see cref="Emitter.TyhpEmitter"/>.
        /// </summary>
        public const string GenericVariantSuffix = "__tyhpGeneric";

        /// <summary>
        /// Prefix on the hidden parameter of a layout-1 binder (<c>$__generic_T</c>). Layout-2
        /// binder parameters are named after the type parameter (<c>$T</c>).
        /// </summary>
        public const string GenericVariantParameterPrefix = "__generic_";

        /// <summary>
        /// True when <paramref name="name"/> collides with the generic variant suffix. PHP matches
        /// function and method names case-insensitively, so a declaration of <c>zero__TYHPGENERIC</c>
        /// would collide with the variant emitted for <c>zero</c>.
        /// </summary>
        public static bool EndsWithGenericVariantSuffix(string? name) =>
            name is not null
            && name.Length > GenericVariantSuffix.Length
            && name.EndsWith(GenericVariantSuffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The generic initialization hook every level of a generic inheritance chain declares
        /// (Mechanism C). Unlike the other generated members this name is <em>uniform</em> across
        /// levels rather than qualified by class: it is overridden and chained with <c>parent::</c>,
        /// which only works if every level spells it identically.
        /// </summary>
        public const string GenericInitHook = "__initGenerics" + GenericVariantSuffix;

        /// <summary>
        /// Creates <c>$__tyhpGeneric</c> on a <c>readonly class</c>. Declared on the topmost generic
        /// level — the property's declaring scope, which is the only place a readonly property may be
        /// initialized — and called from every level in the chain, including after
        /// <c>newInstanceWithoutConstructor()</c>. The name ends in <see cref="GenericVariantSuffix"/>
        /// so a user method of the same name is rejected with the other reserved generic names.
        /// </summary>
        public const string GenericBagBoot = "__bootGenericBag" + GenericVariantSuffix;

        /// <summary>
        /// Cached <c>ReflectionClass</c> backing the generic instantiation factory. Uncached, a
        /// reflection allocation per instantiation is a measurable regression against plain
        /// <c>new</c>.
        /// </summary>
        public const string ReflectedClassField = "__reflectedClass" + GenericVariantSuffix;

        /// <summary>
        /// Factory that binds type arguments before running the author's constructor, named
        /// <c>new_&lt;MangledFqn&gt;__tyhpGeneric</c>. The fully qualified name is embedded because a
        /// short name collides when two classes in one chain live in different namespaces: PHP rejects
        /// the second declaration with "Cannot override final method".
        /// </summary>
        public static string GenericFactory(string fullyQualifiedName) =>
            "new_" + MangleFullyQualifiedName(fullyQualifiedName) + GenericVariantSuffix;

        /// <summary>
        /// Lazy-ghost factory for <c>lazy new</c> of a Mechanism C class, named
        /// <c>new_&lt;MangledFqn&gt;__tyhpLazy</c>. Emitted only when every version at or above
        /// <c>output.phpVersion</c> is at least 8.4 and the class has a lazy-capable instance
        /// property. The <see cref="GenericFactory"/> name stays the eager factory and is the one
        /// stamped on <c>#[\Tyhp\GenericRuntime]</c>.
        /// </summary>
        public static string GenericLazyFactory(string fullyQualifiedName) =>
            "new_" + MangleFullyQualifiedName(fullyQualifiedName) + "__tyhpLazy";

        /// <summary>
        /// Flattens a namespace-qualified name into a single PHP identifier segment.
        /// </summary>
        public static string MangleFullyQualifiedName(string? fullyQualifiedName) =>
            (fullyQualifiedName ?? "").TrimStart('\\').Replace('\\', '_');

        /// <summary>
        /// Suffix on polyfill property-hook get/set methods and the uniform init hook
        /// (<c>__get_&lt;prop&gt;__tyhpPropertyHook</c> / <c>__set_&lt;prop&gt;__tyhpPropertyHook</c> /
        /// <c>__initPropertyHooks__tyhpPropertyHook</c>).
        /// </summary>
        public const string PropertyHookMethodSuffix = "__tyhpPropertyHook";

        /// <summary>
        /// Uniform property-hook polyfill initialization hook every level of a hooked inheritance
        /// chain declares (Mechanism C–style). Overridden and chained with <c>parent::</c>, so every
        /// level spells it identically. Independent of constructor chaining — a child that never
        /// calls <c>parent::__construct</c> still registers ancestor accessors when its ctor
        /// prologue invokes this hook.
        /// </summary>
        public const string PropertyHookInitHook = "__initPropertyHooks" + PropertyHookMethodSuffix;

        /// <summary>
        /// True when <paramref name="name"/> collides with the property-hook method suffix.
        /// </summary>
        public static bool EndsWithPropertyHookMethodSuffix(string? name) =>
            name is not null
            && name.Length > PropertyHookMethodSuffix.Length
            && name.EndsWith(PropertyHookMethodSuffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Polyfill get hook method for a lowered property: <c>__get_&lt;prop&gt;__tyhpPropertyHook</c>.
        /// </summary>
        public static string PropertyHookGetMethod(string propertyName) =>
            "__get_" + propertyName + PropertyHookMethodSuffix;

        /// <summary>
        /// Polyfill set hook method for a lowered property: <c>__set_&lt;prop&gt;__tyhpPropertyHook</c>.
        /// </summary>
        public static string PropertyHookSetMethod(string propertyName) =>
            "__set_" + propertyName + PropertyHookMethodSuffix;

        /// <summary>
        /// Emitted name for an extension-method (or static operator-form) receiver/operand that the
        /// author spelled as <c>$this</c>. PHP rejects <c>$this</c> as a parameter of a static
        /// method; the emitter renames the parameter and rewrites body references to this alias.
        /// </summary>
        public const string ExtensionReceiverThisAlias = "$this_";

        /// <summary>
        /// Tyhp-only alias suffix on the compiled PHP backer class for a standalone
        /// <c>extension Name { }</c> (<c>class PhpName as Name__tyhpExtensionBacker</c>).
        /// Never appears in emitted PHP. Same family as <see cref="GenericVariantSuffix"/>.
        /// </summary>
        public const string ExtensionBackerSuffix = "__tyhpExtensionBacker";

        /// <summary>
        /// True when <paramref name="name"/> is or ends with the extension-backer alias suffix,
        /// which would collide with compiled-library tyhpdef's generated Tyhp name for a standalone extension.
        /// PHP method/class matching is case-insensitive.
        /// </summary>
        public static bool EndsWithExtensionBackerSuffix(string? name) =>
            name is not null
            && name.Length >= ExtensionBackerSuffix.Length
            && name.EndsWith(ExtensionBackerSuffix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Static method emitted for <c>operator default(): self</c>. The runtime hook calls
        /// this name when a generic <c>default(T)</c> is bound to that class. PHP method names
        /// are case-insensitive, so any capitalization collides.
        /// </summary>
        public const string OperatorDefaultMethod = "__tyhpDefault";

        /// <summary>
        /// True when <paramref name="name"/> is the reserved <c>operator default</c> method,
        /// compared case-insensitively.
        /// </summary>
        public static bool IsOperatorDefaultMethod(string? name) =>
            name is not null
            && name.Equals(OperatorDefaultMethod, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Prefix on call-site temps for a repeated splice parameter or receiver
        /// (<c>$__tyhpInlineTemp1</c>, <c>$__tyhpInlineTemp2</c>, …). Unlike
        /// <see cref="ExtensionBackerSuffix"/>, this name appears in emitted PHP.
        /// </summary>
        public const string InlineTempVariablePrefix = "__tyhpInlineTemp";

        /// <summary>
        /// True when <paramref name="name"/> (with or without a leading <c>$</c>) is or starts
        /// with the inline-temp prefix, which would collide with a generated splice hoist.
        /// PHP variables are case-insensitive.
        /// </summary>
        public static bool StartsWithInlineTempPrefix(string? name)
        {
            if (name is null)
            {
                return false;
            }

            var bare = name.StartsWith('$') ? name[1..] : name;
            return bare.StartsWith(InlineTempVariablePrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
