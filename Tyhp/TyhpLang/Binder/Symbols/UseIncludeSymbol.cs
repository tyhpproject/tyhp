using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using System;

namespace Tyhp.TyhpLang.Binder.Symbols {
    public class UseIncludeSymbol :
        BaseSymbol,
        INamespaceBlockScopeSymbol,
        ICodeBlockScopeSymbol
    {
        public string ImportedName { get; protected set; }

        public string? AliasName { get; protected set; }

        public PhpUseType UseType { get; protected set; }

        /// <summary>
        /// Pre-split namespace segments of ImportedName (without leading backslash).
        /// Avoids repeated TrimStart/Split allocations during resolution.
        /// </summary>
        public string[] ImportedNameSegments { get; }

        /// <summary>
        /// Creates a use/import symbol with source-metadata only.
        /// </summary>
        /// <param name="name">
        /// Declared alias name.
        /// Accepted shape: [A-Za-z_][A-Za-z0-9_]*.
        /// </param>
        /// <param name="importedName">
        /// Fully qualified imported symbol name.
        /// Accepted shape examples:
        /// A\B\C, \A\B\C, A.
        /// Each namespace segment must follow [A-Za-z_][A-Za-z0-9_]*.
        /// </param>
        /// <param name="aliasName">
        /// Optional alias for the imported symbol.
        /// If supplied, must match [A-Za-z_][A-Za-z0-9_]*.
        /// </param>
        /// <param name="useType">Kind of use target.</param>
        /// <param name="sourceFile">Source filename for the declaration.</param>
        public UseIncludeSymbol(
            string name,
            string importedName,
            string? aliasName = null,
            PhpUseType useType = PhpUseType.Function,
            string? sourceFile = null
        )
            : this(
                name,
                importedName,
                null,
                sourceFile: sourceFile,
                aliasName: aliasName,
                useType: useType
            )
        {
        }

        /// <summary>
        /// Creates a use/import symbol with AST metadata and visibility.
        /// </summary>
        /// <param name="name">
        /// Declared alias name.
        /// Accepted shape: [A-Za-z_][A-Za-z0-9_]*.
        /// </param>
        /// <param name="importedName">
        /// Fully qualified name imported by the use statement.
        /// Accepted shape examples:
        /// A\B\C, \A\B\C, A.
        /// Each namespace segment must follow [A-Za-z_][A-Za-z0-9_]*.
        /// </param>
        /// <param name="declaringNode">Optional AST node that declared this import.</param>
        /// <param name="sourceFile">Source filename for the declaration.</param>
        /// <param name="visibility">Visibility modifier applied to the imported symbol.</param>
        /// <param name="aliasName">
        /// Optional alias for the imported symbol.
        /// If supplied, must match [A-Za-z_][A-Za-z0-9_]*.
        /// </param>
        /// <param name="useType">Kind of use target.</param>
        public UseIncludeSymbol(
            string name,
            string importedName,
            IBase2Ast? declaringNode,
            string? sourceFile = null,
            MemberModifier visibility = MemberModifier.None,
            string? aliasName = null,
            PhpUseType useType = PhpUseType.Function
        )
            : base(
                NormalizeIdentifier(name, nameof(name), allowLeadingNamespaceSeparator: false),
                SymbolType.UseInclude,
                declaringNode,
                sourceFile: sourceFile ?? string.Empty,
                visibility
            )
        {
            this.ImportedName = NormalizeNamespaceName(importedName, nameof(importedName));
            this.ImportedNameSegments = string.IsNullOrEmpty(this.ImportedName)
                ? Array.Empty<string>()
                : this.ImportedName.TrimStart('\\').Split('\\');
            this.AliasName = NormalizeIdentifierOrNull(aliasName, nameof(aliasName), allowNullOrEmpty: true);
            this.UseType = useType;
        }

        private static string NormalizeNamespaceName(string importedName, string parameterName)
        {
            var normalizedValue = NormalizeIdentifierOrNull(importedName, parameterName, allowLeadingNamespaceSeparator: true);
            if (normalizedValue == null)
            {
                throw new ArgumentException($"Parameter '{parameterName}' is not a valid namespace-like name.", parameterName);
            }

            return normalizedValue;
        }

        private static string NormalizeIdentifier(string value, string parameterName, bool allowLeadingNamespaceSeparator = false)
        {
            var normalizedValue = NormalizeIdentifierOrNull(value, parameterName, allowLeadingNamespaceSeparator);
            if (normalizedValue == null)
            {
                throw new ArgumentException(
                    $"Parameter '{parameterName}' is not a valid identifier token: {value}",
                    parameterName
                );
            }

            return normalizedValue;
        }

        private static string? NormalizeIdentifierOrNull(
            string? value,
            string parameterName,
            bool allowLeadingNamespaceSeparator = false,
            bool allowNullOrEmpty = false
        )
        {
            if (value == null)
            {
                if (allowNullOrEmpty)
                {
                    return null;
                }

                throw new ArgumentException($"Parameter '{parameterName}' must not be null or whitespace.", parameterName);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Parameter '{parameterName}' must not be null or whitespace.", parameterName);
            }

            var normalized = value.Trim();

            if (allowLeadingNamespaceSeparator && normalized.StartsWith("\\", StringComparison.Ordinal))
            {
                normalized = normalized.TrimStart('\\');
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    throw new ArgumentException($"Parameter '{parameterName}' contains only namespace separators.", parameterName);
                }
            }
            else if (normalized.Length > 0 && normalized[0] == '\\')
            {
                throw new ArgumentException(
                    $"Parameter '{parameterName}' cannot begin with a namespace separator in this position.",
                    parameterName
                );
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException($"Parameter '{parameterName}' must not be null or whitespace.", parameterName);
            }

            var segments = normalized.Split('\\');
            if (!allowLeadingNamespaceSeparator && segments.Length > 1)
            {
                throw new ArgumentException(
                    $"Parameter '{parameterName}' must be a single identifier without namespace separators.",
                    parameterName
                );
            }

            foreach (var segment in segments)
            {
                if (segment.Length == 0 || !IsValidTokenSegment(segment))
                {
                    throw new ArgumentException(
                        $"Parameter '{parameterName}' contains malformed namespace token segment '{segment}'.",
                        parameterName
                    );
                }
            }

            return string.Join("\\", segments);
        }

        private static bool IsValidTokenSegment(string segment)
        {
            var start = segment[0];
            if (!(start == '_' || (start >= 'A' && start <= 'Z') || (start >= 'a' && start <= 'z')))
            {
                return false;
            }

            for (var segmentIndex = 1; segmentIndex < segment.Length; segmentIndex += 1)
            {
                var current = segment[segmentIndex];
                if (!(current == '_' ||
                    (current >= 'A' && current <= 'Z') ||
                    (current >= 'a' && current <= 'z') ||
                    (current >= '0' && current <= '9')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}