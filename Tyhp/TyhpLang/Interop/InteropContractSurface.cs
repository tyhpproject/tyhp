namespace Tyhp.TyhpLang.Interop
{
    /// <summary>
    /// Enumerable emitter-required / contract-required runtime symbols (Story 15 Phase 4).
    /// Checked against committed <c>package.tyhpdef</c> surfaces; feeds self-host conformance
    /// when that milestone is green.
    /// </summary>
    public static class InteropContractSurface
    {
        /// <summary>Kind of declaration expected in the package tyhpdef.</summary>
        public enum SymbolKind
        {
            Class,
            Interface,
            Trait,
        }

        /// <summary>One required contract-surface symbol.</summary>
        /// <param name="Package">Composer package name (e.g. <c>tyhp/core</c>).</param>
        /// <param name="FullyQualifiedName">PHP FQN without leading backslash.</param>
        /// <param name="Kind">Class, interface, or trait.</param>
        public sealed record RequiredSymbol(
            string Package,
            string FullyQualifiedName,
            SymbolKind Kind)
        {
            /// <summary>Simple type name (last segment).</summary>
            public string SimpleName
            {
                get
                {
                    var name = FullyQualifiedName;
                    var slash = name.LastIndexOf('\\');
                    return slash < 0 ? name : name[(slash + 1)..];
                }
            }

            /// <summary>Keyword used in tyhpdef / PHP declarations.</summary>
            public string DeclarationKeyword => Kind switch
            {
                SymbolKind.Interface => "interface",
                SymbolKind.Trait => "trait",
                _ => "class",
            };
        }

        /// <summary>
        /// All symbols the interop contract requires to exist in runtime package tyhpdefs.
        /// </summary>
        public static IReadOnlyList<RequiredSymbol> RequiredSymbols { get; } =
        [
            // core
            new("tyhp/core", "Tyhp\\Type", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\NamedType", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\GenericObject", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\PropertyAccessor", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\PropertyAccessorObject", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\ObjectHelper", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\Contracts\\IsDisposable", SymbolKind.Interface),
            new("tyhp/core", "Tyhp\\Contracts\\StringConvertible", SymbolKind.Interface),
            new("tyhp/core", "Tyhp\\Contracts\\BoolConvertible", SymbolKind.Interface),
            new("tyhp/core", "Tyhp\\Contracts\\IntConvertible", SymbolKind.Interface),
            new("tyhp/core", "Tyhp\\Contracts\\FloatConvertible", SymbolKind.Interface),
            new("tyhp/core", "Tyhp\\Contracts\\Convertible", SymbolKind.Interface),
            new("tyhp/core", "Tyhp\\Concerns\\HasGenerics", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\UsesPropertyAccessors", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\HasPropertyAccessors", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\BootsTraits", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\HandlesGet", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\HandlesSet", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\HandlesIsset", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Concerns\\HandlesUnset", SymbolKind.Trait),
            new("tyhp/core", "Tyhp\\Exceptions\\InvalidParametersForOperatorOverloadException", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\Exceptions\\AggregateException", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\Exceptions\\InvalidTypeException", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\Exceptions\\IncompatibleTypeException", SymbolKind.Class),
            new("tyhp/core", "Tyhp\\Exceptions\\PropertyNotFoundException", SymbolKind.Class),

            // decimal
            new("tyhp/decimal", "Tyhp\\Decimal", SymbolKind.Class),
            new("tyhp/decimal", "Tyhp\\Contracts\\DecimalConvertible", SymbolKind.Interface),

            // async
            new("tyhp/async", "Tyhp\\Promise", SymbolKind.Class),
            new("tyhp/async", "Tyhp\\EventLoop", SymbolKind.Class),
            new("tyhp/async", "Tyhp\\CancellationToken", SymbolKind.Class),
            new("tyhp/async", "Tyhp\\CancellationTokenSource", SymbolKind.Class),
            new("tyhp/async", "Tyhp\\DisposableScope", SymbolKind.Class),
            new("tyhp/async", "Tyhp\\Contracts\\AsyncIsDisposable", SymbolKind.Interface),

            // lambda
            new("tyhp/lambda", "Tyhp\\Expression", SymbolKind.Class),
            new("tyhp/lambda", "Tyhp\\PropertyPath", SymbolKind.Class),
            new("tyhp/lambda", "Tyhp\\Expression\\ExpressionNode", SymbolKind.Class),
        ];
    }
}
