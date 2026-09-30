using System;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Binder
{
    /// <summary>
    /// Names that cannot be PHP free functions: PHP keywords and language constructs whose
    /// token consumes the identifier position before <c>function name(...)</c> can be parsed.
    /// File-level type aliases occupy the function namespace, so these names are rejected at
    /// bind. Class-level aliases emit as methods, which PHP allows even for some of these names.
    ///
    /// This is deliberately narrower than PHP's "other reserved words" list (<c>self</c>,
    /// <c>parent</c>, <c>int</c>, <c>string</c>, <c>true</c>, <c>null</c>, <c>readonly</c>,
    /// <c>enum</c>, …) — that list only restricts *class/interface/trait/enum* names, not
    /// function names. All names below were verified against a real PHP CLI: every keyword in
    /// this set fails to parse as <c>function &lt;name&gt;() {}</c>; every name deliberately left
    /// out of this set parses and runs fine as a free function.
    /// </summary>
    internal static class PhpReservedFunctionNames
    {
        private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
        {
            "if", "else", "elseif", "endif", "for", "foreach", "endfor", "endforeach",
            "while", "endwhile", "do", "switch", "endswitch", "match", "case", "default",
            "break", "continue", "try", "catch", "finally", "throw", "return", "yield",
            "function", "fn", "class", "interface", "trait",
            "namespace", "use", "const", "new", "clone",
            "public", "protected", "private", "static", "abstract", "final",
            "extends", "implements", "instanceof", "as",
            "echo", "print", "isset", "empty", "unset",
            "include", "require", "include_once", "require_once",
            "global", "var", "declare", "enddeclare", "goto", "and", "or", "xor",
            // `array` / `callable` became fully reserved (unusable as a function name) as of
            // PHP 8.5; blocked unconditionally since Tyhp does not gate this check by target
            // PHP version.
            "array", "callable",
            "list", "die", "exit", "__halt_compiler", "eval",
        };

        public static bool CannotBePhpFunction(string? name)
            => !string.IsNullOrEmpty(name) && Names.Contains(name);
    }
}
