using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using System;
using System.Collections.Generic;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectDeclarationSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol
    {
        // Lazy initialization via ??= is not thread-safe; the binder is assumed to be single-threaded.
        private List<GenericTypeParameterSymbol>? _genericParameters;
        private List<ITypeExpression>? _implementsTypes;
        private Dictionary<string, IBaseSymbol>? _members;
        private Dictionary<string, IBaseSymbol>? _constants;
        private List<ObjectOperatorOverloadMethodSymbol>? _extensionContributedOperators;

        public ObjectDeclarationSymbol(string name, IBase2Ast? declaringNode = null, string sourceFile = "", MemberModifier visibility = MemberModifier.None)
            : base(name, SymbolType.ObjectTypeDeclaration, declaringNode, sourceFile, visibility)
        {
            this.ObjectKind = PhpTypeDeclType.Class;
        }

        public PhpTypeDeclType ObjectKind { get; internal set; }

        public bool IsStruct { get; internal set; }

        public bool IsExtension { get; internal set; }

        /// <summary>Compiler-generated symbol (e.g. synthetic inline extension class for tyhpdef).</summary>
        public bool IsCompilerGenerated { get; internal set; }

        /// <summary>For a synthetic inline extension class, the tyhpdef class whose <c>self</c>/<c>$this</c> should bind there.</summary>
        public ObjectDeclarationSymbol? InlineExtensionReceiverClass { get; internal set; }

        /// <summary>Synthetic extension class holding tyhpdef <c>extension function</c> / inline <c>extension operator</c> members.</summary>
        public ObjectDeclarationSymbol? SyntheticInlineExtension { get; internal set; }

        /// <summary>
        /// Operator overload symbols contributed by extensions (standalone or synthetic) for this type.
        /// </summary>
        public List<ObjectOperatorOverloadMethodSymbol> ExtensionContributedOperators
        {
            get => this._extensionContributedOperators ??= new List<ObjectOperatorOverloadMethodSymbol>();
            internal set => this._extensionContributedOperators = value;
        }

        /// <summary>
        /// Namespace paths from tyhpdef <c>use extension</c>, resolved during the resolution pass into
        /// <see cref="TyhpdefAutoActivatedExtensions"/>.
        /// </summary>
        public List<string>? PendingTyhpdefUseExtensionNamespaces { get; internal set; }

        /// <summary>
        /// Extension declarations activated for this type via tyhpdef <c>use extension</c> (non-empty ⇒ lookup is restricted to these).
        /// </summary>
        public List<ObjectDeclarationSymbol>? TyhpdefAutoActivatedExtensions { get; internal set; }

        /// <summary>Trait-like <c>insteadof</c> rules for <c>use extension</c> (method name → preferred extension name).</summary>
        public Dictionary<string, string>? ExtensionUseMethodPrecedence { get; set; }

        /// <summary>Trait-like <c>as</c> aliases for <c>use extension</c>.</summary>
        public Dictionary<string, (string? ExtName, string OriginalMethod)>? ExtensionUseMethodAliases { get; set; }

        public List<GenericTypeParameterSymbol> GenericParameters
        {
            get => this._genericParameters ??= new List<GenericTypeParameterSymbol>();
            internal set => this._genericParameters = value;
        }

        public ITypeExpression? ExtendsType { get; internal set; }

        public List<ITypeExpression> ImplementsTypes
        {
            get => this._implementsTypes ??= new List<ITypeExpression>();
            internal set => this._implementsTypes = value;
        }

        /// <summary>
        /// Trait insteadof rules: maps method name to the fully-qualified trait name whose implementation should be preferred.
        /// </summary>
        public Dictionary<string, string>? TraitMethodPrecedence { get; set; }

        /// <summary>
        /// Trait method aliases created via 'as' adaptation rules.
        /// Each entry maps an alias name to the original (traitName, methodName) pair.
        /// </summary>
        public Dictionary<string, (string? TraitName, string OriginalMethod)>? TraitMethodAliases { get; set; }

        /// <summary>
        /// Fast lookup for methods, properties, and object type aliases.
        /// Method names are case-insensitive (PHP); property keys keep their <c>$</c> prefix so they
        /// do not collide with methods. Class constants live in <see cref="Constants"/>.
        /// </summary>
        public Dictionary<string, IBaseSymbol> Members
        {
            get => this._members ??= new Dictionary<string, IBaseSymbol>(ObjectDeclarationMemberNamePolicy.MemberNameComparer);
            internal set => this._members = value;
        }

        /// <summary>
        /// Checker-computed (Prop-init #7 / Top-type #9): names of <em>inherited</em> instance
        /// properties that this class's constructor definitely initializes. Used so a subclass
        /// constructor that assigns <c>$this-&gt;inherited</c> is credited for subsequent instance
        /// methods without mutating the base property's shared
        /// <see cref="ObjectPropertySymbol.MayBeUninitializedAfterConstruction"/>.
        /// </summary>
        public HashSet<string>? InheritedPropertiesInitializedByConstruction { get; set; }

        /// <summary>
        /// Fast lookup for class constants and enum cases. Names are case-sensitive (PHP), and this
        /// namespace is independent of <see cref="Members"/> so <c>const TAG</c> and <c>tag()</c> coexist.
        /// </summary>
        public Dictionary<string, IBaseSymbol> Constants
        {
            get => this._constants ??= new Dictionary<string, IBaseSymbol>(ObjectDeclarationMemberNamePolicy.ConstantNameComparer);
            internal set => this._constants = value;
        }

        /// <summary>
        /// Enumerates methods/properties/aliases then class constants / enum cases.
        /// </summary>
        public IEnumerable<IBaseSymbol> EnumerateMembersAndConstants()
        {
            if (this._members != null)
            {
                foreach (var member in this._members.Values)
                {
                    yield return member;
                }
            }

            if (this._constants != null)
            {
                foreach (var constant in this._constants.Values)
                {
                    yield return constant;
                }
            }
        }

        /// <summary>
        /// Looks up a class constant / enum case by exact (case-sensitive) name.
        /// </summary>
        public bool TryGetConstant(string name, out IBaseSymbol constant)
        {
            if (this._constants != null && this._constants.TryGetValue(name, out constant!))
            {
                return true;
            }

            constant = null!;
            return false;
        }
    }
}