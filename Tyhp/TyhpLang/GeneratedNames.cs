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
        /// Suffix on the generic variant a callable is emitted alongside when it needs its own generic
        /// parameters at runtime (Mechanism D binder; also Mechanism C class bag / factory names).
        /// The <c>__tyhpGeneric</c> spelling is shared ABI legacy — do not rename casually.
        /// See <see cref="Emitter.TyhpEmitter"/>.
        /// </summary>
        public const string GenericVariantSuffix = "__tyhpGeneric";

        /// <summary>
        /// Prefix on the hidden parameter carrying one bound type argument into a generic variant.
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
    }
}
