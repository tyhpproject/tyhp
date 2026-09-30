namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Intermediate representation of a <c>.tyhpdef</c> file. Reflection harvest, PHP-source harvest, compiled Tyhp library tyhpdef, and Layer 2
    /// serialize this model through <see cref="TyhpdefOutputWriter"/>.
    /// </summary>
    public sealed record TyhpdefFile
    {
        /// <summary>
        /// Extra generation-metadata lines (without comment markers), or a full <c>/** … */</c>
        /// comment to emit instead of the default AUTO-GENERATED header.
        /// </summary>
        public string Header { get; init; } = "";

        /// <summary>File-level attributes such as <c>#[\Tyhp\Php(">=8.3")]</c>. Unused until Phase 8.</summary>
        public List<string> FileAttributes { get; init; } = [];

        /// <summary>Gated <c>declare(php=…)</c> blocks wrapping version-specific declarations.</summary>
        public List<TyhpdefDeclareBlock> DeclareBlocks { get; init; } = [];

        public List<TyhpdefNamespace> Namespaces { get; init; } = [];

        public List<TyhpdefConstant> GlobalConstants { get; init; } = [];

        public List<TyhpdefFunction> GlobalFunctions { get; init; } = [];

        public List<TyhpdefTypeAlias> TypeAliases { get; init; } = [];

        public List<TyhpdefClassDeclaration> GlobalTypes { get; init; } = [];

        /// <summary>
        /// File-top <c>global use</c> / <c>global use extension</c> statements, each a complete
        /// tyhpdef statement including the trailing semicolon.
        /// </summary>
        public List<string> GlobalUses { get; init; } = [];
    }

    /// <summary>
    /// A <c>declare(php=…)</c> region wrapping the same kinds of declarations as a file.
    /// Phase 8 / Story 20.5 populate this; harvest generators leave it empty.
    /// </summary>
    public sealed record TyhpdefDeclareBlock
    {
        /// <summary>Constraint such as <c>php="&gt;=8.3"</c> (the inside of <c>declare(…)</c>).</summary>
        public string Constraint { get; init; } = "";

        public List<TyhpdefNamespace> Namespaces { get; init; } = [];

        public List<TyhpdefConstant> Constants { get; init; } = [];

        public List<TyhpdefFunction> Functions { get; init; } = [];

        public List<TyhpdefTypeAlias> TypeAliases { get; init; } = [];

        public List<TyhpdefClassDeclaration> Classes { get; init; } = [];

        /// <summary>Nested <c>declare</c> blocks, used when a php gate wraps an ext gate.</summary>
        public List<TyhpdefDeclareBlock> Children { get; init; } = [];
    }

    public sealed record TyhpdefNamespace
    {
        public string Name { get; init; } = "";

        public List<TyhpdefClassDeclaration> Classes { get; init; } = [];

        public List<TyhpdefFunction> Functions { get; init; } = [];

        public List<TyhpdefConstant> Constants { get; init; } = [];

        public List<TyhpdefTypeAlias> TypeAliases { get; init; } = [];
    }

    /// <summary>
    /// Generic parameter. The writer emits <c>Name</c>, optional <c>extends Constraint</c>,
    /// and optional <c>= Default</c>.
    /// </summary>
    public sealed record TyhpdefGenericParameter
    {
        public string Name { get; init; } = "";

        public string? Constraint { get; init; }

        public string? Default { get; init; }
    }

    public sealed record TyhpdefAttribute
    {
        public string Name { get; init; } = "";

        public List<string> Arguments { get; init; } = [];
    }

    public sealed record TyhpdefTypeAlias
    {
        public string Name { get; init; } = "";

        /// <summary>
        /// Ordinary alias RHS (<c>int|string</c>). Empty when the RHS is an object shape
        /// (possibly intersected with <see cref="IntersectionTypes"/>).
        /// </summary>
        public string AliasedType { get; init; } = "";

        /// <summary>
        /// Nominal conjuncts for <c>LoggerInterface &amp; object { … }</c>. Empty for a
        /// bare shape or a non-shape alias.
        /// </summary>
        public List<string> IntersectionTypes { get; init; } = [];

        /// <summary>
        /// Harvested or authored <c>object { … }</c> body. Null for ordinary aliases.
        /// </summary>
        public TyhpdefObjectShape? ObjectShape { get; init; }

        public List<TyhpdefGenericParameter> GenericParameters { get; init; } = [];

        public List<string> Modifiers { get; init; } = [];

        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        public string? DocComment { get; set; }

        public bool HasObjectShape =>
            ObjectShape is not null
            && (ObjectShape.Methods.Count > 0
                || ObjectShape.Properties.Count > 0
                || ObjectShape.Constants.Count > 0);

        /// <summary>Type spellings whose names the extern / qualify passes must see.</summary>
        public IEnumerable<string> EnumerateReferencedTypes()
        {
            if (!string.IsNullOrWhiteSpace(AliasedType))
            {
                yield return AliasedType;
            }

            foreach (var parent in IntersectionTypes ?? [])
            {
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    yield return parent;
                }
            }

            if (ObjectShape is null)
            {
                yield break;
            }

            foreach (var method in ObjectShape.Methods ?? [])
            {
                if (!string.IsNullOrWhiteSpace(method.ReturnType))
                {
                    yield return method.ReturnType;
                }

                foreach (var parameter in method.Parameters ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(parameter.Type))
                    {
                        yield return parameter.Type;
                    }
                }

                foreach (var generic in method.GenericParameters ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(generic.Constraint))
                    {
                        yield return generic.Constraint;
                    }
                }
            }

            foreach (var property in ObjectShape.Properties ?? [])
            {
                if (!string.IsNullOrWhiteSpace(property.Type))
                {
                    yield return property.Type;
                }
            }

            foreach (var constant in ObjectShape.Constants ?? [])
            {
                if (!string.IsNullOrWhiteSpace(constant.Type))
                {
                    yield return constant.Type;
                }
            }
        }
    }

    /// <summary>
    /// Public instance members of an object-shape alias RHS, plus <c>__construct</c>
    /// as a constructability signature.
    /// </summary>
    public sealed record TyhpdefObjectShape
    {
        public List<TyhpdefMethod> Methods { get; init; } = [];

        public List<TyhpdefProperty> Properties { get; init; } = [];

        public List<TyhpdefConstant> Constants { get; init; } = [];

        public bool HasMembers =>
            Methods.Count > 0 || Properties.Count > 0 || Constants.Count > 0;
    }

    /// <summary>
    /// A class, interface, trait, enum, struct, or standalone <c>extension Name { }</c>.
    /// </summary>
    public sealed record TyhpdefClassDeclaration
    {
        /// <summary><c>class</c>, <c>interface</c>, <c>trait</c>, <c>enum</c>, <c>struct</c>, or <c>extension</c>.</summary>
        public string Kind { get; init; } = "class";

        /// <summary>PHP name for <c>class</c>; Tyhp name for <c>extension</c>.</summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// Tyhp name for <c>class PhpName as TyhpName</c>. Compiled library tyhpdef uses this for
        /// <c>__tyhpExtensionBacker</c>.
        /// </summary>
        public string? AsAlias { get; init; }

        public bool IsPartial { get; init; }

        /// <summary>
        /// Overlay header-only form: <c>partial class Foo&lt;T&gt;;</c> (no braces).
        /// Writer emits <c>;</c> instead of <c>{ … }</c>. Binder replaces written
        /// header clauses and keeps unlisted members.
        /// </summary>
        public bool IsHeaderOnly { get; init; }

        /// <summary>
        /// Name-only placeholder: <c>extern class|interface|enum Name;</c>
        /// or kind-unspecified <c>extern \Name;</c> when <see cref="Kind"/> is
        /// empty or <c>extern</c>. PHP-source harvest writes these to
        /// <c>_tyhpdef/externs.tyhpdef</c>; compiled library tyhpdef writes author-only placeholders
        /// into <c>package.tyhpdef</c>. Generator-owned placeholders carry
        /// <see cref="ProvidedBy"/>.
        /// </summary>
        public bool IsExtern { get; init; }

        /// <summary>
        /// Wrapper package for generator-owned <c>extern</c>
        /// (<c>// @provided-by: tyhpdef/…</c>). Null when omitted.
        /// </summary>
        public string? ProvidedBy { get; init; }

        public List<string> Modifiers { get; init; } = [];

        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        /// <summary>
        /// Parent type for <c>class</c> / <c>interface</c> / <c>struct</c>. For a standalone
        /// <c>extension</c>, the header target (<c>extension Name extends Type</c>). Null when
        /// the extension uses nested <see cref="ExtensionGroups"/> instead of a header target.
        /// </summary>
        public string? Extends { get; init; }

        public List<string> Implements { get; init; } = [];

        /// <summary>Trait <c>use</c> names inside the body.</summary>
        public List<string> Uses { get; init; } = [];

        public List<TyhpdefGenericParameter> GenericParameters { get; init; } = [];

        /// <summary>Enum backing type, e.g. <c>string</c> or <c>int</c>.</summary>
        public string? BackingType { get; init; }

        public List<TyhpdefConstant> Constants { get; init; } = [];

        public List<TyhpdefProperty> Properties { get; init; } = [];

        public List<TyhpdefMethod> Methods { get; init; } = [];

        /// <summary>Class-body <c>operator</c> overloads (signature form in tyhpdef).</summary>
        public List<TyhpdefMethod> Operators { get; init; } = [];

        /// <summary>
        /// Class-body <c>extension function</c> / <c>fn</c> / <c>operator</c>, or members of a
        /// standalone tyhpdef <c>extension Name { }</c> that sit directly in the extension
        /// (header target). Nested <c>extends Type { }</c> groups live in
        /// <see cref="ExtensionGroups"/>.
        /// </summary>
        public List<TyhpdefExtensionMember> ExtensionMembers { get; init; } = [];

        /// <summary>
        /// Nested <c>extends Type { }</c> / <c>extends&lt;T&gt; Type { }</c> groups of a
        /// standalone extension. Empty when the target is the header <see cref="Extends"/>.
        /// </summary>
        public List<TyhpdefExtensionGroup> ExtensionGroups { get; init; } = [];

        public List<TyhpdefTypeAlias> TypeAliases { get; init; } = [];

        public List<TyhpdefEnumCase> EnumCases { get; init; } = [];

        public string? DocComment { get; set; }

        public bool IsDeprecated { get; init; }

        public bool IsObsolete { get; init; }

        /// <summary>Member PHP gate such as <c>&gt;=8.4</c>. Emitted as <c>#[\Tyhp\Php]</c>.</summary>
        public string? PhpGate { get; init; }
    }

    public record TyhpdefMethod
    {
        public string Name { get; init; } = "";

        public List<string> Modifiers { get; init; } = [];

        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        public List<TyhpdefParameter> Parameters { get; init; } = [];

        public string ReturnType { get; init; } = "";

        public List<TyhpdefGenericParameter> GenericParameters { get; init; } = [];

        public List<TyhpdefMethod> Overloads { get; init; } = [];

        public string? DocComment { get; set; }

        public bool IsDeprecated { get; init; }

        public bool IsObsolete { get; init; }

        public bool ReturnsReference { get; init; }

        public bool IsAsync { get; init; }

        /// <summary>Member PHP gate such as <c>&gt;=8.4</c>. Emitted as <c>#[\Tyhp\Php]</c>.</summary>
        public string? PhpGate { get; init; }
    }

    /// <summary>
    /// Standalone function. Same as <see cref="TyhpdefMethod"/> plus the tyhpdef
    /// <c>function name(extends T $this, …)</c> form.
    /// </summary>
    public sealed record TyhpdefFunction : TyhpdefMethod
    {
        /// <summary>When true, emit <c>(extends …)</c> after the opening parenthesis.</summary>
        public bool IsExtension { get; init; }

        /// <summary>
        /// PHP <c>if (!function_exists(...))</c> gate. Emitted as <c>fallback function</c>.
        /// </summary>
        public bool IsFallback { get; init; }

        /// <summary>
        /// Name-only compiled-library / hand placeholder: <c>extern function \Name;</c>.
        /// </summary>
        public bool IsExtern { get; init; }

        /// <summary>
        /// Wrapper package for generator-owned <c>extern function</c>
        /// (<c>// @provided-by: tyhpdef/…</c>). Null when omitted.
        /// </summary>
        public string? ProvidedBy { get; init; }
    }

    /// <summary>
    /// Inline class-body extension member, or a member of standalone tyhpdef
    /// <c>extension Name { }</c> (short <c>=&gt;</c> form only).
    /// </summary>
    public sealed record TyhpdefExtensionMember
    {
        /// <summary>
        /// <c>fn</c> or <c>operator</c>. Class-body and standalone tyhpdef extension members
        /// are thin <c>=&gt;</c> mappings; the writer emits <c>fn</c> for any non-<c>operator</c>
        /// kind.
        /// </summary>
        public string Kind { get; init; } = "function";

        public string Name { get; init; } = "";

        /// <summary>
        /// Not emitted on standalone extension operators. The target is the extension header
        /// or nested group. Class-body inline operators leave this null.
        /// </summary>
        public string? OperatorTarget { get; init; }

        public List<TyhpdefParameter> Parameters { get; init; } = [];

        public string ReturnType { get; init; } = "";

        public List<TyhpdefGenericParameter> GenericParameters { get; init; } = [];

        /// <summary>
        /// Thin mapping expression (emitted as <c>=&gt; expr</c>).
        /// </summary>
        public string? Body { get; init; }

        public string? DocComment { get; set; }

        public bool IsDeprecated { get; init; }

        public bool IsObsolete { get; init; }

        /// <summary>
        /// Optional attributes on the member. Compiled library tyhpdef does not write
        /// <c>#[\\Tyhp\\Optimize\\Inline]</c> — that attribute on an extension member is
        /// error 4176. Owned-type operators map with a thin <c>=&gt;</c> expression onto
        /// the compiled PHP method (<c>self::__add(…)</c>) and stay spliced by form.
        /// </summary>
        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        /// <summary>
        /// When true, the standalone member annotates a by-ref receiver (<c>&amp;$this</c>).
        /// The receiver is not a parameter; the target type comes from the block.
        /// </summary>
        public bool ByRefReceiver { get; init; }

        /// <summary>
        /// Not emitted on standalone extension members. Top-level
        /// <see cref="TyhpdefFunction.IsExtension"/> still marks a free function whose
        /// first parameter is a receiver.
        /// </summary>
        public bool IsExtension { get; init; }
    }

    /// <summary>
    /// One nested <c>extends Type { }</c> group inside a standalone extension.
    /// </summary>
    public sealed record TyhpdefExtensionGroup
    {
        public string TargetType { get; init; } = "";

        public List<TyhpdefGenericParameter> GenericParameters { get; init; } = [];

        public List<TyhpdefExtensionMember> Members { get; init; } = [];
    }

    /// <summary>
    /// One bodyless tyhpdef property hook (<c>get;</c> / <c>set;</c> / <c>&amp;get;</c>).
    /// </summary>
    public sealed record TyhpdefPropertyHook
    {
        /// <summary><c>get</c> or <c>set</c>.</summary>
        public string Name { get; init; } = "";

        /// <summary>When true, emit <c>&amp;get</c>. Illegal on <c>set</c> (checker).</summary>
        public bool ReturnsRef { get; init; }

        /// <summary>Hook modifiers such as <c>final</c> or hook visibility (<c>private</c>).</summary>
        public List<string> Modifiers { get; init; } = [];

        /// <summary>Attributes emitted immediately before the hook name.</summary>
        public List<TyhpdefAttribute> Attributes { get; init; } = [];
    }

    public sealed record TyhpdefProperty
    {
        public string Name { get; init; } = "";

        public string Type { get; init; } = "";

        public List<string> Modifiers { get; init; } = [];

        /// <summary>
        /// Property attributes. <c>#[\Tyhp\Php]</c> gates are also stored on
        /// <see cref="PhpGate"/> and emitted even when this list is empty.
        /// </summary>
        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        /// <summary>
        /// Bodyless hook list. Empty (the default) emits storage form
        /// <c>Type $name;</c>. Non-empty emits
        /// <c>Type $name { get; set; }</c> / <c>{ &amp;get; }</c>. Not emitted on structs.
        /// </summary>
        public List<TyhpdefPropertyHook> Hooks { get; init; } = [];

        /// <summary>
        /// Expected PHP start value, emitted as <c>?? &lt;value&gt;</c> (same form as
        /// tyhpdef consts). Tyhpdef does not define the live value. Null omits the clause.
        /// Not emitted on structs (those use real <c>=</c> defaults via <c>tyhpStructProperty</c>).
        /// </summary>
        public string? CoalesceValue { get; init; }

        public string? DocComment { get; set; }

        public bool IsDeprecated { get; init; }

        /// <summary>Member PHP gate such as <c>&gt;=8.4</c>. Emitted as <c>#[\Tyhp\Php]</c>.</summary>
        public string? PhpGate { get; init; }
    }

    public sealed record TyhpdefConstant
    {
        public string Name { get; init; } = "";

        public string Type { get; init; } = "";

        public string? Value { get; init; }

        /// <summary>When true, emit the import-const <c>?? value</c> form instead of <c>= value</c>.</summary>
        public bool UsesCoalesce { get; init; }

        public List<string> Modifiers { get; init; } = [];

        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        public string? DocComment { get; set; }

        public bool IsDeprecated { get; init; }

        /// <summary>PHP <c>if (!defined(...))</c> gate. Emitted as <c>fallback const</c>.</summary>
        public bool IsFallback { get; init; }

        /// <summary>
        /// Name-only placeholder: <c>extern const \Name;</c>.
        /// </summary>
        public bool IsExtern { get; init; }

        /// <summary>
        /// Wrapper package for generator-owned <c>extern const</c>
        /// (<c>// @provided-by: tyhpdef/…</c>). Null when omitted.
        /// </summary>
        public string? ProvidedBy { get; init; }

        /// <summary>Member PHP gate such as <c>&lt;8.4</c>. Emitted as <c>#[\Tyhp\Php]</c>.</summary>
        public string? PhpGate { get; init; }
    }

    public sealed record TyhpdefParameter
    {
        public string Name { get; init; } = "";

        public string Type { get; init; } = "";

        public string? DefaultValue { get; init; }

        public bool IsVariadic { get; init; }

        public bool IsByReference { get; init; }

        /// <summary>
        /// Constructor-promoted parameter. The writer emits the parameter as a normal argument;
        /// generators also create a matching <see cref="TyhpdefProperty"/>.
        /// </summary>
        public bool IsPromoted { get; init; }

        public List<TyhpdefAttribute> Attributes { get; init; } = [];
    }

    public sealed record TyhpdefEnumCase
    {
        public string Name { get; init; } = "";

        public string? BackingValue { get; init; }

        public string? DocComment { get; set; }

        public List<TyhpdefAttribute> Attributes { get; init; } = [];

        /// <summary>Member PHP gate such as <c>&gt;=8.4</c>. Emitted as <c>#[\Tyhp\Php]</c>.</summary>
        public string? PhpGate { get; init; }
    }

    /// <summary>Options for <see cref="TyhpdefOutputWriter"/>.</summary>
    public sealed record TyhpdefOutputOptions
    {
        public static TyhpdefOutputOptions Default { get; } = new();

        /// <summary>Emit <c>/** … */</c> when declarations have <c>DocComment</c>. Default true.</summary>
        public bool IncludeDocComments { get; init; } = true;
    }
}
