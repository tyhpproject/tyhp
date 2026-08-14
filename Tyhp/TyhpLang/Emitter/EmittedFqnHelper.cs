using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Maps binder fully-qualified names (source namespaces) to emitted PHP FQNs, applying
    /// <c>output.namespacePrefix</c> for project-owned declarations.
    /// </summary>
    internal static class EmittedFqnHelper
    {
        /// <summary>
        /// Root-anchors <paramref name="binderFqn"/> and, when appropriate, prepends
        /// <paramref name="namespacePrefix"/> (e.g. <c>TestEmitter\Animal</c>
        /// → <c>\TyhpDebug\TestEmitter\Animal</c>).
        /// </summary>
        /// <remarks>
        /// When <paramref name="symbol"/> is provided, the prefix is applied only for
        /// project-emitted declarations (not tyhpdef / external symbols), so runtime types
        /// like <c>\Tyhp\Promise</c> stay intact. When <paramref name="symbol"/> is null, the
        /// caller is asserting a project-owned name (e.g. operator-overload targets) and the
        /// prefix is applied whenever configured.
        /// </remarks>
        public static string Format(
            string? binderFqn,
            string? namespacePrefix,
            IBaseSymbol? symbol = null,
            string? fallbackName = null)
        {
            var name = !string.IsNullOrWhiteSpace(binderFqn)
                ? binderFqn!
                : (fallbackName ?? "");
            name = name.Trim().TrimStart('\\');

            var applyPrefix = symbol is null
                ? !string.IsNullOrWhiteSpace(namespacePrefix)
                : ShouldApplyNamespacePrefix(symbol, namespacePrefix);

            if (applyPrefix
                && !string.Equals(name, namespacePrefix!.Trim().TrimStart('\\'), StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith(namespacePrefix.Trim().TrimStart('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = namespacePrefix.Trim().TrimStart('\\');
                name = string.IsNullOrWhiteSpace(name) ? prefix : prefix + "\\" + name;
            }

            return string.IsNullOrWhiteSpace(name) ? "\\" : "\\" + name;
        }

        /// <summary>
        /// True when <paramref name="symbol"/> is a user declaration this compile emits (a
        /// project <c>.tyhp</c> source), so its binder FQN should receive the output namespace
        /// prefix. Tyhpdef stubs and package-contributed sources keep their published FQN.
        /// </summary>
        public static bool ShouldApplyNamespacePrefix(IBaseSymbol? symbol, string? namespacePrefix)
        {
            if (string.IsNullOrWhiteSpace(namespacePrefix) || symbol is null)
            {
                return false;
            }

            // Only concrete object types get their declaring namespace rewritten in output.
            if (symbol is not ObjectDeclarationSymbol { SourceFile: var sourceFile })
            {
                return false;
            }

            return !IsExternalDeclarationSource(sourceFile);
        }

        /// <summary>
        /// Sources that publish a stable FQN (tyhpdef stubs, embedded builtins, Composer /
        /// runtime packages) must not be rewritten under the consuming project's
        /// <c>output.namespacePrefix</c>.
        /// </summary>
        internal static bool IsExternalDeclarationSource(string? sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return true;
            }

            if (sourceFile.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || sourceFile.StartsWith("<tyhpdef:", StringComparison.OrdinalIgnoreCase)
                || sourceFile.StartsWith("<embedded>", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Normalize separators so the same checks work on Windows and Unix paths.
            var normalized = sourceFile.Replace('\\', '/');
            if (normalized.Contains("/runtime/packages/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Composer-vendored Tyhp packages contribute tyhp_src / tyhpdef under vendor/.
            if (normalized.Contains("/vendor/", StringComparison.OrdinalIgnoreCase)
                && (normalized.Contains("/tyhp_src/", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("/tyhpdef/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }
    }
}
