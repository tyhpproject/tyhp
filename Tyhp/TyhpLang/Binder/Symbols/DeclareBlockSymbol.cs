using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class DeclareBlockSymbol :
        CodeBlockSymbol
    {
        public Dictionary<string, string> Directives { get; protected set; }

        /// <summary>
        /// The <c>php</c> constraint on this declare, when present and the directive was alone.
        /// </summary>
        public string? PhpVersionConstraint { get; set; }

        /// <summary>
        /// The <c>ext</c> spec (<c>name</c> or <c>!name</c>) on this declare, when present and the
        /// directive was alone. Enclosing blocks' specs are in <see cref="BaseSymbol.EffectiveExtGates"/>.
        /// </summary>
        public string? ExtGate { get; set; }

        /// <summary>
        /// True when this block's <c>php</c> gate is unsatisfied or could not be evaluated.
        /// Inner declarations are not registered.
        /// </summary>
        public bool IsPhpVersionGateInactive { get; set; }

        /// <summary>
        /// False when the <c>php</c> constraint string is not valid Composer syntax (checker 4300).
        /// </summary>
        public bool IsPhpVersionConstraintValid { get; set; } = true;

        public DeclareBlockSymbol(
            string name,
            string? sourceFile = null
        )
            : base(name, blockType: ScopeType.DeclareBlock, sourceFile: sourceFile ?? string.Empty)
        {
            this.Directives = new Dictionary<string, string>();
        }
    }
}