using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Ast.Interfaces;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class ObjectOperatorOverloadMethodSymbol :
        ObjectMethodSymbol
    {
        public OverloadableOperator Operator { get; protected set; }

        /// <summary>
        /// True for <c>operator +&lt;T&gt;</c> in a standalone extension block OR tyhpdef inline <c>extension operator</c>.
        /// This is broader than <see cref="TyhpOperatorOverloadAst.IsExtensionOperator"/> on the AST,
        /// which is only true for the standalone <c>&lt;Type&gt;</c> syntax.
        /// </summary>
        public bool IsExtensionOperator { get; internal set; }

        /// <summary>
        /// True when this overload is a bodyless tyhpdef <c>operator …;</c> (no <c>extension</c>
        /// keyword). The underlying PHP type already supports the operator natively (engine / PECL),
        /// so the emitter must leave <c>$a + $b</c> as the PHP operator — no rewrite to <c>__add</c>.
        /// Bodied <c>extension operator</c> forms are never passthrough (they map/rewrite).
        /// </summary>
        public bool IsNativePassthrough { get; internal set; }

        /// <summary>AST target type for extension operators; cleared after the resolution pass.</summary>
        public ITypeExpression? PendingExtensionTargetType { get; internal set; }

        /// <summary>
        /// Resolved type that this extension operator applies to — an
        /// <see cref="ObjectDeclarationSymbol"/> (class/interface/…) or a
        /// <see cref="BuiltInTypeSymbol"/> (e.g. <c>string</c>, <c>int</c>).
        /// </summary>
        public IBaseSymbol? ExtensionTargetSymbol { get; internal set; }

        /// <summary>The extension declaration symbol that contains this operator (standalone or synthetic).</summary>
        public ObjectDeclarationSymbol? DeclaringExtensionSymbol { get; internal set; }

        public override bool CanBeInstance => false;
        
        public ObjectOperatorOverloadMethodSymbol(
            string name,
            OverloadableOperator overloadOperator,
            string? sourceFile = null
        )
            : base(name, sourceFile: sourceFile ?? string.Empty, symbolType: SymbolType.ObjectOperatorOverload)
        {
            this.Operator = overloadOperator;
        }
    }
}